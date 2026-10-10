using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>Replaces the cloned vanilla mesh with the embedded ammo-pile OBJ + albedo.</summary>
internal static class AmmoPileModelSwap
{
    private const string ObjResource = "Peak.Shotgun.models.ammo_pile.obj";
    private const string AlbedoResource = "Peak.Shotgun.models.ammo_pile_albedo.png";

    internal static void Apply(GameObject pileObject, ItemDatabase database)
    {
        Mesh? mesh = LoadEmbeddedMesh();
        if (mesh == null)
        {
            Plugin.Log.LogWarning("Custom ammo pile mesh could not be loaded; keeping the clone look.");
            return;
        }

        Texture2D? albedo = LoadEmbeddedTexture(AlbedoResource, "AmmoPileAlbedo");
        Material? peakMaterial = FindItemMaterial(database);
        if (peakMaterial == null)
        {
            peakMaterial = FindAnyMaterial(pileObject);
        }

        HideVanillaRenderers(pileObject);

        var visual = new GameObject("AmmoPileVisual");
        visual.transform.SetParent(pileObject.transform, false);
        visual.transform.localPosition = Vector3.zero;
        // Source mesh lies on its side; tip -90° about X so the open top faces up.
        visual.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        visual.transform.localScale = Vector3.one * Plugin.AmmoPileScale;

        var filter = visual.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var renderer = visual.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = BuildMaterial(peakMaterial, albedo);

        RefitCollider(pileObject, visual, mesh);

        Item? item = pileObject.GetComponent<Item>();
        if (item != null)
        {
            item.mainRenderer = renderer;
            item.addtlRenderers = System.Array.Empty<Renderer>();
        }

        Plugin.Log.LogInfo(
            $"Applied ammo pile mesh (scale={Plugin.AmmoPileScale:0.###}, shader={renderer.sharedMaterial?.shader?.name}).");
    }

    private const string ItemShader = "W/Peak_Standard";

    private static Material? FindItemMaterial(ItemDatabase database)
    {
        foreach (Item candidate in database.Objects)
        {
            if (candidate == null)
            {
                continue;
            }

            foreach (Renderer renderer in candidate.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null && material.shader != null && material.shader.name == ItemShader)
                    {
                        return material;
                    }
                }
            }
        }

        return null;
    }

    private static Material? FindAnyMaterial(GameObject root)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.sharedMaterial?.shader != null)
            {
                return renderer.sharedMaterial;
            }
        }

        return null;
    }

    private static Material BuildMaterial(Material? peakMaterial, Texture2D? albedo)
    {
        Material material;
        if (peakMaterial != null)
        {
            material = new Material(peakMaterial.shader);
            material.CopyPropertiesFromMaterial(peakMaterial);
            ClearTextures(material);
        }
        else
        {
            material = new Material(Shader.Find("Hidden/InternalErrorShader"));
        }

        material.name = "AmmoPileCustom";
        if (albedo != null)
        {
            string[] textureProps =
            [
                "_MainTex", "_BaseMap", "_BaseColorMap", "_UnlitColorMap", "_Albedo",
                "_Diffuse", "_Texture", "_MainTexture", "_BaseColorTexture", "_BaseTexture",
            ];
            foreach (string prop in textureProps)
            {
                if (material.HasProperty(prop))
                {
                    material.SetTexture(prop, albedo);
                }
            }

            try
            {
                material.mainTexture = albedo;
            }
            catch
            {
                // ignored
            }

            if (material.HasProperty("_BaseTexAmount"))
            {
                material.SetFloat("_BaseTexAmount", 1f);
            }

            foreach (string prop in new[] { "_Color", "_BaseColor", "_UnlitColor" })
            {
                if (material.HasProperty(prop))
                {
                    material.SetColor(prop, Color.white);
                }
            }
        }

        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", 0f);
        }

        return material;
    }

    private static void ClearTextures(Material material)
    {
        var names = new List<string>();
        material.GetTexturePropertyNames(names);
        foreach (string name in names)
        {
            if (material.HasProperty(name))
            {
                material.SetTexture(name, null);
            }
        }
    }

    private static void HideVanillaRenderers(GameObject root)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is ParticleSystemRenderer)
            {
                continue;
            }

            Object.DestroyImmediate(renderer);
        }

        foreach (MeshFilter meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Object.DestroyImmediate(meshFilter);
        }
    }

    private static void RefitCollider(GameObject root, GameObject visual, Mesh mesh)
    {
        Item? item = root.GetComponent<Item>();
        bool registered = item != null && Item.ALL_ITEMS.Contains(item);

        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            if (registered
                && Item.COLLIDER_TO_ITEM.TryGetValue(collider, out Item owner)
                && owner == item)
            {
                Item.COLLIDER_TO_ITEM.Remove(collider);
            }

            Object.DestroyImmediate(collider);
        }

        BoxCollider box = visual.AddComponent<BoxCollider>();
        box.isTrigger = false;
        box.center = mesh.bounds.center;
        box.size = mesh.bounds.size;

        if (item == null)
        {
            return;
        }

        item.colliders = root.GetComponentsInChildren<Collider>(true);
        if (registered)
        {
            foreach (Collider collider in item.colliders)
            {
                Item.COLLIDER_TO_ITEM[collider] = item;
            }
        }
    }

    private static Mesh? LoadEmbeddedMesh()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ObjResource);
        if (stream == null)
        {
            Plugin.Log.LogWarning($"Embedded mesh resource '{ObjResource}' was not found.");
            return null;
        }

        return ObjMeshLoader.Load(stream, "AmmoPileMesh");
    }

    private static Texture2D? LoadEmbeddedTexture(string resourceName, string textureName)
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            Plugin.Log.LogWarning($"Embedded texture '{resourceName}' was not found.");
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(memory.ToArray()))
        {
            Plugin.Log.LogWarning($"Embedded texture '{resourceName}' could not be decoded.");
            return null;
        }

        texture.name = textureName;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.anisoLevel = 4;
        return texture;
    }
}
