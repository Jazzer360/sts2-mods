using Xunit;
using static SpireQoL.Sim.Tests.V2.V2Script;

namespace SpireQoL.Sim.Tests.V2;

/// <summary>Card effects that aren't attacks (Sim/SimEngine.Osty.cs, SimEngine.Actions.cs), as their OnPlay does them.</summary>
public class CardActionTests
{
    [Fact]
    public void JudgementKillsAtOrBelowItsThreshold()
    {
        var s = new SimState { Enemies = { Enemy(0, 30), Enemy(1, 31) } };
        Assert.False(Assert.Single(After(s, Play(Card("Judgement", SimCardType.Skill), 0, 1, new ExecuteAction(30)))).Enemy(0)!.Alive);
        Assert.True(Assert.Single(After(s, Play(Card("Judgement", SimCardType.Skill), 1, 1, new ExecuteAction(30)))).Enemy(1)!.Alive);
    }

    [Fact]
    public void CaptureSpiritDamagesWithoutBeingAnAttack()
    {
        // Curl Up reacts to attacks only: not to Capture Spirit's damage.
        var s = new SimState { Enemies = { Enemy(0, 30, block: 2) } };
        s.Enemies[0].Powers = PowerSet.Empty.With(PowerId.CurlUp, 5);
        var after = Assert.Single(After(s, Play(Card("CaptureSpirit", SimCardType.Skill), 0, 1,
            new DamageAction(new DamageSpec(4, SimProps.Unblockable | SimProps.Unpowered)))));
        Assert.Equal(26, after.Enemy(0)!.Hp);
        Assert.Equal(2, after.Enemy(0)!.Block);
        Assert.False(after.Fired.Has("CurlUpPower@0"));
    }

    [Fact]
    public void NecroMasteryHitsEveryEnemyWhenOstyLosesHp()
    {
        var s = new SimState { Enemies = { Enemy(0, 50, block: 5), Enemy(1, 50) } };
        s.P.OstyHp = 7;
        s.P.Powers = s.P.Powers.With(PowerId.NecroMastery, 2);
        var after = Assert.Single(After(s, Play(Card("Sacrifice", SimCardType.Skill), -1, 1, new KillOstyAction())));
        Assert.Equal(0, after.P.OstyHp);
        Assert.Equal(36, after.Enemy(0)!.Hp); // unblockable
        Assert.Equal(36, after.Enemy(1)!.Hp);
        Assert.Equal(28, after.Fired.DataOf("NecroMasteryPower"));
    }

    [Fact]
    public void ThornsOnOstysAttackHurtsHimAndSetsOffNecroMastery()
    {
        var s = new SimState { Enemies = { Enemy(0, 50) } };
        s.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Thorns, 3);
        s.P.OstyHp = 10;
        s.P.Powers = s.P.Powers.With(PowerId.NecroMastery, 1);
        var poke = Play(Card("Poke"), 0, 1, new AttackAction(1, SimPlay.Target.Single, new DamageSpec(6, SimProps.Move) { Fixed = true }) { ByOsty = true });
        var after = Assert.Single(After(s, poke));
        Assert.Equal(7, after.P.OstyHp);
        Assert.Equal(50 - 6 - 3, after.Enemy(0)!.Hp);
    }

    [Fact]
    public void BouncingFlaskBranchesOnEveryApplicationAndSneckoSkullAddsToEach()
    {
        var s = new SimState { Enemies = { Enemy(0, 50), Enemy(1, 50) } };
        s.P.Relics = s.P.Relics.With(RelicId.SneckoSkull, 1);
        var after = After(s, Play(Card("BouncingFlask", SimCardType.Skill), -1, 1,
            new ApplyPowerAction(PowerId.Poison, 3) { To = ApplyPowerAction.Where.RandomEnemy, Times = 2, Debuff = true }));
        Assert.Equal(3, after.Count); // 8 on one, 8 on the other, 4 on each
        Assert.All(after, b => Assert.Equal(8, b.Enemies.Sum(e => e.PoisonStacks)));
    }
}

/// <summary>The play log keeps indirect firings apart per enemy (the Indirect procs setting judges each enemy's power on its own).</summary>
public class PlayLogTests
{
    [Fact]
    public void IndirectFiringsAreLoggedPerEnemy()
    {
        var s = new SimState();
        s.LogFired("SlowPower", 0, 0);
        s.IndirectNow = true;
        s.LogFired("SlowPower", 0, 1);
        Assert.Equal(2, s.Fired["SlowPower"]);
        Assert.Equal(0, s.Fired["~SlowPower@0"]);
        Assert.Equal(1, s.Fired["~SlowPower@1"]);
        Assert.Equal(1, s.Fired["SlowPower@1"]);
    }
}

/// <summary>ArrowCmd.ShootArrowAtTarget runs a shot's attack inside ImpaleTarget: with no arrow, the shot does nothing.</summary>
public class ShotTests
{
    [Fact]
    public void AShotWithoutAnArrowDoesNothingNotEvenSpendVigor()
    {
        var s = new SimState { Enemies = { Enemy(0, 50) } };
        s.P.Powers = s.P.Powers.With(PowerId.Vigor, 5);
        var shot = Play(Card("QuickShot"), 0, 2, new ShootAction(), Attack(6) with { NeedsArrow = true }, new ImpaleAction());
        var none = Assert.Single(After(s, shot));
        Assert.Equal(50, none.Enemy(0)!.Hp);
        Assert.Equal(5, none.P.Powers[PowerId.Vigor]);
        // One arrow: the first play shoots (6 + Vigor 5), the second has none left.
        s.P.Arrows = 1;
        var one = Assert.Single(After(s, shot));
        Assert.Equal(39, one.Enemy(0)!.Hp);
        Assert.Equal(1, one.Enemy(0)!.Powers[PowerId.Impaled]);
    }
}
