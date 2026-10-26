using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// Hit resolution for what the shotgun can affect (creatures, hazards, other scouts). The rules live in
/// <see cref="ShootableRules"/> and each world target in its own <see cref="ShotTarget"/>; this class owns the
/// shared flow. In multiplayer the rules come from the host (<see cref="HostConfigSync"/>), not from each
/// client's config, and the host applies every non-character hit after re-checking it.
/// </summary>
internal static class ShotgunCombat
{
    /// <summary>Host-side (or offline) hit counts toward defeat for world targets, keyed by Photon view id or local instance id.</summary>
    private static readonly Dictionary<int, int> worldHitCounts = new();

    /// <summary>Defeats that every client applies itself (<see cref="ShotTarget.BroadcastsDefeat"/>), replayed to late joiners via a room property.</summary>
    private static readonly Dictionary<long, ShotTargetKind> worldDefeats = new();

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

        // A layer outside Character and Default is only in the pellet mask because some target asked for it
        // (ShotTarget.ExtraLayerMask): offer it to those targets only, never as a scout.
        int layerBit = 1 << collider.gameObject.layer;
        if ((HelperFunctions.CharacterAndDefaultMask.value & layerBit) == 0)
        {
            return TryHitStage(ShotStage.World, collider, gun, shotId, layerBit);
        }

        if (TryHitStage(ShotStage.BeforeCharacters, collider, gun, shotId))
        {
            return true;
        }

        Character? character = collider.GetComponentInParent<Character>();
        if (character != null && character != shooter)
        {
            return TryHitCharacter(character, collider, gun, shotId, hitPoint, direction);
        }

