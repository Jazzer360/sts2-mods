using System.Reflection;
using SpireQoL.Diagnostics;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The simulator's listeners leave out a dead enemy's powers (SimEngine.Listeners), as the game removes them at its death
/// (Creature.RemoveAllPowersAfterDeath). A power whose class keeps it past its owner's death (it overrides ShouldPowerBeRemovedAfterOwnerDeath:
/// Minion, Reattach, Steam Eruption, Die For You...) goes on listening in the game: one that answers a hook in the simulator must say so from its
/// code (SimModel.StaysOnDeadOwner, read: a pet that dies keeps it, SimEngine.PetDied; a dead enemy with it is unknown, SimEngine.AllyDeaths), so
/// a game update that gives a listening model that override without the engine following it fails here. (Steam Eruption's model answers no hook:
/// its tab is judged on how the turn ends.)
/// </summary>
public class DeathListenerTests
{
    [Fact]
    public void EveryModelledPowerStayingPastItsOwnersDeathSaysSo()
    {
        var staying = HookReader.Powers(CoverageLedger.ModelTypes())
            .Where(type => type.GetMethod("ShouldPowerBeRemovedAfterOwnerDeath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null)
            .Select(type => type.Name).ToList();
        Assert.NotEmpty(staying); // the scan finds them (Minion's at least)
        // SimModel's hooks: its virtual methods but the outcome tab and the staying question itself (properties aside).
        var hooks = typeof(SimModel).GetMethods(BindingFlags.Instance | BindingFlags.Public).Where(method => method.IsVirtual && !method.IsSpecialName
            && method.DeclaringType == typeof(SimModel) && method.Name is not (nameof(SimModel.OutcomeTab) or nameof(SimModel.StaysOnDeadOwner))).Select(method => method.Name).ToList();
        Assert.DoesNotContain(staying, name => SimModels.Get(name) is { } model && hooks.Any(model.Overrides) && !model.StaysOnDeadOwner(new SimState(), Who.YourOsty));
    }
}
