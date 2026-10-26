using System;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// Every world target the shotgun can hit, in classify order (first match wins within a <see cref="ShotStage"/>).
/// Each target is created and called behind a try/catch: when a PEAK update renames or removes a type or member
/// one target uses, only that target turns off and every other one keeps working.
/// </summary>
internal static class ShotTargetRegistry
{
    private static readonly List<ShotTarget> all = new();

    private static readonly Dictionary<ShotTargetKind, ShotTarget> byKind = new();

    private static bool initialized;

    internal static IReadOnlyList<ShotTarget> All
    {
        get
        {
            Initialize();
            return all;
        }
    }

    /// <summary>Creates every target once. Plugin.Awake calls this so a broken target is reported at startup.</summary>
    internal static void Initialize()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;

        // Each line is its own lambda so a missing game type fails only that lambda, not this method.
        Add(() => new TickTarget());
        Add(() => new ScoutmasterTarget());
        Add(() => new SpiderTarget());
        Add(() => new BeetleTarget());
        Add(() => new ScorpionTarget());
        Add(() => new CondorTarget());
        Add(() => new AntlionTarget());
        Add(() => new FrogTarget());
        Add(() => new BeehiveTarget());
        Add(() => new JellyfishTarget());
        Add(() => new CoconutTarget());
        Add(() => new MandrakeTarget());
        Add(() => new DynamiteTarget());
        Add(() => new SporeBombTarget());
        Add(() => new SporeCloudTarget());

        Plugin.Log.LogInfo($"Shot targets ready: {all.Count} registered.");
    }

    /// <summary>Extra layers pellets must hit for some targets (<see cref="ShotTarget.ExtraLayerMask"/>).</summary>
    internal static int ExtraLayerMask
    {
        get
        {
            int mask = 0;
            foreach (ShotTarget target in All)
            {
                if (!target.Broken)
                {
                    mask |= target.ExtraLayerMask;
                }
            }

            return mask;
        }
    }

    internal static ShotTarget? For(ShotTargetKind kind)
    {
        Initialize();
        return byKind.TryGetValue(kind, out ShotTarget target) && !target.Broken ? target : null;
    }

    internal static Component? Classify(ShotTarget target, Collider collider) =>
        Run(target, "classify", () => target.Classify(collider));

    internal static Component? FindFromView(ShotTarget target, PhotonView view, int extra) =>
        Run(target, "host lookup", () => target.FindFromView(view, extra));

    internal static PhotonView? ViewOf(ShotTarget target, Component component) =>
        Run(target, "view lookup", () => target.ViewOf(component));

    internal static int ExtraOf(ShotTarget target, Component component) =>
        Run(target, "extra id", () => target.ExtraOf(component));

    internal static bool Defeat(ShotTarget target, Component component, ShotHit hit) =>
        Run(target, "defeat", () =>
        {
            target.Defeat(component, hit);
            return true;
        });

    internal static void Effect(ShotTarget target, Component component, Vector3 push, int variant) =>
        Run(target, "effect", () =>
        {
            target.Effect(component, push, variant);
            return true;
        });

    internal static void ClientDefeat(ShotTarget target, PhotonView? view, int extra) =>
        Run(target, "client defeat", () =>
        {
            target.ClientDefeat(view, extra);
            return true;
        });

    private static void Add(Func<ShotTarget> create)
    {
        try
        {
            ShotTarget target = create();
            if (byKind.ContainsKey(target.Kind))
            {
                Plugin.Log.LogError($"Shot target {target.Kind} is registered twice; keeping the first.");
                return;
            }

            all.Add(target);
            byKind[target.Kind] = target;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning(
                $"A shot target could not be created and is turned off ({e.GetType().Name}: {e.Message}). A PEAK update may have changed it.");
        }
    }

    private static TResult? Run<TResult>(ShotTarget target, string step, Func<TResult> action)
    {
        if (target.Broken)
        {
            return default;
        }

        try
        {
            return action();
        }
        catch (Exception e) when (GameCompat.IsGameMismatch(e))
        {
            target.MarkBroken(step, e);
            return default;
        }
        catch (Exception e)
        {
            // Usually transient (the target was destroyed this frame); keep the target on.
            Plugin.Log.LogWarning($"Shooting {target.Kind}: {step} failed ({e.GetType().Name}: {e.Message}).");
            return default;
        }
    }
}
