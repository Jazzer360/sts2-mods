using SpireQoL.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Sim;

namespace SpireQoL.Diagnostics;

/// <summary>
/// The simulator's damage pipeline against the game's own numbers (docs/SIMULATOR.md "Audits"): for every card in hand, the
/// first hit of its attack on each enemy, as the simulation worked it out (models plus the captured terms), must equal the damage
/// the game's preview gives that hit (GameHitDamage: Hook.ModifyDamage on the card's damage value). A difference means a model's term is
/// wrong or a source is both modelled and captured. Reported once per card and pair of numbers; nothing reaches the screen.
/// </summary>
internal static class DamageFormulaAudit
{
    private static readonly HashSet<string> Reported = new();

    /// <param name="outcomes">The card's simulations, in the order of its targets (CardPrediction.Sim.PlayByTarget).</param>
    public static void Check(CardModel card, CardPrediction prediction, IReadOnlyDictionary<Creature, int> ids, SimOutcome[] outcomes)
    {
        if (prediction.Sim == null) return;
        var targets = prediction.Sim.PlayByTarget.Keys.ToList();
        if (targets.Count != outcomes.Length) return;
        var mismatches = new List<string>();
        for (var i = 0; i < targets.Count; i++)
        {
            // Your attack's hits (an attack Osty makes is his, with his own numbers: not this check's).
            if (prediction.Sim.PlayByTarget[targets[i]] is { ByOsty: true }) continue;
            var hits = outcomes[i].PlayFirstHits;
            if (hits.Count == 0) continue; // random before the first hit, or no attack simulated
            foreach (var (enemy, id) in ids)
            {
                // A single-target card's simulation only hits its own target.
                if (card.TargetType == TargetType.AnyEnemy && targets[i] != id) continue;
                if (!hits.TryGetValue(id, out var sim) || !enemy.IsAlive || !enemy.IsHittable) continue;
                if (GameHitDamage(card, enemy) is not { } game) continue;
                if (sim != game) mismatches.Add($"{SafeName(enemy)}: simulated {sim}, game {game}");
            }
        }
        if (mismatches.Count == 0) return;
        var name = card.GetType().Name;
        var key = name + "|" + string.Join(";", mismatches);
        if (!Reported.Add(key) || Reported.Count > 200) return;
        var details = $"{name} first hit: {string.Join(", ", mismatches)}";
        ModEntry.Log.Detail("[audit] damage formula: " + details);
        Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("damage-formula", $"Simulated hit damage differs from the game's: {name}",
            new[] { "damage-formula", name }, details, new Dictionary<string, string> { ["sim_state"] = prediction.Sim.Start.ToJson() });
    }

    /// <summary>
    /// The game's damage for one hit of the card's attack on <paramref name="enemy"/> (Hook.ModifyDamage with the card as its source, you
    /// dealing it), from its damage value (a calculated one on the enemy) and that value's props, as its preview gives it; null with none.
    /// </summary>
    private static int? GameHitDamage(CardModel card, Creature enemy)
    {
        try
        {
            if (card.Owner is not { } owner) return null;
            decimal raw;
            var props = MegaCrit.Sts2.Core.ValueProps.ValueProp.Move;
            // The card's own damage props, as its preview uses them (DamageVar.Props): an arrow attack's FromArrow is what Aim, Heavy
            // Shot's doubled Aim, Dislike You and Quietus check (v0.113.0).
            if (card.DynamicVars.TryGetValue("CalculatedDamage", out var dynamicVar) && dynamicVar is CalculatedVar calculated)
            {
                raw = calculated.Calculate(enemy);
                if (dynamicVar is CalculatedDamageVar calculatedDamage) props = calculatedDamage.Props;
            }
            else if (card.DynamicVars.TryGetValue("Damage", out var damage))
            {
                raw = damage.BaseValue;
                if (damage is DamageVar damageVar) props = damageVar.Props;
            }
            else return null;
            var dealt = MegaCrit.Sts2.Core.Hooks.Hook.ModifyDamage(owner.RunState, card.CombatState!, enemy, owner.Creature, raw, props, card, null,
                MegaCrit.Sts2.Core.Hooks.ModifyDamageHookType.All, CardPreviewMode.None, out _);
            return Math.Max(0, (int)Math.Max(0m, dealt));
        }
        catch
        {
            return null;
        }
    }

    private static string SafeName(Creature creature)
    {
        try { return creature.Monster != null ? Combat.TurnEndModel.SafeTitle(creature.Monster) : "?"; } catch { return "?"; }
    }
}
