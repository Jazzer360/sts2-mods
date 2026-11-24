using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A play's actions are built from its program's steps (decision #193): every command a card's play calls, on both game branches, is
/// translated into the simulator's action for it (SimAdapter.CommandsTranslated) or is a named gap with why (CommandsNotTranslated). A
/// command a game update adds fails here until it's one or the other, so a new card's effect is never silently dropped.
/// </summary>
public class StepTranslationTests
{
    public static TheoryData<string> Branches => new() { "beta", "main" };

    private static string PathOf(string branch) => branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;

    [Theory]
    [MemberData(nameof(Branches))]
    public void EveryCommandStepIsTranslatedOrANamedGap(string branch)
    {
        if (!File.Exists(PathOf(branch))) return; // no decompile of this branch here (docs/DEVELOPMENT.md "Decompiled game source")
        using var code = new BranchCode(PathOf(branch));
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in code.Cards())
            foreach (var command in ProgramReader.Read(type, code.Read).Commands)
                if (!SimAdapter.CommandsTranslated.ContainsKey(command.Command.FullName) && !SimAdapter.CommandsNotTranslated.ContainsKey(command.Command.FullName))
                    missing.Add($"{command.Command.FullName} ({type.Name})");
        Assert.True(missing.Count == 0, $"Commands a card's play calls with no translation and no named gap ({branch}):\n" + string.Join("\n", missing));
    }

    [Fact]
    public void EveryTranslationAndGapIsACommandACardCalls()
    {
        // A command no card calls on either branch any more is a stale entry.
        var called = new HashSet<string>(StringComparer.Ordinal);
        foreach (var branch in new[] { "beta", "main" })
        {
            if (!File.Exists(PathOf(branch))) return;
            using var code = new BranchCode(PathOf(branch));
            foreach (var type in code.Cards())
                foreach (var command in ProgramReader.Read(type, code.Read).Commands)
                    called.Add(command.Command.FullName);
        }
        var stale = SimAdapter.CommandsTranslated.Keys.Concat(SimAdapter.CommandsNotTranslated.Keys).Where(command => !called.Contains(command)).ToList();
        Assert.True(stale.Count == 0, "Named, but no card's play calls it on either branch:\n" + string.Join("\n", stale));
        Assert.Empty(SimAdapter.CommandsTranslated.Keys.Intersect(SimAdapter.CommandsNotTranslated.Keys));
    }
}
