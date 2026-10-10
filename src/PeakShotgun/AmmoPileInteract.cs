using Photon.Pun;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// World ammo pile: interact while holding an underfilled shotgun to refill up to
/// <see cref="Plugin.ShotCount"/> (never above). The pile stays in the world.
/// </summary>
public class AmmoPileInteract : MonoBehaviour
{
    private Item item = null!;

    private void Awake()
    {
        item = GetComponent<Item>();
    }

    internal static bool IsAmmoPile(Item? candidate) =>
        candidate != null && candidate.GetComponent<AmmoPileInteract>() != null;

    internal static bool CanRefillFrom(Character? interactor, out Item? shotgun)
    {
        shotgun = null;
        if (interactor?.data == null)
        {
            return false;
        }

        Item? held = interactor.data.currentItem;
        if (held == null || held.GetComponent<Action_Gun>() == null || held.GetComponent<Action_Ammo>() == null)
        {
            return false;
        }

        shotgun = held;
        if (!ShotgunAmmoUI.TryGetRemaining(held, null, out int remaining))
        {
            return true;
        }

        return remaining < Plugin.ShotCount;
    }

    /// <summary>Client entry: ask the host to refill the held shotgun from this pile.</summary>
    internal void RequestRefill(Character interactor)
    {
        if (!CanRefillFrom(interactor, out _))
        {
            return;
        }

        if (!PhotonNetwork.InRoom)
        {
            HostTryRefill(interactor);
            return;
        }

        if (item.photonView == null)
        {
            return;
        }

        item.photonView.RPC(nameof(RequestRefillRPC), RpcTarget.MasterClient, interactor.photonView.ViewID);
    }

    [PunRPC]
    public void RequestRefillRPC(int characterViewId, PhotonMessageInfo info)
    {
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            return;
        }

        PhotonView? characterView = PhotonView.Find(characterViewId);
        Character? interactor = characterView != null ? characterView.GetComponent<Character>() : null;
        if (interactor == null)
        {
            return;
        }

        // Only the requesting player may refill from their own interact.
        if (PhotonNetwork.InRoom
            && (info.Sender == null || interactor.photonView == null || info.Sender != interactor.photonView.Owner))
        {
            return;
        }

        HostTryRefill(interactor);
    }

    private void HostTryRefill(Character interactor)
    {
        if (!CanRefillFrom(interactor, out Item? shotgun) || shotgun == null)
        {
            return;
        }

        Action_Ammo? ammo = shotgun.GetComponent<Action_Ammo>();
        if (ammo == null || !ammo.TryRefillToCapacity())
        {
            return;
        }

        Plugin.Log.LogInfo(
            $"Ammo pile refilled {interactor.photonView?.Owner?.NickName ?? "scout"}'s shotgun to {Plugin.ShotCount}.");
    }
}
