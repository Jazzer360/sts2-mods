using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A card whose damage changes during its own plays (decompile v0.113.1). The adapter passes each hit's damage by play and by
/// Mighty Wings triggers so far (SimPlay.HitDamageByPlay[enemy] = (PerPlay, Damage[play][triggers])):
///  - Tender (TenderPower.AfterCardPlayed): 1 Strength and 1 Dexterity less after each play of yours; each play of a replay
///    is one, so play k hits and blocks with k less (BlockByPlay for its block).
///  - Pen Nib (PenNib.ModifyDamageMultiplicative): doubles only the play whose BeforeCardPlayed brings its count to 10, so a
///    replay's other plays aren't doubled.
///  - Mighty Wings (MightyWings.AfterDamageGiven): 2 Strength on each powered hit of yours dealing 30 or more (blocked plus
///    HP lost), right away, so the attack's next hits are stronger. The simulator counts the triggers (SimState.BigHits).
/// </summary>
public class SimByPlayTests
{
    private static SimEnemy E(int id, int hp, int block = 0) => new() { Id = id, Name = $"E{id}", Hp = hp, Block = block, Hits = new() { 7, 7, 7 } };

    private static int[][] PerPlay(params int[] damage) => damage.Select(d => new[] { d }).ToArray();

    private static SimPlay Single(int target, int hits, int damage) =>
        new() { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = target, Hits = hits, HitDamage = { [target] = damage } };

    private static SimState After(SimState s, SimPlay p)
    {
        SimEngine.ApplyPlayTo(s, p, out var branches, out _);
        return Assert.Single(branches);
    }

    [Fact]
    public void TenderReplayHitsOneLessOnItsSecondPlay()
    {
        // Wah Sweep+ played twice into a 23-HP Hunter Killer: 9, then 8 (a report from 0.45.0 predicted 9 twice).
        var p = Single(0, 2, 9);
        p.HitDamageByPlay[0] = (1, PerPlay(9, 8));
        Assert.Equal(6, After(new SimState { Enemies = { E(0, 23) } }, p).Enemy(0)!.Hp);
    }

    [Fact]
    public void EachPlayStepsAndHitsWithinAPlayMatch()
    {
        // Two hits a play, three plays: 5, 5, 4, 4, 3, 3.
        var p = new SimPlay { Name = "Twin Strike", IsAttack = true, HitTarget = SimPlay.Target.All, Hits = 6, HitDamage = { [0] = 5 } };
        p.HitDamageByPlay[0] = (2, PerPlay(5, 4, 3));
        Assert.Equal(26, After(new SimState { Enemies = { E(0, 50) } }, p).Enemy(0)!.Hp);
    }

    [Fact]
    public void TenderReplayBlocksOneLessOnItsSecondPlay()
    {
        var p = new SimPlay { Name = "Defend", BlockGains = 2, BlockAmount = 5, BlockByPlay = (1, new[] { 5, 4 }) };
        Assert.Equal(9, After(new SimState { Enemies = { E(0, 50) } }, p).P.Block);
    }

    [Fact]
    public void PenNibDoublesOnlyTheReplaysFirstPlay()
    {
        // Ki Whip with Pen Nib ready, played twice into a 56-HP Magi Knight: 14, then 7 (a report from 0.46.2 predicted 14 twice).
        var p = Single(0, 2, 14);
        p.HitDamageByPlay[0] = (1, PerPlay(14, 7));
        Assert.Equal(35, After(new SimState { Enemies = { E(0, 56) } }, p).Enemy(0)!.Hp);
    }

    [Fact]
    public void MightyWingsStrengthLandsBeforeTheNextHit()
    {
        // Repeat Shot+ into a 70-HP Soul Nexus: 33 (30 or more: +2 Strength), then 36 (a report from 0.46.2 predicted 33 twice).
        var p = Single(0, 2, 33);
        p.BigHitThreshold = 30;
        p.HitDamageByPlay[0] = (2, new[] { new[] { 33, 36, 39 } });
        var s = After(new SimState { Enemies = { E(0, 70) } }, p);
        Assert.Equal(1, s.Enemy(0)!.Hp);
        Assert.Equal(2, s.BigHits);
    }

    [Fact]
    public void BlockedDamageCountsTowardMightyWings()
    {
        // 25 into 10 block and 50 HP: 25 dealt in all (under 30), so no Strength for the second hit.
        var p = Single(0, 2, 25);
        p.BigHitThreshold = 30;
        p.HitDamageByPlay[0] = (2, new[] { new[] { 25, 28 } });
        var s = After(new SimState { Enemies = { E(0, 50, block: 10) } }, p);
        Assert.Equal(0, s.BigHits);
        Assert.Equal(10, s.Enemy(0)!.Hp);
    }

    [Fact]
    public void ByPlayOverridesTheSingleChangePoint()
    {
        var p = Single(0, 2, 9);
        p.HitDamageLater[0] = (1, 20);
        p.HitDamageByPlay[0] = (1, PerPlay(9, 8));
        Assert.Equal(33, After(new SimState { Enemies = { E(0, 50) } }, p).Enemy(0)!.Hp);
    }
}
