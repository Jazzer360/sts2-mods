using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Draws by profile (decision #90): every class in the installed game that reacts to a card being drawn is reviewed in
/// SimAdapter.DrawReactions, saying what about the drawn card it reads and how the profile covers it. A game update adding one
/// fails here, before a prediction lumps together cards it tells apart.
/// </summary>
public class DrawScanTests
{
    private static readonly string[] Hooks = { "AfterCardDrawn", "AfterCardDrawnEarly" };

    private static List<string> DrawReactors()
    {
        Assert.True(GameModels.EnsureLoaded() > 500, "the game's cards didn't load");
        return typeof(AbstractModel).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(AbstractModel).IsAssignableFrom(t))
            .Where(t => Hooks.Any(h => t.GetMethod(h, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly) != null))
            .Select(t => t.Name).OrderBy(n => n).ToList();
    }

    [Fact]
    public void EveryDrawReactionIsReviewed()
    {
        var reactors = DrawReactors();
        Assert.NotEmpty(reactors);
        var missing = reactors.Where(n => !SimAdapter.DrawReactions.ContainsKey(n)).ToList();
        Assert.True(missing.Count == 0, "React to a drawn card, not reviewed for the draw profile (SimAdapter.DrawReactions): " + string.Join(", ", missing));
    }

    [Fact]
    public void NoReviewForAClassThatsGone()
    {
        var reactors = DrawReactors().ToHashSet();
        var stale = SimAdapter.DrawReactions.Keys.Where(k => !reactors.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "Reviewed in SimAdapter.DrawReactions but no longer reacting to draws: " + string.Join(", ", stale));
    }
}
