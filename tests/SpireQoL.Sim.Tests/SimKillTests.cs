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
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5, hits: 4), Enemy(id: 2, hp: 20, hits: 4) } };
        var play = new SimPlay { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 1, HitDamage = { [1] = 6 }, Hits = 1 };
        var outcome = SimEngine.Run(state, play);
        Assert.Contains(1, outcome.KilledByPlayInAll);
        Assert.DoesNotContain(2, outcome.KilledByPlayInAny);
        Assert.True(outcome.PlayKillsInAll);
    }

    [Fact]
    public void RandomHitKillsOnlyInSomeBranches()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 3, hits: 4), Enemy(id: 2, hp: 20, hits: 4) } };
        var play = new SimPlay { Name = "Sword Boomerang", IsAttack = true, HitTarget = SimPlay.Target.Random, HitDamage = { [1] = 3, [2] = 3 }, Hits = 1 };
        var outcome = SimEngine.Run(state, play);
        Assert.Contains(1, outcome.KilledByPlayInAny);
        Assert.Empty(outcome.KilledByPlayInAll);
        Assert.False(outcome.PlayKillsInAll);
    }

    [Fact]
    public void EveryBranchKillsSomeoneEvenIfNotTheSameEnemy()
    {
        // Two 3-HP enemies, one random 3-damage hit: one of them dies whichever it hits (Gremlin Horn fires).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 3, hits: 4), Enemy(id: 2, hp: 3, hits: 4) } };
        var play = new SimPlay { Name = "Sword Boomerang", IsAttack = true, HitTarget = SimPlay.Target.Random, HitDamage = { [1] = 3, [2] = 3 }, Hits = 1 };
        var outcome = SimEngine.Run(state, play);
        Assert.Empty(outcome.KilledByPlayInAll);
        Assert.True(outcome.PlayKillsInAll);
    }

    [Fact]
    public void ProcDamageKillsCount()
    {
        // A relic proc's damage (Kusarigama, Letter Opener...) can be the kill.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 4, hits: 4) } };
        var play = new SimPlay { Name = "Defend", BlockAmount = 5, BlockGains = 1 };
        play.Procs.Add(new ProcHit { Spread = SimPlay.Target.All, Damage = { [1] = 4 } });
        Assert.Contains(1, SimEngine.Run(state, play).KilledByPlayInAll);
    }

    [Fact]
    public void AllEnemyProcFinishesWhatTheCardStarted()
    {
        // Wah Cho (3) leaves an 11-HP enemy alive; Violent Chord's 9 to every enemy after the play kills it
        // (the Yah tab reads this kill: the dead enemy's arrows come loose). The 40-HP enemy survives.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 11, hits: 0), Enemy(id: 2, hp: 40, hits: 8) } };
        var play = new SimPlay { Name = "Wah Cho", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 1, HitDamage = { [1] = 3 }, Hits = 1, BlockAmount = 3, BlockGains = 1 };
        play.Procs.Add(new ProcHit { Spread = SimPlay.Target.Single, TargetId = 1, Damage = { [1] = 9 } });
        play.Procs.Add(new ProcHit { Spread = SimPlay.Target.Single, TargetId = 2, Damage = { [2] = 9 } });
        var outcome = SimEngine.Run(state, play);
        Assert.Equal(new HashSet<int> { 1 }, outcome.KilledByPlayInAll);
        Assert.True(outcome.PlayKillsInAll);

        // Without the proc the card alone doesn't kill it.
        var alone = new SimPlay { Name = "Wah Cho", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 1, HitDamage = { [1] = 3 }, Hits = 1, BlockAmount = 3, BlockGains = 1 };
        Assert.Empty(SimEngine.Run(state, alone).KilledByPlayInAny);
    }

    [Fact]
    public void EndOfTurnKillsAreNotPlayKills()
    {
        // Juggernaut from end-of-turn Plating kills it, but not during the play.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 4, hits: 9) } };
        state.You.Juggernaut = 5;
        var outcome = SimEngine.Run(state, new SimPlay { Name = "Stone Armor", AddPlating = 3 });
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.Empty(outcome.KilledByPlayInAny);
    }

    [Fact]
    public void CurlUpReactionOnlyInTheBranchThatHitsIt()
    {
        // Curl Up gains its block in the branch where the play hits it (random target here).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 30, hits: 4), Enemy(id: 2, hp: 30, hits: 4) } };
        state.Enemies[0].CurlUp = 5;
        var random = new SimPlay { Name = "Sword Boomerang", IsAttack = true, HitTarget = SimPlay.Target.Random, HitDamage = { [1] = 3, [2] = 3 }, Hits = 1 };
        SimEngine.ApplyPlayTo(state, random, out var randomBranches, out _);
        Assert.Contains(randomBranches, branch => branch.Enemy(id: 1)!.Block == 5);
        var other = new SimPlay { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 2, HitDamage = { [2] = 6 }, Hits = 1 };
        SimEngine.ApplyPlayTo(state, other, out var otherBranches, out _);
        Assert.DoesNotContain(otherBranches, branch => branch.Enemy(id: 1)!.Block > 0);
    }
}
