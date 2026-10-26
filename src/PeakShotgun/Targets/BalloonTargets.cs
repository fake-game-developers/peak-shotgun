using Photon.Pun;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// A balloon tied to a scout. Only the scout who tied it can remove it (vanilla <see cref="TiedBalloon.Pop"/> is
/// owner-only), so the host broadcasts the hit and the owner pops it. Its removal plays the pop particle and drops
/// that scout's float on every client (<see cref="CharacterBalloons.RemoveBalloon"/>).
/// </summary>
internal sealed class TiedBalloonTarget : ShotTarget<TiedBalloon>
{
    internal override ShotTargetKind Kind => ShotTargetKind.TiedBalloon;

    internal override ShootableRule Rule => ShootableRules.Balloons;

    protected override PhotonView? ViewOf(TiedBalloon balloon) =>
        balloon.photonView != null ? balloon.photonView : base.ViewOf(balloon);

    protected override void Defeat(TiedBalloon balloon, ShotHit hit) =>
        ShotgunCombat.BroadcastEffect(this, balloon, hit, Vector3.zero);

    protected override void Effect(TiedBalloon balloon, Vector3 push, int variant)
    {
        if (balloon.photonView != null && balloon.photonView.IsMine)
        {
            balloon.Pop();
        }
    }
}

/// <summary>
/// A balloon or balloon bunch item. Everyone sees the pop; then its owner removes it: in a hand through vanilla
/// <see cref="Item.Consume"/> (empties the holder's slot, like tying it), on the ground with a network destroy.
/// Balloons in a backpack are not in the world and cannot be hit.
/// </summary>
internal sealed class BalloonItemTarget : ShotTarget<Balloon>
{
    internal override ShotTargetKind Kind => ShotTargetKind.Balloon;

    internal override ShootableRule Rule => ShootableRules.Balloons;

    protected override PhotonView? ViewOf(Balloon balloon)
    {
        Item? item = ItemOf(balloon);
        return item != null && item.photonView != null ? item.photonView : base.ViewOf(balloon);
    }

    protected override void Defeat(Balloon balloon, ShotHit hit) =>
        ShotgunCombat.BroadcastEffect(this, balloon, hit, Vector3.zero);

    protected override void Effect(Balloon balloon, Vector3 push, int variant)
    {
        Item? item = ItemOf(balloon);
        if (item == null || !item.gameObject.activeInHierarchy)
        {
            return;
        }

        GameObject? particle = balloon.popParticle;
        if (particle == null && Character.localCharacter != null)
        {
            particle = Character.localCharacter.refs.balloons.popParticle;
        }

        if (particle != null)
        {
            Object.Instantiate(particle, balloon.transform.position, Quaternion.identity);
        }

        PhotonView? view = item.photonView;
        bool mine = !PhotonNetwork.InRoom || (view != null && view.IsMine);
        if (!mine)
        {
            return;
        }

        if (item.itemState == ItemState.Held)
        {
            // -1: nobody "ate" it, so no consume event or achievement fires.
            if (PhotonNetwork.InRoom && view != null)
            {
                view.RPC(nameof(Item.Consume), RpcTarget.All, -1);
            }
            else
            {
                item.Consume(-1);
            }
        }
        else if (item.itemState == ItemState.Ground)
        {
            if (PhotonNetwork.InRoom && view != null)
            {
                PhotonNetwork.Destroy(item.gameObject);
            }
            else
            {
                Object.Destroy(item.gameObject);
            }
        }
    }

    private static Item? ItemOf(Balloon balloon) =>
        balloon.item != null ? balloon.item : balloon.GetComponent<Item>() ?? balloon.GetComponentInParent<Item>();
}
