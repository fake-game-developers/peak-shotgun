using System.Collections.Generic;
using Peak.Afflictions;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace Peak.Shotgun;

public class Action_Gun : ItemAction
{
    public float maxDistance = 40f;

    public float pelletRadius = 0.2f;

    public float fireRate = 0.85f;

    public float spread = 0.07f;

    public int pelletCount = 8;

    [SerializeReference]
    public Affliction[] afflictionsOnHit = [];

    public Transform? spawnTransform;

    public SFX_Instance? shotSFX;

    public System.Action? OnShoot;

    // An unanswered request is re-sent with the same id after this long (message lost, host changed).
    private const float PendingShotRetry = 1.5f;

    // Requests sent fireRate apart can arrive bunched up by network jitter.
    private const float HostFireRateTolerance = 0.9f;

    // How long an accepted shot id stays valid for hit RPCs.
    private const float AuthorizedShotLifetime = 10f;

    private static int nextShotId;

    private float lastShootTime = float.NegativeInfinity;

    private int pendingShotId;

    private float pendingShotSentTime;

    private Vector3 pendingForward;

    // Host-side: newest shot id per sender (actor number) and the answer given, so a retry gets the same answer.
    private readonly Dictionary<int, (int shotId, bool accepted)> lastShotBySender = new();

    private double lastAcceptedShotTime = double.NegativeInfinity;

    // Every client: shots the host accepted (shooter actor + shot id) and which targets each one already hit.
    private readonly Dictionary<long, float> authorizedShots = new();

    private readonly Dictionary<long, HashSet<int>> consumedTargets = new();

    /// <summary>
    /// Asks the host to authorize one shot. Nothing happens locally until the host accepts it, and only one
    /// request is outstanding at a time, so the last round cannot be spent twice. An unanswered request is
    /// retried with the same id; the host answers a retry with its original decision.
    /// </summary>
    public override void RunAction()
    {
        if (pendingShotId != 0)
        {
            if (Time.time >= pendingShotSentTime + PendingShotRetry)
            {
                pendingForward = MainCamera.instance.transform.forward;
                SendShotRequest();
            }

            return;
        }

        if (Time.time <= lastShootTime + fireRate || !HasAmmo() || spawnTransform == null)
        {
            return;
        }

        lastShootTime = Time.time;
        pendingShotId = ++nextShotId;
        pendingForward = MainCamera.instance.transform.forward;
        SendShotRequest();
    }

    private void SendShotRequest()
    {
        pendingShotSentTime = Time.time;
        if (!PhotonNetwork.InRoom)
        {
            HandleShotRequest(pendingShotId, null, Time.timeAsDouble);
            return;
        }

        photonView.RPC(nameof(RPC_RequestShot), RpcTarget.MasterClient, pendingShotId);
    }

    [PunRPC]
    public void RPC_RequestShot(int shotId, PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            return;
        }

