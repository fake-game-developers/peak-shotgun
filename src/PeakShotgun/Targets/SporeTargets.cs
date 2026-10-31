using Photon.Pun;
using UnityEngine;

namespace Peak.Shotgun;

/// <summary>
/// Spore / explosive / poison bombs are <see cref="Breakable"/> mushrooms (prefab names like SporeShroom,
/// ExploShroom, PoisonShroom). Not <see cref="CloudFungus"/> (the deployable platform).
/// </summary>
internal sealed class SporeBombTarget : ShotTarget<Breakable>
{
    internal override ShotTargetKind Kind => ShotTargetKind.SporeBomb;

    internal override ShootableRule Rule => ShootableRules.Spores;

    internal override bool IsLiving => false;

    internal override Component? Classify(Collider collider) =>
        collider.GetComponentInParent<Breakable>() is { } breakable && IsSporeBomb(breakable) ? breakable : null;

    internal override Component? FindFromView(PhotonView view, int extra) =>
        Near<Breakable>(view.gameObject) is { } breakable && IsSporeBomb(breakable) ? breakable : null;

    protected override void Defeat(Breakable breakable, ShotHit hit) => SmashBreakable(breakable);

    private static bool IsSporeBomb(Breakable breakable)
    {
        if (breakable == null || breakable is BreakableEgg)
        {
            return false;
        }

        if (NameLooksLikeSporeBomb(breakable.gameObject.name))
        {
            return true;
        }

        Item? item = breakable.GetComponent<Item>();
        string? itemName = item != null ? item.UIData.itemName : null;
        return !string.IsNullOrEmpty(itemName) && NameLooksLikeSporeBomb(itemName!);
    }

    private static bool NameLooksLikeSporeBomb(string name) =>
        ContainsOrdinalIgnoreCase(name, "SporeShroom")
        || ContainsOrdinalIgnoreCase(name, "ExploShroom")
        || ContainsOrdinalIgnoreCase(name, "PoisonShroom")
        || ContainsOrdinalIgnoreCase(name, "SporeMushroom")
        || ContainsOrdinalIgnoreCase(name, "SporeFungus");
}

/// <summary>Clears a spore cloud (a <see cref="StatusEmitter"/> of Spores).</summary>
internal sealed class SporeCloudTarget : ShotTarget<StatusEmitter>
{
    internal override ShotTargetKind Kind => ShotTargetKind.SporeCloud;

    internal override ShootableRule Rule => ShootableRules.Spores;

    internal override bool IsLiving => false;

    internal override Component? Classify(Collider collider) =>
        collider.GetComponentInParent<StatusEmitter>() is { } emitter && IsSporeCloud(emitter) ? emitter : null;

    internal override Component? FindFromView(PhotonView view, int extra) =>
        Near<StatusEmitter>(view.gameObject) is { } emitter && IsSporeCloud(emitter) ? emitter : null;

    protected override void Defeat(StatusEmitter emitter, ShotHit hit) => DestroyNetworked(hit.View, emitter.gameObject);

    private static bool IsSporeCloud(StatusEmitter emitter) =>
        emitter.statusType == CharacterAfflictions.STATUSTYPE.Spores;
}
