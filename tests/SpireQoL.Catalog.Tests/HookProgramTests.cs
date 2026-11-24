using System.Runtime.CompilerServices;
using System.Text;
using SpireQoL.Diagnostics;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>The catalog tests' simulator models: the hand-written ones and the ones read from the installed game's code, as in the game.</summary>
internal static class CatalogReadModels
{
    [ModuleInitializer]
    internal static void Use() => SimModels.Read = () => ReadModels.Specs(CoverageLedger.ModelTypes(), RuntimeCode.Read);
}

/// <summary>
/// Every power's and relic's hooks read into programs (decision #195): each hook body of every power and relic class on both game branches is
/// read into the simulator's hook programs or is a named gap with why, and the counts are held here (a body that gets worse fails; one that gets
/// better updates them). The classes the simulator models from their code (ReadModels.Classes) are read whole on both branches, and the
/// programs the simulator's tests run (read-models.json) are the installed game's.
/// </summary>
public class HookProgramTests
{
    public static TheoryData<string> Branches => new() { "beta", "main" };

    private static string PathOf(string branch) => branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;

    /// <summary>The simulator tests' read models: written from the installed game's code (SPIREQOL_UPDATE_SNAPSHOT=1 regenerates it).</summary>
    private static string ReadModelsFile => Path.Combine(RepoRoot(), "tests", "SpireQoL.Sim.Tests", "ReadModels", "read-models.json");