        HandleShotRequest(shotId, info.Sender, info.SentServerTime);
    }

    /// <summary>Host (or offline): validate holder, cooldown and ammo, spend once, and announce the result.</summary>
    private void HandleShotRequest(int shotId, Photon.Realtime.Player? sender, double sentTime)
    {
        int senderId = sender?.ActorNumber ?? 0;
        if (lastShotBySender.TryGetValue(senderId, out (int shotId, bool accepted) last))
        {
            if (shotId < last.shotId)
            {
                return;
            }

            if (shotId == last.shotId)
            {
                SendShotResult(senderId, shotId, last.accepted);
                return;
            }
        }

        double sinceLastShot = sentTime - lastAcceptedShotTime;
        // A large negative gap means the server clock wrapped; do not lock the gun forever.
        bool cooledDown = sinceLastShot >= fireRate * HostFireRateTolerance || sinceLastShot < -60.0;
        Action_Ammo? ammo = GetComponent<Action_Ammo>();
        bool accepted = IsHeldBy(sender) && cooledDown && ammo != null && ammo.TrySpendOne();
        if (accepted)
        {
            lastAcceptedShotTime = sentTime;
        }

        lastShotBySender[senderId] = (shotId, accepted);
        SendShotResult(senderId, shotId, accepted);
    }

    private bool IsHeldBy(Photon.Realtime.Player? sender)
    {
        Character? holder = item.holderCharacter;
        if (item.itemState != ItemState.Held || holder == null)
        {
            return false;
        }

        return sender == null || (holder.photonView != null && holder.photonView.Owner == sender);
    }

    /// <summary>
    /// Accepted shots go to everyone, so each client can check that a hit RPC belongs to a shot the host paid
    /// for. Rejections only matter to the shooter.
    /// </summary>
    private void SendShotResult(int shooterActor, int shotId, bool accepted)
    {
        if (!PhotonNetwork.InRoom)
        {
            OnShotResult(shotId, accepted);
            return;
        }

        if (accepted)
        {
            photonView.RPC(nameof(RPC_ShotResult), RpcTarget.All, shooterActor, shotId, true);
            return;
        }

        Photon.Realtime.Player? shooter = PhotonNetwork.CurrentRoom?.GetPlayer(shooterActor);
        if (shooter != null)
        {
            photonView.RPC(nameof(RPC_ShotResult), shooter, shooterActor, shotId, false);
        }
    }

    [PunRPC]
    public void RPC_ShotResult(int shooterActor, int shotId, bool accepted, PhotonMessageInfo info)
    {
        if (info.Sender == null || !info.Sender.IsMasterClient)
        {
            return;
        }

        if (accepted)
        {
            RecordAuthorizedShot(shooterActor, shotId);
        }

        if (PhotonNetwork.LocalPlayer != null && shooterActor == PhotonNetwork.LocalPlayer.ActorNumber)
        {
            OnShotResult(shotId, accepted);
        }
    }

    private void OnShotResult(int shotId, bool accepted)
    {
        if (shotId != pendingShotId)
        {
            return;
        }

        pendingShotId = 0;
        if (accepted)
        {
            Fire(shotId, pendingForward);
        }
    }

    private static long ShotKey(int shooterActor, int shotId) => ((long)shooterActor << 32) | (uint)shotId;

    private void RecordAuthorizedShot(int shooterActor, int shotId)
    {
        float now = Time.time;
        var expired = new List<long>();
        foreach (KeyValuePair<long, float> shot in authorizedShots)
        {
            if (now - shot.Value > AuthorizedShotLifetime)
            {
                expired.Add(shot.Key);
            }
        }

        foreach (long key in expired)
        {
            authorizedShots.Remove(key);
            consumedTargets.Remove(key);
        }

        authorizedShots[ShotKey(shooterActor, shotId)] = now;
    }

    /// <summary>
    /// Receiving end of a hit: true once per target for each shot the host accepted from that sender.
    /// Offline there is nothing to check.
    /// </summary>
    internal bool TryConsumeShot(Photon.Realtime.Player? sender, int shotId, int targetId)
    {
        if (!PhotonNetwork.InRoom)
        {
            return true;
        }

        if (sender == null)
        {
            return false;
        }

        long key = ShotKey(sender.ActorNumber, shotId);
        if (!authorizedShots.ContainsKey(key))
        {
            return false;
        }

        if (!consumedTargets.TryGetValue(key, out HashSet<int> targets))
        {
            targets = new HashSet<int>();
            consumedTargets[key] = targets;
        }

        return targets.Add(targetId);
    }

    private bool HasAmmo()
    {
        // Do not GetData before the entry exists — that registers an empty OptionableInt with
        // HasData=false, which used to be treated as infinite ammo.
        if (!item.HasData(DataEntryKey.ItemUses))
        {
            return item.totalUses <= 0;
        }

        OptionableIntItemData data = item.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
        if (!data.HasData)
        {
            return item.totalUses <= 0;
        }

        return data.Value < 0 || data.Value > 0;
    }

    /// <summary>Runs on the shooter once the host has accepted the shot and spent the round.</summary>
    private void Fire(int shotId, Vector3 forward)
    {
        // The round is already spent; a gun put away before the answer arrived just loses the shot.
        if (spawnTransform == null || character == null || !character.IsLocal)
        {
            return;
        }

        OnShoot?.Invoke();

        Vector3 origin = spawnTransform.position;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        if (right.sqrMagnitude < 0.001f)
        {
            right = Vector3.right;
        }

        right.Normalize();
        Vector3 up = Vector3.Cross(forward, right);

        // Bodypart forces are applied once per physics step, so this acceleration adds exactly Recoil m/s.
        if (Plugin.Recoil > 0f)
        {
            character.AddForce(-forward * (Plugin.Recoil / Time.fixedDeltaTime));
        }

        var struck = new HashSet<int>();

        for (int i = 0; i < pelletCount; i++)
        {
            Vector2 offset = PelletOffset(i, pelletCount, spread);
            Vector3 direction = (forward + right * offset.x + up * offset.y).normalized;
            TryPellet(shotId, origin, direction, struck);
        }

        photonView.RPC(nameof(RPC_ShotgunBlastFX), RpcTarget.All, origin, forward);
    }

    private void TryPellet(int shotId, Vector3 origin, Vector3 direction, HashSet<int> struck)
    {
        float distance = maxDistance;
        if (Physics.Raycast(origin, direction, out RaycastHit lineHit, maxDistance, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
        {
            distance = lineHit.distance;
        }

        // Character + Default covers scouts/zombies, mobs, spiders, and most world items (mandrake, dynamite, spores).
        RaycastHit[] hits = Physics.SphereCastAll(
            origin,
            pelletRadius,
            direction,
            distance,
            HelperFunctions.CharacterAndDefaultMask,
            QueryTriggerInteraction.Collide);

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit candidate in hits)
        {
            if (!candidate.collider)
            {
                continue;
            }

            int id = candidate.collider.transform.root.GetInstanceID();
            if (struck.Contains(id))
            {
                continue;
            }

            if (!ShotgunCombat.TryHit(candidate.collider, character, this, shotId, candidate.point, direction))
            {
                continue;
            }

            struck.Add(id);
            return;
        }
    }

    [PunRPC]
    public void RPC_GunImpact(Photon.Realtime.Player? hitPlayer, Vector3 endpoint, Vector3 direction, int shotId, PhotonMessageInfo info)
    {
        if (hitPlayer == null || !hitPlayer.IsLocal)
        {
            return;
        }

        Character local = Character.localCharacter;
        if (local == null || !TryConsumeShot(info.Sender, shotId, local.photonView.ViewID))
        {
            return;
        }

        local.GetComponent<GunCharacterLaunch>().Blast(direction, endpoint);
        if (!ShotgunCombat.FriendlyFire)
        {
            return;
        }

        foreach (Affliction affliction in afflictionsOnHit)
        {
            local.refs.afflictions.AddAffliction(affliction);
        }
    }

    /// <summary>
    /// Host-only: apply a hit on a non-character target (spider, beetle, scorpion, mandrake, dynamite, spores)
    /// after checking the shot was accepted and the room still allows shooting that kind of target.
    /// </summary>
    [PunRPC]
    public void RPC_HostShootTarget(int viewId, int kind, int shotId, PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient || !TryConsumeShot(info.Sender, shotId, viewId))
        {
            return;
        }

        PhotonView? view = PhotonView.Find(viewId);
        if (view != null)
        {
            ShotgunCombat.HostApply(view, (ShotTargetKind)kind);
        }
    }

    [PunRPC]
    private void RPC_ShotgunBlastFX(Vector3 origin, Vector3 forward)
    {
        shotSFX?.Play(origin);
        GetComponent<ShotgunVFX>()?.Play(origin, forward);
        GamefeelHandler.instance.AddPerlinShakeProximity(origin + forward * 3f, 12f);
    }

    private static Vector2 PelletOffset(int index, int count, float radius)
    {
        if (index <= 0)
        {
            return Vector2.zero;
        }

        float angle = index * 137.5f * Mathf.Deg2Rad;
        float ring = radius * (0.4f + 0.6f * (index / (float)Mathf.Max(1, count - 1)));
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * ring;
    }
}
