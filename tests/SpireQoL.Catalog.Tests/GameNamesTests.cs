using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The typed names (PowerId, RelicId, CardId: Sim/GameNames.g.cs) cover every power, relic and card class of the installed game, and
/// name none it doesn't have unless the list says only the other branch has it. Out of date after a game update: run
/// <c>node scripts/game-names.mjs</c> (after <c>scripts/decompile.ps1</c>).
/// </summary>
public class GameNamesTests
{
    private static HashSet<string> GameClasses<T>()
    {
        Type[] all;
        try { all = typeof(AbstractModel).Assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { all = ex.Types.Where(t => t != null).ToArray()!; }
        // Not the game's test mocks (Models.*.Mocks: never in a real fight).
        return all.Where(t => t is { IsAbstract: false, IsClass: true } && typeof(T).IsAssignableFrom(t) && t.Namespace?.EndsWith(".Mocks") != true)
            .Select(t => t.Name).ToHashSet();
    }

    private static HashSet<string> Named<TId>() where TId : ISimName<TId> =>
        typeof(TId).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(TId))
            .Select(f => ((TId)f.GetValue(null)!).Name).ToHashSet();

    [Fact] public void EveryPowerHasItsName() => Check(GameClasses<PowerModel>(), Named<PowerId>(), "powers");
    [Fact] public void EveryRelicHasItsName() => Check(GameClasses<RelicModel>(), Named<RelicId>(), "relics");
    [Fact] public void EveryCardHasItsName() => Check(GameClasses<CardModel>(), Named<CardId>(), "cards");

    private static void Check(HashSet<string> game, HashSet<string> named, string what)
    {
        var missing = game.Except(named).Order().ToList();
        // A name the installed game lacks is fine when only the other branch has it.
        var otherBranch = GameBranches.BetaOnly.Overlaps(game) ? GameBranches.MainOnly : GameBranches.BetaOnly;
        var extra = named.Except(game).Where(n => !otherBranch.Contains(n)).Order().ToList();
        Assert.True(missing.Count == 0, $"{what} the game has with no typed name (run node scripts/game-names.mjs): {string.Join(", ", missing)}");
        Assert.True(extra.Count == 0, $"typed {what} the game doesn't have and the list doesn't mark as the other branch's: {string.Join(", ", extra)}");
    }
}
