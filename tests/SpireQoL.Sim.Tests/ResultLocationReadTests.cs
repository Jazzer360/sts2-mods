using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A played card's result location read from the game's code (decision #286): ReboundPower.ModifyCardPlayResultLocation, `card.Owner.Creature ==
/// base.Owner` and `location.pileType == PileType.Discard`, returns the location with its pile the draw pile and its position the top; its
/// AfterModifyingCardPlayResultLocation then uses a stack (PowerCmd.Decrement), and its AfterSideTurnEnd removes it as its owner's turn ends.
/// </summary>
public class ResultLocationReadTests
{
    private static SimState Board(int rebound)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 40 } } };
        state.You.Powers = state.You.Powers.With(PowerId.Rebound, rebound);
        state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 60, HandCount = 2, Energy = 2 });
        return state;
    }

    private static SimCardPlay Play(int owner, bool exhausts = false) =>
        new(new SimCard("Played", SimCardType.Skill) { Energy = 1, ExhaustsSelf = exhausts }, owner, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false);

    [Fact]
    public void YourCardBoundForTheDiscardPileGoesOnTopOfYourDrawPileForAStack()
    {
        var state = Board(rebound: 2);
        var model = SimModels.Get(PowerId.Rebound.Name)!;
        Assert.IsType<ReadModel>(model);
        Assert.Equal(SimResultLocation.DrawTop, model.ModifyCardPlayResultLocation(state, Who.You, Play(Who.You), SimResultLocation.Default));
        model.AfterModifyingCardPlayResultLocation(state, Who.You, Play(Who.You), SimResultLocation.DrawTop);
        Assert.Equal(1, SimEngine.PowerOn(state, Who.You, PowerId.Rebound));
    }

    [Fact]
    public void ATeammatesCardOrOneBoundElsewhereStaysWhereItIsAndUsesNoStack()
    {
        var state = Board(rebound: 2);
        var model = SimModels.Get(PowerId.Rebound.Name)!;
        // Another player's card (card.Owner.Creature != base.Owner), or one not bound for the discard pile (location.pileType != PileType.Discard).
        Assert.Equal(SimResultLocation.Default, model.ModifyCardPlayResultLocation(state, Who.You, Play(Who.Teammate(0)), SimResultLocation.Default));
        foreach (var elsewhere in new[] { SimResultLocation.Exhaust, SimResultLocation.Nowhere, SimResultLocation.HandTop, SimResultLocation.DrawTop })
            Assert.Equal(elsewhere, model.ModifyCardPlayResultLocation(state, Who.You, Play(Who.You), elsewhere));
        Assert.Equal(2, SimEngine.PowerOn(state, Who.You, PowerId.Rebound));
    }

    [Fact]
    public void APlayUsesAStackAndReboundGoesAsYourTurnEnds()
    {
        // Played through the engine (SimEngine.DecideCardPlay): one stack used by the play, which no turn-end step needs; then it goes as the turn ends.
        var state = Board(rebound: 2);
        var play = new SimPlay { Name = "Played", Card = Play(Who.You) };
        Assert.All(SimEngine.Run(state, play).Branches, branch => Assert.False(SimEngine.PowersOf(branch, Who.You).Has(PowerId.Rebound)));
        var exhausting = new SimPlay { Name = "Played", Card = Play(Who.You, exhausts: true) };
        Assert.All(SimEngine.Run(state, exhausting).Branches, branch => Assert.False(SimEngine.PowersOf(branch, Who.You).Has(PowerId.Rebound)));
    }
}
