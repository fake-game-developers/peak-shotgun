using Photon.Pun;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// Same path as a burnt-out fuse: <see cref="Dynamite.RPC_Explode"/> on everyone, then destroy the item.
/// No fuse light first.
/// </summary>
internal sealed class DynamiteTarget : ShotTarget<Dynamite>
{
    internal override ShotTargetKind Kind => ShotTargetKind.Dynamite;

    internal override ShootableRule Rule => ShootableRules.Dynamite;

    internal override bool IsLiving => false;

    protected override void Defeat(Dynamite dynamite, ShotHit hit)
    {
        if (dynamite == null)
        {
            return;
        }

        PhotonView? dynamiteView = dynamite.photonView != null
            ? dynamite.photonView
            : dynamite.GetComponent<PhotonView>();
        if (PhotonNetwork.InRoom && dynamiteView != null && dynamiteView.ViewID != 0)
        {
            dynamiteView.RPC(nameof(Dynamite.RPC_Explode), RpcTarget.All);
            PhotonNetwork.Destroy(dynamiteView.gameObject);
        }
        else
        {
            dynamite.RPC_Explode();
            Object.Destroy(dynamite.gameObject);
        }

        Item? item = dynamite.item != null ? dynamite.item : dynamite.GetComponent<Item>();
        item?.ClearDataFromBackpack();
    }
}
