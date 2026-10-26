using System;
using System.Reflection;
using Photon.Pun;
using UnityEngine;

namespace Peak.Shotgun;

internal sealed class BeetleTarget : MobTarget<Beetle>
{
    internal override ShotTargetKind Kind => ShotTargetKind.Beetle;

    internal override ShootableRule Rule => ShootableRules.Beetles;
}

internal sealed class ScorpionTarget : MobTarget<Scorpion>
{
    internal override ShotTargetKind Kind => ShotTargetKind.Scorpion;

    internal override ShootableRule Rule => ShootableRules.Scorpions;
}

/// <summary>
/// Kills a <see cref="Mob"/> by setting its state to Dead (mirrors cooking a scorpion / beetle). Mob state is
/// owned by the mob's owner (its setter only syncs from there), so a host that does not own the mob asks the
/// owner to switch it.
/// </summary>
internal abstract class MobTarget<T> : ShotTarget<T>
    where T : Mob
{
    private static PropertyInfo? mobStateProperty;
    private static object? mobStateDead;

    protected override void Defeat(T mob, ShotHit hit)
    {
        if (mob == null)
        {
            return;
        }

        PhotonView? mobView = mob.photonView;
        bool networked = PhotonNetwork.InRoom && mobView != null && mobView.ViewID != 0;
        if (networked && !mobView!.IsMine)
        {
            // Dead = 3 on Mob.MobState.
            if (mobView.Owner != null)
            {
                mobView.RPC(GameCompat.SyncMobStateRpc, mobView.Owner, 3);
            }
            else
            {
                mobView.RPC(GameCompat.SyncMobStateRpc, RpcTarget.All, 3);
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
