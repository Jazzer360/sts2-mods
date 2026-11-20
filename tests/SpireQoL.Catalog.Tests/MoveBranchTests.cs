using System.Reflection;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Intent Details on moves whose powers depend on a branch (decision #109): which Apply calls run on every way through a move
/// (Branches.OnEveryPath), and the scan that every move with branch powers in the installed game has a rule (MoveBranches.Rules,
/// its shape matching the move's code) or a reviewed reason.
/// </summary>
public class MoveBranchTests
{
    private static IEnumerable<(string Key, MethodInfo Method)> Moves()
    {
        GameModels.EnsureLoaded();
        foreach (var t in typeof(MegaCrit.Sts2.Core.Models.MonsterModel).Assembly.GetTypes()
                     .Where(x => x.Namespace == "MegaCrit.Sts2.Core.Models.Monsters" && typeof(MegaCrit.Sts2.Core.Models.MonsterModel).IsAssignableFrom(x)))
        foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            var ps = m.GetParameters();
            if (m.ReturnType != typeof(Task) || ps.Length != 1 || !ps[0].ParameterType.Name.StartsWith("IReadOnlyList")) continue;
            yield return ($"{t.Name}.{m.Name}", m);
        }
    }

    private static List<MovePowers.Call> Of(string key) => MovePowers.CallsOf(Moves().Single(x => x.Key == key).Method);

    private static string Describe(List<MovePowers.Call> calls) =>
        string.Join(", ", calls.Select(c => (c.Certain ? "" : "?") + c.Power.Name + (c.Constant is { } k ? " " + k : "")));

    [Fact]
    public void PowersOnEveryPathAreCertain()
    {
        // Straight-line moves, awaits and visual ifs in between: every power lands.
        Assert.All(Of("Crusher.BugStingMove"), c => Assert.True(c.Certain));
        Assert.All(Of("Wriggler.WriggleMove"), c => Assert.True(c.Certain)); // an if around a visual effect only
        Assert.All(Of("Flyconid.FrailSporesMove"), c => Assert.True(c.Certain)); // if (TestMode.IsOff) around the spores
    }

    [Fact]
    public void ANegatedAmountIsMinus()
    {
        // Toadpole's Spike Spit applies -SpikenAmount Thorns (takes its Thorns away): a dev-mode report showed +2 against the -2 applied.
        var spit = Assert.Single(Of("Toadpole.SpikeSpitMove"));
        Assert.Equal("ThornsPower", spit.Power.Name);
        Assert.True(spit.Negated);
        Assert.False(Assert.Single(Of("Toadpole.SpikenMove")).Negated); // its Spiken gives them: +SpikenAmount
    }

    [Fact]
    public void PowersOnOneBranchAreNot()
    {
        // Entomancer's Pheromone Spit: Personal Hive and Strength 1 below 3 Personal Hive, Strength 2 otherwise.
        var spit = Of("Entomancer.SpitMove");
        Assert.Equal(new[] { "PersonalHivePower", "StrengthPower", "StrengthPower" }, spit.Select(c => c.Power.Name));
        Assert.All(spit, c => Assert.False(c.Certain));
        Assert.Equal(new decimal?[] { 1, 1, 2 }, spit.Select(c => c.Constant));
    }

    [Fact]
    public void EveryMoveWithBranchPowersHasARuleOrAReason()
    {
        var missing = Moves().Select(x => (x.Key, Calls: MovePowers.CallsOf(x.Method)))
            .Where(x => x.Calls.Any(c => !c.Certain) && !MoveBranches.Rules.ContainsKey(x.Key) && !MoveBranches.Reviewed.ContainsKey(x.Key))
            .Select(x => $"{x.Key}: {Describe(x.Calls)}").ToList();
        Assert.True(missing.Count == 0, "Moves applying powers on only some branches, with no rule in Sim/Adapter/MoveBranches.cs (decision #109): " +
            "read each move in the decompile and add a rule that picks its branch from what a player sees, or a reviewed reason.\n" + string.Join("\n", missing));
    }

    [Fact]
    public void EveryRuleStillMatchesItsMove()
    {
        var moves = Moves().ToDictionary(x => x.Key, x => x.Method);
        var wrong = new List<string>();
        foreach (var (key, rule) in MoveBranches.Rules)
        {
            if (!moves.TryGetValue(key, out var m)) { wrong.Add($"{key}: no such move any more"); continue; }
            var calls = MovePowers.CallsOf(m);
            if (!rule.Shape.SequenceEqual(calls.Select(c => c.Power.Name)))
                wrong.Add($"{key}: the rule expects [{string.Join(", ", rule.Shape)}], the code applies [{Describe(calls)}]");
            else if (calls.All(c => c.Certain))
                wrong.Add($"{key}: every power lands on every path now; the rule isn't needed");
        }
        foreach (var key in MoveBranches.Reviewed.Keys.Where(k => !moves.ContainsKey(k))) wrong.Add($"{key}: reviewed, but no such move any more");
        Assert.True(wrong.Count == 0, "MoveBranches rules out of step with the game's moves (a game update?): re-read each in the decompile.\n" + string.Join("\n", wrong));
    }
}
