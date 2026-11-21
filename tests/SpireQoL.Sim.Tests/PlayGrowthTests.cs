using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Decision #160: a card whose numbers grow with each play has them on its replay (CardModel.OnPlayWrapper plays it PlayCount times,
/// each after the last one's OnPlay, enchantment OnPlay and AfterCardPlayed). A report: a Goopy Defend under Echo Form gained 9,
/// then 10.
/// </summary>
public class PlayGrowthTests
{
    [Fact]
    public void AReplaysBlockHasWhatTheFirstPlayAdded()
    {
        // Goopy.AfterCardPlayed: Amount++, and its block is Amount - 1 more: 9, then 10.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 30) } };
        var after = After(state, Play(Card("Defend", SimCardType.Skill), target: Who.None, plays: 2,
            new GainBlockAction(Times: 1, Base: 9, SimProps.Move) { GrowsEachPlay = 1 })).Single();
        Assert.Equal(19, after.You.Block);
    }

    [Fact]
    public void AReplaysDamageHasWhatTheFirstPlayAdded()
    {
        // Rampage.OnPlay: the attack, then its damage + Increase (5): 8, then 13.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50) } };
        var after = After(state, Play(Card("Rampage"), target: 1, plays: 2, Attack(baseDamage: 8) with { Damage = Attack(baseDamage: 8).Damage with { GrowsEachPlay = 5 } })).Single();
        Assert.Equal(29, after.Enemy(id: 1)!.Hp);
    }

    [Fact]
    public void OnePlayGainsNothing()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50) } };
        var after = After(state, Play(Card("Rampage"), target: 1, plays: 1, Attack(baseDamage: 8) with { Damage = Attack(baseDamage: 8).Damage with { GrowsEachPlay = 5 } })).Single();
        Assert.Equal(42, after.Enemy(id: 1)!.Hp);
    }
}
