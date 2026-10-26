using Photon.Pun;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// Drops a carried scout, then removes the bird. Destroyed only on the host so Photon replicates it; other
/// clients hide their local instance until that arrives.
/// </summary>
internal sealed class CondorTarget : ShotTarget<Condor>
{
    internal override ShotTargetKind Kind => ShotTargetKind.Condor;

    internal override ShootableRule Rule => ShootableRules.Condors;

    internal override bool BroadcastsDefeat => true;

    protected override PhotonView? ViewOf(Condor condor) =>
        condor.view != null ? condor.view : base.ViewOf(condor);

    protected override void Defeat(Condor condor, ShotHit hit) => DefeatCondor(condor, destroy: true);

    internal override void ClientDefeat(PhotonView? view, int extra) =>
        DefeatCondor(view != null ? Near<Condor>(view.gameObject) : null, destroy: false);

    private static void DefeatCondor(Condor? condor, bool destroy)
    {
        ReleaseCondorCarries(condor);
        if (condor != null)
        {
            condor.GiveUp();
            condor.enabled = false;
            if (condor.carryTrigger != null)
            {
                condor.carryTrigger.SetActive(false);
            }

            if (condor.rb != null)
            {
                condor.rb.linearVelocity = Vector3.zero;
                condor.rb.angularVelocity = Vector3.zero;
                condor.rb.isKinematic = true;
            }

            if (condor.anim != null)
            {
                condor.anim.enabled = false;
            }

            foreach (Renderer renderer in condor.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = false;
            }

            foreach (Collider collider in condor.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
        }

        if (!destroy || condor == null)
        {
            return;
        }

        PhotonView? view = condor.view != null ? condor.view : condor.GetComponent<PhotonView>();
        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient && view != null && view.ViewID != 0)
        {
            PhotonNetwork.Destroy(view.gameObject);
        }
        else if (!PhotonNetwork.InRoom)
        {
            Object.Destroy(condor.gameObject);
        }
    }

    private static void ReleaseCondorCarries(Condor? condor)
    {
        foreach (Character character in Character.AllCharacters)
        {
            CharacterData? data = character != null ? character.data : null;
            Condor? carried = data != null ? data.carrierCondor : null;
            if (data == null || (object?)carried == null)
            {
                continue;
            }

            bool destroyed = carried == null;
            if (destroyed || carried == condor)
            {
                data.carrierCondor = null;
            }
        }
    }
}
