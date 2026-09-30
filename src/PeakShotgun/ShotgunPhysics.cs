using HarmonyLib;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// Legs must not shove the gun. CharacterController ignores Rigidbody.excludeLayers, so a solid
/// Ground collider lets scouts stomp the gun under the luggage lining (food uses tiny colliders;
/// our mesh boxes do not). While seated in luggage: kinematic + trigger colliders.
/// While InBackpack: leave PEAK's disabled/trigger colliders alone — Refit would break stashing.
/// </summary>
internal sealed class ShotgunPhysics : MonoBehaviour
{
    private Item? item;
    private Rigidbody? rig;
    private float groundGrace;

    private void Awake()
    {
        item = GetComponent<Item>();
        rig = GetComponent<Rigidbody>();
        // Fresh spawns start at rest above the sand — give gravity a moment before settle-freeze.
        groundGrace = 1.25f;
        Apply();
    }

    private void OnEnable() => Apply();

    private void FixedUpdate()
    {
        if (groundGrace > 0f)
        {
            groundGrace -= Time.fixedDeltaTime;
        }

        if (item == null || rig == null)
        {
            return;
        }

        if (item.itemState == ItemState.Held
            || item.itemState == ItemState.InBackpack
            || item.holderCharacter != null)
        {
            return;
        }

        Transform? visual = transform.Find("ShotgunVisual");
        ShotgunLuggageRest? luggageRest = visual != null ? visual.GetComponent<ShotgunLuggageRest>() : null;
        if (luggageRest != null && luggageRest.Armed)
        {
            return;
        }

        ApplyGround();
    }

    internal void Apply()
    {
        item ??= GetComponent<Item>();
        rig ??= GetComponent<Rigidbody>();
        if (rig == null || item == null)
        {
            return;
        }

        if (item.itemState == ItemState.InBackpack)
        {
            SetKinematicFrozen(rig);
            rig.excludeLayers = 0;
            SetColliders(enabled: false, isTrigger: true);
            return;
        }

        bool held = item.itemState == ItemState.Held || item.holderCharacter != null;
        Transform? visual = transform.Find("ShotgunVisual");
        ShotgunLuggageRest? luggageRest = visual != null ? visual.GetComponent<ShotgunLuggageRest>() : null;
        bool seatedInLuggage = luggageRest != null && luggageRest.Armed;
        int characterMask = CharacterLayerMask();

        if (held)
        {
            int exclude = characterMask;
            int defaultLayer = LayerMask.NameToLayer("Default");
            if (defaultLayer >= 0)
            {
                exclude |= 1 << defaultLayer;
            }

            // PEAK holds an item as a DYNAMIC rigidbody, not as a child of the hand:
            //  - CharacterItems.FixedUpdate → HoldItem adds force/torque every physics step to pull the
            //    item to the hold pose (hip + look direction) and to rotate it to face where you look.
            //  - CharacterItems.AttachItem joins both hand bones to item.rig with joints.
            // Making the body kinematic / FreezeAll here makes those forces do nothing, so the gun stays
            // fixed in world space, and the hand joints then tether the scout to that fixed gun
            // (stuck in mid-air, cannot move). Vanilla Item.SetState(Held) uses: isKinematic = false,
            // useGravity = false. Restore exactly that, and clear the FreezeAll left over from the
            // luggage / ground / backpack states.
            // Solid colliders are still turned into triggers so the big mesh cannot shove the scout.
            rig.isKinematic = false;
            rig.useGravity = false;
            rig.constraints = RigidbodyConstraints.None;
            rig.excludeLayers = exclude;
            SetColliders(enabled: true, isTrigger: true);
            return;
        }

        if (seatedInLuggage)
        {
            SetKinematicFrozen(rig);
            rig.excludeLayers = characterMask;
            SetColliders(enabled: true, isTrigger: true);
            return;
        }

        ApplyGround();
    }

    private void ApplyGround()
    {
        if (rig == null || item == null)
        {
            return;
        }

        rig.excludeLayers = CharacterLayerMask();
        SetColliders(enabled: true, isTrigger: false);

        float gap = GapAboveTerrain(item);
        bool nearGround = gap >= -0.05f && gap < 0.08f;
        bool settled = nearGround
            && groundGrace <= 0f
            && (!rig.isKinematic
                ? rig.linearVelocity.sqrMagnitude < 0.05f && rig.angularVelocity.sqrMagnitude < 0.05f
                : true);

        if (settled)
        {
            SetKinematicFrozen(rig);
            if (gap < -0.02f)
            {
                item.transform.position += Vector3.up * (-gap + 0.02f);
            }

            return;
        }

        if (rig.isKinematic)
        {
            rig.isKinematic = false;
        }

        rig.useGravity = true;
        rig.constraints = RigidbodyConstraints.None;
    }

