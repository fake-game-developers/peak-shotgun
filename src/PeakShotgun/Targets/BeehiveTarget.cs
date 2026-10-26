using Photon.Pun;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>Same smash as throwing something at the hive: honeycomb spawns, then <see cref="Beehive.OnDestroy"/> angers the swarm.</summary>
internal sealed class BeehiveTarget : ShotTarget<Beehive>
{
    internal override ShotTargetKind Kind => ShotTargetKind.Beehive;

    internal override ShootableRule Rule => ShootableRules.Beehives;

    protected override PhotonView? ViewOf(Beehive hive)
    {
        Item? hiveItem = hive.item != null ? hive.item : hive.GetComponent<Item>();
        return hiveItem != null && hiveItem.photonView != null ? hiveItem.photonView : base.ViewOf(hive);
    }

    protected override void Defeat(Beehive hive, ShotHit hit)
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
        PhotonView? itemView = hit.View
            ?? (item != null ? item.photonView : null)
            ?? hive.GetComponentInParent<PhotonView>();
        if (PhotonNetwork.InRoom && itemView != null && itemView.ViewID != 0)
        {
            PhotonNetwork.Destroy(itemView.gameObject);
        }
        else
        {
            Object.Destroy(root);
        }
    }
}
