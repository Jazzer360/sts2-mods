using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The made-up fight the load runs through the engine (decision #254, SimWarmUp): it runs the turn forecast and two plays' searches to an
/// outcome each, the same every time.
/// </summary>
public class SimWarmUpTests
{
    [Fact]
    public void TheMadeUpFightRunsToItsOutcomesTheSameEveryTime()
    {
        var outcomes = SimWarmUp.Run();
        Assert.True(outcomes >= 3, $"{outcomes} outcomes");
        Assert.Equal(outcomes, SimWarmUp.Run());
    }
}
