using Xunit;

namespace SpireQoL.Sim.Tests;

public class SimEngineTests
{
    private static SimEnemy Enemy(int id, int hp, int block = 0, params int[] hits)
        => new() { Id = id, Name = $"E{id}", Hp = hp, Block = block, Hits = hits.ToList() };

    private static SimOrb Lightning(int passive = 3, int evoke = 8) => new() { Kind = OrbKind.Lightning, Passive = passive, Evoke = evoke };
    private static SimOrb Frost(int passive = 2, int evoke = 5) => new() { Kind = OrbKind.Frost, Passive = passive, Evoke = evoke };
    private static SimOrb Glass(int passive = 4, int focus = 0) => new() { Kind = OrbKind.Glass, Passive = passive, Evoke = passive * 2, GlassBase = passive - focus, GlassBonus = focus };

    [Fact]
    public void TwoLightningOrbsKillBothThreeHpEnemiesWhateverTheTargets()
    {
        // Whichever enemy the first bolt hits dies, so the second bolt can only hit the other.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 3, block: 0, hits: 10), Enemy(id: 2, hp: 3, block: 0, hits: 10) } };
        state.You.Orbs = new() { Lightning(), Lightning() };
        state.You.OrbCapacity = 3;
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.True(outcome.DeadInAll(enemyId: 2));
        Assert.Equal(0, outcome.TakenMax);
    }

