using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Cards in your piles a card plays (AutoPlayCardsAction, decision #360; decompile v0.114.0: CardCmd.AutoPlay from Uproar's draw pile Attack, Beat
/// Down's discard pile Attacks, Eidolon's and Knife Trap's exhaust pile cards, The Fool's and Cho Chorus' hand cards): the cards its filter lets through,
/// a random one a branch per card by its count, a list each in turn, each played free with its own play, then gone where a played card goes.
/// </summary>
public class SimAutoPlayCardsTests
{
    private const string Key = "Card#0";

    private static SimPlay Hits(int damage, int target = 1, int handCard = 0, bool exhausts = false)
    {
        var play = new SimPlay
        {
            Name = "StrikeIronclad", IsAttack = true, AutoPlay = true, HandDelta = handCard == 0 ? 0 : -1, HandCard = handCard, TargetId = target,
            Card = new SimCardPlay(new SimCard("StrikeIronclad", SimCardType.Attack) { HandId = handCard, ExhaustsSelf = exhausts }, Who.You, target, PlayIndex: 0, PlayCount: 1, AutoPlay: true),
            Body = { new AttackAction(Hits: 1, SimPlay.Target.Single, new DamageSpec(Base: 0m, SimProps.Move) { BaseOn = new Dictionary<int, decimal> { [target] = damage }, Fixed = true }) },
        };
        if (exhausts) play.After.Add(new CardEventAction(CardEvent.Exhausted, Count: 1));
        return play;
    }

    private static SimPlay Blocks(int block, int handCard = 0) => new()
    {
        Name = "DefendIronclad", AutoPlay = true, HandDelta = handCard == 0 ? 0 : -1, HandCard = handCard, TargetId = Who.None,
        Card = new SimCardPlay(new SimCard("DefendIronclad", SimCardType.Skill) { HandId = handCard }, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: true),
        Body = { new GainBlockAction(Times: 1, block, SimProps.Move) },
    };

    private static readonly IReadOnlyList<string> Listed = new[] { Key };
    private static readonly SimDrawCard Strike = new("strike") { AutoPlayedBy = Listed, FromPile = new SimHandCard { Name = "StrikeIronclad", IsAttack = true, Plays = { Hits(damage: 6) } } };
    private static readonly SimDrawCard Defend = new("defend") { FromPile = new SimHandCard { Name = "DefendIronclad", Plays = { Blocks(block: 5) } } };

    private static SimState Board(List<SimDrawGroup>? rest = null, List<SimDrawGroup>? discard = null)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50 }, new SimEnemy { Id = 2, Name = "E2", Hp = 50 } } };
        state.You.HandCount = 5;
        state.You.Hp = 50;
        state.You.PileAutoPlays = new SimPileAutoPlays(Listed) { FromHand = true };
        state.You.Draw = new SimDrawPile { Rest = rest ?? new(), Discard = discard ?? new() };
        return state;
    }

    private static SimPlay Card(AutoPlayCardsAction action, int target = Who.None) => new()
    {
        Name = "Card", HandCard = 7, HandDelta = -1, TargetId = target,
        Card = new SimCardPlay(new SimCard("Card", SimCardType.Skill) { HandId = 7 }, Who.You, target, PlayIndex: 0, PlayCount: 1, AutoPlay: false),
        Body = { action },
    };

    [Fact]
    public void ARandomCardOfTheDrawPileItsFilterLetsThroughPlaysAtARandomEnemy()
    {
        // Uproar: an Attack of your draw pile (a Strike, 1 of the 3 cards there, the only Attack), at either enemy (CardCmd.AutoPlay given none).
        var strike = Strike with { FromPile = new SimHandCard { Name = "StrikeIronclad", IsAttack = true, Plays = { Hits(damage: 6, target: 1), Hits(damage: 6, target: 2) } } };
        var outcome = SimEngine.Run(Board(rest: new() { new SimDrawGroup(strike, 1), new SimDrawGroup(Defend, 2) }), Card(new AutoPlayCardsAction(Key, SimPile.Draw, AutoPlayWay.Random, Count: 1)));
        Assert.Equal(2, outcome.Branches.Count);
        Assert.All(outcome.Branches, branch => Assert.Equal(94, branch.Enemy(id: 1)!.Hp + branch.Enemy(id: 2)!.Hp));
        Assert.All(outcome.Branches, branch => Assert.Equal(2, branch.You.Draw!.Count));
        Assert.All(outcome.Branches, branch => Assert.Equal(1, branch.You.Draw!.DiscardCount));
        Assert.All(outcome.Branches, branch => Assert.Null(branch.Unmodelled));
    }

    [Fact]
    public void ARandomPickIsABranchPerProfileByItsCount()
    {
        var other = Strike with { Key = "other", FromPile = new SimHandCard { Name = "Other", IsAttack = true, Plays = { Hits(damage: 10) } } };
        var outcome = SimEngine.Run(Board(rest: new() { new SimDrawGroup(Strike, 3), new SimDrawGroup(other, 1) }), Card(new AutoPlayCardsAction(Key, SimPile.Draw, AutoPlayWay.Random, Count: 1)));
        var ten = outcome.Chance(branch => branch.Enemy(id: 1)!.Hp == 40);
        Assert.Equal(0.25, ten.Low, precision: 9);
    }

    [Fact]
    public void WithNoCardItsFilterLetsThroughItsFallbackFindsOneThatGoesUnplayed()
    {
        // Uproar with only an Unplayable Attack: found by its second filter, it goes to the discard pile unplayed (CardCmd.AutoPlay).
        var unplayable = new SimDrawCard("unplayable") { AutoPlayedBy = new[] { Key + "/else" }, FromPile = new SimHandCard { Name = "Unplayable" } };
        var outcome = SimEngine.Run(Board(rest: new() { new SimDrawGroup(unplayable, 1), new SimDrawGroup(Defend, 1) }),
            Card(new AutoPlayCardsAction(Key, SimPile.Draw, AutoPlayWay.Random, Count: 1) { Fallback = Key + "/else" }));
        var branch = Assert.Single(outcome.Branches);
        Assert.Equal(1, branch.You.Draw!.Count);
        Assert.Equal(1, branch.You.Draw.DiscardCount);
        Assert.Null(branch.Unmodelled);
    }

    [Fact]
    public void NoCardThereIsNoPlay()
    {
        var outcome = SimEngine.Run(Board(rest: new() { new SimDrawGroup(Defend, 2) }), Card(new AutoPlayCardsAction(Key, SimPile.Draw, AutoPlayWay.Random, Count: 1)));
        var branch = Assert.Single(outcome.Branches);
        Assert.Equal(50, branch.Enemy(id: 1)!.Hp);
        Assert.Equal(0, branch.AutoPlayed);
    }

    [Fact]
    public void AShuffledListOfTheDiscardPilePlaysAtMostItsCountNoCardTwice()
    {
        // Beat Down: 3 of the discard pile's Attacks, shuffled: its 2 Strikes, each once (played, each goes back to the discard pile).
        var outcome = SimEngine.Run(Board(discard: new() { new SimDrawGroup(Strike, 2), new SimDrawGroup(Defend, 1) }), Card(new AutoPlayCardsAction(Key, SimPile.Discard, AutoPlayWay.Shuffled, Count: 3)));
        var branch = Assert.Single(outcome.Branches);
        Assert.Equal(38, branch.Enemy(id: 1)!.Hp);
        Assert.Equal(3, branch.You.Draw!.DiscardCount);
        Assert.Equal(2, branch.AutoPlayed);
        Assert.Null(branch.You.PileAutoPlays!.Listed);
    }

    [Fact]
    public void AShuffledListIsABranchPerCardLeftByItsCount()
    {
        var other = Strike with { Key = "other", FromPile = new SimHandCard { Name = "Other", IsAttack = true, Plays = { Hits(damage: 10) } } };
        var outcome = SimEngine.Run(Board(discard: new() { new SimDrawGroup(Strike, 2), new SimDrawGroup(other, 1) }), Card(new AutoPlayCardsAction(Key, SimPile.Discard, AutoPlayWay.Shuffled, Count: 1)));
        var ten = outcome.Chance(branch => branch.Enemy(id: 1)!.Hp == 40);
        Assert.Equal(1.0 / 3, ten.Low, precision: 9);
    }

    [Fact]
    public void TheExhaustPilesListedCardsPlayAtThePlaysTargetAndThoseThatExhaustGoBack()
    {
        // Knife Trap+ at enemy 2: each Shiv in the exhaust pile, upgraded first, at its target; a Shiv exhausts itself, so it's back there after.
        var shiv = new SimDrawCard("shiv")
        {
            AutoPlayedBy = Listed, ExhaustsWhenPlayed = true,
            FromPile = new SimHandCard { Name = "Shiv", IsAttack = true, Plays = { Hits(damage: 4, target: 1, exhausts: true), Hits(damage: 4, target: 2, exhausts: true) } },
            FromPileUpgraded = new SimHandCard { Name = "Shiv", IsAttack = true, Plays = { Hits(damage: 6, target: 1, exhausts: true), Hits(damage: 6, target: 2, exhausts: true) } },
        };
        var state = Board();
        state.You.PileAutoPlays = state.You.PileAutoPlays! with { ExhaustCards = new List<SimDrawCard> { shiv, shiv, Defend } };
        state.You.ExhaustPile = 3;
        var outcome = SimEngine.Run(state, Card(new AutoPlayCardsAction(Key, SimPile.Exhaust, AutoPlayWay.Each, Count: 0) { AtTarget = true, Upgraded = true }, target: 2));
        var branch = Assert.Single(outcome.Branches);
        Assert.Equal(50, branch.Enemy(id: 1)!.Hp);
        Assert.Equal(38, branch.Enemy(id: 2)!.Hp);
        Assert.Equal(3, branch.You.ExhaustPile);
        Assert.Equal(3, branch.You.PileAutoPlays!.ExhaustCards!.Count);
        Assert.Equal(2, branch.You.PileAutoPlays!.ExhaustCards!.Count(card => card.Key == "shiv|upgraded"));
        Assert.Null(branch.Unmodelled);
    }

    [Fact]
    public void AnExhaustPilesCardThatDoesntExhaustItselfGoesToTheDiscardPile()
    {
        // Eidolon: an Ethereal Strike in the exhaust pile plays, then goes to the discard pile.
        var state = Board();
        state.You.PileAutoPlays = state.You.PileAutoPlays! with { ExhaustCards = new List<SimDrawCard> { Strike } };
        state.You.ExhaustPile = 1;
        var outcome = SimEngine.Run(state, Card(new AutoPlayCardsAction(Key, SimPile.Exhaust, AutoPlayWay.Each, Count: 0)));
        var branch = Assert.Single(outcome.Branches);
        Assert.Equal(44, branch.Enemy(id: 1)!.Hp);
        Assert.Equal(0, branch.You.ExhaustPile);
        Assert.Empty(branch.You.PileAutoPlays!.ExhaustCards!);
        Assert.Equal(1, branch.You.Draw!.DiscardCount);
    }

    [Fact]
    public void AnExhaustPileWithCardsNotFollowedOneByOneIsUnknown()
    {
        // A card exhausted earlier in the simulation joined only the count: the list isn't complete.
        var state = Board();
        state.You.PileAutoPlays = state.You.PileAutoPlays! with { ExhaustCards = new List<SimDrawCard> { Strike } };
        state.You.ExhaustPile = 2;
        var outcome = SimEngine.Run(state, Card(new AutoPlayCardsAction(Key, SimPile.Exhaust, AutoPlayWay.Each, Count: 0)));
        Assert.NotNull(Assert.Single(outcome.Branches).Unmodelled);
    }

    private static SimHandCard InHand(int id, SimPlay play, bool listed = true) =>
        new() { Id = id, Name = play.Name, IsAttack = play.IsAttack, AutoPlayedBy = listed ? Listed : null, Plays = { play } };

    [Fact]
    public void ARandomCardOfYourHandPlaysButNeverTheCardBeingPlayed()
    {
        // The Fool: one of your Strike and Defend (itself, card 7, in the play pile), each 1/2.
        var state = Board();
        state.You.AutoHand = new() { InHand(id: 5, Hits(damage: 6, handCard: 5)), InHand(id: 6, Blocks(block: 5, handCard: 6)), InHand(id: 7, Blocks(block: 99, handCard: 7)) };
        var outcome = SimEngine.Run(state, Card(new AutoPlayCardsAction(Key, SimPile.Hand, AutoPlayWay.Random, Count: 1)));
        Assert.Equal(2, outcome.Branches.Count);
        Assert.Equal(0.5, outcome.Chance(branch => branch.Enemy(id: 1)!.Hp == 44).Low, precision: 9);
        Assert.Equal(0.5, outcome.Chance(branch => branch.You.Block == 5).Low, precision: 9);
        Assert.All(outcome.Branches, branch => Assert.Equal(3, branch.You.HandCount));
    }

    [Fact]
    public void EachListedCardOfYourHandPlaysInTurn()
    {
        // Cho Chorus: the Wah and Ki cards (5 and 6), not the other (8).
        var state = Board();
        state.You.AutoHand = new() { InHand(id: 5, Hits(damage: 6, handCard: 5)), InHand(id: 6, Blocks(block: 5, handCard: 6)), InHand(id: 8, Blocks(block: 99, handCard: 8), listed: false) };
        var outcome = SimEngine.Run(state, Card(new AutoPlayCardsAction(Key, SimPile.Hand, AutoPlayWay.Each, Count: 0)));
        var branch = Assert.Single(outcome.Branches);
        Assert.Equal(44, branch.Enemy(id: 1)!.Hp);
        Assert.Equal(5, branch.You.Block);
        Assert.Equal(2, branch.AutoPlayed);
        Assert.Equal(2, branch.You.HandCount);
    }

    [Fact]
    public void AnAutoPlayTheBoardDidntReadForIsUnknown()
    {
        var state = Board(rest: new() { new SimDrawGroup(Strike, 1) });
        state.You.PileAutoPlays = null;
        var outcome = SimEngine.Run(state, Card(new AutoPlayCardsAction(Key, SimPile.Draw, AutoPlayWay.Random, Count: 1)));
        Assert.NotNull(Assert.Single(outcome.Branches).Unmodelled);
    }

    [Fact]
    public void ACardItsFilterCantBeAskedOfIsUnknown()
    {
        var unknown = Strike with { Key = "unknown", AutoPlayedBy = new[] { Key + "?" } };
        var outcome = SimEngine.Run(Board(rest: new() { new SimDrawGroup(unknown, 1) }), Card(new AutoPlayCardsAction(Key, SimPile.Draw, AutoPlayWay.Random, Count: 1)));
        Assert.NotNull(Assert.Single(outcome.Branches).Unmodelled);
    }

    [Fact]
    public void ItStopsOnceTheFightIsOver()
    {
        // Two Strikes for 30 at the one enemy left at 20: the first kills it, the second isn't played (CardCmd.AutoPlay does nothing then).
        var strike = Strike with { FromPile = new SimHandCard { Name = "StrikeIronclad", IsAttack = true, Plays = { Hits(damage: 30) } } };
        var state = Board(discard: new() { new SimDrawGroup(strike, 2) });
        state.Enemies.RemoveAt(1);
        state.Enemies[0].Hp = 20;
        var outcome = SimEngine.Run(state, Card(new AutoPlayCardsAction(Key, SimPile.Discard, AutoPlayWay.Shuffled, Count: 3)));
        var branch = Assert.Single(outcome.Branches);
        Assert.Equal(1, branch.AutoPlayed);
    }
}
