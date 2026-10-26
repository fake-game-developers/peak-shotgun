using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Peak.Shotgun;

/// <summary>
/// Keeps the mod loading after a PEAK update. Harmony's <c>PatchAll</c> throws at the first patch whose game
/// method is gone and skips every patch after it, so patches are applied one class at a time here: a renamed
/// method turns off only the feature that patches it. Game names the mod reaches by string are checked at
/// startup, and world targets turn themselves off on a mismatch (<see cref="ShotTargetRegistry"/>).
/// </summary>
internal static class GameCompat
{
    internal const string ZombieTypeName = "MushroomZombie";

    internal const string SyncMobStateRpc = "RPC_SyncMobState";

    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>Applies every <c>[HarmonyPatch]</c> class in this assembly, each on its own.</summary>
    internal static void PatchAll(Harmony harmony)
    {
        int applied = 0;
        var skipped = new List<string>();
        var failed = new List<string>();
        foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
        {
            if (!type.IsDefined(typeof(HarmonyPatch), inherit: true))
            {
                continue;
            }

            try
            {
                List<MethodInfo>? patched = harmony.CreateClassProcessor(type).Patch();
                if (patched is { Count: > 0 })
                {
                    applied++;
                }
                else
                {
                    // Prepare() returned false: the patch itself found its target missing.
                    skipped.Add(type.Name);
                }
            }
            catch (Exception e)
            {
                failed.Add(type.Name);
                Plugin.Log.LogWarning($"Patch {type.Name} was not applied, so that feature is off: {Unwrap(e).Message}");
            }
        }

        string summary = $"Harmony patches: {applied} applied";
        if (skipped.Count > 0)
        {
            summary += $", {skipped.Count} skipped ({string.Join(", ", skipped)})";
        }

        if (failed.Count > 0)
        {
            summary += $", {failed.Count} failed ({string.Join(", ", failed)}). A PEAK update may have changed the game";
            Plugin.Log.LogWarning(summary + ".");
            return;
        }

        Plugin.Log.LogInfo(summary + ".");
    }

    /// <summary>Logs game types and members the mod uses by name (reflection, RPC strings) that no longer exist.</summary>
    internal static void CheckNamedMembers()
    {
        var missing = new List<string>();
        Type? zombie = FindType(ZombieTypeName);
        if (zombie == null)
        {
            missing.Add($"{ZombieTypeName} (zombie knockdown)");
        }
        else if (zombie.GetMethod("RPC_PlaySFX", AnyInstance) == null)
        {
            missing.Add($"{ZombieTypeName}.RPC_PlaySFX (knocked-down zombie silence)");
        }

        Type mob = typeof(Mob);
        if (mob.GetProperty("mobState", AnyInstance) == null
            || mob.GetNestedType("MobState", BindingFlags.Public | BindingFlags.NonPublic) is not { } state
            || !Enum.IsDefined(state, "Dead"))
        {
            missing.Add("Mob.mobState / MobState.Dead (killing beetles and scorpions offline or as owner)");
        }

        if (mob.GetMethod(SyncMobStateRpc, AnyInstance) == null)
        {
            missing.Add($"Mob.{SyncMobStateRpc} (killing beetles and scorpions owned by others)");
        }

        if (AccessTools.Field(typeof(ItemCooking), "disableCooking") == null)
        {
            missing.Add("ItemCooking.disableCooking (ammo piles could be cooked)");
        }

        if (missing.Count > 0)
        {
            Plugin.Log.LogWarning(
                $"These PEAK names were not found; a game update may have changed them: {string.Join("; ", missing)}.");
        }
    }

    /// <summary>
    /// True for what a changed game assembly throws when code built against the old one runs: a type or member
    /// is gone. Not for ordinary runtime errors (a destroyed object, a null reference).
    /// </summary>
    internal static bool IsGameMismatch(Exception e) =>
        Unwrap(e) is MissingMemberException or TypeLoadException or BadImageFormatException;

    /// <summary>Finds a game type by name in any loaded assembly, for types the mod does not reference directly.</summary>
    internal static Type? FindType(string name)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType(name);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

    private static Exception Unwrap(Exception e)
    {
        while (e is TargetInvocationException or TypeInitializationException or HarmonyException && e.InnerException != null)
        {
            e = e.InnerException;
        }

        return e;
    }
}
