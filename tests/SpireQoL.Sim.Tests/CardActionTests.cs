using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>Card effects that aren't attacks (Sim/SimEngine.Osty.cs, SimEngine.Actions.cs), as their OnPlay does them.</summary>
public class CardActionTests
{
    [Fact]
    public void JudgementKillsAtOrBelowItsThreshold()
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 30), Enemy(id: 1, hp: 31) } };
        Assert.False(Assert.Single(After(state, Play(Card("Judgement", SimCardType.Skill), target: 0, plays: 1, new ExecuteAction(Threshold: 30)))).Enemy(id: 0)!.Alive);
        Assert.True(Assert.Single(After(state, Play(Card("Judgement", SimCardType.Skill), target: 1, plays: 1, new ExecuteAction(Threshold: 30)))).Enemy(id: 1)!.Alive);
    }

    [Fact]
    public void ADebuffOnTheTargetOfAPlayAimedAtNoOneLandsNowhere()
    {
        // A card aimed at an enemy with no enemy to aim at (every one unhittable) is simulated aimed at no one: its Weak and Poison
        // land on no one, never on you (a card is never aimed at you).
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 30) } };
        var after = Assert.Single(After(state, Play(Card("DeadlyPoison", SimCardType.Skill), target: Who.None, plays: 1,
            new ApplyPowerAction(PowerId.Weak, Amount: 1) { Debuff = true }, new ApplyPowerAction(PowerId.Poison, Amount: 5) { Debuff = true })));
        Assert.Equal(PowerSet.Empty, after.You.Powers);
        Assert.Equal(PowerSet.Empty, after.Enemy(id: 0)!.Powers);
    }

    [Fact]
    public void CaptureSpiritDamagesWithoutBeingAnAttack()
    {
        // Curl Up reacts to attacks only: not to Capture Spirit's damage.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 30, block: 2) } };
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.CurlUp, amount: 5);
        var after = Assert.Single(After(state, Play(Card("CaptureSpirit", SimCardType.Skill), target: 0, plays: 1,
            new DamageAction(new DamageSpec(Base: 4, SimProps.Unblockable | SimProps.Unpowered)))));
        Assert.Equal(26, after.Enemy(id: 0)!.Hp);
        Assert.Equal(2, after.Enemy(id: 0)!.Block);
        Assert.False(after.Fired.Has("CurlUpPower@0"));
    }

    [Fact]
    public void NecroMasteryHitsEveryEnemyWhenOstyLosesHp()
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50, block: 5), Enemy(id: 1, hp: 50) } };
        state.You.WithOsty(hp: 7);
        state.You.Powers = state.You.Powers.With(PowerId.NecroMastery, amount: 2);
        var after = Assert.Single(After(state, Play(Card("Sacrifice", SimCardType.Skill), target: Who.None, plays: 1, new KillOstyAction())));
        Assert.Equal(0, after.You.OstyHp);
        Assert.Equal(36, after.Enemy(id: 0)!.Hp); // unblockable
        Assert.Equal(36, after.Enemy(id: 1)!.Hp);
        Assert.Equal(28, after.Fired.DataOf("NecroMasteryPower"));
    }

    [Fact]
    public void ThornsOnOstysAttackHurtsHimAndSetsOffNecroMastery()
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50) } };
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Thorns, amount: 3);
        state.You.WithOsty(hp: 10);
        state.You.Powers = state.You.Powers.With(PowerId.NecroMastery, amount: 1);
        var poke = Play(Card("Poke"), target: 0, plays: 1, new AttackAction(Hits: 1, SimPlay.Target.Single, new DamageSpec(Base: 6, SimProps.Move) { Fixed = true }) { ByOsty = true });
        var after = Assert.Single(After(state, poke));
        Assert.Equal(7, after.You.OstyHp);
        Assert.Equal(50 - 6 - 3, after.Enemy(id: 0)!.Hp);
    }

    [Fact]
    public void BouncingFlaskBranchesOnEveryApplicationAndSneckoSkullAddsToEach()
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50), Enemy(id: 1, hp: 50) } };
        state.You.Relics = state.You.Relics.With(RelicId.SneckoSkull, amount: 1);
        var after = After(state, Play(Card("BouncingFlask", SimCardType.Skill), target: Who.None, plays: 1,
            new ApplyPowerAction(PowerId.Poison, Amount: 3) { To = ApplyPowerAction.Where.RandomEnemy, Times = 2, Debuff = true }));
        Assert.Equal(3, after.Count); // 8 on one, 8 on the other, 4 on each
        Assert.All(after, branch => Assert.Equal(8, branch.Enemies.Sum(enemy => enemy.PoisonStacks)));
    }
}

/// <summary>The play log keeps indirect firings apart per enemy (the Indirect procs setting judges each enemy's power on its own).</summary>
public class PlayLogTests
{
    [Fact]
    public void IndirectFiringsAreLoggedPerEnemy()
    {
        var state = new SimState();
        state.LogFired("SlowPower", amount: 0, owner: 0);
        state.IndirectNow = true;
        state.LogFired("SlowPower", amount: 0, owner: 1);
        Assert.Equal(2, state.Fired["SlowPower"]);
        Assert.Equal(0, state.Fired["~SlowPower@0"]);
        Assert.Equal(1, state.Fired["~SlowPower@1"]);
        Assert.Equal(1, state.Fired["SlowPower@1"]);
    }
}

/// <summary>ArrowCmd.ShootArrowAtTarget runs a shot's attack inside ImpaleTarget: with no arrow, the shot does nothing.</summary>
public class ShotTests
{
    [Fact]
    public void AShotWithoutAnArrowDoesNothingNotEvenSpendVigor()
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50) } };
        state.You.Powers = state.You.Powers.With(PowerId.Vigor, amount: 5);
        var shot = Play(Card("QuickShot"), target: 0, plays: 2, new ShootAction(), Attack(baseDamage: 6) with { NeedsArrow = true }, new ImpaleAction());
        var none = Assert.Single(After(state, shot));
        Assert.Equal(50, none.Enemy(id: 0)!.Hp);
        Assert.Equal(5, none.You.Powers[PowerId.Vigor]);
        // One arrow: the first play shoots (6 + Vigor 5), the second has none left.
        state.You.Arrows = 1;
        var one = Assert.Single(After(state, shot));
        Assert.Equal(39, one.Enemy(id: 0)!.Hp);
        Assert.Equal(1, one.Enemy(id: 0)!.Powers[PowerId.Impaled]);
    }

    [Fact]
    public void AnArrowSlotGainedHasItsArrowToShoot()
    {
        // Quiver (ArrowCmd.GainArrowSlot: PlayerCombatState.GainArrow adds an ArrowSlot, Retrieved as it starts, so AvailableArrowCount counts it), then a
        // shot in the same simulation finds one of the two arrows.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50) } };
        var after = Assert.Single(After(state, Play(Card("Quiver", SimCardType.Power), target: 0, plays: 1, new GainArrowSlotsAction(Count: 2), new ShootAction(),
            Attack(baseDamage: 6) with { NeedsArrow = true }, new ImpaleAction())));
        Assert.Equal(1, after.You.Arrows);
    }
}
