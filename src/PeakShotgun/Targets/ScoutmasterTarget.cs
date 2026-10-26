using Photon.Pun;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// Scoutmaster Myres is a <see cref="Character"/>, so he is only classified for a character with
/// <c>isScoutmaster</c> (never as a scout). After enough hits the host warps him to the hidey spot
/// (same as vanilla <see cref="Scoutmaster.TeleportFarAway"/>) and clears aggro.
/// </summary>
internal sealed class ScoutmasterTarget : ShotTarget<Scoutmaster>
{
    internal override ShotTargetKind Kind => ShotTargetKind.Scoutmaster;

    internal override ShootableRule Rule => ShootableRules.Scoutmaster;

    internal override ShotStage Stage => ShotStage.Scoutmaster;

    internal override Component? Classify(Collider collider)
    {
        Character? character = collider.GetComponentInParent<Character>();
        return character != null ? character.GetComponent<Scoutmaster>() : null;
    }

    protected override void Defeat(Scoutmaster scoutmaster, ShotHit hit)
    {
        if (scoutmaster == null)
        {
            return;
        }

        scoutmaster.SetCurrentTarget(null, 0f);
        scoutmaster.discovered = null;
        scoutmaster.chillForSeconds = 120f;
        scoutmaster.tpCounter = 5f;
        scoutmaster.TeleportFarAway();
        Plugin.Log.LogInfo("Scoutmaster dismissed after shotgun hits (warped to hidey spot).");
    }
}
