using Photon.Pun;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// Scatters a bee swarm, angry or not, through vanilla <see cref="BeeSwarm.DisperseRPC"/>: the stinger field goes
/// at once and the owner destroys the swarm about 4 s later (which also reaches late joiners). Vanilla dispersal
/// fades slowly and keeps chasing its target meanwhile, so on top of it every client scatters and fades the bees
/// within half a second and stops the buzzing, and the owner freezes the swarm in place.
/// The swarm's only collider sits on the GhostCharacter layer, which pellets do not hit by default.
/// </summary>
internal sealed class BeeSwarmTarget : ShotTarget<BeeSwarm>
{
    private const float ScatterSpeed = 7f;

    private const float FadeMin = 0.15f;

    private const float FadeMax = 0.5f;

    private readonly int swarmLayers = LayerMask.GetMask("GhostCharacter");

    internal override ShotTargetKind Kind => ShotTargetKind.BeeSwarm;

    internal override ShootableRule Rule => ShootableRules.BeeSwarms;

    internal override int ExtraLayerMask => swarmLayers;

    protected override PhotonView? ViewOf(BeeSwarm swarm) =>
        swarm.photonView != null ? swarm.photonView : base.ViewOf(swarm);

    protected override void Defeat(BeeSwarm swarm, ShotHit hit)
    {
        if (swarm == null || swarm.dispersing)
        {
            return;
        }

        PhotonView? view = ViewOf(swarm);
        if (PhotonNetwork.InRoom && view != null && view.ViewID != 0)
        {
            view.RPC(nameof(BeeSwarm.DisperseRPC), RpcTarget.All);
        }
        else
        {
            swarm.DisperseRPC();
        }

        ShotgunCombat.BroadcastEffect(this, swarm, hit, Vector3.zero);
    }

    protected override void Effect(BeeSwarm swarm, Vector3 push, int variant)
    {
        ScatterParticles(swarm.beeParticles);
        if (swarm.beeForceField != null)
        {
            swarm.beeForceField.gravity = 0f;
        }

        foreach (AudioSource source in swarm.GetComponentsInChildren<AudioSource>(true))
        {
            source.Stop();
        }

        // Vanilla dispersal still pulls the swarm toward its target every physics step; only the owner moves it.
        if (swarm.photonView == null || swarm.photonView.IsMine)
        {
            Rigidbody? rig = swarm.GetComponent<Rigidbody>();
            if (rig != null && !rig.isKinematic)
            {
                rig.linearVelocity = Vector3.zero;
                rig.isKinematic = true;
            }
        }
    }

    private static void ScatterParticles(ParticleSystem? bees)
    {
        if (bees == null)
        {
            return;
        }

        bees.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        var particles = new ParticleSystem.Particle[bees.particleCount];
        int count = bees.GetParticles(particles);
        for (int i = 0; i < count; i++)
        {
            particles[i].velocity += Random.onUnitSphere * ScatterSpeed;
            particles[i].remainingLifetime = Mathf.Min(particles[i].remainingLifetime, Random.Range(FadeMin, FadeMax));
        }

        bees.SetParticles(particles, count);
    }
}
