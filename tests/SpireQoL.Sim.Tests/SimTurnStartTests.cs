using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The enemies' side's turn start (CombatManager.StartTurn), the one turn start a simulation reaches: every listener's BeforeSideTurnStart,
/// then AfterSideTurnStart (then Late), its participants the living enemies, before any enemy moves. A read model's hook acts there as its
/// code says: `participants.Contains(base.Owner)` holds for an enemy owner, never for a player's (theirs is the players' next turn).
/// </summary>
public class SimTurnStartTests
{
    private static SimState Board()
    {
        var state = new SimState { You = new SimPlayer { Hp = 50, Block = 0 } };
        state.Enemies.Add(new SimEnemy { Id = 0, Name = "E0", Hp = 40, Intent = new List<IntentHit> { new(Base: 20m) } });
        return state;
    }

    [Fact]
    public void APowerThatGoesAsItsOwnersTurnStartsGoesOnAnEnemyBeforeItMoves()
    {
        // ConductorsBatonPower.AfterSideTurnStart: if (participants.Contains(base.Owner)) PowerCmd.Remove(this). On an enemy, it's gone as the
        // enemies' turn starts.
        var state = Board();
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.ConductorsBaton, amount: 1);
        var after = Assert.Single(SimEngine.Run(state).Branches);
        Assert.False(after.Enemies[0].Powers.Has(PowerId.ConductorsBaton));
    }

    [Fact]
    public void APlayersPowerThatGoesAsTheirTurnStartsLastsThroughTheEnemyTurn()
    {
        // The same power on you: you don't take part in the enemies' turn start, so it stays and cuts the enemy's powered attack (20 × 0.75).
        var state = Board();
        state.You.Powers = state.You.Powers.With(PowerId.ConductorsBaton, amount: 1);
        var outcome = SimEngine.Run(state);
        Assert.Equal(15, outcome.TakenMax);
        Assert.True(Assert.Single(outcome.Branches).You.Powers.Has(PowerId.ConductorsBaton));
    }

    [Fact]
    public void APlayersTurnStartStoreDoesNothingAsTheEnemiesTurnStarts()
    {
        // LullabyPower.BeforeSideTurnStart: if (participants.Contains(base.Owner)) ProccdThisTurn = false. Yours stays set through the enemy turn.
        var state = Board();
        state.You.Powers = state.You.Powers.With(PowerId.Lullaby, amount: 5);
        SimEngine.SetField(state, Who.You, key: "LullabyPower.ProccdThisTurn", value: 1);
        var after = Assert.Single(SimEngine.Run(state).Branches);
        Assert.Equal(1, after.You.Fields["LullabyPower.ProccdThisTurn"]);
    }
}
