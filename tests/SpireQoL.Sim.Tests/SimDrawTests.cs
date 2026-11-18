using SpireQoL.Sim;
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

    private static SimDrawPile Pile(params (SimDrawCard Card, int Count)[] rest) => new() { Rest = rest.Select(r => new SimDrawGroup(r.Card, r.Count)).ToList() };

    /// <summary>A card played from your hand that draws <paramref name="n"/>.</summary>
    private static SimPlay Draws(int n) => new()
    {
        Name = "Acrobatics", HandCard = 7, HandDelta = -1 + n, Draws = n,
        Body = { new CardEventAction(CardEvent.Drawn, n) },
    };

    private static SimState You(int hand = 5)
    {
        var s = new SimState { Enemies = { Enemy(1, 50) } };
        s.P.HandCount = hand;
        s.P.Hp = 50;
        return s;
    }

    [Fact]
    public void APlainPileDoesntBranch()
    {
        var s = You();
        s.P.Draw = Pile((Plain, 10));
        var o = SimEngine.Run(s, Draws(2));
        Assert.Single(o.Branches);
        Assert.Equal(6, o.Branches[0].P.HandCount); // 5 - the card + 2 drawn
        Assert.Equal(8, o.Branches[0].P.Draw!.RestCount);
    }

    [Fact]
    public void ABurnDrawnActsAtEndOfTurn()
    {
        // One Burn among ten: a 10% chance it's drawn and burns you for 2.
        var s = You();
        s.P.Draw = Pile((Burn, 1), (Plain, 9));
        var o = SimEngine.Run(s, Draws(1));
        Assert.Equal(2, o.TakenMax);
        Assert.Equal(0, o.TakenMin);
        Assert.Equal(0.1, o.Branches.Where(b => b.Taken == 2).Sum(b => b.Weight), 9);
    }

    [Fact]
    public void WhichBurnYouDrawMatters()
    {
        // An Ethereal Burn and a plain one, Feel No Pain 3: the Ethereal one exhausts (3 block) before burning (2), so it costs
        // nothing; the plain one burns you. Drawing two from ten: 28 + 8 + 8 + 1 ways out of 45.
        var s = You();
        s.P.FeelNoPain = 3;
        s.P.Draw = Pile((EtherealBurn, 1), (Burn, 1), (Plain, 8));
        var o = SimEngine.Run(s, Draws(2));
        double P(Func<SimState, bool> f) => o.Branches.Where(f).Sum(b => b.Weight);
        Assert.Equal(4, o.Branches.Count);
        Assert.Equal(28 / 45.0, P(b => b.P.Drawn == 2 && b.P.TurnEndCards.Count == 0), 9);
        Assert.Equal(8 / 45.0, P(b => b.P.TurnEndCards.Count == 1 && b.P.Fading == 1), 9);
        Assert.Equal(8 / 45.0, P(b => b.P.TurnEndCards.Count == 1 && b.P.Fading == 0), 9);
        Assert.Equal(1 / 45.0, P(b => b.P.TurnEndCards.Count == 2), 9);
        Assert.Equal(0, o.Branches.Single(b => b.P.TurnEndCards.Count == 1 && b.P.Fading == 1).Taken); // 3 block takes the 2
        Assert.Equal(2, o.Branches.Single(b => b.P.TurnEndCards.Count == 1 && b.P.Fading == 0).Taken);
    }

    [Fact]
    public void AFullHandDrawsNothing()
    {
        // Ten cards with the one being played: it has left the hand, so one draw fits; the second doesn't.
        var s = You(hand: 10);
        s.P.Draw = Pile((Burn, 5));
        var o = SimEngine.Run(s, Draws(2));
        Assert.All(o.Branches, b => Assert.Equal(1, b.P.TurnEndCards.Count));
        Assert.All(o.Branches, b => Assert.Equal(4, b.P.Draw!.RestCount)); // the second stays in the pile
    }

    [Fact]
    public void AnEmptyPileShufflesTheDiscardIn()
    {
        var s = You();
        s.P.Draw = new SimDrawPile { Discard = { new SimDrawGroup(Burn, 2) } };
        var o = SimEngine.Run(s, Draws(1));
        var b = Assert.Single(o.Branches);
        Assert.Single(b.P.TurnEndCards);
        Assert.Equal(1, b.P.Draw!.RestCount);
        Assert.Equal(0, b.P.Draw.DiscardCount);
    }

    [Fact]
    public void TheKnownTopComesFirst()
    {
        var s = You();
        s.P.Draw = new SimDrawPile { Top = { Burn }, Rest = { new SimDrawGroup(Plain, 9) } };
        var o = SimEngine.Run(s, Draws(1));
        var b = Assert.Single(o.Branches);
        Assert.Single(b.P.TurnEndCards);
    }

    [Fact]
    public void IterationDrawsOnTheFirstStatusOnly()
    {
        // Iteration 2: the first Status drawn this turn draws 2 more (a pile of Statuses: three in all). A second Status doesn't.
        var status = new SimDrawCard("status") { Status = true };
        var s = You();
        s.P.Powers = s.P.Powers.With("IterationPower", 2, 0);
        s.P.Draw = Pile((status, 6));
        var o = SimEngine.Run(s, Draws(1));
        Assert.All(o.Branches, b => Assert.Equal(3, b.P.Drawn));
        s.P.StatusDrawn = 1; // one already drawn this turn
        Assert.All(SimEngine.Run(s, Draws(1)).Branches, b => Assert.Equal(1, b.P.Drawn));
    }

    [Fact]
    public void PagestormDrawsOnEachEtherealCard()
    {
        var ethereal = new SimDrawCard("ethereal") { Ethereal = true };
        var s = You();
        s.P.Powers = s.P.Powers.With("PagestormPower", 1, 0);
        s.P.Draw = Pile((ethereal, 2), (Plain, 1));
        var o = SimEngine.Run(s, Draws(1));
        // The Plain first (1/3): no chain. An Ethereal first (2/3) draws one more: the other Ethereal (1/2), which draws the Plain,
        // or the Plain (1/2).
        Assert.Equal(1.0, o.Branches.Sum(b => b.Weight), 9);
        Assert.Equal(1 / 3.0, o.Branches.Where(b => b.P.Drawn == 1).Sum(b => b.Weight), 9);
        Assert.Equal(1 / 3.0, o.Branches.Where(b => b.P.Drawn == 2).Sum(b => b.Weight), 9);
        Assert.Equal(1 / 3.0, o.Branches.Where(b => b.P.Drawn == 3).Sum(b => b.Weight), 9);
    }

    [Fact]
    public void IterationOnAFullHandStops()
    {
        // Nine in hand (the played card out): the Status fills the hand, so Iteration's draw has no room.
        var status = new SimDrawCard("status") { Status = true };
        var s = You(hand: 10);
        s.P.Powers = s.P.Powers.With("IterationPower", 2, 0);
        s.P.Draw = Pile((status, 6));
        Assert.All(SimEngine.Run(s, Draws(1)).Branches, b => Assert.Equal(1, b.P.Drawn));
    }

    [Fact]
    public void StampedeCanPickADrawnAttack()
    {
        // Pommel Strike-like pick draws; the drawn card is an attack Stampede's second pick may take. Its own id in hand.
        var e = Enemy(1, 30, 5);
        var s = new SimState { Enemies = { e } };
        s.P.HandCount = 1;
        var strike = new SimHandCard { Id = 0, Name = "Strike", IsAttack = true, Cost = 1, Plays = { new SimPlay { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 1, HitDamage = { [1] = 6 }, Hits = 1, HandDelta = -1 } } };
        s.P.Draw = Pile((new SimDrawCard("strike") { Pickable = true, Auto = strike }, 3));
        s.P.Stampede = 2;
        var pommel = new SimPlay
        {
            Name = "PommelStrike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 1, HitDamage = { [1] = 9 }, Hits = 1,
            HandCard = 15, HandDelta = 0, Draws = 1, Body = { new CardEventAction(CardEvent.Drawn, 1) },
        };
        s.P.AutoHand = new() { new SimHandCard { Id = 15, Name = "PommelStrike", IsAttack = true, Cost = 1, Plays = { pommel } } };
        var o = SimEngine.Run(s);
        var b = Assert.Single(o.Branches);
        Assert.Equal(2, b.AutoPlayed);
        Assert.False(b.AutoPlaysUnknown);
        Assert.Equal(30 - 9 - 6, b.Enemy(1)!.Hp + 0); // both hit
    }

    [Fact]
    public void WithoutAPileDrawsAreOnlyCounted()
    {
        var s = You();
        var o = SimEngine.Run(s, Draws(2));
        var b = Assert.Single(o.Branches);
        Assert.Equal(6, b.P.HandCount); // the hand delta, as before
        Assert.Equal(0, b.P.Drawn);
    }

    [Fact]
    public void SlicesStillAddUpWithDraws()
    {
        var s = You();
        s.P.FeelNoPain = 3;
        s.P.Draw = Pile((EtherealBurn, 2), (Burn, 2), (Plain, 6));
        var play = Draws(4);
        var unlimited = SimExploration.Of(s, play, int.MaxValue).ExploreAll();
        var sliced = SimExploration.Of(s, play, 2).ExploreAll();
        Assert.True(unlimited.Branches.Count > 2);
        Assert.Equal(1.0, sliced.Branches.Sum(b => b.Weight), 9);
        Assert.Equal(unlimited.TakenMax, sliced.TakenMax);
        Assert.Equal(unlimited.YouDieChance().Low, sliced.YouDieChance().Low, 9);
    }
}
