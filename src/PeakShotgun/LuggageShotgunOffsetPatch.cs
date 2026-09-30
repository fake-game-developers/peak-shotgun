using HarmonyLib;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// PEAK centers luggage items on mainRenderer.bounds, then applies offsetLuggagePosition.
/// After OffsetSpawn: pin the visual flat on the suitcase floor, then seat using the real mesh
/// vertices (not renderer.bounds — that AABB-of-AABB sits below the gun after the flat-lay yaw).
/// Do not raycast the luggage collider: open suitcases use a shell that sits the gun on the rim.
/// </summary>
// String target: PEAKGameLibs (CI) strips OffsetSpawn from Luggage; the live game DLL still has it.
[HarmonyPatch(typeof(Luggage), "OffsetSpawn")]
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
        Vector3 luggagePos = new(Plugin.RestPosX, Plugin.RestPosY, Plugin.RestPosZ);
        visual.localPosition = luggagePos;
        visual.localRotation = luggageRot;

        ShotgunLuggageRest pose = visual.GetComponent<ShotgunLuggageRest>()
            ?? visual.gameObject.AddComponent<ShotgunLuggageRest>();
        pose.LocalPosition = luggagePos;
        pose.LocalRotation = luggageRot;
        pose.Armed = true;

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

        item.GetComponent<ShotgunPhysics>()?.Apply();
        item.ForceSyncForFrames();
        Plugin.Log.LogInfo(
            $"Luggage seat yaw={Plugin.LuggageYaw:0.#}° Δ={delta:0.###} (spawn-spot, liftExtra={Plugin.LuggageLiftExtra:0.###}).");
    }

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
