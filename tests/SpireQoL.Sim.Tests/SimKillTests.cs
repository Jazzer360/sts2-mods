using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Kills during the play itself (before your end of turn), behind the kill-reactive tabs: warnings
/// (Infested, Steam Eruption, Surprise, Crab Rage, Ravenous) use "in any branch", Gremlin Horn uses
/// "some enemy dies in every branch".
/// </summary>
public class SimKillTests
{
    private static SimEnemy Enemy(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    [Fact]
    public void SingleTargetKillIsInEveryBranch()
    {
        var s = new SimState { Enemies = { Enemy(1, 5, 4), Enemy(2, 20, 4) } };
        var p = new SimPlay { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 1, HitDamage = { [1] = 6 }, Hits = 1 };
        var o = SimEngine.Run(s, p);
        Assert.Contains(1, o.KilledByPlayInAll);
        Assert.DoesNotContain(2, o.KilledByPlayInAny);
        Assert.True(o.PlayKillsInEveryBranch);
    }

    [Fact]
    public void RandomHitKillsOnlyInSomeBranches()
    {
        var s = new SimState { Enemies = { Enemy(1, 3, 4), Enemy(2, 20, 4) } };
        var p = new SimPlay { Name = "Sword Boomerang", IsAttack = true, HitTarget = SimPlay.Target.Random, HitDamage = { [1] = 3, [2] = 3 }, Hits = 1 };
        var o = SimEngine.Run(s, p);
        Assert.Contains(1, o.KilledByPlayInAny);
        Assert.Empty(o.KilledByPlayInAll);
        Assert.False(o.PlayKillsInEveryBranch);
    }

    [Fact]
    public void EveryBranchKillsSomeoneEvenIfNotTheSameEnemy()
    {
        // Two 3-HP enemies, one random 3-damage hit: one of them dies whichever it hits (Gremlin Horn fires).
        var s = new SimState { Enemies = { Enemy(1, 3, 4), Enemy(2, 3, 4) } };
        var p = new SimPlay { Name = "Sword Boomerang", IsAttack = true, HitTarget = SimPlay.Target.Random, HitDamage = { [1] = 3, [2] = 3 }, Hits = 1 };
        var o = SimEngine.Run(s, p);
        Assert.Empty(o.KilledByPlayInAll);
        Assert.True(o.PlayKillsInEveryBranch);
    }

    [Fact]
    public void ProcDamageKillsCount()
    {
        // A relic proc's damage (Kusarigama, Letter Opener...) can be the kill.
        var s = new SimState { Enemies = { Enemy(1, 4, 4) } };
        var p = new SimPlay { Name = "Defend", BlockAmount = 5, BlockGains = 1 };
        p.Procs.Add(new ProcHit { Spread = SimPlay.Target.All, Damage = { [1] = 4 } });
        Assert.Contains(1, SimEngine.Run(s, p).KilledByPlayInAll);
    }

    [Fact]
    public void AllEnemyProcFinishesWhatTheCardStarted()
    {
        // Wah Cho (3) leaves an 11-HP enemy alive; Violent Chord's 9 to every enemy after the play kills it
        // (the Yah tab reads this kill: the dead enemy's arrows come loose). The 40-HP enemy survives.
        var s = new SimState { Enemies = { Enemy(1, 11, 0), Enemy(2, 40, 8) } };
        var p = new SimPlay { Name = "Wah Cho", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 1, HitDamage = { [1] = 3 }, Hits = 1, BlockAmount = 3, BlockGains = 1 };
        p.Procs.Add(new ProcHit { Spread = SimPlay.Target.Single, TargetId = 1, Damage = { [1] = 9 } });
        p.Procs.Add(new ProcHit { Spread = SimPlay.Target.Single, TargetId = 2, Damage = { [2] = 9 } });
        var o = SimEngine.Run(s, p);
        Assert.Equal(new HashSet<int> { 1 }, o.KilledByPlayInAll);
        Assert.True(o.PlayKillsInEveryBranch);

        // Without the proc the card alone doesn't kill it.
        var alone = new SimPlay { Name = "Wah Cho", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 1, HitDamage = { [1] = 3 }, Hits = 1, BlockAmount = 3, BlockGains = 1 };
        Assert.Empty(SimEngine.Run(s, alone).KilledByPlayInAny);
    }

    [Fact]
    public void EndOfTurnKillsAreNotPlayKills()
    {
        // Juggernaut from end-of-turn Plating kills it, but not during the play.
        var s = new SimState { Enemies = { Enemy(1, 4, 9) } };
        s.P.Juggernaut = 5;
        var o = SimEngine.Run(s, new SimPlay { Name = "Stone Armor", AddPlating = 3 });
        Assert.True(o.DeadInAll(1));
        Assert.Empty(o.KilledByPlayInAny);
    }

    [Fact]
    public void CurlUpReactionOnlyInTheBranchThatHitsIt()
    {
        // The Curl Up tab warns when the play makes it gain block in some branch (random target here).
        var s = new SimState { Enemies = { Enemy(1, 30, 4), Enemy(2, 30, 4) } };
        s.Enemies[0].CurlUp = 5;
        var random = new SimPlay { Name = "Sword Boomerang", IsAttack = true, HitTarget = SimPlay.Target.Random, HitDamage = { [1] = 3, [2] = 3 }, Hits = 1 };
        Assert.Contains(1, SimEngine.Run(s, random).BlockReactionInAny);
        var other = new SimPlay { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 2, HitDamage = { [2] = 6 }, Hits = 1 };
        Assert.Empty(SimEngine.Run(s, other).BlockReactionInAny);
    }
}
