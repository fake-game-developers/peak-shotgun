using Photon.Pun;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>Stuns a spider, same as throwing an item at it (<see cref="Spider.Bonk"/>).</summary>
internal sealed class SpiderTarget : ShotTarget<Spider>
{
    internal override ShotTargetKind Kind => ShotTargetKind.Spider;

    internal override ShootableRule Rule => ShootableRules.Spiders;

    internal override Component? Classify(Collider collider)
    {
        Spider? spider = collider.GetComponentInParent<Spider>();
        if (spider != null)
        {
            return spider;
        }

        SpiderTrigger? trigger = collider.GetComponentInParent<SpiderTrigger>();
        return trigger != null ? trigger.spider : null;
    }

    protected override PhotonView? ViewOf(Spider spider) =>
        spider.photonView != null ? spider.photonView : base.ViewOf(spider);

    protected override void Defeat(Spider spider, ShotHit hit) => spider.Bonk();
}
