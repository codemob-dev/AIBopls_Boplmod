using BepInEx;
using BoplFixedMath;
using HarmonyLib;
using System.Reflection;
using UnityEngine.Assertions;

namespace AIBopls
{
    [BepInPlugin("com.codemob.aibopls", "AI Bopls", "1.0.0")]
    public class AIBopls : BaseUnityPlugin
    {
        public Harmony harmony;
        private void Awake()
        {
            harmony = new Harmony(Info.Metadata.GUID);
            harmony.PatchAll(typeof(AIBopls));
        }
    }
}
