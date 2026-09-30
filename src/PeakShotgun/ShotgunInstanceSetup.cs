using System.Collections.Generic;
using UnityEngine;

namespace PeakShotgun;

/// <summary>
/// Re-applies hand anchors and mainRenderer on every spawned instance.
/// Prefab registration / Photon cloning can miss one-shot setup done only at build time.
/// </summary>
internal sealed class ShotgunInstanceSetup : MonoBehaviour
{
    internal static readonly HashSet<ShotgunInstanceSetup> Active = new();

    internal Item? Item { get; private set; }

    private void Awake()
    {
        Item = GetComponent<Item>();
        Transform? visual = transform.Find("ShotgunVisual");
        Mesh? mesh = visual != null ? visual.GetComponent<MeshFilter>()?.sharedMesh : null;
        MeshRenderer? renderer = visual != null ? visual.GetComponent<MeshRenderer>() : null;

        // Rest pose must be applied before luggage Center() / OffsetSpawn in the same frame.
        if (visual != null)
        {
            visual.localPosition = Plugin.RestLocalPosition;
            visual.localRotation = Plugin.RestLocalRotation;
            visual.localScale = Vector3.one * Plugin.ModelScale;
        }

        if (Item != null && renderer != null)
        {
            Item.mainRenderer = renderer;
            Item.addtlRenderers = System.Array.Empty<Renderer>();
            Item.rightHandOnly = false;
        }

        if (mesh != null)
        {
            ShotgunModelSwap.PlaceHandAnchors(gameObject, mesh);
        }

        if (visual != null && mesh != null)
        {
            ShotgunModelSwap.RefitColliders(gameObject, visual.gameObject, mesh);
            if (Item != null)
            {
                Plugin.ConfigureLuggageSpawn(Item, mesh);
            }
        }

        if (GetComponent<ShotgunPhysics>() == null)
        {
            gameObject.AddComponent<ShotgunPhysics>();
        }

        if (visual != null && visual.GetComponent<ShotgunVisualOrient>() == null)
        {
            visual.gameObject.AddComponent<ShotgunVisualOrient>();
        }
    }

    private void OnEnable() => Active.Add(this);

    private void OnDisable() => Active.Remove(this);

    private void OnDestroy() => Active.Remove(this);
}
