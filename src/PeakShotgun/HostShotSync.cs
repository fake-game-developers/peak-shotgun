using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// Magazine size (<c>Shots</c>) is authored by the room host only.
/// Published through Photon room custom properties so every client uses the same capacity.
/// </summary>
internal sealed class HostShotSync : MonoBehaviour, IInRoomCallbacks, IMatchmakingCallbacks
{
    internal const string RoomShotsKey = "PeakShotgun_Shots";

    private static int? hostShots;

    /// <summary>Host-synced magazine size when in a room; otherwise the local config.</summary>
    internal static int ShotCount
    {
        get
        {
            if (hostShots.HasValue)
            {
                return Mathf.Max(1, hostShots.Value);
            }

            return Plugin.LocalConfiguredShots;
        }
    }

    private void OnEnable() => PhotonNetwork.AddCallbackTarget(this);

    private void OnDisable() => PhotonNetwork.RemoveCallbackTarget(this);

    /// <summary>Host publishes its config; clients pull the room value. Call after cfg reload / map load.</summary>
    internal static void SyncFromLocalRole(string reason)
    {
        if (!PhotonNetwork.InRoom)
        {
            hostShots = Plugin.LocalConfiguredShots;
            Plugin.ApplyShotCountConfig();
            Plugin.Log.LogInfo($"Shots (offline/local) = {ShotCount} ({reason}).");
            return;
        }

        if (PhotonNetwork.IsMasterClient)
        {
            PublishHostShots(reason);
            return;
        }

        if (!TryApplyRoomShots())
        {
            Plugin.Log.LogInfo($"Waiting for host Shots room property ({reason}).");
        }
    }

    private static void PublishHostShots(string reason)
    {
        int value = Plugin.LocalConfiguredShots;
        hostShots = value;
        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { RoomShotsKey, value } });
        Plugin.ApplyShotCountConfig();
        Plugin.Log.LogInfo($"Host published Shots = {value} ({reason}).");
    }

    private static bool TryApplyRoomShots()
    {
        Room? room = PhotonNetwork.CurrentRoom;
        if (room?.CustomProperties == null
            || !room.CustomProperties.TryGetValue(RoomShotsKey, out object? raw)
            || raw is not int value)
        {
            return false;
        }

        value = Mathf.Max(1, value);
        if (hostShots == value)
        {
            return true;
        }

        hostShots = value;
        Plugin.ApplyShotCountConfig();
        Plugin.Log.LogInfo($"Client applied host Shots = {value}.");
        return true;
    }

    private static void ClearHostShots() => hostShots = null;

    public void OnJoinedRoom() => SyncFromLocalRole("joined room");

    public void OnLeftRoom() => ClearHostShots();

    public void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        if (PhotonNetwork.IsMasterClient)
        {
            PublishHostShots("became host");
        }
    }

    public void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        if (propertiesThatChanged != null && propertiesThatChanged.ContainsKey(RoomShotsKey))
        {
            TryApplyRoomShots();
        }
    }

    public void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer) { }

    public void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer) { }

    public void OnPlayerPropertiesUpdate(Photon.Realtime.Player targetPlayer, Hashtable changedProps) { }

    public void OnFriendListUpdate(List<FriendInfo> friendList) { }

    public void OnCreatedRoom() { }

    public void OnCreateRoomFailed(short returnCode, string message) { }

    public void OnJoinRoomFailed(short returnCode, string message) { }

    public void OnJoinRandomFailed(short returnCode, string message) { }
}
