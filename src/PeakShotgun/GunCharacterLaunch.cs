using System;
using Peak.Afflictions;
using Photon.Pun;
using UnityEngine;

namespace Peak.Shotgun;

public class GunCharacterLaunch : MonoBehaviourPunCallbacks
{
    private static readonly Type? ZombieType = AccessToolsType("MushroomZombie");

    // A coconut-style impulse is applied to every bone, which launches the body.
    // This is a short acceleration instead, so the ragdoll tips over in place.
    private const float BlastRagdollSeconds = 3f;

    private const float ShoveAcceleration = 36f;

    private const float ShoveSeconds = 0.3f;

    private const float ZombieDrowsyDamage = 0.5f;

    private const int HitsToKnockDown = 2;

    // Zombie shot state is owned by the zombie's owner and mirrored to everyone (RPC_ZombieShotState), so a new
    // owner after host migration continues from it and late joiners see a knocked-down zombie as silenced.
    private int shotgunHits;

    private bool heldDown;

    internal bool Silenced { get; private set; }

    private float shoveTime;

    private Vector3 shoveDirection;

    public static bool IsZombie(Character character) =>
        ZombieType != null && character.GetComponent(ZombieType) != null;

    /// <summary>
    /// Zombie hit sent by a remote shooter. The zombie's owner applies it only for a shot the host accepted
    /// (<paramref name="gunViewId"/> + <paramref name="shotId"/>) and only while the room allows shooting zombies.
    /// </summary>
    [PunRPC]
    public void RPC_ShotgunBlast(Vector3 direction, Vector3 point, int gunViewId, int shotId, PhotonMessageInfo info)
    {
        Character? character = GetComponent<Character>();
        if (character == null || !character.photonView.IsMine || !IsZombie(character) || !ShotgunCombat.CanShootZombies)
        {
            return;
        }

        PhotonView? gunView = PhotonView.Find(gunViewId);
        Action_Gun? gun = gunView != null ? gunView.GetComponent<Action_Gun>() : null;
        if (gun == null)
        {
            return;
        }

        int targetId = character.photonView.ViewID;
        if (!gun.TryConsumeOrDeferHit(info.Sender, shotId, targetId, () => Blast(direction, point)))
        {
            return;
        }

        Blast(direction, point);
    }

    public void Blast(Vector3 direction, Vector3 point)
    {
        Character? character = GetComponent<Character>();
        if (character == null || !character.photonView.IsMine || character.data.dead)
        {
            return;
        }

        Vector3 force = (direction.normalized + Vector3.up * 0.45f).normalized;
        character.Fall(BlastRagdollSeconds);
        shoveDirection = force;
        shoveTime = ShoveSeconds;
        if (!IsZombie(character))
        {
            return;
        }

        shotgunHits++;
        if (shotgunHits >= HitsToKnockDown)
        {
            KnockDown(character);
            return;
        }

        BroadcastShotState();

        // Injury does not affect zombies. The first shot only adds drowsy, so they can get back up.
        character.refs.afflictions.AddAffliction(
            new Affliction_AdjustStatus(CharacterAfflictions.STATUSTYPE.Drowsy, ZombieDrowsyDamage, 1f));
    }

    // Bodypart forces accumulate until the next physics step, so the shove has to be added once per step,
    // not once per rendered frame, or its strength scales with frame rate.
    private void FixedUpdate()
    {
        if (shoveTime <= 0f)
        {
            return;
        }

        Character? character = GetComponent<Character>();
        if (character == null || !character.photonView.IsMine)
        {
            shoveTime = 0f;
            return;
        }

        float step = Time.fixedDeltaTime;
        float fraction = Mathf.Min(shoveTime, step) / step;
        shoveTime -= step;
        character.AddForce(shoveDirection * (ShoveAcceleration * fraction), 1f, 1f);
    }

    private void LateUpdate()
    {
        if (!heldDown)
        {
            return;
        }

        Character? character = GetComponent<Character>();
        if (character == null || !character.photonView.IsMine)
        {
            return;
        }

        if (character.data.dead)
        {
            heldDown = false;
            BroadcastShotState();
            return;
        }

        // A finished lunge recovery sends the zombie back to chasing. Hold the knockout.
        HoldDown(character);
    }

    private void KnockDown(Character character)
    {
        heldDown = true;
        character.PassOutInstantly();
        HoldDown(character);
        Silence();
        BroadcastShotState();
    }

    private void BroadcastShotState()
    {
        if (PhotonNetwork.InRoom && photonView != null)
        {
            photonView.RPC(nameof(RPC_ZombieShotState), RpcTarget.Others, shotgunHits, heldDown);
        }
    }

    [PunRPC]
    public void RPC_ZombieShotState(int hits, bool down, PhotonMessageInfo info)
    {
        if (info.Sender == null || info.Sender != photonView.Owner)
        {
            return;
        }

        shotgunHits = hits;
        heldDown = down;
        if (down)
        {
            Silence();
        }
    }

    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
    {
        if (photonView != null && photonView.IsMine && (shotgunHits > 0 || heldDown))
        {
            photonView.RPC(nameof(RPC_ZombieShotState), newPlayer, shotgunHits, heldDown);
        }
    }

    private void Silence()
    {
        if (Silenced)
        {
            return;
        }

        Silenced = true;
        foreach (AudioSource source in GetComponentsInChildren<AudioSource>(true))
        {
            source.Stop();
            source.mute = true;
        }
    }

    private static void HoldDown(Character character)
    {
        character.refs.afflictions.SetStatus(CharacterAfflictions.STATUSTYPE.Drowsy, 1f, false);
        character.data.passedOut = true;
        character.data.fullyPassedOut = true;
        if (character.data.fallSeconds < 1f)
        {
            character.data.fallSeconds = 5f;
        }
    }

    private static Type? AccessToolsType(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType(name);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }
}
