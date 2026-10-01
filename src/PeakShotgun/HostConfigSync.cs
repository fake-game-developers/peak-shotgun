using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// Room rules authored by the host only: magazine size (<c>Shots</c>), <c>FriendlyFire</c> and the
/// <c>[Shootables]</c> toggles. Published through Photon room custom properties so every client applies
/// the same rules, whoever fires and whoever is hit.
/// </summary>
internal sealed class HostConfigSync : MonoBehaviour, IInRoomCallbacks, IMatchmakingCallbacks
{
    internal const string RoomShotsKey = "PeakShotgun_Shots";

    internal const string RoomFriendlyFireKey = "PeakShotgun_FriendlyFire";

    internal const string RoomShootablesKey = "PeakShotgun_Shootables";

    private static int? hostShots;

    private static bool? hostFriendlyFire;

    private static ShootTargets? hostShootables;

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

    /// <summary>Host-synced friendly fire when in a room; otherwise the local config.</summary>
    internal static bool FriendlyFire => hostFriendlyFire ?? ShotgunCombat.LocalFriendlyFire;

    /// <summary>Host-synced shootable targets when in a room; otherwise the local config.</summary>
    internal static ShootTargets Shootables => hostShootables ?? ShotgunCombat.LocalShootables;

    private void OnEnable() => PhotonNetwork.AddCallbackTarget(this);

    private void OnDisable() => PhotonNetwork.RemoveCallbackTarget(this);

    /// <summary>Host publishes its config; clients pull the room values. Call after cfg reload / map load.</summary>
    internal static void SyncFromLocalRole(string reason)
    {
        if (!PhotonNetwork.InRoom)
        {
            hostShots = Plugin.LocalConfiguredShots;
            hostFriendlyFire = null;
            hostShootables = null;
            Plugin.ApplyShotCountConfig();
            Plugin.Log.LogInfo($"Shots (offline/local) = {ShotCount} ({reason}).");
            return;
        }

        if (PhotonNetwork.IsMasterClient)
        {
            PublishHostSettings(reason);
            return;
        }

        if (!TryApplyRoomSettings())
        {
            Plugin.Log.LogInfo($"Waiting for host shotgun room settings ({reason}).");
        }
    }

    private static void PublishHostSettings(string reason)
    {
        int shots = Plugin.LocalConfiguredShots;
        bool friendlyFire = ShotgunCombat.LocalFriendlyFire;
        ShootTargets shootables = ShotgunCombat.LocalShootables;
        hostShots = shots;
        hostFriendlyFire = friendlyFire;
        hostShootables = shootables;
        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
        {
            { RoomShotsKey, shots },
            { RoomFriendlyFireKey, friendlyFire },
            { RoomShootablesKey, (int)shootables },
        });
        Plugin.ApplyShotCountConfig();
        Plugin.Log.LogInfo(
            $"Host published Shots = {shots}, FriendlyFire = {friendlyFire}, Shootables = {shootables} ({reason}).");
    }

    /// <summary>Applies whichever room settings the host has published. False until <c>Shots</c> arrives.</summary>
    private static bool TryApplyRoomSettings()
    {
        Room? room = PhotonNetwork.CurrentRoom;
        if (room?.CustomProperties == null)
        {
            return false;
        }

        Hashtable props = room.CustomProperties;
        if (props.TryGetValue(RoomFriendlyFireKey, out object? rawFriendlyFire)
            && rawFriendlyFire is bool friendlyFire
            && hostFriendlyFire != friendlyFire)
        {
            hostFriendlyFire = friendlyFire;
            Plugin.Log.LogInfo($"Client applied host FriendlyFire = {friendlyFire}.");
        }

        if (props.TryGetValue(RoomShootablesKey, out object? rawShootables)
            && rawShootables is int shootablesValue
            && hostShootables != (ShootTargets)shootablesValue)
        {
            hostShootables = (ShootTargets)shootablesValue;
            Plugin.Log.LogInfo($"Client applied host Shootables = {hostShootables}.");
        }

        if (!props.TryGetValue(RoomShotsKey, out object? rawShots) || rawShots is not int shots)
        {
            return false;
        }

        shots = Mathf.Max(1, shots);
        if (hostShots == shots)
        {
            return true;
        }

        hostShots = shots;
        Plugin.ApplyShotCountConfig();
        Plugin.Log.LogInfo($"Client applied host Shots = {shots}.");
        return true;
    }

    private static void ClearHostSettings()
    {
        hostShots = null;
        hostFriendlyFire = null;
        hostShootables = null;
    }

    public void OnJoinedRoom() => SyncFromLocalRole("joined room");

    public void OnLeftRoom() => ClearHostSettings();

    public void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        if (PhotonNetwork.IsMasterClient)
        {
            PublishHostSettings("became host");
            ShotgunLootLedger.OnBecameHost();
        }
    }

    public void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        if (propertiesThatChanged == null)
        {
            return;
        }

        ShotgunLootLedger.OnRoomPropertiesUpdate(propertiesThatChanged);
        if (propertiesThatChanged.ContainsKey(RoomShotsKey)
            || propertiesThatChanged.ContainsKey(RoomFriendlyFireKey)
            || propertiesThatChanged.ContainsKey(RoomShootablesKey))
        {
            TryApplyRoomSettings();
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
