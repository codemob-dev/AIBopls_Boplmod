using BepInEx;
using HarmonyLib;
using System;

namespace AIBopls
{
    [BepInPlugin("com.codemob.aibopls", "AI Bopls", "1.0.0")]
    public class AIBopls : BaseUnityPlugin
    {
        public Harmony harmony;
        public InputOverrides inputOverrides = new InputOverrides();
        private void Awake()
        {
            harmony = new Harmony(Info.Metadata.GUID);
            harmony.PatchAll(typeof(AIBopls));
        }

        [HarmonyPatch(typeof(Player), nameof(Player.ForceSetInputProfile))]
        [HarmonyPrefix]
        public void Player_ForceSetInputProfile(ref Player __instance,
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
            if (GameLobby.isPlayingAReplay) return;
            if ((GameLobby.isOnlineGame && __instance.IsLocalPlayer) 
                || (!GameLobby.isOnlineGame && __instance.Id == 0))
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
        }
    }
}