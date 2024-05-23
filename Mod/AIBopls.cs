using BepInEx;
using BoplFixedMath;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;


namespace AIBopls
{
    [BepInPlugin("com.codemob.aibopls", "AI Bopls", "1.0.0")]
    public class AIBopls : BaseUnityPlugin
    {
        public Harmony harmony;
        public static InputOverrides inputOverrides = new InputOverrides();
        public static Communicator communicator;
        public static AIBopls instance;
        public static FileStream file;
        public static BinaryWriter fileWriter;

        const string REPLAYS_FOLDER = "replays_to_load";
        const string RECORDED_INPUTS_FILE = "recorded_inputs";

        const bool RECORD_INPUTS = false;

        private void Awake()
        {
            harmony = new Harmony(Info.Metadata.GUID);
            harmony.PatchAll(typeof(AIBopls));

            instance = this;
            if (RECORD_INPUTS)
            {
                file = File.OpenWrite(Path.ChangeExtension(RECORDED_INPUTS_FILE, ".bpltrain"));
                fileWriter = new BinaryWriter(file);
            }
            else
            {
                communicator = new Communicator();
                SceneManager.sceneLoaded += SceneManager_sceneLoaded;

                var func = typeof(CharacterSelectHandler_online)
                    .GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
                var patch = GetType().GetMethod(nameof(CharacterSelectHandler_online_Update));
                harmony.Patch(func, postfix: new HarmonyMethod(patch));
            }

            LoadAvailableReplays();
        }

        [HarmonyPatch(typeof(GameSessionHandler), nameof(GameSessionHandler.LeaveGame))]
        [HarmonyPostfix]
        public static void GameSessionHandler_Leave()
        {
            var playButtons = FindObjectsOfType<PlayButton>();
            foreach (var playButton in playButtons)
            {
                if (playButton.startOnlineInstead)
                {
                    playButton.Click();
                }
            }
        }

        static DateTime? firstConnection = null;
        
        public static void CharacterSelectHandler_online_Update(
            CharacterSelectHandler_online __instance)
        {
            if (!firstConnection.HasValue)
            {
                if (__instance.networkPlayerBoxes.Length > 0 
                    && __instance.networkPlayerBoxes[0].connectedPlayer.Connected)
                {
                    firstConnection = DateTime.Now;
                }
            } else
            {
                if (__instance.networkPlayerBoxes.Length == 0
                    || !__instance.networkPlayerBoxes[0].connectedPlayer.Connected)
                {
                    firstConnection = null;
                }
                if (DateTime.Now - firstConnection.Value > TimeSpan.FromSeconds(5) 
                    && SteamManager.LocalPlayerIsLobbyOwner)
                {
                    if (SteamManager.currentlyLookingForPlayers)
                    {
                        __instance.ClickFindButton();
                    }
                    
                    if (CharacterSelectHandler_online.startButtonAvailable)
                    {
                        __instance.ClickStartButton();
                    }
                }

                if (__instance.characterSelectBox.menuState == CharSelectMenu.select)
                {
                    __instance.characterSelectBox.OnEnterReady();
                }
            }
        }

        private void SceneManager_sceneLoaded(Scene scene, LoadSceneMode _)
        {
            Logger.LogInfo($"Loaded scene '{scene.name}'!");
            switch (scene.name)
            {
                case "CharacterSelect":
                    {
                        var characterSelectHandler = FindObjectOfType<CharacterSelectHandler>();
                        var firstSlot = characterSelectHandler.characterSelectBoxes[0];
                        switch (firstSlot.menuState)
                        {
                            case CharSelectMenu.join:
                                firstSlot.joinColor.GetComponent<CharSelectClickToJoin>().OnPointerClick(null);
                                firstSlot.selectables[2].Select(3);
                                firstSlot.OnEnterReady();
                                break;
                            case CharSelectMenu.select:
                                firstSlot.OnEnterReady();
                                break;
                        }

                        break;
                    }

                case "ChSelect_online":
                    {
                        firstConnection = null;
                        var characterSelectHandler = FindObjectOfType<CharacterSelectHandler_online>();
                        var firstSlot = characterSelectHandler.characterSelectBox;
                        switch (firstSlot.menuState)
                        {
                            case CharSelectMenu.join:
                                firstSlot.joinColor.GetComponent<CharSelectClickToJoin>().OnPointerClick(null);
                                firstSlot.selectables[2].Move(false);
                                firstSlot.OnEnterReady();
                                break;
                            case CharSelectMenu.select:
                                firstSlot.OnEnterReady();
                                break;
                        }

                        if (!SteamManager.currentlyLookingForPlayers)
                        {
                            characterSelectHandler.ClickFindButton();
                        }

                        break;
                    }
            }
        }

