using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// UnmovablePower.ModifyBlockMultiplicative (v0.114.0): its owner's block from cards doubles while their card plays this turn that gained block,
/// other than the play in progress, are fewer than its amount. The plays are counted once each (DistinctBy the BlockGainedEntry's CardPlay), so a
/// card gaining block twice is one play; before v0.114.0 each gain counted.
/// </summary>
public class UnmovableTests
{
    private static SimState Unmovable(int amount)
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 100) } };
        state.You.Powers = state.You.Powers.With(PowerId.Unmovable, amount);
        return state;
    }

    [Fact]
    public void BothGainsOfTheFirstPlayDouble()
    {
        // The play in progress never counts itself: its second gain doubles too.
        var after = After(Unmovable(amount: 1), Play(Card("Twice", SimCardType.Skill), target: Who.None, plays: 1, Block(baseBlock: 3), Block(baseBlock: 4))).Single();
        Assert.Equal(14, after.You.Block);
    }

    [Fact]
    public void APlayThatGainedBlockTwiceCountsOnce()
    {
        // Amount 2: the first play's two gains are one earlier play, so the second play still doubles.
        var state = Unmovable(amount: 2);
        var first = After(state, Play(Card("Twice", SimCardType.Skill), target: Who.None, plays: 1, Block(baseBlock: 3), Block(baseBlock: 4))).Single();
        Assert.Equal(14, first.You.Block);
        var second = After(first, Play(Card("Defend", SimCardType.Skill), target: Who.None, plays: 1, Block(baseBlock: 5))).Single();
        Assert.Equal(24, second.You.Block);
        // A third play: two earlier plays reach its amount, so no double.
        var third = After(second, Play(Card("Defend", SimCardType.Skill), target: Who.None, plays: 1, Block(baseBlock: 5))).Single();
        Assert.Equal(29, third.You.Block);
    }
}
