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
        ["NHealthBar.RefreshForeground"] = "the HP bar drawn: shown",
        ["NHealthBar.RefreshText"] = "the HP bar's number: shown",
        ["Creature.get_HpDisplay"] = "the getter itself (its field)",
        ["LagavulinMatriarch.get_IsAwake"] = "the getter itself (its field)",
        ["LagavulinMatriarch.get_SleepingVfx"] = "the getter itself (its field)",
        ["LagavulinMatriarch.GenerateAnimator"] = "its animator's states (awake or asleep): how it's drawn",
        ["LagavulinMatriarch.WakeUpMove"] = "if (!_isAwake) { its sleeping effect stopped; IsAwake = true }: read only to set it",
        ["LagavulinMatriarch.AfterDamageReceived"] = "SleepingVfx?.Stop(), then null: its sleeping effect stopped (and IsShellAwake, a flag of how it's drawn, not listed)",
        ["LagavulinMatriarch.AfterDeath"] = "SleepingVfx?.Stop(), then null: its sleeping effect stopped",
        ["LagavulinMatriarch.SlashMove"] = "SleepingVfx?.Stop(), then null, before its attack: its sleeping effect stopped",
        ["LagavulinMatriarch.Slash2Move"] = "SleepingVfx?.Stop(), then null, before its attack: its sleeping effect stopped",
        ["LagavulinMatriarch.DisembowelMove"] = "SleepingVfx?.Stop(), then null, before its attack: its sleeping effect stopped",
        ["LagavulinMatriarch.SoulSiphonMove"] = "SleepingVfx?.Stop(), then null, before its debuffs: its sleeping effect stopped",
        ["LagavulinMatriarch.Sleep"] = "its sleeping effect made and placed on its node: how it's drawn",
        ["HellraiserPower.<AfterCardDrawnEarly>b__7_0"] = "its cap on endless auto-plays (9 a turn once every enemy you could hit shows infinite HP): the adapter reads it from the board for each Strike it may play (SimAdapter's hellraised), and no simulation plays 9 Strikes from Hellraiser after an enemy's HP turns infinite in it (Steam Eruption's revive)",
    };

    public static TheoryData<string> Branches() => new() { "beta", "main" };

    [Theory]
    [MemberData(nameof(Branches))]
    public void NothingAFightRunsReadsAShownOnlyValue(string branch)
    {
        var path = branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;
        if (!File.Exists(path)) return; // no decompile of this branch here (docs/DEVELOPMENT.md "Decompiled game source")
        var code = BranchCode.Of(path);
        var getters = HookReader.ShownOnly.Select(setter => setter.Replace(".set_", ".get_", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        // Each getter's field (what it loads: LagavulinMatriarch._isAwake), read directly by its class's own code (WakeUpMove's if (!_isAwake)).
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var getter in getters)
        {
            var (className, getterName) = (getter[..getter.LastIndexOf('.')], getter[(getter.LastIndexOf('.') + 1)..]);
            if (code.Assembly.GetTypes().FirstOrDefault(type => type.Name == className)?.GetMethod(getterName, all) is not { } method) continue;
            foreach (var instruction in code.Read(method))
                if (instruction.OpCode == System.Reflection.Emit.OpCodes.Ldfld && instruction.Member is { } field && field.DeclaringType == className) fields.Add(className + "." + field.Name);
        }
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
                if (!ops.Any(instruction => instruction.Member is { } member && (instruction.IsCall && getters.Contains(member.DeclaringType + "." + member.Name)
                    || instruction.OpCode == System.Reflection.Emit.OpCodes.Ldfld && fields.Contains(member.DeclaringType + "." + member.Name)))) continue;
                var name = type != outer ? NameIn(type.Name, method.Name) : method.Name.StartsWith('<') ? NameIn(method.Name, method.Name) : method.Name;
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
