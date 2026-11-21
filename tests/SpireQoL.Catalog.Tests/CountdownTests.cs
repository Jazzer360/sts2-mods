using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A counter power a creature can have several copies of (PowerInstanceType.Instanced), each with its own count, is read as one
/// count by the simulator: SimReaders.InstancedCounters lists them, so copies counting apart are marked not modelled (a report: two
/// Orbits, one about to give energy, predicted as one).
/// </summary>
[Collection(GameFightCollection.Name)]
public class CountdownTests
{
    [Fact]
    public void EveryInstancedCountdownIsListed()
    {
        GameModels.EnsureLoaded();
        var countdowns = typeof(SimReaders).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(Sim.Models.EventCountdownPower).IsAssignableFrom(type))
            .Select(type => type.Name).Append("PanachePower");
        var instanced = ModelDb.AllPowers.Where(power => power.InstanceType == PowerInstanceType.Instanced).Select(power => power.GetType().Name).ToHashSet();
        var missing = countdowns.Where(instanced.Contains).Where(name => !SimReaders.InstancedCounters.Contains(name)).ToList();
        Assert.True(missing.Count == 0, "instanced countdown powers missing from SimReaders.InstancedCounters: " + string.Join(", ", missing));
        Assert.Contains("OrbitPower", SimReaders.InstancedCounters);
    }

    [Fact]
    public void TwoOrbitsCountingApartAreFound()
    {
        var gameFight = new GameFight();
        var first = (OrbitPower)ModelDb.Power<OrbitPower>().ToMutable();
        first.ApplyInternal(gameFight.Me.Creature, amount: 1);
        var second = (OrbitPower)ModelDb.Power<OrbitPower>().ToMutable();
        second.ApplyInternal(gameFight.Me.Creature, amount: 1);
        Assert.Null(SimReaders.CountersApart(gameFight.Me.Creature)); // both at 4: they count together
        // One has had 3 energy spent on it (OrbitPower.Data.energySpent): 1 left, the other 4.
        var data = typeof(PowerModel).GetField("_internalData", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(first)!;
        data.GetType().GetField("energySpent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(data, 3);
        Assert.Equal("OrbitPower", SimReaders.CountersApart(gameFight.Me.Creature));
    }
}
