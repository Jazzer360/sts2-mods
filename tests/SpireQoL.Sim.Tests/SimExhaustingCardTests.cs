using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A card that acts when it's exhausted (Drum of Battle: its own AfterCardExhausted gains its Energy), when another play exhausts
/// it from your hand (ExhaustFromHandAction, as each card's OnPlay picks: CombatCardSelection.NextItem, CardSelectCmd.FromHand,
/// every card). Its energy is certain when the play takes every card, a chance when it takes one at random, and unknown when you
/// choose (your later decision).
/// </summary>
public class SimExhaustingCardTests
{
    private static ExhaustingCard Drum(int id, int energy = 2) => new(HandCard: id, energy, SimCardType.Skill);

    /// <summary>You, with a hand of this many cards (the one played among them) and these cards that act when exhausted.</summary>
    private static SimState Hand(int cards, params ExhaustingCard[] exhausting)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 50 } } };
        state.You.HandCount = cards;
        state.You.ExhaustingInHand = exhausting.ToList();
        return state;
    }

    /// <summary>A card played from your hand (id 7) that exhausts others as it picks them.</summary>
    private static SimPlay Exhausting(ExhaustFromHandAction exhaust, int count = 1) => new()
    {
        Name = "Exhauster", HandCard = 7, HandDelta = -1 - count,
        Body = { new CardEventAction(CardEvent.Exhausted, count) { Indirect = true }, exhaust },
    };

    private static double ChanceOfEnergy(SimOutcome outcome, int energy) => outcome.Branches.Where(branch => branch.You.Energy == energy).Sum(branch => branch.Weight);

    [Fact]
    public void CinderTakingADrumAtRandomIsItsShareOfTheHand()
    {
        // Cinder, a Drum of Battle and a Defend: it exhausts one of the two others, the Drum half the time (+2 energy).
        var outcome = SimEngine.Run(Hand(cards: 3, Drum(id: 21)), Exhausting(new ExhaustFromHandAction(ExhaustPick.Random)));
        Assert.All(outcome.Branches, branch => Assert.Null(branch.Unmodelled));
        Assert.Equal(0.5, ChanceOfEnergy(outcome, energy: 2), precision: 9);
        Assert.Equal(0.5, ChanceOfEnergy(outcome, energy: 0), precision: 9);
        Assert.All(outcome.Branches.Where(branch => branch.You.Energy == 2), branch => Assert.Empty(branch.You.ExhaustingInHand));
    }

    [Fact]
    public void TwoCopiesAtRandomAreOneBranchOfTheirSharesTogether()
    {
        // Two identical Drums in a hand of four others: one of them goes half the time, merged into one branch.
        var outcome = SimEngine.Run(Hand(cards: 5, Drum(id: 21), Drum(id: 22)), Exhausting(new ExhaustFromHandAction(ExhaustPick.Random)));
        Assert.Equal(2, outcome.Branches.Count);
        Assert.Equal(0.5, ChanceOfEnergy(outcome, energy: 2), precision: 9);
    }

    [Fact]
    public void FiendFireTakesEveryDrumForCertain()
    {
        var outcome = SimEngine.Run(Hand(cards: 4, Drum(id: 21), Drum(id: 22, energy: 3)), Exhausting(new ExhaustFromHandAction(ExhaustPick.All), count: 3));
        var branch = Assert.Single(outcome.Branches);
        Assert.Null(branch.Unmodelled);
        Assert.Equal(5, branch.You.Energy);
        Assert.Empty(branch.You.ExhaustingInHand);
    }

    [Fact]
    public void SecondWindLeavesAnAttackThatActsWhenExhausted()
    {
        var attack = new ExhaustingCard(HandCard: 23, Energy: 4, SimCardType.Attack);
        var outcome = SimEngine.Run(Hand(cards: 3, Drum(id: 21), attack), Exhausting(new ExhaustFromHandAction(ExhaustPick.All) { Except = SimCardType.Attack }));
        var branch = Assert.Single(outcome.Branches);
        Assert.Equal(2, branch.You.Energy);
        Assert.Equal(attack, Assert.Single(branch.You.ExhaustingInHand));
    }

    [Fact]
    public void ACardYouChooseToExhaustIsACardNeverAParticularOne()
    {
        // Decision #92: your pick is a card, never a particular one. The play does what any pick would do: with a card besides the
        // Drum, nothing that only the Drum would do (no energy), and nothing marked unknown.
        var chosen = new ExhaustFromHandAction(ExhaustPick.Chosen);
        var plain = Assert.Single(SimEngine.Run(Hand(cards: 3, Drum(id: 21)), Exhausting(chosen)).Branches);
        Assert.Null(plain.Unmodelled);
        Assert.Equal(0, plain.You.Energy);
        // Only the Drum to take: any pick is it, so its energy is what the play does.
        Assert.Equal(2, Assert.Single(SimEngine.Run(Hand(cards: 1, Drum(id: 21)), Exhausting(chosen)).Branches).You.Energy);
        // Only Drums that differ: which one is the player's decision, neither simulated nor marked.
        var differing = Assert.Single(SimEngine.Run(Hand(cards: 2, Drum(id: 21), Drum(id: 22, energy: 3)), Exhausting(chosen)).Branches);
        Assert.Null(differing.Unmodelled);
        Assert.Equal(0, differing.You.Energy);
    }

    [Fact]
    public void ARandomCardOfAKindIsUnknownOnlyWhenADrumIsOfThatKind()
    {
        // Thrash exhausts a random Attack: a Drum (a Skill) is never it; an Attack that acts when exhausted might be (how many
        // Attacks the hand holds isn't kept).
        var thrash = new ExhaustFromHandAction(ExhaustPick.Random) { Only = SimCardType.Attack };
        var withDrum = SimEngine.Run(Hand(cards: 3, Drum(id: 21)), Exhausting(thrash));
        Assert.All(withDrum.Branches, branch => Assert.Null(branch.Unmodelled));
        Assert.Equal(1.0, ChanceOfEnergy(withDrum, energy: 0), precision: 9);
        var withAttack = SimEngine.Run(Hand(cards: 3, new ExhaustingCard(HandCard: 23, Energy: 4, SimCardType.Attack)), Exhausting(thrash));
        Assert.All(withAttack.Branches, branch => Assert.NotNull(branch.Unmodelled));
    }

    [Fact]
    public void CleansesPickFromTheDrawPileIsACardNeverAParticularOne()
    {
        // A Drum among cards that differ: which one you exhaust is your decision (decision #92), so the Drum's energy isn't given and
        // nothing is marked unknown.
        var drum = new SimDrawCard("drum") { Type = SimCardType.Skill, Exhausting = Drum(id: 0) };
        var state = Hand(cards: 2);
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(drum, Count: 1), new SimDrawGroup(new SimDrawCard("skill") { Type = SimCardType.Skill }, Count: 2) } };
        var after = Assert.Single(SimEngine.Run(state, Exhausting(new ExhaustFromHandAction(ExhaustPick.Chosen) { FromDrawPile = true })).Branches);
        Assert.Null(after.Unmodelled);
        Assert.Equal(0, after.You.Energy);
        // A card left the pile whichever you took (decision #188): one fewer to draw, no particular one taken.
        Assert.Equal(2, after.You.Draw!.Count);
    }

    [Fact]
    public void CleansesPickLeavesOneCardFewerToDraw()
    {
        // Three cards that differ, one picked and exhausted: a draw of three later this turn draws two, then shuffles the discard
        // pile in for the third (the game: CardPileCmd.Draw's ShuffleIfNecessary once the draw pile is empty).
        var state = Hand(cards: 2);
        state.You.Draw = new SimDrawPile
        {
            Rest = { new SimDrawGroup(new SimDrawCard("drum") { Type = SimCardType.Skill, Exhausting = Drum(id: 0) }, Count: 1), new SimDrawGroup(new SimDrawCard("skill") { Type = SimCardType.Skill }, Count: 2) },
            Discard = { new SimDrawGroup(new SimDrawCard("plain"), Count: 4) },
        };
        var play = Exhausting(new ExhaustFromHandAction(ExhaustPick.Chosen) { FromDrawPile = true });
        play.Body.Add(new CardEventAction(CardEvent.Drawn, Count: 3));
        var result = SimEngine.Run(state, play);
        Assert.All(result.Branches, branch =>
        {
            Assert.Equal(3, branch.You.Draw!.Count); // the discard pile's four, one drawn
            Assert.Equal(0, branch.You.Draw.DiscardCount);
        });
    }

    [Fact]
    public void ADrumBeingPlayedIsNoLongerInYourHandToExhaust()
    {
        // The Drum is the card played (its hand id): it left your hand, so an exhaust of every card in hand doesn't take it.
        var play = Exhausting(new ExhaustFromHandAction(ExhaustPick.All));
        play.HandCard = 21;
        var branch = Assert.Single(SimEngine.Run(Hand(cards: 2, Drum(id: 21)), play).Branches);
        Assert.Equal(0, branch.You.Energy);
    }

    [Fact]
    public void ADrumDrawnThisPlayIsInYourHandToExhaust()
    {
        // A card that draws 1 (a Drum, the only card in the pile), then exhausts every card in your hand: +2.
        var state = Hand(cards: 1);
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(new SimDrawCard("drum") { Type = SimCardType.Skill, Exhausting = Drum(id: 0) }, Count: 1) } };
        var play = new SimPlay
        {
            Name = "DrawThenBurn", HandCard = 7, HandDelta = 0, Draws = 1,
            Body = { new CardEventAction(CardEvent.Drawn, Count: 1), new CardEventAction(CardEvent.Exhausted, Count: 1) { Indirect = true }, new ExhaustFromHandAction(ExhaustPick.All) },
        };
        var branch = Assert.Single(SimEngine.Run(state, play).Branches);
        Assert.Equal(2, branch.You.Energy);
    }
}
