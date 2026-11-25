using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireQoL.Features;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A relic's rest-site map hint shows while it still adds its rest-site option (decision #296), read from the code: its own
/// TryModifyRestSiteOptions, or, when an enchantment its code gives a card adds the option (v0.114.0: Clone.TryModifyRestSiteOptions, Pael's
/// Growth's CardCmd.Enchant&lt;Clone&gt;), only while a card in your deck holds that enchantment. The relics are named here as the ones the rule
/// was written from; the rule names none.
/// </summary>
[Collection(GameFightCollection.Name)]
public class RestSiteOptionTests
{
    [Fact]
    public void PaelsGrowthsOptionIsItsClonedCards()
    {
        var gameFight = new GameFight();
        var plain = gameFight.Combat.CreateCard(ModelDb.Card<StrikeIronclad>(), gameFight.Me);
        var cloned = gameFight.Combat.CreateCard(ModelDb.Card<DefendIronclad>(), gameFight.Me);
        cloned.EnchantInternal(ModelDb.Enchantment<Clone>().ToMutable(), 4m);
        if (typeof(PaelsGrowth).GetMethod(nameof(AbstractModel.TryModifyRestSiteOptions))!.DeclaringType == typeof(PaelsGrowth))
        {
            // main (v0.111.1): the relic's own option, whatever your deck holds.
            Assert.True(RunReminders.OffersRestSiteOption(typeof(PaelsGrowth), new[] { plain }));
            return;
        }
        Assert.Contains(typeof(Clone), RunReminders.RestSiteEnchantmentsOf(typeof(PaelsGrowth)));
        Assert.False(RunReminders.OffersRestSiteOption(typeof(PaelsGrowth), new[] { plain }));
        Assert.True(RunReminders.OffersRestSiteOption(typeof(PaelsGrowth), new[] { plain, cloned }));
    }

    [Fact]
    public void ARelicsOwnOptionAndARestSiteRelicWithNoOptionAlwaysShow()
    {
        Assert.True(RunReminders.OffersRestSiteOption(typeof(Girya), Array.Empty<CardModel>()));
        Assert.True(RunReminders.OffersRestSiteOption(typeof(EternalFeather), Array.Empty<CardModel>()));
    }
}
