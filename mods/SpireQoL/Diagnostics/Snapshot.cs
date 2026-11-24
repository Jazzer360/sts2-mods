using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using Sts2Mods.Core.Game;

namespace SpireQoL.Diagnostics;

/// <summary>
/// The game state attached to every error report (ErrorReporter.ContextProvider), so a report
/// can be understood without reproducing it: the combat, you, every enemy, your hand with the
/// tabs and play prediction the mod computed, and the recent combat history. No personal data.
/// Each value is plain text (one Sentry "extra" per key).
/// </summary>
public static class Snapshot
{
    public static IReadOnlyDictionary<string, string> Capture(QolContext ctx)
    {
        var fields = new Dictionary<string, string>();
        var me = Sts2State.Me;
        var combat = Sts2State.Combat;
        fields["in_combat"] = (combat != null).ToString();
        if (me == null || combat == null) return fields;

        Try(fields, "combat", () =>
            $"encounter {Name(combat.Encounter)}, turn {Sts2State.TurnNumber}, side {combat.CurrentSide}, player turn {Sts2State.IsPlayerTurn}");

        Try(fields, "player", () =>
        {
            var creature = me.Creature;
            var pcs = me.PlayerCombatState;
            var osty = me.Osty;
            return $"{Name(me.Character)}: HP {creature.CurrentHp}/{creature.MaxHp}, block {creature.Block}, energy {pcs?.Energy}, stars {pcs?.Stars}" +
                   (osty != null ? $", Osty HP {osty.CurrentHp}/{osty.MaxHp} (alive {osty.IsAlive})" : "") +
                   $", draw pile {Sts2State.DrawPileCount(me)}, discard pile {Sts2State.DiscardPileCount(me)}";
        });
        Try(fields, "player_powers", () => Powers(me.Creature));
        Try(fields, "relics", () => string.Join(", ", me.Relics.Select(relic =>
            $"{relic.GetType().Name}{(relic.ShowCounter ? $" [{relic.DisplayAmount}]" : "")}{(relic.Status != default ? $" ({relic.Status})" : "")}")));
        Try(fields, "orbs", () =>
        {
            var orbQueue = me.PlayerCombatState?.OrbQueue;
            if (orbQueue == null) return "none";
            var orbs = orbQueue.Orbs.Select((orb, index) => $"{index + 1}. {orb.GetType().Name} (passive {orb.PassiveVal}, evoke {orb.EvokeVal})");
            return $"slots {orbQueue.Orbs.Count}/{orbQueue.Capacity}, front first: " + (orbQueue.Orbs.Count == 0 ? "empty" : string.Join(", ", orbs));
        });

        Try(fields, "enemies", () => string.Join("\n", combat.Enemies.Select(creature =>
        {
            var intent = "";
            try
            {
                var attacks = creature.Monster?.NextMove.Intents.OfType<AttackIntent>().ToList();
                if (attacks is { Count: > 0 })
                {
                    var targets = combat.Players.Select(player => player.Creature).ToList();
                    intent = " | intent " + string.Join(" + ", attacks.Select(attackIntent => $"{attackIntent.GetSingleDamage(targets, creature)}x{Math.Max(1, attackIntent.Repeats)}"));
                }
                else if (creature.Monster != null)
                {
                    intent = " | intent " + string.Join(", ", creature.Monster.NextMove.Intents.Select(abstractIntent => abstractIntent.GetType().Name));
                }
            }
            catch
            {
                // intent unavailable
            }
            return $"{Name(creature.Monster)}: HP {creature.CurrentHp}/{creature.MaxHp}, block {creature.Block}, alive {creature.IsAlive}, stunned {creature.IsStunned} | powers {Powers(creature)}{intent}";
        })));

        Try(fields, "hand", () => string.Join("\n", Sts2State.Hand(me).Select(card =>
        {
            var tabs = ctx.Decor.Previous.TryGetValue(card, out var otherTabs)
                ? string.Join(", ", otherTabs.Select(tab => tab.Text ?? (tab.Amount > 0 ? $"{tab.Key} {tab.Amount}{(tab.AuditMax > 0 ? $" [{tab.AuditMin}-{tab.AuditMax}]" : "")}" : tab.Key) + (tab.Crossed ? " (crossed)" : "")))
                : "none";
            var prediction = ctx.Predictions.PreviousPredictions.TryGetValue(card, out var otherPrediction) ? PredictionLine(otherPrediction) : "no prediction";
            string cost;
            try { cost = card.EnergyCost.CostsX ? "X" : card.EnergyCost.GetAmountToSpend().ToString(); } catch { cost = "?"; }
            return $"{card.GetType().Name}{(card.IsUpgraded ? "+" : "")} ({card.Type}, cost {cost}, keywords {string.Join("/", card.Keywords)}" +
                   $"{(card.Enchantment != null ? $", enchant {card.Enchantment.GetType().Name}" : "")}{(card.Affliction != null ? $", affliction {card.Affliction.GetType().Name}" : "")})" +
                   $" | tabs: {tabs} | prediction: {prediction}";
        })));

        Try(fields, "combat_history", () => string.Join("\n",
            CombatManager.Instance.History.Entries.Reverse().Take(40).Reverse().Select(entry => entry.HumanReadableString)));
        return fields;
    }

