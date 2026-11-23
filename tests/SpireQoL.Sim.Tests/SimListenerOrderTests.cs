using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The models answer each hook in the game's listener order (CombatState.IterateHookListeners, decompile v0.113.1): for each player in
/// player order, their powers (in the order applied), their relics, then the cards in their piles (SimPlayer.Cards); then each
/// enemy's powers. So a card's own hook (Void's energy loss as it's drawn, Regret's count of the hand) comes after every power and
/// relic of its owner, as in the game. ListenerOrderTests (catalog tests) holds the same order against the game's own list.
/// </summary>
public class SimListenerOrderTests
{
    private static SimState Board(int energy = 3)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 40 } } };
        state.You.Energy = energy;
        state.You.HandCount = 5;
        state.You.Hp = 50;
        return state;
    }

    /// <summary>A card played from your hand that draws <paramref name="cards"/>.</summary>
    private static SimPlay Draws(int cards) => new()
    {
        Name = "Acrobatics", HandCard = 7, HandDelta = -1 + cards, Draws = cards,
        Body = { new CardEventAction(CardEvent.Drawn, cards) },
    };

    private static readonly SimDrawCard Plain = new("plain");
    private static readonly SimDrawCard Void = new("void") { Status = true, EnergyLostOnDraw = 1 };

    // ---- the order

    [Fact]
    public void EachPlayersCardsListenAfterTheirPowersAndRelics()
    {
        var state = Board();
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Enrage, amount: 2);
        state.You.Powers = PowerSet.Empty.With(PowerId.Automation, amount: 1, data: 5).With(PowerId.Iteration, amount: 1);
        state.You.Relics = RelicSet.Empty.With(RelicId.PenNib, amount: 3);
        state.You.Cards = CardSet.Empty.With(CardId.Regret, amount: 1).With(CardId.Void, amount: 2);
        state.Teammates.Add(new SimPlayer { Hp = 30, Powers = PowerSet.Empty.With(PowerId.Pagestorm, amount: 1), Cards = CardSet.Empty.With(CardId.Void, amount: 1) });
        state.TeammatesBeforeMe = 1;
        var expected = new List<(ListenerKind Kind, string Name, int Owner)>
        {
            (ListenerKind.Power, PowerId.Pagestorm.Name, Who.Teammate(teammateIndex: 0)),
            (ListenerKind.Card, CardId.Void.Name, Who.Teammate(teammateIndex: 0)),
            (ListenerKind.Power, PowerId.Automation.Name, Who.You),
            (ListenerKind.Power, PowerId.Iteration.Name, Who.You),
            (ListenerKind.Relic, RelicId.PenNib.Name, Who.You),
            (ListenerKind.Card, CardId.Regret.Name, Who.You),
            (ListenerKind.Card, CardId.Void.Name, Who.You),
            (ListenerKind.Power, PowerId.Enrage.Name, 0),
        };
        Assert.Equal(expected, SimEngine.ListenerOrder(state));
    }

    [Fact]
    public void ACardsModelListensOnlyFromThePiles()
    {
        // A power named as a card's model isn't that card, and a card named as a power's model isn't that power.
        var state = Board();
        state.You.Powers = PowerSet.Empty.With(new PowerId(CardId.Void.Name), amount: 1);
        state.You.Cards = CardSet.Empty.With(new CardId(PowerId.Automation.Name), amount: 1);
        Assert.Empty(SimEngine.ListenerOrder(state));
    }

    [Fact]
    public void TheListenersFollowTheCardsInThePiles()
    {
        // The listeners are cached on the state until a set they come from changes: the cards' set is one of them.
        var state = Board();
        Assert.Empty(SimEngine.Listeners(state));
        state.You.Cards = CardSet.Empty.With(CardId.Void, amount: 1);
        Assert.Equal(CardId.Void.Name, Assert.Single(SimEngine.Listeners(state)).Model.Name);
    }

    // ---- what the order changes

    [Fact]
    public void VoidTakesItsEnergyAfterAutomationGivesItsOwn()
    {
        // From 0 energy, a Void drawn on the draw that finishes Automation's count: AutomationPower.AfterCardDrawn (your power) gives 1,
        // then Void.AfterCardDrawn (the card, in your hand) takes 1: 0 left. The other way round (nothing to take, then 1 given) left 1.
        var state = Board(energy: 0);
        state.You.Powers = PowerSet.Empty.With(PowerId.Automation, amount: 1, data: 1);
        state.You.Cards = CardSet.Empty.With(CardId.Void, amount: 1);
        state.You.Draw = new SimDrawPile { Top = { Void }, Rest = { new SimDrawGroup(Plain, Count: 5) } };
        var after = Assert.Single(SimEngine.Run(state, Draws(cards: 1)).Branches);
        Assert.Equal(0, after.You.Energy);
        Assert.True(after.Fired.Has(PowerId.Automation.Name));
    }

    [Fact]
    public void IterationsDrawComesBeforeVoidTakesItsEnergy()
    {
        // Void is a Status: IterationPower.AfterCardDrawn (your power, applied first) draws 1 on it, and that draw finishes
        // Automation's count (1 energy); Void's own hook answers last, after every power, and takes the energy back: 0. Iteration's
        // draw after Void's loss (as the draw used to run) left 1.
        var state = Board(energy: 0);
        state.You.Powers = PowerSet.Empty.With(PowerId.Iteration, amount: 1).With(PowerId.Automation, amount: 1, data: 1);
        state.You.Cards = CardSet.Empty.With(CardId.Void, amount: 1);
        state.You.Draw = new SimDrawPile { Top = { Void }, Rest = { new SimDrawGroup(Plain, Count: 5) } };
        var after = Assert.Single(SimEngine.Run(state, Draws(cards: 1)).Branches);
        Assert.Equal(2, after.You.Drawn);
        Assert.Equal(0, after.You.Energy);
    }

    [Fact]
    public void AVoidDrawnWithoutItsListenerKeepsTheEnergy()
    {
        // The energy loss is the card's own hook: it answers from the piles (SimPlayer.Cards), as the adapter reads them.
        var state = Board(energy: 2);
        state.You.Draw = new SimDrawPile { Top = { Void } };
        Assert.Equal(2, Assert.Single(SimEngine.Run(state, Draws(cards: 1)).Branches).You.Energy);
        state.You.Cards = CardSet.Empty.With(CardId.Void, amount: 1);
        Assert.Equal(1, Assert.Single(SimEngine.Run(state, Draws(cards: 1)).Branches).You.Energy);
    }

    [Fact]
    public void RegretCountsTheHandAsTheTurnEnds()
    {
        // Regret.BeforeSideTurnEnd counts the hand (3: itself, an Ethereal card, another). The Ethereal card exhausts after that
        // (DoTurnEnd) and Dark Embrace draws on it; Regret's OnTurnEndInHand deals the count it took: 3, not the 4 the hand holds then.
        var state = Board();
        state.You.HandCount = 3;
        state.You.Fading = 1;
        state.You.Powers = PowerSet.Empty.With(PowerId.DarkEmbrace, amount: 1);
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(Plain, Count: 5) } };
        state.You.TurnEndCards.Add(new TurnEndCard(CardId.Regret.Name, "Regret", Amount: 0));
        state.You.Cards = CardSet.Empty.With(CardId.Regret, amount: 1);
        var after = SimEngine.Run(state).Worst;
        Assert.Equal(4, after.You.HandCount);
        Assert.Equal(3, after.UnblockableTaken);
        Assert.Equal(3, after.Taken);
    }
}
