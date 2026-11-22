using MegaCrit.Sts2.Core.Models;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>How the adapter reads the game's powers into the simulator's (SimReaders.Powers, SimReaders.Read).</summary>
[Collection(GameFightCollection.Name)]
public class PowerReadTests
{
    [Fact]
    public void ATeammatesImpaledIsReadAsTheirs()
    {
        // SimReaders.Powers named a teammate's Impaled ImpaledPowerOther but made its id before the rename, so their arrows read as
        // yours (found in a dev review). Main has no Impaled (the Fool is beta's): nothing to read there.
        GameModels.EnsureLoaded();
        if (ModelDb.AllPowers.FirstOrDefault(power => power.GetType().Name == PowerId.Impaled.Name) is not { } canon) return;
        var gameFight = new GameFight();
        var impaled = (PowerModel)canon.ToMutable();
        impaled.ApplyInternal(gameFight.Me.Creature, amount: 1);
        impaled.Applier = gameFight.Me.Creature;
        var theirs = SimReaders.Powers(gameFight.Me.Creature, idOf: _ => Who.Teammate(0));
        Assert.True(theirs.Has(SimEngine.ImpaledOther));
        Assert.False(theirs.Has(SimEngine.Impaled));
        var yours = SimReaders.Powers(gameFight.Me.Creature, idOf: _ => Who.You);
        Assert.True(yours.Has(SimEngine.Impaled));
    }
}
