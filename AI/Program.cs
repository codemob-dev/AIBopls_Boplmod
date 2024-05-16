using System;
using System.Collections.Generic;
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
                            RunAI(inReader, outWriter);
                        }
                    }
                }
            }
        }

        static InputOverrides overrides;
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

            var AIPlayerLocation = playerLocations[0];
            playerLocations.RemoveAt(0);

            Vector2 closest = AIPlayerLocation;
            float closestDistance = float.MaxValue;
            foreach (var location in playerLocations)
            {
                if (Vector2.DistanceSquared(location, AIPlayerLocation) <= closestDistance)
                {
                    closest = location;
                }
            }

            var angleTo = Vector2.Normalize(closest - AIPlayerLocation);
            var randomizedAngle = Vector2.Normalize(angleTo + new Vector2(2 * (float)random.NextDouble() - 1, 2 * (float)random.NextDouble() - 1));

            if (random.NextDouble() > .95)
            {
                overrides.SetMovementFromVector(randomizedAngle);
            }
            if (random.NextDouble() > .95)
            {
                overrides.joystickAngle = randomizedAngle;
            }
            overrides.jumpDown = random.NextDouble() > .95;

            if (random.NextDouble() > .95)
            {
                var rand = random.Next(4);
                overrides.firstDown = rand == 0;
                overrides.secondDown = rand == 1;
                overrides.thirdDown = rand == 2;
            }
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
