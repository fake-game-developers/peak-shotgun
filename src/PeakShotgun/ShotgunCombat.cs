using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using Peak.Afflictions;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace Peak.Shotgun;

/// <summary>
/// Shots needed to defeat each non-scout target. Fellow scouts stay damage-per-shot and are not listed here.
/// Packed as a byte array for the host room property (order is fixed — do not reorder; append only).
/// </summary>
internal readonly struct HitsToDefeatSettings
{
    /// <summary>Bytes written by current builds. Older hosts may publish only 7, 8, 11, or 14.</summary>
    internal const int PackedLength = 15;

    internal const int LegacyPackedLength = 7;

    internal const int ScoutmasterPackedLength = 8;

    internal const int JellyfishPackedLength = 14;

    internal const int MaxHits = 20;

    internal HitsToDefeatSettings(
        int zombies,
        int mandrake,
        int beetles,
        int spiders,
        int spores,
        int scorpions,
        int dynamite,
        int scoutmaster,
        int condors,
        int antlions,
        int frogs,
        int beehives,
        int ticks,
        int jellyfish,
        int coconuts)
    {
        Zombies = Clamp(zombies);
        Mandrake = Clamp(mandrake);
        Beetles = Clamp(beetles);
        Spiders = Clamp(spiders);
        Spores = Clamp(spores);
        Scorpions = Clamp(scorpions);
        Dynamite = Clamp(dynamite);
        Scoutmaster = Clamp(scoutmaster);
        Condors = Clamp(condors);
        Antlions = Clamp(antlions);
        Frogs = Clamp(frogs);
        Beehives = Clamp(beehives);
        Ticks = Clamp(ticks);
        Jellyfish = Clamp(jellyfish);
        Coconuts = Clamp(coconuts);
    }

    internal int Zombies { get; }

    internal int Mandrake { get; }

    internal int Beetles { get; }

    internal int Spiders { get; }

    internal int Spores { get; }

    internal int Scorpions { get; }

    internal int Dynamite { get; }

    internal int Scoutmaster { get; }

    internal int Condors { get; }

    internal int Antlions { get; }

    internal int Frogs { get; }

    internal int Beehives { get; }

    internal int Ticks { get; }

    internal int Jellyfish { get; }

    internal int Coconuts { get; }

    internal static HitsToDefeatSettings Defaults { get; } = new(2, 1, 1, 1, 1, 1, 1, 4, 1, 1, 1, 1, 1, 1, 1);

    internal int For(ShotTargetKind kind) => kind switch
    {
        ShotTargetKind.Spider => Spiders,
        ShotTargetKind.Beetle => Beetles,
        ShotTargetKind.Scorpion => Scorpions,
        ShotTargetKind.Mandrake => Mandrake,
        ShotTargetKind.Dynamite => Dynamite,
        ShotTargetKind.SporeBomb or ShotTargetKind.SporeCloud => Spores,
        ShotTargetKind.Scoutmaster => Scoutmaster,
        ShotTargetKind.Condor => Condors,
        ShotTargetKind.Antlion => Antlions,
        ShotTargetKind.Frog => Frogs,
        ShotTargetKind.Beehive => Beehives,
        ShotTargetKind.Tick => Ticks,
        ShotTargetKind.Jellyfish => Jellyfish,
        ShotTargetKind.Coconut => Coconuts,
        _ => 1,
    };

    internal byte[] ToPacked() =>
    [
        (byte)Zombies,
        (byte)Mandrake,
        (byte)Beetles,
        (byte)Spiders,
        (byte)Spores,
        (byte)Scorpions,
        (byte)Dynamite,
        (byte)Scoutmaster,
        (byte)Condors,
        (byte)Antlions,
        (byte)Frogs,
        (byte)Beehives,
        (byte)Ticks,
        (byte)Jellyfish,
        (byte)Coconuts,
    ];

    internal static HitsToDefeatSettings FromPacked(byte[]? packed)
    {
        if (packed == null || packed.Length < LegacyPackedLength)
        {
            return Defaults;
        }

        return new HitsToDefeatSettings(
            packed[0],
            packed[1],
            packed[2],
            packed[3],
            packed[4],
            packed[5],
            packed[6],
            packed.Length >= ScoutmasterPackedLength ? packed[7] : 4,
            packed.Length >= 9 ? packed[8] : 1,
            packed.Length >= 10 ? packed[9] : 1,
            packed.Length >= 11 ? packed[10] : 1,
            packed.Length >= 12 ? packed[11] : 1,
            packed.Length >= 13 ? packed[12] : 1,
            packed.Length >= JellyfishPackedLength ? packed[13] : 1,
            packed.Length >= PackedLength ? packed[14] : 1);
    }

    internal static int Clamp(int value) => Mathf.Clamp(value, 1, MaxHits);
}

/// <summary>Shootable target toggles, packed so the host can publish them as one room property.</summary>
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
}

/// <summary>Non-player targets, as sent to the host in <see cref="Action_Gun.RPC_HostShootTarget"/>.</summary>
internal enum ShotTargetKind
{
    Spider = 1,
    Beetle = 2,
    Scorpion = 3,
    Mandrake = 4,
    Dynamite = 5,
    SporeBomb = 6,
    SporeCloud = 7,
    Scoutmaster = 8,
    Condor = 9,
    Antlion = 10,
    Frog = 11,
    Beehive = 12,
    Tick = 13,
    Jellyfish = 14,
    Coconut = 15,
}

/// <summary>
/// Config + hit resolution for what the shotgun can affect (creatures, hazards, other scouts).
/// In multiplayer the rules come from the host (<see cref="HostConfigSync"/>), not from each client's config,
/// and the host applies every non-character hit after re-checking them.
/// </summary>
internal static class ShotgunCombat
{
    private static ConfigEntry<bool>? shootScouts;
    private static ConfigEntry<bool>? shootZombies;
    private static ConfigEntry<bool>? shootMandrake;
    private static ConfigEntry<bool>? shootBeetles;
    private static ConfigEntry<bool>? shootSpiders;
    private static ConfigEntry<bool>? shootSpores;
    private static ConfigEntry<bool>? shootScorpions;
    private static ConfigEntry<bool>? shootDynamite;
    private static ConfigEntry<bool>? shootScoutmaster;
    private static ConfigEntry<bool>? shootCondors;
    private static ConfigEntry<bool>? shootAntlions;
    private static ConfigEntry<bool>? shootFrogs;
    private static ConfigEntry<bool>? shootBeehives;
    private static ConfigEntry<bool>? shootTicks;
    private static ConfigEntry<bool>? shootJellyfish;
    private static ConfigEntry<bool>? shootCoconuts;

