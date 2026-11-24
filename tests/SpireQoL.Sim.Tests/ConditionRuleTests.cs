using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Conditions apart from effects (decision #190): an action is the effect its command has, and a condition guards it from outside
/// (WhenAction), asked by the engine on each branch as it runs. An action that carries what to do on a condition of its own (a kill
/// reward on the attack, Timber's kill attack inside its hit, a draw's count per play index) is the shape that rule replaced, so no
/// action holds a condition or other actions except the reviewed ones below, and the members it removed don't come back.
/// </summary>
public class ConditionRuleTests
{
    /// <summary>Action members that hold a condition or actions, each with why it isn't a condition fused into an effect.</summary>
    private static readonly Dictionary<string, string> Reviewed = new()
    {
        ["WhenAction.Condition"] = "the one guard: any condition, any effect",
        ["WhenAction.Then"] = "what it guards",
        ["AskAction.Condition"] = "a test the code stores before its first command, asked there",
        ["CardPlayAction.Body"] = "the card's play itself",
        ["TakeFromHandAction.EachCard"] = "a loop's other commands, once per card taken (Second Wind's block): no condition",
        ["FirstPlayOnly.Actions"] = "the test shorthand's totals and the first play's own (Swift's draw, the forged blade): a test of the play's index, still apart from a SimCondition (TRACKER 20)",
        ["DrawnCardAction.Block"] = "Escape Plan's block on the drawn card's type: still fused with its draw (TRACKER 20, docs/CARD_PROGRAMS.md A5)",
    };

    [Fact]
    public void OnlyTheGuardHoldsAConditionOrActions()
    {
        var found = new List<string>();
        foreach (var kind in SimJson.ActionKinds)
            foreach (var property in kind.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                var type = property.PropertyType;
                var holds = typeof(SimCondition).IsAssignableFrom(type) || typeof(SimAction).IsAssignableFrom(type)
                            || type.IsGenericType && type.GetGenericArguments().Any(argument => typeof(SimAction).IsAssignableFrom(argument) || typeof(SimCondition).IsAssignableFrom(argument));
                if (holds) found.Add(kind.Name + "." + property.Name);
            }
        var unreviewed = found.Where(member => !Reviewed.ContainsKey(member)).ToList();
        Assert.True(unreviewed.Count == 0, "an action holding a condition or actions of its own (decision #190: guard it with a WhenAction):\n" + string.Join("\n", unreviewed));
        var stale = Reviewed.Keys.Where(member => !found.Contains(member)).ToList();
        Assert.True(stale.Count == 0, "reviewed, but no longer there:\n" + string.Join("\n", stale));
    }

    /// <summary>The members decision #190 removed: a condition fused into an effect, or a card-level switch standing for a step's condition.</summary>
    private static readonly Regex Removed = new(
        @"\b(KillRewards|KillReward|OnKillHitAll|HitEveryEnemy|GainsStarsOnKill|HitsAllOnKill|DrawsOnACondition|DrawsDependOnTarget|DrawCountsByPlay)\b|\.OnKill\b|\bByPlay\s*=",
        RegexOptions.Compiled);

    [Fact]
    public void TheFusedMembersDontComeBack()
    {
        var found = RepoFiles.Sources("mods/SpireQoL").Concat(RepoFiles.Sources("tests"))
            .Where(file => !file.Path.EndsWith("ConditionRuleTests.cs", StringComparison.Ordinal))
            .SelectMany(file => file.Lines.Select((line, index) => (file.Path, Line: index + 1, Text: line)))
            .Where(line => Removed.IsMatch(line.Text))
            .Select(line => $"{line.Path}:{line.Line}: {line.Text.Trim()}").ToList();
        Assert.True(found.Count == 0, "a condition fused into an effect again (decision #190: a SimCondition guarding a WhenAction):\n" + string.Join("\n", found));
    }
}
