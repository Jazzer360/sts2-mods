using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The bonus rule (decision #239) against the game's own label for a bonus given up: a relic's play hook setting its Status to Normal (it goes
/// dark: Art of War's and Ripple Basin's AfterCardPlayed). On both branches, every relic whose play hook darkens it has a payout the play decides
/// (ModelSpec.Payouts: the rule's crossed tab), and every relic with such a payout darkens on a play, or a reviewed reason says why not. And
/// every class the rule would give a tab but that has no settings row to show it (ReadModels.HasTabRow) is listed: the owner's call, never shown.
/// </summary>
public class BonusRuleTests
{
    /// <summary>A relic whose play hook darkens it with no payout the rule reads, or the other way round: the class, and why.</summary>
    private static readonly Dictionary<string, string> ReviewedStatus = new(StringComparer.Ordinal)
    {
        ["PaelsEye"] = "BeforeCardPlayed goes dark on its owner's first card of the turn, giving up its extra turn (ShouldTakeExtraTurnEarly, AfterTakingExtraTurn) and its hand's exhaust as the turn ends (BeforeSideTurnEndEarly's loop): hooks the reader doesn't read yet, so no payout is read; its hand-written model keeps its crossed tab (going dark)",
        ["PaelsLegion"] = "AfterCardPlayed goes dark as its block doubling is used on the play (ModifyBlockMultiplicative, then its Cooldown): a bonus spent, not given up; its hand-written model's tab is its proc",
        ["UnsettlingLamp"] = "AfterCardPlayed goes dark once it has doubled the powers a card gave (ModifyPowerAmountGivenMultiplicative): a bonus spent, not given up; its hand-written model's tab is its proc",
    };

    /// <summary>The classes whose code pays out on a condition a play changes, or reacts to its owner losing HP, but that have no settings row: no tab.</summary>
    private static readonly Dictionary<string, string> WithoutARow = new(StringComparer.Ordinal)
    {
        ["CloakClasp"] = "its block as your turn ends, by the cards left in your hand: a play would set it up or give it up (decided where it's paid)",
        ["FakeOrichalcum"] = "its block as your turn ends with none: a play that gains block would give it up",
        ["HailstormPower"] = "its damage as your turn ends while you have a Frost orb: a play that channels or evokes one would set it up or give it up",
        ["ObsidianVice"] = "its damage as your turn ends, by the cards in your hand: a play would set it up or give it up",
        ["Orichalcum"] = "its block as your turn ends with none: a play that gains block would give it up",
        ["RegenPower"] = "its heal as your turn ends, while its owner lives: a play would give it up only by your death",
        ["RitualPower"] = "its Strength as its owner's turn ends, an enemy's (not one it got on its own turn): the rule shows yours only",
        ["ScreamingFlagon"] = "its damage as your turn ends with an empty hand: a play that empties your hand would set it up",
        ["TheBombPower"] = "its damage as your turn ends once its countdown is over: decided by its count as your turn ends",
        ["TunePower"] = "a card made free next turn, on its own count of the turn's plays: a play would set it up",
    };

    public static TheoryData<string> Branches() => new() { "beta", "main" };

    [Theory]
    [MemberData(nameof(Branches))]
    public void EveryRelicThatGoesDarkOnAPlayGivesUpAPayoutTheRuleReads(string branch)
    {
        var path = branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;
        if (!File.Exists(path)) return; // no decompile of this branch here (docs/DEVELOPMENT.md "Decompiled game source")
        var code = BranchCode.Of(path);
        var problems = new List<string>();
        foreach (var type in HookReader.Relics(code.Assembly.GetTypes()).OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            var spec = code.HookReading(type).Spec;
            var givesUp = spec.Payouts?.Any(payout => payout.DuringTheTurn != null) == true;
            var darkens = DarkensOnAPlay(type, code);
            if (spec.DarkensOnPlay && !darkens) problems.Add($"{type.Name}: read as going dark on a play, which its code doesn't");
            if (darkens == givesUp) continue;
            if (!ReviewedStatus.ContainsKey(type.Name))
                problems.Add($"{type.Name}: {(darkens ? "goes dark on a play, with no payout the play decides" : "has a payout a play gives up, and never goes dark on a play")}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// A play hook of the relic's code (BeforeCardPlayed, AfterCardPlayed, an async one's state machine included) sets its Status to Normal
    /// (RelicStatus.Normal, 0: a constant 0 stored by set_Status), from its instructions alone, whatever the reader reads of the hook.
    /// </summary>
    private static bool DarkensOnAPlay(Type relic, BranchCode code)
    {
        foreach (var hook in new[] { "BeforeCardPlayed", "AfterCardPlayed" })
        {
            if (relic.GetMethod(hook, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly) is not { } method) continue;
            var body = ProgramReader.BodyOf(method);
            var instructions = code.Read(body);
            for (var i = 1; i < instructions.Count; i++)
                if (instructions[i].IsCall && instructions[i].Member is { Name: "set_Status" } && instructions[i - 1] is { Integer: 0 } constant && constant.OpCode.Name!.StartsWith("ldc", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    [Fact]
    public void EveryReviewedRelicStillDiffers()
    {
        if (!File.Exists(BranchCode.BetaPath)) return;
        var code = BranchCode.Of(BranchCode.BetaPath);
        var relics = HookReader.Relics(code.Assembly.GetTypes()).ToDictionary(type => type.Name, StringComparer.Ordinal);
        var stale = ReviewedStatus.Keys.Where(name => !relics.TryGetValue(name, out var type)
            || DarkensOnAPlay(type, code) == (code.HookReading(type).Spec.Payouts?.Any(payout => payout.DuringTheTurn != null) == true)).ToList();
        Assert.True(stale.Count == 0, "reviewed, but the rule and the game's label agree now: " + string.Join(", ", stale));
    }

    [Fact]
    public void TheClassesTheRuleWouldTabWithoutARowAreListed()
    {
        if (!File.Exists(BranchCode.BetaPath)) return;
        var code = BranchCode.Of(BranchCode.BetaPath);
        var types = code.Assembly.GetTypes();
        var found = HookReader.Powers(types).Concat(HookReader.Relics(types))
            .Where(type => !ReadModels.HasTabRow(type.Name) && code.HookReading(type) is { Spec: var spec } && (spec.Payouts?.Count > 0 || spec.Outcome is HookOutcome.ReactsToHpLoss))
            .Select(type => type.Name).Order(StringComparer.Ordinal).ToList();
        Assert.True(WithoutARow.Keys.Order(StringComparer.Ordinal).SequenceEqual(found), "the classes the rule would tab with no row: " + string.Join(", ", found));
    }
}
