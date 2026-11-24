using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using SpireQoL.Diagnostics;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A monster's hook bodies read as programs (decision #195) hold its coverage ledger entry to its code: a hook whose program changes nothing a
/// prediction reads (no command; stores only to state no move, no conditional branch reads) must be an ignore, and a hook that does
/// change something (a command such as SetMoveImmediate, a store a move or a branch reads) must name it in the entry, so a reviewed reason
/// written by eye can't drift from what the code does.
/// </summary>
public class MonsterHookTests
{
    private static readonly MethodInfo Generate = typeof(MonsterModel).GetMethod("GenerateMoveStateMachine", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>What a monster's moves and conditional branches read of its own state (each property or field named on "this").</summary>
    private static HashSet<string> ReadByItsMoves(Type type)
    {
        var read = new HashSet<string>(StringComparer.Ordinal);
        void Add(IEnumerable<ProgramValue> values)
        {
            foreach (var part in values.SelectMany(value => value.Parts()))
                if (part is QueryValue { Of: NamedValue { Name: "this" } } own) read.Add(own.Name);
        }
        foreach (var move in MonsterCode.Moves(type))
            foreach (var step in ModelPrograms.Of(type, move.Name).All())
                if (step.Condition != null) Add(step.Condition.Literals.Select(literal => literal.Test));
        var canon = ModelDb.All.OfType<MonsterModel>().FirstOrDefault(monster => monster.GetType() == type);
        if (canon == null) return read;
        if (Generate.Invoke(canon.ToMutable(), null) is not MonsterMoveStateMachine machine) return read;
        foreach (var branchState in machine.States.Values.OfType<ConditionalBranchState>())
            foreach (var (_, lambda) in MoveConditions.BranchesOf(branchState))
                if (lambda != null) Add(ProgramReader.ReadReturned(type, lambda.Method, RuntimeCode.Read).Literals.Select(literal => literal.Test));
        return read;
    }

    [Fact]
    public void EveryMonsterHooksLedgerEntryMatchesWhatItsCodeDoes()
    {
        GameModels.EnsureLoaded();
        var wrong = new List<string>();
        var monsters = CoverageLedger.ByType.Where(entry => typeof(MonsterModel).IsAssignableFrom(entry.Key)).ToList();
        Assert.True(monsters.Count >= 10, $"only {monsters.Count} monsters in the ledger");
        foreach (var (type, entry) in monsters)
        {
            var read = ReadByItsMoves(type);
            var effects = new List<string>();
            foreach (var hook in entry.Hooks.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var program = ModelPrograms.Of(type, hook);
                Assert.Empty(program.Unknowns);
                effects.AddRange(program.Commands.Select(command => command.Command.Name));
                effects.AddRange(program.All().OfType<StoreStep>().Where(store => read.Contains(store.Field)).Select(store => store.Field));
            }
            if (effects.Count == 0 && entry.Status != CoverageLedger.Status.Ignored)
                wrong.Add($"{type.Name}: its hooks change nothing a prediction reads, but its entry is {entry.Status}");
            foreach (var effect in effects.Distinct().Where(effect => !entry.Note.Contains(effect, StringComparison.Ordinal)))
                wrong.Add($"{type.Name}: its hooks' {effect} isn't named in its entry ({entry.Note})");
        }
        Assert.True(wrong.Count == 0, "Monster ledger entries that don't match their hooks' code:\n" + string.Join("\n", wrong));
    }
}
