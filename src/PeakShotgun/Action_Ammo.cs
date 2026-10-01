using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace Peak.Shotgun;

public class Action_Ammo : ItemAction
{
    public bool consumeOnFullyUsed;

    /// <summary>
    /// Host (or offline) only: spend one shot if any remain and broadcast the new count.
    /// Returns false when the magazine is empty, so the caller must not fire.
    /// </summary>
    internal bool TrySpendOne()
    {
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            return false;
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
            return false;
        }

        data.Value--;
        BroadcastUses(data.Value);

        if (data.Value == 0 && consumeOnFullyUsed && character && character.IsLocal && character.data.currentItem == item)
        {
            item.StartCoroutine(item.ConsumeDelayed());
        }

        return true;
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
            ApplyUses(remaining, capacity);
            return;
        }

        item.photonView.RPC(nameof(ApplyUsesRPC), RpcTarget.All, remaining, capacity);
    }

    /// <summary>Remaining shots are host-authoritative: ignore magazine writes from anyone else.</summary>
    [PunRPC]
    public void ApplyUsesRPC(int remaining, int capacity, PhotonMessageInfo info)
    {
        if (PhotonNetwork.InRoom && (info.Sender == null || !info.Sender.IsMasterClient))
        {
            return;
        }

        ApplyUses(remaining, capacity);
    }

    private void ApplyUses(int remaining, int capacity)
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

    /// <summary>
    /// Copies the count into the inventory slot that holds this gun's instance data. The slot is found by the
    /// instance-data guid, not by the current selection: an update that arrives after the player switched items
    /// must not land on the item they switched to.
    /// </summary>
    private void SyncHolderSlotUses(int remaining)
    {
        Character? holder = character != null ? character : item.holderCharacter;
        if (holder == null || holder.player == null || item.data == null)
        {
            return;
        }

        ItemSlot? slot = FindSlotHolding(holder.player, item.data.guid);
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

    private static ItemSlot? FindSlotHolding(Player player, System.Guid guid)
    {
        foreach (ItemSlot slot in player.itemSlots)
        {
            if (slot?.data != null && slot.data.guid == guid)
            {
                return slot;
            }
        }

        ItemSlot? temporary = player.tempFullSlot;
        return temporary?.data != null && temporary.data.guid == guid ? temporary : null;
    }
}