        return TryHitStage(ShotStage.World, collider, gun, shotId);
    }

    /// <summary>First enabled target of that stage under the collider, in <see cref="ShotTargetRegistry"/> order.</summary>
    private static bool TryHitStage(ShotStage stage, Collider collider, Action_Gun gun, int shotId, int extraLayerBit = 0)
    {
        foreach (ShotTarget target in ShotTargetRegistry.All)
        {
            if (target.Stage != stage || target.Broken || !target.Rule.Enabled)
            {
                continue;
            }

            if (extraLayerBit != 0 && (target.ExtraLayerMask & extraLayerBit) == 0)
            {
                continue;
            }

            Component? found = ShotTargetRegistry.Classify(target, collider);
            if (found != null)
            {
                HitWorldTarget(target, found, gun, shotId);
                return true;
            }
        }

        return false;
    }

    private static void HitWorldTarget(ShotTarget target, Component component, Action_Gun gun, int shotId)
    {
        PhotonView? view = ShotTargetRegistry.ViewOf(target, component);
        int extra = ShotTargetRegistry.ExtraOf(target, component);
        if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient || view == null)
        {
            ApplyRegisteredHit(target, component, view, gun, extra);
            return;
        }

        gun.photonView.RPC(
            nameof(Action_Gun.RPC_HostShootTarget),
            RpcTarget.MasterClient,
            view.ViewID,
            (int)target.Kind,
            shotId,
            extra);
    }

    /// <summary>
    /// Host side of <see cref="Action_Gun.RPC_HostShootTarget"/>: find the claimed target under the view and apply
    /// it only if it really is that kind of target and the room allows shooting it.
    /// </summary>
    internal static void HostApply(PhotonView view, ShotTargetKind kind, Action_Gun? gun = null, int extra = 0)
    {
        ShotTarget? target = ShotTargetRegistry.For(kind);
        if (target == null || !target.Rule.Enabled)
        {
            return;
        }

        Component? component = ShotTargetRegistry.FindFromView(target, view, extra);

        // The view must be the target's own, so a forged id cannot destroy some larger object that merely
        // contains a mandrake or spore emitter.
        if (component != null && ShotTargetRegistry.ViewOf(target, component) == view)
        {
            ApplyRegisteredHit(target, component, view, gun, extra);
        }
    }

    /// <summary>Runs on the host (or offline, or for a target with no network view).</summary>
    private static void ApplyRegisteredHit(ShotTarget target, Component component, PhotonView? view, Action_Gun? gun, int extra)
    {
        if (!target.Rule.Enabled || !RegisterWorldHitTowardDefeat(target, component, view, extra))
        {
            return;
        }

        if (ShotTargetRegistry.Defeat(target, component, new ShotHit(view, gun, extra)))
        {
            ReplicateWorldDefeat(target, gun, view, extra);
        }
    }

    private static void ReplicateWorldDefeat(ShotTarget target, Action_Gun? gun, PhotonView? view, int extra)
    {
        if (!target.BroadcastsDefeat)
        {
            return;
        }

        if (view != null && view.ViewID != 0)
        {
            worldDefeats[DefeatKey(view.ViewID, extra)] = target.Kind;
        }

        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
        {
            return;
        }

        HostConfigSync.PublishWorldDefeats();
        if (gun != null && view != null)
        {
            gun.photonView.RPC(nameof(Action_Gun.RPC_ApplyWorldDefeat), RpcTarget.Others, view.ViewID, (int)target.Kind, extra);
        }
    }

    /// <summary>
    /// Host or offline, from <see cref="ShotTarget.Defeat"/>: runs <see cref="ShotTarget.Effect"/> on every client
    /// (<see cref="Action_Gun.RPC_ShotEffect"/>). Not replayed to late joiners.
    /// </summary>
    internal static void BroadcastEffect(ShotTarget target, Component component, ShotHit hit, Vector3 push, int variant = 0)
    {
        if (PhotonNetwork.InRoom && hit.Gun != null && hit.View != null && hit.View.ViewID != 0)
        {
            hit.Gun.photonView.RPC(
                nameof(Action_Gun.RPC_ShotEffect),
                RpcTarget.All,
                hit.View.ViewID,
                (int)target.Kind,
                hit.Extra,
                push,
                variant);
            return;
        }

        ShotTargetRegistry.Effect(target, component, push, variant);
    }

    /// <summary>Every client: the host's <see cref="BroadcastEffect"/> arrived.</summary>
    internal static void ApplyEffect(PhotonView? view, ShotTargetKind kind, int extra, Vector3 push, int variant)
    {
        ShotTarget? target = ShotTargetRegistry.For(kind);
        if (target == null || view == null)
        {
            return;
        }

        Component? component = ShotTargetRegistry.FindFromView(target, view, extra);
        if (component != null)
        {
            ShotTargetRegistry.Effect(target, component, push, variant);
        }
    }

    /// <summary>Other clients / late joiners: apply a host-authoritative defeat that has no vanilla RPC.</summary>
    internal static void ClientApplyDefeat(PhotonView? view, ShotTargetKind kind, int extra = 0)
    {
        ShotTarget? target = ShotTargetRegistry.For(kind);
        if (target != null && target.BroadcastsDefeat)
        {
            ShotTargetRegistry.ClientDefeat(target, view, extra);
        }
    }

    /// <summary>
    /// Counts this pellet toward defeating a world target. True when the hit count has reached
    /// <see cref="ShootableRule.HitsToDefeat"/> (then the counter is cleared).
    /// </summary>
    private static bool RegisterWorldHitTowardDefeat(ShotTarget target, Component component, PhotonView? view, int extra)
    {
        int need = target.Rule.HitsToDefeat;
        if (need <= 1)
        {
            return true;
        }

        int key = view != null && view.ViewID != 0
            ? view.ViewID ^ (extra * unchecked((int)0x9E3779B9))
            : unchecked((int)0x80000000) ^ component.GetInstanceID();
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

    private static bool TryHitCharacter(
        Character character,
        Collider collider,
        Action_Gun gun,
        int shotId,
        Vector3 hitPoint,
        Vector3 direction)
    {
        // Never fall through to scout damage: the Scoutmaster's owner is the host, who would take the Injury.
        if (character.isScoutmaster)
        {
            return TryHitStage(ShotStage.Scoutmaster, collider, gun, shotId);
        }

        if (GunCharacterLaunch.IsZombie(character))
        {
            if (!ShootableRules.Zombies.Enabled)
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

        if (!ShootableRules.Scouts.Enabled)
        {
            return false;
        }

        gun.photonView.RPC(nameof(Action_Gun.RPC_GunImpact), RpcTarget.All, character.photonView.Owner, hitPoint, direction, shotId);
        return true;
    }
}
