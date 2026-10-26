using Photon.Pun;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// Bing Bong squeaks, says one of his usual answers (same voice line and subtitle as asking him) and, lying on
/// the ground, gets knocked into the air. Nothing is destroyed; every hit reacts. The host picks the line so
/// everyone hears the same one, and only the item's owner pushes it (ground items sync from their owner).
/// </summary>
internal sealed class BingBongTarget : ShotTarget<BingBong>
{
    private const float PushSpeed = 6f;

    private const float PushLift = 5f;

    private const float Spin = 15f;

    internal override ShotTargetKind Kind => ShotTargetKind.BingBong;

    internal override ShootableRule Rule => ShootableRules.BingBong;

    protected override PhotonView? ViewOf(BingBong bingBong)
    {
        Item? item = ItemOf(bingBong);
        return item != null && item.photonView != null ? item.photonView : base.ViewOf(bingBong);
    }

    protected override void Defeat(BingBong bingBong, ShotHit hit)
    {
        Action_AskBingBong? ask = AskOf(bingBong);
        int line = ask != null && ask.responses != null && ask.responses.Length > 0
            ? Random.Range(0, ask.responses.Length)
            : -1;
        Vector3 push = hit.DirectionTo(bingBong.transform.position) * PushSpeed + Vector3.up * PushLift;
        ShotgunCombat.BroadcastEffect(this, bingBong, hit, push, line);
    }

    protected override void Effect(BingBong bingBong, Vector3 push, int line)
    {
        Item? item = ItemOf(bingBong);
        Action_AskBingBong? ask = AskOf(bingBong);
        if (ask != null && ask.responses != null && line >= 0 && line < ask.responses.Length)
        {
            if (item != null && item.holderCharacter != null)
            {
                // Vanilla reaction; it needs a holder for the hand animation.
                ask.Ask(line, false);
            }
            else
            {
                Speak(ask, line);
            }
        }

        if (item == null || item.itemState != ItemState.Ground)
        {
            return;
        }

        if (PhotonNetwork.InRoom && (item.photonView == null || !item.photonView.IsMine))
        {
            return;
        }

        Rigidbody? rig = item.rig != null ? item.rig : item.GetComponent<Rigidbody>();
        if (rig == null)
        {
            return;
        }

        if (rig.isKinematic)
        {
            item.SetKinematicNetworked(false);
        }

        rig.AddForce(push, ForceMode.VelocityChange);
        rig.AddTorque(Random.insideUnitSphere * Spin, ForceMode.VelocityChange);
    }

    /// <summary><see cref="Action_AskBingBong.Ask"/> without a holder: squish, squeak, then the voice line and subtitle.</summary>
    private static void Speak(Action_AskBingBong ask, int line)
    {
        if (ask.squishAnim != null)
        {
            ask.squishAnim.SetTrigger("Squish");
        }

        if (ask.squeak != null && SFX_Player.instance != null)
        {
            SFX_Player.instance.PlaySFX(ask.squeak, ask.transform.position, ask.transform);
        }

        Action_AskBingBong.BingBongResponse response = ask.responses[line];
        if (ask.subtitles != null && !string.IsNullOrEmpty(response.subtitleID))
        {
            if (ask.subtitleRoutine != null)
            {
                ask.StopCoroutine(ask.subtitleRoutine);
            }

            ask.subtitleRoutine = ask.StartCoroutine(ask.SubtitleRoutine(response.subtitleID));
        }

        if (ask.source != null && response.sfx != null && response.sfx.clips is { Length: > 0 })
        {
            ask.source.Stop();
            ask.source.PlayOneShot(response.sfx.clips[0]);
        }
    }

    private static Item? ItemOf(BingBong bingBong) =>
        bingBong.GetComponent<Item>() ?? bingBong.GetComponentInParent<Item>();

    private static Action_AskBingBong? AskOf(BingBong bingBong) =>
        bingBong.GetComponent<Action_AskBingBong>() ?? bingBong.GetComponentInChildren<Action_AskBingBong>(true);
}