    private static string RepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "mods")) && Directory.Exists(Path.Combine(directory.FullName, "core"))) return directory.FullName;
        throw new DirectoryNotFoundException("the repository root (mods/ and core/) not found above the test's folder");
    }

    /// <summary>Every power and relic hook body on the branch, read or a named gap.</summary>
    private static List<(string Kind, string Class, HookReading Hook)> Readings(BranchCode code)
    {
        var types = code.Assembly.GetTypes();
        var readings = new List<(string, string, HookReading)>();
        foreach (var (kind, classes) in new[] { ("power", HookReader.Powers(types)), ("relic", HookReader.Relics(types)) })
            foreach (var type in classes.OrderBy(type => type.Name, StringComparer.Ordinal))
                foreach (var hook in HookReader.Read(type, code.Read).Hooks) readings.Add((kind, type.Name, hook));
        return readings;
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void EveryHookBodyIsReadOrANamedGap(string branch)
    {
        if (!File.Exists(PathOf(branch))) return; // no decompile of this branch here (docs/DEVELOPMENT.md "Decompiled game source")
        using var code = new BranchCode(PathOf(branch));
        var readings = Readings(code);
        // Every gap says why.
        Assert.All(readings.Where(reading => !reading.Hook.Read), reading => Assert.StartsWith("gap: ", reading.Hook.Gap, StringComparison.Ordinal));
        var counts = readings.GroupBy(reading => reading.Kind).ToDictionary(group => group.Key,
            group => (Read: group.Count(reading => reading.Hook.Translated), NotRun: group.Count(reading => reading.Hook.NotRun != null), Gaps: group.Count(reading => !reading.Hook.Read)));
        var report = string.Join("; ", counts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}: {pair.Value.Read} read, {pair.Value.NotRun} never run, {pair.Value.Gaps} gaps"));
        // What's read only grows: a body read before that isn't now fails here (pinned at v0.113.1 beta and v0.111.1 main).
        var least = branch == "main" ? LeastReadOnMain : LeastReadOnBeta;
        foreach (var (kind, leastRead) in least)
            Assert.True(counts.GetValueOrDefault(kind).Read >= leastRead, $"{branch}: {report} (at least {leastRead} {kind} hook bodies were read)");
    }

    /// <summary>Hook bodies read at least, per kind, on each branch (raise them as the reader reads more; a drop is a regression).</summary>
    private static readonly Dictionary<string, int> LeastReadOnBeta = new() { ["power"] = 73, ["relic"] = 9 };
    private static readonly Dictionary<string, int> LeastReadOnMain = new() { ["power"] = 65, ["relic"] = 7 };

    [Theory]
    [MemberData(nameof(Branches))]
    public void EveryReadClassIsReadWhole(string branch)
    {
        if (!File.Exists(PathOf(branch))) return;
        using var code = new BranchCode(PathOf(branch));
        var types = code.Assembly.GetTypes();
        var found = HookReader.Powers(types).Concat(HookReader.Relics(types)).Where(type => ReadModels.Classes.Contains(type.Name)).ToList();
        var missing = ReadModels.Classes.Except(found.Select(type => type.Name)).Where(name => branch == "beta").ToList();
        Assert.True(missing.Count == 0, $"read classes with no game class ({branch}): {string.Join(", ", missing)}");
        var partial = found.Select(type => HookReader.Read(type, code.Read)).Where(reading => !reading.Whole)
            .Select(reading => $"{reading.Spec.Name}: {string.Join("; ", reading.Gaps)}").ToList();
        Assert.True(partial.Count == 0, $"read classes whose hooks aren't read whole ({branch}):\n" + string.Join("\n", partial));
    }

    [Fact]
    public void AClassIsReadOrHandWrittenNeverBoth()
    {
        // A hand-written model's class name is its Name; a read class must have none (SimModels keeps the hand-written one).
        var handWritten = typeof(SimModel).Assembly.GetTypes().Where(type => !type.IsAbstract && typeof(SimModel).IsAssignableFrom(type) && type != typeof(ReadModel)
                && type.GetConstructor(Type.EmptyTypes) != null)
            .Select(type => ((SimModel)Activator.CreateInstance(type)!).Name).ToHashSet(StringComparer.Ordinal);
        Assert.Empty(ReadModels.Classes.Intersect(handWritten));
        Assert.All(ReadModels.Classes, name => Assert.IsType<ReadModel>(SimModels.Get(name)));
    }

    [Fact]
    public void TheSimulatorTestsReadModelsAreTheInstalledGamesCode()
    {
        var actual = HookProgramJson.Write(ReadModels.Specs(CoverageLedger.ModelTypes(), RuntimeCode.Read));
        if (Environment.GetEnvironmentVariable("SPIREQOL_UPDATE_SNAPSHOT") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReadModelsFile)!);
            File.WriteAllText(ReadModelsFile, actual.ReplaceLineEndings("\n") + "\n");
            return;
        }
        Assert.True(File.Exists(ReadModelsFile), $"missing {ReadModelsFile}: generate it with SPIREQOL_UPDATE_SNAPSHOT=1");
        Assert.True(File.ReadAllText(ReadModelsFile).ReplaceLineEndings("\n").TrimEnd() == actual.ReplaceLineEndings("\n").TrimEnd(),
            $"{ReadModelsFile} isn't what the installed game's code reads: regenerate it with SPIREQOL_UPDATE_SNAPSHOT=1 and explain the change");
    }

    [Fact]
    public void TheGameAndItsAssemblyFileReadTheSame()
    {
        if (!File.Exists(BranchCode.BetaPath)) return;
        using var code = new BranchCode(BranchCode.BetaPath);
        var fromFile = HookProgramJson.Write(ReadModels.Specs(code.Assembly.GetTypes(), code.Read));
        var fromGame = HookProgramJson.Write(ReadModels.Specs(CoverageLedger.ModelTypes(), RuntimeCode.Read));
        Assert.Equal(fromGame, fromFile);
    }

    /// <summary>Every power and relic, its hooks read or a gap, written to SPIREQOL_DUMP when set: the scan's whole list, for review.</summary>
    [Fact]
    public void ListEveryReading()
    {
        var path = Environment.GetEnvironmentVariable("SPIREQOL_DUMP");
        if (path == null || !File.Exists(BranchCode.BetaPath)) return;
        using var code = new BranchCode(BranchCode.BetaPath);
        var text = new StringBuilder();
        foreach (var group in Readings(code).GroupBy(reading => (reading.Kind, reading.Class)))
            text.AppendLine($"{group.Key.Kind} {group.Key.Class}: {string.Join(" | ", group.Select(reading => reading.Hook))}");
        File.WriteAllText(path, text.ToString());
    }
}
