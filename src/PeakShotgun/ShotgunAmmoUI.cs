using HarmonyLib;
using Zorro.Core;

namespace Peak.Shotgun;

internal static class ShotgunAmmoUI
{
    internal static void Refresh()
    {
        if (GUIManager.instance == null)
        {
            return;
        }

        if (GUIManager.instance.items != null)
        {
            foreach (InventoryItemUI slot in GUIManager.instance.items)
            {
                slot?.UpdateNameText();
            }
        }

        GUIManager.instance.temporaryItem?.UpdateNameText();
        GUIManager.instance.UpdateItemPrompts();
    }

    internal static bool TryGetRemaining(Item item, ItemInstanceData? data, out int remaining)
    {
        remaining = 0;
        if (item == null || item.GetComponent<Action_Gun>() == null)
        {
            return false;
        }

        if (data != null && data.TryGetDataEntry(DataEntryKey.ItemUses, out OptionableIntItemData entry) && entry.HasData && entry.Value >= 0)
        {
            remaining = entry.Value;
            return true;
        }

        OptionableIntItemData own = item.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
        if (own.HasData && own.Value >= 0)
        {
            remaining = own.Value;
            return true;
        }

        if (item.totalUses > 0)
        {
            remaining = item.totalUses;
            return true;
        }

        return false;
    }
}

[HarmonyPatch(typeof(Item), nameof(Item.GetItemName))]
internal static class ShotgunNamePatch
{
    [HarmonyPostfix]
    private static void AppendShots(Item __instance, ItemInstanceData data, ref string __result)
    {
        if (ShotgunAmmoUI.TryGetRemaining(__instance, data, out int remaining))
        {
            __result = $"{__result} ({remaining})";
        }
    }
}

[HarmonyPatch(typeof(GUIManager), "GetMainInteractPrompt")]
internal static class ShotgunPromptPatch
{
    [HarmonyPostfix]
    private static void AppendShots(Item item, ref string __result)
    {
        if (ShotgunAmmoUI.TryGetRemaining(item, null, out int remaining))
        {
            __result = $"{__result} ({remaining})";
        }
    }
}

/// <summary>
/// Vanilla <see cref="Item.CanUsePrimary"/> is false at 0 uses, so <see cref="Action_Gun.RunAction"/>
/// never runs and the dry-fire click cannot play. Keep the shotgun usable when empty.
/// </summary>
[HarmonyPatch(typeof(Item), nameof(Item.CanUsePrimary))]
internal static class ShotgunEmptyUsePatch
{
    [HarmonyPostfix]
    private static void AllowEmptyClick(Item __instance, ref bool __result)
    {
        if (__result || __instance == null || __instance.GetComponent<Action_Gun>() == null)
        {
            return;
        }

        if (!__instance.overrideUsability.IsNone || __instance.UIData == null || !__instance.UIData.hasMainInteract)
        {
            return;
        }

        __result = true;
    }
}
