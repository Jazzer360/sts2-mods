using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireQoL.Procs;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #157: a card's draws and star gains are read from its own code (CardCode, CardEnergy.CallsOf), not from lists of names,
/// so a new card, or one that differs between the game's branches, is read as it is in the installed game.
/// </summary>
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
    public void EveryCardThatDrawsInItsPlayIsCounted()
    {
        // Every card whose play calls the game's draw command, apart from the ones that draw for a teammate and the ones whose
        // draw depends on the board (a condition, the target, a count the game calculates), draws at least one.
        GameModels.EnsureLoaded();
        var notCounted = ModelDb.AllCards
            .Where(card => CardCode.Calls(card.GetType(), "CardPileCmd", "Draw"))
            .Where(card => !PlayFacts.AllyDrawCards.Contains(card.GetType().Name) && !PlayFacts.TargetDrawCards.Contains(card.GetType().Name)
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
        Assert.Contains(nameof(KnockoutBlow), PlayFacts.KillStarCards);
    }
}
