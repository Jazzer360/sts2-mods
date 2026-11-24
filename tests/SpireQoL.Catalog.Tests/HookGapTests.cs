using System.Text;
using System.Text.RegularExpressions;
using SpireQoL.Diagnostics;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// What each game model still needs before it's read from its code (decision #195), measured by the reader capability each gap waits on, which the
/// reader names where it stops (HookReading.Capability, a ReaderCapability: round eight). Two snapshots, regenerated with SPIREQOL_UPDATE_SNAPSHOT=1:
/// Snapshots/hook-gaps.txt, each hand-written model's game class on beta, every hook body's first gap; and Snapshots/model-kinds.txt, every kind on
/// both branches (powers and relics in totals; every enchantment, affliction, potion and orb with how it's handled and its bodies' first gaps; the
/// adapter's per-class reading tables, with what would replace each entry), and the capabilities by the classes they'd unlock across every kind. A
/// class blocked by several capabilities counts in each; "alone" counts the classes it's the last one for. The reader stops a body at its first gap,
/// so the measurement is a floor, by what the code reads to.
/// </summary>
public class HookGapTests
{
    private static string SnapshotPath(string file) => Path.Combine(HookProgramTests.RepoRoot(), "tests", "SpireQoL.Catalog.Tests", "Snapshots", file);

    /// <summary>A gap's capability's words: every gap the reader names carries one.</summary>
    private static string CapabilityOf(HookReading hook, string kind) =>
        hook.Capability is not { } capability ? throw new InvalidOperationException($"{hook}: a gap with no capability (HookReader names one where it stops)")
        : capability == ReaderCapability.EffectNotRun ? $"the engine runs no read {kind} (its bodies read whole but for that)" : ReaderCapabilities.Text(capability);

    [Fact]
    public void EachHandWrittenModelsGapsAreTheSnapshots()
    {
        if (!File.Exists(BranchCode.BetaPath)) return; // no decompile here (docs/DEVELOPMENT.md "Decompiled game source")
        using var code = new BranchCode(BranchCode.BetaPath);
        var byClass = HandWrittenGaps(code);
        var text = new StringBuilder();
        text.Append("# The hand-written models' gaps (HookGapTests: SPIREQOL_UPDATE_SNAPSHOT=1 regenerates). Beta. Each hook body's first gap, by the capability the reader names.\n");
        text.Append($"# {byClass.Count} hand-written models.\n\n## Capabilities, by the classes each blocks (alone: the classes it's the last one for)\n\n");
        AppendCapabilities(text, byClass);
        text.Append("\n## Classes, each hook body's gap in reading order\n\n");
        foreach (var (name, gaps) in byClass)
            text.Append($"{name}: {string.Join(" | ", gaps.Select(gap => $"[{gap.Capability}] {gap.Gap}"))}\n");
        Matches("hook-gaps.txt", text.ToString(), "the hand-written models' gaps");
    }

    /// <summary>Each hand-written model's game class's gaps on beta, in name order.</summary>
    private static List<(string Class, List<(string Capability, string Gap)> Gaps)> HandWrittenGaps(BranchCode code)
    {
        var types = code.Assembly.GetTypes();
        var classes = HookReader.Powers(types).Concat(HookReader.Relics(types)).ToDictionary(type => type.Name, StringComparer.Ordinal);
        var byClass = new List<(string Class, List<(string Capability, string Gap)> Gaps)>();
        foreach (var name in SimModels.HandWrittenNames.Order(StringComparer.Ordinal))
        {
            if (!classes.TryGetValue(name, out var type))
            {
                byClass.Add((name, new() { ("the simulator's own model (no game class by its name)", "no game class") }));
                continue;
            }
            var reading = HookReader.Read(type, code.Read);
            var kind = HookReader.Relics(new[] { type }).Any() ? "relic" : "power";
            var gaps = reading.Hooks.Where(hook => !hook.Read).Select(hook => (CapabilityOf(hook, kind), $"{hook.Hook}: {Shortened(hook.Gap!)}")).ToList();
            // A class with no hook of its own: what it does is in another class's code (Sovereign Blade's plays read Parry, Seeking Edge).
            if (gaps.Count == 0 && reading.Hooks.Count == 0) gaps.Add(("the simulator's own model (its game class has no hooks: another class's code reads it)", "no hooks"));
            if (gaps.Count == 0) gaps.Add(("read whole: a parity proof (its hand-written model does more than its hooks: a tab, a rule)", "read whole"));
            byClass.Add((name, gaps));
        }
        return byClass;
    }

