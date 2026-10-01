using HarmonyLib;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// PEAK centers luggage items on mainRenderer.bounds, then applies offsetLuggagePosition.
/// After OffsetSpawn: pin the visual flat on the suitcase floor, then seat using the real mesh
/// vertices (not renderer.bounds — that AABB-of-AABB sits below the gun after the flat-lay yaw).
/// Do not raycast the luggage collider: open suitcases use a shell that sits the gun on the rim.
/// </summary>
[HarmonyPatch(typeof(Luggage), nameof(Luggage.OffsetSpawn))]
internal static class LuggageShotgunOffsetPatch
{
    [HarmonyPostfix]
    private static void SitFlatOnFloor(Luggage __instance, Item item)
    {
        if (item == null
            || !item.offsetLuggageSpawn
            || __instance is RespawnChest
            || item.GetComponent<ShotgunInstanceSetup>() == null)
        {
            return;
        }

        Transform? visual = item.transform.Find("ShotgunVisual");
        MeshFilter? filter = visual != null ? visual.GetComponent<MeshFilter>() : null;
        Mesh? mesh = filter != null ? filter.sharedMesh : null;
        if (visual == null || mesh == null)
        {
            return;
        }

        Vector3 up = __instance.transform.up;

        // Spawn spot / floor plane from PEAK's placement (Center on spot, then offset lift).
        // Capture before undoing the lift — that plane is the cavity floor, not the collider lid.
        Renderer? renderer = item.mainRenderer;
        Vector3 spawnSpot = renderer != null
            ? renderer.bounds.center - __instance.transform.rotation * item.offsetLuggagePosition
            : item.transform.position - __instance.transform.rotation * item.offsetLuggagePosition;
        float floorAlongUp = Vector3.Dot(spawnSpot, up);

        // Undo PEAK's half-height lift — we reseat from the flat-lay pose ourselves.
        item.transform.position -= __instance.transform.rotation * item.offsetLuggagePosition;

        Quaternion luggageRot = Plugin.LuggageVisualRotation(item.transform, up);
        Vector3 luggagePos = LuggageRestPosition;
        visual.localPosition = luggagePos;
        visual.localRotation = luggageRot;

        float minAlongUp = MinMeshAlongUp(visual, mesh, up);
        float delta = floorAlongUp - minAlongUp + Plugin.LuggageLiftExtra;
        item.transform.position += up * delta;

        Rigidbody? rig = item.GetComponent<Rigidbody>();
        if (rig != null)
        {
            // Zero velocity before kinematic — Unity errors if you write velocity after.
            if (!rig.isKinematic)
            {
                rig.angularVelocity = Vector3.zero;
                rig.linearVelocity = Vector3.zero;
            }

            rig.useGravity = false;
            // Stay put in the suitcase — Ground state would enable solid colliders and let
            // CharacterController stomp the gun under the lining.
            rig.isKinematic = true;
            rig.constraints = RigidbodyConstraints.FreezeAll;
        }

        // Sent to the other clients from LuggageShotgunSyncPatch, after PEAK's own SetKinematicRPC.
        GetOrAddRest(item).Arm(luggagePos, luggageRot);
        ShotgunLootLedger.SavePose(__instance, luggagePos, luggageRot);
        item.ForceSyncForFrames();
        Plugin.Log.LogInfo(
            $"Luggage seat yaw={Plugin.LuggageYaw:0.#}° Δ={delta:0.###} (spawn-spot, liftExtra={Plugin.LuggageLiftExtra:0.###}).");
    }

    internal static Vector3 LuggageRestPosition => new(Plugin.RestPosX, Plugin.RestPosY, Plugin.RestPosZ);

    internal static ShotgunLuggageRest GetOrAddRest(Item item) =>
        item.GetComponent<ShotgunLuggageRest>() ?? item.gameObject.AddComponent<ShotgunLuggageRest>();

    private static float MinMeshAlongUp(Transform visual, Mesh mesh, Vector3 up)
    {
        Vector3[] vertices = mesh.vertices;
        Matrix4x4 localToWorld = visual.localToWorldMatrix;
        float minAlongUp = float.PositiveInfinity;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 world = localToWorld.MultiplyPoint3x4(vertices[i]);
            minAlongUp = Mathf.Min(minAlongUp, Vector3.Dot(world, up));
        }

        return minAlongUp;
    }
}

/// <summary>
/// Luggage calls <c>Spawner.InitializePhysics</c> on the host after placing each item, both for fresh loot
/// (after <c>OffsetSpawn</c>) and for loot restored from a save (<c>SpawnAndTrackFromItemHistory</c>, which
/// restores the seated root transform but skips <c>OffsetSpawn</c>). Restored guns get the seat pose saved in
/// <see cref="ShotgunLootLedger"/> (the current config only when none was saved) and count against the biome
/// cap; then the pose is sent to the other clients so they render and freeze the gun the same way.
/// </summary>
[HarmonyPatch(typeof(Spawner), nameof(Spawner.InitializePhysics))]
internal static class LuggageShotgunSyncPatch
{
    // Restored loot that was moved out of its suitcase before saving keeps its ordinary ground pose.
    private const float RestoreSlack = 0.5f;

    [HarmonyPostfix]
    private static void SyncLuggageRest(Spawner __instance, Item newItem)
    {
        if (__instance is not Luggage luggage
            || luggage is RespawnChest
            || newItem == null
            || !newItem.offsetLuggageSpawn
            || newItem.GetComponent<ShotgunInstanceSetup>() == null)
        {
            return;
        }

        ShotgunLuggageRest rest = LuggageShotgunOffsetPatch.GetOrAddRest(newItem);
        if (!rest.Armed)
        {
            if (!IsInside(luggage, newItem))
            {
                return;
            }

            bool saved = ShotgunLootLedger.TryGetPose(luggage, out Vector3 position, out Quaternion rotation);
            if (!saved)
            {
                position = LuggageShotgunOffsetPatch.LuggageRestPosition;
                rotation = Plugin.LuggageVisualRotation(newItem.transform, luggage.transform.up);
            }

            rest.Arm(position, rotation);
            ShotgunLootLedger.NoteRestored(luggage, RootsLuggagePatch.LuggageBiomeKey(luggage, luggage.spawnPool));
            Plugin.Log.LogInfo(
                $"Restored luggage seat for a saved shotgun in '{luggage.name}' ({(saved ? "saved pose" : "current config")}).");
        }

        rest.Broadcast();
    }

    private static bool IsInside(Luggage luggage, Item item)
    {
        Bounds bounds = HelperFunctions.GetTotalBounds(luggage.meshRenderers);
        bounds.Expand(RestoreSlack);
        return bounds.Contains(item.transform.position);
    }
}
