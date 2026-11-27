using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A card you pick from cards offered, by a pick you may skip (decision #408: CardSelectCmd.FromChooseACardScreen with canSkip, then
/// CardPileCmd.AddGeneratedCardToCombat only `if (cardModel != null)`: Colorless Potion, Discovery, Quasar, Splash). Whether it joins your hand is your
/// later decision: it's counted in (SimPlayer.MaybeSkipped), and what reads the hand's size or its room after it is unknown where taking it or not
/// changes it, as are the hooks that hear a card made. Nothing else is.
/// </summary>
public class SkippablePickTests
{
    private static SimState Fight(int handCount)
    {
        var enemy = new SimEnemy { Id = 0, Name = "E0", Hp = 40, Hits = new() { 6 }, Intent = new() { new(Base: 6) }, IntendsToAttack = true };
        var state = new SimState { Enemies = { enemy } };
        state.You.Hp = 50;
        state.You.MaxHp = 50;
        state.You.Potions = 2;
        state.You.HandCount = handCount;
        return state;
    }

    private static SimPlay ColorlessPotion(params SimAction[] after)
    {
        var play = new SimPlay { Name = "ColorlessPotion", TargetId = Who.You, Potion = new SimPotionUse("ColorlessPotion", Who.You, Who.You) };
        play.Body.Add(new CardEventAction(CardEvent.Generated, Count: 1) { Indirect = true, JoinsHand = true, YouMaySkip = true });
        play.Body.AddRange(after);
        return play;
    }

    [Fact]
    public void TheCardIsCountedAsOneYouMayNotHaveTaken()
    {
        // A dev review report: you skipped Colorless Potion's pick and had 6 cards in hand where 7 were predicted. Nothing reads the hand here: known.
        SimEngine.ApplyPlayTo(Fight(handCount: 6), ColorlessPotion(), out var after, out _);
        var branch = Assert.Single(after);
        Assert.Null(branch.Unmodelled);
        Assert.Equal(7, branch.You.HandCount);
        Assert.Equal(1, branch.You.MaybeSkipped);
    }

    [Fact]
    public void RegretCountingTheHandAtTheTurnsEndIsUnknown()
    {
        // Regret's BeforeSideTurnEnd loses 1 HP per card in hand: 7 or 6, your pick decides.
        var state = Fight(handCount: 6);
        state.You.TurnEndCards.Add(new TurnEndCard(CardId.Regret.Name, "Regret", Amount: 0));
        state.You.Cards = state.You.Cards.With(CardId.Regret, amount: 1);
        Assert.All(SimEngine.Run(state, ColorlessPotion()).Branches, branch => Assert.Equal(SimEngine.SkippedPickUnknown, branch.Unmodelled));
    }

    [Fact]
    public void ADrawAfterItIsUnknownOnlyWhenTheHandIsFull()
    {
        // 9 cards and the one you may take: a draw finds a full hand, or room had you skipped it. With room either way, the draw is the same.
        var draw = new CardEventAction(CardEvent.Drawn, Count: 1) { JoinsHand = true };
        SimEngine.ApplyPlayTo(Fight(handCount: 9), ColorlessPotion(draw), out var full, out _);
        Assert.Equal(SimEngine.SkippedPickUnknown, Assert.Single(full).Unmodelled);
        SimEngine.ApplyPlayTo(Fight(handCount: 5), ColorlessPotion(draw), out var room, out _);
        Assert.Null(Assert.Single(room).Unmodelled);
    }

    [Fact]
    public void ATakeOfYourWholeHandAfterItIsUnknown()
    {
        // Every card of your hand taken after it (a whole-hand exhaust): one more or not, your pick decides. A take of one from a bigger hand isn't.
        var whole = new TakeFromHandAction(HandTakeWay.Each) { Event = CardEvent.Exhausted };
        SimEngine.ApplyPlayTo(Fight(handCount: 3), ColorlessPotion(whole), out var every, out _);
        Assert.NotNull(Assert.Single(every).Unmodelled);
        var one = new TakeFromHandAction(HandTakeWay.Pick) { Event = CardEvent.Exhausted, Most = 1 };
        SimEngine.ApplyPlayTo(Fight(handCount: 3), ColorlessPotion(one), out var picked, out _);
        Assert.Null(Assert.Single(picked).Unmodelled);
    }

    [Fact]
    public void ATakeWhoseCountYouChooseCountsTheMostItCouldTake()
    {
        // A dev review of the owner's Regent run: Purity exhausted one card you picked, and the play audit's budget held it to Purity's own exhaust alone.
        // How many is your choice (decision #406): the play's log counts the most it could take (up to 3 of 4), as an "up to" count is.
        var purity = new SimPlay { Name = "Purity", TargetId = Who.None };
        purity.Body.Add(new TakeFromHandAction(HandTakeWay.Pick) { Event = CardEvent.Exhausted, Most = 3, ChoosesHowMany = true });
        var run = SimEngine.Run(Fight(handCount: 4), purity);
        var branch = Assert.Single(run.Branches);
        Assert.NotNull(branch.Unmodelled);
        Assert.Equal(3, branch.Fired[SimKeys.PlayEvent(CardEvent.Exhausted)]);
        // Two in hand: two at most.
        Assert.Equal(2, Assert.Single(SimEngine.Run(Fight(handCount: 2), purity).Branches).Fired[SimKeys.PlayEvent(CardEvent.Exhausted)]);
    }

    [Fact]
    public void AHookThatActsOnACardMadeIsUnknown()
    {
        // Skipped, the game makes no card: AfterCardGeneratedForCombat isn't heard. Trash to Treasure channels an orb for a Status you make.
        var state = Fight(handCount: 3);
        state.You.OrbCapacity = 3;
        state.You.Powers = state.You.Powers.With(PowerId.TrashToTreasure, amount: 1);
        var play = new SimPlay { Name = "ColorlessPotion", TargetId = Who.You, Potion = new SimPotionUse("ColorlessPotion", Who.You, Who.You) };
        play.Body.Add(new CardEventAction(CardEvent.Generated, Count: 1) { Indirect = true, JoinsHand = true, YouMaySkip = true, Card = new SimCard(CardId.Dazed.Name, SimCardType.Status) });
        SimEngine.ApplyPlayTo(state, play, out var after, out _);
        Assert.All(after, branch => Assert.NotNull(branch.Unmodelled));
        // A card it doesn't act on: known.
        play.Body[0] = new CardEventAction(CardEvent.Generated, Count: 1) { Indirect = true, JoinsHand = true, YouMaySkip = true, Card = new SimCard("Finesse", SimCardType.Skill) };
        SimEngine.ApplyPlayTo(state, play, out var plain, out _);
        Assert.Null(Assert.Single(plain).Unmodelled);
    }
}
