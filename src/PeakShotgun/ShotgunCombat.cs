using System;
using System.Reflection;
using BepInEx.Configuration;
using Peak.Afflictions;
using Photon.Pun;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// Config + hit resolution for what the shotgun can affect (creatures, hazards, friendly fire).
/// </summary>
internal static class ShotgunCombat
{
    private static ConfigEntry<bool>? friendlyFire;
    private static ConfigEntry<bool>? shootZombies;
    private static ConfigEntry<bool>? shootMandrake;
    private static ConfigEntry<bool>? shootBeetles;
    private static ConfigEntry<bool>? shootSpiders;
    private static ConfigEntry<bool>? shootSpores;
    private static ConfigEntry<bool>? shootScorpions;
    private static ConfigEntry<bool>? shootDynamite;

    private static PropertyInfo? mobStateProperty;
    private static object? mobStateDead;

    internal static bool FriendlyFire => friendlyFire?.Value ?? true;

    internal static bool CanShootZombies => shootZombies?.Value ?? true;

    internal static bool CanShootMandrake => shootMandrake?.Value ?? false;

    internal static bool CanShootBeetles => shootBeetles?.Value ?? false;

    internal static bool CanShootSpiders => shootSpiders?.Value ?? false;

    internal static bool CanShootSpores => shootSpores?.Value ?? false;

    internal static bool CanShootScorpions => shootScorpions?.Value ?? false;

    internal static bool CanShootDynamite => shootDynamite?.Value ?? false;

    internal static void Bind(ConfigFile config)
    {
        friendlyFire = config.Bind(
            "Combat",
            "FriendlyFire",
            true,
            "If true, shotguns injure other scouts. If false, hits still ragdoll / shove them but deal no Injury.");

        shootZombies = config.Bind("Shootables", "Zombies", true, "Shotguns can hit and knock down zombies.");
        shootMandrake = config.Bind("Shootables", "Mandrake", false, "Shotguns can destroy mandrakes.");
        shootBeetles = config.Bind("Shootables", "Beetles", false, "Shotguns can kill beetles.");
        shootSpiders = config.Bind("Shootables", "Spiders", false, "Shotguns can stun spiders (same as throwing an item at them).");
        shootSpores = config.Bind("Shootables", "Spores", false, "Shotguns can break spore bombs and clear spore clouds.");
        shootScorpions = config.Bind("Shootables", "Scorpions", false, "Shotguns can kill scorpions.");
        shootDynamite = config.Bind("Shootables", "Dynamite", false, "Shotguns can light dynamite fuses.");
    }

    /// <summary>
    /// Applies the shotgun hit to whatever is under the collider, if that target is enabled in config.
    /// Returns true when a target was handled (so the pellet should not also hit something else).
    /// </summary>
    internal static bool TryHit(
        Collider collider,
        Character? shooter,
        Action_Gun gun,
        Vector3 hitPoint,
        Vector3 direction)
    {
        if (collider == null)
        {
            return false;
        }

        Character? character = collider.GetComponentInParent<Character>();
        if (character != null && character != shooter)
        {
            return TryHitCharacter(character, gun, hitPoint, direction);
        }

        if (CanShootSpiders)
        {
            Spider? spider = collider.GetComponentInParent<Spider>();
            if (spider != null)
            {
                spider.Bonk();
                return true;
            }

            SpiderTrigger? trigger = collider.GetComponentInParent<SpiderTrigger>();
            if (trigger != null)
            {
                trigger.Bonk();
                return true;
            }
        }

        if (CanShootBeetles)
        {
            Beetle? beetle = collider.GetComponentInParent<Beetle>();
            if (beetle != null)
            {
                KillMob(beetle);
                return true;
            }
        }

        if (CanShootScorpions)
        {
            Scorpion? scorpion = collider.GetComponentInParent<Scorpion>();
            if (scorpion != null)
            {
                KillMob(scorpion);
                return true;
            }
        }

        if (CanShootMandrake)
        {
            Mandrake? mandrake = collider.GetComponentInParent<Mandrake>();
            if (mandrake != null)
            {
                Item? mandrakeItem = mandrake.item != null ? mandrake.item : mandrake.GetComponent<Item>();
                if (mandrakeItem != null)
                {
                    RequestHostDestroy(gun, mandrakeItem.gameObject);
                }

                return true;
            }
        }

        if (CanShootDynamite)
        {
            Dynamite? dynamite = collider.GetComponentInParent<Dynamite>();
            if (dynamite != null)
            {
                dynamite.LightFlare();
                return true;
            }
        }

        if (CanShootSpores)
        {
            CloudFungus? fungus = collider.GetComponentInParent<CloudFungus>();
            if (fungus != null)
            {
                RequestHostBreakFungus(gun, fungus);
                return true;
            }

            StatusEmitter? emitter = collider.GetComponentInParent<StatusEmitter>();
            if (emitter != null && emitter.statusType == CharacterAfflictions.STATUSTYPE.Spores)
            {
                RequestHostDestroy(gun, emitter.gameObject);
                return true;
            }
        }

        return false;
    }

