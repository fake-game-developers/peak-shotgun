using System.Collections.Generic;
using UnityEngine;

namespace Peak.Shotgun;

public class ShotgunVFX : MonoBehaviour
{
    private const float LightSeconds = 0.15f;

    /// <summary>Floats per pellet in the blast RPC: end x, y, z, and 1 when the pellet hit something.</summary>
    internal const int PelletStride = 4;

    private const float MuzzleSmokeScale = 0.35f;

    private const float ImpactDustScale = 0.12f;

    private const float SparksScale = 0.6f;

    private const float TracerSeconds = 0.09f;

    private const float TracerWidth = 0.03f;

    // A missing effect is looked for again after this long (its item may load later), not on every shot.
    private const float RetrySearchSeconds = 10f;

    // Resources paths of PEAK prefabs whose effects are reused. A missing one is logged on first use.
    internal const string BugleResource = "0_Items/Bugle_Magic";

    internal const string GliderResource = "0_Items/Glider";

    internal const string SparksResource = "RopeAnchor";

    private static readonly Color TracerColor = new(1f, 0.85f, 0.55f, 0.9f);

    private static Material? tracerMaterial;

    private static float nextTracerSearch;

    private static GameObject? sparksPrefab;

    private static float nextSparksSearch;

    private static readonly string[] PreferredSmoke =
    [
        "SmokeParticleSimple",
        "SmokeParticle",
        "VFX_Smoke",
        "Smoke_LongFade",
        "VFX_FlareSmoke",
    ];

    private Light? flashLight;

    private float lightTime;

    [SerializeField]
    private GameObject? smokePrefab;

    private readonly List<LineRenderer> tracers = new();

    private readonly List<(Vector3 start, Vector3 end)> tracerEnds = new();

    private GameObject? tracerRoot;

    private float tracerTime;


