using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Cards a play takes from your draw or discard pile into your hand (TakeToHandAction; decompile v0.113.1: Wish, Secret Weapon, Secret
/// Technique, Locked In, Seeker Strike and Ki Chant through CardSelectCmd.FromCombatPile then CardPileCmd.Add, Anointed through
/// TakeRandom): the card leaves the pile, so a later draw in the same simulation can't draw it, and joins your hand as it is (a Burn
/// taken acts at the end of your turn). Anointed's pick is a roll; the others are yours.
/// </summary>
public class SimTakeToHandTests
{
    private static readonly SimDrawCard Plain = new("plain");
    private static readonly SimDrawCard Burn = new("burn") { TurnEnd = new TurnEndCard("Burn", "Burn", Amount: 2) };

    private static SimState You(params (SimDrawCard Card, int Count)[] rest)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50 } } };
        state.You.HandCount = 5;
        state.You.Hp = 50;
        state.You.Draw = new SimDrawPile { Rest = rest.Select(cardCount => new SimDrawGroup(cardCount.Card, cardCount.Count)).ToList() };
        return state;
    }

    private static SimPlay Takes(TakeToHandAction take, int drawsAfter = 0) => new()
    {
        Name = "Wish", HandCard = 7, HandDelta = -1 + take.Count + drawsAfter, Draws = drawsAfter,
        Body = { take, new CardEventAction(CardEvent.Drawn, drawsAfter) },
    };

    [Fact]
    public void TheCardTakenLeavesThePileAndJoinsYourHand()
    {
        // Wish takes the Burn (the only card it can): it burns you at the end of your turn, and the pile is down to the plain cards.
        var outcome = SimEngine.Run(You((Plain, 3), (Burn, 1)), Takes(new TakeToHandAction(Count: 1, new Dictionary<string, int> { ["burn"] = 1 })));
        var branch = Assert.Single(outcome.Branches);
        Assert.Equal(3, branch.You.Draw!.RestCount);
        Assert.DoesNotContain(branch.You.Draw.Rest, group => group.Card.Key == "burn");
        Assert.Equal(2, branch.You.Taken);
    }

    [Fact]
    public void ALaterDrawCantDrawTheCardTaken()
    {
        // Taking the one Burn, then drawing: every draw is plain.
        var outcome = SimEngine.Run(You((Plain, 3), (Burn, 1)), Takes(new TakeToHandAction(Count: 1, new Dictionary<string, int> { ["burn"] = 1 }), drawsAfter: 1));
        Assert.Single(outcome.Branches);
        Assert.Equal(2, outcome.Branches[0].You.Draw!.RestCount);
    }

    [Fact]
    public void ARandomTakeIsABranchPerProfileByItsShare()
    {
        // Anointed with room for one of four Rares (three plain, one Burn): the Burn 1/4.
        var outcome = SimEngine.Run(You((Plain, 3), (Burn, 1)),
            Takes(new TakeToHandAction(Count: 1, new Dictionary<string, int> { ["plain"] = 3, ["burn"] = 1 }) { Random = true }));
        Assert.Equal(2, outcome.Branches.Count);
        var burnt = outcome.Chance(branch => branch.You.Taken > 0);
        Assert.Equal(0.25, burnt.Low, precision: 9);
        Assert.Equal(0.25, burnt.High, precision: 9);
    }

    [Fact]
    public void TakingEveryOneItCanIsntAPick()
    {
        var outcome = SimEngine.Run(You((Plain, 3), (Burn, 1)),
            Takes(new TakeToHandAction(Count: 4, new Dictionary<string, int> { ["plain"] = 3, ["burn"] = 1 }) { Random = true }));
        var branch = Assert.Single(outcome.Branches);
        Assert.Equal(0, branch.You.Draw!.Count);
        Assert.Null(branch.Unmodelled);
    }

    [Fact]
    public void YourPickTakesAPlainCardWhenThereIsOne()
    {
        // Wish with a Burn and plain cards to choose from (decision #167: your pick is your later decision): a plain card, so the
        // Burn stays in the pile. With only cards the simulation follows to choose from, and different ones, it's unknown.
        var outcome = SimEngine.Run(You((Plain, 3), (Burn, 1)),
            Takes(new TakeToHandAction(Count: 1, new Dictionary<string, int> { ["plain"] = 3, ["burn"] = 1 })));
        var taken = Assert.Single(outcome.Branches);
        Assert.Null(taken.Unmodelled);
        Assert.Equal(1, taken.You.Draw!.Rest.Single(group => group.Card.Key == "burn").Count);
        var ethereal = new SimDrawCard("ethereal") { Fading = true };
        var unknown = SimEngine.Run(You((ethereal, 1), (Burn, 1)), Takes(new TakeToHandAction(Count: 1, new Dictionary<string, int> { ["ethereal"] = 1, ["burn"] = 1 })));
        Assert.Contains(unknown.Branches, branch => branch.Unmodelled != null);
        // Among plain cards only, it's the same whichever you take.
        var plain = SimEngine.Run(You((Plain, 3), (Burn, 1)), Takes(new TakeToHandAction(Count: 1, new Dictionary<string, int> { ["plain"] = 3 })));
        Assert.Null(Assert.Single(plain.Branches).Unmodelled);
        Assert.Equal(1, plain.Branches[0].You.Draw!.Rest.Single(group => group.Card.Key == "burn").Count);
    }

    [Fact]
    public void ACardTakenFromTheDiscardPileLeavesIt()
    {
        // Hologram takes the Burn from your discard pile: it burns you, and a shuffle later can't bring it back.
        var state = You((Plain, 2));
        state.You.Draw = new SimDrawPile { Rest = state.You.Draw!.Rest, Discard = new() { new SimDrawGroup(Burn, 1), new SimDrawGroup(Plain, 1) } };
        var outcome = SimEngine.Run(state, Takes(new TakeToHandAction(Count: 1, new Dictionary<string, int> { ["burn"] = 1 }) { FromDiscard = true }));
        var branch = Assert.Single(outcome.Branches);
        Assert.Equal(1, branch.You.Draw!.DiscardCount);
        Assert.Equal(2, branch.You.Taken);
    }
}
