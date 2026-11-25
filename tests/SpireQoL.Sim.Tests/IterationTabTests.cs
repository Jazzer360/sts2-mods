using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Iteration's tag (IterationPower.AfterCardDrawn, read from its code: its amount drawn when the Status drawn is your turn's first): plain with
/// its draws on a card whose draw surely gives your first Status, and with its chance where only some outcomes do (its row's FiresByChance, the
/// owner's decision #297, as Hailstorm's chance tag of decision #249). With Show chances off, only a certain one shows.
/// </summary>
public class IterationTabTests
{
    private static readonly SimDrawCard Status = new("status") { Status = true };
    private static readonly SimDrawCard Plain = new("plain");

    private static SimState Iteration(params (SimDrawCard Card, int Count)[] pile)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50 } } };
        state.You.HandCount = 5;
        state.You.Hp = 50;
        state.You.Powers = state.You.Powers.With(PowerId.Iteration, amount: 2, data: 0);
        var question = Assert.Single(Assert.IsType<ReadModel>(SimModels.Get(PowerId.Iteration.Name)).Spec.Values().OfType<HookHistory>().Distinct()).Query;
        state.History = new[] { question };
        state.You.Draw = new SimDrawPile { Rest = pile.Select(cardCount => new SimDrawGroup(cardCount.Card, cardCount.Count)).ToList() };
        return state;
    }

    private static SimPlay Draws(int cards) => new()
    {
        Name = "Acrobatics", HandCard = 7, HandDelta = -1 + cards, Draws = cards, TargetId = 1,
        Body = { new CardEventAction(CardEvent.Drawn, cards) },
    };

    private static SourceTab? Tab(CardTabs tabs) => tabs.Tabs.GetValueOrDefault("proc:IterationPower");

    [Fact]
    public void ADrawThatSurelyGivesYourFirstStatusShowsItsDraws()
    {
        var tab = Tab(BonusTabRowTests.TabsOf(Iteration((Status, 6)), Draws(cards: 1)));
        Assert.NotNull(tab);
        Assert.False(tab!.Crossed);
        Assert.Equal(2, tab.Number);
        Assert.Null(tab.Chance);
    }

    [Fact]
    public void ADrawThatMightGiveYourFirstStatusShowsTheChance()
    {
        // One Status among four cards: the draw gives it one time in four.
        var tab = Tab(BonusTabRowTests.TabsOf(Iteration((Status, 1), (Plain, 3)), Draws(cards: 1)));
        Assert.NotNull(tab);
        Assert.False(tab!.Crossed);
        Assert.Equal("25%", tab.Chance);
        Assert.Null(tab.Number);
        Assert.Null(Tab(BonusTabRowTests.TabsOf(Iteration((Status, 1), (Plain, 3)), Draws(cards: 1), chances: false)));
    }

    [Fact]
    public void NoStatusToDrawShowsNothing()
    {
        Assert.Null(Tab(BonusTabRowTests.TabsOf(Iteration((Plain, 4)), Draws(cards: 1))));
    }
}
