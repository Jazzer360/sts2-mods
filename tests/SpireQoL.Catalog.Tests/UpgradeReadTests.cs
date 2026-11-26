using SpireQoL.Sim;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A card of yours that upgrades every card of a pile (decision #363) has the board read each card's upgraded copy where an upgrade changes what's
/// followed: with Armaments+ in hand, an Ethereal card that stays Ethereal once upgraded is no card an upgrade makes unknown, and Armaments+'s play is known.
/// </summary>
[Collection(GameFightCollection.Name)]
public class UpgradeReadTests
{
    [Fact]
    public void ArmamentsPlusReadsYourHandUpgraded()
    {
        GameModels.EnsureLoaded();
        Assert.Equal(UpgradedPiles.Hand, SimAdapter.PilesEveryCardUpgraded(typeof(Armaments)) & UpgradedPiles.Hand);
        // An Ethereal card of the game's that can be upgraded and stays Ethereal (read from the game: none named here).
        var ethereal = ModelDb.AllCards.Where(card => card.Keywords.Contains(CardKeyword.Ethereal) && !card.Keywords.Contains(CardKeyword.Unplayable) && card.MaxUpgradeLevel > 0)
            .OrderBy(card => card.GetType().Name, StringComparer.Ordinal).First(card => SpireQoL.Cards.UpgradedCopy.Of(new GameFight().Combat.CreateCard(card, new GameFight().Me)).Keywords.Contains(CardKeyword.Ethereal));

        int Unknown(bool withArmaments)
        {
            var gameFight = new GameFight();
            if (withArmaments) gameFight.Add<Armaments>(upgraded: true);
            var card = gameFight.Combat.CreateCard(ethereal, gameFight.Me);
            PileType.Hand.GetPile(gameFight.Me).AddInternal(card);
            return SimAdapter.Build(gameFight.Me, gameFight.Combat, out _).You.UpgradableAtTurnEnd;
        }
        Assert.Equal(1, Unknown(withArmaments: false));
        Assert.Equal(0, Unknown(withArmaments: true));
    }
}
