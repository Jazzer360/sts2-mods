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
        foreach (var type in typeof(MegaCrit.Sts2.Core.Models.MonsterModel).Assembly.GetTypes()
                     .Where(type => type.Namespace == "MegaCrit.Sts2.Core.Models.Monsters" && typeof(MegaCrit.Sts2.Core.Models.MonsterModel).IsAssignableFrom(type)))
        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            var parameterInfos = method.GetParameters();
            if (method.ReturnType != typeof(Task) || parameterInfos.Length != 1 || !parameterInfos[0].ParameterType.Name.StartsWith("IReadOnlyList")) continue;
            yield return ($"{type.Name}.{method.Name}", method);
        }
    }

    private static List<MovePowers.Call> Of(string key) => MovePowers.CallsOf(Moves().Single(move => move.Key == key).Method);

    private static string Describe(List<MovePowers.Call> calls) =>
        string.Join(", ", calls.Select(call => (call.Certain ? "" : "?") + call.Power.Name + (call.Constant is { } constant ? " " + constant : "")));

    [Fact]
    public void PowersOnEveryPathAreCertain()
    {
        // Straight-line moves, awaits and visual ifs in between: every power lands.
        Assert.All(Of("Crusher.BugStingMove"), call => Assert.True(call.Certain));
        Assert.All(Of("Wriggler.WriggleMove"), call => Assert.True(call.Certain)); // an if around a visual effect only
        Assert.All(Of("Flyconid.FrailSporesMove"), call => Assert.True(call.Certain)); // if (TestMode.IsOff) around the spores
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
        Assert.Equal(new[] { "PersonalHivePower", "StrengthPower", "StrengthPower" }, spit.Select(call => call.Power.Name));
        Assert.All(spit, call => Assert.False(call.Certain));
        Assert.Equal(new decimal?[] { 1, 1, 2 }, spit.Select(call => call.Constant));
    }

    [Fact]
    public void EveryMoveWithBranchPowersHasARuleOrAReason()
    {
        var missing = Moves().Select(move => (move.Key, Calls: MovePowers.CallsOf(move.Method)))
            .Where(keyCalls => keyCalls.Calls.Any(call => !call.Certain) && !MoveBranches.Rules.ContainsKey(keyCalls.Key) && !MoveBranches.Reviewed.ContainsKey(keyCalls.Key))
            .Select(keyCalls => $"{keyCalls.Key}: {Describe(keyCalls.Calls)}").ToList();
        Assert.True(missing.Count == 0, "Moves applying powers on only some branches, with no rule in Sim/Adapter/MoveBranches.cs (decision #109): " +
            "read each move in the decompile and add a rule that picks its branch from what a player sees, or a reviewed reason.\n" + string.Join("\n", missing));
    }

    [Fact]
    public void EveryRuleStillMatchesItsMove()
    {
        var moves = Moves().ToDictionary(move => move.Key, move => move.Method);
        var wrong = new List<string>();
        foreach (var (key, rule) in MoveBranches.Rules)
        {
            if (!moves.TryGetValue(key, out var method)) { wrong.Add($"{key}: no such move any more"); continue; }
            var calls = MovePowers.CallsOf(method);
            if (!rule.Shape.SequenceEqual(calls.Select(call => call.Power.Name)))
                wrong.Add($"{key}: the rule expects [{string.Join(", ", rule.Shape)}], the code applies [{Describe(calls)}]");
            else if (calls.All(call => call.Certain))
                wrong.Add($"{key}: every power lands on every path now; the rule isn't needed");
        }
        foreach (var key in MoveBranches.Reviewed.Keys.Where(key => !moves.ContainsKey(key))) wrong.Add($"{key}: reviewed, but no such move any more");
        Assert.True(wrong.Count == 0, "MoveBranches rules out of step with the game's moves (a game update?): re-read each in the decompile.\n" + string.Join("\n", wrong));
    }
}
