using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A card's CardCmd.Upgrade of your cards in your piles (UpgradeCardsAction, decision #347; decompile v0.114.0: CardCmd.Upgrade runs
/// UpgradeInternal and FinalizeUpgradeInternal on each card that can be upgraded, and no hook). What it changes is what a later step reads of an
/// upgraded card: in your hand, one that acts as the turn ends (Burn's damage, an Ethereal card's exhaust) or one Stampede or Simpleton could
/// auto-play; in your draw or discard pile, one drawn, taken or played from there whose profile isn't plain; in your exhaust pile, one that plays
/// itself from there as the turn ends (Howl from Beyond). Anything else (a hand card's numbers) is read when it's played, a later decision.
/// </summary>
public class UpgradeCardsTests
{
    private static readonly SimDrawCard Plain = new("plain");
    private static readonly SimDrawCard UpgradableBurn = new("burn|upgradable") { TurnEnd = new TurnEndCard("Burn", "Burn", Amount: 2), Upgradable = true };

    private static SimState You(int hand = 5)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50 } } };
        state.You.HandCount = hand;
        state.You.Hp = 50;
        return state;
    }

    private static SimPlay Upgrades(UpgradedPiles piles, params SimAction[] after)
    {
        var play = new SimPlay { Name = "Apotheosis", HandCard = 7, HandDelta = -1, Body = { new UpgradeCardsAction(piles, Source: "Apotheosis") } };
        play.Body.AddRange(after);
        return play;
    }

    [Fact]
    public void AnUpgradeOfAHandWithNothingActingLaterIsKnown()
    {
        var outcome = SimEngine.Run(You(), Upgrades(UpgradedPiles.Hand));
        Assert.Null(Assert.Single(outcome.Branches).Unmodelled);
    }

    [Fact]
    public void AnUpgradeOfAHandHoldingAnUpgradableBurnIsUnknown()
    {
        // The Burn's damage as the turn ends is read at the board: upgraded, it deals more.
        var state = You();
        state.You.UpgradableAtTurnEnd = 1;
        state.You.TurnEndCards = new List<TurnEndCard> { new("Burn", "Burn", Amount: 2) };
        var outcome = SimEngine.Run(state, Upgrades(UpgradedPiles.Hand));
        Assert.Contains("upgrades cards in hand", Assert.Single(outcome.Branches).Unmodelled);
    }

    [Fact]
    public void AnUpgradeOfAHandStampedeCouldPickFromMakesItsAutoPlaysUnknown()
    {
        var state = You();
        state.You.AutoHand = new List<SimHandCard> { new() { Id = 3, Name = "StrikeIronclad", IsAttack = true, Card = new SimCard("StrikeIronclad", SimCardType.Attack) { Upgradable = true } } };
        var outcome = SimEngine.Run(state, Upgrades(UpgradedPiles.Hand));
        Assert.True(Assert.Single(outcome.Branches).AutoPlaysUnknown);
    }

    [Fact]
    public void AHandCardThatCantBeUpgradedLeavesItsAutoPlaysKnown()
    {
        var state = You();
        state.You.AutoHand = new List<SimHandCard> { new() { Id = 3, Name = "StrikeIronclad", IsAttack = true, Card = new SimCard("StrikeIronclad", SimCardType.Attack) { Upgraded = true } } };
        var outcome = SimEngine.Run(state, Upgrades(UpgradedPiles.Hand));
        Assert.False(Assert.Single(outcome.Branches).AutoPlaysUnknown);
    }

    [Fact]
    public void ACardDrawnFromAnUpgradedPileIsUnknownWhenAnUpgradeCouldChangeIt()
    {
        // Apotheosis, then a draw of one card: the Burn (1 in 2) could be upgraded; the plain card changes nothing followed.
        var state = You();
        state.You.Draw = new SimDrawPile { Rest = new() { new SimDrawGroup(UpgradableBurn, 1), new SimDrawGroup(Plain, 1) } };
        var outcome = SimEngine.Run(state, Upgrades(UpgradedPiles.Hand | UpgradedPiles.Draw | UpgradedPiles.Discard, new CardEventAction(CardEvent.Drawn, Count: 1)));
        Assert.Equal(2, outcome.Branches.Count);
        Assert.Equal(0.5, outcome.Branches.Where(branch => branch.Unmodelled != null).Sum(branch => branch.Weight), precision: 9);
    }

    [Fact]
    public void ADrawWithNoUpgradeBeforeItIsKnown()
    {
        var state = You();
        state.You.Draw = new SimDrawPile { Rest = new() { new SimDrawGroup(UpgradableBurn, 1) } };
        var outcome = SimEngine.Run(state, new SimPlay { Name = "Acrobatics", HandCard = 7, HandDelta = -1, Body = { new CardEventAction(CardEvent.Drawn, Count: 1) } });
        Assert.Null(Assert.Single(outcome.Branches).Unmodelled);
    }

    [Fact]
    public void AnUpgradableCardDrawnCountsForALaterUpgradeOfTheHand()
    {
        // A drawn Burn that can be upgraded joins the hand among the cards an upgrade there could change (Tune's, Armaments+'s after it).
        var state = You();
        state.You.Draw = new SimDrawPile { Rest = new() { new SimDrawGroup(UpgradableBurn, 1) } };
        var outcome = SimEngine.Run(state, Upgrades(UpgradedPiles.None, new CardEventAction(CardEvent.Drawn, Count: 1), new UpgradeCardsAction(UpgradedPiles.Hand, Source: "Armaments")));
        var branch = Assert.Single(outcome.Branches);
        Assert.Equal(1, branch.You.UpgradableAtTurnEnd);
        Assert.Contains("upgrades cards in hand", branch.Unmodelled);
    }

    [Fact]
    public void AnUpgradeOfTheExhaustPileWithACardPlayingItselfFromThereIsUnknown()
    {
        var state = You();
        state.You.ExhaustAutoPlays = new List<SimPlay>
        {
            new() { Name = "HowlFromBeyond", HandDelta = 0, Card = new SimCardPlay(new SimCard("HowlFromBeyond", SimCardType.Attack) { Upgradable = true }, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: true) },
        };
        var outcome = SimEngine.Run(state, Upgrades(UpgradedPiles.Exhaust));
        Assert.Contains("exhaust pile", Assert.Single(outcome.Branches).Unmodelled);
    }

    [Fact]
    public void AnUpgradeOfTheDrawPileWithIAmInvincibleOnTopIsUnknown()
    {
        var state = You();
        state.You.InvincibleBlock = 12;
        state.You.InvincibleDrawPile = 1;
        var outcome = SimEngine.Run(state, Upgrades(UpgradedPiles.Draw));
        Assert.Contains("I Am Invincible", Assert.Single(outcome.Branches).Unmodelled);
    }
}
