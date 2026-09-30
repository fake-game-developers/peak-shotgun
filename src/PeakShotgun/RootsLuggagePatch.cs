using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Peak.Shotgun;

[HarmonyPatch(typeof(ItemDatabase), nameof(ItemDatabase.OnLoaded))]
internal static class ItemDatabasePatch
{
    [HarmonyPostfix]
    private static void CreateShotgun()
    {
        Plugin.Instance?.CreateFromBlowgun();
    }
}

[HarmonyPatch(typeof(LootData), nameof(LootData.GetRandomItems))]
internal static class RootsLuggagePatch
{
    [HarmonyPostfix]
    private static void GuaranteeShotgun(SpawnPool spawnPool, ref List<GameObject>? __result)
    {
        const SpawnPool testPools = SpawnPool.LuggageBeach | SpawnPool.LuggageRoots;
        GameObject? shotgun = Plugin.ShotgunPrefab;
        if (shotgun == null || (spawnPool & testPools) == 0 || __result == null || __result.Count == 0)
        {
            return;
        }

        __result[0] = shotgun;
    }
}
