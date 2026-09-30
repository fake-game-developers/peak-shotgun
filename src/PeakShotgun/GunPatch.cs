using HarmonyLib;
using UnityEngine;

namespace PeakShotgun;

[HarmonyPatch(typeof(Character), "Awake")]
internal static class GunPatch
{
    [HarmonyPostfix]
    private static void AddLaunch(Character __instance)
    {
        __instance.gameObject.AddComponent<GunCharacterLaunch>();
    }
}