    private static void AppendCapabilities(StringBuilder text, IEnumerable<(string Class, List<(string Capability, string Gap)> Gaps)> byClass)
    {
        var capabilities = byClass.SelectMany(entry => entry.Gaps.Select(gap => gap.Capability).Distinct().Select(capability => (capability, entry.Class, Alone: entry.Gaps.All(gap => gap.Capability == capability))))
            .GroupBy(entry => entry.capability).OrderByDescending(group => group.Count(entry => entry.Alone)).ThenByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal);
        foreach (var group in capabilities)
            text.Append($"{group.Key}: {group.Count()} classes, {group.Count(entry => entry.Alone)} alone: {string.Join(", ", group.Select(entry => entry.Alone ? entry.Class + "*" : entry.Class))}\n");
    }

    /// <summary>The other kinds the simulator handles: their folder, base class and the word for one.</summary>
    private static readonly (string Folder, string Base, string Kind)[] OtherKinds =
    {
        ("Enchantments", "EnchantmentModel", "enchantment"), ("Afflictions", "AfflictionModel", "affliction"), ("Potions", "PotionModel", "potion"), ("Orbs", "OrbModel", "orb"),
    };

    /// <summary>
    /// Every kind measured, on both branches: powers' and relics' totals (read from code, hand-written, by the rule's gaps); every enchantment,
    /// affliction, potion and orb with how the mod handles it now (read live through the game's own code, a per-name rule in the adapter or the engine,
    /// a simulator kind, its ledger review) and each body's first gap; the adapter's per-class reading tables; and the capabilities across every kind.
    /// </summary>
    [Fact]
    public void EveryKindIsMeasured()
    {
        if (!File.Exists(BranchCode.BetaPath) || !File.Exists(BranchCode.MainPath)) return;
        using var beta = new BranchCode(BranchCode.BetaPath);
        using var main = new BranchCode(BranchCode.MainPath);
        var sources = ModSources();
        var text = new StringBuilder();
        text.Append("# Every kind of game model as the simulator handles it (HookGapTests: SPIREQOL_UPDATE_SNAPSHOT=1 regenerates). Beta and main.\n");
        text.Append("# Each body's first gap by the capability the reader names (decision #195, round eight). A measurement: a floor, by what the code reads to.\n\n");

        // ---- powers and relics: totals
        text.Append("## Powers and relics\n\n");
        foreach (var (branch, code) in new[] { ("beta", beta), ("main", main) })
        {
            var types = code.Assembly.GetTypes();
            var read = ReadModels.Specs(types, code.Read).Where(spec => spec.Unread == null).Select(spec => spec.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var (kind, classes) in new[] { ("powers", HookReader.Powers(types).ToList()), ("relics", HookReader.Relics(types).ToList()) })
            {
                var withHooks = classes.Where(type => HookReader.Hooks(type).Any()).ToList();
                var handWritten = classes.Count(type => SimModels.IsHandWritten(type.Name));
                var fromCode = classes.Count(type => read.Contains(type.Name));
                text.Append($"{branch} {kind}: {classes.Count} classes, {withHooks.Count} with hooks; read from code {fromCode}, hand-written {handWritten}, neither {withHooks.Count(type => !read.Contains(type.Name) && !SimModels.IsHandWritten(type.Name))} (each by its first gap: Snapshots/hook-readings.txt)\n");
            }
        }

        // ---- the other kinds
        var byClass = HandWrittenGaps(beta).Select(entry => (Class: entry.Class, entry.Gaps)).ToList();
        foreach (var (folder, baseName, kind) in OtherKinds)
        {
            var betaTypes = ClassesOf(beta, folder, baseName);
            var mainTypes = ClassesOf(main, folder, baseName);
            var names = betaTypes.Keys.Union(mainTypes.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            var lines = new List<string>();
            var handled = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                var branches = betaTypes.ContainsKey(name) && mainTypes.ContainsKey(name) ? "both" : betaTypes.ContainsKey(name) ? "beta only" : "main only";
                var type = betaTypes.GetValueOrDefault(name) ?? mainTypes[name];
                var code = betaTypes.ContainsKey(name) ? beta : main;
                var (category, how) = HandledBy(name, kind, sources);
                // An orb is read from its code (decision #214): read whole, the engine runs its programs; else its gaps, where the reader stops.
                if (kind == "orb" && HookReader.ReadOrb(type, code.Read) is var orbReading)
                {
                    if (orbReading.Whole)
                    {
                        handled["read from its code"] = handled.GetValueOrDefault("read from its code") + 1;
                        lines.Add($"{name} ({branches}; read from its code (ReadModel); {how}): {string.Join(" | ", orbReading.Hooks.Select(hook => hook.NotRun != null ? $"{hook.Hook}: not run ({hook.NotRun})" : $"{hook.Hook}: read"))}");
                        continue;
                    }
                }
                var readings = kind == "orb" ? HookReader.ReadOrb(type, code.Read).Hooks : HookReader.ReadEffects(type, code.Read);
                var gaps = new List<(string Capability, string Gap)>();
                var parts = new List<string>();
                foreach (var reading in readings)
                {
                    if (reading.NotRun != null) { parts.Add($"{reading.Hook}: not run ({reading.NotRun})"); continue; }
                    if (CalledLive(reading.Hook, sources)) { parts.Add($"{reading.Hook}: called live (the adapter runs the game's own code)"); continue; }
                    var capability = CapabilityOf(reading, kind);
                    if (branches != "main only") gaps.Add((capability, reading.Hook));
                    parts.Add($"{reading.Hook}: [{capability}] {Shortened(reading.Gap!)}");
                }
                if (branches != "main only" && gaps.Count > 0) byClass.Add(($"{name} ({kind})", gaps));
                if (category == "not modelled by name" && readings.Any(reading => reading.NotRun == null && CalledLive(reading.Hook, sources))) category = "the game's own code, called live";
                if (category == "not modelled by name" && readings.Count == 0) category = "no hooks";
                handled[category] = handled.GetValueOrDefault(category) + 1;
                lines.Add($"{name} ({branches}; {how}): {(parts.Count == 0 ? "no hooks" : string.Join(" | ", parts))}");
            }
            text.Append($"\n## {folder}: {names.Count} classes (beta {betaTypes.Count}, main {mainTypes.Count}); handled: {string.Join(", ", handled.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key} {pair.Value}"))}\n\n");
            foreach (var line in lines) text.Append(line).Append('\n');
        }

        // ---- the adapter's per-class reading tables
        text.Append("\n## The adapter's per-class readings and name gates (what a read model's own fields and values would replace)\n\n");
        foreach (var line in AdapterTables(beta, sources)) text.Append(line).Append('\n');

        // ---- the capabilities across every kind
        text.Append("\n## Capabilities across every kind, by the classes each blocks on beta (alone: the classes it's the last one for; hand-written powers and relics and every other kind's class with a body to read)\n\n");
        AppendCapabilities(text, byClass);
        Matches("model-kinds.txt", text.ToString(), "every kind's measurement");
    }

    /// <summary>A kind's classes on a branch, by name.</summary>
    private static Dictionary<string, Type> ClassesOf(BranchCode code, string folder, string baseName) =>
        code.Assembly.GetTypes().Where(type => type.Namespace == "MegaCrit.Sts2.Core.Models." + folder && !type.IsAbstract && !type.IsNested && Derives(type, baseName))
            .ToDictionary(type => type.Name, StringComparer.Ordinal);

    /// <summary>The mod's own code (not generated, not the ledger's reviews or the menu's words), by path from the mod's folder.</summary>
    private static Dictionary<string, string> ModSources()
    {
        var root = Path.Combine(HookProgramTests.RepoRoot(), "mods", "SpireQoL");
        return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(path => (Relative: Path.GetRelativePath(root, path).Replace('\\', '/'), path))
            .Where(file => !file.Relative.StartsWith("obj/", StringComparison.Ordinal) && !file.Relative.StartsWith("bin/", StringComparison.Ordinal) && !file.Relative.EndsWith(".g.cs", StringComparison.Ordinal)
                && !file.Relative.StartsWith("Diagnostics/CoverageLedger", StringComparison.Ordinal) && !file.Relative.StartsWith("Menu/", StringComparison.Ordinal) && file.Relative != "Cards/GameIcons.cs")
            .ToDictionary(file => file.Relative, file => StripComments(File.ReadAllText(file.path)), StringComparer.Ordinal);
    }

    private static string StripComments(string source) => Regex.Replace(source, @"//[^\n]*", "");

    /// <summary>
    /// How the mod handles a class of another kind now: a simulator kind of its own (an orb's OrbKind, an affliction's SimAffliction: the engine's
    /// or the models' rules for it), a per-name rule where the mod's code names it (the adapter, the engine, a feature), or nothing but its ledger
    /// review; then the ledger's status.
    /// </summary>
    private static (string Category, string Detail) HandledBy(string name, string kind, Dictionary<string, string> sources)
    {
        var how = new List<string>();
        var category = "not modelled by name";
        if (kind == "orb" && HookReader.OrbKindOf(name) is { } orb)
        {
            how.Add($"OrbKind.{orb}");
            category = "a simulator kind of its own";
        }
        if (kind == "affliction" && Enum.TryParse<SimAffliction>(name, out var affliction) && affliction != SimAffliction.Other)
        {
            how.Add($"SimAffliction.{affliction}, tested by the models");
            category = "a simulator kind of its own";
        }
        // Named as a class: nameof, a type pattern or a switch arm on it, its namespace's name, or its name as a text.
        var pattern = $@"nameof\({name}\)|""{name}""|\.(Enchantments|Afflictions|Potions|Orbs)\.{name}\b|\bis {name}\b|\b{name}\s*=>|<{name}>";
        var named = sources.Where(source => Regex.IsMatch(source.Value, pattern)).Select(source => source.Key).Order(StringComparer.Ordinal).ToList();
        if (named.Count > 0)
        {
            how.Add("named in " + string.Join(", ", named));
            if (category == "not modelled by name") category = "a per-name rule";
        }
        var review = CoverageLedger.ReviewedEntries.TryGetValue(name, out var note) ? note.Split(':')[0] : "no review";
        return (category, $"{(how.Count == 0 ? category : string.Join(" and ", how))}; ledger: {review}");
    }

    /// <summary>A hook the adapter calls on the game's own object (the enchantment's damage and block math): the game's code runs it live.</summary>
    private static bool CalledLive(string hook, Dictionary<string, string> sources) =>
        sources.Where(source => source.Key.StartsWith("Sim/Adapter/", StringComparison.Ordinal)).Any(source => Regex.IsMatch(source.Value, $@"\.{hook}\("));

    /// <summary>
    /// The adapter's per-class readings (SimReaders.Read's powers, SimReaders.Relics', RelicAmountVars) and every other per-class name in the adapter
    /// and the procs (a name gate): each class, what it reads (its own field or value, or the game's), and whether a read model would replace it.
    /// </summary>
    private static IEnumerable<string> AdapterTables(BranchCode beta, Dictionary<string, string> sources)
    {
        var types = beta.Assembly.GetTypes();
        var models = HookReader.Powers(types).Concat(HookReader.Relics(types)).ToDictionary(type => type.Name, StringComparer.Ordinal);
        var lines = new List<string>();
        var readers = sources["Sim/Adapter/SimReaders.cs"];
        foreach (var (table, start, end) in new[] { ("SimReaders.Read (powers)", "Read(PowerModel power", "return (name, amount, data);"), ("SimReaders.Relics", "public static RelicSet Relics(Player player)", "return set;") })
        {
            var body = readers[readers.IndexOf(start, StringComparison.Ordinal)..];
            body = body[..body.IndexOf(end, StringComparison.Ordinal)];
            foreach (var chunk in Regex.Split(body, @"(?=case nameof\()").Where(chunk => chunk.StartsWith("case nameof(", StringComparison.Ordinal)))
            {
                var label = chunk[..chunk.IndexOf(':')];
                var classes = Regex.Matches(label, @"nameof\((\w+)\)").Select(match => match.Groups[1].Value).ToList();
                var members = Regex.Matches(chunk, @"(Var|Private(?:<[^>]+>)?|Internal)\((?:\w+), ""(\w+)""").Select(match => (Kind: match.Groups[1].Value.StartsWith("Var", StringComparison.Ordinal) ? "value" : "field", Name: match.Groups[2].Value)).ToList();
                var game = new[] { "DisplayAmount", "Applier", "Owner.Powers", "MaxHp", "ArrowsIn", "HitCounter", "CopiesFrom" }.Where(member => chunk.Contains(member, StringComparison.Ordinal)).ToList();
                foreach (var name in classes) lines.Add($"{table}: {name}: {Replaced(name, members, game, models)}");
            }
        }
        foreach (Match entry in Regex.Matches(readers, @"\[nameof\((\w+)\)\] = Var\(""(\w+)"""))
            lines.Add($"SimReaders.RelicAmountVars: {entry.Groups[1].Value}: {Replaced(entry.Groups[1].Value, new() { ("value", entry.Groups[2].Value) }, new(), models)}");
        // Every other per-class name in the adapter and the procs: a gate on a power's or relic's class by its name.
        var gates = sources.Where(source => (source.Key.StartsWith("Sim/Adapter/", StringComparison.Ordinal) || source.Key.StartsWith("Procs/", StringComparison.Ordinal)) && source.Key != "Sim/Adapter/SimReaders.cs")
            .SelectMany(source => Regex.Matches(source.Value, @"nameof\((\w+)\)|""(\w+)""").Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)
                .Where(models.ContainsKey).Select(name => (name, source.Key)))
            .GroupBy(gate => gate.name, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal);
        var readFromCode = ReadModels.Specs(types, beta.Read).Where(spec => spec.Unread == null).Select(spec => spec.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var group in gates)
            lines.Add($"name gate: {group.Key} in {string.Join(", ", group.Select(gate => gate.Key).Distinct().Order(StringComparer.Ordinal))}: "
                + (SimModels.IsHandWritten(group.Key) ? "hand-written: goes with its model" : readFromCode.Contains(group.Key) ? "read from code: a gate its reading could replace" : "not modelled from code: a gate to replace by a rule"));
        return lines;
    }

    /// <summary>What would replace one class's reading: a read model's own fields and values (all its own), or more (what it reads of the game).</summary>
    private static string Replaced(string name, List<(string Kind, string Name)> members, List<string> game, Dictionary<string, Type> models)
    {
        var reads = members.Select(member => $"its own {member.Kind} {member.Name}").Concat(game.Select(member => $"the game's {member}")).ToList();
        var handWritten = SimModels.IsHandWritten(name) ? "hand-written" : "read from code";
        var replaced = game.Count == 0 && members.Count > 0 ? "a read model's own fields and values replace it" : "needs more than its own fields and values";
        return $"{handWritten}; reads {(reads.Count == 0 ? "nothing named" : string.Join(", ", reads))}: {replaced}{(models.ContainsKey(name) ? "" : " (no game class by its name)")}";
    }

    private static void Matches(string file, string text, string what)
    {
        var path = SnapshotPath(file);
        if (Environment.GetEnvironmentVariable("SPIREQOL_UPDATE_SNAPSHOT") == "1")
        {
            File.WriteAllText(path, text);
            return;
        }
        Assert.True(File.Exists(path), $"missing {path}: generate it with SPIREQOL_UPDATE_SNAPSHOT=1");
        Assert.True(File.ReadAllText(path).ReplaceLineEndings("\n") == text, $"{path} isn't {what}: regenerate it with SPIREQOL_UPDATE_SNAPSHOT=1 and explain the change");
    }

    /// <summary>
    /// The adapter keeps no per-class reading for a class read from its code (SimReaders.Read's powers, SimReaders.Relics, RelicAmountVars): its
    /// own fields and values are seeded for it (SimReaders.Fields, AddVars), so such a reading is dead or stands in for its code.
    /// </summary>
    [Fact]
    public void NoAdapterReadingForAClassReadFromItsCode()
    {
        if (!File.Exists(BranchCode.BetaPath)) return;
        using var beta = new BranchCode(BranchCode.BetaPath);
        var stale = AdapterTables(beta, ModSources()).Where(line => line.StartsWith("SimReaders.", StringComparison.Ordinal) && line.Contains(": read from code;", StringComparison.Ordinal)).ToList();
        Assert.True(stale.Count == 0, "the adapter reads a class read from its code by name: " + string.Join("; ", stale));
    }

    /// <summary>Every gap the reader names on either branch, of every kind, carries the capability it waits on.</summary>
    [Fact]
    public void EveryGapNamesItsCapability()
    {
        foreach (var path in new[] { BranchCode.BetaPath, BranchCode.MainPath })
        {
            if (!File.Exists(path)) continue;
            using var code = new BranchCode(path);
            var types = code.Assembly.GetTypes();
            var readings = HookReader.Powers(types).Concat(HookReader.Relics(types)).SelectMany(type => HookReader.Read(type, code.Read).Hooks.Select(hook => (type.Name, hook)))
                .Concat(OtherKinds.SelectMany(kind => ClassesOf(code, kind.Folder, kind.Base).Values).SelectMany(type => HookReader.ReadEffects(type, code.Read).Select(hook => (type.Name, hook))));
            Assert.All(readings.Where(reading => !reading.hook.Read), reading => Assert.True(reading.hook.Capability != null, $"{reading.Name}.{reading.hook}"));
        }
    }

    /// <summary>A gap's text cut short (a Modify hook's whole ways can run to pages: Vulnerable's).</summary>
    private static string Shortened(string gap) => gap.Length <= 160 ? gap : gap[..157] + "...";

    private static bool Derives(Type type, string baseName)
    {
        for (var current = type.BaseType; current != null; current = current.BaseType)
            if (current.Name == baseName) return true;
        return false;
    }
}
