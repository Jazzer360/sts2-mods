using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A power that counts its own amount down to a payout (decision #254): The Bomb (TheBombPower.BeforeSideTurnEnd: Decrement while Amount > 1,
/// then its Damage to every enemy and Remove at Amount &lt;= 1). Its last tick is read from its code (ReadModel.CountdownAt), so its Relic Counters
/// row says how many turns are left and what it deals, as the simulation plays it.
/// </summary>
public class CountdownTests
{
    private static ReadModel Bomb => Assert.IsType<ReadModel>(SimModels.Get(PowerId.TheBomb.Name));

    [Fact]
    public void TheBombsLastTickIsReadFromItsCode()
    {
        Assert.Equal(1, Bomb.CountdownAt);
        // A model with no countdown has none.
        Assert.Null(Assert.IsType<ReadModel>(SimModels.Get(PowerId.Hailstorm.Name)).CountdownAt);
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(2, 2)]
    [InlineData(1, 1)]
    public void TheTurnsLeftAreTheTurnsItsCodeTakesToExplode(int amount, int turns)
    {
        Assert.Equal(turns, PowerCountdown.TurnsLeft(amount, Bomb.CountdownAt!.Value));
        // As the simulation plays it: it explodes at the end of the turn its count reaches the last tick, never before.
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 100, Hits = { 1 } } } };
        state.You.Powers = state.You.Powers.With(PowerId.TheBomb, amount: amount);
        var hp = Assert.Single(SimEngine.Run(state).Branches).Enemy(id: 1)!.Hp;
        Assert.Equal(turns == 1 ? 60 : 100, hp);
    }

    [Fact]
    public void TheRowSaysTheTurnsLeftAndTheDamage()
    {
        Assert.Equal("40 to all in 3 turns", PowerCountdown.Label(turnsLeft: 3, damage: 40));
        Assert.Equal("50 to all this turn", PowerCountdown.Label(turnsLeft: 1, damage: 50));
    }
}
