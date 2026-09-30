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
        OptionableIntItemData data = item.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
        if (!data.HasData)
        {
            return true;
        }

        return data.Value < 0 || data.Value > 0;
    }

    private void Fire()
    {
        if (spawnTransform == null)
        {
            return;
        }

        item.photonView.RPC(nameof(Action_Ammo.ReduceUsesRPC), RpcTarget.All);
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
        var struck = new List<Character>();

        for (int i = 0; i < pelletCount; i++)
        {
            Vector2 offset = PelletOffset(i, pelletCount, spread);
            Vector3 direction = (forward + right * offset.x + up * offset.y).normalized;
            if (!PelletHit(origin, direction, out RaycastHit hit, out Character? character))
            {
                continue;
            }

            if (character == null || character == this.character || struck.Contains(character))
            {
                continue;
            }

            struck.Add(character);
            Impact(character, hit.point, direction);
        }

        photonView.RPC(nameof(RPC_ShotgunBlastFX), RpcTarget.All, origin, forward);
    }

    private bool PelletHit(Vector3 origin, Vector3 direction, out RaycastHit hit, out Character? struck)
    {
        struck = null;
        float distance = maxDistance;
        if (Physics.Raycast(origin, direction, out RaycastHit lineHit, maxDistance, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
        {
            distance = lineHit.distance;
            hit = lineHit;
        }
        else
        {
            hit = default;
            hit.point = origin + direction * maxDistance;
            hit.distance = maxDistance;
        }

        RaycastHit[] hits = Physics.SphereCastAll(
            origin,
            pelletRadius,
            direction,
            distance,
            LayerMask.GetMask("Character"),
            QueryTriggerInteraction.Ignore);

        float closest = float.MaxValue;
        foreach (RaycastHit candidate in hits)
        {
            if (!candidate.collider || candidate.distance > closest)
            {
                continue;
            }

            Character? character = candidate.collider.GetComponentInParent<Character>();
            if (character == null || character == this.character)
            {
                continue;
            }

            closest = candidate.distance;
            hit = candidate;
            struck = character;
        }

        return true;
    }

    private void Impact(Character struck, Vector3 endpoint, Vector3 direction)
    {
        if (GunCharacterLaunch.IsZombie(struck))
        {
            GunCharacterLaunch? launch = struck.GetComponent<GunCharacterLaunch>();
            if (struck.photonView.IsMine)
            {
                launch?.Blast(direction, endpoint);
            }
            else
            {
                struck.photonView.RPC(nameof(GunCharacterLaunch.RPC_ShotgunBlast), RpcTarget.All, direction, endpoint);
            }

            return;
        }

        photonView.RPC(nameof(RPC_GunImpact), RpcTarget.All, struck.photonView.Owner, endpoint, direction);
    }

    [PunRPC]
    private void RPC_GunImpact(Photon.Realtime.Player? hitPlayer, Vector3 endpoint, Vector3 direction)
    {
        if (hitPlayer != null && hitPlayer.IsLocal)
        {
            Character local = Character.localCharacter;
            local.GetComponent<GunCharacterLaunch>().Blast(direction, endpoint);
            foreach (Affliction affliction in afflictionsOnHit)
            {
                local.refs.afflictions.AddAffliction(affliction);
            }
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
