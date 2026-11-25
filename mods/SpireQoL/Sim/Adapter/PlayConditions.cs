using MegaCrit.Sts2.Core.Models;
using SpireQoL.Procs.Programs;

namespace SpireQoL.Sim;

/// <summary>
/// The conditions a play's steps run on, for the simulator (decision #190: conditions apart from effects). Each step's condition, read
/// from the card's code, is translated once (ConditionReader): a simulator condition guards the step's ordinary action (WhenAction, put
/// there as each step is translated: SimAdapter.Steps), asked of each branch as it runs; one nothing in the play can change is decided on
/// the board; one with no translation yet is decided on the board, and listed by the card scan (CardCoverage) as a gap.
/// </summary>
internal static class PlayConditions
{
    /// <summary>The step's condition on this card's board.</summary>
    internal static ConditionReading Reading(CardModel card, ProgramCondition condition)
    {
        var type = card.GetType();
        return ConditionReader.Read(condition, new ConditionReader.Context(type, RuntimeCode.Read, new ProgramBoard(card), CardPrograms.Of(type)));
    }

    /// <summary>
    /// The first input of the step's condition the play can change (decision #311), with whether every test that reads one is read before the play
    /// runs any command (ConditionReader.ReadBeforeAnyCommand); null when it reads only what's fixed for the play.
    /// </summary>
    internal static (string Input, bool BeforeAnyCommand)? ChangeableInput(CardModel card, CommandStep step, ProgramCondition condition, ProgramBoard board)
    {
        var type = card.GetType();
        var program = CardPrograms.Of(type);
        var context = new ConditionReader.Context(type, RuntimeCode.Read, Board: null, program);
        string? first = null;
        var beforeAnyCommand = true;
        foreach (var way in condition.Terms)
        {
            // A way a test of what's fixed for the play closes on the board (Charge's upgrade) is closed whatever its other tests read.
            if (way.Any(literal => ConditionReader.ChangeableInput(literal.Test, context) == null
                                   && ProgramEvaluator.Holds(ProgramCondition.Of(literal), board, program) == false))
                continue;
            foreach (var literal in way)
            {
                if (ConditionReader.ChangeableInput(literal.Test, context) is not { } input) continue;
                first ??= input;
                beforeAnyCommand &= ConditionReader.ReadBeforeAnyCommand(literal.Test, step, context);
            }
        }
        return first == null ? null : (first, beforeAnyCommand);
    }

    /// <summary>The step's condition from the card's code alone (no board): how the card scan classifies it.</summary>
    internal static ConditionReading CodeReading(Type cardType, ProgramCondition condition) =>
        ConditionReader.Read(condition, new ConditionReader.Context(cardType, RuntimeCode.Read, Board: null, CardPrograms.Of(cardType)));
}
