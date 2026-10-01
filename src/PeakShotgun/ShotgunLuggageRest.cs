using Photon.Pun;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// Luggage-only rest pose of the shotgun visual, plus the "seated in a suitcase" freeze. Lives on the item root
/// so it can receive RPCs. Only the host places luggage loot, so it sends the pose to everyone else, replays it
/// to late joiners, and <see cref="LuggageShotgunOffsetPatch"/> rebuilds it for guns restored from a save.
/// While armed, <see cref="ShotgunVisualOrient"/> uses this pose instead of the ground rest pose and
/// <see cref="ShotgunPhysics"/> keeps the gun kinematic. Cleared as soon as the gun leaves the ground state.
/// </summary>
internal sealed class ShotgunLuggageRest : MonoBehaviourPunCallbacks
{
    private Item? item;

    internal bool Armed { get; private set; }

    internal Vector3 LocalPosition { get; private set; }

    internal Quaternion LocalRotation { get; private set; } = Quaternion.identity;

    private void Awake() => item = GetComponent<Item>();

    internal void Arm(Vector3 localPosition, Quaternion localRotation)
    {
        LocalPosition = localPosition;
        LocalRotation = localRotation;
        Armed = true;
        GetComponent<ShotgunPhysics>()?.Apply();
    }

    internal void Clear() => Armed = false;

    /// <summary>Host: send the armed pose to every other client.</summary>
    internal void Broadcast()
    {
        if (Armed && PhotonNetwork.InRoom && photonView != null)
        {
            photonView.RPC(nameof(RPC_SetLuggageRest), RpcTarget.Others, LocalPosition, LocalRotation);
        }
    }

    [PunRPC]
    public void RPC_SetLuggageRest(Vector3 localPosition, Quaternion localRotation, PhotonMessageInfo info)
    {
        item ??= GetComponent<Item>();
        if (info.Sender == null || !info.Sender.IsMasterClient || item == null || item.itemState != ItemState.Ground)
        {
            return;
        }

        Arm(localPosition, localRotation);
    }

    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
    {
        if (Armed && PhotonNetwork.IsMasterClient && photonView != null)
        {
            photonView.RPC(nameof(RPC_SetLuggageRest), newPlayer, LocalPosition, LocalRotation);
        }
    }
}