    /// <summary>
    /// What the mod expects a play of a card to do (CardPrediction), for reports: the card play it simulated, and at the target given (none: its
    /// first aim) its simulation's counts over its outcomes, fewest-most (SimKeys.PlayEvent, PlayOrbChanneled, OrbEvokeBlock, OrbEvokeDamage).
    /// </summary>
    public static string PredictionLine(SpireQoL.Combat.CardPrediction prediction, Creature? target = null)
    {
        var line = $"plays {prediction.Plays}, energy {prediction.Energy}, stars {prediction.Stars}, random outcome {prediction.RandomOutcome}";
        string Range(string what, (int Least, int Most)? counted) => counted is { } range ? $", {what} {range.Least}-{range.Most}" : "";
        if (prediction.OutcomeAt(target) == null) return line + (prediction.SimPending ? ", simulation pending" : ", no finished simulation");
        return line + Range("draws", prediction.Logged(SpireQoL.Sim.SimKeys.PlayEvent(SpireQoL.Sim.CardEvent.Drawn), target))
                    + Range("exhausts", prediction.Logged(SpireQoL.Sim.SimKeys.PlayEvent(SpireQoL.Sim.CardEvent.Exhausted), target))
                    + Range("discards", prediction.Logged(SpireQoL.Sim.SimKeys.PlayEvent(SpireQoL.Sim.CardEvent.Discarded), target))
                    + Range("created", prediction.Logged(SpireQoL.Sim.SimKeys.PlayEvent(SpireQoL.Sim.CardEvent.Generated), target))
                    + Range("orbs channeled", prediction.Logged(SpireQoL.Sim.SimKeys.PlayOrbChanneled, target))
                    + Range("orb evoke damage", prediction.LoggedAmount(SpireQoL.Sim.SimKeys.OrbEvokeDamage, target))
                    + Range("orb evoke block", prediction.LoggedAmount(SpireQoL.Sim.SimKeys.OrbEvokeBlock, target));
    }

    private static string Powers(Creature creature) => creature.Powers.Count == 0 ? "none" : string.Join(", ", creature.Powers.Select(power => $"{power.GetType().Name} {power.Amount}"));

    private static string Name(object? model) => model?.GetType().Name ?? "none";

    private static void Try(Dictionary<string, string> fields, string key, Func<string> get)
    {
        try { fields[key] = get(); }
        catch (Exception ex) { fields[key] = $"(unavailable: {ex.Message})"; }
    }
}
