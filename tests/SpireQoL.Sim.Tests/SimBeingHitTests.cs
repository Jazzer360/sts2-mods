using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Enemy powers that react to being hit (decompile v0.111.0): Burrowed, Flutter, Plow / Shriek,
/// The Gambit, Slippery; plus the play counts behind the Juggernaut, Sneaky and Hand Drill tabs.
/// </summary>
public class SimBeingHitTests
{
    private static SimEnemy Enemy(int id, int hp, int block = 0, params int[] hits)
        => new() { Id = id, Name = $"E{id}", Hp = hp, Block = block, Hits = hits.ToList() };

    private static SimPlay Strike(int target, int damage, int hits = 1)
        => new() { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = target, HitDamage = { [target] = damage }, Hits = hits };

    [Fact]
    public void BreakingBurrowedBlockStunsItSoItDoesntAttack()
    {
        // BurrowedPower.AfterBlockBroken: stunned, power and block removed. Its block stays between turns.
        var s = new SimState { Enemies = { Enemy(1, 40, 10, 15) } };
        s.Enemies[0].Burrowed = true;
        s.Enemies[0].Barricade = true;
        Assert.Equal(15, SimEngine.Run(s).TakenMax);
        Assert.Equal(15, SimEngine.Run(s, Strike(1, 6)).TakenMax); // block not broken
        var o = SimEngine.Run(s, Strike(1, 12));
        Assert.True(o.StunnedInAll(1));
        Assert.Equal(0, o.TakenMax);
        Assert.True(o.PlayBreaksBlockInEveryBranch);
    }

    [Fact]
    public void FlutterStunsAfterEnoughPoweredHitsGetThrough()
    {
        // FlutterPower.AfterDamageReceived: unblocked powered-attack hits decrement it; 0 stuns.
        var s = new SimState { Enemies = { Enemy(1, 40, 0, 10) } };
        s.Enemies[0].Flutter = 2;
        Assert.False(SimEngine.Run(s, Strike(1, 3)).StunnedInAll(1));
        Assert.True(SimEngine.Run(s, Strike(1, 3, hits: 2)).StunnedInAll(1));
        // Proc damage isn't a powered attack.
        var proc = new SimPlay { Name = "Defend" };
        proc.Procs.Add(new ProcHit { Spread = SimPlay.Target.Single, TargetId = 1, Damage = { [1] = 3 }, Times = 2 });
        Assert.False(SimEngine.Run(s, proc).StunnedInAll(1));
    }

    [Fact]
    public void PlowAndShriekStunAtTheirHpThresholdFromAnyDamage()
    {
        // PlowPower / ShriekPower: any damage through that leaves it at or below the amount.
        var s = new SimState { Enemies = { Enemy(1, 30, 0, 10) } };
        s.Enemies[0].StunAtHp = 20;
        Assert.False(SimEngine.Run(s, Strike(1, 9)).StunnedInAll(1));
        Assert.True(SimEngine.Run(s, Strike(1, 10)).StunnedInAll(1));
        // A Lightning orb's end-of-turn hit counts too, so the card isn't needed then.
        s.P.Orbs = new() { new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 } };
        s.P.OrbCapacity = 3;
        Assert.True(SimEngine.Run(s, Strike(1, 7)).StunnedInAll(1));
    }

    [Fact]
    public void TheGambitDiesToAPoweredHitThatGetsThrough()
    {
        var s = new SimState { Enemies = { Enemy(1, 60, 5, 20) } };
        s.Enemies[0].Gambit = true;
        Assert.False(SimEngine.Run(s, Strike(1, 5)).GambitInAll(1)); // blocked
        var o = SimEngine.Run(s, Strike(1, 6));
        Assert.True(o.GambitInAll(1));
        Assert.True(o.DeadInAll(1));
        Assert.Equal(0, o.TakenMax);
    }

    [Fact]
    public void SlipperyCapsEachHitAtOneHp()
    {
        // SlipperyPower.ModifyHpLostAfterOsty: at most 1 HP per hit while it has charges.
        var s = new SimState { Enemies = { Enemy(1, 5, 0, 10) } };
        s.Enemies[0].Slippery = 2;
        var o = SimEngine.Run(s, Strike(1, 20, hits: 2));
        Assert.True(o.SlipperyInAny(1));
        Assert.False(o.DeadInAll(1)); // two hits of 20 lose it only 2 HP
        Assert.Equal(10, o.TakenMax);
    }

    [Fact]
    public void SlipperyRunsOutOfCharges()
    {
        var s = new SimState { Enemies = { Enemy(1, 5, 0, 10) } };
        s.Enemies[0].Slippery = 2;
        SimEngine.ApplyPlayTo(s, Strike(1, 20, hits: 2), out var two, out _);
        Assert.Equal(3, two[0].Enemy(1)!.Hp);
        SimEngine.ApplyPlayTo(s, Strike(1, 20, hits: 3), out var three, out _);
        Assert.Equal(0, Math.Max(0, three[0].Enemy(1)!.Hp)); // the third hit has no charge left
    }

    [Fact]
    public void JuggernautCountsEveryBlockGainOfThePlay()
    {
        // The card's own block, a block proc, and a Frost evoke: three gains.
        var s = new SimState { Enemies = { Enemy(1, 50, 0, 5) } };
        s.P.Orbs = new() { new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 } };
        s.P.OrbCapacity = 1;
        var p = new SimPlay { Name = "Coolheaded", BlockAmount = 5, BlockGains = 1, ProcBlock = { 3 } };
        p.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.EvokeFront });
        var o = SimEngine.Run(s, p);
        Assert.Equal(3, o.PlayBlockGainsMax);
        Assert.Equal(3, o.PlayBlockGainsMin);
    }

    [Fact]
    public void SneakyCountsAsABlockReaction()
    {
        var s = new SimState { Enemies = { Enemy(1, 30, 0, 5) } };
        s.Enemies[0].Sneaky = 4;
        Assert.Contains(1, SimEngine.Run(s, Strike(1, 3)).BlockReactionInAny);
        Assert.Empty(SimEngine.Run(s, new SimPlay { Name = "Defend", BlockAmount = 5, BlockGains = 1 }).BlockReactionInAny);
    }
}
