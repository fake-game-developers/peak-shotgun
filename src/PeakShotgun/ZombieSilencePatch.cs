using System.Reflection;
using HarmonyLib;

namespace Peak.Shotgun;

[HarmonyPatch]
internal static class ZombieSilencePatch
{
    private static bool Prepare() => TargetMethod() != null;

    private static MethodBase? TargetMethod()
    {
        foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            System.Type? type = assembly.GetType("MushroomZombie");
            MethodBase? method = type?.GetMethod(
                "RPC_PlaySFX",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method != null)
            {
                return method;
            }
        }

        return null;
    }

    private static bool Prefix(UnityEngine.Component __instance)
    {
        GunCharacterLaunch? launch = __instance.GetComponent<GunCharacterLaunch>();
        return launch == null || !launch.Silenced;
    }
}
