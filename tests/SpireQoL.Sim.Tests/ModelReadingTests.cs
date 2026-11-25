using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The models read on a worker (SimModels.StartReading, the game at the mod's load): until the read is over they aren't loaded and a wait for
/// them gives up at its budget (the frame's: SimWait.Models, so its predictions are pending); once it's over they are, and every model asked
/// for is the read's.
/// </summary>
public class ModelReadingTests
{
    [Fact]
    public void TheModelsAreLoadedOnceTheReadOnAWorkerIsOver()
    {
        var read = SimModels.Read!;
        var readRules = SimModels.ReadRules!;
        var readTurnEndGaps = SimModels.ReadTurnEndGaps;
        var readTurnStartGaps = SimModels.ReadTurnStartGaps;
        using var gate = new ManualResetEventSlim();
        var reader = 0;
        SimModels.StartReading(() =>
        {
            reader = Environment.CurrentManagedThreadId;
            gate.Wait(TimeSpan.FromSeconds(30));
            return read();
        }, readRules, readTurnEndGaps, readTurnStartGaps);
        try
        {
            Assert.False(SimModels.Loaded);
            Assert.False(SimModels.WaitLoaded(milliseconds: 5));
            Assert.False(SimModels.WaitLoaded(milliseconds: 0));
        }
        finally { gate.Set(); }
        Assert.True(SimModels.WaitLoaded(milliseconds: 30_000));
        Assert.True(SimModels.Loaded);
        Assert.NotEqual(Environment.CurrentManagedThreadId, reader);
        Assert.IsType<ReadModel>(SimModels.Get(PowerId.Strength.Name));
    }
}
