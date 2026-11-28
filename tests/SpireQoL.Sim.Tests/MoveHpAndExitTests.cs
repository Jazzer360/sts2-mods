using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A monster's move's HP, deaths, escapes and powers taken away, read from its code (decision #417; decompile v0.114.0: CreatureCmd.Heal,
/// CreatureCmd.SetMaxHp / SetMaxAndCurrentHp, CreatureCmd.Kill, CreatureCmd.Escape, PowerCmd.Remove): each runs as its step does in the enemy turn, so
/// what comes after it in the turn (a later enemy's hit, the fight ending, a hit reading the power) sees the board the game has then.
/// </summary>
public class MoveHpAndExitTests
{
    private static SimState Fight(params SimEnemy[] enemies)
    {
        var state = new SimState { TurnNumber = 1 };
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        state.Enemies.AddRange(enemies);
        return state;
    }

    private static SimEnemy Mover(int id, int hp, int? maxHp, params SimAction[] move) => new() { Id = id, Name = $"Mover{id}", Hp = hp, MaxHp = maxHp, Move = move };

    private static SimEnemy Biter(int id, int hit) => new() { Id = id, Name = $"Biter{id}", Hp = 30, Hits = new() { hit } };

    [Fact]
    public void AHealIsCappedAtTheEnemysMaxHp()
    {
        // Knowledge Demon's Ponder heals 30: from 50 of 70, to 70.
        var branch = Assert.Single(SimEngine.Run(Fight(Mover(id: 0, hp: 50, maxHp: 70, new MoveHealAction(Amount: 30, To: new[] { 0 })))).Branches);
        Assert.Null(branch.Unmodelled);
        Assert.Equal(70, branch.Enemy(id: 0)!.Hp);
        var partial = Assert.Single(SimEngine.Run(Fight(Mover(id: 0, hp: 20, maxHp: 70, new MoveHealAction(Amount: 30, To: new[] { 0 })))).Branches);
        Assert.Equal(50, partial.Enemy(id: 0)!.Hp);
    }

    [Fact]
    public void AHealOfAnEnemyWhoseMaxHpIsntReadIsUnknown()
    {
        var outcome = SimEngine.Run(Fight(Mover(id: 0, hp: 50, maxHp: null, new MoveHealAction(Amount: 30, To: new[] { 0 }))));
        Assert.True(outcome.AnyUnmodelled);
    }

    [Fact]
    public void SettingMaxHpLowersTheHpAboveIt()
    {
        var branch = Assert.Single(SimEngine.Run(Fight(Mover(id: 0, hp: 60, maxHp: 70, new MoveSetHpAction(Amount: 40, To: new[] { 0 }, Current: false)))).Branches);
        Assert.Equal((40, 40), (branch.Enemy(id: 0)!.MaxHp!.Value, branch.Enemy(id: 0)!.Hp));
    }

    [Fact]
    public void SettingMaxAndCurrentHpSetsBoth()
    {
        var branch = Assert.Single(SimEngine.Run(Fight(Mover(id: 0, hp: 10, maxHp: 20, new MoveSetHpAction(Amount: 45, To: new[] { 0 }, Current: true)))).Branches);
        Assert.Null(branch.Unmodelled);
        Assert.Equal((45, 45), (branch.Enemy(id: 0)!.MaxHp!.Value, branch.Enemy(id: 0)!.Hp));
    }

    [Fact]
    public void AMonsterThatKillsItselfIsDeadForTheRestOfTheTurn()
    {
        // Gas Bomb explodes and dies: the enemy after it still attacks, and the bomb is dead with its death handled.
        var branch = Assert.Single(SimEngine.Run(Fight(Mover(id: 0, hp: 10, maxHp: 10, new MoveKillAction(To: new[] { 0 })), Biter(id: 1, hit: 5))).Branches);
        Assert.False(branch.Enemy(id: 0)!.Alive);
        Assert.True(branch.Enemy(id: 0)!.DeathHandled);
        Assert.Equal(50 - 5, branch.You.Hp);
    }

    [Fact]
    public void TheLastMonsterKillingItselfEndsTheFight()
    {
        // Waterfall Giant's Explode: its attack, then its own death; with no one else left, the fight is over.
        var giant = Mover(id: 0, hp: 10, maxHp: 10, new MoveAttackAction(FirstHit: 0, Hits: 1), new MoveKillAction(To: new[] { 0 }));
        giant.Hits = new() { 7 };
        var branch = Assert.Single(SimEngine.Run(Fight(giant)).Branches);
        Assert.False(branch.Enemy(id: 0)!.Alive);
        Assert.Equal(50 - 7, branch.You.Hp);
    }

    [Fact]
    public void AnEscapedMonsterLeavesWithItsPowersAndNoDeath()
    {
        // Fat Gremlin flees: gone from the fight with its powers, no death's hooks.
        var gremlin = Mover(id: 0, hp: 15, maxHp: 15, new MoveEscapeAction(To: new[] { 0 }));
        gremlin.Powers = gremlin.Powers.With(PowerId.Strength, amount: 2);
        var branch = Assert.Single(SimEngine.Run(Fight(gremlin, Biter(id: 1, hit: 4))).Branches);
        var gone = branch.Enemy(id: 0)!;
        Assert.Equal((false, true, 0), (gone.Alive, gone.Escaped, gone.Powers[PowerId.Strength]));
        Assert.Equal(50 - 4, branch.You.Hp);
    }

    [Fact]
    public void APowerTakenAwayBeforeAnAttackIsntInIt()
    {
        // A power off the monster before its hit lands (the hit worked out through the damage pipeline as it lands): its Strength gone, the hit its base.
        var mover = Mover(id: 0, hp: 30, maxHp: 30, new MoveRemovePowerAction(PowerId.Strength, To: new[] { 0 }), new MoveAttackAction(FirstHit: 0, Hits: 1));
        mover.Powers = mover.Powers.With(PowerId.Strength, amount: 3);
        mover.Intent = new List<IntentHit> { new(Base: 6) };
        mover.Hits = new() { 9 };
        var branch = Assert.Single(SimEngine.Run(Fight(mover)).Branches);
        Assert.Equal(0, branch.Enemy(id: 0)!.Powers[PowerId.Strength]);
        Assert.Equal(50 - 6, branch.You.Hp);
    }
}
