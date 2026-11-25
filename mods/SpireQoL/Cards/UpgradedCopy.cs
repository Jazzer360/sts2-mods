using System.Reflection;
using MegaCrit.Sts2.Core.Models;

namespace SpireQoL.Cards;

/// <summary>
/// An upgraded copy of a card, made the way the game makes its upgrade previews: MutableClone, UpgradeInternal, FinalizeUpgradeInternal.
/// </summary>
internal static class UpgradedCopy
{
    /// <summary>
    /// CardModel.UpgradeInternal by reflection, its parameters at their defaults: v0.114.0 added an optional <c>silent</c>, and a direct
    /// call compiled against one game version is a missing method on the other (decision #32; main, v0.111, takes none).
    /// </summary>
    private static readonly MethodInfo Upgrade = typeof(CardModel).GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .First(method => method.Name == nameof(CardModel.UpgradeInternal) && method.GetParameters().All(parameter => parameter.HasDefaultValue));

    private static readonly object?[] Defaults = Upgrade.GetParameters().Select(parameter => parameter.DefaultValue).ToArray();

    /// <summary>The card upgraded once, as a new mutable copy (the card itself is untouched).</summary>
    public static CardModel Of(CardModel card)
    {
        var copy = (CardModel)card.MutableClone();
        Upgrade.Invoke(copy, Defaults);
        copy.FinalizeUpgradeInternal();
        return copy;
    }
}
