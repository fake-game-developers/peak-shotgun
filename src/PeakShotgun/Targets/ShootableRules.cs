using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// Shootable target toggles, packed so the host can publish them as one room property.
/// Wire format: never renumber a bit, only append.
/// </summary>
[Flags]
internal enum ShootTargets
{
    None = 0,
    Zombies = 1 << 0,
    Mandrake = 1 << 1,
    Beetles = 1 << 2,
    Spiders = 1 << 3,
    Spores = 1 << 4,
    Scorpions = 1 << 5,
    Dynamite = 1 << 6,
    Scouts = 1 << 7,
    Scoutmaster = 1 << 8,
    Condors = 1 << 9,
    Antlions = 1 << 10,
    Frogs = 1 << 11,
    Beehives = 1 << 12,
    Ticks = 1 << 13,
    Jellyfish = 1 << 14,
    Coconuts = 1 << 15,
    Tumbleweeds = 1 << 16,
    BeeSwarms = 1 << 17,
    BingBong = 1 << 18,
    Balloons = 1 << 19,
}

/// <summary>
/// One <c>[Shootables]</c> toggle and its <c>[HitsToDefeat]</c> count. In a room both come from the host
/// (<see cref="HostConfigSync"/>); <see cref="Enabled"/> and <see cref="HitsToDefeat"/> already read those.
/// </summary>
internal sealed class ShootableRule
{
    private ConfigEntry<bool>? enabled;
    private ConfigEntry<int>? hits;

    internal ShootableRule(
        string key,
        ShootTargets flag,
        int hitsSlot,
        int defaultHits,
        string effect,
        string hitsGoal,
        bool unlocksAllBiomeLoot = true)
    {
        Key = key;
        Flag = flag;
        HitsSlot = hitsSlot;
        DefaultHits = defaultHits;
        Effect = effect;
        HitsGoal = hitsGoal;
        UnlocksAllBiomeLoot = unlocksAllBiomeLoot;
    }

    /// <summary>Key under both <c>[Shootables]</c> and <c>[HitsToDefeat]</c>.</summary>
    internal string Key { get; }

    internal ShootTargets Flag { get; }

    /// <summary>Byte index in the packed <c>[HitsToDefeat]</c> room property. -1 = no hit count (scouts take Injury every hit).</summary>
    internal int HitsSlot { get; }

    internal int DefaultHits { get; }

    /// <summary>Finishes "Shotguns can …" in the <c>[Shootables]</c> description.</summary>
    internal string Effect { get; }

    /// <summary>Finishes "Shots needed to …" in the <c>[HitsToDefeat]</c> description.</summary>
    internal string HitsGoal { get; }

    /// <summary>
    /// False only for targets that must not open luggage outside Roots (Scoutmaster). See
    /// <see cref="ShootableRules.IsZombiesOnly"/>.
    /// </summary>
    internal bool UnlocksAllBiomeLoot { get; }

    internal bool HasHitCount => HitsSlot >= 0;

    /// <summary>This client's own config (the host publishes it; clients use the host's).</summary>
    internal bool LocalEnabled => enabled?.Value ?? true;

    /// <summary>This client's own config (the host publishes it; clients use the host's).</summary>
    internal int LocalHits => HitsToDefeatSettings.Clamp(hits?.Value ?? DefaultHits);

    /// <summary>Room rule: the host's toggle in multiplayer, otherwise the local config.</summary>
    internal bool Enabled => (HostConfigSync.Shootables & Flag) != 0;

    /// <summary>Room rule: the host's hit count in multiplayer, otherwise the local config.</summary>
    internal int HitsToDefeat => HostConfigSync.HitsToDefeat.For(this);

    internal void Bind(ConfigFile config)
    {
        enabled = config.Bind("Shootables", Key, true, $"Shotguns can {Effect}. Host's value is used in multiplayer.");
        if (!HasHitCount)
        {
            return;
        }

        hits = config.Bind("HitsToDefeat", Key, DefaultHits, new ConfigDescription(
            $"Shots needed to {HitsGoal}. Host's value is used in multiplayer. Fellow scouts are not listed — they take Injury every hit.",
            new AcceptableValueRange<int>(1, HitsToDefeatSettings.MaxHits)));
    }
}

/// <summary>
/// Every <c>[Shootables]</c> / <c>[HitsToDefeat]</c> entry. Config binding, the host room properties and the
/// zombies-only loot rule are all built from <see cref="All"/>. To add one: append a <see cref="ShootTargets"/>
/// bit, add a rule here with the next free <see cref="ShootableRule.HitsSlot"/>, and (for a world target) a
/// <see cref="ShotTarget"/> in <see cref="ShotTargetRegistry"/>.
/// </summary>
internal static class ShootableRules
{
    internal static readonly ShootableRule Scouts = new(
        "Scouts", ShootTargets.Scouts, -1, 1, "injure and ragdoll other scouts", "");

