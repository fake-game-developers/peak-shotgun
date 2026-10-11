using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// Room rules authored by the host only: magazine size (<c>Shots</c>), the
/// <c>[Shootables]</c> toggles, and <c>[HitsToDefeat]</c>. Published through Photon room custom
/// properties so every client applies the same rules, whoever fires and whoever is hit.
/// </summary>
internal sealed class HostConfigSync : MonoBehaviour, IInRoomCallbacks, IMatchmakingCallbacks
{
    internal const string RoomShotsKey = "PeakShotgun_Shots";

    internal const string RoomShootablesKey = "PeakShotgun_Shootables";

    internal const string RoomHitsToDefeatKey = "PeakShotgun_HitsToDefeat";

    private static int? hostShots;

    private static ShootTargets? hostShootables;

    private static HitsToDefeatSettings? hostHitsToDefeat;

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

    /// <summary>Host-synced shootable targets when in a room; otherwise the local config.</summary>
    internal static ShootTargets Shootables => hostShootables ?? ShotgunCombat.LocalShootables;

    /// <summary>Host-synced hits-to-defeat when in a room; otherwise the local config.</summary>
    internal static HitsToDefeatSettings HitsToDefeat => hostHitsToDefeat ?? ShotgunCombat.LocalHitsToDefeat;

    private void OnEnable() => PhotonNetwork.AddCallbackTarget(this);

    private void OnDisable() => PhotonNetwork.RemoveCallbackTarget(this);

    /// <summary>Host publishes its config; clients pull the room values. Call after cfg reload / map load.</summary>
    internal static void SyncFromLocalRole(string reason)
    {
        if (!PhotonNetwork.InRoom)
        {
            hostShots = Plugin.LocalConfiguredShots;
            hostShootables = null;
            hostHitsToDefeat = null;
            ShotgunCombat.ClearWorldHitCounts();
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
        ShootTargets shootables = ShotgunCombat.LocalShootables;
        HitsToDefeatSettings hits = ShotgunCombat.LocalHitsToDefeat;
        hostShots = shots;
        hostShootables = shootables;
        hostHitsToDefeat = hits;
        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
        {
            { RoomShotsKey, shots },
            { RoomShootablesKey, (int)shootables },
            { RoomHitsToDefeatKey, hits.ToPacked() },
        });
        Plugin.ApplyShotCountConfig();
        Plugin.Log.LogInfo(
            $"Host published Shots = {shots}, Shootables = {shootables}, HitsToDefeat zombies={hits.Zombies} ({reason}).");
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
        if (props.TryGetValue(RoomShootablesKey, out object? rawShootables)
            && rawShootables is int shootablesValue
            && hostShootables != (ShootTargets)shootablesValue)
        {
            hostShootables = (ShootTargets)shootablesValue;
            Plugin.Log.LogInfo($"Client applied host Shootables = {hostShootables}.");
        }

        if (props.TryGetValue(RoomHitsToDefeatKey, out object? rawHits) && rawHits is byte[] packed)
        {
            HitsToDefeatSettings hits = HitsToDefeatSettings.FromPacked(packed);
            if (hostHitsToDefeat == null || !HitsEqual(hostHitsToDefeat.Value, hits))
            {
                hostHitsToDefeat = hits;
                Plugin.Log.LogInfo($"Client applied host HitsToDefeat zombies={hits.Zombies}.");
            }
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

    private static bool HitsEqual(HitsToDefeatSettings a, HitsToDefeatSettings b) =>
        a.Zombies == b.Zombies
        && a.Mandrake == b.Mandrake
        && a.Beetles == b.Beetles
        && a.Spiders == b.Spiders
        && a.Spores == b.Spores
        && a.Scorpions == b.Scorpions
        && a.Dynamite == b.Dynamite
        && a.Scoutmaster == b.Scoutmaster;

    private static void ClearHostSettings()
    {
        hostShots = null;
        hostShootables = null;
        hostHitsToDefeat = null;
        ShotgunCombat.ClearWorldHitCounts();
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
            || propertiesThatChanged.ContainsKey(RoomShootablesKey)
            || propertiesThatChanged.ContainsKey(RoomHitsToDefeatKey))
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