    private static ConfigEntry<int>? hitsZombies;
    private static ConfigEntry<int>? hitsMandrake;
    private static ConfigEntry<int>? hitsBeetles;
    private static ConfigEntry<int>? hitsSpiders;
    private static ConfigEntry<int>? hitsSpores;
    private static ConfigEntry<int>? hitsScorpions;
    private static ConfigEntry<int>? hitsDynamite;
    private static ConfigEntry<int>? hitsScoutmaster;
    private static ConfigEntry<int>? hitsCondors;
    private static ConfigEntry<int>? hitsAntlions;
    private static ConfigEntry<int>? hitsFrogs;
    private static ConfigEntry<int>? hitsBeehives;
    private static ConfigEntry<int>? hitsTicks;
    private static ConfigEntry<int>? hitsJellyfish;
    private static ConfigEntry<int>? hitsCoconuts;

    /// <summary>Host-side (or offline) hit counts toward defeat for world targets, keyed by Photon view id or local instance id.</summary>
    private static readonly Dictionary<int, int> worldHitCounts = new();

    /// <summary>Defeated condors / antlions / jellyfish, replayed to late joiners via a room property.</summary>
    private static readonly Dictionary<long, ShotTargetKind> worldDefeats = new();

    private static PropertyInfo? mobStateProperty;
    private static object? mobStateDead;

    /// <summary>This client's own <c>[Shootables]</c> config (the host publishes it; clients use the host's).</summary>
    internal static ShootTargets LocalShootables =>
        (shootScouts?.Value ?? true ? ShootTargets.Scouts : ShootTargets.None)
        | (shootZombies?.Value ?? true ? ShootTargets.Zombies : ShootTargets.None)
        | (shootMandrake?.Value ?? true ? ShootTargets.Mandrake : ShootTargets.None)
        | (shootBeetles?.Value ?? true ? ShootTargets.Beetles : ShootTargets.None)
        | (shootSpiders?.Value ?? true ? ShootTargets.Spiders : ShootTargets.None)
        | (shootSpores?.Value ?? true ? ShootTargets.Spores : ShootTargets.None)
        | (shootScorpions?.Value ?? true ? ShootTargets.Scorpions : ShootTargets.None)
        | (shootDynamite?.Value ?? true ? ShootTargets.Dynamite : ShootTargets.None)
        | (shootScoutmaster?.Value ?? true ? ShootTargets.Scoutmaster : ShootTargets.None)
        | (shootCondors?.Value ?? true ? ShootTargets.Condors : ShootTargets.None)
        | (shootAntlions?.Value ?? true ? ShootTargets.Antlions : ShootTargets.None)
        | (shootFrogs?.Value ?? true ? ShootTargets.Frogs : ShootTargets.None)
        | (shootBeehives?.Value ?? true ? ShootTargets.Beehives : ShootTargets.None)
        | (shootTicks?.Value ?? true ? ShootTargets.Ticks : ShootTargets.None)
        | (shootJellyfish?.Value ?? true ? ShootTargets.Jellyfish : ShootTargets.None)
        | (shootCoconuts?.Value ?? true ? ShootTargets.Coconuts : ShootTargets.None);

    /// <summary>
    /// True when Zombies is the only loot-relevant enabled shootable. Scoutmaster does not unlock
    /// all-biome luggage. That mix (or Zombies alone) keeps luggage Roots-exclusive unless
    /// <c>CanSpawnOnAnyBiome</c> forces every biome.
    /// </summary>
    internal static bool IsZombiesOnlyShootables =>
        (LocalShootables & ~ShootTargets.Scoutmaster) == ShootTargets.Zombies;

    internal static bool CanShootScouts => Allows(ShootTargets.Scouts);

    internal static bool CanShootZombies => Allows(ShootTargets.Zombies);

    internal static bool CanShootMandrake => Allows(ShootTargets.Mandrake);

    internal static bool CanShootBeetles => Allows(ShootTargets.Beetles);

    internal static bool CanShootSpiders => Allows(ShootTargets.Spiders);

    internal static bool CanShootSpores => Allows(ShootTargets.Spores);

    internal static bool CanShootScorpions => Allows(ShootTargets.Scorpions);

    internal static bool CanShootDynamite => Allows(ShootTargets.Dynamite);

    internal static bool CanShootScoutmaster => Allows(ShootTargets.Scoutmaster);

    internal static bool CanShootCondors => Allows(ShootTargets.Condors);

    internal static bool CanShootAntlions => Allows(ShootTargets.Antlions);

    internal static bool CanShootFrogs => Allows(ShootTargets.Frogs);

    internal static bool CanShootBeehives => Allows(ShootTargets.Beehives);

    internal static bool CanShootTicks => Allows(ShootTargets.Ticks);

    internal static bool CanShootJellyfish => Allows(ShootTargets.Jellyfish);

    internal static bool CanShootCoconuts => Allows(ShootTargets.Coconuts);

    /// <summary>This client's own <c>[HitsToDefeat]</c> config (the host publishes it; clients use the host's).</summary>
    internal static HitsToDefeatSettings LocalHitsToDefeat => new(
        hitsZombies?.Value ?? 2,
        hitsMandrake?.Value ?? 1,
        hitsBeetles?.Value ?? 1,
        hitsSpiders?.Value ?? 1,
        hitsSpores?.Value ?? 1,
        hitsScorpions?.Value ?? 1,
        hitsDynamite?.Value ?? 1,
        hitsScoutmaster?.Value ?? 4,
        hitsCondors?.Value ?? 1,
        hitsAntlions?.Value ?? 1,
        hitsFrogs?.Value ?? 1,
        hitsBeehives?.Value ?? 1,
        hitsTicks?.Value ?? 1,
        hitsJellyfish?.Value ?? 1,
        hitsCoconuts?.Value ?? 1);

    /// <summary>Shots needed to knock down a zombie (host-synced).</summary>
    internal static int HitsToDefeatZombies => HostConfigSync.HitsToDefeat.Zombies;

    private static bool Allows(ShootTargets target) => (HostConfigSync.Shootables & target) != 0;

    private static bool Allows(ShotTargetKind kind) => kind switch
    {
        ShotTargetKind.Spider => CanShootSpiders,
        ShotTargetKind.Beetle => CanShootBeetles,
        ShotTargetKind.Scorpion => CanShootScorpions,
        ShotTargetKind.Mandrake => CanShootMandrake,
        ShotTargetKind.Dynamite => CanShootDynamite,
        ShotTargetKind.SporeBomb or ShotTargetKind.SporeCloud => CanShootSpores,
        ShotTargetKind.Scoutmaster => CanShootScoutmaster,
        ShotTargetKind.Condor => CanShootCondors,
        ShotTargetKind.Antlion => CanShootAntlions,
        ShotTargetKind.Frog => CanShootFrogs,
        ShotTargetKind.Beehive => CanShootBeehives,
        ShotTargetKind.Tick => CanShootTicks,
        ShotTargetKind.Jellyfish => CanShootJellyfish,
        ShotTargetKind.Coconut => CanShootCoconuts,
        _ => false,
    };

