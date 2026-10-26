using Photon.Pun;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// Ticks sit on a scout, so they are classified before the scout: a hit on the bug does not Injury the carrier.
/// Removed the same way as picking the tick off (<see cref="Bugfix.RPCA_Remove"/>).
/// </summary>
internal sealed class TickTarget : ShotTarget<Bugfix>
{
    internal override ShotTargetKind Kind => ShotTargetKind.Tick;

    internal override ShootableRule Rule => ShootableRules.Ticks;

    internal override ShotStage Stage => ShotStage.BeforeCharacters;

    protected override PhotonView? ViewOf(Bugfix tick) =>
        tick.photonView != null ? tick.photonView : base.ViewOf(tick);

    /// <summary>Owner-only destroy, so ask the owner when the host does not own the tick.</summary>
    protected override void Defeat(Bugfix tick, ShotHit hit)
    {
        if (tick == null)
        {
            return;
        }

        PhotonView? tickView = tick.photonView != null ? tick.photonView : tick.GetComponent<PhotonView>();
        if (PhotonNetwork.InRoom && tickView != null && tickView.ViewID != 0)
        {
            if (tickView.IsMine)
            {
                tickView.RPC(nameof(Bugfix.RPCA_Remove), RpcTarget.All);
            }
            else if (tickView.Owner != null)
            {
                tickView.RPC(nameof(Bugfix.RPCA_Remove), tickView.Owner);
            }
            else
            {
                tickView.RPC(nameof(Bugfix.RPCA_Remove), RpcTarget.All);
            }

            return;
        }

        Object.Destroy(tick.gameObject);
    }
}
