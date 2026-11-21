using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Procs;
using SpireQoL.Sim;

namespace SpireQoL.Diagnostics;

/// <summary>
/// The simulator's damage pipeline against the game's own numbers (docs/SIMULATOR_V2.md "Audits"): for every card in hand, the
/// first hit of its attack on each enemy, as the simulation worked it out (models plus the captured terms), must equal the damage
/// the game's preview gives that hit (PlayFacts.DamagePerHit). A difference means a model's term is wrong or a source is both
/// modelled and captured. Reported once per card and pair of numbers; nothing reaches the screen.
/// </summary>
internal static class DamageFormulaAudit
{
    private static readonly HashSet<string> Reported = new();

    /// <param name="outcomes">The card's simulations, in the order of its targets (PlayFacts.Sim.PlayByTarget).</param>
    public static void Check(CardModel card, PlayFacts f, IReadOnlyDictionary<Creature, int> ids, SimOutcome[] outcomes)
    {
        if (f.Sim == null || f.AttackHits <= 0 || f.ByOsty || f.Plays <= 0) return;
        var targets = f.Sim.PlayByTarget.Keys.ToList();
        if (targets.Count != outcomes.Length) return;
        var mismatches = new List<string>();
        for (var i = 0; i < targets.Count; i++)
        {
            var hits = outcomes[i].PlayFirstHits;
            if (hits.Count == 0) continue; // random before the first hit, or no attack simulated
            foreach (var (enemy, id) in ids)
            {
                // A single-target card's simulation only hits its own target.
                if (card.TargetType == TargetType.AnyEnemy && targets[i] != id) continue;
                if (!hits.TryGetValue(id, out var sim) || !f.CanHit(enemy)) continue;
                int game;
                try { game = f.DamagePerHit(enemy); } catch { continue; }
                if (sim != game) mismatches.Add($"{SafeName(enemy)}: simulated {sim}, game {game}");
            }
        }
        if (mismatches.Count == 0) return;
        var name = card.GetType().Name;
        var key = name + "|" + string.Join(";", mismatches);
        if (!Reported.Add(key) || Reported.Count > 200) return;
        var details = $"{name} first hit: {string.Join(", ", mismatches)}";
        ModEntry.Log.Warn("[audit] damage formula: " + details);
        Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("damage-formula", $"Simulated hit damage differs from the game's: {name}",
            new[] { "damage-formula", name }, details, new Dictionary<string, string> { ["sim_state"] = f.Sim.Start.ToJson() });
    }

    private static string SafeName(Creature c)
    {
        try { return c.Monster != null ? Combat.TurnEndModel.SafeTitle(c.Monster) : "?"; } catch { return "?"; }
    }
}
