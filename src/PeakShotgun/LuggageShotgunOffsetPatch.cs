using HarmonyLib;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// PEAK centers luggage items on mainRenderer.bounds, then applies offsetLuggagePosition.
/// After OffsetSpawn: pin the visual flat, then seat the mesh bottom on a real floor hit
/// (raycast into the suitcase). Reconstructing the floor from bounds is intermittent and
/// leaves the gun sunk into the lining.
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

        string floorSource;
        if (!TryFloorAlongUp(__instance, item, up, out float floorAlongUp))
        {
            floorAlongUp = FallbackFloorAlongUp(item, up);
            floorSource = "bounds-fallback";
        }
        else
        {
            floorSource = "raycast";
        }

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
            $"Luggage seat yaw={Plugin.LuggageYaw:0.#}° Δ={delta:0.###} ({floorSource}, liftExtra={Plugin.LuggageLiftExtra:0.###}).");
    }

    /// <summary>
    /// Cast down onto the suitcase mesh after the flat-lay pose. Ignores the gun and other loot
    /// so the mesh bottom lands on the lining, not halfway through gloves.
    /// </summary>
    private static bool TryFloorAlongUp(Luggage luggage, Item item, Vector3 up, out float floorAlongUp)
    {
        floorAlongUp = 0f;

        Renderer? renderer = item.mainRenderer;
        if (renderer == null)
        {
            return false;
        }

        Bounds bounds = renderer.bounds;
        float probeHeight = Mathf.Max(bounds.extents.magnitude, 0.25f) + 0.35f;
        Vector3 origin = bounds.center + up * probeHeight;
        float maxDistance = probeHeight + Mathf.Max(bounds.extents.magnitude, 0.25f) + 1.5f;

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            -up,
            maxDistance,
            ~0,
            QueryTriggerInteraction.Ignore);

        Collider[] self = item.colliders != null && item.colliders.Length > 0
            ? item.colliders
            : item.GetComponentsInChildren<Collider>(true);

        float bestAlongUp = float.NegativeInfinity;
        bool found = false;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || IsOwnCollider(hit.collider, self))
            {
                continue;
            }

            if (hit.collider.GetComponentInParent<Luggage>() != luggage)
            {
                continue;
            }

            float along = Vector3.Dot(hit.point, up);
            if (along > bestAlongUp)
            {
                bestAlongUp = along;
                found = true;
            }
        }

        if (!found)
        {
            return false;
        }

        floorAlongUp = bestAlongUp;
        return true;
    }

    private static float FallbackFloorAlongUp(Item item, Vector3 up)
    {
        // PEAK lift already undone; item root sits where Center() put the spawn spot.
        return Vector3.Dot(item.transform.position, up);
    }

    private static bool IsOwnCollider(Collider hit, Collider[] self)
    {
        foreach (Collider collider in self)
        {
            if (collider != null && collider == hit)
            {
                return true;
            }
        }

        return false;
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