    private static bool TryHitCharacter(Character character, Action_Gun gun, Vector3 hitPoint, Vector3 direction)
    {
        if (GunCharacterLaunch.IsZombie(character))
        {
            if (!CanShootZombies)
            {
                return false;
            }

            GunCharacterLaunch? launch = character.GetComponent<GunCharacterLaunch>();
            if (character.photonView.IsMine)
            {
                launch?.Blast(direction, hitPoint);
            }
            else
            {
                character.photonView.RPC(nameof(GunCharacterLaunch.RPC_ShotgunBlast), RpcTarget.All, direction, hitPoint);
            }

            return true;
        }

        // Scouts: always impact; Injury only when FriendlyFire is on.
        gun.photonView.RPC(nameof(Action_Gun.RPC_GunImpact), RpcTarget.All, character.photonView.Owner, hitPoint, direction);
        return true;
    }

    private static void KillMob(Mob mob)
    {
        if (mob == null)
        {
            return;
        }

        EnsureMobDeadCache();
        if (mobStateProperty != null && mobStateDead != null)
        {
            try
            {
                mobStateProperty.SetValue(mob, mobStateDead);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not kill mob '{mob.name}': {e.Message}");
            }
        }

        if (mob.photonView != null && mob.photonView.ViewID != 0)
        {
            // Dead = 3 on Mob.MobState; mirrors cooking a scorpion/beetle.
            mob.photonView.RPC("RPC_SyncMobState", RpcTarget.All, 3);
        }

        Rigidbody? rig = mob.GetComponent<Rigidbody>();
        if (rig != null && !rig.isKinematic)
        {
            rig.AddForce(Vector3.up * 8f + UnityEngine.Random.insideUnitSphere * 4f, ForceMode.VelocityChange);
        }
    }

    private static void EnsureMobDeadCache()
    {
        if (mobStateProperty != null)
        {
            return;
        }

        Type mobType = typeof(Mob);
        mobStateProperty = mobType.GetProperty("mobState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Type? stateType = mobType.GetNestedType("MobState", BindingFlags.Public | BindingFlags.NonPublic);
        if (stateType != null)
        {
            mobStateDead = Enum.Parse(stateType, "Dead");
        }
    }

    private static void RequestHostDestroy(Action_Gun gun, GameObject target)
    {
        PhotonView? view = target.GetComponent<PhotonView>() ?? target.GetComponentInParent<PhotonView>();
        if (view == null)
        {
            UnityEngine.Object.Destroy(target);
            return;
        }

        if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient || view.IsMine)
        {
            PhotonNetwork.Destroy(view.gameObject);
            return;
        }

        gun.photonView.RPC(nameof(Action_Gun.RPC_HostDestroyView), RpcTarget.MasterClient, view.ViewID);
    }

    private static void RequestHostBreakFungus(Action_Gun gun, CloudFungus fungus)
    {
        PhotonView? view = fungus.GetComponent<PhotonView>();
        if (view == null)
        {
            fungus.Break();
            return;
        }

        if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient || view.IsMine)
        {
            fungus.Break();
            return;
        }

        gun.photonView.RPC(nameof(Action_Gun.RPC_HostBreakFungus), RpcTarget.MasterClient, view.ViewID);
    }
}
