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
///  - Free Attack (Unrelenting), Attacks; Free Skill (Pounce), Skills.
/// So a card that already costs 0, or an X-cost card (its X is your energy, never 0 from these), wastes the charge.
/// Under Snecko Eye (ConfusedPower.AfterCardDrawn: EnergyCost.SetThisCombat with a random 0–3), the free power shows
/// the re-rolled cost as 0, so what it saves is hidden: the tab says "?" and is never crossed.
/// </summary>
internal static class FreeCards
{
    public static readonly string[] Powers = { nameof(FreeCardPower), nameof(FreeAttackPower), nameof(FreeSkillPower) };

    public const string Prefix = "free:";

    /// <summary>What one free power does for this card: the energy it saves, "?" when that's hidden, or crossed (wasted).</summary>
    public readonly record struct Use(PowerModel Power, int? Saved, bool Hidden, bool Wasted);

    /// <summary>Whether <paramref name="power"/> takes a charge when <paramref name="card"/> is played from your hand.</summary>
    public static bool Applies(PowerModel power, CardModel card) => power switch
    {
        FreeCardPower => true,
        FreeAttackPower => card.Type == CardType.Attack,
        FreeSkillPower => card.Type == CardType.Skill,
        _ => false,
    };

    /// <summary>The free powers playing <paramref name="card"/> from your hand uses up, in the game's hook order.</summary>
    public static List<Use> Uses(CardModel card, Player me)
    {
        var uses = new List<Use>();
        if (card.Pile?.Type != PileType.Hand || card.EnergyCost.Canonical < 0) return uses;
        var powers = me.Creature.Powers.Where(p => p.Amount > 0 && Powers.Contains(p.GetType().Name) && Applies(p, card)).ToList();
        if (powers.Count == 0) return uses;
        var x = card.EnergyCost.CostsX;
        var hidden = !x && CostHidden(card, me);
        var saved = x || hidden ? 0 : CostWithoutFree(card);
        // One play makes the card free once: the first of them saves the energy, the others are spent for nothing.
        var savedYet = false;
        foreach (var p in powers)
        {
            if (x) { uses.Add(new Use(p, null, false, true)); continue; }
            if (hidden) { uses.Add(new Use(p, null, true, false)); continue; }
            if (saved > 0 && !savedYet) { uses.Add(new Use(p, saved, false, false)); savedYet = true; }
            else uses.Add(new Use(p, null, false, true));
        }
        return uses;
    }

    /// <summary>
    /// Snecko Eye re-rolled this card's cost (Confused on you, and an absolute this-combat cost on the card), so with a free
    /// power showing it as 0 you can't see it.
    /// </summary>
    public static bool CostHidden(CardModel card, Player me)
    {
        if (!me.Creature.Powers.Any(p => p is ConfusedPower)) return false;
        var mods = Sts2State.Private<List<LocalCostModifier>?>(card.EnergyCost, "_localModifiers", null);
        // Unknown (a game change): hide it rather than risk showing what the player can't know.
        if (mods == null) return true;
        return mods.Any(m => m.Type == LocalCostType.Absolute && m.Expiration == LocalCostModifierExpiration.EndOfCombat);
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
            if (card.CombatState is not { } cs || cost < 0) return Math.Max(0, (int)cost);
            var listeners = cs.IterateHookListeners().Where(m => !Powers.Contains(m.GetType().Name)).ToList();
            foreach (var m in listeners) m.TryModifyEnergyCostInCombat(card, cost, out cost);
            foreach (var m in listeners) m.TryModifyEnergyCostInCombatLate(card, cost, out cost);
            return Math.Max(0, (int)cost);
        }
        catch
        {
            return 0;
        }
    }
}
