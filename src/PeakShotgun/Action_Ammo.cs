using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace Peak.Shotgun;

public class Action_Ammo : ItemAction
{
    public bool consumeOnFullyUsed;

    /// <summary>Clients ask the host to spend one shot; only the host mutates remaining ammo.</summary>
    [PunRPC]
    public void RequestSpendRPC()
    {
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            return;
        }

        if (!item.HasData(DataEntryKey.ItemUses))
        {
            EnsureHostMagazine();
        }

        OptionableIntItemData data = item.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
        if (!data.HasData)
        {
            EnsureHostMagazine();
            data = item.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
        }

        if (!data.HasData || data.Value <= 0)
        {
            BroadcastUses(0);
            return;
        }

        data.Value--;
        BroadcastUses(data.Value);

        if (data.Value == 0 && consumeOnFullyUsed && character && character.IsLocal && character.data.currentItem == item)
        {
            item.StartCoroutine(item.ConsumeDelayed());
        }
    }

    /// <summary>Host (or offline) writes the magazine size into instance data when it is still unset.</summary>
    internal void EnsureHostMagazine()
    {
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            return;
        }

        int count = Plugin.ShotCount;
        item.totalUses = count;
        OptionableIntItemData data = item.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
        if (data.HasData && data.Value >= 0)
        {
            return;
        }

        data.HasData = true;
        data.Value = count;
        item.SetUseRemainingPercentage(1f);
        if (PhotonNetwork.InRoom && item.photonView != null)
        {
            item.photonView.RPC(nameof(ApplyUsesRPC), RpcTarget.All, data.Value, count);
        }
    }

    private void BroadcastUses(int remaining)
    {
        int capacity = Mathf.Max(item.totalUses, Plugin.ShotCount);
        item.totalUses = capacity;
        if (!PhotonNetwork.InRoom)
        {
            ApplyUsesRPC(remaining, capacity);
            return;
        }

        item.photonView.RPC(nameof(ApplyUsesRPC), RpcTarget.All, remaining, capacity);
    }

    [PunRPC]
    public void ApplyUsesRPC(int remaining, int capacity)
    {
        item.totalUses = Mathf.Max(1, capacity);
        OptionableIntItemData data = item.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
        data.HasData = true;
        data.Value = Mathf.Max(0, remaining);
        if (item.totalUses > 0)
        {
            item.SetUseRemainingPercentage(data.Value / (float)item.totalUses);
        }

        SyncHolderSlotUses(data.Value);
        ShotgunAmmoUI.Refresh();
    }

    private void SyncHolderSlotUses(int remaining)
    {
        Character? holder = character != null ? character : item.holderCharacter;
        if (holder == null || holder.player == null || holder.refs?.items == null)
        {
            return;
        }

        if (!holder.refs.items.currentSelectedSlot.IsSome)
        {
            return;
        }

        ItemSlot? slot = holder.player.GetItemSlot(holder.refs.items.currentSelectedSlot.Value);
        if (slot?.data == null)
        {
            return;
        }

        if (slot.data.TryGetDataEntry(DataEntryKey.ItemUses, out OptionableIntItemData slotUses))
        {
            slotUses.HasData = true;
            slotUses.Value = remaining;
            return;
        }

        var entry = new OptionableIntItemData
        {
            HasData = true,
            Value = remaining,
        };
        slot.data.RegisterEntry(DataEntryKey.ItemUses, entry);
    }
}
