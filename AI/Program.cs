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

        static double RunCheck(Network net, List<double[]> inputs, List<double[]> outputs, int iteration, int fractionOfData)
        {
            var random = new Random(iteration);

            var factor = (double)fractionOfData / inputs.Count;

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

            var ability1Change = TimeSpan.Zero;
            var ability2Change = TimeSpan.Zero;
            var ability3Change = TimeSpan.Zero;
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

                var timeSinceMatchStart = TimeSpan.FromSeconds(fileReader.ReadDouble());

                if (timeSinceMatchStart == TimeSpan.Zero)
                {
                    ability1Change = ability2Change = ability3Change = TimeSpan.Zero;
                }

                List<double> otherInfo = [
                    fileReader.ReadBoolean()?1:0, // jump
                    fileReader.ReadBoolean()?1:0, // 1
                    fileReader.ReadBoolean()?1:0, // 2
                    fileReader.ReadBoolean()?1:0, // 3
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

                double[] output = [
                    ..otherInfo
                ];

                var timeSince1 = (timeSinceMatchStart - ability1Change).TotalSeconds;
                var timeSince2 = (timeSinceMatchStart - ability2Change).TotalSeconds;
                var timeSince3 = (timeSinceMatchStart - ability3Change).TotalSeconds;

                if (otherInfo[1] != previousOutputs[1]) ability1Change = timeSinceMatchStart;
                if (otherInfo[2] != previousOutputs[2]) ability2Change = timeSinceMatchStart;
                if (otherInfo[3] != previousOutputs[3]) ability3Change = timeSinceMatchStart;

                previousOutputs[1] = (previousOutputs[1] == 1)?timeSince1:-timeSince1;
                previousOutputs[2] = (previousOutputs[2] == 1)?timeSince2:-timeSince2;
                previousOutputs[3] = (previousOutputs[3] == 1)?timeSince3:-timeSince3;

                double[] input = [
                    ..vision,
                    ..(playerLocations.SelectMany<Vector2, double>(vec=>[vec.X,vec.Y])),
                    numPlayers,
                    ..previousOutputs
                ];

                previousOutputs = [..output];

                inputs.Add(input);
                outputs.Add(output);
                Console.Write($"Loaded {((double)file.Position / file.Length * 100).ToString("F", CultureInfo.InvariantCulture)}% of data\r");
            }

            Console.WriteLine($"Loaded {inputs.Count} frames of data");


            var timer = Stopwatch.StartNew();
            var generationSize = 64;
            var randomize = .01;
            var i = 0;
            var fractionOfData = 384;

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
                    var newAccuracy = RunCheck(net, inputs, outputs, i, fractionOfData);
                    if (!(newAccuracy >= bestAccuracy))
                    {
                        bestAccuracy = newAccuracy;
                        neuralNetwork = net;
                    }
                }
                accuracies.Add(bestAccuracy);
                if (accuracies.Count > 16) accuracies.RemoveAt(0);
                var avgAccuracy = accuracies.Average();
                Console.Write($"Iteration: {i} " +
                    $"| Generation Size: {generationSize} " +
                    $"| Accuracy: {avgAccuracy.ToString("F", CultureInfo.InvariantCulture)} " +
                    $"({(100 - avgAccuracy * 50).ToString("F", CultureInfo.InvariantCulture)}%)\r");
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
            ability1down = ability2down = ability3down = TimeSpan.Zero;
            while (!score.HasValue)
            {
                score = RunAI(net, inReader, outWriter);
            }
            return score.Value;
        }

        public static TimeSpan ability1down;
        public static TimeSpan ability2down;
        public static TimeSpan ability3down;

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

                var timeSinceMatchStart = TimeSpan.FromSeconds(inReader.ReadDouble());

                var previousMoveVector = overrides.GetVectorFromMovement();

                var timeSinceAbility1 = (timeSinceMatchStart - ability1down).TotalSeconds;
                var timeSinceAbility2 = (timeSinceMatchStart - ability2down).TotalSeconds;
                var timeSinceAbility3 = (timeSinceMatchStart - ability3down).TotalSeconds;

                var outputs = net.Evaluate(
                    [..vision,
                    ..(playerLocations.SelectMany<Vector2, double>(vec=>[vec.X,vec.Y])),
                    numPlayers,
                    overrides.jumpDown?1:0,
                    overrides.firstDown?timeSinceAbility1:-timeSinceAbility1,
                    overrides.secondDown?timeSinceAbility2:-timeSinceAbility2,
                    overrides.thirdDown?timeSinceAbility3:-timeSinceAbility3,
                    overrides.joystickAngle.X, overrides.joystickAngle.Y,
                    previousMoveVector.X, previousMoveVector.Y
                    ]);

                var oldFirstDown = overrides.firstDown;
                var oldSecondDown = overrides.secondDown;
                var oldThirdDown = overrides.thirdDown;

                overrides.jumpDown = outputs[0] > .5;
                overrides.firstDown = outputs[1] > .5;
                overrides.secondDown = outputs[2] > .5;
                overrides.thirdDown = outputs[3] > .5;

                if (oldFirstDown != overrides.firstDown) ability1down = timeSinceMatchStart;
                if (oldSecondDown != overrides.secondDown) ability2down = timeSinceMatchStart;
                if (oldThirdDown != overrides.thirdDown) ability3down = timeSinceMatchStart;

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
