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
    internal static void Use()
    {
        SimModels.Read = () => InstalledCode.Specs;
        SimModels.ReadRules = () => ReadModels.PowerRules(CoverageLedger.ModelTypes(), RuntimeCode.Read);
    }
}

/// <summary>The installed game's code as the mod reads it (RuntimeCode), read once a test run and shared, as BranchCode shares a branch's.</summary>
internal static class InstalledCode
{
    private static readonly Lazy<List<ModelSpec>> ReadSpecs = new(() => ReadModels.Specs(CoverageLedger.ModelTypes(), RuntimeCode.Read));

    /// <summary>The read models of the installed game (ReadModels.Specs over the ledger's model types): callers only read the list.</summary>
    public static List<ModelSpec> Specs => ReadSpecs.Value;
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

    internal static string RepoRoot()
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
                foreach (var hook in code.HookReading(type).Hooks) readings.Add((kind, type.Name, hook));
        return readings;
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void EveryHookBodyIsReadOrANamedGap(string branch)
    {
        if (!File.Exists(PathOf(branch))) return; // no decompile of this branch here (docs/DEVELOPMENT.md "Decompiled game source")
        var code = BranchCode.Of(PathOf(branch));
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
    private static readonly Dictionary<string, int> LeastReadOnBeta = new() { ["power"] = 247, ["relic"] = 80 };
    private static readonly Dictionary<string, int> LeastReadOnMain = new() { ["power"] = 209, ["relic"] = 73 };

    /// <summary>The classes the rule models from their code on a branch (ReadModels.Specs: no hand-written model, hooks read whole, a hook run).</summary>
    private static HashSet<string> ReadOn(BranchCode code) =>
        code.Specs().Where(spec => spec.Unread == null).Select(spec => spec.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>The generated record of the classes read whole at the reviewed builds (Sim/Adapter/ReadModels.g.cs).</summary>
    private static string RecordFile => Path.Combine(RepoRoot(), "mods", "SpireQoL", "Sim", "Adapter", "ReadModels.g.cs");

    /// <summary>
    /// The record the game checks a read against (ReadModels.ReadWholeAtReview) is the classes the rule reads whole on both reviewed builds
    /// (a class only one branch has, on that one), and the two branches read the same classes whole wherever both have them: a class one
    /// branch reads and the other doesn't is a difference to review, not a record (SPIREQOL_UPDATE_SNAPSHOT=1 rewrites the record).
    /// </summary>
    [Fact]
    public void TheRecordIsTheClassesReadWholeOnBothBuilds()
    {
        if (!File.Exists(BranchCode.BetaPath) || !File.Exists(BranchCode.MainPath)) return;
        var beta = BranchCode.Of(BranchCode.BetaPath);
        var main = BranchCode.Of(BranchCode.MainPath);
        var (betaRead, mainRead) = (ReadOn(beta), ReadOn(main));
        var betaClasses = HookReader.Powers(beta.Assembly.GetTypes()).Concat(HookReader.Relics(beta.Assembly.GetTypes())).Concat(HookReader.Orbs(beta.Assembly.GetTypes())).Concat(HookReader.Enchantments(beta.Assembly.GetTypes())).Concat(HookReader.Afflictions(beta.Assembly.GetTypes())).Select(type => type.Name).ToHashSet(StringComparer.Ordinal);
        var mainClasses = HookReader.Powers(main.Assembly.GetTypes()).Concat(HookReader.Relics(main.Assembly.GetTypes())).Concat(HookReader.Orbs(main.Assembly.GetTypes())).Concat(HookReader.Enchantments(main.Assembly.GetTypes())).Concat(HookReader.Afflictions(main.Assembly.GetTypes())).Select(type => type.Name).ToHashSet(StringComparer.Ordinal);
        // A class whose code on the other branch has nothing the simulator runs (it reads whole with no program: main's Byrdpip counts no Swoops)
        // differs in the game's code, not in the reader.
        static HashSet<string> Nothing(BranchCode code) => HookReader.Powers(code.Assembly.GetTypes()).Concat(HookReader.Relics(code.Assembly.GetTypes()))
            .Where(type => code.HookReading(type) is { Whole: true, Spec.Hooks.Count: 0 }).Select(type => type.Name).ToHashSet(StringComparer.Ordinal);
        var (betaNothing, mainNothing) = (Nothing(beta), Nothing(main));
        var differ = betaRead.Where(mainClasses.Contains).Except(mainRead).Where(name => !mainNothing.Contains(name))
            .Concat(mainRead.Where(betaClasses.Contains).Except(betaRead).Where(name => !betaNothing.Contains(name))).Order(StringComparer.Ordinal).ToList();
        Assert.True(differ.Count == 0, "read whole on one branch, not the other (review it: the reader on the other branch's code): " + string.Join(", ", differ));
        var record = betaRead.Union(mainRead).Order(StringComparer.Ordinal).ToList();
        var text = RecordText(record);
        if (Environment.GetEnvironmentVariable("SPIREQOL_UPDATE_SNAPSHOT") == "1")
        {
            File.WriteAllText(RecordFile, text);
            return;
        }
        Assert.True(File.ReadAllText(RecordFile).ReplaceLineEndings("\n") == text,
            $"{RecordFile} isn't the classes read whole on both builds ({string.Join(", ", record.Except(ReadModels.ReadWholeAtReview).Concat(ReadModels.ReadWholeAtReview.Except(record)))} differ): " +
            "regenerate it with SPIREQOL_UPDATE_SNAPSHOT=1 and explain the change");
    }

    /// <summary>The record as C#, each class by its typed name (GameNames.g.cs: decision #136).</summary>
    private static string RecordText(IEnumerable<string> classes)
    {
        static string Typed(string name)
        {
            foreach (var (holder, kind) in new[] { (typeof(PowerId), nameof(PowerId)), (typeof(RelicId), nameof(RelicId)) })
                foreach (var field in holder.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                    if (field.GetValue(null) is { } value && value.ToString() == name) return $"{kind}.{field.Name}.Name";
            // An orb class (the engine's OrbKind, no typed name of its own): the game's class by nameof.
            if (HookReader.OrbKindOf(name) != null) return $"nameof(MegaCrit.Sts2.Core.Models.Orbs.{name})";
            // An enchantment class (no typed name of its own): the game's class by nameof (every one is on both branches: model-kinds.txt).
            if (typeof(MegaCrit.Sts2.Core.Models.EnchantmentModel).Assembly.GetType("MegaCrit.Sts2.Core.Models.Enchantments." + name) != null)
                return $"nameof(MegaCrit.Sts2.Core.Models.Enchantments.{name})";
            // An affliction class (decision #225): the game's class by nameof (every one is on both branches: model-kinds.txt).
            if (typeof(MegaCrit.Sts2.Core.Models.EnchantmentModel).Assembly.GetType("MegaCrit.Sts2.Core.Models.Afflictions." + name) != null)
                return $"nameof(MegaCrit.Sts2.Core.Models.Afflictions.{name})";
            throw new InvalidOperationException($"{name} has no typed name: run scripts/game-names.mjs");
        }
        var text = new StringBuilder();
        text.Append("// <auto-generated>\n");
        text.Append("// HookProgramTests (catalog tests) writes this from the decompiled game, beta and main: SPIREQOL_UPDATE_SNAPSHOT=1. Don't edit.\n");
        text.Append("// </auto-generated>\n\nnamespace SpireQoL.Sim;\n\npublic static partial class ReadModels\n{\n");
        text.Append("    /// <summary>\n");
        text.Append("    /// The power, relic and orb classes modelled from their code whose hooks read whole on both reviewed builds (SupportedVersions): one that\n");
        text.Append("    /// doesn't read whole in the running game is a game update's change, reported, and every simulation it's in is unknown (decision #92).\n");
        text.Append("    /// </summary>\n");
        text.Append("    internal static readonly IReadOnlySet<string> ReadWholeAtReview = new HashSet<string>(StringComparer.Ordinal)\n    {\n");
        foreach (var name in classes) text.Append("        ").Append(Typed(name)).Append(",\n");
        text.Append("    };\n}\n");
        return text.ToString();
    }

    /// <summary>
    /// The failure path (decision #92): a class read at the reviewed builds whose hooks the game's code no longer reads (a reader that fails on
    /// it, as a game update would make it) is an unread spec with why, told once, and the rest still read; no class reads into a hand-written one.
    /// </summary>
    [Fact]
    public void AClassThatNoLongerReadsIsUnreadAndToldOnce()
    {
        if (!File.Exists(BranchCode.BetaPath)) return;
        var code = BranchCode.Of(BranchCode.BetaPath);
        var told = new List<string>();
        List<CodeOp> Failing(System.Reflection.MethodBase method) =>
            method.DeclaringType?.Name == PowerId.Strength.Name && method.Name == "ModifyDamageAdditive" ? throw new InvalidOperationException("a game update") : code.Read(method);
        var specs = ReadModels.Specs(code.Assembly.GetTypes(), Failing, unread: (name, _) => told.Add(name));
        var strength = Assert.Single(specs, spec => spec.Name == PowerId.Strength.Name);
        Assert.NotNull(strength.Unread);
        Assert.Empty(strength.Hooks);
        Assert.Equal(new[] { PowerId.Strength.Name }, told);
        Assert.Equal(ReadOn(code).Count - 1, specs.Count(spec => spec.Unread == null));
    }

    [Fact]
    public void AClassIsReadOrHandWrittenNeverBoth()
    {
        var read = InstalledCode.Specs;
        Assert.DoesNotContain(read, spec => SimModels.IsHandWritten(spec.Name));
        Assert.All(read.Where(spec => spec.Unread == null && spec.Kind is ModelKind.Power or ModelKind.Relic), spec => Assert.IsType<ReadModel>(SimModels.Get(spec.Name)));
        Assert.All(read.Where(spec => spec.Unread == null && spec.Kind == ModelKind.Enchantment), spec => Assert.IsType<ReadModel>(SimModels.Enchantment(spec.Name)));
        Assert.All(read.Where(spec => spec.Unread == null && spec.Kind == ModelKind.Orb), spec => Assert.IsType<ReadModel>(SimModels.Orb(spec.Orb!.Value)));
    }

    /// <summary>A read class's coverage ledger entry says it's read from its code (its hooks, translated: the scan's), and only a read class's does.</summary>
    [Fact]
    public void EachReadClassesLedgerEntrySaysSo()
    {
        var says = CoverageLedger.ReviewedEntries.Where(entry => entry.Value.Contains("read from its code (ReadModel", StringComparison.Ordinal)).Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        var withEntries = ReadModels.ReadWholeAtReview.Where(CoverageLedger.ReviewedEntries.ContainsKey).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(withEntries.OrderBy(name => name, StringComparer.Ordinal), says.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void TheSimulatorTestsReadModelsAreTheInstalledGamesCode()
    {
        var actual = HookProgramJson.Write(InstalledCode.Specs);
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
        var code = BranchCode.Of(BranchCode.BetaPath);
        var fromFile = HookProgramJson.Write(code.Specs());
        var fromGame = HookProgramJson.Write(InstalledCode.Specs);
        Assert.Equal(fromGame, fromFile);
    }

    /// <summary>
    /// Every power and relic with combat hooks (the coverage ledger's), each hook body read, never run (with why) or a named gap, on beta: what
    /// each ledger entry's class has read (Snapshots/hook-readings.txt, regenerated with SPIREQOL_UPDATE_SNAPSHOT=1). A game update or a reader
    /// change that reads a body differently shows here, class by class.
    /// </summary>
    [Fact]
    public void EachLedgerClassesReadingsAreTheSnapshots()
    {
        if (!File.Exists(BranchCode.BetaPath)) return;
        var code = BranchCode.Of(BranchCode.BetaPath);
        var ledger = CoverageLedger.ReviewedEntries.Keys.ToHashSet(StringComparer.Ordinal);
        var lines = Readings(code).Where(reading => ledger.Contains(reading.Class)).GroupBy(reading => (reading.Kind, reading.Class))
            .Select(group => $"{group.Key.Kind} {group.Key.Class}: {string.Join(" | ", group.Select(reading => reading.Hook))}").ToList();
        var path = Path.Combine(RepoRoot(), "tests", "SpireQoL.Catalog.Tests", "Snapshots", "hook-readings.txt");
        if (Environment.GetEnvironmentVariable("SPIREQOL_UPDATE_SNAPSHOT") == "1")
        {
            File.WriteAllText(path, string.Join("\n", lines) + "\n");
            return;
        }
        Assert.True(File.Exists(path), $"missing {path}: generate it with SPIREQOL_UPDATE_SNAPSHOT=1");
        var expected = File.ReadAllText(path).ReplaceLineEndings("\n").TrimEnd().Split('\n');
        var changed = lines.Except(expected).Take(10).ToList();
        Assert.True(changed.Count == 0 && expected.Length == lines.Count, "hook readings changed (regenerate with SPIREQOL_UPDATE_SNAPSHOT=1 and explain):\n" + string.Join("\n", changed));
    }

    /// <summary>
    /// Juggling copies the third Attack where each branch's code does (round twenty-one: read from its code, where a branch rule told main's way
    /// before): beta's AfterCardPlayed, once the card is played (cardToClone == cardPlay.Card), main's BeforeCardPlayed, as it's played.
    /// </summary>
    [Theory]
    [InlineData("beta", "AfterCardPlayed")]
    [InlineData("main", "BeforeCardPlayed")]
    public void JugglingCopiesWhereEachBranchsCodeDoes(string branch, string copiesIn)
    {
        if (!File.Exists(PathOf(branch))) return;
        var spec = BranchCode.Of(PathOf(branch)).Specs().Single(read => read.Name == nameof(MegaCrit.Sts2.Core.Models.Powers.JugglingPower));
        var copying = spec.Hooks.Where(program => new ModelSpec("", ModelKind.Power, new[] { program }).Steps().OfType<MakeCopyStep>().Any()).Select(program => program.Hook.ToString());
        Assert.Equal(new[] { copiesIn }, copying);
    }

    /// <summary>Every power and relic, its hooks read or a gap, written to SPIREQOL_DUMP when set: the scan's whole list, for review.</summary>
    [Fact]
    public void ListEveryReading()
    {
        var path = Environment.GetEnvironmentVariable("SPIREQOL_DUMP");
        if (path == null || !File.Exists(BranchCode.BetaPath)) return;
        var code = BranchCode.Of(BranchCode.BetaPath);
        var text = new StringBuilder();
        foreach (var group in Readings(code).GroupBy(reading => (reading.Kind, reading.Class)))
            text.AppendLine($"{group.Key.Kind} {group.Key.Class}: {string.Join(" | ", group.Select(reading => reading.Hook))}");
        File.WriteAllText(path, text.ToString());
    }
}
