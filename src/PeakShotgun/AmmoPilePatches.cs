using HarmonyLib;

namespace Peak.Shotgun;

[HarmonyPatch(typeof(Item), nameof(Item.IsInteractible))]
internal static class AmmoPileIsInteractiblePatch
{
    [HarmonyPrefix]
    private static bool Prefix(Item __instance, Character interactor, ref bool __result)
    {
        if (!AmmoPileInteract.IsAmmoPile(__instance))
        {
            return true;
        }

        if (__instance.blockInteraction
            || __instance.itemState == ItemState.Held
            || __instance.itemState == ItemState.InBackpack)
        {
            __result = false;
            return false;
        }

        __result = AmmoPileInteract.CanRefillFrom(interactor, out _);
        return false;
    }
}

[HarmonyPatch(typeof(Item), nameof(Item.GetInteractionText))]
internal static class AmmoPileInteractionTextPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Item __instance, ref string __result)
    {
        if (!AmmoPileInteract.IsAmmoPile(__instance))
        {
            return true;
        }

        __result = "REFILL";
        return false;
    }
}

[HarmonyPatch(typeof(Item), nameof(Item.Interact))]
internal static class AmmoPileInteractPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Item __instance, Character interactor)
    {
        AmmoPileInteract? pile = __instance.GetComponent<AmmoPileInteract>();
        if (pile == null)
        {
            return true;
        }

        pile.RequestRefill(interactor);
        return false;
    }
}
