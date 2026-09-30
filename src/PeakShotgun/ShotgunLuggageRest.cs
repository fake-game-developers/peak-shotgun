using UnityEngine;

namespace PeakShotgun;

/// <summary>
/// Marks a shotgun visual as using a luggage-only rest pose so <see cref="ShotgunVisualOrient"/>
/// does not overwrite it with the ground rest pose (including RestMeshLift) each frame.
/// </summary>
internal sealed class ShotgunLuggageRest : MonoBehaviour
{
    internal bool Armed;
    internal Vector3 LocalPosition;
    internal Quaternion LocalRotation = Quaternion.identity;
}
