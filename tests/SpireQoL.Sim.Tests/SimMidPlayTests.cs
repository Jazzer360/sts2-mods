using SpireQoL.Sim;
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
        // Three hits of 6 (4 with Serrated Arrows' +50%); the second hit frees the last arrow: 6, 6, 4.
        var s = new SimState { Enemies = { E(1, 100) } };
        var play = new SimPlay { HitTarget = SimPlay.Target.Single, TargetId = 1, Hits = 3, HitDamage = { [1] = 6 }, HitDamageLater = { [1] = (2, 4) } };
        SimEngine.ApplyPlayTo(s, play, out var branches, out _);
        Assert.Equal(84, branches[0].Enemy(1)!.Hp);
    }

    [Fact]
    public void AReplayHitsHarderOnceItsFirstPlayMadeTheTargetVulnerable()
    {
        // Break played twice (Glam) on a Queen at 362 that isn't Vulnerable: the first play hits for 15 and applies
        // Vulnerable (Break.OnPlay: the hit, then PowerCmd.Apply<VulnerablePower>), so the replay hits for 23
        // (15.75 × 1.5, rounded down): 362 → 324.
        var s = new SimState { Enemies = { E(1, 362) } };
        var play = new SimPlay { HitTarget = SimPlay.Target.Single, TargetId = 1, Plays = 2, Hits = 2, HitDamage = { [1] = 15 }, HitDamageLater = { [1] = (1, 23) } };
        SimEngine.ApplyPlayTo(s, play, out var branches, out _);
        Assert.Single(branches);
        Assert.Equal(324, branches[0].Enemy(1)!.Hp);
    }

    [Fact]
    public void AnAllEnemyAttackCountsEachEnemyOnItsOwn()
    {
        var s = new SimState { Enemies = { E(1, 50), E(2, 50) } };
        var play = new SimPlay { HitTarget = SimPlay.Target.All, Hits = 2, HitDamage = { [1] = 10, [2] = 10 }, HitDamageLater = { [2] = (1, 5) } };
        SimEngine.ApplyPlayTo(s, play, out var branches, out _);
        Assert.Equal(30, branches[0].Enemy(1)!.Hp);
        Assert.Equal(35, branches[0].Enemy(2)!.Hp);
    }

    [Fact]
    public void DamageToYouAfterThePlayComesOffYourBlockFirst()
    {
        // Galvanic: 6 to you once the card resolves; 29 block → 23, no HP lost.
        var s = new SimState { Enemies = { E(1, 50) } };
        s.P.Block = 29;
        var play = new SimPlay { DamageToYou = { ("GalvanicPower", 6) } };
        SimEngine.ApplyPlayTo(s, play, out var branches, out _);
        Assert.Equal(23, branches[0].P.Block);
        Assert.Equal(0, branches[0].Taken);
    }

    [Fact]
    public void DamageToYouPastYourBlockCostsHp()
    {
        var s = new SimState { Enemies = { E(1, 50) } };
        s.P.Block = 4;
        var play = new SimPlay { DamageToYou = { ("GalvanicPower", 6) } };
        SimEngine.ApplyPlayTo(s, play, out var branches, out _);
        Assert.Equal(0, branches[0].P.Block);
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
        // WuppysLoyaltyPower.ModifyPreBlockDamageTarget: 7 then 5 go to Wuppy (10 HP: the second kills it,
        // nothing carries over); the third hit (5) comes to you, through your 2 block.
        var s = new SimState { Enemies = { E(1, 50, 0, 7, 5, 5) } };
        s.P.Block = 2;
        s.P.WuppyHp = 10;
        var o = SimEngine.Run(s);
        Assert.Equal(3, o.TakenMax);
        Assert.Equal(0, o.Worst.P.WuppyHp);
        Assert.Equal(12, o.Worst.WuppyTaken);
    }

    [Fact]
    public void HitsOnWuppyDontSetOffYourThorns()
    {
        var s = new SimState { Enemies = { E(1, 50, 0, 7) } };
        s.P.Thorns = 3;
        s.P.WuppyHp = 20;
        var o = SimEngine.Run(s);
        Assert.Equal(0, o.TakenMax);
        Assert.Equal(50, o.Worst.Enemy(1)!.Hp);
    }

    [Fact]
    public void StretchBoostsOnlyTheFirstPlayOfAReplayedBlockCard()
    {
        // Defend+ played twice with Stretch 12: 20, then 8 (StretchPower is spent by the first play).
        var s = new SimState { Enemies = { E(1, 50) } };
        var play = new SimPlay { BlockGains = 2, BlockAmount = 20, BlockLater = (1, 8) };
        SimEngine.ApplyPlayTo(s, play, out var branches, out _);
        Assert.Equal(28, branches[0].P.Block);
    }
}
