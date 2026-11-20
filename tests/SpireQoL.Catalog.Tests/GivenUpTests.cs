using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Crossed relic tabs are checked by what the game shows when the relic acts (decision #117, SimModel.Seen): a flash, the relic going
/// dark, or the energy left. Each model's choice is held to the relic's own code, so a game update that changes how one acts fails
/// here instead of reporting every play.
/// </summary>
public class GivenUpTests
{
    private static readonly string[] CardPlayHooks = { "BeforeCardPlayed", "AfterCardPlayed" };

    private static IEnumerable<(string Name, ActedSeen Seen, Type Relic)> CrossedRelics()
    {
        GameModels.EnsureLoaded();
        var relics = typeof(RelicModel).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(RelicModel).IsAssignableFrom(t)).ToDictionary(t => t.Name);
        foreach (var name in SimModels.Names)
            if (SimModels.Get(name) is { Harmful: true } m && relics.TryGetValue(name, out var relic))
                yield return (name, m.Seen, relic);
    }

    /// <summary>The names of the methods a relic's hook calls (an async one's state machine included).</summary>
    private static HashSet<string> CallsIn(Type relic, string hook)
    {
        var m = relic.GetMethod(hook, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (m == null) return new();
        var body = m.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            .GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ?? (MethodBase)m;
        return SpireQoL.Diagnostics.GameCode.Instructions(body).Select(i => (i.operand as MethodInfo)?.Name).OfType<string>().ToHashSet();
    }

    [Fact]
    public void TheFourGiveUpTabsAreCheckedByWhatTheGameShows()
    {
        var seen = CrossedRelics().ToDictionary(x => x.Name, x => x.Seen);
        Assert.Equal(ActedSeen.StatusCleared, seen["ArtOfWar"]);
        Assert.Equal(ActedSeen.StatusCleared, seen["RippleBasin"]);
        Assert.Equal(ActedSeen.StatusCleared, seen["PaelsEye"]);
        Assert.Equal(ActedSeen.NoEnergyLeft, seen["PaelsTears"]);
        Assert.Equal(ActedSeen.Flash, seen["BiiigHug"]);
    }

    [Fact]
    public void EachRelicsCodeShowsItTheWayItsModelSays()
    {
        foreach (var (name, seen, relic) in CrossedRelics())
        {
            var hooks = CardPlayHooks.Select(h => CallsIn(relic, h)).ToList();
            switch (seen)
            {
                case ActedSeen.StatusCleared:
                    // Its card-play hook sets its status, and doesn't flash (a flash would be checked as one).
                    Assert.True(hooks.Any(c => c.Contains("set_Status")), $"{name}: no card-play hook sets its status");
                    Assert.False(hooks.Any(c => c.Contains("Flash")), $"{name}: flashes on a play, so its tab can be checked by the flash");
                    break;
                case ActedSeen.NoEnergyLeft:
                    // It reads your energy as the turn ends.
                    Assert.Contains("get_Energy", CallsIn(relic, "BeforeSideTurnEnd"));
                    break;
                case ActedSeen.Flash:
                    // Some hook of it flashes when it acts (Biiig Hug: AfterShuffle).
                    Assert.True(relic.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                        .Where(m => m.Name.StartsWith("After") || m.Name.StartsWith("Before"))
                        .Any(m => CallsIn(relic, m.Name).Contains("Flash")), $"{name}: no hook of it flashes, so a flash can't check its tab");
                    break;
            }
        }
    }
}
