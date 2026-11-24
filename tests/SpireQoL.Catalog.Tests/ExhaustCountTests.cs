using SpireQoL.Procs.Programs;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Other cards a play exhausts, read from its program and counted on the board (decision #182, CardEffects.ExhaustsOfOthers): a
/// filter over your hand, a test on the picked card and a pick from your draw pile: one play's (decision #189).
/// </summary>
[Collection(GameFightCollection.Name)]
public class ExhaustCountTests
{
    private static int Exhausts(GameFight gameFight, CardModel card)
    {
        var others = PileType.Hand.GetPile(gameFight.Me).Cards.Where(other => !ReferenceEquals(other, card)).ToList();
        return BoardCounts.ExhaustOthers(card, others, drawPile: PileType.Draw.GetPile(gameFight.Me).Cards.Count);
    }

    [Fact]
    public void GrowthExhaustsEverySadnessInYourHand()
    {
        var gameFight = new GameFight();
        var card = gameFight.Add<Growth>();
        gameFight.Add<Sadness>();
        gameFight.Add<Sadness>();
        gameFight.Add<StrikeIronclad>();
        Assert.Equal(2, Exhausts(gameFight, card));
    }

    [Fact]
    public void SecondWindExhaustsYourNonAttacksOnce()
    {
        // One play's (a replay's later play finds them gone in the simulator: SimPerPlayTests).
        var gameFight = new GameFight();
        var card = gameFight.Add<SecondWind>();
        gameFight.Add<StrikeIronclad>();
        gameFight.Add<DefendIronclad>();
        gameFight.Add<Dazed>();
        Assert.Equal(2, Exhausts(gameFight, card));
    }

    [Fact]
    public void TheHermitExhaustsOnlyACurseYouPick()
    {
        var gameFight = new GameFight();
        var card = gameFight.Add<TheHermit>();
        gameFight.Add<StrikeIronclad>();
        Assert.Equal(0, Exhausts(gameFight, card));
        gameFight.Add<Normality>();
        Assert.Equal(1, Exhausts(gameFight, card));
    }

    [Fact]
    public void BrandExhaustsOneCardAPlay()
    {
        var gameFight = new GameFight();
        var card = gameFight.Add<Brand>();
        gameFight.Add<StrikeIronclad>();
        Assert.Equal(1, Exhausts(gameFight, card));
        gameFight.Add<DefendIronclad>();
    }

    [Fact]
    public void CleanseExhaustsFromYourDrawPile()
    {
        var gameFight = new GameFight();
        var card = gameFight.Add<Cleanse>();
        gameFight.Add<StrikeIronclad>();
        Assert.Equal(0, Exhausts(gameFight, card));
        gameFight.Add<DefendIronclad>(PileType.Draw);
        Assert.Equal(1, Exhausts(gameFight, card));
    }
}
