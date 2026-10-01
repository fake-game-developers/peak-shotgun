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
/// Makes the shotgun an invalid loot roll while the current luggage's biome is at its cap, so
/// <c>LootData.GetRandomItems</c> fills the slot with something else instead of losing it.
/// </summary>
[HarmonyPatch(typeof(Item), nameof(Item.IsValidToSpawn))]
internal static class ShotgunSpawnValidityPatch
{
    [HarmonyPostfix]
    private static void BlockAtCap(Item __instance, ref bool __result)
    {
        if (__result
            && Plugin.ShotgunPrefab != null
            && __instance.gameObject == Plugin.ShotgunPrefab
            && RootsLuggagePatch.ShotgunBlockedForCurrentRoll())
        {
            __result = false;
        }
    }
}

/// <summary>
/// Roots: force <see cref="Plugin.GuaranteedLuggageShotguns"/> random suitcases (when the run allows the shotgun).
/// Every biome: hard-cap how many luggage can yield a shotgun this run. The bookkeeping lives in
/// <see cref="ShotgunLootLedger"/>, so it survives quicksave reloads and host migration.
/// </summary>
[HarmonyPatch(typeof(LootData), nameof(LootData.GetRandomItems))]
internal static class RootsLuggagePatch
{
    private static Spawner? currentSpawner;

    // Set while re-rolling a blocked shotgun slot: the nested GetRandomItems must neither inject nor roll a shotgun.
    private static bool rollingReplacement;

    internal static void BeginSpawn(Spawner spawner) => currentSpawner = spawner;

    internal static void EndSpawn() => currentSpawner = null;

    /// <summary>
    /// Called from <see cref="ShotgunSpawnValidityPatch"/>: while luggage rolls loot, the shotgun stops being a
    /// valid roll once its biome hit <see cref="Plugin.MaxShotgunsPerBiome"/>, so the slot gets ordinary loot.
    /// </summary>
    internal static bool ShotgunBlockedForCurrentRoll()
    {
        if (rollingReplacement)
        {
            return true;
        }

        if (currentSpawner is not Luggage luggage)
        {
            return false;
        }

        SpawnPool biome = LuggageBiomeKey(luggage, luggage.spawnPool);
        return ShotgunLootLedger.CountInBiome(biome, except: luggage) >= Plugin.MaxShotgunsPerBiome;
    }

    [HarmonyPostfix]
    private static void AfterLootRoll(
        SpawnPool spawnPool,
        int count,
        bool canRepeat,
        GameObject? fallback,
        ref List<GameObject>? __result)
    {
        if (rollingReplacement)
        {
            return;
        }

        TryGuaranteeRootsShotgun(spawnPool, count, ref __result);
        EnforcePerBiomeCap(spawnPool, canRepeat, fallback, ref __result);
    }

    private static void TryGuaranteeRootsShotgun(SpawnPool spawnPool, int count, ref List<GameObject>? __result)
    {
        // Forced shotguns are always Roots-only, regardless of CanSpawnOnAnyBiome.
        if ((spawnPool & SpawnPool.LuggageRoots) == 0 || currentSpawner is not Luggage luggage)
        {
            return;
        }

        EnsureRandomLuggagePicked(luggage);
        if (!ShotgunLootLedger.IsChosen(luggage))
        {
            return;
        }

        GameObject? shotgun = Plugin.ShotgunPrefab;
        if (shotgun == null || count <= 0)
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

        // The guarantee applies only while the run allows the shotgun (custom-run item settings, cap).
        Item? shotgunItem = shotgun.GetComponent<Item>();
        if (shotgunItem == null || !shotgunItem.IsValidToSpawn())
        {
            Plugin.Log.LogInfo(
                $"Skipped forced shotgun in Roots luggage '{luggage.name}': not valid to spawn in this run.");
            return;
        }

        // The spawner only fills `count` spots; a longer list silently drops its last item.
        if (__result.Count < count)
        {
            __result.Insert(0, shotgun);
        }
        else
        {
            __result[0] = shotgun;
        }

        Plugin.Log.LogInfo(
            $"Forced shotgun into random Roots luggage '{luggage.name}' "
            + $"({ShotgunLootLedger.ChosenCount} marked this run).");
    }

