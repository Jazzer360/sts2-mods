using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Attacks Osty makes for your cards (DamageCmd.Attack(...).FromOsty: Bone Shards, Poke, Squeeze...): enemy Thorns hits the
/// dealer, Osty, not you; your Envenom (EnvenomPower.AfterDamageGiven: you as the dealer) doesn't apply; the enemy reacts
/// as to any card hit (Curl Up, Slippery).
/// </summary>
public class SimOstyAttackTests
{
    private static SimEnemy E(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    private static SimPlay Poke(int target, int damage, bool byOsty) => new()
    {
        Name = "Poke", HitTarget = SimPlay.Target.Single, TargetId = target, Hits = 1, HitDamage = { [target] = damage }, ByOsty = byOsty,
    };

    [Fact]
    public void ThornsHitsOstyNotYou()
    {
        var s = new SimState { Enemies = { E(1, 30) } };
        s.Enemies[0].Thorns = 3;
        s.P.OstyHp = 10;
        SimEngine.ApplyPlayTo(s, Poke(1, 6, byOsty: true), out var branches, out _);
        Assert.All(branches, b =>
        {
            Assert.Equal(24, b.Enemy(1)!.Hp);
            Assert.Equal(7, b.P.OstyHp);
            Assert.Equal(0, b.Taken);
        });
        // Your own attack takes the Thorns yourself.
        SimEngine.ApplyPlayTo(s, Poke(1, 6, byOsty: false), out var mine, out _);
        Assert.All(mine, b => Assert.Equal(3, b.Taken));
    }

    [Fact]
    public void YourEnvenomDoesntApply()
    {
        var s = new SimState { Enemies = { E(1, 30) } };
        s.P.Envenom = 2;
        SimEngine.ApplyPlayTo(s, Poke(1, 6, byOsty: true), out var branches, out _);
        Assert.All(branches, b => Assert.Equal(0, b.Enemy(1)!.Poison));
    }

    [Fact]
    public void SlipperyReactsToOstysHit()
    {
        var s = new SimState { Enemies = { E(1, 30) } };
        s.Enemies[0].Slippery = 1;
        Assert.True(SimEngine.Run(s, Poke(1, 6, byOsty: true)).SlipperyInAny(1));
    }

    [Fact]
    public void NecroMasteryHitsEveryEnemyThroughBlockWhenOstyDies()
    {
        // Bone Shards kills Osty at 4 HP with Necro Mastery 2: 8 unblockable damage to every enemy.
        var s = new SimState { Enemies = { E(1, 30), E(2, 5) } };
        s.Enemies[0].Block = 20;
        var play = new SimPlay { Name = "BoneShards", KillsOsty = true, AllEnemiesUnblockable = 8 };
        SimEngine.ApplyPlayTo(s, play, out var branches, out _);
        Assert.All(branches, b =>
        {
            Assert.Equal(22, b.Enemy(1)!.Hp);
            Assert.Equal(20, b.Enemy(1)!.Block);
            Assert.False(b.Enemy(2)!.Alive);
        });
    }
}
