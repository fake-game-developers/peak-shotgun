using Photon.Pun;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>Destroys the mandrake item.</summary>
internal sealed class MandrakeTarget : ShotTarget<Mandrake>
{
    internal override ShotTargetKind Kind => ShotTargetKind.Mandrake;

    internal override ShootableRule Rule => ShootableRules.Mandrake;

    protected override void Defeat(Mandrake mandrake, ShotHit hit)
    {
        Item? item = mandrake.item != null ? mandrake.item : mandrake.GetComponent<Item>();
        DestroyNetworked(hit.View, item != null ? item.gameObject : mandrake.gameObject);
    }
}
