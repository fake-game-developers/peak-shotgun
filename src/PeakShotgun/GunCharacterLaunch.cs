using System;
using Peak.Afflictions;
using Photon.Pun;
using UnityEngine;

namespace PeakShotgun;

public class GunCharacterLaunch : MonoBehaviour
{
    private static readonly Type? ZombieType = AccessToolsType("MushroomZombie");

    // A coconut-style impulse is applied to every bone, which launches the body.
    // This is a short acceleration instead, so the ragdoll tips over in place.
    private const float BlastRagdollSeconds = 3f;

    private const float ShoveAcceleration = 36f;

    private const float ShoveSeconds = 0.3f;

    private const float ZombieDrowsyDamage = 0.5f;

    private const int HitsToKnockDown = 2;

    private int shotgunHits;

    private bool heldDown;

    internal bool Silenced { get; private set; }

    private float shoveTime;

    private Vector3 shoveDirection;

    public static bool IsZombie(Character character) =>
        ZombieType != null && character.GetComponent(ZombieType) != null;

    [PunRPC]
    public void RPC_ShotgunBlast(Vector3 direction, Vector3 point)
    {
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

        // Injury does not affect zombies. The first shot only adds drowsy, so they can get back up.
        character.refs.afflictions.AddAffliction(
            new Affliction_AdjustStatus(CharacterAfflictions.STATUSTYPE.Drowsy, ZombieDrowsyDamage, 1f));
    }

    private void Update()
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

        shoveTime -= Time.deltaTime;
        character.AddForce(shoveDirection * ShoveAcceleration, 1f, 1f);
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
        character.photonView.RPC(nameof(RPC_SilenceZombie), RpcTarget.All);
    }

    [PunRPC]
    public void RPC_SilenceZombie()
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
