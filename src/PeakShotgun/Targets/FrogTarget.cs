using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace Peak.Shotgun;

/// <summary>
/// Dead mob state does not stop a frog: <see cref="FrogTongue"/> keeps licking, and <see cref="MobItem"/>
/// rewrites <c>mobState</c> every frame. Same as the stove: drop the tongue, destroy the frog, then spawn
/// cooked frog legs (<see cref="CookingBehavior_ReplaceItem"/>). Destroy / spawn only on the host.
/// </summary>
internal sealed class FrogTarget : ShotTarget<FrogTongue>
{
    private static Item? frogLegsPrefab;

    internal override ShotTargetKind Kind => ShotTargetKind.Frog;

    internal override ShootableRule Rule => ShootableRules.Frogs;

    internal override bool BroadcastsDefeat => true;

    internal override Component? Classify(Collider collider)
    {
        FrogTongue? frog = collider.GetComponentInParent<FrogTongue>();
        return frog != null ? frog : collider.transform.root.GetComponentInChildren<FrogTongue>(true);
    }

    protected override PhotonView? ViewOf(FrogTongue frog)
    {
        Item? frogItem = FrogItem(frog);
        if (frogItem != null && frogItem.photonView != null)
        {
            return frogItem.photonView;
        }

        return frog.photonView != null ? frog.photonView : base.ViewOf(frog);
    }

    protected override void Defeat(FrogTongue frog, ShotHit hit) => DefeatFrog(frog, destroy: true);

    internal override void ClientDefeat(PhotonView? view, int extra) =>
        DefeatFrog(view != null ? Near<FrogTongue>(view.gameObject) : null, destroy: false);

    private static Item? FrogItem(FrogTongue frog) =>
        frog.mobItem != null ? frog.mobItem : frog.GetComponent<Item>() ?? frog.GetComponentInParent<Item>();

    private void DefeatFrog(FrogTongue? frog, bool destroy)
    {
        if (frog == null)
        {
            return;
        }

        ReleaseFrogTongue(frog, networked: destroy);
        frog.enabled = false;

        Item? item = FrogItem(frog);
        ItemCooking? cooking = item != null
            ? item.cooking
            : frog.GetComponent<ItemCooking>() ?? frog.GetComponentInParent<ItemCooking>();
        CookingBehavior_ReplaceItem? replace = FrogLegReplace(cooking);
        Vector3 dropAt = item != null ? item.transform.position : frog.transform.position;

        if (item != null)
        {
            item.ClearDataFromBackpack();
        }

        if (!destroy)
        {
            return;
        }

        PhotonView? view = ViewOf(frog);
        GameObject root = item != null ? item.gameObject : frog.gameObject;
        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient && view != null && view.ViewID != 0)
        {
            PhotonNetwork.Destroy(view.gameObject);
        }
        else if (!PhotonNetwork.InRoom)
        {
            Object.Destroy(root);
        }

        SpawnCookedFrogLegs(replace, dropAt);
    }

    private static void ReleaseFrogTongue(FrogTongue frog, bool networked)
    {
        PhotonView? view = frog.photonView;
        if (networked && PhotonNetwork.InRoom && view != null && view.ViewID != 0)
        {
            PhotonView? targetView = frog._targetCharacter != null ? frog._targetCharacter.photonView : null;
            view.RPC(nameof(FrogTongue.RPCA_FrogAction), RpcTarget.All, targetView, FrogTongue.FrogActionType.LetGo, Vector3.zero);
            return;
        }

        frog.LetGo();
    }

    private static CookingBehavior_ReplaceItem? FrogLegReplace(ItemCooking? cooking)
    {
        AdditionalCookingBehavior[]? behaviors = cooking != null ? cooking.additionalCookingBehaviors : null;
        if (behaviors == null)
        {
            return null;
        }

        for (int i = 0; i < behaviors.Length; i++)
        {
            if (behaviors[i] is CookingBehavior_ReplaceItem replace && replace.replaceWithItem != null)
            {
                return replace;
            }
        }

        return null;
    }

    /// <summary>Stove path: instantiate frog legs, then <see cref="ItemCooking.FinishCooking"/> so they are edible.</summary>
    private static void SpawnCookedFrogLegs(CookingBehavior_ReplaceItem? replace, Vector3 position)
    {
        Item? prefab = replace != null && replace.replaceWithItem != null
            ? replace.replaceWithItem
            : FindFrogLegsPrefab();
        if (prefab == null)
        {
            Plugin.Log.LogWarning("Shot a frog but could not find the frog-legs item to drop.");
            return;
        }

        GameObject? spawned;
        if (PhotonNetwork.InRoom)
        {
            spawned = PhotonNetwork.InstantiateItemRoom(prefab.gameObject.name, position, Quaternion.identity, false);
        }
        else
        {
            spawned = Object.Instantiate(prefab.gameObject, position, Quaternion.identity);
        }

        if (spawned == null)
        {
            Plugin.Log.LogWarning($"Could not spawn frog legs from '{prefab.gameObject.name}'.");
            return;
        }

        Item? legs = spawned.GetComponent<Item>();
        ItemCooking? cooking = legs != null ? legs.cooking : spawned.GetComponent<ItemCooking>();
        bool cook = replace == null ? cooking != null && cooking.canBeCooked && cooking.preCooked <= 0 : replace.cookNewItem;
        if (cooking != null && cook)
        {
            cooking.FinishCooking();
        }
    }

    private static Item? FindFrogLegsPrefab()
    {
        if (frogLegsPrefab != null)
        {
            return frogLegsPrefab;
        }

        ItemDatabase? database = SingletonAsset<ItemDatabase>.Instance;
        if (database?.Objects == null)
        {
            return null;
        }

        Item? cooked = null;
        for (int i = 0; i < database.Objects.Count; i++)
        {
            Item candidate = database.Objects[i];
            if (candidate == null || !LooksLikeFrogLegs(candidate))
            {
                continue;
            }

            ItemCooking? cooking = candidate.cooking;
            bool alreadyCooked = cooking != null && (cooking.preCooked > 0 || cooking.timesCookedLocal > 0);
            if (!alreadyCooked)
            {
                frogLegsPrefab = candidate;
                return candidate;
            }

            cooked ??= candidate;
        }

        frogLegsPrefab = cooked;
        return cooked;
    }

    private static bool LooksLikeFrogLegs(Item item)
    {
        string name = item.name ?? "";
        string ui = item.UIData != null ? item.UIData.itemName ?? "" : "";
        return ContainsOrdinalIgnoreCase(name, "FrogLeg")
            || ContainsOrdinalIgnoreCase(name, "Frog_Leg")
            || ContainsOrdinalIgnoreCase(name, "Frog Leg")
            || ContainsOrdinalIgnoreCase(ui, "Frog Leg")
            || ContainsOrdinalIgnoreCase(ui, "FrogLeg");
    }
}
