using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The power classes another class's code checks for by type (decision #312): a creature's HasPower&lt;T&gt;, GetPower&lt;T&gt;, GetPowerAmount&lt;T&gt;,
/// GetPowerInstances&lt;T&gt;, a filter of its powers by type (OfType&lt;T&gt;, is T), on either reviewed build. A card's application of one is kept as a
/// power (with no behaviour of its own where nothing models it), so the code that checks for it finds it: Vulnerable's damage reads its dealer's
/// Cruelty, a Shiv its owner's Fan of Knives, Frost its owner's Hibernate. The record the adapter keeps them by is
/// Sim/Adapter/PowerChecks.g.cs (ReadModels.CheckedByOtherCode), written here with SPIREQOL_UPDATE_SNAPSHOT=1.
/// </summary>
public class PowerChecksTests
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>
    /// The calls generic over a power type that aren't a check of a creature's powers: the commands that apply, change or remove one (PowerCmd), a
    /// tooltip of one (HoverTipFactory) and a card's text value of one (PowerVar).
    /// </summary>
    private static readonly HashSet<string> NotChecks = new(StringComparer.Ordinal) { "PowerCmd", "HoverTipFactory", "PowerVar`1" };

    private static Type Outermost(Type type)
    {
        while (type.DeclaringType != null) type = type.DeclaringType;
        return type;
    }

    /// <summary>Each power class some other class's code checks for by type, with the classes that do, on one build.</summary>
    internal static Dictionary<string, SortedSet<string>> ChecksOn(BranchCode code)
    {
        var types = code.Assembly.GetTypes();
        var powers = HookReader.Powers(types).Select(type => type.Name).ToHashSet(StringComparer.Ordinal);
        var checks = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            // The game's own scenes and nodes only show the board: what they check changes nothing in a fight.
            if (type.Namespace?.StartsWith("MegaCrit.Sts2.Core.Nodes", StringComparison.Ordinal) == true) continue;
            var checker = Outermost(type).Name;
            MethodBase[] methods;
            try { methods = type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)).ToArray(); }
            catch { continue; }
            foreach (var method in methods)
            {
                List<CodeOp> ops;
                try { ops = code.Read(method); }
                catch { continue; }
                foreach (var instruction in ops)
                {
                    if (instruction.Member is not { } member) continue;
                    string? power = null;
                    if (instruction.IsCall && member.GenericArguments is [var argument] && powers.Contains(argument) && !NotChecks.Contains(member.DeclaringType)) power = argument;
                    else if ((instruction.OpCode == OpCodes.Isinst || instruction.OpCode == OpCodes.Castclass) && member.Kind == CodeMemberKind.Type && powers.Contains(member.Name)) power = member.Name;
                    if (power == null || power == checker) continue;
                    if (!checks.TryGetValue(power, out var checkers)) checks[power] = checkers = new SortedSet<string>(StringComparer.Ordinal);
                    checkers.Add(checker);
                }
            }
        }
        return checks;
    }

    private static Dictionary<string, SortedSet<string>> Checks()
    {
        var checks = ChecksOn(BranchCode.Of(BranchCode.BetaPath));
        if (File.Exists(BranchCode.MainPath))
            foreach (var (power, checkers) in ChecksOn(BranchCode.Of(BranchCode.MainPath)))
                if (checks.TryGetValue(power, out var already)) already.UnionWith(checkers);
                else checks[power] = checkers;
        return checks;
    }

    private static string RecordFile => Path.Combine(CardPlaySnapshotTests.SourceFolder(), "..", "..", "mods", "SpireQoL", "Sim", "Adapter", "PowerChecks.g.cs");

    [Fact]
    public void TheRecordIsThePowersOtherCodeChecksFor()
    {
        if (!File.Exists(BranchCode.BetaPath)) return;
        var checks = Checks();
        var record = checks.Keys.Order(StringComparer.Ordinal).ToList();
        if (Environment.GetEnvironmentVariable("SPIREQOL_UPDATE_SNAPSHOT") == "1")
        {
            File.WriteAllText(RecordFile, Generated(checks));
            return;
        }
        var differ = record.Except(ReadModels.CheckedByOtherCode).Concat(ReadModels.CheckedByOtherCode.Except(record)).ToList();
        Assert.True(differ.Count == 0, $"{RecordFile} isn't the power classes other code checks for ({string.Join(", ", differ)} differ): regenerate it with SPIREQOL_UPDATE_SNAPSHOT=1");
    }

    [Fact]
    public void NoPowerOtherCodeChecksForIsDroppedFromAPlay()
    {
        // Every power class a card applies that another class's code checks for: the play keeps its application (SimAdapter's PowerTo), as the
        // simulator's model of it or as a power with no behaviour of its own.
        Assert.True(GameModels.EnsureLoaded() > 500, "the game's cards didn't load");
        var kept = typeof(SimAdapter).GetMethod("Simulated", BindingFlags.Static | BindingFlags.NonPublic)!;
        var dropped = new List<string>();
        foreach (var power in ReadModels.CheckedByOtherCode)
        {
            var appliedBy = ModelDb.AllCards.Where(card => Diagnostics.CardCoverage.PowersApplied(card.GetType()).Any(type => type.Name == power)).Select(card => card.GetType().Name).ToList();
            if (appliedBy.Count > 0 && !(bool)kept.Invoke(null, new object[] { power })!) dropped.Add($"{power} (applied by {string.Join(", ", appliedBy)})");
        }
        Assert.True(dropped.Count == 0, "Powers other code checks for that a card's play would drop:\n" + string.Join("\n", dropped));
        Assert.Contains(PowerId.Cruelty.Name, ReadModels.CheckedByOtherCode);
        Assert.Contains(PowerId.FanOfKnives.Name, ReadModels.CheckedByOtherCode);
        Assert.Contains(PowerId.Hibernate.Name, ReadModels.CheckedByOtherCode);
    }

    private static string Generated(Dictionary<string, SortedSet<string>> checks)
    {
        var text = new StringBuilder();
        text.Append("// <auto-generated>\n");
        text.Append("// PowerChecksTests (catalog tests) writes this from the decompiled game, beta and main: SPIREQOL_UPDATE_SNAPSHOT=1. Don't edit.\n");
        text.Append("// </auto-generated>\n\nnamespace SpireQoL.Sim;\n\npublic static partial class ReadModels\n{\n");
        text.Append("    /// <summary>\n");
        text.Append("    /// The power classes another class's code checks for by type on either reviewed build (HasPower, GetPower, GetPowerAmount, a filter of a\n");
        text.Append("    /// creature's powers by type), each with the classes that do: a card's application of one is kept as a power, with no behaviour of its own\n");
        text.Append("    /// where nothing models it, so the code that checks for it finds it (decision #312).\n");
        text.Append("    /// </summary>\n");
        text.Append("    internal static readonly IReadOnlySet<string> CheckedByOtherCode = new HashSet<string>(StringComparer.Ordinal)\n    {\n");
        foreach (var (power, checkers) in checks.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            text.Append("        PowerId.").Append(power.EndsWith("Power", StringComparison.Ordinal) ? power[..^"Power".Length] : power).Append(".Name, // ")
                .Append(string.Join(", ", checkers)).Append('\n');
        text.Append("    };\n}\n");
        return text.ToString();
    }
}
