using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace AI
{
    internal class Program
    {
        static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                ModConnection(args[0]);
            }
            else
            {
                try
                {
                    SetupAI(true);
                }
                finally
                {
                    neuralNetwork.Save(NETWORK_FILE);
                }
            }
        }

        const string NETWORK_FILE = "ai";
        static void ModConnection(string pipeName)
        {
            using var pipe = new NamedPipeClientStream(pipeName);
            using var inReader = new BinaryReader(pipe);
            using var outWriter = new BinaryWriter(pipe);
            pipe.Connect();
            try
            {
                SetupAI(false);

                while (pipe.IsConnected)
                {
                    var batch = neuralNetwork.GenRandomBatch(5, .08);
                    neuralNetwork = RunBatch(batch, inReader, outWriter);
                }
            }
            finally
            {
                neuralNetwork.Save(NETWORK_FILE);
            }
        }

        static double RunCheck(Network net, List<double[]> inputs, List<double[]> outputs, int iteration)
        {
            var random = new Random(iteration);

            var factor = 320d / inputs.Count;

            ConcurrentBag<double> errors = [];
            var countdown = new CountdownEvent(inputs.Count);
            for (int i = 0; i < inputs.Count; i++)
            {
                if (random.NextDouble() > factor || i == 0)
                {
                    countdown.Signal();
                    continue;
                }
                ThreadPool.QueueUserWorkItem(i =>
                {
                    double[] input = inputs[(int)i];
                    double[] output = outputs[(int)i];
                    double[] evaluatedOutput = [.. net.Evaluate([.. input])];
                    var localErrors = 0d;
                    for (int j = 0; j < output.Length; j++)
                    {
                        localErrors += Math.Abs(output[j] - evaluatedOutput[j]);
                    }
                    errors.Add(localErrors / output.Length);
                    countdown.Signal();
                }, i);
            }
            countdown.Wait();
            double accuracy = errors.Average();
            return accuracy;
        }

        const int rays = 16;

        static void SetupAI(bool train)
        {
            List<Type> outputTypes = [
                typeof(SigmoidNode), // jump
                typeof(SigmoidNode), typeof(SigmoidNode), typeof(SigmoidNode), // abilities
                typeof(TanhNode), typeof(TanhNode), // aiming
                typeof(TanhNode), typeof(TanhNode) // movement
            ];
            try
            {
                neuralNetwork = Network.Load(NETWORK_FILE);
            }
            catch (FileNotFoundException)
            {
                neuralNetwork = new Network();
                neuralNetwork.AddLayer(new InputLayer(typeof(IdentityNode),
                    rays // Vision
                    + 8 // Player Locations
                    + 1 // Number of players
                    + outputTypes.Count // Previous inputs
                    ));
                neuralNetwork.AddLayer(new HiddenLayer(typeof(TanhNode), 48,
                    neuralNetwork.PreviousLayerSize));
                neuralNetwork.AddLayer(new HiddenLayer(typeof(TanhNode), 48,
                    neuralNetwork.PreviousLayerSize));
                neuralNetwork.AddLayer(new HiddenLayer(typeof(TanhNode), 48,
                    neuralNetwork.PreviousLayerSize));
                neuralNetwork.AddLayer(new HiddenLayer(typeof(TanhNode), 48,
                    neuralNetwork.PreviousLayerSize));
                neuralNetwork.AddLayer(new HiddenLayer(typeof(TanhNode), 48,
                    neuralNetwork.PreviousLayerSize));
                neuralNetwork.AddLayer(new HiddenLayer(typeof(TanhNode), 48,
                    neuralNetwork.PreviousLayerSize));
                neuralNetwork.AddLayer(new OutputLayer(
                    outputTypes,
                    neuralNetwork.PreviousLayerSize));
            }
            if (!train) return;
            using var file = File.OpenRead("recordedInputs.bin");
            var fileReader = new BinaryReader(file);

            List<double[]> inputs = [];
            List<double[]> outputs = [];

            double[] previousOutputs = new double[outputTypes.Count];
            while (file.Length - file.Position > 1)
            {
                List<Vector2> playerLocations = [];
                var numPlayers = fileReader.ReadInt32();
                for (var j = 0; j < numPlayers; j++)
                {
                    playerLocations.Add(new Vector2(
                        fileReader.ReadSingle(),
                        fileReader.ReadSingle()
                        ));
                }
                playerLocations.AddRange(Enumerable.Repeat(Vector2.Zero, 4 - numPlayers));

                List<double> vision = [];
                var numVision = fileReader.ReadInt32();
                for (var j = 0; j < numVision; j++)
                {
                    vision.Add(fileReader.ReadDouble());
                }

                List<double> otherInfo = [
                    fileReader.ReadBoolean()?1:0,
                    fileReader.ReadBoolean()?1:0,
                    fileReader.ReadBoolean()?1:0,
                    fileReader.ReadBoolean()?1:0,
                    fileReader.ReadSingle(),
                    fileReader.ReadSingle()
                ];

                var moveVector = new InputOverrides
                {
                    w = fileReader.ReadBoolean(),
                    a = fileReader.ReadBoolean(),
                    s = fileReader.ReadBoolean(),
                    d = fileReader.ReadBoolean()
                }.GetVectorFromMovement();
                otherInfo.Add(moveVector.X);
                otherInfo.Add(moveVector.Y);
                double[] input = [
                    ..vision,
                    ..(playerLocations.SelectMany<Vector2, double>(vec=>[vec.X,vec.Y])),
                    numPlayers,
                    ..previousOutputs
                ];
                double[] output = [
                    ..otherInfo
                ];
                previousOutputs = output;
                inputs.Add(input);
                outputs.Add(output);
                Console.Write($"Loaded {((double)file.Position / file.Length * 100).ToString("F", CultureInfo.InvariantCulture)}% of data\r");
            }

            Console.WriteLine($"Loaded {inputs.Count} frames of data");


            var timer = Stopwatch.StartNew();
            var generationSize = 26;
            var randomize = .03;
            var i = 0;

            var running = true;

            new Thread(() => { Console.ReadKey(); running = false; }).Start();
            var autosaveThread = new Thread(() =>
            {
                while (running)
                {
                    neuralNetwork.Save(NETWORK_FILE);
                    try {
                        Thread.Sleep(1500);
                    } catch (ThreadInterruptedException)
                    {
                        break;
                    }
                }
            });
            autosaveThread.Start();

            double bestAccuracy;
            List<double> accuracies = [];
            while (running)
            {
                var nets = neuralNetwork.GenRandomBatch(generationSize, randomize);

                bestAccuracy = double.NaN;
                foreach (var net in nets)
                {
                    var newAccuracy = RunCheck(net, inputs, outputs, i);
                    if (!(newAccuracy >= bestAccuracy))
                    {
                        bestAccuracy = newAccuracy;
                        neuralNetwork = net;
                    }
                }
                accuracies.Add(bestAccuracy);
                if (accuracies.Count > 42) accuracies.RemoveAt(0);
                Console.Write($"Iteration: {i} " +
                    $"| Generation Size: {generationSize} " +
                    $"| Accuracy: {bestAccuracy.ToString("F", CultureInfo.InvariantCulture)} " +
                    $"| Averaged Accuracy: {accuracies.Average().ToString("F", CultureInfo.InvariantCulture)}\r");
                i++;
            }

            timer.Stop();
            autosaveThread.Interrupt();
            Console.WriteLine($"\n\nCompleted in {timer.ElapsedMilliseconds}ms");
        }

        static InputOverrides overrides;
        static Network neuralNetwork;

        static Network RunBatch(IEnumerable<Network> networks, BinaryReader inReader, BinaryWriter outWriter)
        {
            double bestScore = double.NaN;
            Network bestNetwork = null;

            foreach (var network in networks)
            {
                var networkScore = RunAIMatch(network, inReader, outWriter);
                if (!(networkScore < bestScore))
                {
                    bestNetwork = network;
                    bestScore = networkScore;
                }
            }
            return bestNetwork;
        }

        static double RunAIMatch(Network net, BinaryReader inReader, BinaryWriter outWriter)
        {
            double? score = null;
            while (!score.HasValue)
            {
                score = RunAI(net, inReader, outWriter);
            }
            return score.Value;
        }
        static double? RunAI(Network net, BinaryReader inReader, BinaryWriter outWriter)
        {
            if (inReader.ReadBoolean())
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

                var previousMoveVector = overrides.GetVectorFromMovement();

                var outputs = net.Evaluate(
                    [..vision,
                    ..(playerLocations.SelectMany<Vector2, double>(vec=>[vec.X,vec.Y])),
                    numPlayers,
                    overrides.jumpDown?1:0,
                    overrides.firstDown?1:0,
                    overrides.secondDown?1:0,
                    overrides.thirdDown?1:0,
                    overrides.joystickAngle.X, overrides.joystickAngle.Y,
                    previousMoveVector.X, previousMoveVector.Y
                    ]);

                overrides.jumpDown = outputs[0] > .5;
                overrides.firstDown = outputs[1] > .5;
                overrides.secondDown = outputs[2] > .5;
                overrides.thirdDown = outputs[3] > .5;

                overrides.joystickAngle = new Vector2((float)outputs[4], (float)outputs[5]);
                overrides.SetMovementFromVector(new Vector2((float)outputs[6], (float)outputs[7]));


                overrides.Transmit(outWriter);
                return null;
            }
            else
            {
                var score = inReader.ReadDouble();
                Console.WriteLine($"AI SCORE : {score.ToString("F", CultureInfo.InvariantCulture)}");
                return score;
            }
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

        public readonly Vector2 GetVectorFromMovement()
        {
            var vector = Vector2.Zero;
            if (w) vector.Y = 1;
            if (a) vector.X = -1;
            if (s) vector.Y = -1;
            if (d) vector.X = 1;
            return vector;
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
