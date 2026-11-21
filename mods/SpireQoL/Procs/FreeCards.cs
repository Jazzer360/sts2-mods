using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using Sts2Mods.Core.Game;

namespace SpireQoL.Procs;

/// <summary>
/// Powers that make your next card from hand free and are used up by playing one (decompile v0.113.1). Each one's
/// TryModifyEnergyCostInCombatLate sets the cost of your cards in your hand (or being played) to 0, and its
/// BeforeCardPlayed takes a charge for any such card, whatever it cost:
///  - Free Card (Tune: TunePower.AfterSideTurnEnd after a turn you played a Ki), any card;
///  - Free Attack (Unrelenting), Attacks; Free Skill (Pounce), Skills; Free Power (Synthesis), Powers.
/// So a card that already costs 0, or an X-cost card (its X is your energy, never 0 from these), wastes the charge.
///
/// Under a free power the card shows 0, so what it saves is only shown when a player could know the cost anyway
/// (decision #54): they saw it in hand, with no free power on it, and it hasn't left the hand or changed since; or no copy
/// that looks the same has had its cost changed for the rest of the combat (Snecko Eye's re-roll: ConfusedPower.AfterCardDrawn,
/// SetThisCombat; Frantic Escape's +1 per play: AddThisCombat), so every such copy costs what the card says. Otherwise the
/// tab says "?" and is never crossed.
/// </summary>
internal static class FreeCards
{
    public static readonly string[] Powers = { nameof(FreeCardPower), nameof(FreeAttackPower), nameof(FreeSkillPower), nameof(FreePowerPower) };

    public const string Prefix = "free:";

    /// <summary>What one free power does for this card: the energy it saves, "?" when that's hidden, or crossed (wasted).</summary>
    public readonly record struct Use(PowerModel Power, int? Saved, bool Hidden, bool Wasted);

    /// <summary>Whether <paramref name="power"/> takes a charge when <paramref name="card"/> is played from your hand.</summary>
    public static bool Applies(PowerModel power, CardModel card) => power switch
    {
        FreeCardPower => true,
        FreeAttackPower => card.Type == CardType.Attack,
        FreeSkillPower => card.Type == CardType.Skill,
        FreePowerPower => card.Type == CardType.Power,
        _ => false,
    };

    /// <summary>The free powers playing <paramref name="card"/> from your hand uses up, in the game's hook order.</summary>
    public static List<Use> Uses(CardModel card, Player me)
    {
        var uses = new List<Use>();
        if (card.Pile?.Type != PileType.Hand || card.EnergyCost.Canonical < 0) return uses;
        var powers = ActiveOn(card, me);
        if (powers.Count == 0) return uses;
        var costsX = card.EnergyCost.CostsX;
        var hidden = !costsX && CostHidden(card, me);
        var saved = costsX || hidden ? 0 : CostWithoutFree(card);
        // One play makes the card free once: the first of them saves the energy, the others are spent for nothing.
        var savedYet = false;
        foreach (var power in powers)
        {
            if (costsX) { uses.Add(new Use(power, null, false, true)); continue; }
            if (hidden) { uses.Add(new Use(power, null, true, false)); continue; }
            if (saved > 0 && !savedYet) { uses.Add(new Use(power, saved, false, false)); savedYet = true; }
            else uses.Add(new Use(power, null, false, true));
        }
        return uses;
    }

    private static List<PowerModel> ActiveOn(CardModel card, Player me) =>
        me.Creature.Powers.Where(power => power.Amount > 0 && Powers.Contains(power.GetType().Name) && Applies(power, card)).ToList();

    // ------------------------------------------------------------------ what the player could know

    /// <summary>Cards in your hand whose cost you saw with no free power on them: the local cost modifiers they had then.</summary>
    private static readonly Dictionary<CardModel, int> Seen = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Each frame: remembers the cards in your hand whose cost is on screen (no free power applies to them), and forgets the
    /// ones that left your hand.
    /// </summary>
    public static void Observe(Player me, IReadOnlyList<CardModel> hand)
    {
        try
        {
            var inHand = new HashSet<CardModel>(hand, ReferenceEqualityComparer.Instance);
            foreach (var gone in Seen.Keys.Where(card => !inHand.Contains(card)).ToList()) Seen.Remove(gone);
            foreach (var card in hand)
                if (ActiveOn(card, me).Count == 0 && LocalModifiers(card) is { } mods) Seen[card] = mods.Count;
        }
        catch
        {
            Seen.Clear(); // unsure what was on screen: show nothing as known
        }
    }

    /// <summary>
    /// What this copy costs is something you couldn't know while a free power shows it as 0: you haven't seen it unchanged
    /// in your hand, and it or a copy that looks the same had its cost changed for the rest of the combat.
    /// </summary>
    public static bool CostHidden(CardModel card, Player me)
    {
        var mods = LocalModifiers(card);
        // Unknown (a game change): hide it rather than risk showing what the player can't know.
        if (mods == null) return true;
        if (Seen.TryGetValue(card, out var seenMods) && seenMods == mods.Count) return false;
        return LookAlikes(card, me).Any(lookAlike => LocalModifiers(lookAlike) is not { } localCostModifiers || localCostModifiers.Any(localCostModifier => localCostModifier.Expiration == LocalCostModifierExpiration.EndOfCombat));
    }

    private static List<LocalCostModifier>? LocalModifiers(CardModel card) =>
        Sts2State.Private<List<LocalCostModifier>?>(card.EnergyCost, "_localModifiers", null);

    /// <summary>The card and every copy of yours this fight you can't tell apart from it (same card, upgrade and enchantment).</summary>
    private static IEnumerable<CardModel> LookAlikes(CardModel card, Player me)
    {
        yield return card;
        foreach (var pile in new[] { PileType.Hand, PileType.Draw, PileType.Discard, PileType.Exhaust, PileType.Play })
        {
            IReadOnlyList<CardModel> cards;
            try { cards = pile.GetPile(me).Cards.ToList(); }
            catch { continue; }
            foreach (var otherCard in cards)
                if (!ReferenceEquals(otherCard, card) && otherCard.GetType() == card.GetType() && otherCard.CurrentUpgradeLevel == card.CurrentUpgradeLevel
                    && otherCard.Enchantment?.GetType() == card.Enchantment?.GetType())
                    yield return otherCard;
        }
    }

    /// <summary>
    /// What the card would cost without the free powers: its own cost modifiers, then every model's cost hooks
    /// (Hook.ModifyEnergyCostInCombat, both passes) except the free powers'.
    /// </summary>
    public static int CostWithoutFree(CardModel card)
    {
        try
        {
            decimal cost = card.EnergyCost.GetWithModifiers(CostModifiers.Local);
            if (card.CombatState is not { } combat || cost < 0) return Math.Max(0, (int)cost);
            var listeners = combat.IterateHookListeners().Where(model => !Powers.Contains(model.GetType().Name)).ToList();
            foreach (var model in listeners) model.TryModifyEnergyCostInCombat(card, cost, out cost);
            foreach (var model in listeners) model.TryModifyEnergyCostInCombatLate(card, cost, out cost);
            return Math.Max(0, (int)cost);
        }
        catch
        {
            return 0;
        }
    }
}
