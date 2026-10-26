using Photon.Pun;

namespace Peak.Shotgun;

/// <summary>Stops the pit AI and hides the head. Leaves the sand funnel in place so the terrain does not vanish.</summary>
internal sealed class AntlionTarget : ShotTarget<Antlion>
{
    internal override ShotTargetKind Kind => ShotTargetKind.Antlion;

    internal override ShootableRule Rule => ShootableRules.Antlions;

    internal override bool BroadcastsDefeat => true;

    protected override PhotonView? ViewOf(Antlion antlion) =>
        antlion.view != null ? antlion.view : base.ViewOf(antlion);

    protected override void Defeat(Antlion antlion, ShotHit hit) => DefeatAntlion(antlion);

    internal override void ClientDefeat(PhotonView? view, int extra)
    {
        if (view != null)
        {
            DefeatAntlion(Near<Antlion>(view.gameObject));
        }
    }

    private static void DefeatAntlion(Antlion? antlion)
    {
        if (antlion == null)
        {
            return;
        }

        antlion.DoInactive();
        antlion.enabled = false;
        if (antlion.head != null)
        {
            antlion.head.SetActive(false);
        }

        if (antlion.slideObj != null)
        {
            antlion.slideObj.SetActive(false);
        }

        if (antlion.anim != null)
        {
            antlion.anim.enabled = false;
        }
    }
}
