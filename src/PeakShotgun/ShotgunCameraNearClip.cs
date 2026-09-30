using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// PEAK's default near plane clips the custom shotgun when you stand over it.
/// Pull the near plane in based on distance to world shotgun mesh bounds.
/// Must NOT skip this while another shotgun is held — that was leaving luggage guns clipped.
/// </summary>
[DefaultExecutionOrder(32000)]
internal sealed class ShotgunCameraNearClip : MonoBehaviour
{
    private const float MinNearPlane = 0.001f;
    private const float FullNearRestoreDistance = 3f;

    private float defaultNear = -1f;
    private Camera? subscribedCam;

    private void OnEnable()
    {
        Camera.onPreCull += OnAnyCameraPreCull;
    }

    private void OnDisable()
    {
        Camera.onPreCull -= OnAnyCameraPreCull;
        RestoreNear(subscribedCam);
        subscribedCam = null;
    }

    private void OnDestroy()
    {
        Camera.onPreCull -= OnAnyCameraPreCull;
        RestoreNear(subscribedCam);
    }

    private void LateUpdate() => ApplyNearClip(GetPeakCamera());

    private void OnAnyCameraPreCull(Camera cam)
    {
        Camera? peak = GetPeakCamera();
        if (peak != null && cam == peak)
        {
            ApplyNearClip(peak);
        }
    }

    private void ApplyNearClip(Camera? cam)
    {
        if (cam == null)
        {
            return;
        }

        subscribedCam = cam;
        if (defaultNear < 0f)
        {
            defaultNear = cam.nearClipPlane;
            if (defaultNear <= MinNearPlane * 2f)
            {
                defaultNear = 0.05f;
            }
        }

        float nearestBoundsDistance = float.MaxValue;
        Vector3 camPos = cam.transform.position;

        foreach (ShotgunInstanceSetup shotgun in ShotgunInstanceSetup.Active)
        {
            if (shotgun == null)
            {
                continue;
            }

            Item? item = shotgun.Item;
            bool held = item != null
                && (item.itemState == ItemState.Held || item.holderCharacter != null);
            // Only world / luggage guns need the near-plane pull-in. The held FP gun sits
            // past the default near plane; skipping it avoids fighting PEAK's hold FOV.
            if (held)
            {
                continue;
            }

            float distance = DistanceToVisual(camPos, item, shotgun.transform);
            if (distance < nearestBoundsDistance)
            {
                nearestBoundsDistance = distance;
            }
        }

        if (nearestBoundsDistance >= FullNearRestoreDistance)
        {
            cam.nearClipPlane = defaultNear;
            return;
        }

        // Ease in hard when the camera is on top of / inside the mesh bounds.
        float t = Mathf.Clamp01(nearestBoundsDistance / FullNearRestoreDistance);
        float eased = t * t * t;
        cam.nearClipPlane = Mathf.Lerp(MinNearPlane, defaultNear, eased);
    }

    private void RestoreNear(Camera? cam)
    {
        if (cam != null && defaultNear > 0f)
        {
            cam.nearClipPlane = defaultNear;
        }
    }

    private static Camera? GetPeakCamera() =>
        MainCamera.instance != null ? MainCamera.instance.cam : Camera.main;

    private static float DistanceToVisual(Vector3 camPos, Item? item, Transform itemRoot)
    {
        Renderer? renderer = item?.mainRenderer;
        if (renderer != null)
        {
            Bounds bounds = renderer.bounds;
            if (bounds.size.sqrMagnitude > 1e-8f)
            {
                // Inside the AABB ClosestPoint == camPos → distance 0 → full near pull-in.
                return Vector3.Distance(camPos, bounds.ClosestPoint(camPos));
            }
        }

        Transform? visual = itemRoot.Find("ShotgunVisual");
        return Vector3.Distance(camPos, visual != null ? visual.position : itemRoot.position);
    }
}