    public static GameObject? FindSmoke()
    {
        ParticleSystem[] systems = Resources.FindObjectsOfTypeAll<ParticleSystem>();
        GameObject? fallback = null;
        foreach (string preferred in PreferredSmoke)
        {
            foreach (ParticleSystem particles in systems)
            {
                if (!CanUse(particles) || particles.gameObject.scene.IsValid())
                {
                    continue;
                }

                if (particles.gameObject.name != preferred)
                {
                    continue;
                }

                return particles.gameObject;
            }
        }

        foreach (ParticleSystem particles in systems)
        {
            if (!CanUse(particles) || particles.gameObject.scene.IsValid())
            {
                continue;
            }

            string name = particles.gameObject.name;
            if (name.IndexOf("Smoke", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (name.Contains("Explo") || name.Contains("Tornado") || name.Contains("Geyser") || name.Contains("Kick"))
            {
                continue;
            }

            fallback ??= particles.gameObject;
        }

        return fallback;
    }

    public void Build(Transform muzzle, GameObject? smoke)
    {
        smokePrefab = smoke;
        var lightObject = new GameObject("MuzzleLight");
        lightObject.transform.SetParent(muzzle, false);
        flashLight = lightObject.AddComponent<Light>();
        flashLight.type = LightType.Point;
        flashLight.color = new Color(1f, 0.55f, 0.15f);
        flashLight.range = 9f;
        flashLight.intensity = 0f;
        flashLight.shadows = LightShadows.None;
    }

    public void Play(Vector3 origin, Vector3 forward)
    {
        // The light hangs off ShotgunMuzzle, whose forward is the barrel. Use this client's own muzzle so the
        // flash and smoke leave the barrel end along the barrel, not along the shooter's camera ray.
        Transform? muzzle = Muzzle();
        Vector3 point = muzzle != null ? muzzle.position : origin;
        if (muzzle != null)
        {
            forward = muzzle.forward;
        }
        else if (forward.sqrMagnitude < 0.001f)
        {
            forward = transform.forward;
        }

        if (flashLight != null)
        {
            flashLight.transform.localPosition = Vector3.forward * 0.05f;
            flashLight.intensity = 18f;
            lightTime = LightSeconds;
        }

        Puff(point, forward, MuzzleSmokeScale, emissionScale: 3f, seconds: 1.4f);
    }

    /// <summary>
    /// Tracers from this client's muzzle and sparks plus dust where pellets landed. <paramref name="pellets"/>
    /// holds four floats per pellet (end x, y, z, and 1 when it hit something), see <see cref="PackPellet"/>.
    /// </summary>
    public void Pellets(Vector3 origin, float[]? pellets)
    {
        if (pellets == null || pellets.Length < PelletStride)
        {
            return;
        }

        Transform? muzzle = Muzzle();
        Vector3 start = muzzle != null ? muzzle.position : origin;
        bool drawTracers = Plugin.Tracers && EnsureTracers(pellets.Length / PelletStride);
        for (int i = 0, pellet = 0; i + PelletStride <= pellets.Length; i += PelletStride, pellet++)
        {
            Vector3 end = new(pellets[i], pellets[i + 1], pellets[i + 2]);
            if (drawTracers)
            {
                StartTracer(pellet, start, end);
            }

            if (pellets[i + 3] > 0.5f && Plugin.ImpactEffects)
            {
                Impact(end, start - end);
            }
        }

        if (drawTracers)
        {
            tracerTime = TracerSeconds;
        }
        else
        {
            HideTracers();
        }
    }

    internal static void PackPellet(float[] into, int pellet, Vector3 end, bool impact)
    {
        int i = pellet * PelletStride;
        into[i] = end.x;
        into[i + 1] = end.y;
        into[i + 2] = end.z;
        into[i + 3] = impact ? 1f : 0f;
    }

    private Transform? Muzzle()
    {
        if (flashLight == null)
        {
            foreach (Light light in GetComponentsInChildren<Light>(true))
            {
                if (light.gameObject.name == "MuzzleLight")
                {
                    flashLight = light;
                    break;
                }
            }
        }

        return flashLight != null ? flashLight.transform.parent : null;
    }

    private bool EnsureTracers(int count)
    {
        if (tracerMaterial == null)
        {
            tracerMaterial = FindTracerMaterial();
        }

        if (tracerMaterial == null)
        {
            return false;
        }

        // Not under the gun: PEAK reads an item's renderers for its bounds, and a tracer reaches 40 m out.
        // A scene change destroys the root with its lines, so start over then.
        if (tracerRoot == null)
        {
            tracerRoot = new GameObject("PeakShotgunTracers");
            tracers.Clear();
            tracerEnds.Clear();
        }

        for (int i = tracers.Count; i < count; i++)
        {
            var line = new GameObject($"PelletTracer{i}").AddComponent<LineRenderer>();
            line.transform.SetParent(tracerRoot.transform, false);
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.sharedMaterial = tracerMaterial;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.View;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.widthCurve = AnimationCurve.Linear(0f, 0.35f, 1f, 1f);
            line.enabled = false;
            tracers.Add(line);
            tracerEnds.Add(default);
        }

        return true;
    }

    private void StartTracer(int pellet, Vector3 start, Vector3 end)
    {
        LineRenderer line = tracers[pellet];
        tracerEnds[pellet] = (start, end);
        line.enabled = true;
        UpdateTracer(line, start, end, 0f);
    }

    /// <summary>A streak that runs from the muzzle to the end point and thins out while it goes.</summary>
    private static void UpdateTracer(LineRenderer line, Vector3 start, Vector3 end, float t)
    {
        line.SetPosition(0, Vector3.Lerp(start, end, Mathf.Clamp01(t * 1.2f - 0.15f)));
        line.SetPosition(1, Vector3.Lerp(start, end, Mathf.Clamp01(0.35f + t)));
        float fade = 1f - t;
        line.widthMultiplier = TracerWidth * fade;
        Color head = TracerColor;
        head.a *= fade;
        Color tail = head;
        tail.a = 0f;
        line.startColor = tail;
        line.endColor = head;
    }

    private void Impact(Vector3 point, Vector3 towardShooter)
    {
        Quaternion facing = towardShooter.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(towardShooter) : Quaternion.identity;
        GameObject? sparks = FindSparks();
        if (sparks != null)
        {
            GameObject copy = Object.Instantiate(sparks, point, facing);
            // A copy of a scene object can bring that object's scripts along; only the particles are wanted.
            foreach (MonoBehaviour behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
            {
                Object.Destroy(behaviour);
            }

            copy.transform.localScale = Vector3.one * SparksScale;
            PlayOnce(copy, emissionScale: 1f);
            Object.Destroy(copy, 1.2f);
        }

        Puff(point, towardShooter, ImpactDustScale, emissionScale: 1f, seconds: 1f);
    }

    private void Puff(Vector3 point, Vector3 forward, float scale, float emissionScale, float seconds)
    {
        smokePrefab ??= FindSmoke();
        if (smokePrefab == null)
        {
            return;
        }

        Quaternion facing = forward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(forward) : Quaternion.identity;
        GameObject puff = Object.Instantiate(smokePrefab, point, facing);
        puff.transform.localScale = Vector3.one * scale;
        PlayOnce(puff, emissionScale);
        Object.Destroy(puff, seconds);
    }

    private static void PlayOnce(GameObject effect, float emissionScale)
    {
        effect.SetActive(true);
        foreach (ParticleSystem particles in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = particles.main;
            main.loop = false;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = Mathf.CeilToInt(main.maxParticles * emissionScale);
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = Scale(emission.rateOverTime, emissionScale);
            emission.rateOverDistance = Scale(emission.rateOverDistance, emissionScale);
            int burstCount = emission.burstCount;
            if (burstCount > 0)
            {
                var bursts = new ParticleSystem.Burst[burstCount];
                emission.GetBursts(bursts);
                for (int i = 0; i < bursts.Length; i++)
                {
                    bursts[i].count = Scale(bursts[i].count, emissionScale);
                }

                emission.SetBursts(bursts);
            }

            particles.Play(true);
        }
    }

    private static ParticleSystem.MinMaxCurve Scale(ParticleSystem.MinMaxCurve curve, float factor)
    {
        switch (curve.mode)
        {
            case ParticleSystemCurveMode.TwoConstants:
                curve.constantMin *= factor;
                curve.constantMax *= factor;
                break;
            case ParticleSystemCurveMode.Curve:
            case ParticleSystemCurveMode.TwoCurves:
                curve.curveMultiplier *= factor;
                break;
            default:
                curve.constant *= factor;
                break;
        }

        return curve;
    }

    /// <summary>
    /// The trail material of an effect already in PEAK (the magic bugle's bullet trail, else the glider's trail),
    /// so tracers use a shader the game draws. Null when neither is found: then there are no tracers.
    /// </summary>
    private static Material? FindTracerMaterial()
    {
        if (Time.time < nextTracerSearch)
        {
            return null;
        }

        nextTracerSearch = Time.time + RetrySearchSeconds;
        Material? material =
            FirstMaterial(Loaded<ParticleSystemRenderer>(BugleResource), r => r.trailMaterial, IsBulletTrail)
            ?? FirstMaterial(Resources.FindObjectsOfTypeAll<ParticleSystemRenderer>(), r => r.trailMaterial, IsBulletTrail)
            ?? FirstMaterial(Loaded<TrailRenderer>(GliderResource), t => t.sharedMaterial, IsGliderTrail)
            ?? FirstMaterial(Resources.FindObjectsOfTypeAll<TrailRenderer>(), t => t.sharedMaterial, IsGliderTrail);
        if (material == null)
        {
            Plugin.Log.LogWarning("No PEAK trail material was found. Pellets will not draw tracers.");
            return null;
        }

        Plugin.Log.LogInfo($"Pellet tracers use {material.name}.");
        return material;
    }

    /// <summary>
    /// PEAK's bullet-spark burst (what a rope anchor throws off when it hits rock). From the rope anchor prefab,
    /// else any loaded one, preferring a prefab over a copy of one in the scene.
    /// </summary>
    private static GameObject? FindSparks()
    {
        if (sparksPrefab != null || Time.time < nextSparksSearch)
        {
            return sparksPrefab;
        }

        nextSparksSearch = Time.time + RetrySearchSeconds;
        GameObject? inScene = null;
        foreach (ParticleSystemRenderer renderer in Loaded<ParticleSystemRenderer>(SparksResource))
        {
            if (IsSparks(renderer))
            {
                sparksPrefab = renderer.gameObject;
                return sparksPrefab;
            }
        }

        foreach (ParticleSystemRenderer renderer in Resources.FindObjectsOfTypeAll<ParticleSystemRenderer>())
        {
            if (!IsSparks(renderer))
            {
                continue;
            }

            if (!renderer.gameObject.scene.IsValid())
            {
                sparksPrefab = renderer.gameObject;
                return sparksPrefab;
            }

            inScene ??= renderer.gameObject;
        }

        sparksPrefab = inScene;
        if (sparksPrefab == null)
        {
            Plugin.Log.LogWarning("No PEAK spark effect was found. Pellet impacts will only puff dust.");
        }

        return sparksPrefab;
    }

    /// <summary>
    /// Components of a prefab under PEAK's Resources folder, loaded without instantiating it. Empty when the path
    /// is gone (a game update).
    /// </summary>
    private static T[] Loaded<T>(string path)
        where T : Component
    {
        GameObject? prefab = Resources.Load<GameObject>(path);
        return prefab != null ? prefab.GetComponentsInChildren<T>(true) : [];
    }

    private static Material? FirstMaterial<T>(T[] renderers, System.Func<T, Material?> materialOf, System.Func<Material, bool> wanted)
        where T : Component
    {
        foreach (T renderer in renderers)
        {
            Material? material = materialOf(renderer);
            if (IsDrawable(material) && wanted(material!))
            {
                return material;
            }
        }

        return null;
    }

    private static bool IsBulletTrail(Material material) =>
        material.name.StartsWith("M_VFX_BulletTrail", System.StringComparison.Ordinal);

    private static bool IsGliderTrail(Material material) =>
        material.name.StartsWith("GliderTrail", System.StringComparison.Ordinal);

    private static bool IsSparks(ParticleSystemRenderer renderer) =>
        renderer.gameObject.name == "Sparks" && IsDrawable(renderer.sharedMaterial) && renderer.sharedMaterial.name == "M_VFX_BulletSpark";

    private static bool CanUse(ParticleSystem particles)
    {
        if (particles == null)
        {
            return false;
        }

        ParticleSystemRenderer? renderer = particles.GetComponent<ParticleSystemRenderer>();
        return IsDrawable(renderer != null ? renderer.sharedMaterial : null);
    }

    private static bool IsDrawable(Material? material) =>
        material != null && material.shader != null && material.shader.name != "Hidden/InternalErrorShader";

    private void HideTracers()
    {
        tracerTime = 0f;
        foreach (LineRenderer line in tracers)
        {
            if (line != null)
            {
                line.enabled = false;
            }
        }
    }

    private void OnDisable()
    {
        HideTracers();
    }

    private void OnDestroy()
    {
        if (tracerRoot != null)
        {
            Object.Destroy(tracerRoot);
        }
    }

    private void Update()
    {
        if (flashLight != null && lightTime > 0f)
        {
            lightTime -= Time.deltaTime;
            flashLight.intensity = Mathf.Max(0f, lightTime / LightSeconds) * 18f;
        }

        if (tracerTime > 0f)
        {
            tracerTime -= Time.deltaTime;
            float t = 1f - Mathf.Max(0f, tracerTime) / TracerSeconds;
            for (int i = 0; i < tracers.Count; i++)
            {
                LineRenderer line = tracers[i];
                if (line == null || !line.enabled)
                {
                    continue;
                }

                if (tracerTime <= 0f)
                {
                    line.enabled = false;
                    continue;
                }

                (Vector3 start, Vector3 end) = tracerEnds[i];
                UpdateTracer(line, start, end, t);
            }
        }
    }
}
