using BepInEx;
using BoplFixedMath;
using HarmonyLib;
using UnityEngine;

namespace AIBopls
{
    [BepInPlugin("com.codemob.aibopls", "AI Bopls", "1.0.0")]
    public class AIBopls : BaseUnityPlugin
    {
        public Harmony harmony;
        public static InputOverrides inputOverrides = new InputOverrides();
        public static AIBopls instance;
        private void Awake()
        {
            harmony = new Harmony(Info.Metadata.GUID);
            harmony.PatchAll(typeof(AIBopls));
            instance = this;
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
            Player player = PlayerHandler.Get().GetPlayer(__instance.GetComponent<IPlayerIdHolder>().GetPlayerId());
            if (IsAIPlayer(player))
            {
                RandomAI(player);
            }
        }

        public static void RandomAI(Player player)
        {
            if (Random.Range(0f, 1f) > .95)
            {
                inputOverrides.SetMovementFromVector(Random.insideUnitCircle);
            }
            if (Random.Range(0f, 1f) > .975)
            {
                inputOverrides.SetAimVector(Random.insideUnitCircle);
            }
            inputOverrides.jumpDown = Random.Range(0f, 1f) > .95;

            if (Random.Range(0f, 1f) > .95)
            {
                var rand = Random.Range(0, 4);
                inputOverrides.firstDown = rand == 0;
                inputOverrides.secondDown = rand == 1;
                inputOverrides.thirdDown = rand == 2;
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
                joystickAngle = (byte)((int)(long)(Vec2.NormalizedVectorAngle((Vec2)vector) / Fix.PiTimes2 * (Fix)255L) % 255 + 1);
            }
        }
    }
}