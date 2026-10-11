using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using Peak.Afflictions;
using Photon.Pun;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// Shots needed to defeat each non-scout target. Scouts stay damage-per-shot and are not listed here.
/// Packed as a 7-byte array for the host room property (order is fixed — do not reorder).
/// </summary>
internal readonly struct HitsToDefeatSettings
{
    internal const int PackedLength = 7;

    internal const int MaxHits = 20;

    internal HitsToDefeatSettings(
        int zombies,
        int mandrake,
        int beetles,
        int spiders,
        int spores,
        int scorpions,
        int dynamite)
    {
        Zombies = Clamp(zombies);
        Mandrake = Clamp(mandrake);
        Beetles = Clamp(beetles);
        Spiders = Clamp(spiders);
        Spores = Clamp(spores);
        Scorpions = Clamp(scorpions);
        Dynamite = Clamp(dynamite);
    }

    internal int Zombies { get; }

    internal int Mandrake { get; }

    internal int Beetles { get; }

    internal int Spiders { get; }

    internal int Spores { get; }

    internal int Scorpions { get; }

    internal int Dynamite { get; }

    internal static HitsToDefeatSettings Defaults { get; } = new(2, 1, 1, 1, 1, 1, 1);

    internal int For(ShotTargetKind kind) => kind switch
    {
        ShotTargetKind.Spider => Spiders,
        ShotTargetKind.Beetle => Beetles,
        ShotTargetKind.Scorpion => Scorpions,
        ShotTargetKind.Mandrake => Mandrake,
        ShotTargetKind.Dynamite => Dynamite,
        ShotTargetKind.SporeBomb or ShotTargetKind.SporeCloud => Spores,
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
    ];

    internal static HitsToDefeatSettings FromPacked(byte[]? packed)
    {
        if (packed == null || packed.Length < PackedLength)
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
            packed[6]);
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
}

/// <summary>Non-character targets, as sent to the host in <see cref="Action_Gun.RPC_HostShootTarget"/>.</summary>
internal enum ShotTargetKind
{
    Spider = 1,
    Beetle = 2,
    Scorpion = 3,
    Mandrake = 4,
    Dynamite = 5,
    SporeBomb = 6,
    SporeCloud = 7,
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

    private static ConfigEntry<int>? hitsZombies;
    private static ConfigEntry<int>? hitsMandrake;
    private static ConfigEntry<int>? hitsBeetles;
    private static ConfigEntry<int>? hitsSpiders;
    private static ConfigEntry<int>? hitsSpores;
    private static ConfigEntry<int>? hitsScorpions;
    private static ConfigEntry<int>? hitsDynamite;

    /// <summary>Host-side (or offline) hit counts toward defeat for world targets, keyed by Photon view id or local instance id.</summary>
    private static readonly Dictionary<int, int> worldHitCounts = new();

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
        | (shootDynamite?.Value ?? true ? ShootTargets.Dynamite : ShootTargets.None);

    /// <summary>
    /// True when Zombies is the only enabled shootable. That is the only shootables mix that keeps
    /// luggage Roots-exclusive (unless <c>CanSpawnOnAnyBiome</c> forces every biome).
    /// </summary>
    internal static bool IsZombiesOnlyShootables => LocalShootables == ShootTargets.Zombies;

    internal static bool CanShootScouts => Allows(ShootTargets.Scouts);

    internal static bool CanShootZombies => Allows(ShootTargets.Zombies);

    internal static bool CanShootMandrake => Allows(ShootTargets.Mandrake);

    internal static bool CanShootBeetles => Allows(ShootTargets.Beetles);

    internal static bool CanShootSpiders => Allows(ShootTargets.Spiders);

    internal static bool CanShootSpores => Allows(ShootTargets.Spores);

    internal static bool CanShootScorpions => Allows(ShootTargets.Scorpions);

    internal static bool CanShootDynamite => Allows(ShootTargets.Dynamite);

