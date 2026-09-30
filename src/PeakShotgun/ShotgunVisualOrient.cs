using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// Lives on the ShotgunVisual child so networked clones cannot lose the mesh reference.
/// Switches held / backpack / luggage / rest local pose every frame from the parent Item state.
/// </summary>
internal sealed class ShotgunVisualOrient : MonoBehaviour
{
    private Item? item;
    private Mesh? mesh;
    private bool? lastHeld;
    private ShotgunLuggageRest? luggageRest;
    private int appliedPoseVersion = -1;

    private void Awake()
    {
        item = GetComponentInParent<Item>();
        mesh = GetComponent<MeshFilter>()?.sharedMesh;
        luggageRest = GetComponent<ShotgunLuggageRest>();
    }

    private void LateUpdate()
    {
        item ??= GetComponentInParent<Item>();
        luggageRest ??= GetComponent<ShotgunLuggageRest>();

        // Hand anchors were only placed once, when the prefab was built. Re-place them whenever the pose
        // config changes on disk so Right / Down / Forward / ToeIn apply to the hands live, not just the mesh.
        if (appliedPoseVersion != Plugin.PoseVersion)
        {
            appliedPoseVersion = Plugin.PoseVersion;
            if (item != null && mesh != null)
            {
                ShotgunModelSwap.PlaceHandAnchors(item.gameObject, mesh, log: appliedPoseVersion > 0);
            }
        }

        bool held = item != null
            && (item.itemState == ItemState.Held || item.holderCharacter != null);

        if (held && luggageRest != null)
        {
            luggageRest.Armed = false;
        }

        bool justHeld = held && lastHeld != true;
        lastHeld = held;
        bool inBackpack = !held && item != null && item.itemState == ItemState.InBackpack;
        transform.localScale = Vector3.one * (Plugin.ModelScale * (inBackpack ? BackpackScale : 1f));
        if (held)
        {
            transform.localPosition = Plugin.HeldLocalPosition;
            transform.localRotation = Plugin.HeldLocalRotation;
            if (justHeld)
            {
                LogHeldGeometry();
            }
        }
        else if (inBackpack)
        {
            // Strapped upright on the pack: the barrel (mesh -X) points up the slot, the top (mesh +Z) faces out.
            transform.localPosition = Vector3.zero;
            transform.localRotation = BackpackRotation;
        }
        else if (luggageRest != null && luggageRest.Armed)
        {
            transform.localPosition = luggageRest.LocalPosition;
            transform.localRotation = luggageRest.LocalRotation;
        }
        else
        {
            transform.localPosition = Plugin.RestLocalPosition;
            transform.localRotation = Plugin.RestLocalRotation;
        }
    }

    // Item.SetState(InBackpack) halves the item root (forceScale); this grows the gun back part of the way.
    private const float BackpackScale = 1.4f;

    // Maps mesh -X (muzzle) to item +Y and keeps mesh +Z on item +Z.
    private static readonly Quaternion BackpackRotation = Quaternion.LookRotation(Vector3.forward, Vector3.right);

    /// <summary>
    /// One line per equip, in camera space (+X right, +Y up, +Z forward), so the [HoldPose] values can be
    /// judged against where the camera and the scout's real hands are. Runs a second later so the hold settles.
    /// </summary>
    private void LogHeldGeometry()
    {
        if (item?.holderCharacter == null || !item.holderCharacter.IsLocal)
        {
            return;
        }

        Invoke(nameof(WriteHeldGeometry), 1f);
    }

    private void WriteHeldGeometry()
    {
        Character? holder = item?.holderCharacter;
        Camera? cam = MainCamera.instance != null ? MainCamera.instance.cam : Camera.main;
        if (item == null || holder == null || cam == null)
        {
            return;
        }

        Transform c = cam.transform;
        Vector3 Cam(Vector3 world) => c.InverseTransformPoint(world);
        Transform head = holder.GetBodypart(BodypartType.Head).transform;
        Plugin.Log.LogInfo(
            $"Held shotgun (camera space): item={Cam(item.transform.position)}, target={Cam(holder.refs.items.GetItemHoldPos(item))}, "
            + $"anchorR={Cam(item.transform.Find("Hand_R").position)}, anchorL={Cam(item.transform.Find("Hand_L").position)}, "
            + $"handR={Cam(holder.GetBodypart(BodypartType.Hand_R).transform.position)}, handL={Cam(holder.GetBodypart(BodypartType.Hand_L).transform.position)}, "
            + $"shoulderR={Cam(holder.GetBodypart(BodypartType.Arm_R).transform.position)}, headBone={Cam(head.position)}, "
            + $"itemScale={item.transform.lossyScale}, headScale={head.lossyScale}, defaultPos={item.defaultPos}.");
    }
}
