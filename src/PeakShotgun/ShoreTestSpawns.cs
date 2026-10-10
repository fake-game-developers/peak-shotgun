using System.Collections;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.Core;

namespace Peak.Shotgun;

internal static class ShoreTestSpawns
{
    private const int ZombieCount = 3;

    /// <summary>Photon resource names tried for a Shore-style suitcase.</summary>
    private static readonly string[] LuggagePrefabNames =
    {
        "LuggageBig",
        "Luggage",
        "LuggageSmall",
        "LuggageEpic",
    };

    internal static IEnumerator WhenTheShoreIsReady()
    {
        if (!Plugin.DebugMode)
        {
            yield break;
        }

        var wait = new WaitForSecondsRealtime(1f);
        bool spawnedShore = false;
        bool spawnedAirport = false;
        while (!spawnedShore || !spawnedAirport)
        {
            yield return wait;
            // Debug mode can be switched off in the .cfg while this waits for a room or the Shore.
            if (!Plugin.DebugMode)
            {
                yield break;
            }

            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || LoadingScreenHandler.loading)
            {
                continue;
            }

            Character? player = Character.localCharacter;
            if (player == null || Plugin.ShotgunPrefab == null)
            {
                continue;
            }

            if (!spawnedAirport && IsAirport())
            {
                SpawnShotgun(player, "Airport");
                SpawnAmmoPile(player, "Airport");
                SpawnLuggage(player, "Airport");
                spawnedAirport = true;
                continue;
            }

            if (!spawnedShore
                && MapHandler.ExistsAndInitialized
                && Singleton<MapHandler>.Instance.GetCurrentBiome() == Biome.BiomeType.Shore)
            {
                SpawnNear(player);
                SpawnShotgun(player, "Shore");
                SpawnAmmoPile(player, "Shore");
                spawnedShore = true;
            }
        }
    }

    private static bool IsAirport() =>
        SceneManager.GetActiveScene().name == "Airport";

    private static void SpawnShotgun(Character player, string place)
    {
        GameObject? shotgun = Plugin.ShotgunPrefab;
        if (shotgun == null)
        {
            return;
        }

        Vector3 point = GroundPoint(player.Center + player.transform.forward * 4f);

        try
        {
            PhotonNetwork.InstantiateItemRoom(shotgun.name, point, Quaternion.identity, false);
            Plugin.Log.LogInfo($"Spawned a test shotgun at the {place}.");
        }
        catch (System.Exception exception)
        {
            Plugin.Log.LogError($"Could not spawn the {place} test shotgun: {exception.Message}");
        }
    }

    private static void SpawnAmmoPile(Character player, string place)
    {
        GameObject? pile = Plugin.AmmoPilePrefab;
        if (pile == null)
        {
            Plugin.Log.LogWarning($"No ammo pile prefab yet; skipped {place} debug pile.");
            return;
        }

        // Slightly right of the test shotgun so both are obvious.
        Vector3 point = GroundPoint(
            player.Center + player.transform.forward * 4f + player.transform.right * 1.5f);

        try
        {
            PhotonNetwork.InstantiateItemRoom(pile.name, point, Quaternion.identity, false);
            Plugin.Log.LogInfo($"Spawned a test ammo pile at the {place}.");
        }
        catch (System.Exception exception)
        {
            Plugin.Log.LogError($"Could not spawn the {place} test ammo pile: {exception.Message}");
        }
    }

    private static void SpawnLuggage(Character player, string place)
    {
        // Slightly left of the test shotgun so both are visible.
        Vector3 point = GroundPoint(
            player.Center + player.transform.forward * 4f - player.transform.right * 2f);
        Quaternion rotation = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f);

        foreach (string prefabName in LuggagePrefabNames)
        {
            try
            {
                GameObject? spawned = PhotonNetwork.Instantiate(prefabName, point, rotation, 0);
                if (spawned == null)
                {
                    continue;
                }

                Luggage? luggage = spawned.GetComponent<Luggage>();
                if (luggage == null)
                {
                    Plugin.Log.LogWarning(
                        $"Spawned '{prefabName}' at the {place} but it has no Luggage component.");
                    continue;
                }

                // Roots loot pool so RootsLuggagePatch injects a shotgun (zombie biome only).
                // Debug luggage on the Airport/Shore still uses this pool so a shotgun appears for testing.
                luggage.spawnPool = SpawnPool.LuggageRoots;
                luggage.OpenImmediatelyNoNotify();
                Plugin.Log.LogInfo($"Spawned test luggage '{prefabName}' at the {place}.");
                return;
            }
            catch (System.Exception exception)
            {
                Plugin.Log.LogWarning(
                    $"Could not spawn luggage prefab '{prefabName}' at the {place}: {exception.Message}");
            }
        }

        Plugin.Log.LogError($"Could not spawn any luggage prefab at the {place}.");
    }

    private static Vector3 GroundPoint(Vector3 point)
    {
        Vector3 probe = point + Vector3.up * 30f;
        if (Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 80f, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
        {
            // Low hover so gravity can seat the gun; 0.5f was freezing mid-air via settle.
            return hit.point + Vector3.up * 0.08f;
        }

        return point;
    }

    private static void SpawnNear(Character player)
    {
        Vector3 center = player.Center;
        int spawned = 0;
        for (int i = 0; i < ZombieCount; i++)
        {
            float angle = i * (360f / ZombieCount);
            Vector3 offset = Quaternion.Euler(0f, angle, 0f) * (player.transform.forward * 14f);
            Vector3 point = center + offset;
            Vector3 probe = point + Vector3.up * 30f;
            if (Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 80f, HelperFunctions.terrainMapMask, QueryTriggerInteraction.Ignore))
            {
                point = hit.point + Vector3.up * 0.5f;
            }

            try
            {
                PhotonNetwork.Instantiate("MushroomZombie", point, Quaternion.identity, 0);
                spawned++;
            }
            catch (System.Exception exception)
            {
                Plugin.Log.LogError($"Could not spawn a Shore test zombie: {exception.Message}");
                return;
            }
        }

        Plugin.Log.LogInfo($"Spawned {spawned} test zombies on the Shore.");
    }
}