    internal static readonly ShootableRule Zombies = new(
        "Zombies", ShootTargets.Zombies, 0, 2, "hit and knock down zombies", "knock down a zombie");

    internal static readonly ShootableRule Mandrake = new(
        "Mandrake", ShootTargets.Mandrake, 1, 1, "destroy mandrakes", "destroy a mandrake");

    internal static readonly ShootableRule Beetles = new(
        "Beetles", ShootTargets.Beetles, 2, 1, "kill beetles", "kill a beetle");

    internal static readonly ShootableRule Spiders = new(
        "Spiders", ShootTargets.Spiders, 3, 1, "stun spiders (same as throwing an item at them)", "stun a spider");

    internal static readonly ShootableRule Spores = new(
        "Spores",
        ShootTargets.Spores,
        4,
        1,
        "break spore bombs (spore, explosive, and poison) and clear spore clouds",
        "break a spore bomb or clear a spore cloud");

    internal static readonly ShootableRule Scorpions = new(
        "Scorpions", ShootTargets.Scorpions, 5, 1, "kill scorpions", "kill a scorpion");

    internal static readonly ShootableRule Dynamite = new(
        "Dynamite", ShootTargets.Dynamite, 6, 1, "explode dynamite on hit", "explode dynamite");

    internal static readonly ShootableRule Scoutmaster = new(
        "Scoutmaster",
        ShootTargets.Scoutmaster,
        7,
        4,
        "drive off Scoutmaster Myres after enough hits",
        "drive off the Scoutmaster",
        unlocksAllBiomeLoot: false);

    internal static readonly ShootableRule Condors = new(
        "Condors", ShootTargets.Condors, 8, 1, "kill Caldera condors and drop anyone they are carrying", "kill a condor");

    internal static readonly ShootableRule Antlions = new(
        "Antlions", ShootTargets.Antlions, 9, 1, "disable Mesa antlion pits", "disable an antlion");

    internal static readonly ShootableRule Frogs = new(
        "Frogs", ShootTargets.Frogs, 10, 1, "kill Gloom frogs and drop cooked frog legs", "kill a frog (drops cooked frog legs)");

    internal static readonly ShootableRule Beehives = new(
        "Beehives", ShootTargets.Beehives, 11, 1, "smash Tropics beehives (honeycomb drops, bees get angry)", "smash a beehive");

    internal static readonly ShootableRule Ticks = new(
        "Ticks", ShootTargets.Ticks, 12, 1, "knock ticks off scouts", "knock a tick off a scout");

    internal static readonly ShootableRule Jellyfish = new(
        "Jellyfish", ShootTargets.Jellyfish, 13, 1, "smash Shore jellyfish", "smash a jellyfish");

    internal static readonly ShootableRule Coconuts = new(
        "Coconuts", ShootTargets.Coconuts, 14, 1, "knock whole coconuts off palm trees", "knock a coconut off a tree");

    // The rules below do not unlock all-biome luggage, so an existing Zombies-only config keeps Roots-only loot
    // when these keys appear with their default (on).
    internal static readonly ShootableRule Tumbleweeds = new(
        "Tumbleweeds",
        ShootTargets.Tumbleweeds,
        -1,
        1,
        "blow Mesa tumbleweeds away (every hit pushes)",
        "",
        unlocksAllBiomeLoot: false);

    internal static readonly ShootableRule BeeSwarms = new(
        "BeeSwarms",
        ShootTargets.BeeSwarms,
        15,
        1,
        "scatter bee swarms, angry or not (they disperse like bees that lost their hive)",
        "scatter a bee swarm",
        unlocksAllBiomeLoot: false);

    internal static readonly ShootableRule BingBong = new(
        "BingBong",
        ShootTargets.BingBong,
        -1,
        1,
        "make Bing Bong squeak, say something, and fly (every hit reacts)",
        "",
        unlocksAllBiomeLoot: false);

    internal static readonly ShootableRule Balloons = new(
        "Balloons",
        ShootTargets.Balloons,
        -1,
        1,
        "pop balloons: ones tied to a scout (they lose that lift) and balloon items on the ground or in a hand",
        "",
        unlocksAllBiomeLoot: false);

    internal static readonly IReadOnlyList<ShootableRule> All =
    [
        Scouts,
        Zombies,
        Mandrake,
        Beetles,
        Spiders,
        Spores,
        Scorpions,
        Dynamite,
        Scoutmaster,
        Condors,
        Antlions,
        Frogs,
        Beehives,
        Ticks,
        Jellyfish,
        Coconuts,
        Tumbleweeds,
        BeeSwarms,
        BingBong,
        Balloons,
    ];

    /// <summary>Bytes in the packed <c>[HitsToDefeat]</c> room property (highest slot + 1).</summary>
    internal static readonly int HitsSlotCount = CountHitsSlots();

