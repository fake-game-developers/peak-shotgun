using UnityEngine;

namespace PeakShotgun;

/// <summary>
/// Lives on the ShotgunVisual child so networked clones cannot lose the mesh reference.
/// Switches rest vs held local pose every frame from the parent Item state.
/// </summary>
internal sealed class ShotgunVisualOrient : MonoBehaviour
{
    private static readonly int PlayerPosId = Shader.PropertyToID("PlayerPos");

    private Item? item;
    private Mesh? mesh;
    private bool? lastHeld;
    private ShotgunLuggageRest? luggageRest;
    private MeshRenderer? meshRenderer;
    private MaterialPropertyBlock? propertyBlock;
    private int appliedPoseVersion = -1;

    private void Awake()
    {
        item = GetComponentInParent<Item>();
        mesh = GetComponent<MeshFilter>()?.sharedMesh;
        luggageRest = GetComponent<ShotgunLuggageRest>();
        meshRenderer = GetComponent<MeshRenderer>();
        propertyBlock = new MaterialPropertyBlock();
    }

    private void LateUpdate()
    {
        KeepPlayerFadeAway();
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

        lastHeld = held;
        if (held)
        {
            transform.localPosition = Plugin.HeldLocalPosition;
            transform.localRotation = Plugin.HeldLocalRotation;
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

    private void OnWillRenderObject() => KeepPlayerFadeAway();

    private void KeepPlayerFadeAway()
    {
        if (meshRenderer == null || propertyBlock == null)
        {
            return;
        }

        meshRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetVector(PlayerPosId, new Vector4(10000f, 10000f, 10000f, 0f));
        meshRenderer.SetPropertyBlock(propertyBlock);
    }
}
