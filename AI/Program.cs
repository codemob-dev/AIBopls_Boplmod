using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Numerics;

namespace AI
{
    internal class Program
    {
        static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                using var pipe = new NamedPipeClientStream(args[0]);
                using var inReader = new BinaryReader(pipe);
                using var outWriter = new BinaryWriter(pipe);

                SetupAI();

                pipe.Connect();
                while (pipe.IsConnected)
                {
                    RunAI(inReader, outWriter);
                }
            }
            else
            {
                var timer = Stopwatch.StartNew();
                var generationSize = 80;
                var randomizeBase = 8;
                var randomizeMultiplier = 16;

                var net = new Network();
                net.AddLayer(new InputLayer(typeof(IdentityNode), 2));
                net.AddLayer(new HiddenLayer(typeof(TanhNode), 12, net.PreviousLayerSize));
                net.AddLayer(new HiddenLayer(typeof(TanhNode), 12, net.PreviousLayerSize));
                net.AddLayer(new HiddenLayer(typeof(TanhNode), 12, net.PreviousLayerSize));
                net.AddLayer(new OutputLayer([typeof(SigmoidNode)], net.PreviousLayerSize));

                List<double[]> inputs = [];
                List<double[]> outputs = [];

                for (double m = 1; m <= 2; m += 0.5)
                {
                    for (double n = 1; n <= 2; n += 0.5)
                    {
                        inputs.Add([m, n]);
                        outputs.Add([m * n]);
                    }
                }

                var i = 0;

                var previousNet = net;
                var accuracy = RunCheck(net, inputs, outputs, logOutput: true);
                while (accuracy > 0.00001)
                {
                    var newNets = Enumerable.Range(0, generationSize)
                        .Select(x => net.Randomize((accuracy - 1 + randomizeBase) * randomizeMultiplier));

                    foreach (var newNet in newNets)
                    {
                        var newAccuracy = RunCheck(newNet, inputs, outputs);
                        if (newAccuracy < accuracy)
                        {
                            accuracy = newAccuracy;
                            net = newNet;
                        }
                    }
                    if (net != previousNet)
                    {
                        Console.WriteLine($"\n\nIteration: {i} | Generation Size: {generationSize}");
                        RunCheck(net, inputs, outputs, logOutput: true);
                    }
                    previousNet = net;
                    i++;
                }

                timer.Stop();
                Console.WriteLine($"Completed in {timer.ElapsedMilliseconds}ms");
            }
        }

        static double RunCheck(Network net, List<double[]> inputs, List<double[]> outputs, bool logOutput = false)
        {
            List<double> errors = [];
            for (int i = 0; i < inputs.Count; i++)
            {
                double[] input = inputs[i];
                double[] output = outputs[i];
                double[] evaluatedOutput = [.. net.Evaluate([.. input]).Select(x => Math.Round(x, 6))];
                if (logOutput) Console.WriteLine(
                    $"[{string.Join(", ", input)}] => " +
                    $"[{string.Join(", ", evaluatedOutput)}] " +
                    $"(expected [{string.Join(", ", output)}])");
                for (int j = 0; j < output.Length; j++)
                {
                    errors.Add(Math.Abs(output[j] - evaluatedOutput[j]));
                }
            }
            double accuracy = errors.Average();
            if (logOutput) Console.WriteLine($"Accuracy: {accuracy}");
            return accuracy;
        }

        const int rays = 16;

        static void SetupAI()
        {
            neuralNetwork = new Network();
            neuralNetwork.AddLayer(new InputLayer(typeof(IdentityNode),
                rays // Vision
                + 8 // Player Locations
                + 1 // Number of players
                ));
            neuralNetwork.AddLayer(new HiddenLayer(typeof(TanhNode), 32,
                neuralNetwork.PreviousLayerSize));
            neuralNetwork.AddLayer(new HiddenLayer(typeof(TanhNode), 32,
                neuralNetwork.PreviousLayerSize));
            neuralNetwork.AddLayer(new HiddenLayer(typeof(TanhNode), 32,
                neuralNetwork.PreviousLayerSize));
            neuralNetwork.AddLayer(new HiddenLayer(typeof(TanhNode), 32,
                neuralNetwork.PreviousLayerSize));
            neuralNetwork.AddLayer(new OutputLayer(
                [
                typeof(SigmoidNode), // jump
                typeof(SigmoidNode), typeof(SigmoidNode), typeof(SigmoidNode), // abilities
                typeof(TanhNode), typeof(TanhNode), // aiming
                typeof(TanhNode), typeof(TanhNode) // movement
                ],
                neuralNetwork.PreviousLayerSize));
        }

        static InputOverrides overrides;
        static Network neuralNetwork;
        static void RunAI(BinaryReader inReader, BinaryWriter outWriter)
        {

            var playerLocations = new List<Vector2>(inReader.ReadInt32());
            while (playerLocations.Count < playerLocations.Capacity)
            {
                playerLocations.Add(new Vector2(
                    inReader.ReadSingle(),
                    inReader.ReadSingle()
                ));
            }

            var numPlayers = playerLocations.Count;
            playerLocations.AddRange(Enumerable.Repeat(Vector2.Zero, 4 - numPlayers));

            var vision = new List<double>(inReader.ReadInt32());
            while (vision.Count < vision.Capacity)
            {
                vision.Add(inReader.ReadDouble());
            }

            var AIPlayerLocation = playerLocations[0];
            playerLocations.RemoveAt(0);

            var outputs = neuralNetwork.Evaluate(
                [..vision,
                AIPlayerLocation.X, AIPlayerLocation.Y,
                ..(playerLocations.SelectMany<Vector2, double>(vec=>[vec.X,vec.Y])),
                numPlayers
                ]);

            overrides.jumpDown = outputs[0] > .5;
            overrides.firstDown = outputs[1] > .5;
            overrides.secondDown = outputs[2] > .5;
            overrides.thirdDown = outputs[3] > .5;

            overrides.joystickAngle = new Vector2((float)outputs[4], (float)outputs[5]);
            overrides.SetMovementFromVector(new Vector2((float)outputs[6], (float)outputs[7]));


            overrides.Transmit(outWriter);
        }
    }

    public struct InputOverrides
    {
        public bool startDown;
        public bool selectDown;
        public bool jumpDown;
        public bool firstDown;
        public bool secondDown;
        public bool thirdDown;
        public Vector2 joystickAngle;
        public bool w;
        public bool a;
        public bool s;
        public bool d;

        public void SetMovementFromVector(Vector2 vector)
        {
            vector = Vector2.Normalize(vector);
            w = vector.Y > .5;
            a = vector.X < -.5;
            s = vector.Y < -.5;
            d = vector.X > .5;
        }

        public readonly void Transmit(BinaryWriter writer)
        {
            writer.Write(startDown);
            writer.Write(selectDown);
            writer.Write(jumpDown);
            writer.Write(firstDown);
            writer.Write(secondDown);
            writer.Write(thirdDown);
            writer.Write(joystickAngle.X);
            writer.Write(joystickAngle.Y);
            writer.Write(w);
            writer.Write(a);
            writer.Write(s);
            writer.Write(d);
        }
    }
}
