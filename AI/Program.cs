using System;
using System.IO;
using System.IO.Pipes;
using System.Numerics;

namespace AI
{
    internal class Program
    {
        static readonly Random random = new Random();
        static void Main(string[] args)
        {
            using (var pipe =
                new NamedPipeClientStream(args[0]))
            {
                using (var inReader = new BinaryReader(pipe))
                {
                    using (var outWriter = new BinaryWriter(pipe))
                    {
                        pipe.Connect();
                        while (pipe.IsConnected)
                        {
                            inReader.ReadBoolean();
                            RunAI().Transmit(outWriter);
                        }
                    }
                }
            }
        }

        static InputOverrides overrides;
        static InputOverrides RunAI()
        {
            if (random.NextDouble() > .95)
            {
                overrides.SetMovementFromVector(new Vector2((float)random.NextDouble(), (float)random.NextDouble()));
            }
            if (random.NextDouble() > .975)
            {
                overrides.joystickAngle = Vector2.Normalize(new Vector2(2 * (float)random.NextDouble() - 1, 2 * (float)random.NextDouble() - 1));
            }
            overrides.jumpDown = random.NextDouble() > .95;

            if (random.NextDouble() > .95)
            {
                var rand = random.Next(4);
                overrides.firstDown = rand == 0;
                overrides.secondDown = rand == 1;
                overrides.thirdDown = rand == 2;
            }
            return overrides;
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