        public static void LoadAvailableReplays()
        {
            if (RECORD_INPUTS)
            {
                if (!Directory.Exists(REPLAYS_FOLDER))
                {
                    Directory.CreateDirectory(REPLAYS_FOLDER);
                }
                else
                {
                    var replays = Directory.EnumerateFiles(REPLAYS_FOLDER);
                    if (replays.Any())
                    {
                        var file = replays.First();
                        var host = FindObjectOfType<Host>();
                        Host.recordReplay = false;

                        var host_replay = typeof(Host).GetField(
                            "replay",
                            BindingFlags.NonPublic | BindingFlags.Instance);

                        var host_clients = typeof(Host).GetField(
                            "clients",
                            BindingFlags.NonPublic | BindingFlags.Instance);

                        host_replay.SetValue(
                            host,
                            NetworkTools.ReadCompressedReplay(
                                File.ReadAllBytes(file),
                                out StartRequestPacket startRequestPacket));

                        Host.replayPath = file;

                        SteamManager.startParameters = startRequestPacket;
                        GameLobby.isPlayingAReplay = true;
                        var clients = new List<Client>();
                        for (int j = 0; j < startRequestPacket.nrOfPlayers - 1; j++)
                        {
                            clients.Add(new Client(1, new SteamConnection()));
                        }
                        host_clients.SetValue(host, clients);

                        SceneManager.LoadScene("Replay");
                    }
                    else
                    {
                        SceneManager.LoadScene("MainMenu");
                    }
                }
            }
        }

        static bool gameStarted = false;

        [HarmonyPatch(typeof(GameSessionHandler), nameof(GameSessionHandler.UpdateSim))]
        [HarmonyPostfix]
        private static void ReplayUnloader()
        {
            if (GameLobby.isPlayingAReplay)
            {
                var host = FindObjectOfType<Host>();

                var host_replay = typeof(Host).GetField(
                    "replay",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var replay = host_replay.GetValue(host) as Queue<InputPacketQuad>;

                if (GameSessionHandler.HasGameEnded() || replay.Count == 0)
                {
                    if (gameStarted)
                    {
                        var replays = Directory.EnumerateFiles(REPLAYS_FOLDER);

                        File.Delete(replays.First());

                        gameStarted = false;
                        LoadAvailableReplays();
                    }
                }
                else if (!gameStarted)
                {
                    gameStarted = true;
                }
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.IsLocalPlayer), MethodType.Getter)]
        [HarmonyPostfix]
        public static void Player_IsLocalPlayer_get(Player __instance, ref bool __result)
        {
            var caller = new StackTrace().GetFrame(2);
            if (caller.GetMethod().Name == "SpawnPlayers" && IsAIPlayer(__instance) && !RECORD_INPUTS && !GameLobby.isPlayingAReplay)
            {
                __result = false;
            }
        }

        [HarmonyPatch(typeof(CharacterSelectBox), nameof(CharacterSelectBox.OnEnterSelect))]
        [HarmonyPrefix]
        public static void CharacterSelectBox_OnEnterSelect(CharacterSelectBox __instance)
        {
            if (__instance.RectangleIndex == 0 && !RECORD_INPUTS)
            {
                CharacterSelectBox.keyboardMouseIsOccupied = false;
            }
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
                if (RECORD_INPUTS || GameLobby.isPlayingAReplay)
                {
                    inputOverrides.startDown = startDown;
                    inputOverrides.selectDown = selectDown;
                    inputOverrides.jumpDown = jumpDown;
                    inputOverrides.firstDown = firstDown;
                    inputOverrides.secondDown = secondDown;
                    inputOverrides.thirdDown = thirdDown;
                    inputOverrides.joystickAngle = joystickAngle;
                    inputOverrides.w = w;
                    inputOverrides.a = a;
                    inputOverrides.s = s;
                    inputOverrides.d = d;
                }
                else
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
        }

