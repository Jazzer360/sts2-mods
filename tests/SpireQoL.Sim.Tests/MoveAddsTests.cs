using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// What an enemy's move adds to the fight, read from code (decision #381): Fogmog's Illusion move (CreatureCmd.Add&lt;EyeWithTeeth&gt;), The Obscura's
/// (CreatureCmd.Add&lt;Parafright&gt;). The creature joins as the step runs, with the HP it starts with and its AfterAddedToRoom's powers (Illusion, and
/// the Minion Illusion's AfterApplied gives it); the enemies that act were listed as the turn started (CombatManager.ExecuteEnemyTurn's
/// Enemies.ToList()), so it makes no move this turn, and it's in the fight for the rest of the turn (Hook.AfterCreatureAddedToCombat, the turn's end).
/// </summary>
public class MoveAddsTests
{
    private static SimSpawn Eye(params int[] hp) => new("Eye with Teeth", hp)
    {
        Monster = "EyeWithTeeth", Powers = PowerSet.Empty.With(PowerId.Illusion, amount: 1).With(PowerId.Minion, amount: 1), UniqueFromEarlier = true,
    };

    private static SimEnemy Fogmog(SimSpawn spawn) => new() { Id = 0, Name = "Fogmog", Monster = "Fogmog", Hp = 70, Move = new SimAction[] { new MoveSpawnAction(spawn) } };

    private static SimState Fight(params SimEnemy[] enemies)
    {
        var state = new SimState { TurnNumber = 1 };
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        state.Enemies.AddRange(enemies);
        return state;
    }

    [Fact]
    public void TheCreatureAMoveAddsJoinsWithItsPowers()
    {
        var biter = new SimEnemy { Id = 1, Name = "Biter", Hp = 30, Hits = new() { 5 } };
        var outcome = SimEngine.Run(Fight(Fogmog(Eye(6)), biter));
        var branch = Assert.Single(outcome.Branches);
        Assert.Null(branch.Unmodelled);
        var eye = branch.Enemy(id: 2)!;
        Assert.Equal(("Eye with Teeth", "EyeWithTeeth", 6, 0), (eye.Name, eye.Monster, eye.Hp, eye.AddedBy!.Value));
        Assert.Equal(1, eye.Powers[PowerId.Illusion]);
        Assert.Equal(1, eye.Powers[PowerId.Minion]);
        // The enemy after Fogmog still makes its move; the eye, listed after the turn started, makes none.
        Assert.Equal(5, outcome.TakenMax);
    }

    [Fact]
    public void PhilosophersStoneGivesTheCreatureAMoveAddsStrength()
    {
        // Hook.AfterCreatureAddedToCombat as it joins, in the enemy turn: Philosopher's Stone (read from its code) gives it Strength.
        Assert.IsType<ReadModel>(SimModels.Get(RelicId.PhilosophersStone.Name));
        var fogmog = Fogmog(Eye(6));
        fogmog.Powers = fogmog.Powers.With(PowerId.Strength, amount: 1); // the board's enemies have it already, as the adapter reads them
        var state = Fight(fogmog);
        state.You.Relics = state.You.Relics.With(RelicId.PhilosophersStone, 0);
        var branch = Assert.Single(SimEngine.Run(state).Branches);
        Assert.Null(branch.Unmodelled);
        Assert.Equal(1, branch.Enemy(id: 1)!.Powers[PowerId.Strength]);
    }

    [Fact]
    public void AnHpItCouldStartWithAmongSeveralLeavesTheTurnUnknown()
    {
        // The enemy turn runs on one state: a roll of its HP isn't branched on there, so what follows is unknown, never a pick.
        var outcome = SimEngine.Run(Fight(Fogmog(Eye(5, 6))));
        Assert.True(outcome.AnyUnmodelled);
        Assert.Single(Assert.Single(outcome.Branches).Enemies);
    }

    [Fact]
    public void ADeadSummonerAddsNothing()
    {
        // Killed before the enemy turn: it makes no move, so nothing joins.
        var fogmog = Fogmog(Eye(6));
        fogmog.Hp = 0;
        var branch = Assert.Single(SimEngine.Run(Fight(fogmog)).Branches);
        Assert.Single(branch.Enemies);
    }
}