    internal static void Bind(ConfigFile config)
    {
        shootScouts = config.Bind("Shootables", "Scouts", true, "Shotguns can injure and ragdoll other scouts. Host's value is used in multiplayer.");
        shootZombies = config.Bind("Shootables", "Zombies", true, "Shotguns can hit and knock down zombies. Host's value is used in multiplayer.");
        shootMandrake = config.Bind("Shootables", "Mandrake", true, "Shotguns can destroy mandrakes. Host's value is used in multiplayer.");
        shootBeetles = config.Bind("Shootables", "Beetles", true, "Shotguns can kill beetles. Host's value is used in multiplayer.");
        shootSpiders = config.Bind("Shootables", "Spiders", true, "Shotguns can stun spiders (same as throwing an item at them). Host's value is used in multiplayer.");
        shootSpores = config.Bind("Shootables", "Spores", true, "Shotguns can break spore bombs (spore, explosive, and poison) and clear spore clouds. Host's value is used in multiplayer.");
        shootScorpions = config.Bind("Shootables", "Scorpions", true, "Shotguns can kill scorpions. Host's value is used in multiplayer.");
        shootDynamite = config.Bind("Shootables", "Dynamite", true, "Shotguns can explode dynamite on hit. Host's value is used in multiplayer.");
        shootScoutmaster = config.Bind("Shootables", "Scoutmaster", true, "Shotguns can drive off Scoutmaster Myres after enough hits. Host's value is used in multiplayer.");
        shootCondors = config.Bind("Shootables", "Condors", true, "Shotguns can kill Caldera condors and drop anyone they are carrying. Host's value is used in multiplayer.");
        shootAntlions = config.Bind("Shootables", "Antlions", true, "Shotguns can disable Mesa antlion pits. Host's value is used in multiplayer.");
        shootFrogs = config.Bind("Shootables", "Frogs", true, "Shotguns can kill Gloom frogs and drop cooked frog legs. Host's value is used in multiplayer.");
        shootBeehives = config.Bind("Shootables", "Beehives", true, "Shotguns can smash Tropics beehives (honeycomb drops, bees get angry). Host's value is used in multiplayer.");
        shootTicks = config.Bind("Shootables", "Ticks", true, "Shotguns can knock ticks off scouts. Host's value is used in multiplayer.");
        shootJellyfish = config.Bind("Shootables", "Jellyfish", true, "Shotguns can smash Shore jellyfish. Host's value is used in multiplayer.");
        shootCoconuts = config.Bind("Shootables", "Coconuts", true, "Shotguns can knock whole coconuts off palm trees. Host's value is used in multiplayer.");

        ConfigDescription hitsDesc(string what) => new(
            $"Shots needed to {what}. Host's value is used in multiplayer. Fellow scouts are not listed — they take Injury every hit.",
            new AcceptableValueRange<int>(1, HitsToDefeatSettings.MaxHits));

        hitsZombies = config.Bind("HitsToDefeat", "Zombies", 2, hitsDesc("knock down a zombie"));
        hitsMandrake = config.Bind("HitsToDefeat", "Mandrake", 1, hitsDesc("destroy a mandrake"));
        hitsBeetles = config.Bind("HitsToDefeat", "Beetles", 1, hitsDesc("kill a beetle"));
        hitsSpiders = config.Bind("HitsToDefeat", "Spiders", 1, hitsDesc("stun a spider"));
        hitsSpores = config.Bind("HitsToDefeat", "Spores", 1, hitsDesc("break a spore bomb or clear a spore cloud"));
        hitsScorpions = config.Bind("HitsToDefeat", "Scorpions", 1, hitsDesc("kill a scorpion"));
        hitsDynamite = config.Bind("HitsToDefeat", "Dynamite", 1, hitsDesc("explode dynamite"));
        hitsScoutmaster = config.Bind("HitsToDefeat", "Scoutmaster", 4, hitsDesc("drive off the Scoutmaster"));
        hitsCondors = config.Bind("HitsToDefeat", "Condors", 1, hitsDesc("kill a condor"));
        hitsAntlions = config.Bind("HitsToDefeat", "Antlions", 1, hitsDesc("disable an antlion"));
        hitsFrogs = config.Bind("HitsToDefeat", "Frogs", 1, hitsDesc("kill a frog (drops cooked frog legs)"));
        hitsBeehives = config.Bind("HitsToDefeat", "Beehives", 1, hitsDesc("smash a beehive"));
        hitsTicks = config.Bind("HitsToDefeat", "Ticks", 1, hitsDesc("knock a tick off a scout"));
        hitsJellyfish = config.Bind("HitsToDefeat", "Jellyfish", 1, hitsDesc("smash a jellyfish"));
        hitsCoconuts = config.Bind("HitsToDefeat", "Coconuts", 1, hitsDesc("knock a coconut off a tree"));
    }

    /// <summary>Clears host/offline world-target hit progress (map change / leave room).</summary>
    internal static void ClearWorldHitCounts()
    {
        worldHitCounts.Clear();
        worldDefeats.Clear();
    }

    internal static int[] PackWorldDefeats()
    {
        int[] packed = new int[worldDefeats.Count * 3];
        int i = 0;
        foreach (KeyValuePair<long, ShotTargetKind> entry in worldDefeats)
        {
            UnpackDefeatKey(entry.Key, out int viewId, out int extra);
            packed[i++] = viewId;
            packed[i++] = (int)entry.Value;
            packed[i++] = extra;
        }

        return packed;
    }

    internal static void ApplyPackedWorldDefeats(int[]? packed)
    {
        if (packed == null)
        {
            return;
        }

        for (int i = 0; i + 2 < packed.Length; i += 3)
        {
            int viewId = packed[i];
            var kind = (ShotTargetKind)packed[i + 1];
            int extra = packed[i + 2];
            worldDefeats[DefeatKey(viewId, extra)] = kind;
            PhotonView? view = PhotonView.Find(viewId);
            ClientApplyDefeat(view, kind, extra);
        }
    }

    private static long DefeatKey(int viewId, int extra) => ((long)viewId << 32) | (uint)extra;

    private static void UnpackDefeatKey(long key, out int viewId, out int extra)
    {
        viewId = (int)(key >> 32);
        extra = (int)key;
    }

