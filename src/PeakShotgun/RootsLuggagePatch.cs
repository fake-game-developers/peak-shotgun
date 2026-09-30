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

/// <summary>
/// Sets which <see cref="Spawner"/> is currently rolling loot so the shotgun inject
/// can target specific luggage instances.
/// </summary>
[HarmonyPatch(typeof(Spawner), nameof(Spawner.GetObjectsToSpawn))]
internal static class SpawnerLootContextPatch
{
    [HarmonyPrefix]
    private static void Begin(Spawner __instance) => RootsLuggagePatch.BeginSpawn(__instance);

    [HarmonyPostfix]
    private static void End() => RootsLuggagePatch.EndSpawn();
}

/// <summary>
/// Roots: force <see cref="Plugin.GuaranteedLuggageShotguns"/> random suitcases.
/// Every biome: hard-cap how many luggage can yield a shotgun this run.
/// </summary>
[HarmonyPatch(typeof(LootData), nameof(LootData.GetRandomItems))]
internal static class RootsLuggagePatch
{
    private static Spawner? currentSpawner;
    private static HashSet<int>? chosenLuggageIds;
    private static bool assignedThisRun;

    /// <summary>How many luggage in each biome pool have already received a shotgun this run.</summary>
    private static readonly Dictionary<SpawnPool, int> spawnedPerBiome = new();

    internal static void ResetGuarantees()
    {
        assignedThisRun = false;
        chosenLuggageIds = null;
        currentSpawner = null;
        spawnedPerBiome.Clear();
    }

    internal static void BeginSpawn(Spawner spawner) => currentSpawner = spawner;

    internal static void EndSpawn() => currentSpawner = null;

    [HarmonyPostfix]
    private static void AfterLootRoll(SpawnPool spawnPool, ref List<GameObject>? __result)
    {
        TryGuaranteeRootsShotgun(spawnPool, ref __result);
        EnforcePerBiomeCap(spawnPool, ref __result);
    }

    private static void TryGuaranteeRootsShotgun(SpawnPool spawnPool, ref List<GameObject>? __result)
    {
        // Forced shotguns are always Roots-only, regardless of CanSpawnOnAnyBiome.
        if ((spawnPool & SpawnPool.LuggageRoots) == 0 || currentSpawner is not Luggage luggage)
        {
            return;
        }

        EnsureRandomLuggagePicked(luggage);
        if (chosenLuggageIds == null || !chosenLuggageIds.Contains(luggage.GetInstanceID()))
        {
            return;
        }

        GameObject? shotgun = Plugin.ShotgunPrefab;
        if (shotgun == null)
        {
            return;
        }

        if (__result == null)
        {
            __result = new List<GameObject>();
        }

        for (int i = 0; i < __result.Count; i++)
        {
            if (__result[i] == shotgun)
            {
                return;
            }
        }

        __result.Insert(0, shotgun);
        Plugin.Log.LogInfo(
            $"Forced shotgun into random Roots luggage '{luggage.name}' "
            + $"({chosenLuggageIds.Count} marked this run).");
    }

    /// <summary>
    /// After loot (and Roots guarantees) are decided, strip extra shotguns so each luggage
    /// biome yields at most <see cref="Plugin.MaxShotgunsPerBiome"/> this run.
    /// </summary>
    private static void EnforcePerBiomeCap(SpawnPool spawnPool, ref List<GameObject>? __result)
    {
        GameObject? shotgun = Plugin.ShotgunPrefab;
        if (__result == null || shotgun == null || currentSpawner is not Luggage)
        {
            return;
        }

        int shotgunSlots = 0;
        for (int i = 0; i < __result.Count; i++)
        {
            if (__result[i] == shotgun)
            {
                shotgunSlots++;
            }
        }

        if (shotgunSlots == 0)
        {
            return;
        }

        SpawnPool biome = LuggageBiomeKey(currentSpawner, spawnPool);
        spawnedPerBiome.TryGetValue(biome, out int already);
        int allowed = Mathf.Max(0, Plugin.MaxShotgunsPerBiome - already);
        if (allowed <= 0)
        {
            RemoveAllShotguns(__result, shotgun);
            Plugin.Log.LogInfo(
                $"Blocked shotgun in '{currentSpawner.name}' — biome {biome} already hit cap "
                + $"{Plugin.MaxShotgunsPerBiome} this run.");
            return;
        }

        int kept = 0;
        for (int i = 0; i < __result.Count;)
        {
            if (__result[i] != shotgun)
            {
                i++;
                continue;
            }

            if (kept < allowed)
            {
                kept++;
                i++;
                continue;
            }

            __result.RemoveAt(i);
        }

        if (kept > 0)
        {
            spawnedPerBiome[biome] = already + kept;
            Plugin.Log.LogInfo(
                $"Luggage '{currentSpawner.name}' biome {biome}: +{kept} shotgun "
                + $"({spawnedPerBiome[biome]}/{Plugin.MaxShotgunsPerBiome} this run).");
        }
    }

    private static void RemoveAllShotguns(List<GameObject> result, GameObject shotgun)
    {
        for (int i = result.Count - 1; i >= 0; i--)
        {
            if (result[i] == shotgun)
            {
                result.RemoveAt(i);
            }
        }
    }

    private static SpawnPool LuggageBiomeKey(Spawner spawner, SpawnPool rolledPool)
    {
        SpawnPool pool;
        try
        {
            pool = spawner.GetSpawnPool();
        }
        catch
        {
            pool = spawner.spawnPool;
        }

        SpawnPool luggage = pool & Plugin.AllLuggagePools;
        if (luggage == SpawnPool.None)
        {
            luggage = rolledPool & Plugin.AllLuggagePools;
        }

        return luggage == SpawnPool.None ? rolledPool : luggage;
    }

    /// <summary>
    /// Once per run: among Roots luggage still closed (plus the one opening now), pick
    /// <see cref="Plugin.GuaranteedLuggageShotguns"/> at random anywhere on the map.
    /// </summary>
    private static void EnsureRandomLuggagePicked(Luggage opening)
    {
        if (assignedThisRun)
        {
            return;
        }

        assignedThisRun = true;
        var candidates = new List<Luggage>();
        foreach (Luggage luggage in Luggage.ALL_LUGGAGE)
        {
            if (UsesRootsPool(luggage))
            {
                candidates.Add(luggage);
            }
        }

        if (UsesRootsPool(opening) && !candidates.Contains(opening))
        {
            candidates.Add(opening);
        }

        chosenLuggageIds = new HashSet<int>();
        if (candidates.Count == 0)
        {
            Plugin.Log.LogWarning("No Roots luggage found to mark for shotgun spawns.");
            return;
        }

        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        int take = Mathf.Min(Plugin.GuaranteedLuggageShotguns, candidates.Count);
        for (int i = 0; i < take; i++)
        {
            chosenLuggageIds.Add(candidates[i].GetInstanceID());
        }

        Plugin.Log.LogInfo(
            $"Marked {take} random Roots luggage for a shotgun (of {candidates.Count} candidates).");
    }

    private static bool UsesRootsPool(Spawner spawner)
    {
        try
        {
            return (spawner.GetSpawnPool() & SpawnPool.LuggageRoots) != 0;
        }
        catch
        {
            return (spawner.spawnPool & SpawnPool.LuggageRoots) != 0;
        }
    }
}
