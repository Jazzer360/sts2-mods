using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// What a read model is asked from outside a simulation is worked out once (decision #177): the board probe asks every power on every
/// creature on every frame whether its code keeps its applier (SimReaders.Read), and the adapter asks for its own fields each time it
/// reads the board or a card. Walking the model's code for each ask made about 9 KB of garbage a frame in a fight.
/// </summary>
public class ReadModelAskedOnceTests
{
    private static IEnumerable<ReadModel> Every() => SimModels.Names.Select(SimModels.Get).OfType<ReadModel>();

    [Fact]
    public void AskingAgainAllocatesNothing()
    {
        var models = Every().ToList();
        Assert.NotEmpty(models);
        var applierUsers = 0;
        foreach (var model in models)
        {
            applierUsers += model.UsesApplier ? 1 : 0;
            _ = model.OwnFields;
        }
        Assert.True(applierUsers > 0, "some read power keeps its applier (Strangle, Constrict)");

        // A stretch of asks, as the board probe makes them frame after frame: once each was worked out, none allocates. The runtime's own
        // one-off work on this thread can show in a stretch, so one stretch of three with nothing allocated passes.
        var allocated = new List<long>();
        for (var stretch = 0; stretch < 3 && !allocated.Contains(0); stretch++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var asked = 0;
            for (var frame = 0; frame < 100; frame++)
                foreach (var model in models)
                {
                    asked += model.UsesApplier ? 1 : 0;
                    asked += model.OwnFields.Count;
                }
            allocated.Add(GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.True(asked > 0);
        }
        Assert.Contains(0, allocated);
    }
}
