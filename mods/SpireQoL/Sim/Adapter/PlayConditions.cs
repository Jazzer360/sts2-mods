using MegaCrit.Sts2.Core.Models;
using SpireQoL.Procs.Programs;

namespace SpireQoL.Sim;

/// <summary>
/// The conditions a play's steps run on, for the simulator (decision #190: conditions apart from effects). Each step's condition, read
/// from the card's code, is translated once (ConditionReader): a simulator condition guards the step's ordinary action (WhenAction, put
/// there as each step is translated: SimAdapter.Steps), asked of each branch as it runs; one nothing in the play can change is decided on
/// the board; one with no translation yet is listed by the card scan (CardCoverage) as a gap, and decided on the board only where the play
/// can't change what it reads (SimAdapter.Decide, decision #311): else unknown where it runs.
/// </summary>
internal static class PlayConditions
{
    /// <summary>The step's condition on the board of the card played or the potion used (decision #335).</summary>
    internal static ConditionReading Reading(AbstractModel model, ProgramCondition condition)
    {
        var type = model.GetType();
        return ConditionReader.Read(condition, new ConditionReader.Context(type, RuntimeCode.Read, ProgramBoard.For(model), CardPrograms.Of(type)));
    }

    /// <summary>
    /// The first input of the step's condition the play can change (decision #311), with whether every test that reads one is read before the play
    /// runs any command (ConditionReader.ReadBeforeAnyCommand); null when it reads only what's fixed for the play.
    /// </summary>
    internal static (string Input, bool BeforeAnyCommand)? ChangeableInput(AbstractModel model, CommandStep step, ProgramCondition condition, ProgramBoard board)
    {
        var type = model.GetType();
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
