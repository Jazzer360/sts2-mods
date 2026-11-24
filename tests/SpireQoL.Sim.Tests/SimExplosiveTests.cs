using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Explosive (ExplosivePower.AfterDeath, decompile v0.113.1): when its owner dies, CreatureCmd.Damage hits every living
/// player for its amount (unpowered, so block takes it first), wherever the death happens: your card, your end of turn,
/// or its poison tick at the start of the enemy turn.
/// </summary>
public class SimExplosiveTests
{
    private static SimEnemy Enemy(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    private static SimPlay Strike(int target, int damage)
        => new() { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = target, HitDamage = { [target] = damage }, Hits = 1 };

    [Fact]
    public void KillingItDamagesYouThroughYourBlock()
    {
        // 4 block. Alive, it doesn't attack; the other enemy hits 8 (4 through). Killed: 6 explosion (4 blocked, 2 through),
        // then the 8 hit with no block left.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5), Enemy(id: 2, hp: 30, hits: 8) } };
        state.Enemies[0].Explosive = 6;
        state.You.Block = 4;
        Assert.Equal(4, SimEngine.Run(state).TakenMax);
        var outcome = SimEngine.Run(state, Strike(target: 1, damage: 5));
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.Equal(10, outcome.TakenMax);
        Assert.Equal(10, outcome.TakenMin);
    }

    [Fact]
    public void ItGoesOffOnce()
    {
        // Killed by the card, then the end of turn and the enemy turn check deaths again: still one explosion.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5) } };
        state.Enemies[0].Explosive = 6;
        Assert.Equal(6, SimEngine.Run(state, Strike(target: 1, damage: 5)).TakenMax);
    }

    [Fact]
    public void AnEndOfTurnKillMeetsTheBlockGainedBeforeIt()
    {
        // Plating's block comes at BeforeSideTurnEnd, before orb passives: the Lightning passive kills it and
        // the 6 block takes the 5 explosion.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 3) } };
        state.Enemies[0].Explosive = 5;
        state.You.Plating = 6;
        state.You.OrbCapacity = 3;
        state.You.Orbs.Add(new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 });
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.Equal(0, outcome.TakenMax);
    }

    [Fact]
    public void APoisonDeathAtTheEnemyTurnHitsYouButNotInferno()
    {
        // Poison kills it at the start of its turn: 5 to you (no block), and Inferno doesn't answer it (not your turn),
        // so the other enemy survives and attacks for 7.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 3), Enemy(id: 2, hp: 4, hits: 7) } };
        state.Enemies[0].Explosive = 5;
        state.Enemies[0].AddPoison(3);
        state.Enemies[0].PoisonStacks = 3;
        state.You.Inferno = 10;
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.False(outcome.DeadInAny(enemyId: 2));
        Assert.Equal(12, outcome.TakenMax);
    }

    [Fact]
    public void ItHitsTeammatesWithTheirOwnAmount()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5) } };
        state.Enemies[0].Explosive = 6;
        state.Enemies[0].ExplosiveOnTeammates = new List<int> { 9 };
        state.Teammates.Add(new SimPlayer { Hp = 40, Block = 2 });
        var outcome = SimEngine.Run(state, Strike(target: 1, damage: 5));
        Assert.Equal(6, outcome.TakenMax);
        Assert.All(outcome.Branches, branch => Assert.Equal(9, branch.Teammates[0].Incoming));
        Assert.All(outcome.Branches, branch => Assert.Equal(33, branch.Teammates[0].Hp));
    }

    [Fact]
    public void ADeadTeammateIsNotHit()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5) } };
        state.Enemies[0].Explosive = 6;
        state.Teammates.Add(new SimPlayer { Hp = 0 });
        var outcome = SimEngine.Run(state, Strike(target: 1, damage: 5));
        Assert.All(outcome.Branches, branch => Assert.Equal(0, branch.Teammates[0].Incoming));
    }
}
