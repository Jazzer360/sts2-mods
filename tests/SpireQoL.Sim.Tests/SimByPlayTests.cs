using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A replayed card whose numbers change with every play (decompile v0.113.1): Tender (TenderPower.AfterCardPlayed) takes
/// 1 Strength and 1 Dexterity after each play of yours, and each play of a replay is one, so play k hits and blocks with k
/// less. The adapter passes each play's numbers (SimPlay.HitDamageByPlay, BlockByPlay); the last one covers further plays.
/// </summary>
public class SimByPlayTests
{
    private static SimEnemy E(int id, int hp) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = new() { 7, 7, 7 } };

    [Fact]
    public void TenderReplayHitsOneLessOnItsSecondPlay()
    {
        // Wah Sweep+ played twice into a 23-HP Hunter Killer: 9, then 8 (a report from 0.45.0 predicted 9 twice).
        var s = new SimState { Enemies = { E(0, 23) } };
        var p = new SimPlay { Name = "Wah Sweep", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 0, Hits = 2, HitDamage = { [0] = 9 } };
        p.HitDamageByPlay[0] = (1, new[] { 9, 8 });
        SimEngine.ApplyPlayTo(s, p, out var branches, out _);
        Assert.Equal(6, Assert.Single(branches).Enemy(0)!.Hp);
    }

    [Fact]
    public void EachPlayStepsAndHitsWithinAPlayMatch()
    {
        // Two hits a play, three plays: 5, 5, 4, 4, 3, 3.
        var s = new SimState { Enemies = { E(0, 50) } };
        var p = new SimPlay { Name = "Twin Strike", IsAttack = true, HitTarget = SimPlay.Target.All, Hits = 6, HitDamage = { [0] = 5 } };
        p.HitDamageByPlay[0] = (2, new[] { 5, 4, 3 });
        SimEngine.ApplyPlayTo(s, p, out var branches, out _);
        Assert.Equal(26, Assert.Single(branches).Enemy(0)!.Hp);
    }

    [Fact]
    public void TenderReplayBlocksOneLessOnItsSecondPlay()
    {
        var s = new SimState { Enemies = { E(0, 50) } };
        var p = new SimPlay { Name = "Defend", BlockGains = 2, BlockAmount = 5, BlockByPlay = (1, new[] { 5, 4 }) };
        SimEngine.ApplyPlayTo(s, p, out var branches, out _);
        Assert.Equal(9, Assert.Single(branches).P.Block);
    }

    [Fact]
    public void ByPlayOverridesTheSingleChangePoint()
    {
        var s = new SimState { Enemies = { E(0, 50) } };
        var p = new SimPlay { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 0, Hits = 2, HitDamage = { [0] = 9 } };
        p.HitDamageLater[0] = (1, 20);
        p.HitDamageByPlay[0] = (1, new[] { 9, 8 });
        SimEngine.ApplyPlayTo(s, p, out var branches, out _);
        Assert.Equal(33, Assert.Single(branches).Enemy(0)!.Hp);
    }
}
