using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace Peak.Shotgun;

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
            // Capacity from host-synced ShotCount only — never invent ItemUses on clients.
            Item.totalUses = Plugin.ShotCount;
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

        if (GetComponent<ShotgunLuggageRest>() == null)
        {
            gameObject.AddComponent<ShotgunLuggageRest>();
        }

        if (visual != null && visual.GetComponent<ShotgunVisualOrient>() == null)
        {
            visual.gameObject.AddComponent<ShotgunVisualOrient>();
        }
    }

    private void Start()
    {
        if (Item != null)
        {
            Item.totalUses = Plugin.ShotCount;
        }

        // Host fills a brand-new magazine when instance data never arrived (loot / debug spawn).
        // Drop/equip already apply networked ItemUses — leave those alone.
        if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient)
        {
            StartCoroutine(HostEnsureNewMagazine());
        }
    }

    private IEnumerator HostEnsureNewMagazine()
    {
        // After Item.Start / SetItemInstanceDataRPC on the same spawn.
        yield return null;
        if (Item == null)
        {
            yield break;
        }

        Item.totalUses = Plugin.ShotCount;
        if (Item.HasData(DataEntryKey.ItemUses))
        {
            OptionableIntItemData existing = Item.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
            if (existing.HasData && existing.Value >= 0)
            {
                yield break;
            }
        }

        Action_Ammo? ammo = GetComponent<Action_Ammo>();
        ammo?.EnsureHostMagazine();
    }

    private void OnEnable() => Active.Add(this);

    private void OnDisable() => Active.Remove(this);

    private void OnDestroy() => Active.Remove(this);
}
