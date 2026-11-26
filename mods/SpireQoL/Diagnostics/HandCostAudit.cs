using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using SpireQoL.Sim;
using Sts2Mods.Core.Game;

namespace SpireQoL.Diagnostics;

/// <summary>
/// The simulator's live cost rule against the game's own cost (decision #334, docs/SIMULATOR.md "Audits"): for every playable card in your hand
/// that doesn't cost X, its cost before global modifiers and its facts run through the start state's cost passes (SimEngine.HandCost) must equal
/// the game's CardEnergyCost.GetWithModifiers(CostModifiers.All); and for one with a star cost (not X), its star cost before the listeners' pass
/// through the star cost pass (SimEngine.HandStarCost) the game's GetStarCostWithModifiers (decision #386). A difference means a pass is read
/// wrong, runs in the wrong order, or the adapter reads the card's local cost wrong. Checked once per board (a new start state). A board whose costs aren't known (SimState.CostsUnknown:
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
            // The fight is ending: the game's cost hooks reach no one then (IterateCombatHookListeners: Spiked Gauntlets' +1 on Powers gone), while
            // the start state was read for a fight that goes on.
            var why = MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsOverOrEnding ? CheckSkip.FightEnded : start.CostsUnknown;
            if (why != null)
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
                if (card.Keywords.Contains(CardKeyword.Unplayable)) continue;
                var game = card.EnergyCost.CostsX ? 0 : card.EnergyCost.GetWithModifiers(CostModifiers.All);
                var handCard = new SimHandCard
                {
                    Id = SimAdapter.HandId(card), Name = card.GetType().Name, Cost = game,
                    LocalCost = SimAdapter.LocalCostOf(card), LocalStarCost = SimAdapter.LocalStarCostOf(card), Card = SimReaders.CostedFacts(card),
                };
                if (!card.EnergyCost.CostsX && SimEngine.HandCost(start, Who.You, handCard) is { } simulated && simulated != game)
                    mismatches.Add($"{handCard.Name}: simulated {simulated}, game {game} (before global modifiers {handCard.LocalCost})");
                if (handCard.LocalStarCost is { } localStars && SimEngine.HandStarCost(start, Who.You, handCard) is { } simulatedStars
                    && card.GetStarCostWithModifiers() is var gameStars && simulatedStars != gameStars)
                    mismatches.Add($"{handCard.Name}: simulated {simulatedStars} stars, game {gameStars} (before the listeners {localStars})");
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
