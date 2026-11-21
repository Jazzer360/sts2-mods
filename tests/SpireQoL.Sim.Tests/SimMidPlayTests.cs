using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Things that change partway through a play (game v0.113.0): hits that lose a bonus once your last arrow comes
/// out of an Impaled enemy (Serrated Arrows, Wah Hammer), damage to you once the card resolves (Galvanic), and
/// Rain Shot's damage to the other enemies.
/// </summary>
public class SimMidPlayTests
{
    private static SimEnemy E(int id, int hp, int block = 0, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Block = block, Hits = hits.ToList() };

    [Fact]
    public void HitsAfterTheLastArrowComesOutLoseTheBonus()
    {
        // Three hits of 4 with Serrated Arrows 50 (6) on an enemy holding your last arrow, its counter at 2: the second hit
        // knocks the arrow out (ImpaledPower.AfterDamageReceived) and with it Impaled, so the third hit loses the bonus: 6, 6, 4.
        var s = new SimState { Enemies = { E(1, 100) } };
        s.You.Powers = s.You.Powers.With(PowerId.SerratedArrows, 50);
        s.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Impaled, 1, 2);
        var after = Assert.Single(ScriptBuilder.After(s, ScriptBuilder.Play(ScriptBuilder.Card("Flurry"), 1, 1, ScriptBuilder.Attack(4, hits: 3))));
        Assert.Equal(84, after.Enemy(1)!.Hp);
        Assert.Equal(1, after.You.Arrows);

    }

    [Fact]
    public void AReplayHitsHarderOnceItsFirstPlayMadeTheTargetVulnerable()
    {
        // Break played twice (Glam) on a Queen at 362 that isn't Vulnerable: the first play hits for 15 (15.75) and applies
        // Vulnerable (Break.OnPlay: the hit, then PowerCmd.Apply<VulnerablePower>), so the replay hits for 23 (15.75 × 1.5,
        // rounded down): 362 → 324.
        var s = new SimState { Enemies = { E(1, 362) } };
        var after = Assert.Single(ScriptBuilder.After(s, ScriptBuilder.Play(ScriptBuilder.Card("Break"), 1, 2,
            ScriptBuilder.Attack(15, terms: new CapturedTerms { Mul = 1.05m }), new ApplyPowerAction(PowerId.Vulnerable, 2) { Debuff = true })));
        Assert.Equal(324, after.Enemy(1)!.Hp);

    }

    [Fact]
    public void AReplayedShotHitsHarderOnceItsFirstPlayImpaledTheTarget()
    {
        // Auto-report: Rapid Shot played 4 times (3 hits each) at a Rocket at 82 that isn't Impaled, with Serrated Arrows 50,
        // Weak and two arrows. The first play hits for 2 and then impales it (ArrowCmd.ShootArrowAtTarget: the attack, then
        // ImpaleTarget); each later play's third hit knocks an arrow out and its shot impales again, so their hits get +50%:
        // 3. 82 - 6 - 27 = 49.
        var s = new SimState { Enemies = { E(1, 82) } };
        s.You.Arrows = 2;
        s.You.Powers = s.You.Powers.With(PowerId.SerratedArrows, 50).With(PowerId.Weak, 1);
        var after = Assert.Single(ScriptBuilder.After(s, ScriptBuilder.Play(ScriptBuilder.Card("RapidShot"), 1, 4,
            new ShootAction(), ScriptBuilder.Attack(3, hits: 3) with { NeedsArrow = true }, new ImpaleAction())));
        Assert.Equal(49, after.Enemy(1)!.Hp);

    }

    [Fact]
    public void AnAllEnemyAttackCountsEachEnemyOnItsOwn()
    {
        // Two hits of 5 on every enemy with Serrated Arrows 100: the one holding your last arrow (counter at 1) takes 10, loses
        // the arrow, then 5; the other takes 5 twice.
        var s = new SimState { Enemies = { E(1, 50), E(2, 50) } };
        s.You.Powers = s.You.Powers.With(PowerId.SerratedArrows, 100);
        s.Enemies[1].Powers = PowerSet.Empty.With(PowerId.Impaled, 1, 1);
        var after = Assert.Single(ScriptBuilder.After(s, ScriptBuilder.Play(ScriptBuilder.Card("Cleave"), -1, 1, ScriptBuilder.Attack(5, hits: 2, mode: SimPlay.Target.All))));
        Assert.Equal(40, after.Enemy(1)!.Hp);
        Assert.Equal(35, after.Enemy(2)!.Hp);

    }

    [Fact]
    public void DamageToYouAfterThePlayComesOffYourBlockFirst()
    {
        // Galvanic: 6 to you once the card resolves; 29 block → 23, no HP lost.
        var s = new SimState { Enemies = { E(1, 50) } };
        s.You.Block = 29;
        var play = new SimPlay { DamageToYou = { ("GalvanicPower", 6) } };
        SimEngine.ApplyPlayTo(s, play, out var branches, out _);
        Assert.Equal(23, branches[0].You.Block);
        Assert.Equal(0, branches[0].Taken);
    }

    [Fact]
    public void DamageToYouPastYourBlockCostsHp()
    {
        var s = new SimState { Enemies = { E(1, 50) } };
        s.You.Block = 4;
        var play = new SimPlay { DamageToYou = { ("GalvanicPower", 6) } };
        SimEngine.ApplyPlayTo(s, play, out var branches, out _);
        Assert.Equal(0, branches[0].You.Block);
        Assert.Equal(2, branches[0].Taken);
    }

    [Fact]
    public void RainShotHitsEveryOtherEnemyForTheSameDamage()
    {
        // 12 to the target (its block takes 5), then 12 to each other enemy (their own block first).
        var s = new SimState { Enemies = { E(1, 30, 5), E(2, 20, 3), E(3, 8) } };
        var play = new SimPlay { HitTarget = SimPlay.Target.Single, TargetId = 1, Hits = 1, HitDamage = { [1] = 12, [2] = 12, [3] = 12 }, SplashFromTarget = true };
        SimEngine.ApplyPlayTo(s, play, out var branches, out _);
        var b = branches[0];
        Assert.Equal(23, b.Enemy(1)!.Hp);
        Assert.Equal(11, b.Enemy(2)!.Hp);
        Assert.False(b.Enemy(3)!.Alive);
    }

    [Fact]
    public void RainShotDealsTheTargetsDamageEvenWhenItKillsIt()
    {
        // Overkill counts: 12 to a 3 HP target still sends 12 to the others.
        var s = new SimState { Enemies = { E(1, 3), E(2, 20) } };
        var play = new SimPlay { HitTarget = SimPlay.Target.Single, TargetId = 1, Hits = 1, HitDamage = { [1] = 12, [2] = 12 }, SplashFromTarget = true };
        SimEngine.ApplyPlayTo(s, play, out var branches, out _);
        Assert.False(branches[0].Enemy(1)!.Alive);
        Assert.Equal(8, branches[0].Enemy(2)!.Hp);
    }

    [Fact]
    public void WuppyTakesTheHitsAimedAtYouWhileItLives()
    {
        // WuppysLoyaltyPower.ModifyPreBlockDamageTarget: 7 then 5 go to Wuppy (10 HP). The second kills it with 2 to spare
        // (Creature.LoseHpInternal's OverkillDamage), which goes on to your 2 block; the third hit (5) is all yours.
        var s = new SimState { Enemies = { E(1, 50, 0, 7, 5, 5) } };
        s.You.Block = 2;
        s.You.WuppyHp = 10;
        var o = SimEngine.Run(s);
        Assert.Equal(5, o.TakenMax);
        Assert.Equal(0, o.Worst.You.WuppyHp);
        Assert.Equal(10, o.Worst.WuppyTaken);
    }

    [Fact]
    public void WhatKillsWuppyGoesOnToYourBlockAndYou()
    {
        // Knowledge Demon's 19 into Wuppy at 8 HP with 6 block: 8 to Wuppy, 11 on, 6 blocked, 5 lost (a 1.0.0 report forecast 0).
        var s = new SimState { Enemies = { E(1, 116, 0, 19) } };
        s.You.Block = 6;
        s.You.WuppyHp = 8;
        var o = SimEngine.Run(s);
        Assert.Equal(5, o.TakenMax);
        Assert.Equal(8, o.Worst.WuppyTaken);
    }

    [Fact]
    public void HitsOnWuppySetOffYourThorns()
    {
        // ThornsPower.BeforeDamageReceived: you're still the hit's target (Wuppy takes it later, before block).
        var s = new SimState { Enemies = { E(1, 50, 0, 7) } };
        s.You.Thorns = 3;
        s.You.WuppyHp = 20;
        var o = SimEngine.Run(s);
        Assert.Equal(0, o.TakenMax);
        Assert.Equal(47, o.Worst.Enemy(1)!.Hp);
    }

    [Fact]
    public void StretchBoostsOnlyTheFirstPlayOfAReplayedBlockCard()
    {
        // Defend+ (8) played twice with Stretch 12: 20, then 8 (StretchPower is spent by the first play).
        var s = new SimState { Enemies = { E(1, 50) } };
        s.You.Powers = s.You.Powers.With(PowerId.Stretch, 12);
        var defend = new SimCard("Defend", SimCardType.Skill) { GainsBlock = true };
        Assert.Equal(28, Assert.Single(ScriptBuilder.After(s, ScriptBuilder.Play(defend, -1, 2, ScriptBuilder.Block(8)))).You.Block);

    }

    [Fact]
    public void AnEnchantmentsBlockComesOnEveryPlayAfterTheCardsOwn()
    {
        // Adroit 3 on a card played twice that gains 5: 5 + 3 + 5 + 3 (EnchantmentModel.OnPlay on each play).
        var s = new SimState { Enemies = { E(1, 50) } };
        var play = new SimPlay { BlockGains = 2, BlockAmount = 5, EnchantmentBlock = (2, 3) };
        SimEngine.ApplyPlayTo(s, play, out var branches, out _);
        Assert.Equal(16, branches[0].You.Block);
        Assert.Equal(4, branches[0].You.BlockGains);
    }

    [Fact]
    public void StretchBoostsAnAdroitBlockOnlyOnTheFirstPlay()
    {
        // An Adroit 3 attack played twice with Stretch 4 (v0.113.1: StretchPower counts the enchantment's block): 7 on the
        // first play, which spends the Stretch, then 3.
        var s = new SimState { Enemies = { E(1, 50) } };
        s.You.Powers = s.You.Powers.With(PowerId.Stretch, 4);
        var adroit = new SimCard("Strike", SimCardType.Attack) { GainsBlock = true, Enchanted = true };
        var after = Assert.Single(ScriptBuilder.After(s, ScriptBuilder.Play(adroit, -1, 2, ScriptBuilder.Block(3))));
        Assert.Equal(10, after.You.Block);
        Assert.Equal(2, after.You.BlockGains);

    }
}
