using Photon.Pun;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// Jellyfish share one <see cref="TriggerRelay"/> Photon view, so the hit carries the jelly's sibling index and
/// only that child is removed. Every client must run this; do not <see cref="PhotonNetwork.Destroy"/> the relay.
/// </summary>
internal sealed class JellyfishTarget : ShotTarget<SlipperyJellyfish>
{
    internal override ShotTargetKind Kind => ShotTargetKind.Jellyfish;

    internal override ShootableRule Rule => ShootableRules.Jellyfish;

    internal override bool BroadcastsDefeat => true;

    internal override int ExtraOf(Component target) => target.transform.GetSiblingIndex();

    internal override Component? FindFromView(PhotonView view, int extra) => FindJellyfish(view, extra);

    protected override PhotonView? ViewOf(SlipperyJellyfish jellyfish) =>
        jellyfish.relay != null && jellyfish.relay.view != null ? jellyfish.relay.view : base.ViewOf(jellyfish);

    protected override void Defeat(SlipperyJellyfish jellyfish, ShotHit hit) => Smash(jellyfish);

    internal override void ClientDefeat(PhotonView? view, int extra)
    {
        if (view != null)
        {
            Smash(FindJellyfish(view, extra));
        }
    }

    private static SlipperyJellyfish? FindJellyfish(PhotonView view, int extra)
    {
        Transform root = view.transform;
        if (extra >= 0 && extra < root.childCount)
        {
            SlipperyJellyfish? child = root.GetChild(extra).GetComponent<SlipperyJellyfish>();
            if (child != null)
            {
                return child;
            }
        }

        return Near<SlipperyJellyfish>(view.gameObject);
    }

    private static void Smash(SlipperyJellyfish? jellyfish)
    {
        if (jellyfish == null)
        {
            return;
        }

        jellyfish.enabled = false;
        foreach (Collider collider in jellyfish.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }

        foreach (Renderer renderer in jellyfish.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = false;
        }

        Object.Destroy(jellyfish.gameObject);
    }
}
