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
        var state = new SimState { Enemies = { E(id: 1, hp: 100) } };
        state.You.Powers = state.You.Powers.With(PowerId.SerratedArrows, amount: 50);
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Impaled, amount: 1, data: 2);
        var after = Assert.Single(ScriptBuilder.After(state, ScriptBuilder.Play(ScriptBuilder.Card("Flurry"), target: 1, plays: 1, ScriptBuilder.Attack(baseDamage: 4, hits: 3))));
        Assert.Equal(84, after.Enemy(id: 1)!.Hp);
        Assert.Equal(1, after.You.Arrows);

    }

    [Fact]
    public void AReplayHitsHarderOnceItsFirstPlayMadeTheTargetVulnerable()
    {
        // Break played twice (Glam) on a Queen at 362 that isn't Vulnerable: the first play hits for 15 (15.75) and applies
        // Vulnerable (Break.OnPlay: the hit, then PowerCmd.Apply<VulnerablePower>), so the replay hits for 23 (15.75 × 1.5,
        // rounded down): 362 → 324.
        var state = new SimState { Enemies = { E(id: 1, hp: 362) } };
        var after = Assert.Single(ScriptBuilder.After(state, ScriptBuilder.Play(ScriptBuilder.Card("Break"), target: 1, plays: 2,
            ScriptBuilder.Attack(baseDamage: 15, terms: new CapturedTerms { Mul = 1.05m }), new ApplyPowerAction(PowerId.Vulnerable, Amount: 2) { Debuff = true })));
        Assert.Equal(324, after.Enemy(id: 1)!.Hp);

    }

    [Fact]
    public void AReplayedShotHitsHarderOnceItsFirstPlayImpaledTheTarget()
    {
        // Auto-report: Rapid Shot played 4 times (3 hits each) at a Rocket at 82 that isn't Impaled, with Serrated Arrows 50,
        // Weak and two arrows. The first play hits for 2 and then impales it (ArrowCmd.ShootArrowAtTarget: the attack, then
        // ImpaleTarget); each later play's third hit knocks an arrow out and its shot impales again, so their hits get +50%:
        // 3. 82 - 6 - 27 = 49.
        var state = new SimState { Enemies = { E(id: 1, hp: 82) } };
        state.You.Arrows = 2;
        state.You.Powers = state.You.Powers.With(PowerId.SerratedArrows, amount: 50).With(PowerId.Weak, amount: 1);
        var after = Assert.Single(ScriptBuilder.After(state, ScriptBuilder.Play(ScriptBuilder.Card("RapidShot"), target: 1, plays: 4,
            new ShootAction(), ScriptBuilder.Attack(baseDamage: 3, hits: 3) with { NeedsArrow = true }, new ImpaleAction())));
        Assert.Equal(49, after.Enemy(id: 1)!.Hp);

    }

    [Fact]
    public void AnAllEnemyAttackCountsEachEnemyOnItsOwn()
    {
        // Two hits of 5 on every enemy with Serrated Arrows 100: the one holding your last arrow (counter at 1) takes 10, loses
        // the arrow, then 5; the other takes 5 twice.
        var state = new SimState { Enemies = { E(id: 1, hp: 50), E(id: 2, hp: 50) } };
        state.You.Powers = state.You.Powers.With(PowerId.SerratedArrows, amount: 100);
        state.Enemies[1].Powers = PowerSet.Empty.With(PowerId.Impaled, amount: 1, data: 1);
        var after = Assert.Single(ScriptBuilder.After(state, ScriptBuilder.Play(ScriptBuilder.Card("Cleave"), target: -1, plays: 1, ScriptBuilder.Attack(baseDamage: 5, hits: 2, mode: SimPlay.Target.All))));
        Assert.Equal(40, after.Enemy(id: 1)!.Hp);
        Assert.Equal(35, after.Enemy(id: 2)!.Hp);

    }

    [Fact]
    public void DamageToYouAfterThePlayComesOffYourBlockFirst()
    {
        // Galvanic: 6 to you once the card resolves; 29 block → 23, no HP lost.
        var state = new SimState { Enemies = { E(id: 1, hp: 50) } };
        state.You.Block = 29;
        var play = new SimPlay { DamageToYou = { ("GalvanicPower", 6) } };
        SimEngine.ApplyPlayTo(state, play, out var branches, out _);
        Assert.Equal(23, branches[0].You.Block);
        Assert.Equal(0, branches[0].Taken);
    }

    [Fact]
    public void DamageToYouPastYourBlockCostsHp()
    {
        var state = new SimState { Enemies = { E(id: 1, hp: 50) } };
        state.You.Block = 4;
        var play = new SimPlay { DamageToYou = { ("GalvanicPower", 6) } };
        SimEngine.ApplyPlayTo(state, play, out var branches, out _);
        Assert.Equal(0, branches[0].You.Block);
        Assert.Equal(2, branches[0].Taken);
    }

    [Fact]
    public void RainShotHitsEveryOtherEnemyForTheSameDamage()
    {
        // 12 to the target (its block takes 5), then 12 to each other enemy (their own block first).
        var state = new SimState { Enemies = { E(id: 1, hp: 30, block: 5), E(id: 2, hp: 20, block: 3), E(id: 3, hp: 8) } };
        var play = new SimPlay { HitTarget = SimPlay.Target.Single, TargetId = 1, Hits = 1, HitDamage = { [1] = 12, [2] = 12, [3] = 12 }, SplashFromTarget = true };
        SimEngine.ApplyPlayTo(state, play, out var branches, out _);
        var branch = branches[0];
        Assert.Equal(23, branch.Enemy(id: 1)!.Hp);
        Assert.Equal(11, branch.Enemy(id: 2)!.Hp);
        Assert.False(branch.Enemy(id: 3)!.Alive);
    }

    [Fact]
    public void RainShotDealsTheTargetsDamageEvenWhenItKillsIt()
    {
        // Overkill counts: 12 to a 3 HP target still sends 12 to the others.
        var state = new SimState { Enemies = { E(id: 1, hp: 3), E(id: 2, hp: 20) } };
        var play = new SimPlay { HitTarget = SimPlay.Target.Single, TargetId = 1, Hits = 1, HitDamage = { [1] = 12, [2] = 12 }, SplashFromTarget = true };
        SimEngine.ApplyPlayTo(state, play, out var branches, out _);
        Assert.False(branches[0].Enemy(id: 1)!.Alive);
        Assert.Equal(8, branches[0].Enemy(id: 2)!.Hp);
    }

    [Fact]
    public void WuppyTakesTheHitsAimedAtYouWhileItLives()
    {
        // WuppysLoyaltyPower.ModifyPreBlockDamageTarget: 7 then 5 go to Wuppy (10 HP). The second kills it with 2 to spare
        // (Creature.LoseHpInternal's OverkillDamage), which goes on to your 2 block; the third hit (5) is all yours.
        var state = new SimState { Enemies = { E(id: 1, hp: 50, block: 0, hits: [7, 5, 5]) } };
        state.You.Block = 2;
        state.You.WuppyHp = 10;
        var outcome = SimEngine.Run(state);
        Assert.Equal(5, outcome.TakenMax);
        Assert.Equal(0, outcome.Worst.You.WuppyHp);
        Assert.Equal(10, outcome.Worst.WuppyTaken);
    }

    [Fact]
    public void WhatKillsWuppyGoesOnToYourBlockAndYou()
    {
        // Knowledge Demon's 19 into Wuppy at 8 HP with 6 block: 8 to Wuppy, 11 on, 6 blocked, 5 lost (a 1.0.0 report forecast 0).
        var state = new SimState { Enemies = { E(id: 1, hp: 116, block: 0, hits: 19) } };
        state.You.Block = 6;
        state.You.WuppyHp = 8;
        var outcome = SimEngine.Run(state);
        Assert.Equal(5, outcome.TakenMax);
        Assert.Equal(8, outcome.Worst.WuppyTaken);
    }

    [Fact]
    public void HitsOnWuppySetOffYourThorns()
    {
        // ThornsPower.BeforeDamageReceived: you're still the hit's target (Wuppy takes it later, before block).
        var state = new SimState { Enemies = { E(id: 1, hp: 50, block: 0, hits: 7) } };
        state.You.Thorns = 3;
        state.You.WuppyHp = 20;
        var outcome = SimEngine.Run(state);
        Assert.Equal(0, outcome.TakenMax);
        Assert.Equal(47, outcome.Worst.Enemy(id: 1)!.Hp);
    }

    [Fact]
    public void StretchBoostsOnlyTheFirstPlayOfAReplayedBlockCard()
    {
        // Defend+ (8) played twice with Stretch 12: 20, then 8 (StretchPower is spent by the first play).
        var state = new SimState { Enemies = { E(id: 1, hp: 50) } };
        state.You.Powers = state.You.Powers.With(PowerId.Stretch, amount: 12);
        var defend = new SimCard("Defend", SimCardType.Skill) { GainsBlock = true };
        Assert.Equal(28, Assert.Single(ScriptBuilder.After(state, ScriptBuilder.Play(defend, target: -1, plays: 2, ScriptBuilder.Block(baseBlock: 8)))).You.Block);

    }

    [Fact]
    public void AnEnchantmentsBlockComesOnEveryPlayAfterTheCardsOwn()
    {
        // Adroit 3 on a card played twice that gains 5: 5 + 3 + 5 + 3 (EnchantmentModel.OnPlay on each play).
        var state = new SimState { Enemies = { E(id: 1, hp: 50) } };
        var play = new SimPlay { BlockGains = 2, BlockAmount = 5, EnchantmentBlock = (2, 3) };
        SimEngine.ApplyPlayTo(state, play, out var branches, out _);
        Assert.Equal(16, branches[0].You.Block);
        Assert.Equal(4, branches[0].You.BlockGains);
    }

    [Fact]
    public void StretchBoostsAnAdroitBlockOnlyOnTheFirstPlay()
    {
        // An Adroit 3 attack played twice with Stretch 4 (v0.113.1: StretchPower counts the enchantment's block): 7 on the
        // first play, which spends the Stretch, then 3.
        var state = new SimState { Enemies = { E(id: 1, hp: 50) } };
        state.You.Powers = state.You.Powers.With(PowerId.Stretch, amount: 4);
        var adroit = new SimCard("Strike", SimCardType.Attack) { GainsBlock = true, Enchanted = true };
        var after = Assert.Single(ScriptBuilder.After(state, ScriptBuilder.Play(adroit, target: -1, plays: 2, ScriptBuilder.Block(baseBlock: 3))));
        Assert.Equal(10, after.You.Block);
        Assert.Equal(2, after.You.BlockGains);

    }
}
