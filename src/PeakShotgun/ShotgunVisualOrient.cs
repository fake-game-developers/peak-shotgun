using UnityEngine;

namespace PeakShotgun;

/// <summary>
/// Lives on the ShotgunVisual child so networked clones cannot lose the mesh reference.
/// Switches rest vs held local pose every frame from the parent Item state.
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

        if (lastHeld == held)
        {
            // Still re-apply while held so nothing else can leave us stuck in rest pose.
            // Also re-apply armed luggage pose so ground rest cannot wipe the diagonal.
            if (!held)
            {
                if (luggageRest != null && luggageRest.Armed)
                {
                    transform.localPosition = luggageRest.LocalPosition;
                    transform.localRotation = luggageRest.LocalRotation;
                }

                return;
            }
        }

        bool justHeld = held && lastHeld != true;
        lastHeld = held;
        if (held)
        {
            transform.localPosition = Plugin.HeldLocalPosition;
            transform.localRotation = Plugin.HeldLocalRotation;
            if (justHeld)
            {
                LogHeldGeometry();
            }
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
            $"Held shotgun (camera space): item={Cam(item.transform.position)}, anchorR={Cam(item.transform.Find("Hand_R").position)}, "
            + $"anchorL={Cam(item.transform.Find("Hand_L").position)}, handR={Cam(holder.GetBodypart(BodypartType.Hand_R).transform.position)}, "
            + $"handL={Cam(holder.GetBodypart(BodypartType.Hand_L).transform.position)}, headBone={Cam(head.position)}, "
            + $"itemScale={item.transform.lossyScale}, headScale={head.lossyScale}, defaultPos={item.defaultPos}.");
    }
}
