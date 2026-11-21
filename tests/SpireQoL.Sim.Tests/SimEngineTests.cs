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
        var state = new SimState { Enemies = { Enemy(1, 3, 0, 10), Enemy(2, 3, 0, 10) } };
        state.You.Orbs = new() { Lightning(), Lightning() };
        state.You.OrbCapacity = 3;
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.DeadInAll(1));
        Assert.True(outcome.DeadInAll(2));
        Assert.Equal(0, outcome.TakenMax);
    }

    [Fact]
    public void OneLightningOrbWithTwoEnemiesIsNotAGuaranteedKill()
    {
        var state = new SimState { Enemies = { Enemy(1, 3, 0, 10), Enemy(2, 30, 0, 10) } };
        state.You.Orbs = new() { Lightning() };
        state.You.OrbCapacity = 3;
        var outcome = SimEngine.Run(state);
        Assert.False(outcome.DeadInAll(1));
        Assert.True(outcome.DeadInAny(1));
        Assert.Equal(20, outcome.TakenMax); // worst case: the bolt hits the big one, both attack
        Assert.Equal(10, outcome.TakenMin);
    }

    [Fact]
    public void StoneArmorWithJuggernautKillsAtEndOfTurn()
    {
        // Plating gives block at end of turn; each block gain sets off Juggernaut.
        var state = new SimState { Enemies = { Enemy(1, 8, 0, 12) } };
        state.You.Juggernaut = 8;
        var baseline = SimEngine.Run(state);
        Assert.False(baseline.DeadInAll(1));

        var stoneArmor = new SimPlay { Name = "Stone Armor", AddPlating = 4 };
        var outcome = SimEngine.Run(state, stoneArmor);
        Assert.True(outcome.DeadInAll(1));
        Assert.Equal(0, outcome.TakenMax);
    }

    [Fact]
    public void JuggernautCardWithOrichalcumAndNoBlockKills()
    {
        var state = new SimState { Enemies = { Enemy(1, 5, 0, 9) } };
        state.You.OrichalcumBlock = 6;
        var juggernaut = new SimPlay { Name = "Juggernaut", AddJuggernaut = 5 };
        var outcome = SimEngine.Run(state, juggernaut);
        Assert.True(outcome.DeadInAll(1));
        Assert.False(SimEngine.Run(state).DeadInAll(1));
    }

    [Fact]
    public void OrichalcumDoesNothingWhenYouHaveBlock()
    {
        var state = new SimState { Enemies = { Enemy(1, 5, 0, 9) } };
        state.You.OrichalcumBlock = 6;
        state.You.Block = 1;
        var outcome = SimEngine.Run(state, new SimPlay { AddJuggernaut = 5 });
        Assert.False(outcome.DeadInAll(1));
        Assert.Equal(8, outcome.TakenMax); // 9 - 1 block
    }

    [Fact]
    public void GlassPassiveLosesOnePerTrigger()
    {
        var glass = Glass(4);
        glass.Triggers = 2;
        var state = new SimState { Enemies = { Enemy(1, 7, 0, 5) } };
        state.You.Orbs = new() { glass };
        state.You.OrbCapacity = 3;
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.DeadInAll(1)); // 4 + 3 = 7
    }

    [Fact]
    public void PoisonKillsBeforeItAttacks()
    {
        var enemy = Enemy(1, 5, 0, 20);
        enemy.Poison = 5;
        var outcome = SimEngine.Run(new SimState { Enemies = { enemy } });
        Assert.Equal(0, outcome.TakenMax);
        Assert.Equal(1, outcome.Worst.SkippedPoisoned);
    }

    [Fact]
    public void ImbalancedFullBlockOnlyWhenTheCardsBlockCoversTheHit()
    {
        var enemy = Enemy(1, 40, 0, 8);
        enemy.Imbalanced = true;
        var state = new SimState { Enemies = { enemy } };
        state.You.Block = 5;
        Assert.False(SimEngine.Run(state).FirstHitBlockedInAll(1));
        var defend = new SimPlay { BlockAmount = 5, BlockGains = 1 };
        Assert.True(SimEngine.Run(state, defend).FirstHitBlockedInAll(1));
    }

    [Fact]
    public void ChannelIntoFullSlotsEvokesTheFrontOrb()
    {
        // Frost evoke gives block, which sets off Juggernaut.
        var state = new SimState { Enemies = { Enemy(1, 4, 0, 6) } };
        state.You.Orbs = new() { Frost(), Lightning(0, 0) };
        state.You.OrbCapacity = 2;
        state.You.Juggernaut = 4;
        var zap = new SimPlay { OrbOps = { new OrbOp { Op = OrbOp.Kind.Channel, Orb = Lightning(0, 0) } } };
        var outcome = SimEngine.Run(state, zap);
        Assert.True(outcome.DeadInAll(1));
    }

    [Fact]
    public void HardenedShellCapsHpLostPerTurn()
    {
        var enemy = Enemy(1, 10, 0, 5);
        enemy.ShellLeft = enemy.ShellPerTurn = 4;
        var strike = new SimPlay { HitTarget = SimPlay.Target.Single, TargetId = 1, Hits = 2, HitDamage = { [1] = 6 } };
        var outcome = SimEngine.Run(new SimState { Enemies = { enemy } }, strike);
        Assert.Equal(6, outcome.Worst.Enemy(1)!.Hp);
    }

    [Fact]
    public void HitCapAppliesToEverySource()
    {
        var enemy = Enemy(1, 10, 0, 5);
        enemy.HitCap = 1; // Intangible
        var state = new SimState { Enemies = { enemy } };
        state.You.Orbs = new() { Lightning(9, 9) };
        state.You.OrbCapacity = 3;
        Assert.Equal(9, SimEngine.Run(state).Worst.Enemy(1)!.Hp);
    }

    [Fact]
    public void UnblockedInstancesCountForWaking()
    {
        // Strike strips block, then the end-of-turn bolt gets through: one unblocked instance.
        var enemy = Enemy(1, 50, 10, 0);
        var state = new SimState { Enemies = { enemy } };
        state.You.Orbs = new() { Lightning(4, 8) };
        state.You.OrbCapacity = 3;
        Assert.Equal(0, SimEngine.Run(state).UnblockedMax(1));
        var strike = new SimPlay { HitTarget = SimPlay.Target.Single, TargetId = 1, Hits = 1, HitDamage = { [1] = 8 } };
        Assert.Equal(1, SimEngine.Run(state, strike).UnblockedMin(1));
    }

    [Fact]
    public void RetaliationCancelsLaterHits()
    {
        var enemy = Enemy(1, 3, 0, 5, 5, 5);
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
        var state = new SimState { Enemies = { Enemy(1, 100), Enemy(2, 100), Enemy(3, 100) } };
        state.You.Orbs = Enumerable.Range(0, 8).Select(_ => Lightning(1, 1)).ToList();
        state.You.OrbCapacity = 10;
        var outcome = SimEngine.Run(state);
        Assert.False(outcome.Truncated);
        Assert.True(outcome.Branches.Count <= 45); // distinct ways to split 8 hits among 3
    }
}
