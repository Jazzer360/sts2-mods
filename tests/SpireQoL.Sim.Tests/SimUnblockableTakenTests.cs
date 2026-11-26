using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Incoming Damage shows HP loss no block stops (Regret, Beckon, Bad Luck: CardsActInHand; Demise) on its own line, under what
/// gets through your block: SimState.UnblockableTaken is that part of Taken, after the HP-loss modifiers.
/// </summary>
public class SimUnblockableTakenTests
{
    private static SimEnemy E(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    [Fact]
    public void RegretIsCountedApartFromTheHitsThroughYourBlock()
    {
        // Regret with 3 cards in hand (3, unblockable) and a hit of 10 against 6 block: 4 through, 7 in all.
        var state = new SimState { Enemies = { E(id: 1, hp: 30, hits: 10) } };
        state.You.Block = 6;
        state.You.HandCount = 3;
        state.You.TurnEndCards.Add(new TurnEndCard("Regret", "Regret", Amount: 0));
        state.You.Cards = state.You.Cards.With(CardId.Regret, amount: 1); // its BeforeSideTurnEnd counts the hand (RegretCard)
        var branch = SimEngine.Run(state).Worst;
        Assert.Equal(7, branch.Taken);
        Assert.Equal(3, branch.UnblockableTaken);
    }

    [Fact]
    public void BlockDoesntTouchIt()
    {
        // Beckon's 6 with 20 block and nothing else coming: all 6 lost, all of it unblockable.
        var state = new SimState { Enemies = { E(id: 1, hp: 30) } };
        state.You.Block = 20;
        state.You.TurnEndCards.Add(new TurnEndCard("Beckon", "Beckon", Amount: 6));
        var branch = SimEngine.Run(state).Worst;
        Assert.Equal(6, branch.Taken);
        Assert.Equal(6, branch.UnblockableTaken);
    }

    /// <summary>
    /// Beckon is a Status you can play (cost 1): played, it isn't in your hand as the turn ends, so its 6 never comes. The turn-end list read from
    /// your hand had it all the same (2.0.0's audit: the Play Preview showed 13, the board after the play gave 7).
    /// </summary>
    [Fact]
    public void ABeckonYouPlayIsntInYourHandAsTheTurnEnds()
    {
        var state = new SimState { Enemies = { E(id: 1, hp: 30, hits: 7) } };
        state.You.HandCount = 2;
        state.You.TurnEndCards.Add(new TurnEndCard("Beckon", "Beckon", Amount: 6));
        state.You.TurnEndCards.Add(new TurnEndCard("Beckon", "Beckon", Amount: 6));
        var play = new SimPlay { Name = "Beckon", HandCard = 7, HandDelta = -1, TurnEndLeaving = new TurnEndCard("Beckon", "Beckon", Amount: 6),
            Card = new SimCardPlay(new SimCard("Beckon", SimCardType.Status), Who.You, Target: Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
        var branch = Assert.Single(SimEngine.Run(state, play).Branches);
        // The hit's 7 and the other Beckon's 6.
        Assert.Equal(13, branch.Taken);
        Assert.Equal(6, branch.UnblockableTaken);
    }

    [Fact]
    public void ItsWhatYouLoseAfterTungstenRod()
    {
        // Bad Luck's 13 through Tungsten Rod: 12 lost.
        var state = new SimState { Enemies = { E(id: 1, hp: 30) } };
        state.You.TungstenRod = 1;
        state.You.TurnEndCards.Add(new TurnEndCard("BadLuck", "Bad Luck", Amount: 13));
        var branch = SimEngine.Run(state).Worst;
        Assert.Equal(12, branch.UnblockableTaken);
        Assert.Equal(12, branch.Taken);
    }

    [Fact]
    public void BurnIsBlockable()
    {
        var state = new SimState { Enemies = { E(id: 1, hp: 30) } };
        state.You.TurnEndCards.Add(new TurnEndCard("Burn", "Burn", Amount: 2));
        var branch = SimEngine.Run(state).Worst;
        Assert.Equal(2, branch.Taken);
        Assert.Equal(0, branch.UnblockableTaken);
    }
}
