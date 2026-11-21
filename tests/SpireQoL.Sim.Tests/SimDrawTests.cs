using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Draws from your draw pile (decision #90, decompile: CardPileCmd.Draw). A draw takes the known top card, else one card of
/// the rest, each equally likely: a branch per profile weighted by its count, so a pile of plain cards doesn't branch at all.
/// The card joins your hand (it acts there at end of turn, exhausts if Ethereal, Stampede can pick it). None into a full hand
/// (10, the card being played already out of it); the discard pile shuffles in when the draw pile is empty. Iteration draws
/// on the first Status drawn this turn, Pagestorm on each Ethereal card, before the next draw.
/// </summary>
public class SimDrawTests
{
    private static SimEnemy Enemy(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    private static readonly SimDrawCard Plain = new("plain");
    private static readonly SimDrawCard Burn = new("burn") { TurnEnd = new TurnEndCard("Burn", "Burn", 2) };
    private static readonly SimDrawCard EtherealBurn = new("ethereal burn") { TurnEnd = new TurnEndCard("Burn", "Burn", 2), Fading = true };

    private static SimDrawPile Pile(params (SimDrawCard Card, int Count)[] rest) => new() { Rest = rest.Select(cardCount => new SimDrawGroup(cardCount.Card, cardCount.Count)).ToList() };

    /// <summary>A card played from your hand that draws <paramref name="cards"/>.</summary>
    private static SimPlay Draws(int cards) => new()
    {
        Name = "Acrobatics", HandCard = 7, HandDelta = -1 + cards, Draws = cards,
        Body = { new CardEventAction(CardEvent.Drawn, cards) },
    };

    private static SimState You(int hand = 5)
    {
        var state = new SimState { Enemies = { Enemy(1, 50) } };
        state.You.HandCount = hand;
        state.You.Hp = 50;
        return state;
    }

    [Fact]
    public void APlainPileDoesntBranch()
    {
        var state = You();
        state.You.Draw = Pile((Plain, 10));
        var outcome = SimEngine.Run(state, Draws(2));
        Assert.Single(outcome.Branches);
        Assert.Equal(6, outcome.Branches[0].You.HandCount); // 5 - the card + 2 drawn
        Assert.Equal(8, outcome.Branches[0].You.Draw!.RestCount);
    }

    [Fact]
    public void ABurnDrawnActsAtEndOfTurn()
    {
        // One Burn among ten: a 10% chance it's drawn and burns you for 2.
        var state = You();
        state.You.Draw = Pile((Burn, 1), (Plain, 9));
        var outcome = SimEngine.Run(state, Draws(1));
        Assert.Equal(2, outcome.TakenMax);
        Assert.Equal(0, outcome.TakenMin);
        Assert.Equal(0.1, outcome.Branches.Where(branch => branch.Taken == 2).Sum(branch => branch.Weight), 9);
    }

    [Fact]
    public void WhichBurnYouDrawMatters()
    {
        // An Ethereal Burn and a plain one, Feel No Pain 3: the Ethereal one exhausts (3 block) before burning (2), so it costs
        // nothing; the plain one burns you. Drawing two from ten: 28 + 8 + 8 + 1 ways out of 45.
        var state = You();
        state.You.FeelNoPain = 3;
        state.You.Draw = Pile((EtherealBurn, 1), (Burn, 1), (Plain, 8));
        var outcome = SimEngine.Run(state, Draws(2));
        double P(Func<SimState, bool> holds) => outcome.Branches.Where(holds).Sum(branch => branch.Weight);
        Assert.Equal(4, outcome.Branches.Count);
        Assert.Equal(28 / 45.0, P(branch => branch.You.Drawn == 2 && branch.You.TurnEndCards.Count == 0), 9);
        Assert.Equal(8 / 45.0, P(branch => branch.You.TurnEndCards.Count == 1 && branch.You.Fading == 1), 9);
        Assert.Equal(8 / 45.0, P(branch => branch.You.TurnEndCards.Count == 1 && branch.You.Fading == 0), 9);
        Assert.Equal(1 / 45.0, P(branch => branch.You.TurnEndCards.Count == 2), 9);
        Assert.Equal(0, outcome.Branches.Single(branch => branch.You.TurnEndCards.Count == 1 && branch.You.Fading == 1).Taken); // 3 block takes the 2
        Assert.Equal(2, outcome.Branches.Single(branch => branch.You.TurnEndCards.Count == 1 && branch.You.Fading == 0).Taken);
    }

    [Fact]
    public void AFullHandDrawsNothing()
    {
        // Ten cards with the one being played: it has left the hand, so one draw fits; the second doesn't.
        var state = You(hand: 10);
        state.You.Draw = Pile((Burn, 5));
        var outcome = SimEngine.Run(state, Draws(2));
        Assert.All(outcome.Branches, branch => Assert.Single(branch.You.TurnEndCards));
        Assert.All(outcome.Branches, branch => Assert.Equal(4, branch.You.Draw!.RestCount)); // the second stays in the pile
    }

    [Fact]
    public void AnEmptyPileShufflesTheDiscardIn()
    {
        var state = You();
        state.You.Draw = new SimDrawPile { Discard = { new SimDrawGroup(Burn, 2) } };
        var outcome = SimEngine.Run(state, Draws(1));
        var branch = Assert.Single(outcome.Branches);
        Assert.Single(branch.You.TurnEndCards);
        Assert.Equal(1, branch.You.Draw!.RestCount);
        Assert.Equal(0, branch.You.Draw.DiscardCount);
    }

    [Fact]
    public void TheKnownTopComesFirst()
    {
        var state = You();
        state.You.Draw = new SimDrawPile { Top = { Burn }, Rest = { new SimDrawGroup(Plain, 9) } };
        var outcome = SimEngine.Run(state, Draws(1));
        var branch = Assert.Single(outcome.Branches);
        Assert.Single(branch.You.TurnEndCards);
    }

    [Fact]
    public void IterationDrawsOnTheFirstStatusOnly()
    {
        // Iteration 2: the first Status drawn this turn draws 2 more (a pile of Statuses: three in all). A second Status doesn't.
        var status = new SimDrawCard("status") { Status = true };
        var state = You();
        state.You.Powers = state.You.Powers.With(PowerId.Iteration, 2, 0);
        state.You.Draw = Pile((status, 6));
        var outcome = SimEngine.Run(state, Draws(1));
        Assert.All(outcome.Branches, branch => Assert.Equal(3, branch.You.Drawn));
        state.You.StatusDrawn = 1; // one already drawn this turn
        Assert.All(SimEngine.Run(state, Draws(1)).Branches, branch => Assert.Equal(1, branch.You.Drawn));
    }

    [Fact]
    public void PagestormDrawsOnEachEtherealCard()
    {
        var ethereal = new SimDrawCard("ethereal") { Ethereal = true };
        var state = You();
        state.You.Powers = state.You.Powers.With(PowerId.Pagestorm, 1, 0);
        state.You.Draw = Pile((ethereal, 2), (Plain, 1));
        var outcome = SimEngine.Run(state, Draws(1));
        // The Plain first (1/3): no chain. An Ethereal first (2/3) draws one more: the other Ethereal (1/2), which draws the Plain,
        // or the Plain (1/2).
        Assert.Equal(1.0, outcome.Branches.Sum(branch => branch.Weight), 9);
        Assert.Equal(1 / 3.0, outcome.Branches.Where(branch => branch.You.Drawn == 1).Sum(branch => branch.Weight), 9);
        Assert.Equal(1 / 3.0, outcome.Branches.Where(branch => branch.You.Drawn == 2).Sum(branch => branch.Weight), 9);
        Assert.Equal(1 / 3.0, outcome.Branches.Where(branch => branch.You.Drawn == 3).Sum(branch => branch.Weight), 9);
    }

    [Fact]
    public void IterationOnAFullHandStops()
    {
        // Nine in hand (the played card out): the Status fills the hand, so Iteration's draw has no room.
        var status = new SimDrawCard("status") { Status = true };
        var state = You(hand: 10);
        state.You.Powers = state.You.Powers.With(PowerId.Iteration, 2, 0);
        state.You.Draw = Pile((status, 6));
        Assert.All(SimEngine.Run(state, Draws(1)).Branches, branch => Assert.Equal(1, branch.You.Drawn));
    }

    [Fact]
    public void StampedeCanPickADrawnAttack()
    {
        // Pommel Strike-like pick draws; the drawn card is an attack Stampede's second pick may take. Its own id in hand.
        var enemy = Enemy(1, 30, 5);
        var state = new SimState { Enemies = { enemy } };
        state.You.HandCount = 1;
        var strike = new SimHandCard { Id = 0, Name = "Strike", IsAttack = true, Cost = 1, Plays = { new SimPlay { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 1, HitDamage = { [1] = 6 }, Hits = 1, HandDelta = -1 } } };
        state.You.Draw = Pile((new SimDrawCard("strike") { Pickable = true, Auto = strike }, 3));
        state.You.Stampede = 2;
        var pommel = new SimPlay
        {
            Name = "PommelStrike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 1, HitDamage = { [1] = 9 }, Hits = 1,
            HandCard = 15, HandDelta = 0, Draws = 1, Body = { new CardEventAction(CardEvent.Drawn, 1) },
        };
        state.You.AutoHand = new() { new SimHandCard { Id = 15, Name = "PommelStrike", IsAttack = true, Cost = 1, Plays = { pommel } } };
        var outcome = SimEngine.Run(state);
        var branch = Assert.Single(outcome.Branches);
        Assert.Equal(2, branch.AutoPlayed);
        Assert.False(branch.AutoPlaysUnknown);
        Assert.Equal(30 - 9 - 6, branch.Enemy(1)!.Hp + 0); // both hit
    }

    [Fact]
    public void WithoutAPileDrawsAreOnlyCounted()
    {
        var state = You();
        var outcome = SimEngine.Run(state, Draws(2));
        var branch = Assert.Single(outcome.Branches);
        Assert.Equal(6, branch.You.HandCount); // the hand delta, as before
        Assert.Equal(0, branch.You.Drawn);
    }

    [Fact]
    public void DrawsInAnyOrderMergeAndKeepTheirShares()
    {
        // Four draws from Ethereal Burns, Burns and plain cards: the same cards drawn in another order is the same hand, one branch.
        var state = You();
        state.You.FeelNoPain = 3;
        state.You.Draw = Pile((EtherealBurn, 2), (Burn, 2), (Plain, 6));
        var outcome = SimEngine.Run(state, Draws(4));
        Assert.False(outcome.Truncated);
        Assert.True(outcome.Branches.Count > 2);
        Assert.Equal(1.0, outcome.Branches.Sum(branch => branch.Weight), 9);
        Assert.Equal(outcome.Branches.Count, outcome.Branches.Select(branch => branch.Key()).Distinct().Count());
    }

    private static readonly SimDrawCard Invincible = new("invincible") { InvincibleBlock = 10 };

    [Fact]
    public void IAmInvincibleKnownOnTopPlaysItself()
    {
        // Known on top (placed there): its 10 block, for certain, and it goes to the discard pile.
        var state = You();
        state.Enemies[0].Hits = new() { 12 };
        state.You.Draw = new SimDrawPile { Top = { Invincible }, Rest = { new SimDrawGroup(Plain, 5) } };
        var outcome = SimEngine.Run(state);
        var branch = Assert.Single(outcome.Branches);
        Assert.Equal(2, branch.Taken);
        Assert.Equal(1, branch.AutoPlayed);
        Assert.Equal(1, branch.You.Draw!.DiscardCount);
        Assert.Empty(branch.You.Draw.Top);
    }

    [Fact]
    public void IAmInvincibleSomewhereInThePileIsAChance()
    {
        // Two copies among ten, order unknown: a 20% chance one is on top.
        var state = You();
        state.Enemies[0].Hits = new() { 12 };
        state.You.Draw = Pile((Invincible, 2), (Plain, 8));
        var outcome = SimEngine.Run(state);
        Assert.Equal(0.2, outcome.Branches.Where(branch => branch.Taken == 2).Sum(branch => branch.Weight), 9);
        Assert.Equal(0.8, outcome.Branches.Where(branch => branch.Taken == 12).Sum(branch => branch.Weight), 9);
    }

    [Fact]
    public void ACardKnownOnTopKeepsIAmInvincibleBelow()
    {
        var state = You();
        state.Enemies[0].Hits = new() { 12 };
        state.You.Draw = new SimDrawPile { Top = { Plain }, Rest = { new SimDrawGroup(Invincible, 3) } };
        var branch = Assert.Single(SimEngine.Run(state).Branches);
        Assert.Equal(12, branch.Taken);
        Assert.Equal(0, branch.AutoPlayed);
    }

    [Fact]
    public void DrawingTheKnownTopFirstRevealsWhatsBelow()
    {
        // Draw 1 with I Am Invincible known on top: it's drawn, so the new top is random again (no block for certain).
        var state = You();
        state.Enemies[0].Hits = new() { 12 };
        state.You.Draw = new SimDrawPile { Top = { Invincible }, Rest = { new SimDrawGroup(Plain, 4) } };
        var branch = Assert.Single(SimEngine.Run(state, Draws(1)).Branches);
        Assert.Equal(12, branch.Taken);
    }

    [Fact]
    public void AShuffleBringsTheOnePerfectFitCardToTheTop()
    {
        // The draw pile is empty: the discard pile shuffles in, and the card enchanted with Perfect Fit goes on top.
        var fit = new SimDrawCard("fit burn") { TurnEnd = new TurnEndCard("Burn", "Burn", 2), PerfectFit = true };
        var state = You();
        state.You.Draw = new SimDrawPile { Discard = { new SimDrawGroup(fit, 1), new SimDrawGroup(Plain, 9) } };
        var branch = Assert.Single(SimEngine.Run(state, Draws(1)).Branches);
        Assert.Single(branch.You.TurnEndCards); // drew it for certain
        Assert.Equal(2, branch.Taken);
    }

    [Fact]
    public void TwoPerfectFitCardsLeaveTheTopUnknown()
    {
        var fit = new SimDrawCard("fit burn") { TurnEnd = new TurnEndCard("Burn", "Burn", 2), PerfectFit = true };
        var state = You();
        state.You.Draw = new SimDrawPile { Discard = { new SimDrawGroup(fit, 2), new SimDrawGroup(Plain, 8) } };
        var outcome = SimEngine.Run(state, Draws(1));
        Assert.Equal(0.2, outcome.Branches.Where(branch => branch.Taken == 2).Sum(branch => branch.Weight), 9);
    }

    [Fact]
    public void ACardMadeIntoAFullHandGoesToTheDiscardPile()
    {
        // Nine cards and a play that makes three (Blade Dance-like): the hand stops at 10.
        var state = You(hand: 9);
        state.You.Draw = Pile((Plain, 5));
        var make = new SimPlay { Name = "BladeDance", HandCard = 7, HandDelta = -1 + 3 };
        var branch = Assert.Single(SimEngine.Run(state, make).Branches);
        Assert.Equal(10, branch.You.HandCount);
    }
}

