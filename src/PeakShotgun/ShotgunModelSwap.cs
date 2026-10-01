using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Peak.Shotgun;

internal static class ShotgunModelSwap
{
    private const string ObjResource = "Peak.Shotgun.models.shotgun.obj";
    private const string AlbedoResource = "Peak.Shotgun.models.shotgun_albedo.png";

    internal static Transform? Apply(GameObject gunObject, ItemDatabase database)
    {
        Mesh? mesh = LoadEmbeddedMesh();
        if (mesh == null)
        {
            Plugin.Log.LogWarning("Custom shotgun mesh could not be loaded; keeping the blowgun look.");
            return null;
        }

        Texture2D? albedo = LoadEmbeddedTexture(AlbedoResource, "ShotgunAlbedo");
        Material? peakMaterial = FindItemMaterial(database);
        if (peakMaterial == null)
        {
            Plugin.Log.LogWarning($"No {ItemShader} material was found; falling back to the blowgun shader.");
            peakMaterial = FindPeakMaterial(gunObject);
        }

        HideVanillaRenderers(gunObject);

        Plugin.UpdateMeshAnchors(mesh);

        var visual = new GameObject("ShotgunVisual");
        visual.transform.SetParent(gunObject.transform, false);
        // Start in rest pose; ShotgunVisualOrient switches to held when picked up.
        visual.transform.localPosition = Plugin.RestLocalPosition;
        visual.transform.localRotation = Plugin.RestLocalRotation;
        visual.transform.localScale = Vector3.one * Plugin.ModelScale;

        var filter = visual.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var renderer = visual.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = BuildPeakSafeMaterial(peakMaterial, albedo);
        visual.AddComponent<ShotgunVisualOrient>();

        RefitColliders(gunObject, visual, mesh);

        var muzzle = new GameObject("ShotgunMuzzle");
        muzzle.transform.SetParent(visual.transform, false);
        // Barrel end, facing out of the barrel: mesh -X is the aim, mesh +Z is the top.
        muzzle.transform.localPosition = Plugin.MuzzleMeshPoint;
        muzzle.transform.localRotation = Quaternion.LookRotation(Vector3.left, Vector3.forward);

        Item? item = gunObject.GetComponent<Item>();
        if (item != null)
        {
            // Luggage uses mainRenderer.bounds to center items visually. If this still points at the
            // hidden blowgun mesh, the shotgun is shifted under the suitcase and looks "invisible".
            item.mainRenderer = renderer;
            item.addtlRenderers = System.Array.Empty<Renderer>();
            item.rightHandOnly = false;
            PlaceHandAnchors(gunObject, mesh);
            Plugin.ConfigureLuggageSpawn(item, mesh);
        }

        gunObject.AddComponent<ShotgunInstanceSetup>();
        gunObject.AddComponent<ShotgunPhysics>();
        gunObject.AddComponent<ShotgunLuggageRest>();

        Plugin.Log.LogInfo(
            $"Applied custom shotgun mesh (scale={Plugin.ModelScale:0.###}, shader={renderer.sharedMaterial?.shader?.name}, albedo={(albedo != null ? $"{albedo.width}x{albedo.height}" : "null")}).");
        return muzzle.transform;
    }

