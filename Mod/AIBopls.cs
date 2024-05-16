using BepInEx;
using BoplFixedMath;
using HarmonyLib;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;


namespace AIBopls
{
    [BepInPlugin("com.codemob.aibopls", "AI Bopls", "1.0.0")]
    public class AIBopls : BaseUnityPlugin
    {
        public Harmony harmony;
        public static InputOverrides inputOverrides = new InputOverrides();
        public static Communicator communicator;
        public static AIBopls instance;
        private void Awake()
        {
            harmony = new Harmony(Info.Metadata.GUID);
            harmony.PatchAll(typeof(AIBopls));
            instance = this;
            communicator = new Communicator();
        }

        [HarmonyPatch(typeof(Player), nameof(Player.ForceSetInputProfile))]
        [HarmonyPrefix]
        public static void Player_ForceSetInputProfile(ref Player __instance,
                                                ref bool startDown,
                                                ref bool selectDown,
                                                ref bool jumpDown,
                                                ref bool firstDown,
                                                ref bool secondDown,
                                                ref bool thirdDown,
                                                ref byte joystickAngle,
                                                ref bool w,
                                                ref bool a,
                                                ref bool s,
                                                ref bool d)
        {
            if (IsAIPlayer(__instance))
            {
                startDown = inputOverrides.startDown;
                selectDown = inputOverrides.selectDown;
                jumpDown = inputOverrides.jumpDown;
                firstDown = inputOverrides.firstDown;
                secondDown = inputOverrides.secondDown;
                thirdDown = inputOverrides.thirdDown;
                joystickAngle = inputOverrides.joystickAngle;
                w = inputOverrides.w;
                a = inputOverrides.a;
                s = inputOverrides.s;
                d = inputOverrides.d;
            }
        }

        public static bool IsAIPlayer(Player player)
        {
            return ((GameLobby.isOnlineGame && player.IsLocalPlayer)
                || (!GameLobby.isOnlineGame && player.Id == 2)) && !GameLobby.isPlayingAReplay;
        }

        [HarmonyPatch(typeof(CharacterSelectBox), nameof(CharacterSelectBox.OnEnterSelect))]
        [HarmonyPrefix]
        public static void CharacterSelectBox_OnEnterSelect()
        {
            CharacterSelectBox.keyboardMouseIsOccupied = false;
        }

        [HarmonyPatch(typeof(PlayerBody), nameof(PlayerBody.UpdateSim))]
        [HarmonyPrefix]
        public static void PlayerBody_UpdateSim(PlayerBody __instance)
        {
            var playerHandler = PlayerHandler.Get();
            Player player = playerHandler.GetPlayer(__instance.GetComponent<IPlayerIdHolder>().GetPlayerId());
            if (IsAIPlayer(player))
            {
                ExternalAI(player);
            }
        }

        public static void ExternalAI(Player player)
        {
            var playerHandler = PlayerHandler.Get();
            var playerList = playerHandler.PlayerList().ConvertAll(x => x);
            playerList.Remove(player);
            playerList.Insert(0, player);

            communicator.outWriter.Write(playerList.Count);
            foreach (var p in playerList)
            {
                communicator.outWriter.Write((float)p.Position.x);
                communicator.outWriter.Write((float)p.Position.y);
            }

            inputOverrides = InputOverrides.Receive(communicator.inReader);
        }

        public struct InputOverrides
        {
            public bool startDown;
            public bool selectDown;
            public bool jumpDown;
            public bool firstDown;
            public bool secondDown;
            public bool thirdDown;
            public byte joystickAngle;
            public bool w;
            public bool a;
            public bool s;
            public bool d;

            public void SetMovementFromVector(Vector2 vector)
            {
                vector.Normalize();
                w = vector.y > .5;
                a = vector.x < -.5;
                s = vector.y < -.5;
                d = vector.x > .5;
            }

            public void SetAimVector(Vector2 vector)
            {
                joystickAngle = CreateAimVector(vector);
            }
            public static byte CreateAimVector(Vector2 vector)
            {
                return (byte)((int)(long)(Vec2.NormalizedVectorAngle((Vec2)vector) / Fix.PiTimes2 * (Fix)255L) % 255 + 1);
            }
            public static InputOverrides Receive(BinaryReader reader)
            {
                return new InputOverrides
                {
                    startDown = reader.ReadBoolean(),
                    selectDown = reader.ReadBoolean(),
                    jumpDown = reader.ReadBoolean(),
                    firstDown = reader.ReadBoolean(),
                    secondDown = reader.ReadBoolean(),
                    thirdDown = reader.ReadBoolean(),
                    joystickAngle = CreateAimVector(new Vector2(
                        reader.ReadSingle(),
                        reader.ReadSingle())),
                    w = reader.ReadBoolean(),
                    a = reader.ReadBoolean(),
                    s = reader.ReadBoolean(),
                    d = reader.ReadBoolean()
                };
            }
        }

        public class Communicator
        {
            public NamedPipeServerStream pipe;
            public BinaryWriter outWriter;
            public BinaryReader inReader;

            public Communicator()
            {
                Process AIinterface = new Process();
                AIinterface.StartInfo.FileName = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "AI.exe");

                var pipeGUID = System.Guid.NewGuid().ToString();
                pipe = new NamedPipeServerStream(pipeGUID);

                instance.Logger.LogInfo("Starting process...");

                AIinterface.StartInfo.Arguments = pipeGUID;
                AIinterface.StartInfo.RedirectStandardOutput = true;
                AIinterface.StartInfo.RedirectStandardError = true;
                AIinterface.StartInfo.UseShellExecute = false;
                AIinterface.Start();

                HandleStdoutStderr(AIinterface.StandardOutput, AIinterface.StandardError);

                outWriter = new BinaryWriter(pipe);
                inReader = new BinaryReader(pipe);

                instance.Logger.LogInfo("Process started, waiting for connection");
                pipe.WaitForConnection();
                instance.Logger.LogInfo("Process connected!");
            }

            public void HandleStdoutStderr(StreamReader stdout, StreamReader stderr)
            {
                var AILogger = BepInEx.Logging.Logger.CreateLogSource("Bopl AI System");
                Task.Run(() =>
                {
                    string line;
                    while (true)
                    {
                        while ((line = stdout.ReadLine()) != null)
                            AILogger.LogInfo(line);
                        while ((line = stderr.ReadLine()) != null && line != string.Empty)
                            AILogger.LogError(line);
                    }
                });
            }
        }
    }
}