    /// <summary>This client's own <c>[HitsToDefeat]</c> config (the host publishes it; clients use the host's).</summary>
    internal static HitsToDefeatSettings LocalHitsToDefeat => new(
        hitsZombies?.Value ?? 2,
        hitsMandrake?.Value ?? 1,
        hitsBeetles?.Value ?? 1,
        hitsSpiders?.Value ?? 1,
        hitsSpores?.Value ?? 1,
        hitsScorpions?.Value ?? 1,
        hitsDynamite?.Value ?? 1);

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

        ConfigDescription hitsDesc(string what) => new(
            $"Shots needed to {what}. Host's value is used in multiplayer. Scouts are not listed — they take Injury every hit.",
            new AcceptableValueRange<int>(1, HitsToDefeatSettings.MaxHits));

        hitsZombies = config.Bind("HitsToDefeat", "Zombies", 2, hitsDesc("knock down a zombie"));
        hitsMandrake = config.Bind("HitsToDefeat", "Mandrake", 1, hitsDesc("destroy a mandrake"));
        hitsBeetles = config.Bind("HitsToDefeat", "Beetles", 1, hitsDesc("kill a beetle"));
        hitsSpiders = config.Bind("HitsToDefeat", "Spiders", 1, hitsDesc("stun a spider"));
        hitsSpores = config.Bind("HitsToDefeat", "Spores", 1, hitsDesc("break a spore bomb or clear a spore cloud"));
        hitsScorpions = config.Bind("HitsToDefeat", "Scorpions", 1, hitsDesc("kill a scorpion"));
        hitsDynamite = config.Bind("HitsToDefeat", "Dynamite", 1, hitsDesc("explode dynamite"));
    }

    /// <summary>Clears host/offline world-target hit progress (map change / leave room).</summary>
    internal static void ClearWorldHitCounts() => worldHitCounts.Clear();

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

        Character? character = collider.GetComponentInParent<Character>();
        if (character != null && character != shooter)
        {
            return TryHitCharacter(character, gun, shotId, hitPoint, direction);
        }

        if (!TryClassify(collider, out Component? target, out ShotTargetKind kind))
        {
            return false;
        }

        PhotonView? view = ViewOf(target);
        if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient || view == null)
        {
            if (RegisterWorldHitTowardDefeat(target, kind, view))
            {
                Apply(target, kind, view);
            }
        }
        else
        {
            gun.photonView.RPC(nameof(Action_Gun.RPC_HostShootTarget), RpcTarget.MasterClient, view.ViewID, (int)kind, shotId);
        }

        return true;
    }

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
    internal static void HostApply(PhotonView view, ShotTargetKind kind)
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
            _ => null,
        };

        // The view must be the target's own, so a forged id cannot destroy some larger object that merely
        // contains a mandrake or spore emitter.
        if (target != null && ViewOf(target) == view && RegisterWorldHitTowardDefeat(target, kind, view))
        {
            Apply(target, kind, view);
        }
    }

    /// <summary>
    /// Counts this pellet toward defeating a world target. True when the hit count has reached
    /// <see cref="HitsToDefeatSettings"/> for that kind (then the counter is cleared).
    /// </summary>
    private static bool RegisterWorldHitTowardDefeat(Component target, ShotTargetKind kind, PhotonView? view)
    {
        int need = HostConfigSync.HitsToDefeat.For(kind);
        if (need <= 1)
        {
            return true;
        }

        int key = view != null && view.ViewID != 0
            ? view.ViewID
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

    private static PhotonView? ViewOf(Component target) =>
        target is Spider spider && spider.photonView != null
            ? spider.photonView
            : target.GetComponentInParent<PhotonView>();

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
            case ShotTargetKind.Dynamite:
                ExplodeDynamite((Dynamite)target);
                break;
            case ShotTargetKind.SporeBomb:
                BreakSporeBomb((Breakable)target);
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
    private static void BreakSporeBomb(Breakable breakable)
    {
        breakable.ragdollCharacterOnBreak = false;
        breakable.spawnsItemsKinematic = false;
        breakable.Break(null!);
    }

    private static bool TryHitCharacter(Character character, Action_Gun gun, int shotId, Vector3 hitPoint, Vector3 direction)
    {
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
