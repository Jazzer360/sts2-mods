using System.Reflection;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The values the reader takes as only shown (HookReader.ShownOnly: a hook that only sets them isn't run, Vital Spark's AfterPowerAmountChanged
/// setting its cards' Tainted amount) are read by nothing a fight runs, on both branches: every method of the game's code that reads one is
/// reviewed here with why it doesn't change a fight. A game update whose code reads one elsewhere fails here, and the reader's rule with it.
/// </summary>
public class ShownOnlyValueTests
{
    /// <summary>"Class.Method" (an async or lambda body under the method that holds it) → why its read changes no fight.</summary>
    private static readonly Dictionary<string, string> Reviewed = new(StringComparer.Ordinal)
    {
        ["CardCmd.Afflict"] = "adds to the amount it reads (card.Affliction.Amount += amount): the value itself, read to be set",
        ["CardModel.DeepCloneFields"] = "a card's copy keeps its affliction's amount: the value itself, copied",
        ["CardState.From"] = "the multiplayer state check's copy of each card (NetFullCombatState.CardState's afflictionCount): compared, never acted on",
        ["Tainted.get_ExtraHoverTips"] = "its tooltip: Tainted's power shown with the amount",
        ["AfflictionModel.get_DynamicDescription"] = "its text: shown",
        ["AfflictionModel.get_DynamicExtraCardText"] = "its text on the card: shown",
        ["MockSelfDamageAffliction.OnPlay"] = "a mock (IsMock: the game's own tests), never on a card in a run",
        ["AfflictionModel.get_Amount"] = "the getter itself",
        ["AfflictionModel.set_Amount"] = "the setter itself",
    };

    public static TheoryData<string> Branches() => new() { "beta", "main" };

    [Theory]
    [MemberData(nameof(Branches))]
    public void NothingAFightRunsReadsAShownOnlyValue(string branch)
    {
        var path = branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;
        if (!File.Exists(path)) return; // no decompile of this branch here (docs/DEVELOPMENT.md "Decompiled game source")
        using var code = new BranchCode(path);
        var getters = HookReader.ShownOnly.Select(setter => setter.Replace(".set_", ".get_", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var readers = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in code.Assembly.GetTypes())
        {
            // A compiler-made body (an async method's state machine, a lambda's closure) is its outer method's: Class.Method by the outer names.
            var outer = type;
            while (outer.IsNested && outer.Name.StartsWith('<')) outer = outer.DeclaringType!;
            IEnumerable<MethodBase> methods;
            try { methods = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)).ToList(); }
            catch { continue; }
            foreach (var method in methods)
            {
                List<Procs.Programs.CodeOp> ops;
                try { ops = code.Read(method); }
                catch { continue; }
                if (!ops.Any(instruction => instruction.IsCall && instruction.Member is { } member && getters.Contains(member.DeclaringType + "." + member.Name))) continue;
                var name = type == outer ? method.Name : NameIn(type.Name, method.Name);
                readers.Add(outer.Name + "." + name);
            }
        }
        var unreviewed = readers.Where(reader => !Reviewed.ContainsKey(reader)).ToList();
        Assert.True(unreviewed.Count == 0, $"{branch}: read by code a fight may run (review each, or the reader's ShownOnly rule is wrong): {string.Join(", ", unreviewed)}");
    }

    /// <summary>The method a compiler-made body belongs to: "&lt;Afflict&gt;d__3" is Afflict's, "&lt;FromState&gt;b__0_1" FromState's.</summary>
    private static string NameIn(string nested, string method)
    {
        var name = nested.StartsWith('<') && nested.IndexOf('>', StringComparison.Ordinal) is > 1 and var end ? nested[1..end] : method;
        return name.Length == 0 ? method : name;
    }
}
