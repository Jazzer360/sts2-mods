using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A power a creature can have several copies of (PowerInstanceType.Instanced), each with its own count, is read copy by copy (a report: two
/// Orbits, one about to give energy, predicted as one): every instanced power the simulator models is read from its code and kept in copies
/// (decision #196), and the adapter reads each copy with its own amount and its own state.
/// </summary>
[Collection(GameFightCollection.Name)]
public class CountdownTests
{
    [Fact]
    public void EveryInstancedModelKeepsItsCopies()
    {
        GameModels.EnsureLoaded();
        var instanced = ModelDb.AllPowers.Where(power => power.InstanceType != PowerInstanceType.None).Select(power => power.GetType().Name).ToHashSet();
        var wrong = SimModels.Names.Where(instanced.Contains).Where(name => SimModels.Get(name) is ReadModel { HasCopies: false }).ToList();
        Assert.True(wrong.Count == 0, "read models of instanced powers kept as one: " + string.Join(", ", wrong));
        Assert.True(SimModels.Get(nameof(OrbitPower)) is ReadModel { HasCopies: true });
    }

    [Fact]
    public void EachOrbitIsReadWithItsOwnCount()
    {
        var gameFight = new GameFight();
        var first = (OrbitPower)ModelDb.Power<OrbitPower>().ToMutable();
        first.ApplyInternal(gameFight.Me.Creature, amount: 1);
        var second = (OrbitPower)ModelDb.Power<OrbitPower>().ToMutable();
        second.ApplyInternal(gameFight.Me.Creature, amount: 1);
        // One has had 3 energy spent on it (OrbitPower.Data.energySpent): 1 left, the other 4.
        var data = typeof(PowerModel).GetField("_internalData", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(first)!;
        data.GetType().GetField("energySpent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(data, 3);
        var fields = SimReaders.Fields(gameFight.Me.Creature.Powers);
        Assert.Equal(new[] { (1, 3), (1, 0) }, new[] { 1, 2 }.Select(copy => (fields[$"{PowerId.Orbit.Name}@{copy}"], fields[$"{PowerId.Orbit.Name}@{copy}.Data.energySpent"])));
    }
}
