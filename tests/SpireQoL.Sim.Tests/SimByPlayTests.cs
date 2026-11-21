using Xunit;
using static SpireQoL.Sim.Tests.V2.V2Script;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A card whose damage or block changes during its own plays (decompile v0.113.1), worked out by the models as each hit lands:
///  - Tender (TenderPower.AfterCardPlayed): 1 Strength and 1 Dexterity less after each play of yours; each play of a replay
///    is one, so play k hits and blocks with k less.
///  - Pen Nib (PenNib.ModifyDamageMultiplicative): doubles only the play whose BeforeCardPlayed brings its count to 10.
///  - Mighty Wings (MightyWings.AfterDamageGiven): 2 Strength on each powered hit of yours dealing 30 or more (blocked plus
///    HP lost), right away, so the attack's next hits are stronger.
/// </summary>
public class SimByPlayTests
{
    private static SimState State(int hp, int block = 0) => new() { Enemies = { Enemy(0, hp, block) } };

    [Fact]
    public void TenderReplayHitsOneLessOnEachPlay()
    {
        // Twin Strike (two hits a play) played three times: 5, 5, 4, 4, 3, 3.
        var s = State(50);
        s.You.Powers = s.You.Powers.With(PowerId.Tender, 1);
        var after = Assert.Single(After(s, Play(Card("TwinStrike"), 0, 3, Attack(5, hits: 2))));
        Assert.Equal(26, after.Enemy(0)!.Hp);
    }

    [Fact]
    public void TenderReplayBlocksOneLessOnItsSecondPlay()
    {
        var s = State(50);
        s.You.Powers = s.You.Powers.With(PowerId.Tender, 1);
        var after = Assert.Single(After(s, Play(Card("Defend", SimCardType.Skill), -1, 2, Block(5))));
        Assert.Equal(9, after.You.Block);
    }

    [Fact]
    public void MightyWingsStrengthLandsBeforeTheNextHit()
    {
        // Repeat Shot+ into a 70-HP Soul Nexus: 33 (30 or more: +2 Strength), then 35 (a report from 0.46.2 predicted 33 twice).
        var s = State(70);
        s.You.Relics = s.You.Relics.With(RelicId.MightyWings, 2, 30);
        var after = Assert.Single(After(s, Play(Card("RepeatShot"), 0, 1, Attack(33, hits: 2))));
        Assert.Equal(2, after.Enemy(0)!.Hp);
        Assert.Equal(4, after.You.Powers[PowerId.Strength]); // the second hit (35) is 30 or more too
    }

    [Fact]
    public void BlockedDamageCountsTowardMightyWings()
    {
        // 25 into 10 block: 25 dealt in all (under 30), so no Strength for the second hit; 31 would count, block included.
        var s = State(50, block: 10);
        s.You.Relics = s.You.Relics.With(RelicId.MightyWings, 2, 30);
        Assert.False(Assert.Single(After(s, Play(Card("Strike"), 0, 1, Attack(25, hits: 2)))).You.Powers.Has(PowerId.Strength));
        Assert.Equal(2, Assert.Single(After(s, Play(Card("Strike"), 0, 1, Attack(31)))).You.Powers[PowerId.Strength]);
    }
}
