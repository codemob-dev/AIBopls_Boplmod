using PureHDF.Selections;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Numerics;
using static Tensorflow.Binding;

namespace AI
{
    internal class Program
    {
        static readonly Random random = new Random();
        static void Main(string[] args)
        {
            using var pipe = new NamedPipeClientStream(args[0]);
            using var inReader = new BinaryReader(pipe);
            using var outWriter = new BinaryWriter(pipe);

            pipe.Connect();
            while (pipe.IsConnected)
            {
                RunAI(inReader, outWriter);
            }
        }

        static InputOverrides overrides;
        static void RunAI(BinaryReader inReader, BinaryWriter outWriter)
        {
            var hello = tf.constant("Hello, TensorFlow!");
            Console.WriteLine(hello);

            var playerLocations = new List<Vector2>(inReader.ReadInt32());

            while (playerLocations.Count < playerLocations.Capacity)
            {
                playerLocations.Add(new Vector2(
                    inReader.ReadSingle(),
                    inReader.ReadSingle()
                ));
            }

            var AIPlayerLocation = playerLocations[0];
            playerLocations.RemoveAt(0);

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

        public void Transmit(BinaryWriter writer)
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
