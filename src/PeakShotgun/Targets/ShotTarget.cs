using System;
using Photon.Pun;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// Non-player targets, as sent to the host in <see cref="Action_Gun.RPC_HostShootTarget"/>.
/// Wire format: never renumber, only append.
/// </summary>
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
    Tumbleweed = 16,
    BeeSwarm = 17,
    BingBong = 18,
    Balloon = 19,
    TiedBalloon = 20,
}

/// <summary>What the host knows about a hit when it defeats a target.</summary>
internal readonly struct ShotHit
{
    internal ShotHit(PhotonView? view, Action_Gun? gun, int extra)
    {
        View = view;
        Gun = gun;
        Extra = extra;
    }

    /// <summary>The target's own Photon view (null for a target without one).</summary>
    internal PhotonView? View { get; }

    /// <summary>The shooter's gun as this client simulates it; null when unknown.</summary>
    internal Action_Gun? Gun { get; }

    internal int Extra { get; }

    /// <summary>Unit vector from the shooter's muzzle to <paramref name="point"/>; straight up when the shooter is unknown.</summary>
    internal Vector3 DirectionTo(Vector3 point)
    {
        if (Gun == null)
        {
            return Vector3.up;
        }

        Vector3 from = Gun.spawnTransform != null ? Gun.spawnTransform.position : Gun.transform.position;
        Vector3 direction = point - from;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.up;
    }
}

/// <summary>When <see cref="ShotgunCombat.TryHit"/> asks a target to classify a pellet's collider.</summary>
internal enum ShotStage
{
    /// <summary>Before the scout under the collider (a tick sits on a scout).</summary>
    BeforeCharacters,

    /// <summary>Only for a character with <c>isScoutmaster</c>; never falls through to scout damage.</summary>
    Scoutmaster,

    /// <summary>Anything that is not a character.</summary>
    World,
}

/// <summary>
/// One kind of shootable world target: how a pellet finds it, how the host re-checks the claim, and how it
/// is defeated. <see cref="ShotgunCombat"/> owns the shared flow (host authority, hit counts, replay to late
/// joiners); a subclass only knows its own game type. Register new ones in <see cref="ShotTargetRegistry"/>.
/// </summary>
internal abstract class ShotTarget
{
    internal abstract ShotTargetKind Kind { get; }

    /// <summary>Toggle and hit count from <c>[Shootables]</c> / <c>[HitsToDefeat]</c>.</summary>
    internal abstract ShootableRule Rule { get; }

    internal virtual ShotStage Stage => ShotStage.World;

    /// <summary>
    /// True when no vanilla RPC carries the defeat: the host then broadcasts it (<see cref="Action_Gun.RPC_ApplyWorldDefeat"/>
    /// and room property <c>PeakShotgun_WorldDown</c>) and every other client runs <see cref="ClientDefeat"/>.
    /// </summary>
    internal virtual bool BroadcastsDefeat => false;

    /// <summary>
    /// Layers outside Character and Default that pellets must also hit to find this target (the bee swarm's body is
    /// on GhostCharacter). Colliders on these layers are only offered to targets that ask for that layer.
    /// </summary>
    internal virtual int ExtraLayerMask => 0;

    /// <summary>Set when a PEAK type or member this target uses is missing (<see cref="GameCompat.IsGameMismatch"/>); the target is then skipped.</summary>
    internal bool Broken { get; private set; }

    /// <summary>Shooter side: the target under this pellet's collider, or null.</summary>
    internal abstract Component? Classify(Collider collider);

    /// <summary>Host side: the claimed target under its own Photon view, or null.</summary>
    internal abstract Component? FindFromView(PhotonView view, int extra);

    internal abstract PhotonView? ViewOf(Component target);

    /// <summary>Extra id sent with the view id when several targets share one view (jellyfish).</summary>
    internal virtual int ExtraOf(Component target) => 0;

    /// <summary>Host or offline: the hit count was reached.</summary>
    internal abstract void Defeat(Component target, ShotHit hit);

    /// <summary>
    /// Every client, when <see cref="Defeat"/> called <see cref="ShotgunCombat.BroadcastEffect"/>: short-lived
    /// feedback (a push, a voice line) that late joiners do not need.
    /// </summary>
    internal virtual void Effect(Component target, Vector3 push, int variant)
    {
    }

    /// <summary>Other clients and late joiners, only when <see cref="BroadcastsDefeat"/>.</summary>
    internal virtual void ClientDefeat(PhotonView? view, int extra)
    {
    }

    internal void MarkBroken(string step, Exception e)
    {
        if (Broken)
        {
            return;
        }

        Broken = true;
        Plugin.Log.LogWarning(
            $"Shooting {Kind} is turned off for this session: {step} failed ({e.GetType().Name}: {e.Message}). A PEAK update may have changed it.");
    }

    protected static T? Near<T>(GameObject root)
        where T : Component
    {
        T? below = root.GetComponentInChildren<T>(true);
        return below != null ? below : root.GetComponentInParent<T>();
    }

    protected static bool ContainsOrdinalIgnoreCase(string haystack, string needle) =>
        haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>
    /// <see cref="Breakable.Break"/> expects a collision for ragdoll push / kinematic stick. A pellet has
    /// neither, so clear those flags and pass null — the bomb still pops (cloud / VFX) and despawns.
    /// </summary>
    protected static void SmashBreakable(Breakable breakable)
    {
        breakable.ragdollCharacterOnBreak = false;
        breakable.spawnsItemsKinematic = false;
        breakable.Break(null!);
    }

    /// <summary>Host destroys through Photon so it replicates; offline destroys locally.</summary>
    protected static void DestroyNetworked(PhotonView? view, GameObject fallback)
    {
        if (view != null)
        {
            PhotonNetwork.Destroy(view.gameObject);
        }
        else
        {
            UnityEngine.Object.Destroy(fallback);
        }
    }
}

/// <summary><see cref="ShotTarget"/> for one component type: casts once, finds it with GetComponentInParent / <c>Near</c>.</summary>
internal abstract class ShotTarget<T> : ShotTarget
    where T : Component
{
    internal override Component? Classify(Collider collider) => collider.GetComponentInParent<T>();

    internal override Component? FindFromView(PhotonView view, int extra) => Near<T>(view.gameObject);

    internal sealed override PhotonView? ViewOf(Component target) =>
        target is T typed ? ViewOf(typed) : target.GetComponentInParent<PhotonView>();

    internal sealed override void Defeat(Component target, ShotHit hit) => Defeat((T)target, hit);

    internal sealed override void Effect(Component target, Vector3 push, int variant)
    {
        if (target is T typed)
        {
            Effect(typed, push, variant);
        }
    }

    protected virtual PhotonView? ViewOf(T target) => target.GetComponentInParent<PhotonView>();

    protected abstract void Defeat(T target, ShotHit hit);

    protected virtual void Effect(T target, Vector3 push, int variant)
    {
    }
}