    /// <summary>
    /// PEAK attaches each hand to direct children named Hand_R / Hand_L (position AND rotation), and holds
    /// the item root at <c>Item.defaultPos</c>. Put the grip at the root, the pump ahead of it, and turn
    /// each hand to wrap its part. The blowgun rotations we clone are for a tube held at the mouth.
    /// </summary>
    internal static void PlaceHandAnchors(GameObject gunObject, Mesh mesh, bool log = true)
    {
        Transform? handR = gunObject.transform.Find("Hand_R");
        Transform? handL = gunObject.transform.Find("Hand_L");
        if (handR == null && handL == null)
        {
            Plugin.Log.LogWarning("Blowgun hand anchors (Hand_R / Hand_L) were missing; cannot set a two-handed grip.");
            return;
        }

        Vector3 gripMesh = Plugin.GripMeshPoint;
        Vector3 forendMesh = Plugin.ForendMeshPoint;
        if (gripMesh == Vector3.zero && forendMesh == Vector3.zero)
        {
            Plugin.UpdateMeshAnchors(mesh);
            gripMesh = Plugin.GripMeshPoint;
            forendMesh = Plugin.ForendMeshPoint;
        }

        Quaternion heldRot = Plugin.HeldLocalRotation;
        Vector3 heldPos = Plugin.HeldLocalPosition;
        float scale = Plugin.ModelScale;

        Vector3 ToItem(Vector3 meshLocal) => heldPos + heldRot * (meshLocal * scale);

        // PEAK's CharacterItems.AttachItem / UpdateAttachedItem snap the scout's Hand_R bone
        // (BodypartType 10) to this item's "Hand_R" child and Hand_L (7) to "Hand_L" — no swapping.
        // The right hand holds the grip / trigger, the left hand reaches forward to the pump.
        if (handR != null)
        {
            Quaternion rightRot = Plugin.HeldAim * Quaternion.Euler(-Plugin.RightWristTilt, 0f, 0f) * RightGripRotation;
            handR.localRotation = rightRot;
            // The anchor is the hand BONE (wrist end), not the palm: the fist closes GripReach along the fingers.
            handR.localPosition = ToItem(gripMesh) - rightRot * (Vector3.up * GripReach);
        }

        if (handL != null)
        {
            handL.localPosition = ToItem(forendMesh) + Plugin.HeldAim * (Vector3.left * Plugin.LeftHandLeft);
            handL.localRotation = Plugin.HeldAim * LeftPumpRotation;
        }

        if (gunObject.TryGetComponent(out Item item))
        {
            item.defaultPos = Plugin.HeldDefaultPos;
        }

        if (log)
        {
            Plugin.Log.LogInfo(
                $"Shotgun hand anchors: grip(Hand_R)={handR?.localPosition}, forend(Hand_L)={handL?.localPosition}, defaultPos={Plugin.HeldDefaultPos}.");
        }
    }

    // Scout hand bones: forward = thumb side of the fist (the gripped axis), up = the fingers,
    // right = palm normal on the right hand (checked against vanilla Torch / Honeycomb / RopeShooter anchors).
    // Right: the vanilla RopeShooter's Hand_R (upright pistol grip), then tipped by RightWristTiltDegrees.
    // A thumb-forward rifle-wrist fist points the fingers down, which brings the forearm in from above.
    private static readonly Quaternion RightGripRotation = new(-0.108f, 0.658f, 0.677f, 0.311f);

    // Metres from the hand bone to the centre of the bar it grips, along the fingers axis. Vanilla anchors
    // around a centred shaft: Torch R 0.134, Torch L 0.160, RopeShooter R 0.141; ~0 along the palm normal.
    private const float GripReach = 0.14f;

    // Left: thumb along the barrel toward the muzzle, palm up under the pump, fingers wrapping up its right side.
    private static readonly Quaternion LeftPumpRotation = Quaternion.LookRotation(Vector3.forward, new Vector3(1f, 0.4f, 0f));

    private static Material? FindPeakMaterial(GameObject gunObject)
    {
        foreach (Renderer renderer in gunObject.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is ParticleSystemRenderer)
            {
                continue;
            }

            Material? mat = renderer.sharedMaterial;
            if (mat?.shader != null
                && !mat.shader.name.Contains("Error")
                && !mat.shader.name.Contains("Hidden")
                && mat.shader.name != "Standard"
                && mat.shader.name != "Diffuse")
            {
                return mat;
            }
        }

