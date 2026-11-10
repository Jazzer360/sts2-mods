using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

public class SimEngineTests
{
    private static SimEnemy Enemy(int id, int hp, int block = 0, params int[] hits)
        => new() { Id = id, Name = $"E{id}", Hp = hp, Block = block, Hits = hits.ToList() };

    private static SimOrb Lightning(int passive = 3, int evoke = 8) => new() { Kind = OrbKind.Lightning, Passive = passive, Evoke = evoke };
    private static SimOrb Frost(int passive = 2, int evoke = 5) => new() { Kind = OrbKind.Frost, Passive = passive, Evoke = evoke };
    private static SimOrb Glass(int passive = 4) => new() { Kind = OrbKind.Glass, Passive = passive, Evoke = passive * 2 };

    [Fact]
    public void TwoLightningOrbsKillBothThreeHpEnemiesWhateverTheTargets()
    {
        // Whichever enemy the first bolt hits dies, so the second bolt can only hit the other.
        var s = new SimState { Enemies = { Enemy(1, 3, 0, 10), Enemy(2, 3, 0, 10) } };
        s.P.Orbs = new() { Lightning(), Lightning() };
        s.P.OrbCapacity = 3;
        var o = SimEngine.Run(s);
        Assert.True(o.DeadInAll(1));
        Assert.True(o.DeadInAll(2));
        Assert.Equal(0, o.TakenMax);
    }

    [Fact]
    public void OneLightningOrbWithTwoEnemiesIsNotAGuaranteedKill()
    {
        var s = new SimState { Enemies = { Enemy(1, 3, 0, 10), Enemy(2, 30, 0, 10) } };
        s.P.Orbs = new() { Lightning() };
        s.P.OrbCapacity = 3;
        var o = SimEngine.Run(s);
        Assert.False(o.DeadInAll(1));
        Assert.True(o.DeadInAny(1));
        Assert.Equal(20, o.TakenMax); // worst case: the bolt hits the big one, both attack
        Assert.Equal(10, o.TakenMin);
    }

    [Fact]
    public void StoneArmorWithJuggernautKillsAtEndOfTurn()
    {
        // Plating gives block at end of turn; each block gain sets off Juggernaut.
        var s = new SimState { Enemies = { Enemy(1, 8, 0, 12) } };
        s.P.Juggernaut = 8;
        var baseline = SimEngine.Run(s);
        Assert.False(baseline.DeadInAll(1));

        var stoneArmor = new SimPlay { Name = "Stone Armor", AddPlating = 4 };
        var o = SimEngine.Run(s, stoneArmor);
        Assert.True(o.DeadInAll(1));
        Assert.Equal(0, o.TakenMax);
    }

    [Fact]
    public void JuggernautCardWithOrichalcumAndNoBlockKills()
    {
        var s = new SimState { Enemies = { Enemy(1, 5, 0, 9) } };
        s.P.OrichalcumBlock = 6;
        var juggernaut = new SimPlay { Name = "Juggernaut", AddJuggernaut = 5 };
        var o = SimEngine.Run(s, juggernaut);
        Assert.True(o.DeadInAll(1));
        Assert.False(SimEngine.Run(s).DeadInAll(1));
    }

    [Fact]
    public void OrichalcumDoesNothingWhenYouHaveBlock()
    {
        var s = new SimState { Enemies = { Enemy(1, 5, 0, 9) } };
        s.P.OrichalcumBlock = 6;
        s.P.Block = 1;
        var o = SimEngine.Run(s, new SimPlay { AddJuggernaut = 5 });
        Assert.False(o.DeadInAll(1));
        Assert.Equal(8, o.TakenMax); // 9 - 1 block
    }

    [Fact]
    public void GlassPassiveLosesOnePerTrigger()
    {
        var glass = Glass(4);
        glass.Triggers = 2;
        var s = new SimState { Enemies = { Enemy(1, 7, 0, 5) } };
        s.P.Orbs = new() { glass };
        s.P.OrbCapacity = 3;
        var o = SimEngine.Run(s);
        Assert.True(o.DeadInAll(1)); // 4 + 3 = 7
    }

