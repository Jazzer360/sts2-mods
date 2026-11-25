using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Explosive (ExplosivePower.AfterDeath, read from its code): when its owner dies, CreatureCmd.Damage hits every living player creature for its
/// amount (Unpowered | SkipHurtAnim, no dealer: the damage math, then block takes it first), wherever the death happens: your card, your end of
/// turn (before the next step), or its poison tick at the start of the enemy turn.
/// </summary>
public class SimExplosiveTests
{
    private static SimEnemy Enemy(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    private static void Explosive(SimEnemy enemy, int amount) => enemy.Powers = enemy.Powers.With(PowerId.Explosive, amount);

    /// <summary>An enemy that doesn't attack and isn't hit, so the explosive one's death doesn't end the fight (decision #301).</summary>
    private static SimEnemy Bystander() => Enemy(id: 9, hp: 100);

    [Fact]
    public void TheLastEnemysExplosionHitsNoOne()
    {
        // Its death ends the fight: CreatureCmd.Damage returns at its start once CombatManager.IsOverOrEnding (decision #301).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5) } };
        Explosive(state.Enemies[0], amount: 6);
        var outcome = SimEngine.Run(state, Strike(target: 1, damage: 5));
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.Equal(0, outcome.TakenMax);
    }

    private static SimPlay Strike(int target, int damage)
        => new() { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = target, HitDamage = { [target] = damage }, Hits = 1 };

    [Fact]
    public void KillingItDamagesYouThroughYourBlock()
    {
        // 4 block. Alive, it doesn't attack; the other enemy hits 8 (4 through). Killed: 6 explosion (4 blocked, 2 through),
        // then the 8 hit with no block left.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5), Enemy(id: 2, hp: 30, hits: 8) } };
        Explosive(state.Enemies[0], amount: 6);
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
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5), Bystander() } };
        Explosive(state.Enemies[0], amount: 6);
        Assert.Equal(6, SimEngine.Run(state, Strike(target: 1, damage: 5)).TakenMax);
    }

    [Fact]
    public void AnEndOfTurnKillMeetsTheBlockGainedBeforeIt()
    {
        // Plating's block comes at BeforeSideTurnEnd, before orb passives: the Lightning passive kills it and
        // the 6 block takes the 5 explosion.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 3) } };
        Explosive(state.Enemies[0], amount: 5);
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
        Explosive(state.Enemies[0], amount: 5);
        state.Enemies[0].AddPoison(3);
        state.Enemies[0].PoisonStacks = 3;
        state.You.Inferno = 10;
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.False(outcome.DeadInAny(enemyId: 2));
        Assert.Equal(12, outcome.TakenMax);
    }

    [Fact]
    public void ItHitsEveryLivingPlayerThroughTheirOwnBlock()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5), Bystander() } };
        Explosive(state.Enemies[0], amount: 6);
        state.Teammates.Add(new SimPlayer { Hp = 40, Block = 2 });
        var outcome = SimEngine.Run(state, Strike(target: 1, damage: 5));
        Assert.Equal(6, outcome.TakenMax);
        Assert.All(outcome.Branches, branch => Assert.Equal(6, branch.Teammates[0].Incoming));
        Assert.All(outcome.Branches, branch => Assert.Equal(36, branch.Teammates[0].Hp));
    }

    [Fact]
    public void ADeadTeammateIsNotHit()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5), Bystander() } };
        Explosive(state.Enemies[0], amount: 6);
        state.Teammates.Add(new SimPlayer { Hp = 0 });
        var outcome = SimEngine.Run(state, Strike(target: 1, damage: 5));
        Assert.All(outcome.Branches, branch => Assert.Equal(0, branch.Teammates[0].Incoming));
    }

    [Fact]
    public void ADamageCapOnAPlayerCapsItBeforeTheirBlock()
    {
        // CreatureCmd.Damage: Hook.ModifyDamage (Intangible's ModifyDamageCap: 1) before Creature.DamageBlockInternal, so 1 of your 5 block goes
        // and you lose nothing; the hand-coded rule took 10 against your block (all 5 gone) and capped only the HP lost.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5), Bystander() } };
        Explosive(state.Enemies[0], amount: 10);
        state.You.Block = 5;
        state.You.Powers = PowerSet.Empty.With(PowerId.Intangible, amount: 1);
        var branch = Assert.Single(SimEngine.Run(state, Strike(target: 1, damage: 5)).Branches);
        Assert.Equal(0, branch.Taken);
        Assert.Equal(4, branch.BlockAtEnemyTurn);
    }
}
