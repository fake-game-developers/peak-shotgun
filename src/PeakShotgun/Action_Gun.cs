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

    private float lastShootTime = float.NegativeInfinity;

    public override void RunAction()
    {
        if (Time.time <= lastShootTime + fireRate || !HasAmmo())
        {
            return;
        }

        lastShootTime = Time.time;
        Fire();
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

    private void Fire()
    {
        if (spawnTransform == null)
        {
            return;
        }

        // Host is authoritative for remaining ammo; clients only request a spend.
        item.photonView.RPC(nameof(Action_Ammo.RequestSpendRPC), RpcTarget.MasterClient);
        OnShoot?.Invoke();

        Vector3 origin = spawnTransform.position;
        Vector3 forward = MainCamera.instance.transform.forward;
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
            TryPellet(origin, direction, struck);
        }

        photonView.RPC(nameof(RPC_ShotgunBlastFX), RpcTarget.All, origin, forward);
    }

    private void TryPellet(Vector3 origin, Vector3 direction, HashSet<int> struck)
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

            if (!ShotgunCombat.TryHit(candidate.collider, character, this, candidate.point, direction))
            {
                continue;
            }

            struck.Add(id);
            return;
        }
    }

    [PunRPC]
    public void RPC_GunImpact(Photon.Realtime.Player? hitPlayer, Vector3 endpoint, Vector3 direction)
    {
        if (hitPlayer == null || !hitPlayer.IsLocal)
        {
            return;
        }

        Character local = Character.localCharacter;
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

    /// <summary>Host-only: destroy a networked object the shotgun hit (mandrake, spore cloud, etc.).</summary>
    [PunRPC]
    public void RPC_HostDestroyView(int viewId)
    {
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            return;
        }

        PhotonView? view = PhotonView.Find(viewId);
        if (view != null)
        {
            PhotonNetwork.Destroy(view.gameObject);
        }
    }

    /// <summary>Host-only: break a spore bomb (<see cref="CloudFungus"/>).</summary>
    [PunRPC]
    public void RPC_HostBreakFungus(int viewId)
    {
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            return;
        }

        PhotonView? view = PhotonView.Find(viewId);
        CloudFungus? fungus = view != null ? view.GetComponent<CloudFungus>() : null;
        fungus?.Break();
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
