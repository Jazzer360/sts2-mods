using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using Sts2Mods.Core.Game;

namespace SpireQoL.Procs;

/// <summary>
/// Whether a card can be played, and how many times a play of it plays: the game's own rules, read through its hooks. Every
/// prediction of a play starts here (the play facts, the simulator's plays, the tabs, the audits).
/// </summary>
public static class CardPlays
{
    /// <summary>
    /// The card can be played now, or later this turn: it's blocked only by what can change mid-turn (energy, stars). Nothing happens
    /// on play for a card that never can (Unplayable statuses and curses, blocked cards, cards whose own condition isn't met).
    /// </summary>
    public static bool CanPlayThisTurn(CardModel card)
    {
        try
        {
            if (card.CanPlay(out var reason, out _)) return true;
            const UnplayableReason never = UnplayableReason.HasUnplayableKeyword | UnplayableReason.BlockedByHook
                                           | UnplayableReason.BlockedByCardLogic | UnplayableReason.NoLivingAllies;
            return (reason & never) == 0;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>How many times the card would play right now (best case over targets), an open hand choice's auto-plays included.</summary>
    public static int Count(CardModel card)
    {
        var combat = Sts2State.Combat;
        if (combat == null) return 1;
        var auto = card.Owner is { } me && card.Pile?.Type == PileType.Hand ? ChoiceAutoPlays(me) : 0;
        var count = 1;
        foreach (var target in Targets(card, combat)) count = Math.Max(count, PlayCount(combat, card, target, out _));
        return count * Math.Max(1, auto);
    }

    /// <summary>
    /// What would make this card play more than once right now: the same calculation the game runs when you play it
    /// (CardModel.GeneratePlayCount): enchanted replays + 1, then Hook.ModifyCardPlayCount, which reports the models that changed
    /// the count. That covers Echo Form, Burst, Duplication, One-Two Punch, Signal Boost, Tag Team, Throwing Axe and anything added
    /// later. The hook is a pure query; side effects live in AfterModifyingCardPlayCount, which we never call. Tag Team only doubles
    /// attacks aimed at the enemy that has it, so a single-target card is also checked against each enemy, and every source that
    /// applies to any of them counts.
    /// </summary>
    public static List<AbstractModel> ReplaySources(CardModel card)
    {
        var sources = new List<AbstractModel>();
        var combat = Sts2State.Combat;
        if (combat == null) return sources;
        foreach (var target in Targets(card, combat))
        {
            PlayCount(combat, card, target, out var models);
            foreach (var model in models)
                if (!sources.Contains(model)) sources.Add(model);
        }
        return sources;
    }

    /// <summary>
    /// While Decisions, Decisions asks you to pick a Skill from your hand (CardSelectCmd.FromHand: the hand in select mode, the card
    /// in your play pile), the pick is auto-played Repeat times for free (CardCmd.AutoPlay, each with its own replays). 0 when no such
    /// choice is open.
    /// </summary>
    public static int ChoiceAutoPlays(Player me)
    {
        try
        {
            if (NPlayerHand.Instance is not { IsInCardSelection: true }) return 0;
            var source = me.PlayerCombatState?.PlayPile.Cards.OfType<DecisionsDecisions>().FirstOrDefault();
            return source == null ? 0 : Math.Max(1, Sts2State.Var(source, "Repeat", fallback: 3));
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>The card would be auto-played by an open hand choice (it's in your hand while one is open).</summary>
    public static bool ChoiceAutoPlayed(CardModel card)
        => card.Owner is { } me && card.Pile?.Type == PileType.Hand && ChoiceAutoPlays(me) > 0;

    /// <summary>The play count the game would give the card at this target (null: none), and the models that changed it.</summary>
    private static int PlayCount(ICombatState combat, CardModel card, Creature? target, out IEnumerable<AbstractModel> changedBy)
    {
        var count = Hook.ModifyCardPlayCount(combat, card, card.GetEnchantedReplayCount() + 1, target, out var models);
        changedBy = models;
        return count;
    }

    /// <summary>Where the play count is worked out: no target, and each hittable enemy for a card aimed at one (Tag Team).</summary>
    private static IEnumerable<Creature?> Targets(CardModel card, ICombatState combat)
    {
        yield return null;
        if (card.TargetType != TargetType.AnyEnemy) yield break;
        foreach (var enemy in combat.HittableEnemies) yield return enemy;
    }
}