    [Fact]
    public void OneLightningOrbWithTwoEnemiesIsNotAGuaranteedKill()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 3, block: 0, hits: 10), Enemy(id: 2, hp: 30, block: 0, hits: 10) } };
        state.You.Orbs = new() { Lightning() };
        state.You.OrbCapacity = 3;
        var outcome = SimEngine.Run(state);
        Assert.False(outcome.DeadInAll(enemyId: 1));
        Assert.True(outcome.DeadInAny(enemyId: 1));
        Assert.Equal(20, outcome.TakenMax); // worst case: the bolt hits the big one, both attack
        Assert.Equal(10, outcome.TakenMin);
    }

    [Fact]
    public void StoneArmorWithJuggernautKillsAtEndOfTurn()
    {
        // Plating gives block at end of turn; each block gain sets off Juggernaut.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 8, block: 0, hits: 12) } };
        state.You.Juggernaut = 8;
        var baseline = SimEngine.Run(state);
        Assert.False(baseline.DeadInAll(enemyId: 1));

        var stoneArmor = new SimPlay { Name = "Stone Armor", AddPlating = 4 };
        var outcome = SimEngine.Run(state, stoneArmor);
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.Equal(0, outcome.TakenMax);
    }

    [Fact]
    public void JuggernautCardWithOrichalcumAndNoBlockKills()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5, block: 0, hits: 9) } };
        state.WithRelic(Who.You, RelicId.Orichalcum, "Block", value: 6);
        var juggernaut = new SimPlay { Name = "Juggernaut", AddJuggernaut = 5 };
        var outcome = SimEngine.Run(state, juggernaut);
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.False(SimEngine.Run(state).DeadInAll(enemyId: 1));
    }

    [Fact]
    public void OrichalcumDoesNothingWhenYouHaveBlock()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5, block: 0, hits: 9) } };
        state.WithRelic(Who.You, RelicId.Orichalcum, "Block", value: 6);
        state.You.Block = 1;
        var outcome = SimEngine.Run(state, new SimPlay { AddJuggernaut = 5 });
        Assert.False(outcome.DeadInAll(enemyId: 1));
        Assert.Equal(8, outcome.TakenMax); // 9 - 1 block
    }

    [Fact]
    public void GlassPassiveLosesOnePerTrigger()
    {
        var glass = Glass(passive: 4);
        glass.Triggers = 2;
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 7, block: 0, hits: 5) } };
        state.You.Orbs = new() { glass };
        state.You.OrbCapacity = 3;
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.DeadInAll(enemyId: 1)); // 4 + 3 = 7
    }

    [Fact]
    public void PoisonKillsBeforeItAttacks()
    {
        var enemy = Enemy(id: 1, hp: 5, block: 0, hits: 20);
        enemy.Poison = 5;
        var outcome = SimEngine.Run(new SimState { Enemies = { enemy } });
        Assert.Equal(0, outcome.TakenMax);
        Assert.Equal(1, outcome.Worst.SkippedPoisoned);
    }

    [Fact]
    public void ImbalancedFullBlockOnlyWhenTheCardsBlockCoversTheHit()
    {
        var enemy = Enemy(id: 1, hp: 40, block: 0, hits: 8);
        enemy.Imbalanced = true;
        var state = new SimState { Enemies = { enemy } };
        state.You.Block = 5;
        Assert.False(SimEngine.Run(state).FirstHitBlockedInAll(enemyId: 1));
        var defend = new SimPlay { BlockAmount = 5, BlockGains = 1 };
        Assert.True(SimEngine.Run(state, defend).FirstHitBlockedInAll(enemyId: 1));
    }

    [Fact]
    public void ChannelIntoFullSlotsEvokesTheFrontOrb()
    {
        // Frost evoke gives block, which sets off Juggernaut.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 4, block: 0, hits: 6) } };
        state.You.Orbs = new() { Frost(), Lightning(passive: 0, evoke: 0) };
        state.You.OrbCapacity = 2;
        state.You.Juggernaut = 4;
        var zap = new SimPlay { OrbOps = { new OrbOp { Op = OrbOp.Kind.Channel, Orb = Lightning(passive: 0, evoke: 0) } } };
        var outcome = SimEngine.Run(state, zap);
        Assert.True(outcome.DeadInAll(enemyId: 1));
    }

    [Fact]
    public void HardenedShellCapsHpLostPerTurn()
    {
        var enemy = Enemy(id: 1, hp: 10, block: 0, hits: 5);
        enemy.ShellLeft = enemy.ShellPerTurn = 4;
        var strike = new SimPlay { HitTarget = SimPlay.Target.Single, TargetId = 1, Hits = 2, HitDamage = { [1] = 6 } };
        var outcome = SimEngine.Run(new SimState { Enemies = { enemy } }, strike);
        Assert.Equal(6, outcome.Worst.Enemy(id: 1)!.Hp);
    }

    [Fact]
    public void HitCapAppliesToEverySource()
    {
        var enemy = Enemy(id: 1, hp: 10, block: 0, hits: 5);
        enemy.HitCap = 1; // Intangible
        var state = new SimState { Enemies = { enemy } };
        state.You.Orbs = new() { Lightning(passive: 9, evoke: 9) };
        state.You.OrbCapacity = 3;
        Assert.Equal(9, SimEngine.Run(state).Worst.Enemy(id: 1)!.Hp);
    }

    [Fact]
    public void UnblockedInstancesCountForWaking()
    {
        // Strike strips block, then the end-of-turn bolt gets through: one unblocked instance.
        var enemy = Enemy(id: 1, hp: 50, block: 10, hits: 0);
        var state = new SimState { Enemies = { enemy } };
        state.You.Orbs = new() { Lightning(passive: 4, evoke: 8) };
        state.You.OrbCapacity = 3;
        Assert.Equal(0, SimEngine.Run(state).UnblockedMax(enemyId: 1));
        var strike = new SimPlay { HitTarget = SimPlay.Target.Single, TargetId = 1, Hits = 1, HitDamage = { [1] = 8 } };
        Assert.Equal(1, SimEngine.Run(state, strike).UnblockedMin(enemyId: 1));
    }

    [Fact]
    public void RetaliationCancelsLaterHits()
    {
        var enemy = Enemy(id: 1, hp: 3, block: 0, hits: [5, 5, 5]);
        var state = new SimState { Enemies = { enemy } };
        state.You.FlameBarrier = 3;
        var outcome = SimEngine.Run(state);
        Assert.Equal(5, outcome.TakenMax); // first hit lands, Flame Barrier kills it, the rest are cancelled
        Assert.Equal(2, outcome.Worst.CancelledHits);
    }

    [Fact]
    public void BranchesThatEndTheSameAreMerged()
    {
        // Eight random bolts on three identical enemies would be 3^8 branches without merging.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 100), Enemy(id: 2, hp: 100), Enemy(id: 3, hp: 100) } };
        state.You.Orbs = Enumerable.Range(0, 8).Select(_ => Lightning(passive: 1, evoke: 1)).ToList();
        state.You.OrbCapacity = 10;
        var outcome = SimEngine.Run(state);
        Assert.False(outcome.Truncated);
        Assert.True(outcome.Branches.Count <= 45); // distinct ways to split 8 hits among 3
    }
}