        public static bool IsAIPlayer(Player player)
        {
            return (GameLobby.isOnlineGame && player.IsLocalPlayer)
                || (!GameLobby.isOnlineGame && player.Id == 1);
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Kill))]
        [HarmonyPrefix]
        public static void Player_Kill(Player __instance, int idOfKiller)
        {
            if (!IsAIPlayer(__instance)) return;

            HandleGameEnd(__instance, idOfKiller == __instance.Id);
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
                if (!RECORD_INPUTS && !GameLobby.isPlayingAReplay)
                {
                    player.ForceSetInputProfile(default,
                                                default,
                                                default,
                                                default,
                                                default,
                                                default,
                                                default,
                                                default,
                                                default,
                                                default,
                                                default);
                }
            }
        }

        public static double GetDistance(Player player, double angle)
        {
            var direction = new Vec2(Fix.Sin((Fix)angle), Fix.Cos((Fix)angle));

            var result = DetPhysics.Get().RaycastToClosest(
                player.Position + (direction * player.Scale * (Fix)2),
                direction,
                DetPhysics.Get().maxBeamDistance,
                DetPhysics.Get().beamHitMask);


            return result ? (double)result.nearDist : -1;
        }

        static DateTime? matchStartTime = null;
        public static void HandleGameEnd(Player player, bool killedSelf)
        {
            if (sentGameEnd || RECORD_INPUTS) return;
            sentGameEnd = true;
            communicator.outWriter.Write(false);
            var elapsedTime = DateTime.Now - matchStartTime.Value;

            double score = 75 * (player.Kills - player.KillsAtStartOfRound);

            if (player.WonThisRound)
            {
                score += 240 - elapsedTime.TotalSeconds;
            }
            else
            {
                score += .3 * elapsedTime.TotalSeconds;
            }
            if (killedSelf) score -= 75;

            communicator.outWriter.Write(score);

            matchStartTime = null;
        }


        public static bool sentGameEnd = false;
        public static void ExternalAI(Player player)
        {
            if (!player.stillAliveThisRound) return;

            const int rays = 16;
            var playerHandler = PlayerHandler.Get();
            var playerList = playerHandler.PlayerList();
            playerList.Remove(player);
            playerList.Insert(0, player);

            List<double> vision = new List<double>();

            for (int i = 0; i < rays; i++)
            {
                vision.Add(GetDistance(player, 2 * Math.PI * (i / rays)));
            }

            TimeSpan timeSinceMatchStart;
            if (!matchStartTime.HasValue)
            {
                matchStartTime = DateTime.Now;
                timeSinceMatchStart = TimeSpan.Zero;
            }
            else
            {
                timeSinceMatchStart = DateTime.Now - matchStartTime.Value;
            }


            if (RECORD_INPUTS)
            {
                if (player.WonThisRound) return;
                fileWriter.Write(playerList.Count);
                foreach (var p in playerList)
                {
                    fileWriter.Write((float)p.Position.x);
                    fileWriter.Write((float)p.Position.y);
                }
                fileWriter.Write(vision.Count);
                foreach (var dbl in vision)
                {
                    fileWriter.Write(dbl);
                }
                fileWriter.Write(timeSinceMatchStart.TotalSeconds);
                fileWriter.Write(inputOverrides.jumpDown);
                fileWriter.Write(inputOverrides.firstDown);
                fileWriter.Write(inputOverrides.secondDown);
                fileWriter.Write(inputOverrides.thirdDown);
                fileWriter.Write((float)inputOverrides.GetJoystickAngleVector().x);
                fileWriter.Write((float)inputOverrides.GetJoystickAngleVector().y);
                fileWriter.Write(inputOverrides.w);
                fileWriter.Write(inputOverrides.a);
                fileWriter.Write(inputOverrides.s);
                fileWriter.Write(inputOverrides.d);
                return;
            }

            if (GameLobby.isPlayingAReplay) return;

            if (player.WonThisRound)
            {
                HandleGameEnd(player, false);
                return;
            }

            sentGameEnd = false;

            communicator.outWriter.Write(true);
            communicator.outWriter.Write(playerList.Count);
            foreach (var p in playerList)
            {
                communicator.outWriter.Write((float)p.Position.x);
                communicator.outWriter.Write((float)p.Position.y);
            }

            communicator.outWriter.Write(vision.Count);
            foreach (var dbl in vision)
            {
                communicator.outWriter.Write(dbl);
            }
            communicator.outWriter.Write(timeSinceMatchStart.TotalSeconds);

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

            static readonly Vec2[] inputVectors = GenerateInputVectorTable();
            private static Vec2[] GenerateInputVectorTable()
            {
                var vectors = new Vec2[256];
                vectors[0] = Vec2.zero;
                for (int i = 1; i < vectors.Length; i++)
                {
                    int num = i - 1;
                    Fix x = Fix.PiTimes2 * ((Fix)num / (Fix)255f);
                    vectors[i] = new Vec2(Fix.Cos(x), Fix.Sin(x));
                }
                return vectors;
            }
            public Vec2 GetJoystickAngleVector()
            {
                return inputVectors[joystickAngle];
            }

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
                return (byte)(((int)(long)(Vec2.NormalizedVectorAngle((Vec2)vector) / Fix.PiTimes2 * (Fix)255L) % 255) + 1);
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
                var AIinterface = new System.Diagnostics.Process();
                AIinterface.StartInfo.FileName = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "AI.exe");

                var pipeGUID = Guid.NewGuid().ToString();
                pipe = new NamedPipeServerStream(pipeGUID);

                instance.Logger.LogInfo("Starting process...");

                AIinterface.StartInfo.Arguments = pipeGUID;
                AIinterface.StartInfo.EnvironmentVariables["TF_ENABLE_ONEDNN_OPTS"] = "0";
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