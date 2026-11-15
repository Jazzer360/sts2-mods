using SpireQoL.Sim;
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
        var s = new SimState { Enemies = { Enemy(1, 5), Enemy(2, 30, 8) } };
        s.Enemies[0].Explosive = 6;
        s.P.Block = 4;
        Assert.Equal(4, SimEngine.Run(s).TakenMax);
        var o = SimEngine.Run(s, Strike(1, 5));
        Assert.True(o.DeadInAll(1));
        Assert.Equal(10, o.TakenMax);
        Assert.Equal(10, o.TakenMin);
    }

    [Fact]
    public void ItGoesOffOnce()
    {
        // Killed by the card, then the end of turn and the enemy turn check deaths again: still one explosion.
        var s = new SimState { Enemies = { Enemy(1, 5) } };
        s.Enemies[0].Explosive = 6;
        Assert.Equal(6, SimEngine.Run(s, Strike(1, 5)).TakenMax);
    }

    [Fact]
    public void AnEndOfTurnKillMeetsTheBlockGainedBeforeIt()
    {
        // Plating's block comes at BeforeSideTurnEnd, before orb passives: the Lightning passive kills it and
        // the 6 block takes the 5 explosion.
        var s = new SimState { Enemies = { Enemy(1, 3) } };
        s.Enemies[0].Explosive = 5;
        s.P.Plating = 6;
        s.P.OrbCapacity = 3;
        s.P.Orbs.Add(new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 });
        var o = SimEngine.Run(s);
        Assert.True(o.DeadInAll(1));
        Assert.Equal(0, o.TakenMax);
    }

    [Fact]
    public void APoisonDeathAtTheEnemyTurnHitsYouButNotInferno()
    {
        // Poison kills it at the start of its turn: 5 to you (no block), and Inferno doesn't answer it (not your turn),
        // so the other enemy survives and attacks for 7.
        var s = new SimState { Enemies = { Enemy(1, 3), Enemy(2, 4, 7) } };
        s.Enemies[0].Explosive = 5;
        s.Enemies[0].Poison = 3;
        s.Enemies[0].PoisonStacks = 3;
        s.P.Inferno = 10;
        var o = SimEngine.Run(s);
        Assert.True(o.DeadInAll(1));
        Assert.False(o.DeadInAny(2));
        Assert.Equal(12, o.TakenMax);
    }

    [Fact]
    public void ItHitsTeammatesWithTheirOwnAmount()
    {
        var s = new SimState { Enemies = { Enemy(1, 5) } };
        s.Enemies[0].Explosive = 6;
        s.Enemies[0].ExplosiveOnTeammates = new List<int> { 9 };
        s.Teammates.Add(new SimPlayer { Hp = 40, Block = 2 });
        var o = SimEngine.Run(s, Strike(1, 5));
        Assert.Equal(6, o.TakenMax);
        Assert.All(o.Branches, b => Assert.Equal(9, b.Teammates[0].Incoming));
        Assert.All(o.Branches, b => Assert.Equal(33, b.Teammates[0].Hp));
    }

    [Fact]
    public void ADeadTeammateIsNotHit()
    {
        var s = new SimState { Enemies = { Enemy(1, 5) } };
        s.Enemies[0].Explosive = 6;
        s.Teammates.Add(new SimPlayer { Hp = 0 });
        var o = SimEngine.Run(s, Strike(1, 5));
        Assert.All(o.Branches, b => Assert.Equal(0, b.Teammates[0].Incoming));
    }
}
