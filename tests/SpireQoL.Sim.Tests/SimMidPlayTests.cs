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
}