    /// <summary>
    /// After loot (and Roots guarantees) are decided, count the shotguns against the biome cap. Shotguns past
    /// <see cref="Plugin.MaxShotgunsPerBiome"/> are replaced with other loot from the same pool so the suitcase
    /// still spawns as many items as it rolled.
    /// </summary>
    private static void EnforcePerBiomeCap(
        SpawnPool spawnPool,
        bool canRepeat,
        GameObject? fallbackSpawn,
        ref List<GameObject>? __result)
    {
        GameObject? shotgun = Plugin.ShotgunPrefab;
        if (__result == null || shotgun == null || currentSpawner is not Luggage luggage)
        {
            return;
        }

        SpawnPool biome = LuggageBiomeKey(luggage, spawnPool);
        if (!__result.Contains(shotgun))
        {
            // A suitcase re-rolled after a reload may no longer hold the shotgun it was counted for.
            ShotgunLootLedger.SetShotguns(luggage, biome, 0);
            return;
        }

        int already = ShotgunLootLedger.CountInBiome(biome, except: luggage);
        int allowed = Mathf.Max(0, Plugin.MaxShotgunsPerBiome - already);
        int kept = 0;
        int blocked = 0;
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

            blocked++;
            GameObject? replacement = RollReplacement(spawnPool, canRepeat, fallbackSpawn, __result, shotgun);
            if (replacement == null)
            {
                __result.RemoveAt(i);
                continue;
            }

            __result[i] = replacement;
            i++;
        }

        if (blocked > 0)
        {
            Plugin.Log.LogInfo(
                $"Blocked {blocked} shotgun(s) in '{currentSpawner.name}' — biome {biome} already at cap "
                + $"{Plugin.MaxShotgunsPerBiome} this run; replaced with other loot.");
        }

        ShotgunLootLedger.SetShotguns(luggage, biome, kept);
        if (kept > 0)
        {
            Plugin.Log.LogInfo(
                $"Luggage '{currentSpawner.name}' biome {biome}: +{kept} shotgun "
                + $"({already + kept}/{Plugin.MaxShotgunsPerBiome} this run).");
        }
    }

    /// <summary>Rolls one non-shotgun item from the same pool, preferring one the suitcase does not already hold.</summary>
    private static GameObject? RollReplacement(
        SpawnPool spawnPool,
        bool canRepeat,
        GameObject? fallbackSpawn,
        List<GameObject> current,
        GameObject shotgun)
    {
        List<GameObject>? roll;
        rollingReplacement = true;
        try
        {
            roll = LootData.GetRandomItems(spawnPool, current.Count + 1, canRepeat, fallbackSpawn);
        }
        finally
        {
            rollingReplacement = false;
        }

        if (roll == null)
        {
            return null;
        }

        GameObject? fallback = null;
        foreach (GameObject candidate in roll)
        {
            if (candidate == null || candidate == shotgun)
            {
                continue;
            }

            if (!current.Contains(candidate))
            {
                return candidate;
            }

            fallback ??= candidate;
        }

        return fallback;
    }

    internal static SpawnPool LuggageBiomeKey(Spawner spawner, SpawnPool rolledPool)
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
        if (ShotgunLootLedger.Assigned)
        {
            return;
        }

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

        if (candidates.Count == 0)
        {
            ShotgunLootLedger.MarkAssigned(candidates);
            Plugin.Log.LogWarning("No Roots luggage found to mark for shotgun spawns.");
            return;
        }

        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        int take = Mathf.Min(Plugin.GuaranteedLuggageShotguns, candidates.Count);
        ShotgunLootLedger.MarkAssigned(candidates.GetRange(0, take));

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
