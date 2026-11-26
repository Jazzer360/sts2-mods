using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using SpireQoL.Sim;
using Sts2Mods.Core.Game;

namespace SpireQoL.Diagnostics;

/// <summary>
/// The simulator's live cost rule against the game's own cost (decision #334, docs/SIMULATOR.md "Audits"): for every playable card in your hand
/// that doesn't cost X, its cost before global modifiers and its facts run through the start state's cost passes (SimEngine.HandCost) must equal
/// the game's CardEnergyCost.GetWithModifiers(CostModifiers.All). A difference means a pass is read wrong, runs in the wrong order, or the
/// adapter reads the card's local cost wrong. Checked once per board (a new start state). A board whose costs aren't known (SimState.CostsUnknown:
/// a pass the simulator doesn't run) is skipped and counted by its reason. Nothing reaches the screen.
/// </summary>
internal static class HandCostAudit
{
    private static readonly HashSet<string> Reported = new();
    private static readonly Dictionary<string, int> Skipped = new();

    public static void Check(Player me, SimState start)
    {
        try
        {
            if (start.CostsUnknown is { } why)
            {
                lock (Skipped)
                {
                    Skipped[why] = Skipped.GetValueOrDefault(why) + 1;
                    if (Skipped[why] == 1) ModEntry.Log.Detail($"[audit] hand cost: not checked: {why}");
                }
                return;
            }
            var mismatches = new List<string>();
            foreach (var card in Sts2State.Hand(me))
            {
                if (card.EnergyCost.CostsX || card.Keywords.Contains(CardKeyword.Unplayable)) continue;
                var game = card.EnergyCost.GetWithModifiers(CostModifiers.All);
                var handCard = new SimHandCard
                {
                    Id = SimAdapter.HandId(card), Name = card.GetType().Name, Cost = game,
                    LocalCost = SimAdapter.LocalCostOf(card), Card = SimReaders.CostedFacts(card),
                };
                if (SimEngine.HandCost(start, Who.You, handCard) is { } simulated && simulated != game)
                    mismatches.Add($"{handCard.Name}: simulated {simulated}, game {game} (before global modifiers {handCard.LocalCost})");
            }
            if (mismatches.Count == 0) return;
            var details = string.Join(", ", mismatches);
            lock (Reported)
                if (!Reported.Add(details) || Reported.Count > 200) return;
            ModEntry.Log.Detail("[audit] hand cost: " + details);
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("hand-cost", $"Simulated hand card cost differs from the game's: {mismatches[0]}",
                new[] { "hand-cost" }, details, new Dictionary<string, string> { ["sim_state"] = start.ToJson() });
        }
        catch (Exception exception)
        {
            ModEntry.Log.Detail($"[audit] hand cost: not checked ({exception.GetType().Name}: {exception.Message})");
        }
    }
}
