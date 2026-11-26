using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A play hook's test of `cardPlay.IsAutoPlay` (Brilliant Scarf's and Void Form's AfterCardPlayed, Pael's Eye's BeforeCardPlayed) is the play's own
/// flag (HookFact.PlayIsAutoPlay: SimCardPlay.AutoPlay, a card a card or a power played), and `cardPlay != null` holds in a play hook, which is
/// always given one. Brilliant Scarf's count, as its code has it: `if (cardPlay.IsAutoPlay) return;` then its count of the cards played this turn.
/// </summary>
public class PlayIsAutoPlayTests
{
    private const string Counter = "TestCountingPower";

    private static SimCardPlay Played(bool autoPlay) => new(new SimCard("Played", SimCardType.Attack), Owner: Who.You, Target: 0, PlayIndex: 0, PlayCount: 1, AutoPlay: autoPlay);

    private static int CountAfter(bool autoPlay)
    {
        var notAutoPlay = new HookNot(new HookFactValue(HookFact.PlayIsAutoPlay));
        var model = new ReadModel(new ModelSpec(Counter, ModelKind.Power, new[]
        {
            new HookProgram(HookName.AfterCardPlayed, new HookStep[] { new GainBlockStep(new HookConstant(1m), new HookConstant((int)SimProps.Unpowered)) { When = notAutoPlay } }),
        }));
        var state = new SimState { You = new SimPlayer { Hp = 50 }, Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 40 } } };
        state.You.Powers = state.You.Powers.With(new PowerId(Counter), amount: 1);
        return Assert.Single(model.AfterCardPlayed(state, Who.You, Played(autoPlay))).You.Block;
    }

    [Fact]
    public void AnAutoPlayIsTheGamesIsAutoPlay()
    {
        Assert.Equal(1, CountAfter(autoPlay: false));
        Assert.Equal(0, CountAfter(autoPlay: true));
    }
}
