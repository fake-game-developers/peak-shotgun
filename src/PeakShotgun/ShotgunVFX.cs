using UnityEngine;

namespace PeakShotgun;

public class ShotgunVFX : MonoBehaviour
{
    private const float LightSeconds = 0.15f;

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

        // The light hangs off ShotgunMuzzle, whose forward is the barrel. Use this client's own muzzle so the
        // flash and smoke leave the barrel end along the barrel, not along the shooter's camera ray.
        Transform? muzzle = flashLight != null ? flashLight.transform.parent : null;
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

        Puff(point, forward);
    }

    private void Puff(Vector3 point, Vector3 forward)
    {
        smokePrefab ??= FindSmoke();
        if (smokePrefab == null)
        {
            return;
        }

        GameObject puff = Object.Instantiate(smokePrefab, point, Quaternion.LookRotation(forward));
        puff.transform.localScale = Vector3.one * 0.35f;
        puff.SetActive(true);
        foreach (ParticleSystem particles in puff.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = particles.main;
            main.loop = false;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles *= 3;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = Triple(emission.rateOverTime);
            emission.rateOverDistance = Triple(emission.rateOverDistance);
            int burstCount = emission.burstCount;
            if (burstCount > 0)
            {
                var bursts = new ParticleSystem.Burst[burstCount];
                emission.GetBursts(bursts);
                for (int i = 0; i < bursts.Length; i++)
                {
                    bursts[i].count = Triple(bursts[i].count);
                }

                emission.SetBursts(bursts);
            }

            particles.Play(true);
        }

        Object.Destroy(puff, 1.4f);
    }

    private static ParticleSystem.MinMaxCurve Triple(ParticleSystem.MinMaxCurve curve)
    {
        switch (curve.mode)
        {
            case ParticleSystemCurveMode.TwoConstants:
                curve.constantMin *= 3f;
                curve.constantMax *= 3f;
                break;
            case ParticleSystemCurveMode.Curve:
            case ParticleSystemCurveMode.TwoCurves:
                curve.curveMultiplier *= 3f;
                break;
            default:
                curve.constant *= 3f;
                break;
        }

        return curve;
    }

    private static bool CanUse(ParticleSystem particles)
    {
        if (particles == null)
        {
            return false;
        }

        ParticleSystemRenderer? renderer = particles.GetComponent<ParticleSystemRenderer>();
        Material? material = renderer != null ? renderer.sharedMaterial : null;
        if (material == null || material.shader == null)
        {
            return false;
        }

        return material.shader.name != "Hidden/InternalErrorShader";
    }

    private void Update()
    {
        if (flashLight == null || lightTime <= 0f)
        {
            return;
        }

        lightTime -= Time.deltaTime;
        flashLight.intensity = Mathf.Max(0f, lightTime / LightSeconds) * 18f;
    }
}