        // Fallback: any material with a live shader from the item.
        foreach (Renderer renderer in gunObject.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.sharedMaterial?.shader != null)
            {
                return renderer.sharedMaterial;
            }
        }

        return null;
    }

    /// <summary>
    /// Ordinary PEAK items (flare, dynamite, guidebook…) render with W/Peak_Standard. Other catalog shaders
    /// such as W/Character, W/Peak_Dither and W/Peak_Glass read the screen position and dither the mesh away
    /// when the camera gets close, so do not take whatever non-Character material comes first.
    /// </summary>
    private const string ItemShader = "W/Peak_Standard";

    private static Material? FindItemMaterial(ItemDatabase database)
    {
        // Catalog prefabs never ran Item.Awake, so Item.mainRenderer is usually unset: scan their renderers.
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
                        Plugin.Log.LogInfo($"Using world-item material {material.name} from {candidate.name} (shader={ItemShader}).");
                        return material;
                    }
                }
            }
        }

        return null;
    }

    private static Material BuildPeakSafeMaterial(Material? peakMaterial, Texture2D? albedo)
    {
        Material material;
        if (peakMaterial != null)
        {
            // Match an ordinary world item's material instead of inheriting Character proximity fade.
            material = new Material(peakMaterial.shader);
            material.CopyPropertiesFromMaterial(peakMaterial);
            ClearTextures(material);
        }
        else
        {
            Plugin.Log.LogWarning("No PEAK material found on blowgun; custom mesh may stay invisible.");
            material = new Material(Shader.Find("Hidden/InternalErrorShader"));
        }

        material.name = "ShotgunCustom";
        ApplyAlbedoToMaterial(material, albedo);
        ForceOpaqueDoubleSided(material);
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

    private static void ForceOpaqueDoubleSided(Material material)
    {
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 0f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_AlphaClip"))
        {
            material.SetFloat("_AlphaClip", 0f);
        }

        if (material.HasProperty("_Cull"))
        {
            // 0 = Off (double-sided) so inverted winding still shows.
            material.SetFloat("_Cull", 0f);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 1f);
        }

        material.SetOverrideTag("RenderType", "Opaque");
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
    }

    private static void ApplyAlbedoToMaterial(Material material, Texture2D? albedo)
    {
        if (albedo == null)
        {
            return;
        }

        string[] textureProps =
        [
            "_MainTex",
            "_BaseMap",
            "_BaseColorMap",
            "_UnlitColorMap",
            "_Albedo",
            "_Diffuse",
            "_Texture",
            "_MainTexture",
            "_BaseColorTexture",
            "_BaseTexture",
        ];

        foreach (string prop in textureProps)
        {
            if (material.HasProperty(prop))
            {
                material.SetTexture(prop, albedo);
                material.SetTextureScale(prop, Vector2.one);
                material.SetTextureOffset(prop, Vector2.zero);
            }
        }

        try
        {
            material.mainTexture = albedo;
            material.mainTextureScale = Vector2.one;
            material.mainTextureOffset = Vector2.zero;
        }
        catch
        {
            // ignored
        }

        // W/Peak_Standard blends _BaseTexture over _BaseColor by this amount.
        if (material.HasProperty("_BaseTexAmount"))
        {
            material.SetFloat("_BaseTexAmount", 1f);
        }

        string[] colorProps = ["_Color", "_BaseColor", "_UnlitColor"];
        foreach (string prop in colorProps)
        {
            if (material.HasProperty(prop))
            {
                material.SetColor(prop, Color.white);
            }
        }
    }

    private static void HideVanillaRenderers(GameObject gunObject)
    {
        // Destroy (don't just disable) so Item.AddPropertyBlock / Center() cannot pick the
        // invisible blowgun mesh as mainRenderer and shove the item under the luggage floor.
        foreach (Renderer renderer in gunObject.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is ParticleSystemRenderer)
            {
                continue;
            }

            if (renderer.gameObject.name == "ShotgunVisual")
            {
                continue;
            }

            UnityEngine.Object.DestroyImmediate(renderer);
        }

        foreach (MeshFilter meshFilter in gunObject.GetComponentsInChildren<MeshFilter>(true))
        {
            if (meshFilter.gameObject.name == "ShotgunVisual")
            {
                continue;
            }

            UnityEngine.Object.DestroyImmediate(meshFilter);
        }
    }

    /// <summary>
    /// Tight boxes along barrel (+X): one full AABB is as wide as the receiver over the whole gun,
    /// so the stock feels much fatter than the mesh. Three slices match grip / body / barrel.
    /// </summary>
    private static readonly (float t0, float t1)[] CollisionSegments =
    [
        (0f, 0.34f),
        (0.30f, 0.80f),
        (0.76f, 1f),
    ];

    private static Mesh? layoutMesh;
    private static Bounds[]? layoutBoxes;

    /// <summary>
    /// Fits the collision boxes to the mesh, reusing the boxes already on the visual. Instances inherit the
    /// prefab's boxes, so after the first fit this only rewrites bounds. PEAK's <c>Item.Awake</c> registers
    /// every collider in <c>Item.COLLIDER_TO_ITEM</c> (and the held-item interaction raycast relies on it), so any
    /// collider added or removed after that is registered / unregistered here too.
    /// </summary>
    internal static void RefitColliders(GameObject gunObject, GameObject visual, Mesh mesh)
    {
        Bounds[] layout = ColliderLayout(mesh);
        Item? item = gunObject.GetComponent<Item>();
        // Registering before Item.Awake would make its RegisterItem throw on the duplicate key.
        bool registered = item != null && Item.ALL_ITEMS.Contains(item);

        var kept = new HashSet<Collider>();
        BoxCollider[] existing = visual.GetComponents<BoxCollider>();
        for (int i = 0; i < layout.Length; i++)
        {
            BoxCollider box;
            if (i < existing.Length)
            {
                box = existing[i];
            }
            else
            {
                box = visual.AddComponent<BoxCollider>();
                box.isTrigger = false;
            }

            box.center = layout[i].center;
            box.size = layout[i].size;
            kept.Add(box);
        }

        foreach (Collider collider in gunObject.GetComponentsInChildren<Collider>(true))
        {
            if (kept.Contains(collider))
            {
                continue;
            }

            if (registered
                && Item.COLLIDER_TO_ITEM.TryGetValue(collider, out Item owner)
                && owner == item)
            {
                Item.COLLIDER_TO_ITEM.Remove(collider);
            }

            UnityEngine.Object.DestroyImmediate(collider);
        }

        if (item == null)
        {
            return;
        }

        item.colliders = gunObject.GetComponentsInChildren<Collider>(true);
        if (registered)
        {
            foreach (Collider collider in item.colliders)
            {
                Item.COLLIDER_TO_ITEM[collider] = item;
            }
        }
    }

    private static Bounds[] ColliderLayout(Mesh mesh)
    {
        if (layoutMesh == mesh && layoutBoxes != null)
        {
            return layoutBoxes;
        }

        var boxes = new List<Bounds>();
        Vector3[] vertices = mesh.vertices;
        Bounds barrel = mesh.bounds;
        float xMin = barrel.min.x;
        float xSpan = barrel.max.x - xMin;
        if (xSpan >= 1e-5f && vertices.Length > 0)
        {
            foreach ((float t0, float t1) in CollisionSegments)
            {
                float x0 = xMin + xSpan * t0;
                float x1 = xMin + xSpan * t1;
                if (TryBoundsAlongBarrel(vertices, x0, x1, out Bounds segment))
                {
                    boxes.Add(new Bounds(segment.center, Vector3.Max(segment.size, new Vector3(0.02f, 0.02f, 0.02f))));
                }
            }
        }

        if (boxes.Count == 0)
        {
            boxes.Add(new Bounds(barrel.center, Vector3.Max(barrel.size, new Vector3(0.05f, 0.05f, 0.05f))));
        }

        layoutMesh = mesh;
        layoutBoxes = boxes.ToArray();
        return layoutBoxes;
    }

    private static bool TryBoundsAlongBarrel(Vector3[] vertices, float x0, float x1, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        Vector3 min = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        foreach (Vector3 v in vertices)
        {
            if (v.x < x0 || v.x > x1)
            {
                continue;
            }

            any = true;
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }

        if (!any)
        {
            return false;
        }

        bounds = new Bounds();
        bounds.SetMinMax(min, max);
        return true;
    }

    private static Mesh? LoadEmbeddedMesh()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ObjResource);
        if (stream == null)
        {
            Plugin.Log.LogWarning($"Embedded mesh resource '{ObjResource}' was not found.");
            return null;
        }

        return ObjMeshLoader.Load(stream, "ShotgunMesh");
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
