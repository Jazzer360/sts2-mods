using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using Sts2Mods.Core.Game;

namespace SpireQoL.Procs;

/// <summary>
/// Powers that make your next card of a type from hand free and are used up by playing one (decompile v0.114.0). Each one's
/// TryModifyEnergyCostInCombatLate sets the cost of your cards in your hand (or being played) to 0, and its
/// BeforeCardPlayed takes a charge for any such card, whatever it cost:
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
    public static readonly string[] Powers = { nameof(FreeAttackPower), nameof(FreeSkillPower), nameof(FreePowerPower) };

    public const string Prefix = "free:";

    /// <summary>What one free power does for this card: the energy it saves, "?" when that's hidden, or crossed (wasted).</summary>
    public readonly record struct Use(PowerModel Power, int? Saved, bool Hidden, bool Wasted);

    /// <summary>Whether <paramref name="power"/> takes a charge when <paramref name="card"/> is played from your hand.</summary>
    public static bool Applies(PowerModel power, CardModel card) => power switch
    {
        FreeAttackPower => card.Type == CardType.Attack,
        FreeSkillPower => card.Type == CardType.Skill,
        FreePowerPower => card.Type == CardType.Power,
        _ => false,
    };

    /// <summary>
    /// The free powers playing <paramref name="card"/> from your hand uses up, in the game's hook order. <paramref name="start"/>: the board's start
    /// state (TurnEndModel.ForecastStart.State), from which the energy saved is worked out by the simulator's cost rule; null for the game's own passes.
    /// </summary>
    public static List<Use> Uses(CardModel card, Player me, Sim.SimState? start = null)
    {
        var uses = new List<Use>();
        if (card.Pile?.Type != PileType.Hand || card.EnergyCost.Canonical < 0) return uses;
        var powers = ActiveOn(card, me);
        if (powers.Count == 0) return uses;
        var costsX = card.EnergyCost.CostsX;
        var hidden = !costsX && CostHidden(card, me);
        var saved = costsX || hidden ? 0 : CostWithoutFree(card, start);
        // One play makes the card free once: the first of them saves the energy, the others are spent for nothing.
        var savedYet = false;
        foreach (var power in powers)
        {
            if (costsX) { uses.Add(new Use(power, Saved: null, Hidden: false, Wasted: true)); continue; }
            if (hidden) { uses.Add(new Use(power, Saved: null, Hidden: true, Wasted: false)); continue; }
            if (saved > 0 && !savedYet) { uses.Add(new Use(power, saved, Hidden: false, Wasted: false)); savedYet = true; }
            else uses.Add(new Use(power, Saved: null, Hidden: false, Wasted: true));
        }
        return uses;
    }

    private static readonly List<CardModel> Gone = new();

    private static bool InHand(IReadOnlyList<CardModel> hand, CardModel card)
    {
        for (var i = 0; i < hand.Count; i++)
            if (ReferenceEquals(hand[i], card)) return true;
        return false;
    }

    /// <summary>ActiveOn(card, me).Count > 0, without the list (Observe, every frame).</summary>
    private static bool AnyActiveOn(CardModel card, Player me)
    {
        var powers = me.Creature.Powers;
        for (var i = 0; i < powers.Count; i++)
            if (powers[i].Amount > 0 && Powers.Contains(powers[i].GetType().Name) && Applies(powers[i], card)) return true;
        return false;
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
            // Every frame: nothing made unless a card left your hand (Gone, kept between frames).
            Gone.Clear();
            foreach (var card in Seen.Keys)
                if (!InHand(hand, card)) Gone.Add(card);
            foreach (var card in Gone) Seen.Remove(card);
            for (var i = 0; i < hand.Count; i++)
                if (!AnyActiveOn(hand[i], me) && LocalModifiers(hand[i]) is { } mods) Seen[hand[i]] = mods.Count;
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
        Sts2State.Private<List<LocalCostModifier>?>(card.EnergyCost, "_localModifiers", fallback: null);

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
    /// (Hook.ModifyEnergyCostInCombat, both passes) except the free powers'. Worked out through the simulator's cost rule (SimEngine.HandCost,
    /// decision #381) on the board's start state; the game's own passes where that isn't known (a pass the simulator doesn't run:
    /// SimState.CostsUnknown) or there's no start state.
    /// </summary>
    public static int CostWithoutFree(CardModel card, Sim.SimState? start = null)
    {
        try
        {
            // A copy of the start state: working out a cost caches its listeners on the state, which the simulations share (decision #107).
            if (start != null && Sim.SimAdapter.LocalCostOf(card) is { } local
                && Sim.SimEngine.LiveCost(start.Clone(), Sim.Who.You, local, Sim.SimReaders.CostedFacts(card), without: Powers) is { } simulated)
                return simulated;
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
