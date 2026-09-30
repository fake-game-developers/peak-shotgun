using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace PeakShotgun;

public class Action_Ammo : ItemAction
{
    public bool consumeOnFullyUsed;

    [PunRPC]
    public void ReduceUsesRPC()
    {
        OptionableIntItemData data = item.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
        if (!data.HasData || data.Value <= 0)
        {
            return;
        }

        data.Value--;
        if (item.totalUses > 0)
        {
            item.SetUseRemainingPercentage(data.Value / (float)item.totalUses);
        }

        ShotgunAmmoUI.Refresh();

        if (data.Value == 0 && consumeOnFullyUsed && character && character.IsLocal && character.data.currentItem == item)
        {
            item.StartCoroutine(item.ConsumeDelayed());
        }
    }
}
