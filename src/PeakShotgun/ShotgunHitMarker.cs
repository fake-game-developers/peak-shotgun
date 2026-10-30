using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;

namespace Peak.Shotgun;

/// <summary>
/// Shooter-only hit feedback over the crosshair: four short diagonal ticks when a shot hits something the mod
/// handles, larger red ticks when the target's authority confirms it went down (<see cref="ConfirmDefeat"/>). The ticks are plain UI images sized from PEAK's own reticle
/// dot (10 × 10 units), so they need no sprite and no shader of their own.
/// </summary>
internal sealed class ShotgunHitMarker : MonoBehaviour
{
    private const float HitSeconds = 0.2f;

    private const float DefeatSeconds = 0.45f;

    // On the host, a shot that downs a target confirms the defeat before Fire reports the plain hit.
    private const float DefeatHoldSeconds = 0.2f;

    private const float TickLength = 10f;

    private const float TickWidth = 2.5f;

    private const float TickGap = 9f;

    private static readonly Color HitColor = new(1f, 1f, 1f, 0.95f);

    private static readonly Color DefeatColor = new(1f, 0.22f, 0.18f, 1f);

    private static ShotgunHitMarker? instance;

    // Built under the GUIManager's reticle, so it goes away with that scene and is built again in the next.
    private RectTransform? marker;

    private Image[] ticks = [];

    private float shownAt = float.NegativeInfinity;

    private float defeatAt = float.NegativeInfinity;

    private float duration;

    private float startScale;

    private Color color;

    private void Awake() => instance = this;

    internal static void Show(bool defeat)
    {
        if (instance != null)
        {
            ShotgunHitMarker marker = instance;
            Plugin.Guard("Hit marker", () => marker.Flash(defeat));
        }
    }

    /// <summary>
    /// Runs where a defeat is decided (the host for world targets, a zombie's owner for its knockdown) and shows the
    /// red ticks on the shooter's screen only. <paramref name="shooter"/> null means the local player fired.
    /// </summary>
    internal static void ConfirmDefeat(Action_Gun? gun, Photon.Realtime.Player? shooter, int targetViewId) =>
        Plugin.Guard("Hit marker", () =>
        {
            if (shooter == null || shooter.IsLocal || !PhotonNetwork.InRoom)
            {
                Show(defeat: true);
            }
            else if (gun != null && gun.photonView != null)
            {
                gun.photonView.RPC(nameof(Action_Gun.RPC_ShotDefeat), shooter, targetViewId);
            }
        });

    private void Flash(bool defeat)
    {
        if (!Plugin.HitMarkers || (!defeat && Time.time < defeatAt + DefeatHoldSeconds))
        {
            return;
        }

        if (defeat)
        {
            defeatAt = Time.time;
        }

        if (!EnsureMarker())
        {
            return;
        }

        shownAt = Time.time;
        duration = defeat ? DefeatSeconds : HitSeconds;
        startScale = defeat ? 1.9f : 1.35f;
        color = defeat ? DefeatColor : HitColor;
        marker!.gameObject.SetActive(true);
        marker.SetAsLastSibling();
        Animate(0f);
    }

    private void Update()
    {
        if (marker == null || !marker.gameObject.activeSelf)
        {
            return;
        }

        float t = (Time.time - shownAt) / duration;
        if (t >= 1f)
        {
            marker.gameObject.SetActive(false);
            return;
        }

        Animate(t);
    }

    private void Animate(float t)
    {
        float eased = 1f - (1f - t) * (1f - t);
        marker!.localScale = Vector3.one * Mathf.Lerp(startScale, 1f, eased);
        Color faded = color;
        faded.a *= 1f - t * t;
        foreach (Image tick in ticks)
        {
            tick.color = faded;
        }
    }

    private bool EnsureMarker()
    {
        if (marker != null)
        {
            return true;
        }

        GameObject? dot = GUIManager.instance != null ? GUIManager.instance.reticleDefault : null;
        if (dot == null || dot.transform.parent == null || dot.transform is not RectTransform dotRect)
        {
            return false;
        }

        var root = new GameObject("PeakShotgunHitMarker", typeof(RectTransform));
        marker = (RectTransform)root.transform;
        marker.SetParent(dot.transform.parent, false);
        marker.anchorMin = dotRect.anchorMin;
        marker.anchorMax = dotRect.anchorMax;
        marker.pivot = new Vector2(0.5f, 0.5f);
        marker.anchoredPosition = dotRect.anchoredPosition;
        marker.sizeDelta = Vector2.zero;

        ticks = new Image[4];
        for (int i = 0; i < ticks.Length; i++)
        {
            float angle = 45f + 90f * i;
            Vector2 outward = new(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            var tickObject = new GameObject($"Tick{i}", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)tickObject.transform;
            rect.SetParent(marker, false);
            rect.sizeDelta = new Vector2(TickWidth, TickLength);
            rect.anchoredPosition = outward * (TickGap + TickLength * 0.5f);
            rect.localRotation = Quaternion.Euler(0f, 0f, angle - 90f);
            Image image = tickObject.GetComponent<Image>();
            image.raycastTarget = false;
            ticks[i] = image;
        }

        root.SetActive(false);
        return true;
    }
}
