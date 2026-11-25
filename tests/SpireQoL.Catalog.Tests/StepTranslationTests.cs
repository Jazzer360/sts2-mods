using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A play's actions are built from its program's steps (decision #193): every command a card's play calls, on both game branches, is
/// translated into the simulator's action for it (SimAdapter.CommandsTranslated), is a named gap with why (CommandsNotTranslated: the play's
/// outcome unknown where it runs, an open TRACKER item, decision #292) or is reviewed as changing nothing the simulator predicts
/// (CommandsOutsideTheFight). A command a game update adds fails here until it's one of them, so a new card's effect is never silently dropped.
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
        var code = BranchCode.Of(PathOf(branch));
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in code.Cards())
            foreach (var command in ProgramReader.Read(type, code.Read).Commands)
                if (!Named(command.Command.FullName))
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
            var code = BranchCode.Of(PathOf(branch));
            foreach (var type in code.Cards())
                foreach (var command in ProgramReader.Read(type, code.Read).Commands)
                    called.Add(command.Command.FullName);
        }
        var stale = SimAdapter.CommandsTranslated.Keys.Concat(SimAdapter.CommandsNotTranslated.Keys).Concat(SimAdapter.CommandsOutsideTheFight.Keys)
            .Where(command => !called.Contains(command)).ToList();
        Assert.True(stale.Count == 0, "Named, but no card's play calls it on either branch:\n" + string.Join("\n", stale));
        Assert.Empty(SimAdapter.CommandsTranslated.Keys.Intersect(SimAdapter.CommandsNotTranslated.Keys));
        Assert.Empty(SimAdapter.CommandsTranslated.Keys.Intersect(SimAdapter.CommandsOutsideTheFight.Keys));
        Assert.Empty(SimAdapter.CommandsNotTranslated.Keys.Intersect(SimAdapter.CommandsOutsideTheFight.Keys));
    }

    /// <summary>
    /// Every command the simulator can't follow yet is a gap to close (decision #292): an open docs/TRACKER.md item ("- [ ]") names it in
    /// backticks, so a gap added to the list is tracked.
    /// </summary>
    [Fact]
    public void EveryCommandNotFollowedHasAnOpenTrackerItem()
    {
        var open = File.ReadLines(Path.Combine(RepoRoot(), "docs", "TRACKER.md")).Where(line => line.TrimStart().StartsWith("- [ ]", StringComparison.Ordinal)).ToList();
        var untracked = SimAdapter.CommandsNotTranslated.Keys.Where(command => !open.Any(line => line.Contains("`" + command + "`", StringComparison.Ordinal))).ToList();
        Assert.True(untracked.Count == 0, "Commands the simulator can't follow with no open TRACKER item naming them (in backticks):\n" + string.Join("\n", untracked));
    }

    private static bool Named(string command) =>
        SimAdapter.CommandsTranslated.ContainsKey(command) || SimAdapter.CommandsNotTranslated.ContainsKey(command) || SimAdapter.CommandsOutsideTheFight.ContainsKey(command);

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
