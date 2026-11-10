using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Rules from the first logic review of the simulator (each checked against the v0.111.0 decompile).
/// </summary>
public class SimReviewTests
{
    private static SimEnemy Enemy(int id, int hp, int block = 0, params int[] hits)
        => new() { Id = id, Name = $"E{id}", Hp = hp, Block = block, Hits = hits.ToList() };

    [Fact]
    public void BeatingRemnantBudgetCarriesFromYourTurnIntoTheEnemyTurn()
    {
        // BeatingRemnant.BeforeSideTurnStart resets only at the start of YOUR turn.
        var s = new SimState { Enemies = { Enemy(1, 50, 0, 10) } };
        s.P.RemnantCap = 20;
        s.P.RemnantLeftPlayerTurn = 5; // already lost 15 this turn
        Assert.Equal(5, SimEngine.Run(s).TakenMax);
    }

    [Fact]
    public void BufferAbsorbingAHitDoesNotSpendTheRemnantBudget()
    {
        // Remnant counts result.UnblockedDamage after Buffer (ModifyHpLostAfterOstyLate).
        var s = new SimState { Enemies = { Enemy(1, 50, 0, 6, 6) } };
        s.P.RemnantCap = 6;
        s.P.RemnantLeftPlayerTurn = 6;
        s.P.Buffer = 1;
        Assert.Equal(6, SimEngine.Run(s).TakenMax); // first hit buffered, second hit uses the full 6
    }

    [Fact]
    public void EtherealCardsExhaustBeforeBurnDealsItsDamage()
    {
        // CombatManager.DoTurnEnd: orb passives, then Ethereal exhausts (Feel No Pain block), then
        // DoTurnEndCards (Burn...). The block is up before Burn hits.
        var s = new SimState { Enemies = { Enemy(1, 50) } };
        s.P.FeelNoPain = 3;
        s.P.Fading = 1;
        s.P.HandBlockable.Add(("Burn", 2));
        Assert.Equal(0, SimEngine.Run(s).TakenMax);
    }

    [Fact]
    public void GlassEvokeShrinksWithItsPassive()
    {
        // GlassOrb.EvokeVal => PassiveVal * 2; the passive loses 1 per trigger. Consuming Shadow evokes
        // after the passives (AfterSideTurnEnd): Glass 4 → passive 4, then evoke (3*2)=6, total 10.
        var s = new SimState { Enemies = { Enemy(1, 11) } };
        s.P.Orbs = new() { new SimOrb { Kind = OrbKind.Glass, Passive = 4, Evoke = 8 } };
        s.P.OrbCapacity = 3;
        s.P.ConsumingShadow = 1;
        var o = SimEngine.Run(s);
        Assert.Equal(1, o.Worst.Enemy(1)!.Hp);
        Assert.False(o.DeadInAll(1));
    }

    [Fact]
    public void GoldPlatedCablesBonusFollowsTheFrontOrb()
    {
        // GoldPlatedCables: +1 trigger for whichever orb is Orbs[0] when its passive fires. After Zap
        // evicts the front Frost orb, the bonus goes to the next orb (a Lightning).
        var s = new SimState { Enemies = { Enemy(1, 7, 0, 10) } };
        s.P.Orbs = new() { new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 0 }, new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 } };
        s.P.OrbCapacity = 2;
        s.P.FrontOrbBonusTriggers = 1;
        var zap = new SimPlay { OrbOps = { new OrbOp { Op = OrbOp.Kind.Channel, Orb = new SimOrb { Kind = OrbKind.Lightning, Passive = 0, Evoke = 0 } } } };
        var o = SimEngine.Run(s, zap);
        Assert.Equal(1, o.Worst.Enemy(1)!.Hp); // the front Lightning fires twice: 3 + 3
    }

    [Fact]
    public void LethalMeansDeadBeforeItActsNotKilledByRetaliationDuringItsAttack()
    {
        var s = new SimState { Enemies = { Enemy(1, 3, 0, 5, 5) } };
        s.P.FlameBarrier = 5; // kills it after its first hit lands
        var o = SimEngine.Run(s);
        Assert.Equal(0, o.Worst.Enemy(1)!.Hp > 0 ? 1 : 0);
        Assert.False(o.DeadInAll(1)); // it still acted
        Assert.Equal(5, o.TakenMax);
    }

    [Fact]
    public void PowersFireBeforeRelicsAtTurnEnd()
    {
        // IterateHookListeners: your powers before your relics. Hailstorm (power) kills the 3 HP enemy
        // before Orichalcum's block (relic) sets off Juggernaut, which then can only hit the other one.
        var s = new SimState { Enemies = { Enemy(1, 3, 0, 10), Enemy(2, 4, 0, 10) } };
        s.P.Hailstorm = 3;
        s.P.HailstormFrostNeeded = 0;
        s.P.OrichalcumBlock = 5;
        s.P.Juggernaut = 1;
        var o = SimEngine.Run(s);
        Assert.True(o.DeadInAll(1));
        Assert.True(o.DeadInAll(2)); // 3 from Hailstorm + 1 from Juggernaut
    }

    [Fact]
    public void DisintegrationLandsAfterParryingShieldChecksYourBlock()
    {
        // DisintegrationPower: AfterSideTurnEndLate, after Parrying Shield (AfterSideTurnEnd).
        var s = new SimState { Enemies = { Enemy(1, 5, 0, 1) } };
        s.P.Block = 10;
        s.P.ParryingShieldBlock = 10;
        s.P.ParryingShieldDamage = 5;
        s.P.DebuffLateBlockable.Add(("Disintegration", 4));
        Assert.True(SimEngine.Run(s).DeadInAll(1));
    }

    [Fact]
    public void ImbalancedCountsAHitThatBufferStops()
    {
        // WasFullyBlocked = (blocked > 0 || block > 0) && no HP lost after the AfterOsty modifiers.
        var e = Enemy(1, 40, 0, 8);
        e.Imbalanced = true;
        var s = new SimState { Enemies = { e } };
        s.P.Block = 3;
        s.P.Buffer = 1;
        Assert.True(SimEngine.Run(s).FirstHitBlockedInAll(1));
    }

    [Fact]
    public void DamageStoppedByAnExhaustedHardenedShellDoesNotWakeIt()
    {
        var e = Enemy(1, 50, 0, 0);
        e.ShellLeft = 0;
        e.ShellPerTurn = 10;
        var hit = new SimPlay { HitTarget = SimPlay.Target.Single, TargetId = 1, Hits = 1, HitDamage = { [1] = 6 } };
        Assert.Equal(0, SimEngine.Run(new SimState { Enemies = { e } }, hit).UnblockedMax(1));
    }
}