    /// <summary>
    /// Clear velocities only while dynamic. Writing velocity on a kinematic body errors every frame.
    /// </summary>
    private static void SetKinematicFrozen(Rigidbody rig)
    {
        if (!rig.isKinematic)
        {
            rig.linearVelocity = Vector3.zero;
            rig.angularVelocity = Vector3.zero;
            rig.isKinematic = true;
        }

        rig.useGravity = false;
        rig.constraints = RigidbodyConstraints.FreezeAll;
    }

    private static int CharacterLayerMask()
    {
        int mask = 0;
        foreach (string name in new[] { "Character", "Player", "Characters" })
        {
            int layer = LayerMask.NameToLayer(name);
            if (layer >= 0)
            {
                mask |= 1 << layer;
            }
        }

        return mask;
    }

    /// <summary>
    /// Signed gap between mesh bottom and terrain: positive = floating, negative = buried, -1 = none.
    /// Skips the gun's own colliders so we never snap onto ourselves.
    /// </summary>
    private static float GapAboveTerrain(Item item)
    {
        Renderer? renderer = item.mainRenderer;
        if (renderer == null)
        {
            return -1f;
        }

        Bounds bounds = renderer.bounds;
        Vector3 probe = bounds.center + Vector3.up * (bounds.extents.y + 3f);
        RaycastHit[] hits = Physics.RaycastAll(
            probe,
            Vector3.down,
            bounds.extents.y + 6f,
            HelperFunctions.terrainMapMask,
            QueryTriggerInteraction.Ignore);

        Collider[] self = item.colliders != null && item.colliders.Length > 0
            ? item.colliders
            : item.GetComponentsInChildren<Collider>(true);

        float bestHitY = float.NegativeInfinity;
        bool found = false;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || IsOwnCollider(hit.collider, self))
            {
                continue;
            }

            if (hit.point.y > bestHitY)
            {
                bestHitY = hit.point.y;
                found = true;
            }
        }

        return found ? bestHitY - bounds.min.y : -1f;
    }

    private static bool IsOwnCollider(Collider hit, Collider[] self)
    {
        foreach (Collider c in self)
        {
            if (c != null && c == hit)
            {
                return true;
            }
        }

        return false;
    }

    private void SetColliders(bool enabled, bool isTrigger)
    {
        Collider[] colliders = item != null && item.colliders != null && item.colliders.Length > 0
            ? item.colliders
            : GetComponentsInChildren<Collider>(true);
        foreach (Collider collider in colliders)
        {
            if (collider == null)
            {
                continue;
            }

            collider.enabled = enabled;
            collider.isTrigger = isTrigger;
        }
    }
}

[HarmonyPatch(typeof(Item), nameof(Item.SetState))]
internal static class ShotgunItemStatePatch
{
    [HarmonyPostfix]
    private static void AfterStateChange(Item __instance)
    {
        if (__instance.GetComponent<ShotgunInstanceSetup>() == null)
        {
            return;
        }

        if (__instance.itemState != ItemState.InBackpack)
        {
            Transform? visual = __instance.transform.Find("ShotgunVisual");
            Mesh? mesh = visual != null ? visual.GetComponent<MeshFilter>()?.sharedMesh : null;
            if (visual != null && mesh != null)
            {
                ShotgunModelSwap.RefitColliders(__instance.gameObject, visual.gameObject, mesh);
            }
        }

        __instance.GetComponent<ShotgunPhysics>()?.Apply();
    }
}

/// <summary>
/// While equipping, <c>CharacterItems.Equip</c> places the item at <c>GetItemHoldPos(item, pushOffTerrain: true)</c>
/// and then teleports both hand bones onto its anchors and joins them there. Our hold target sits 1.3-1.5 m out, far
/// past arm's reach, so the arms snap back and can fling the gun through the body, where the torso traps the arms
/// (gun inside the chest, muzzle out the front). The held pose is not networked: every client runs this for every
/// holder, so remote scouts land in that trap on their own. Vanilla's terrain push only pulls toward the hip, which
/// traps it too. Seat the gun <see cref="EquipReach"/> from the right shoulder toward the target instead; the hold
/// force then straightens the arms out. Held colliders are triggers, so terrain needs no push.
/// </summary>
[HarmonyPatch(typeof(CharacterItems), nameof(CharacterItems.GetItemHoldPos))]
internal static class ShotgunHoldPosPatch
{
    // Roughly where the right hand settles in a good hold (shoulder → grip ≈ 0.35 m in the equip log).
    private const float EquipReach = 0.35f;

    [HarmonyPostfix]
    private static void SeatWithinReach(CharacterItems __instance, Item item, bool pushOffTerrain, ref Vector3 __result)
    {
        if (!pushOffTerrain || item == null || item.GetComponent<ShotgunInstanceSetup>() == null)
        {
            return;
        }

        Vector3 shoulder = __instance.character.GetBodypart(BodypartType.Arm_R).transform.position;
        Vector3 target = __instance.GetItemHoldPos(item, pushOffTerrain: false);
        __result = shoulder + Vector3.ClampMagnitude(target - shoulder, EquipReach);
    }
}