    /// <summary>
    /// Applies the shotgun hit to whatever is under the collider, if that target is enabled.
    /// Returns true when a target was handled (so the pellet should not also hit something else).
    /// </summary>
    internal static bool TryHit(
        Collider collider,
        Character? shooter,
        Action_Gun gun,
        int shotId,
        Vector3 hitPoint,
        Vector3 direction)
    {
        if (collider == null)
        {
            return false;
        }

        // Ticks sit on a scout. Classify them before the scout so a hit on the bug does not Injury the carrier.
        if (CanShootTicks && collider.GetComponentInParent<Bugfix>() is { } tick)
        {
            return HitWorldTarget(tick, ShotTargetKind.Tick, gun, shotId);
        }

        Character? character = collider.GetComponentInParent<Character>();
        if (character != null && character != shooter)
        {
            return TryHitCharacter(character, gun, shotId, hitPoint, direction);
        }

        if (!TryClassify(collider, out Component? target, out ShotTargetKind kind))
        {
            return false;
        }

        return HitWorldTarget(target, kind, gun, shotId);
    }

    private static bool HitWorldTarget(Component target, ShotTargetKind kind, Action_Gun gun, int shotId)
    {
        PhotonView? view = ViewOf(target);
        int extra = ExtraOf(target, kind);
        if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient || view == null)
        {
            ApplyRegisteredHit(target, kind, view, gun, extra);
        }
        else
        {
            gun.photonView.RPC(
                nameof(Action_Gun.RPC_HostShootTarget),
                RpcTarget.MasterClient,
                view.ViewID,
                (int)kind,
                shotId,
                extra);
        }

