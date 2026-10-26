using Photon.Pun;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// Tree fruit is a kinematic <see cref="Item"/> (same as a berry bush). A thrown item knocks it loose through
/// <see cref="EventOnItemCollision"/>; a pellet has no collision, so unfreeze it the same way. It falls whole and
/// does not split.
/// </summary>
internal sealed class CoconutTarget : ShotTarget<Item>
{
    internal override ShotTargetKind Kind => ShotTargetKind.Coconut;

    internal override ShootableRule Rule => ShootableRules.Coconuts;

    internal override Component? Classify(Collider collider) =>
        collider.GetComponentInParent<Item>() is { } item && IsTreeCoconut(item) ? item : null;

    internal override Component? FindFromView(PhotonView view, int extra) =>
        Near<Item>(view.gameObject) is { } item && IsTreeCoconut(item) ? item : null;

    protected override PhotonView? ViewOf(Item item) =>
        item.photonView != null ? item.photonView : base.ViewOf(item);

    protected override void Defeat(Item coconut, ShotHit hit)
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
}
