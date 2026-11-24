using MegaCrit.Sts2.Core.Models;
using SpireQoL.Procs.Programs;

namespace SpireQoL.Sim;

/// <summary>
/// The conditions a play's steps run on, for the simulator (decision #190: conditions apart from effects). Each step's condition, read
/// from the card's code, is translated once (ConditionReader): a simulator condition guards the step's ordinary action (WhenAction), asked
/// of each branch as it runs; one nothing in the play can change is decided on the board; one with no translation yet is decided on the
/// board as before, and listed by the card scan (CardCoverage) as a gap.
/// </summary>
internal static class PlayConditions
{
    /// <summary>The step's condition on this card's board.</summary>
    internal static ConditionReading Reading(CardModel card, ProgramCondition condition)
    {
        var type = card.GetType();
        return ConditionReader.Read(condition, new ConditionReader.Context(type, RuntimeCode.Read, new ProgramBoard(card), CardPrograms.Of(type)));
    }

    /// <summary>The step's condition from the card's code alone (no board): how the card scan and the energy scan classify it.</summary>
    internal static ConditionReading CodeReading(Type cardType, ProgramCondition condition) =>
        ConditionReader.Read(condition, new ConditionReader.Context(cardType, RuntimeCode.Read, Board: null, CardPrograms.Of(cardType)));

    /// <summary>The action under its step's condition: a WhenAction on a simulated one, else the action itself.</summary>
    internal static SimAction Guarded(SimAction action, ConditionReading reading) =>
        reading is { Way: ConditionWay.Simulated, Condition: { } condition } ? new WhenAction(condition, new[] { action }) : action;

    /// <summary>
    /// The step doesn't run on this board, as far as the board decides: decided false, or (not translated yet) false on the board. A
    /// simulated condition is the simulator's to ask.
    /// </summary>
    internal static bool Skipped(CardModel card, ProgramCondition condition, ConditionReading reading) => reading.Way switch
    {
        ConditionWay.Decided => reading.Holds == false,
        ConditionWay.NotRead => ProgramEvaluator.Holds(condition, new ProgramBoard(card), CardPrograms.Of(card.GetType())) == false,
        _ => false,
    };

    /// <summary>
    /// The actions the adapter builds from the facts (present when their step's condition holds, or can't be told, on the board before the
    /// play), each guarded by its command's condition when the simulator can ask it (an Osty attack, Sacrifice's block and Osty's death,
    /// Nail's hit on an Impaled enemy): a replay's later play asks it again on the board the earlier plays left.
    /// </summary>
    private static bool GuardedFromFacts(SimAction action) =>
        action is AttackAction or GainBlockAction or KillOstyAction or DislodgeAllAction or ApplyPowerAction or TakeFromHandAction;

    /// <summary>
    /// The play's arranged actions with each one the facts built guarded by its command's condition (GuardedFromFacts), and the tests the
    /// code stores before its first command asked first (AskAction, once each).
    /// </summary>
    internal static List<SimAction> Guard(List<(SimAction Action, CommandStep? Step)> arranged, CardModel card)
    {
        var program = CardPrograms.Of(card.GetType());
        var conditions = new Dictionary<CommandStep, ProgramCondition>(ReferenceEqualityComparer.Instance);
        foreach (var found in CardEffects.Find(program, _ => true)) conditions[found.Command] = found.Condition;
        var asks = new List<SimCondition>();
        var body = new List<SimAction>();
        foreach (var (action, step) in arranged)
        {
            if (action is AskAction ask)
            {
                asks.Add(ask.Condition);
                continue;
            }
            if (!GuardedFromFacts(action) || step == null || !conditions.TryGetValue(step, out var condition))
            {
                body.Add(action);
                continue;
            }
            var reading = Reading(card, condition);
            asks.AddRange(reading.AskFirst);
            body.Add(Guarded(action, reading));
        }
        var first = asks.Where(condition => condition.At != null).DistinctBy(condition => condition.At).Select(condition => (SimAction)new AskAction(condition));
        return first.Concat(body).ToList();
    }

    /// <summary>The tests a reading asks first, as AskActions (the play's draws and gains guarded where they're built put them in the body).</summary>
    internal static IEnumerable<SimAction> AsksOf(ConditionReading reading) => reading.AskFirst.Select(condition => (SimAction)new AskAction(condition));
}
