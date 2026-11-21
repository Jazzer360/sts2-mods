using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using SpireQoL.Features;
using Sts2Mods.Core.Game;

namespace SpireQoL.Diagnostics;

/// <summary>
/// The game state attached to every error report (ErrorReporter.ContextProvider), so a report
/// can be understood without reproducing it: the combat, you, every enemy, your hand with the
/// tabs and play facts the mod computed, and the recent combat history. No personal data.
/// Each value is plain text (one Sentry "extra" per key).
/// </summary>
public static class Snapshot
{
    public static IReadOnlyDictionary<string, string> Capture(QolContext ctx)
    {
        var d = new Dictionary<string, string>();
        var me = Sts2State.Me;
        var combat = Sts2State.Combat;
        d["in_combat"] = (combat != null).ToString();
        if (me == null || combat == null) return d;

        Try(d, "combat", () =>
            $"encounter {Name(combat.Encounter)}, turn {Sts2State.TurnNumber}, side {combat.CurrentSide}, player turn {Sts2State.IsPlayerTurn}");

        Try(d, "player", () =>
        {
            var creature = me.Creature;
            var pcs = me.PlayerCombatState;
            var osty = me.Osty;
            return $"{Name(me.Character)}: HP {creature.CurrentHp}/{creature.MaxHp}, block {creature.Block}, energy {pcs?.Energy}, stars {pcs?.Stars}" +
                   (osty != null ? $", Osty HP {osty.CurrentHp}/{osty.MaxHp} (alive {osty.IsAlive})" : "") +
                   $", draw pile {Sts2State.DrawPileCount(me)}, discard pile {Sts2State.DiscardPileCount(me)}";
        });
        Try(d, "player_powers", () => Powers(me.Creature));
        Try(d, "relics", () => string.Join(", ", me.Relics.Select(relic =>
            $"{relic.GetType().Name}{(relic.ShowCounter ? $" [{relic.DisplayAmount}]" : "")}{(relic.Status != default ? $" ({relic.Status})" : "")}")));
        Try(d, "orbs", () =>
        {
            var orbQueue = me.PlayerCombatState?.OrbQueue;
            if (orbQueue == null) return "none";
            var orbs = orbQueue.Orbs.Select((orb, i) => $"{i + 1}. {orb.GetType().Name} (passive {orb.PassiveVal}, evoke {orb.EvokeVal})");
            return $"slots {orbQueue.Orbs.Count}/{orbQueue.Capacity}, front first: " + (orbQueue.Orbs.Count == 0 ? "empty" : string.Join(", ", orbs));
        });

        Try(d, "enemies", () => string.Join("\n", combat.Enemies.Select(creature =>
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

        Try(d, "hand", () => string.Join("\n", Sts2State.Hand(me).Select(card =>
        {
            var tabs = ctx.Decor.Previous.TryGetValue(card, out var otherTabs)
                ? string.Join(", ", otherTabs.Select(tab => tab.Text ?? (tab.Amount > 0 ? $"{tab.Key} {tab.Amount}{(tab.AuditMax > 0 ? $" [{tab.AuditMin}-{tab.AuditMax}]" : "")}" : tab.Key) + (tab.Crossed ? " (crossed)" : "")))
                : "none";
            var facts = ctx.Predictions.PreviousFacts.TryGetValue(card, out var otherFacts) ? FactsLine(otherFacts) : "no facts";
            string cost;
            try { cost = card.EnergyCost.CostsX ? "X" : card.EnergyCost.GetAmountToSpend().ToString(); } catch { cost = "?"; }
            return $"{card.GetType().Name}{(card.IsUpgraded ? "+" : "")} ({card.Type}, cost {cost}, keywords {string.Join("/", card.Keywords)}" +
                   $"{(card.Enchantment != null ? $", enchant {card.Enchantment.GetType().Name}" : "")}{(card.Affliction != null ? $", affliction {card.Affliction.GetType().Name}" : "")})" +
                   $" | tabs: {tabs} | facts: {facts}";
        })));

        Try(d, "combat_history", () => string.Join("\n",
            CombatManager.Instance.History.Entries.Reverse().Take(40).Reverse().Select(entry => entry.HumanReadableString)));
        return d;
    }

    /// <summary>What the mod expects one play of a card to do (PlayFacts), for reports.</summary>
    public static string FactsLine(SpireQoL.Procs.PlayFacts facts) =>
        $"plays {facts.Plays}, energy {facts.Energy}, stars {facts.Stars}, exhausts {facts.TotalExhausts}, discards {facts.Discards}, draws {facts.Draws}, created {facts.Generated}, " +
        $"orbs channeled {facts.Channels}, hits {facts.AttackHits}, block gains {facts.BlockGains}, debuffs {string.Join("/", facts.EnemyDebuffs.Select(type => type.Name))}, " +
        $"orb evoke damage {facts.OrbDamageMin}-{facts.OrbDamageMax}, orb evoke block {facts.OrbBlock}, random outcome {facts.RandomOutcome}";

    private static string Powers(Creature creature) => creature.Powers.Count == 0 ? "none" : string.Join(", ", creature.Powers.Select(power => $"{power.GetType().Name} {power.Amount}"));

    private static string Name(object? model) => model?.GetType().Name ?? "none";

    private static void Try(Dictionary<string, string> d, string key, Func<string> get)
    {
        try { d[key] = get(); }
        catch (Exception ex) { d[key] = $"(unavailable: {ex.Message})"; }
    }
}
