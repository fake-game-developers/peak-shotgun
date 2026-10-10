using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace Peak.Shotgun;

/// <summary>
/// Host-only: when <see cref="Plugin.SpawnAmmoPilesAtCampfire"/> is on, place ammo piles around a
/// campfire at the same moment vanilla spawns marshmallows (<c>MapHandler.SpawnCampfireItems</c>).
/// </summary>
internal static class CampfireAmmoSpawner
{
    private static readonly HashSet<int> SpawnedKeys = new();

    internal static void ClearRunState() => SpawnedKeys.Clear();

    /// <summary>Spawn around the campfire under this root (called from <c>SpawnCampfireItems</c>).</summary>
    internal static void TrySpawnAtRoot(GameObject? campfireRoot)
    {
        if (campfireRoot == null)
        {
            return;
        }

        Campfire? campfire = campfireRoot.GetComponentInChildren<Campfire>(true);
        if (campfire == null)
        {
            Plugin.Log.LogWarning("Campfire ammo: SpawnCampfireItems root had no Campfire component.");
            return;
        }

        TrySpawnAround(campfire);
    }

    /// <summary>Host catch-up: current segment campfire after map load / join.</summary>
    internal static IEnumerator WhenMapIsReady()
    {
        if (!Plugin.SpawnAmmoPilesAtCampfire)
        {
            yield break;
        }

        var wait = new WaitForSecondsRealtime(0.5f);
        for (int i = 0; i < 60; i++)
        {
            yield return wait;
            if (!Plugin.SpawnAmmoPilesAtCampfire)
            {
                yield break;
            }

            if (Plugin.AmmoPilePrefab == null
                || !PhotonNetwork.InRoom
                || !PhotonNetwork.IsMasterClient
                || LoadingScreenHandler.loading
                || !MapHandler.ExistsAndInitialized)
            {
                continue;
            }

            Campfire? current = null;
            try
            {
                current = MapHandler.CurrentCampfire;
            }
            catch (System.Exception)
            {
                // Segment root not ready yet.
            }

            if (current == null)
            {
                continue;
            }

            TrySpawnAround(current);
            yield break;
        }
    }

    internal static void TrySpawnAround(Campfire campfire)
    {
        if (!Plugin.SpawnAmmoPilesAtCampfire || campfire == null)
        {
            return;
        }

        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            return;
        }

        if (IsPortableOrWings(campfire))
        {
            return;
        }

        int key = CampfireKey(campfire);
        if (!SpawnedKeys.Add(key))
        {
            return;
        }

        if (!campfire.isActiveAndEnabled)
        {
            // Wait until the fire is the active segment campfire before placing piles.
            campfire.StartCoroutine(SpawnWhenReady(campfire, key));
            return;
        }

        campfire.StartCoroutine(SpawnWhenReady(campfire, key));
    }

    private static IEnumerator SpawnWhenReady(Campfire campfire, int key)
    {
        var wait = new WaitForSecondsRealtime(0.25f);
        for (int i = 0; i < 40; i++)
        {
            if (!Plugin.SpawnAmmoPilesAtCampfire || campfire == null)
            {
                SpawnedKeys.Remove(key);
                yield break;
            }

            if (Plugin.AmmoPilePrefab == null || (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient))
            {
                yield return wait;
                continue;
            }

            if (PhotonNetwork.InRoom && (PhotonNetwork.CurrentRoom == null || LoadingScreenHandler.loading))
            {
                yield return wait;
                continue;
            }

            if (!campfire.isActiveAndEnabled)
            {
                yield return wait;
                continue;
            }

            SpawnPiles(campfire, Plugin.AmmoPilesPerCampfire);
            yield break;
        }

        SpawnedKeys.Remove(key);
        Plugin.Log.LogWarning(
            $"Timed out waiting to spawn campfire ammo piles ({campfire?.advanceToSegment}).");
    }

    private static void SpawnPiles(Campfire campfire, int count)
    {
        GameObject? prefab = Plugin.AmmoPilePrefab;
        if (prefab == null || count < 1)
        {
            return;
        }

        Vector3 center = campfire.transform.position;
        int spawned = 0;
        for (int i = 0; i < count; i++)
        {
            float angle = (360f / Mathf.Max(count, 1)) * i + 35f;
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * (Vector3.forward * 2.4f);
            Vector3 point = GroundPoint(center + offset);
            Quaternion rotation = Quaternion.Euler(0f, angle + 180f, 0f);

            try
            {
                if (PhotonNetwork.InRoom)
                {
                    PhotonNetwork.InstantiateItemRoom(prefab.name, point, rotation, false);
                }
                else
                {
                    Object.Instantiate(prefab, point, rotation).SetActive(true);
                }

                spawned++;
                Plugin.Log.LogInfo(
                    $"Ammo pile spawn point {point} (campfire {center}, segment {campfire.advanceToSegment}).");
            }
            catch (System.Exception exception)
            {
                Plugin.Log.LogError($"Could not spawn an ammo pile at a campfire: {exception.Message}");
            }
        }

        if (spawned > 0)
        {
            Plugin.Log.LogInfo($"Spawned {spawned} ammo pile(s) at campfire ({campfire.advanceToSegment}).");
        }
    }

    private static Vector3 GroundPoint(Vector3 point)
    {
        Vector3 probe = point + Vector3.up * 30f;
        if (Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 80f, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
        {
            return hit.point + Vector3.up * 0.08f;
        }

        // Fall back to a short ray from above the campfire offset if terrain mask misses.
        if (Physics.Raycast(probe, Vector3.down, out hit, 80f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            return hit.point + Vector3.up * 0.08f;
        }

        return point;
    }

    private static int CampfireKey(Campfire campfire)
    {
        PhotonView? view = campfire.GetComponent<PhotonView>();
        if (view != null && view.ViewID > 0)
        {
            return view.ViewID;
        }

        return (int)campfire.advanceToSegment * 1_000_003 ^ campfire.GetInstanceID();
    }

    private static bool IsPortableOrWings(Campfire campfire)
    {
        if (campfire.nameOverride == "NAME_PORTABLE STOVE")
        {
            return true;
        }

        Transform? parent = campfire.transform.parent;
        return parent != null && parent.gameObject.name.ToLowerInvariant().Contains("wings");
    }
}

[HarmonyPatch(typeof(MapHandler), "SpawnCampfireItems")]
internal static class SpawnCampfireItemsAmmoPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameObject campfireRoot) => CampfireAmmoSpawner.TrySpawnAtRoot(campfireRoot);
}
