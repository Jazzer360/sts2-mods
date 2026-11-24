using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The failure path of the models read from code (decision #92, #195): a class whose hooks read whole at the reviewed builds and no longer do
/// (a game update) has no model, and every simulation it's in is unknown with why (SimState.Unmodelled), so nothing shown counts it as
/// exact: on the board from the start, or landing during the simulation.
/// </summary>
public class ReadModelFailureTests
{
    private static readonly PowerId Unreadable = new(ReadModelsFile.Unreadable.Name);

    private static SimState Board()
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 30, Hits = new List<int> { 6 } } } };
        state.You.Hp = 40;
        state.You.HandCount = 3;
        return state;
    }

    private static SimPlay Strike() => new() { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 1, HitDamage = { [1] = 6 }, Hits = 1, HandCard = 1 };

    [Fact]
    public void AnUnreadClassHasNoModelAndSaysWhy()
    {
        Assert.Null(SimModels.Get(Unreadable.Name));
        Assert.Equal(ReadModelsFile.Unreadable.Unread, SimModels.UnreadReason(Unreadable.Name));
        Assert.Null(SimModels.UnreadReason(PowerId.Strength.Name));
    }

    [Theory]
    [InlineData(Who.You)]
    [InlineData(1)]
    public void ASimulationWithItOnTheBoardIsUnknown(int owner)
    {
        var state = Board();
        SimEngine.SetPowers(state, owner, SimEngine.PowersOf(state, owner).With(Unreadable, amount: 2));
        var outcome = SimEngine.Run(state, Strike());
        Assert.True(outcome.AnyUnmodelled);
        Assert.Equal(new[] { ReadModelsFile.Unreadable.Unread! }, outcome.UnmodelledReasons);
        Assert.False(outcome.YouDieChance().Exact);
    }

    [Fact]
    public void ASimulationItLandsInIsUnknown()
    {
        var play = Strike();
        play.Body.Add(new ApplyPowerAction(Unreadable, Amount: 1) { To = ApplyPowerAction.Where.Target });
        var outcome = SimEngine.Run(Board(), play);
        Assert.True(outcome.AnyUnmodelled);
        Assert.Contains(ReadModelsFile.Unreadable.Unread!, outcome.UnmodelledReasons);
    }

    [Fact]
    public void WithoutItTheSameSimulationIsExact()
    {
        var outcome = SimEngine.Run(Board(), Strike());
        Assert.False(outcome.AnyUnmodelled);
        Assert.True(outcome.YouDieChance().Exact);
    }
}