    /// <summary>This client's own <c>[Shootables]</c> config (the host publishes it; clients use the host's).</summary>
    internal static ShootTargets LocalShootables
    {
        get
        {
            ShootTargets targets = ShootTargets.None;
            foreach (ShootableRule rule in All)
            {
                if (rule.LocalEnabled)
                {
                    targets |= rule.Flag;
                }
            }

            return targets;
        }
    }

    /// <summary>
    /// True when Zombies is the only loot-relevant enabled shootable. Scoutmaster does not unlock
    /// all-biome luggage. That mix (or Zombies alone) keeps luggage Roots-exclusive unless
    /// <c>CanSpawnOnAnyBiome</c> forces every biome.
    /// </summary>
    internal static bool IsZombiesOnly
    {
        get
        {
            ShootTargets lootRelevant = ShootTargets.None;
            foreach (ShootableRule rule in All)
            {
                if (rule.UnlocksAllBiomeLoot)
                {
                    lootRelevant |= rule.Flag;
                }
            }

            return (LocalShootables & lootRelevant) == ShootTargets.Zombies;
        }
    }

    internal static void Bind(ConfigFile config)
    {
        foreach (ShootableRule rule in All)
        {
            rule.Bind(config);
        }
    }

    private static int CountHitsSlots()
    {
        var flags = new HashSet<ShootTargets>();
        var slots = new HashSet<int>();
        int highest = -1;
        foreach (ShootableRule rule in All)
        {
            // A duplicate here would silently desync the room properties, so fail loudly at startup.
            if (!flags.Add(rule.Flag) || (rule.HasHitCount && !slots.Add(rule.HitsSlot)))
            {
                throw new InvalidOperationException($"Shootable rule '{rule.Key}' reuses a flag or hits slot.");
            }

            highest = Mathf.Max(highest, rule.HitsSlot);
        }

        if (slots.Count != highest + 1)
        {
            throw new InvalidOperationException("Shootable hits slots must be contiguous from 0.");
        }

        return highest + 1;
    }
}

/// <summary>
/// Shots needed to defeat each non-scout target, indexed by <see cref="ShootableRule.HitsSlot"/>.
/// Packed as a byte array for the host room property (slot order is fixed — do not reorder; append only).
/// </summary>
internal readonly struct HitsToDefeatSettings : IEquatable<HitsToDefeatSettings>
{
    /// <summary>Oldest host builds published 7 bytes; anything shorter is ignored.</summary>
    internal const int LegacyPackedLength = 7;

    internal const int MaxHits = 20;

    private readonly byte[]? hits;

    private HitsToDefeatSettings(byte[] hits) => this.hits = hits;

    internal static HitsToDefeatSettings Defaults => From(rule => rule.DefaultHits);

    /// <summary>This client's own <c>[HitsToDefeat]</c> config (the host publishes it; clients use the host's).</summary>
    internal static HitsToDefeatSettings Local => From(rule => rule.LocalHits);

    /// <summary>Hits for that rule. A slot missing from an older host's packet uses the default.</summary>
    internal int For(ShootableRule rule)
    {
        if (!rule.HasHitCount)
        {
            return 1;
        }

        return hits != null && rule.HitsSlot < hits.Length ? hits[rule.HitsSlot] : rule.DefaultHits;
    }

    internal byte[] ToPacked() => (byte[])(hits ?? Defaults.hits!).Clone();

    internal static HitsToDefeatSettings FromPacked(byte[]? packed)
    {
        if (packed == null || packed.Length < LegacyPackedLength)
        {
            return Defaults;
        }

        return From(rule => rule.HitsSlot < packed.Length ? packed[rule.HitsSlot] : rule.DefaultHits);
    }

    internal static int Clamp(int value) => Mathf.Clamp(value, 1, MaxHits);

    public bool Equals(HitsToDefeatSettings other)
    {
        foreach (ShootableRule rule in ShootableRules.All)
        {
            if (For(rule) != other.For(rule))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is HitsToDefeatSettings other && Equals(other);

    public override string ToString()
    {
        var parts = new List<string>();
        foreach (ShootableRule rule in ShootableRules.All)
        {
            if (rule.HasHitCount)
            {
                parts.Add($"{rule.Key}={For(rule)}");
            }
        }

        return string.Join(", ", parts);
    }

    public override int GetHashCode()
    {
        int hash = 17;
        foreach (ShootableRule rule in ShootableRules.All)
        {
            hash = hash * 31 + For(rule);
        }

        return hash;
    }

    private static HitsToDefeatSettings From(Func<ShootableRule, int> value)
    {
        byte[] packed = new byte[ShootableRules.HitsSlotCount];
        foreach (ShootableRule rule in ShootableRules.All)
        {
            if (rule.HasHitCount)
            {
                packed[rule.HitsSlot] = (byte)Clamp(value(rule));
            }
        }

        return new HitsToDefeatSettings(packed);
    }
}
