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
        var cs = Sts2State.Combat;
        d["in_combat"] = (cs != null).ToString();
        if (me == null || cs == null) return d;

        Try(d, "combat", () =>
            $"encounter {Name(cs.Encounter)}, turn {Sts2State.TurnNumber}, side {cs.CurrentSide}, player turn {Sts2State.IsPlayerTurn}");

        Try(d, "player", () =>
        {
            var c = me.Creature;
            var pcs = me.PlayerCombatState;
            var osty = me.Osty;
            return $"{Name(me.Character)}: HP {c.CurrentHp}/{c.MaxHp}, block {c.Block}, energy {pcs?.Energy}, stars {pcs?.Stars}" +
                   (osty != null ? $", Osty HP {osty.CurrentHp}/{osty.MaxHp} (alive {osty.IsAlive})" : "") +
                   $", draw pile {Sts2State.DrawPileCount(me)}, discard pile {Sts2State.DiscardPileCount(me)}";
        });
        Try(d, "player_powers", () => Powers(me.Creature));
        Try(d, "relics", () => string.Join(", ", me.Relics.Select(r =>
            $"{r.GetType().Name}{(r.ShowCounter ? $" [{r.DisplayAmount}]" : "")}{(r.Status != default ? $" ({r.Status})" : "")}")));
        Try(d, "orbs", () =>
        {
            var q = me.PlayerCombatState?.OrbQueue;
            if (q == null) return "none";
            var orbs = q.Orbs.Select((o, i) => $"{i + 1}. {o.GetType().Name} (passive {o.PassiveVal}, evoke {o.EvokeVal})");
            return $"slots {q.Orbs.Count}/{q.Capacity}, front first: " + (q.Orbs.Count == 0 ? "empty" : string.Join(", ", orbs));
        });

        Try(d, "enemies", () => string.Join("\n", cs.Enemies.Select(e =>
        {
            var intent = "";
            try
            {
                var attacks = e.Monster?.NextMove.Intents.OfType<AttackIntent>().ToList();
                if (attacks is { Count: > 0 })
                {
                    var targets = cs.Players.Select(p => p.Creature).ToList();
                    intent = " | intent " + string.Join(" + ", attacks.Select(a => $"{a.GetSingleDamage(targets, e)}x{Math.Max(1, a.Repeats)}"));
                }
                else if (e.Monster != null)
                {
                    intent = " | intent " + string.Join(", ", e.Monster.NextMove.Intents.Select(i => i.GetType().Name));
                }
            }
            catch
            {
                // intent unavailable
            }
            return $"{Name(e.Monster)}: HP {e.CurrentHp}/{e.MaxHp}, block {e.Block}, alive {e.IsAlive}, stunned {e.IsStunned} | powers {Powers(e)}{intent}";
        })));

        Try(d, "hand", () => string.Join("\n", Sts2State.Hand(me).Select(c =>
        {
            var tabs = ctx.Decor.Previous.TryGetValue(c, out var t)
                ? string.Join(", ", t.Select(x => x.Text ?? (x.Amount > 0 ? $"{x.Key} {x.Amount}{(x.AuditMax > 0 ? $" [{x.AuditMin}-{x.AuditMax}]" : "")}" : x.Key) + (x.Crossed ? " (crossed)" : "")))
                : "none";
            var facts = ProcIndicatorsFeature.PreviousFacts.TryGetValue(c, out var f) ? FactsLine(f) : "no facts";
            string cost;
            try { cost = c.EnergyCost.CostsX ? "X" : c.EnergyCost.GetAmountToSpend().ToString(); } catch { cost = "?"; }
            return $"{c.GetType().Name}{(c.IsUpgraded ? "+" : "")} ({c.Type}, cost {cost}, keywords {string.Join("/", c.Keywords)}" +
                   $"{(c.Enchantment != null ? $", enchant {c.Enchantment.GetType().Name}" : "")}{(c.Affliction != null ? $", affliction {c.Affliction.GetType().Name}" : "")})" +
                   $" | tabs: {tabs} | facts: {facts}";
        })));

        Try(d, "combat_history", () => string.Join("\n",
            CombatManager.Instance.History.Entries.Reverse().Take(40).Reverse().Select(e => e.HumanReadableString)));
        return d;
    }

    /// <summary>What the mod expects one play of a card to do (PlayFacts), for reports.</summary>
    public static string FactsLine(SpireQoL.Procs.PlayFacts f) =>
        $"plays {f.Plays}, energy {f.Energy}, stars {f.Stars}, exhausts {f.TotalExhausts}, discards {f.Discards}, draws {f.Draws}, created {f.Generated}, " +
        $"orbs channeled {f.Channels}, hits {f.AttackHits}, block gains {f.BlockGains}, debuffs {string.Join("/", f.EnemyDebuffs.Select(x => x.Name))}, " +
        $"orb evoke damage {f.OrbDamageMin}-{f.OrbDamageMax}, orb evoke block {f.OrbBlock}, random outcome {f.RandomOutcome}";

    private static string Powers(Creature c) => c.Powers.Count == 0 ? "none" : string.Join(", ", c.Powers.Select(p => $"{p.GetType().Name} {p.Amount}"));

    private static string Name(object? model) => model?.GetType().Name ?? "none";

    private static void Try(Dictionary<string, string> d, string key, Func<string> get)
    {
        try { d[key] = get(); }
        catch (Exception ex) { d[key] = $"(unavailable: {ex.Message})"; }
    }
}