        return true;
    }

    private static int ExtraOf(Component target, ShotTargetKind kind) =>
        kind == ShotTargetKind.Jellyfish ? target.transform.GetSiblingIndex() : 0;

    /// <summary>Finds the first enabled target type under the collider, in priority order.</summary>
    private static bool TryClassify(Collider collider, out Component target, out ShotTargetKind kind)
    {
        target = null!;
        kind = default;

        if (CanShootSpiders)
        {
            Spider? spider = collider.GetComponentInParent<Spider>();
            if (spider == null)
            {
                SpiderTrigger? trigger = collider.GetComponentInParent<SpiderTrigger>();
                spider = trigger != null ? trigger.spider : null;
            }

            if (spider != null)
            {
                return Found(spider, ShotTargetKind.Spider, out target, out kind);
            }
        }

        if (CanShootBeetles && collider.GetComponentInParent<Beetle>() is { } beetle)
        {
            return Found(beetle, ShotTargetKind.Beetle, out target, out kind);
        }

        if (CanShootScorpions && collider.GetComponentInParent<Scorpion>() is { } scorpion)
        {
            return Found(scorpion, ShotTargetKind.Scorpion, out target, out kind);
        }

        if (CanShootCondors && collider.GetComponentInParent<Condor>() is { } condor)
        {
            return Found(condor, ShotTargetKind.Condor, out target, out kind);
        }

        if (CanShootAntlions && collider.GetComponentInParent<Antlion>() is { } antlion)
        {
            return Found(antlion, ShotTargetKind.Antlion, out target, out kind);
        }

        if (CanShootFrogs)
        {
            FrogTongue? frog = collider.GetComponentInParent<FrogTongue>()
                ?? collider.transform.root.GetComponentInChildren<FrogTongue>(true);
            if (frog != null)
            {
                return Found(frog, ShotTargetKind.Frog, out target, out kind);
            }
        }

        if (CanShootBeehives && collider.GetComponentInParent<Beehive>() is { } hive)
        {
            return Found(hive, ShotTargetKind.Beehive, out target, out kind);
        }

        if (CanShootJellyfish && collider.GetComponentInParent<SlipperyJellyfish>() is { } jellyfish)
        {
            return Found(jellyfish, ShotTargetKind.Jellyfish, out target, out kind);
        }

        if (CanShootCoconuts && collider.GetComponentInParent<Item>() is { } coconut && IsTreeCoconut(coconut))
        {
            return Found(coconut, ShotTargetKind.Coconut, out target, out kind);
        }

        if (CanShootMandrake && collider.GetComponentInParent<Mandrake>() is { } mandrake)
        {
            return Found(mandrake, ShotTargetKind.Mandrake, out target, out kind);
        }

        if (CanShootDynamite && collider.GetComponentInParent<Dynamite>() is { } dynamite)
        {
            return Found(dynamite, ShotTargetKind.Dynamite, out target, out kind);
        }

        if (CanShootSpores)
        {
            if (collider.GetComponentInParent<Breakable>() is { } breakable && IsSporeBomb(breakable))
            {
                return Found(breakable, ShotTargetKind.SporeBomb, out target, out kind);
            }

            StatusEmitter? emitter = collider.GetComponentInParent<StatusEmitter>();
            if (IsSporeCloud(emitter))
            {
                return Found(emitter!, ShotTargetKind.SporeCloud, out target, out kind);
            }
        }

        return false;
    }

    private static bool Found(Component component, ShotTargetKind found, out Component target, out ShotTargetKind kind)
    {
        target = component;
        kind = found;
        return true;
    }

    /// <summary>
    /// Spore / explosive / poison bombs are <see cref="Breakable"/> mushrooms (prefab names like
    /// SporeShroom, ExploShroom, PoisonShroom). Not <see cref="CloudFungus"/> (the deployable platform).
    /// </summary>
    private static bool IsSporeBomb(Breakable breakable)
    {
        if (breakable == null || breakable is BreakableEgg)
        {
            return false;
        }

        if (NameLooksLikeSporeBomb(breakable.gameObject.name))
        {
            return true;
        }

        Item? item = breakable.GetComponent<Item>();
        string? itemName = item != null ? item.UIData.itemName : null;
        return !string.IsNullOrEmpty(itemName) && NameLooksLikeSporeBomb(itemName);
    }

    private static bool NameLooksLikeSporeBomb(string name) =>
        ContainsOrdinalIgnoreCase(name, "SporeShroom")
        || ContainsOrdinalIgnoreCase(name, "ExploShroom")
        || ContainsOrdinalIgnoreCase(name, "PoisonShroom")
        || ContainsOrdinalIgnoreCase(name, "SporeMushroom")
        || ContainsOrdinalIgnoreCase(name, "SporeFungus");

    private static bool ContainsOrdinalIgnoreCase(string haystack, string needle) =>
        haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsSporeCloud(StatusEmitter? emitter) =>
        emitter != null && emitter.statusType == CharacterAfflictions.STATUSTYPE.Spores;

    /// <summary>
    /// Host side of <see cref="Action_Gun.RPC_HostShootTarget"/>: find the claimed target under the view and apply
    /// it only if it really is that kind of target and the room allows shooting it.
    /// </summary>
    internal static void HostApply(PhotonView view, ShotTargetKind kind, Action_Gun? gun = null, int extra = 0)
    {
        GameObject root = view.gameObject;
        Component? target = kind switch
        {
            ShotTargetKind.Spider => Near<Spider>(root),
            ShotTargetKind.Beetle => Near<Beetle>(root),
            ShotTargetKind.Scorpion => Near<Scorpion>(root),
            ShotTargetKind.Mandrake => Near<Mandrake>(root),
            ShotTargetKind.Dynamite => Near<Dynamite>(root),
            ShotTargetKind.SporeBomb => Near<Breakable>(root) is { } breakable && IsSporeBomb(breakable) ? breakable : null,
            ShotTargetKind.SporeCloud => Near<StatusEmitter>(root) is { } emitter && IsSporeCloud(emitter) ? emitter : null,
            ShotTargetKind.Scoutmaster => Near<Scoutmaster>(root),
            ShotTargetKind.Condor => Near<Condor>(root),
            ShotTargetKind.Antlion => Near<Antlion>(root),
            ShotTargetKind.Frog => Near<FrogTongue>(root),
            ShotTargetKind.Beehive => Near<Beehive>(root),
            ShotTargetKind.Tick => Near<Bugfix>(root),
            ShotTargetKind.Jellyfish => FindJellyfish(view, extra),
            ShotTargetKind.Coconut => Near<Item>(root) is { } coconut && IsTreeCoconut(coconut) ? coconut : null,
            _ => null,
        };

        // The view must be the target's own, so a forged id cannot destroy some larger object that merely
        // contains a mandrake or spore emitter.
        if (target != null && ViewOf(target) == view)
        {
            ApplyRegisteredHit(target, kind, view, gun, extra);
        }
    }

    private static SlipperyJellyfish? FindJellyfish(PhotonView view, int extra)
    {
        Transform root = view.transform;
        if (extra >= 0 && extra < root.childCount)
        {
            SlipperyJellyfish? child = root.GetChild(extra).GetComponent<SlipperyJellyfish>();
            if (child != null)
            {
                return child;
            }
        }

        return Near<SlipperyJellyfish>(view.gameObject);
    }

    private static void ApplyRegisteredHit(Component target, ShotTargetKind kind, PhotonView? view, Action_Gun? gun, int extra)
    {
        if (!RegisterWorldHitTowardDefeat(target, kind, view, extra))
        {
            return;
        }

        Apply(target, kind, view);
        ReplicateWorldDefeat(gun, view, kind, extra);
    }

    private static bool NeedsClientDefeat(ShotTargetKind kind) =>
        kind is ShotTargetKind.Condor or ShotTargetKind.Antlion or ShotTargetKind.Jellyfish
            or ShotTargetKind.Frog;

    private static void ReplicateWorldDefeat(Action_Gun? gun, PhotonView? view, ShotTargetKind kind, int extra)
    {
        if (!NeedsClientDefeat(kind))
        {
            return;
        }

        if (view != null && view.ViewID != 0)
        {
            worldDefeats[DefeatKey(view.ViewID, extra)] = kind;
        }

        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
        {
            return;
        }

        HostConfigSync.PublishWorldDefeats();
        if (gun != null && view != null)
        {
            gun.photonView.RPC(nameof(Action_Gun.RPC_ApplyWorldDefeat), RpcTarget.Others, view.ViewID, (int)kind, extra);
        }
    }

    /// <summary>Other clients / late joiners: apply a host-authoritative condor, antlion, frog, or jellyfish defeat.</summary>
    internal static void ClientApplyDefeat(PhotonView? view, ShotTargetKind kind, int extra = 0)
    {
        if (kind == ShotTargetKind.Condor)
        {
            Condor? condor = view != null ? Near<Condor>(view.gameObject) : null;
            DefeatCondor(condor, destroy: false);
            return;
        }

        if (kind == ShotTargetKind.Frog)
        {
            FrogTongue? frog = view != null ? Near<FrogTongue>(view.gameObject) : null;
            DefeatFrog(frog, destroy: false);
            return;
        }

        if (view == null)
        {
            return;
        }

        if (kind == ShotTargetKind.Antlion)
        {
            DefeatAntlion(Near<Antlion>(view.gameObject));
            return;
        }

        if (kind == ShotTargetKind.Jellyfish)
        {
            SmashJellyfish(FindJellyfish(view, extra));
        }
    }

    /// <summary>
    /// Counts this pellet toward defeating a world target. True when the hit count has reached
    /// <see cref="HitsToDefeatSettings"/> for that kind (then the counter is cleared).
    /// </summary>
    private static bool RegisterWorldHitTowardDefeat(Component target, ShotTargetKind kind, PhotonView? view, int extra = 0)
    {
        int need = HostConfigSync.HitsToDefeat.For(kind);
        if (need <= 1)
        {
            return true;
        }

        int key = view != null && view.ViewID != 0
            ? view.ViewID ^ (extra * unchecked((int)0x9E3779B9))
            : unchecked((int)0x80000000) ^ target.GetInstanceID();
        worldHitCounts.TryGetValue(key, out int hits);
        hits++;
        if (hits >= need)
        {
            worldHitCounts.Remove(key);
            return true;
        }

        worldHitCounts[key] = hits;
        return false;
    }

    private static PhotonView? ViewOf(Component target)
    {
        if (target is Spider spider && spider.photonView != null)
        {
            return spider.photonView;
        }

        if (target is Condor condor && condor.view != null)
        {
            return condor.view;
        }

        if (target is Antlion antlion && antlion.view != null)
        {
            return antlion.view;
        }

        if (target is FrogTongue frogTongue)
        {
            Item? frogItem = frogTongue.mobItem != null
                ? frogTongue.mobItem
                : frogTongue.GetComponent<Item>() ?? frogTongue.GetComponentInParent<Item>();
            if (frogItem != null && frogItem.photonView != null)
            {
                return frogItem.photonView;
            }

            if (frogTongue.photonView != null)
            {
                return frogTongue.photonView;
            }
        }

        if (target is Beehive hive)
        {
            Item? hiveItem = hive.item != null ? hive.item : hive.GetComponent<Item>();
            if (hiveItem != null && hiveItem.photonView != null)
            {
                return hiveItem.photonView;
            }
        }

        if (target is Bugfix tick && tick.photonView != null)
        {
            return tick.photonView;
        }

        if (target is SlipperyJellyfish jellyfish && jellyfish.relay != null && jellyfish.relay.view != null)
        {
            return jellyfish.relay.view;
        }

        if (target is Item hanging && hanging.photonView != null)
        {
            return hanging.photonView;
        }

        return target.GetComponentInParent<PhotonView>();
    }

    private static T? Near<T>(GameObject root)
        where T : Component
    {
        T? below = root.GetComponentInChildren<T>(true);
        return below != null ? below : root.GetComponentInParent<T>();
    }

    /// <summary>Runs on the host (or offline, or for a target with no network view).</summary>
    private static void Apply(Component target, ShotTargetKind kind, PhotonView? view)
    {
        if (!Allows(kind))
        {
            return;
        }

        switch (kind)
        {
            case ShotTargetKind.Spider:
                ((Spider)target).Bonk();
                break;
            case ShotTargetKind.Beetle:
            case ShotTargetKind.Scorpion:
                KillMob((Mob)target);
                break;
            case ShotTargetKind.Frog:
                DefeatFrog((FrogTongue)target, destroy: true);
                break;
            case ShotTargetKind.Condor:
                DefeatCondor((Condor)target, destroy: true);
                break;
            case ShotTargetKind.Antlion:
                DefeatAntlion((Antlion)target);
                break;
            case ShotTargetKind.Beehive:
                SmashBeehive((Beehive)target, view);
                break;
            case ShotTargetKind.Tick:
                RemoveTick((Bugfix)target);
                break;
            case ShotTargetKind.Jellyfish:
                SmashJellyfish((SlipperyJellyfish)target);
                break;
            case ShotTargetKind.Coconut:
                DropCoconut((Item)target);
                break;
            case ShotTargetKind.Dynamite:
                ExplodeDynamite((Dynamite)target);
                break;
            case ShotTargetKind.SporeBomb:
                SmashBreakable((Breakable)target);
                break;
            case ShotTargetKind.Mandrake:
            case ShotTargetKind.SporeCloud:
                if (view != null)
                {
                    PhotonNetwork.Destroy(view.gameObject);
                }
                else
                {
                    UnityEngine.Object.Destroy(MandrakeOrEmitterRoot(target, kind));
                }

                break;
            case ShotTargetKind.Scoutmaster:
                DismissScoutmaster((Scoutmaster)target);
                break;
        }
    }

    private static GameObject MandrakeOrEmitterRoot(Component target, ShotTargetKind kind)
    {
        if (kind == ShotTargetKind.Mandrake)
        {
            Mandrake mandrake = (Mandrake)target;
            Item? item = mandrake.item != null ? mandrake.item : mandrake.GetComponent<Item>();
            if (item != null)
            {
                return item.gameObject;
            }
        }

        return target.gameObject;
    }

    /// <summary>
    /// Same path as a burnt-out fuse: <see cref="Dynamite.RPC_Explode"/> on everyone, then destroy the item.
    /// </summary>
    private static void ExplodeDynamite(Dynamite dynamite)
    {
        if (dynamite == null)
        {
            return;
        }

        PhotonView? view = dynamite.photonView != null
            ? dynamite.photonView
            : dynamite.GetComponent<PhotonView>();
        if (PhotonNetwork.InRoom && view != null && view.ViewID != 0)
        {
            view.RPC(nameof(Dynamite.RPC_Explode), RpcTarget.All);
            PhotonNetwork.Destroy(view.gameObject);
        }
        else
        {
            dynamite.RPC_Explode();
            UnityEngine.Object.Destroy(dynamite.gameObject);
        }

        Item? item = dynamite.item != null ? dynamite.item : dynamite.GetComponent<Item>();
        item?.ClearDataFromBackpack();
    }

    /// <summary>
    /// <see cref="Breakable.Break"/> expects a collision for ragdoll push / kinematic stick. A pellet has
    /// neither, so clear those flags and pass null — the bomb still pops (cloud / VFX) and despawns.
    /// </summary>
    private static void SmashBreakable(Breakable breakable)
    {
        breakable.ragdollCharacterOnBreak = false;
        breakable.spawnsItemsKinematic = false;
        breakable.Break(null!);
    }

    /// <summary>Same smash as throwing something at the hive: honeycomb spawns, then <see cref="Beehive.OnDestroy"/> angers the swarm.</summary>
    private static void SmashBeehive(Beehive hive, PhotonView? view)
    {
        if (hive == null)
        {
            return;
        }

        Item? item = hive.item != null ? hive.item : hive.GetComponent<Item>();
        Breakable? breakable = hive.GetComponent<Breakable>()
            ?? item?.GetComponent<Breakable>()
            ?? hive.GetComponentInParent<Breakable>();
        if (breakable != null)
        {
            SmashBreakable(breakable);
            return;
        }

        GameObject root = item != null ? item.gameObject : hive.gameObject;
        PhotonView? itemView = view
            ?? (item != null ? item.photonView : null)
            ?? hive.GetComponentInParent<PhotonView>();
        if (PhotonNetwork.InRoom && itemView != null && itemView.ViewID != 0)
        {
            PhotonNetwork.Destroy(itemView.gameObject);
        }
        else
        {
            UnityEngine.Object.Destroy(root);
        }
    }

    /// <summary>Owner-only destroy, same path as picking the tick off (<see cref="Bugfix.RPCA_Remove"/>).</summary>
    private static void RemoveTick(Bugfix tick)
    {
        if (tick == null)
        {
            return;
        }

        PhotonView? view = tick.photonView != null ? tick.photonView : tick.GetComponent<PhotonView>();
        if (PhotonNetwork.InRoom && view != null && view.ViewID != 0)
        {
            if (view.IsMine)
            {
                view.RPC(nameof(Bugfix.RPCA_Remove), RpcTarget.All);
            }
            else if (view.Owner != null)
            {
                view.RPC(nameof(Bugfix.RPCA_Remove), view.Owner);
            }
            else
            {
                view.RPC(nameof(Bugfix.RPCA_Remove), RpcTarget.All);
            }

            return;
        }

        UnityEngine.Object.Destroy(tick.gameObject);
    }

    /// <summary>
    /// Tree fruit is a kinematic <see cref="Item"/> (same as a berry bush). A thrown item knocks it loose
    /// through <see cref="EventOnItemCollision"/>; a pellet has no collision, so unfreeze it the same way.
    /// </summary>
    private static void DropCoconut(Item? coconut)
    {
        if (coconut == null || !IsTreeCoconut(coconut))
        {
            return;
        }

        EventOnItemCollision? knock = coconut.GetComponent<EventOnItemCollision>();
        knock?.TriggerEvent();
        if (coconut.rig != null && coconut.rig.isKinematic)
        {
            coconut.SetKinematicNetworked(false);
        }
    }

    private static bool IsTreeCoconut(Item? item)
    {
        if (item == null || !LooksLikeCoconut(item))
        {
            return false;
        }

        Rigidbody? rig = item.rig != null ? item.rig : item.GetComponent<Rigidbody>();
        return rig != null && rig.isKinematic;
    }

    private static bool LooksLikeCoconut(Item item)
    {
        string name = item.name ?? "";
        string ui = item.UIData != null ? item.UIData.itemName ?? "" : "";
        if (ContainsOrdinalIgnoreCase(name, "Half") || ContainsOrdinalIgnoreCase(ui, "Half"))
        {
            return false;
        }

        return ContainsOrdinalIgnoreCase(name, "Coconut") || ContainsOrdinalIgnoreCase(ui, "Coconut");
    }

    /// <summary>
    /// Jellyfish share one <see cref="TriggerRelay"/> Photon view. Destroy only this child so the rest of the
    /// cluster stays. Every client must run this; do not <see cref="PhotonNetwork.Destroy"/> the relay.
    /// </summary>
    private static void SmashJellyfish(SlipperyJellyfish? jellyfish)
    {
        if (jellyfish == null)
        {
            return;
        }

        jellyfish.enabled = false;
        foreach (Collider collider in jellyfish.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }

        foreach (Renderer renderer in jellyfish.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = false;
        }

        UnityEngine.Object.Destroy(jellyfish.gameObject);
    }

    private static bool TryHitCharacter(Character character, Action_Gun gun, int shotId, Vector3 hitPoint, Vector3 direction)
    {
        if (character.isScoutmaster)
        {
            return TryHitScoutmaster(character, gun, shotId);
        }

        if (GunCharacterLaunch.IsZombie(character))
        {
            if (!CanShootZombies)
            {
                return false;
            }

            GunCharacterLaunch? launch = character.GetComponent<GunCharacterLaunch>();
            if (character.photonView.IsMine)
            {
                launch?.Blast(direction, hitPoint);
            }
            else
            {
                character.photonView.RPC(
                    nameof(GunCharacterLaunch.RPC_ShotgunBlast),
                    character.photonView.Owner,
                    direction,
                    hitPoint,
                    gun.photonView.ViewID,
                    shotId);
            }

            return true;
        }

        if (!CanShootScouts)
        {
            return false;
        }

        gun.photonView.RPC(nameof(Action_Gun.RPC_GunImpact), RpcTarget.All, character.photonView.Owner, hitPoint, direction, shotId);
        return true;
    }

    /// <summary>
    /// Host counts hits toward <see cref="HitsToDefeatSettings.Scoutmaster"/>, then warps him to the hidey spot
    /// (same as vanilla <see cref="Scoutmaster.TeleportFarAway"/>) and clears aggro.
    /// </summary>
    private static bool TryHitScoutmaster(Character character, Action_Gun gun, int shotId)
    {
        if (!CanShootScoutmaster)
        {
            return false;
        }

        Scoutmaster? scoutmaster = character.GetComponent<Scoutmaster>();
        if (scoutmaster == null)
        {
            return false;
        }

        PhotonView? view = ViewOf(scoutmaster);
        if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient || view == null)
        {
            if (RegisterWorldHitTowardDefeat(scoutmaster, ShotTargetKind.Scoutmaster, view))
            {
                DismissScoutmaster(scoutmaster);
            }
        }
        else
        {
            gun.photonView.RPC(
                nameof(Action_Gun.RPC_HostShootTarget),
                RpcTarget.MasterClient,
                view.ViewID,
                (int)ShotTargetKind.Scoutmaster,
                shotId,
                0);
        }

        return true;
    }

    /// <summary>
    /// Clears the Scoutmaster's target, warps him to <see cref="Character.ScoutmasterHideySpot"/>, and chills
    /// AI so he does not immediately re-aggro.
    /// </summary>
    private static void DismissScoutmaster(Scoutmaster scoutmaster)
    {
        if (scoutmaster == null)
        {
            return;
        }

        scoutmaster.SetCurrentTarget(null, 0f);
        scoutmaster.discovered = null;
        scoutmaster.chillForSeconds = 120f;
        scoutmaster.tpCounter = 5f;
        scoutmaster.TeleportFarAway();
        Plugin.Log.LogInfo("Scoutmaster dismissed after shotgun hits (warped to hidey spot).");
    }

    /// <summary>
    /// Dead mob state does not stop a frog: <see cref="FrogTongue"/> keeps licking, and
    /// <see cref="MobItem"/> rewrites <c>mobState</c> every frame. Same as the stove:
    /// drop the tongue, destroy the frog, then spawn cooked frog legs
    /// (<see cref="CookingBehavior_ReplaceItem"/>). Destroy / spawn only on the host.
    /// </summary>
    private static void DefeatFrog(FrogTongue? frog, bool destroy)
    {
        if (frog == null)
        {
            return;
        }

        ReleaseFrogTongue(frog, networked: destroy);
        frog.enabled = false;

        Item? item = frog.mobItem != null
            ? frog.mobItem
            : frog.GetComponent<Item>() ?? frog.GetComponentInParent<Item>();
        ItemCooking? cooking = item != null
            ? item.cooking
            : frog.GetComponent<ItemCooking>() ?? frog.GetComponentInParent<ItemCooking>();
        CookingBehavior_ReplaceItem? replace = FrogLegReplace(cooking);
        Vector3 dropAt = item != null ? item.transform.position : frog.transform.position;

        if (item != null)
        {
            item.ClearDataFromBackpack();
        }

        if (!destroy)
        {
            return;
        }

        PhotonView? view = ViewOf(frog);
        GameObject root = item != null ? item.gameObject : frog.gameObject;
        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient && view != null && view.ViewID != 0)
        {
            PhotonNetwork.Destroy(view.gameObject);
        }
        else if (!PhotonNetwork.InRoom)
        {
            UnityEngine.Object.Destroy(root);
        }

        SpawnCookedFrogLegs(replace, dropAt);
    }

    private static void ReleaseFrogTongue(FrogTongue frog, bool networked)
    {
        PhotonView? view = frog.photonView;
        if (networked && PhotonNetwork.InRoom && view != null && view.ViewID != 0)
        {
            PhotonView? targetView = frog._targetCharacter != null ? frog._targetCharacter.photonView : null;
            view.RPC(nameof(FrogTongue.RPCA_FrogAction), RpcTarget.All, targetView, FrogTongue.FrogActionType.LetGo, Vector3.zero);
            return;
        }

        frog.LetGo();
    }

    private static CookingBehavior_ReplaceItem? FrogLegReplace(ItemCooking? cooking)
    {
        AdditionalCookingBehavior[]? behaviors = cooking != null ? cooking.additionalCookingBehaviors : null;
        if (behaviors == null)
        {
            return null;
        }

        for (int i = 0; i < behaviors.Length; i++)
        {
            if (behaviors[i] is CookingBehavior_ReplaceItem replace && replace.replaceWithItem != null)
            {
                return replace;
            }
        }

        return null;
    }

    private static Item? frogLegsPrefab;

    /// <summary>Stove path: instantiate frog legs, then <see cref="ItemCooking.FinishCooking"/> so they are edible.</summary>
    private static void SpawnCookedFrogLegs(CookingBehavior_ReplaceItem? replace, Vector3 position)
    {
        Item? prefab = replace != null && replace.replaceWithItem != null
            ? replace.replaceWithItem
            : FindFrogLegsPrefab();
        if (prefab == null)
        {
            Plugin.Log.LogWarning("Shot a frog but could not find the frog-legs item to drop.");
            return;
        }

        GameObject? spawned;
        if (PhotonNetwork.InRoom)
        {
            spawned = PhotonNetwork.InstantiateItemRoom(prefab.gameObject.name, position, Quaternion.identity, false);
        }
        else
        {
            spawned = UnityEngine.Object.Instantiate(prefab.gameObject, position, Quaternion.identity);
        }

        if (spawned == null)
        {
            Plugin.Log.LogWarning($"Could not spawn frog legs from '{prefab.gameObject.name}'.");
            return;
        }

        Item? legs = spawned.GetComponent<Item>();
        ItemCooking? cooking = legs != null ? legs.cooking : spawned.GetComponent<ItemCooking>();
        bool cook = replace == null ? cooking != null && cooking.canBeCooked && cooking.preCooked <= 0 : replace.cookNewItem;
        if (cooking != null && cook)
        {
            cooking.FinishCooking();
        }
    }

    private static Item? FindFrogLegsPrefab()
    {
        if (frogLegsPrefab != null)
        {
            return frogLegsPrefab;
        }

        ItemDatabase? database = SingletonAsset<ItemDatabase>.Instance;
        if (database?.Objects == null)
        {
            return null;
        }

        Item? cooked = null;
        for (int i = 0; i < database.Objects.Count; i++)
        {
            Item candidate = database.Objects[i];
            if (candidate == null || !LooksLikeFrogLegs(candidate))
            {
                continue;
            }

            ItemCooking? cooking = candidate.cooking;
            bool alreadyCooked = cooking != null && (cooking.preCooked > 0 || cooking.timesCookedLocal > 0);
            if (!alreadyCooked)
            {
                frogLegsPrefab = candidate;
                return candidate;
            }

            cooked ??= candidate;
        }

        frogLegsPrefab = cooked;
        return cooked;
    }

    private static bool LooksLikeFrogLegs(Item item)
    {
        string name = item.name ?? "";
        string ui = item.UIData != null ? item.UIData.itemName ?? "" : "";
        return ContainsOrdinalIgnoreCase(name, "FrogLeg")
            || ContainsOrdinalIgnoreCase(name, "Frog_Leg")
            || ContainsOrdinalIgnoreCase(name, "Frog Leg")
            || ContainsOrdinalIgnoreCase(ui, "Frog Leg")
            || ContainsOrdinalIgnoreCase(ui, "FrogLeg");
    }

    /// <summary>
    /// Drops a carried scout, then removes the bird. Destroy only on the host so Photon replicates it;
    /// other clients hide the local instance until that arrives.
    /// </summary>
    private static void DefeatCondor(Condor? condor, bool destroy)
    {
        ReleaseCondorCarries(condor);
        if (condor != null)
        {
            condor.GiveUp();
            condor.enabled = false;
            if (condor.carryTrigger != null)
            {
                condor.carryTrigger.SetActive(false);
            }

            if (condor.rb != null)
            {
                condor.rb.linearVelocity = Vector3.zero;
                condor.rb.angularVelocity = Vector3.zero;
                condor.rb.isKinematic = true;
            }

            if (condor.anim != null)
            {
                condor.anim.enabled = false;
            }

            foreach (Renderer renderer in condor.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = false;
            }

            foreach (Collider collider in condor.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
        }

        if (!destroy || condor == null)
        {
            return;
        }

        PhotonView? view = condor.view != null ? condor.view : condor.GetComponent<PhotonView>();
        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient && view != null && view.ViewID != 0)
        {
            PhotonNetwork.Destroy(view.gameObject);
        }
        else if (!PhotonNetwork.InRoom)
        {
            UnityEngine.Object.Destroy(condor.gameObject);
        }
    }

    private static void ReleaseCondorCarries(Condor? condor)
    {
        foreach (Character character in Character.AllCharacters)
        {
            CharacterData? data = character != null ? character.data : null;
            Condor? carried = data != null ? data.carrierCondor : null;
            if (data == null || (object?)carried == null)
            {
                continue;
            }

            bool destroyed = carried == null;
            if (destroyed || carried == condor)
            {
                data.carrierCondor = null;
            }
        }
    }

    /// <summary>
    /// Stops the pit AI and hides the head. Leaves the sand funnel in place so the terrain does not vanish.
    /// </summary>
    private static void DefeatAntlion(Antlion? antlion)
    {
        if (antlion == null)
        {
            return;
        }

        antlion.DoInactive();
        antlion.enabled = false;
        if (antlion.head != null)
        {
            antlion.head.SetActive(false);
        }

        if (antlion.slideObj != null)
        {
            antlion.slideObj.SetActive(false);
        }

        if (antlion.anim != null)
        {
            antlion.anim.enabled = false;
        }
    }

    /// <summary>
    /// Mob state is owned by the mob's owner (its setter only syncs from there), so a host that does not own
    /// the mob asks the owner to switch it to Dead.
    /// </summary>
    private static void KillMob(Mob mob)
    {
        if (mob == null)
        {
            return;
        }

        PhotonView? view = mob.photonView;
        bool networked = PhotonNetwork.InRoom && view != null && view.ViewID != 0;
        if (networked && !view!.IsMine)
        {
            // Dead = 3 on Mob.MobState; mirrors cooking a scorpion/beetle.
            if (view.Owner != null)
            {
                view.RPC("RPC_SyncMobState", view.Owner, 3);
            }
            else
            {
                view.RPC("RPC_SyncMobState", RpcTarget.All, 3);
            }

            return;
        }

        EnsureMobDeadCache();
        if (mobStateProperty != null && mobStateDead != null)
        {
            try
            {
                // On the owner the setter also sends RPC_SyncMobState to everyone else.
                mobStateProperty.SetValue(mob, mobStateDead);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not kill mob '{mob.name}': {e.Message}");
            }
        }

        Rigidbody? rig = mob.GetComponent<Rigidbody>();
        if (rig != null && !rig.isKinematic)
        {
            rig.AddForce(Vector3.up * 8f + UnityEngine.Random.insideUnitSphere * 4f, ForceMode.VelocityChange);
        }
    }

    private static void EnsureMobDeadCache()
    {
        if (mobStateProperty != null)
        {
            return;
        }

        Type mobType = typeof(Mob);
        mobStateProperty = mobType.GetProperty("mobState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Type? stateType = mobType.GetNestedType("MobState", BindingFlags.Public | BindingFlags.NonPublic);
        if (stateType != null)
        {
            mobStateDead = Enum.Parse(stateType, "Dead");
        }
    }
}