    [Fact]
    public void PoisonKillsBeforeItAttacks()
    {
        var e = Enemy(1, 5, 0, 20);
        e.Poison = 5;
        var o = SimEngine.Run(new SimState { Enemies = { e } });
        Assert.Equal(0, o.TakenMax);
        Assert.Equal(1, o.Worst.SkippedPoisoned);
    }

    [Fact]
    public void ImbalancedFullBlockOnlyWhenTheCardsBlockCoversTheHit()
    {
        var e = Enemy(1, 40, 0, 8);
        e.Imbalanced = true;
        var s = new SimState { Enemies = { e } };
        s.P.Block = 5;
        Assert.False(SimEngine.Run(s).FirstHitBlockedInAll(1));
        var defend = new SimPlay { BlockAmount = 5, BlockGains = 1 };
        Assert.True(SimEngine.Run(s, defend).FirstHitBlockedInAll(1));
    }

    [Fact]
    public void ChannelIntoFullSlotsEvokesTheFrontOrb()
    {
        // Frost evoke gives block, which sets off Juggernaut.
        var s = new SimState { Enemies = { Enemy(1, 4, 0, 6) } };
        s.P.Orbs = new() { Frost(), Lightning(0, 0) };
        s.P.OrbCapacity = 2;
        s.P.Juggernaut = 4;
        var zap = new SimPlay { OrbOps = { new OrbOp { Op = OrbOp.Kind.Channel, Orb = Lightning(0, 0) } } };
        var o = SimEngine.Run(s, zap);
        Assert.True(o.DeadInAll(1));
    }

    [Fact]
    public void HardenedShellCapsHpLostPerTurn()
    {
        var e = Enemy(1, 10, 0, 5);
        e.ShellLeft = e.ShellPerTurn = 4;
        var strike = new SimPlay { HitTarget = SimPlay.Target.Single, TargetId = 1, Hits = 2, HitDamage = { [1] = 6 } };
        var o = SimEngine.Run(new SimState { Enemies = { e } }, strike);
        Assert.Equal(6, o.Worst.Enemy(1)!.Hp);
    }

    [Fact]
    public void HitCapAppliesToEverySource()
    {
        var e = Enemy(1, 10, 0, 5);
        e.HitCap = 1; // Intangible
        var s = new SimState { Enemies = { e } };
        s.P.Orbs = new() { Lightning(9, 9) };
        s.P.OrbCapacity = 3;
        Assert.Equal(9, SimEngine.Run(s).Worst.Enemy(1)!.Hp);
    }

    [Fact]
    public void UnblockedInstancesCountForWaking()
    {
        // Strike strips block, then the end-of-turn bolt gets through: one unblocked instance.
        var e = Enemy(1, 50, 10, 0);
        var s = new SimState { Enemies = { e } };
        s.P.Orbs = new() { Lightning(4, 8) };
        s.P.OrbCapacity = 3;
        Assert.Equal(0, SimEngine.Run(s).UnblockedMax(1));
        var strike = new SimPlay { HitTarget = SimPlay.Target.Single, TargetId = 1, Hits = 1, HitDamage = { [1] = 8 } };
        Assert.Equal(1, SimEngine.Run(s, strike).UnblockedMin(1));
    }

    [Fact]
    public void RetaliationCancelsLaterHits()
    {
        var e = Enemy(1, 3, 0, 5, 5, 5);
        var s = new SimState { Enemies = { e } };
        s.P.FlameBarrier = 3;
        var o = SimEngine.Run(s);
        Assert.Equal(5, o.TakenMax); // first hit lands, Flame Barrier kills it, the rest are cancelled
        Assert.Equal(2, o.Worst.CancelledHits);
    }

    [Fact]
    public void BranchesThatEndTheSameAreMerged()
    {
        // Eight random bolts on three identical enemies would be 3^8 branches without merging.
        var s = new SimState { Enemies = { Enemy(1, 100), Enemy(2, 100), Enemy(3, 100) } };
        s.P.Orbs = Enumerable.Range(0, 8).Select(_ => Lightning(1, 1)).ToList();
        s.P.OrbCapacity = 10;
        var o = SimEngine.Run(s);
        Assert.False(o.Truncated);
        Assert.True(o.Branches.Count <= 45); // distinct ways to split 8 hits among 3
    }
}
