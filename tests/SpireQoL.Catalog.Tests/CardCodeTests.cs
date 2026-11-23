using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireQoL.Procs;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #157: a card's draws and star gains are read from its own code (CardCode, CardEnergy.CallsOf), not from lists of names,
/// so a new card, or one that differs between the game's branches, is read as it is in the installed game.
/// </summary>
[Collection(GameFightCollection.Name)]
public class CardCodeTests
{
    [Fact]
    public void DrawsAreReadFromTheCardsPlay()
    {
        GameModels.EnsureLoaded();
        // Shrug It Off draws its Cards; Dagger Throw has no Cards value and draws one; a Strike doesn't draw.
        Assert.Equal(ModelDb.Card<ShrugItOff>().DynamicVars["Cards"].IntValue, PlayFacts.CardsDrawn(ModelDb.Card<ShrugItOff>(), handCount: 5));
        Assert.Equal(1, PlayFacts.CardsDrawn(ModelDb.Card<DaggerThrow>(), handCount: 5));
        Assert.Equal(0, PlayFacts.CardsDrawn(ModelDb.Card<StrikeIronclad>(), handCount: 5));
        // Constellation makes the teammate draw, not you.
        Assert.Equal(0, PlayFacts.CardsDrawn(ModelDb.Card<Constellation>(), handCount: 5));
    }

    [Fact]
    public void FtlDrawsOnThePlaysUnderItsLimit()
    {
        // Ftl.CanDrawCard: fewer than PlayMax plays finished this turn. Claw played twice, then FTL twice under Echo Form: only the
        // first FTL draws (a report predicted 2 draws, the game drew 1).
        Assert.Equal(1, PlayFacts.PlaysUnder(finishedBefore: 2, max: 3, plays: 2));
        Assert.Equal(2, PlayFacts.PlaysUnder(finishedBefore: 0, max: 3, plays: 2));
        Assert.Equal(0, PlayFacts.PlaysUnder(finishedBefore: 3, max: 3, plays: 2));
    }

    [Fact]
    public void EveryCardThatDrawsInItsPlayIsCounted()
    {
        // Every card whose play calls the game's draw command, apart from the ones that draw for a teammate and the ones whose
        // draw depends on the board (a condition, the target, a count the game calculates), draws at least one.
        GameModels.EnsureLoaded();
        var notCounted = ModelDb.AllCards
            .Where(card => CardCode.Calls(card.GetType(), "CardPileCmd", "Draw"))
            .Where(card => !SpireQoL.Procs.Programs.CardEffects.DrawsForTarget(card.GetType()) && !PlayFacts.TargetDrawCards.Contains(card.GetType().Name)
                           && !PlayFacts.ConditionalDrawCards.Contains(card.GetType().Name)
                           && !card.DynamicVars.ContainsKey("CalculatedCards")) // a count from the fight: none on the template card
            .Where(card => PlayFacts.CardsDrawn(card, handCount: 5) + PlayFacts.CardsDrawn(card, handCount: 1) == 0)
            .Select(card => card.GetType().Name).ToList();
        Assert.True(notCounted.Count == 0, "draw in their play, counted as none: " + string.Join(", ", notCounted));
    }

    [Fact]
    public void StarGainsAreReadFromTheCardsPlay()
    {
        GameModels.EnsureLoaded();
        // Gather Light gains stars on every play; Knockout Blow only when its attack kills (the attack's, not the play's).
        Assert.Equal(1, PlayFacts.StarGainsOf(ModelDb.Card<GatherLight>()));
        Assert.Equal(0, PlayFacts.StarGainsOf(ModelDb.Card<KnockoutBlow>()));
        Assert.True(SpireQoL.Procs.Programs.CardEffects.GainsStarsOnKill(typeof(KnockoutBlow)));
    }

    [Fact]
    public void MadeCardsGoWhereTheCodeSends()
    {
        // Undeath's copy goes to your discard pile (a report: the hand was counted one too many); Severance's three go one to each pile,
        // so one into your hand; Bundle of Joy's all into your hand.
        GameModels.EnsureLoaded();
        Assert.Equal(0, PlayFacts.MadeIntoHand(ModelDb.Card<Undeath>(), generated: 1));
        Assert.Equal(1, PlayFacts.MadeIntoHand(ModelDb.Card<Severance>(), generated: 3));
        Assert.Equal(4, PlayFacts.MadeIntoHand(ModelDb.Card<BundleOfJoy>(), generated: 4));
    }

    [Fact]
    public void EveryMadeCardsPileIsRead()
    {
        // Every card that makes cards names their pile as a constant the code reader finds (a new way to pass it would read as unknown).
        GameModels.EnsureLoaded();
        var unread = ModelDb.AllCards.Where(card => CardCode.MadeInto(card.GetType()).Any(pile => pile == null)).Select(card => card.GetType().Name).ToList();
        Assert.True(unread.Count == 0, "cards whose made cards' pile isn't read: " + string.Join(", ", unread));
    }
}
