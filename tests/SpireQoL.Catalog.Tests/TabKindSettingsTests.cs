using SpireQoL.Cards;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #122: the settings by kind of tab and Show chances, as a tab is added to a card. The audits still see every tab
/// predicted (CardDecor.Predicted), whatever is shown.
/// </summary>
public class TabKindSettingsTests
{
    private static CardDecor Decor(QolSettings settings) => new CardDecorFrame { Filter = settings.TabEnabled, Shape = settings.Shown }.For(new MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad());

    [Fact]
    public void WarningsOnlyHidesPlainTabsButKeepsThemPredicted()
    {
        var decor = Decor(new QolSettings { PlainTabs = false });
        Assert.False(decor.AddTab(new TabSpec("relic:PenNib", Icon: null)));
        Assert.True(decor.AddTab(new TabSpec("relic:ArtOfWar", Icon: null, Crossed: true)));
        Assert.Equal(new[] { "relic:ArtOfWar" }, decor.Tabs.Select(tab => tab.Key));
        Assert.Equal(2, decor.Predicted.Count);
    }

    [Fact]
    public void TabsOnCardsOffHidesEveryKind()
    {
        var decor = Decor(new QolSettings { CardTabs = false });
        Assert.False(decor.AddTab(new TabSpec("relic:PenNib", Icon: null)));
        Assert.False(decor.AddTab(new TabSpec("relic:ArtOfWar", Icon: null, Crossed: true)));
        Assert.Empty(decor.Tabs);
        Assert.Equal(2, decor.Predicted.Count);
    }

    [Fact]
    public void CrossedOffHidesWarnings()
    {
        var decor = Decor(new QolSettings { CrossedTabs = false });
        Assert.False(decor.AddTab(new TabSpec("relic:ArtOfWar", Icon: null, Crossed: true)));
        Assert.True(decor.AddTab(new TabSpec("relic:PenNib", Icon: null)));
    }

    [Fact]
    public void AimMarkerOffShowsTheTabWithoutIt()
    {
        var decor = Decor(new QolSettings { AimMarker = false });
        Assert.True(decor.AddTab(new TabSpec("lethal", Icon: null, Aimed: true)));
        Assert.False(Assert.Single(decor.Tabs).Aimed);
        Assert.True(Assert.Single(decor.Predicted).Aimed);
    }

    [Fact]
    public void ChancesOffHidesTheDrawChanceTabAndEveryChance()
    {
        var settings = new QolSettings { Chances = false };
        Assert.False(Decor(settings).AddTab(new TabSpec(QolSettings.DrawChanceKey, Icon: null, Text: "40%")));
        Assert.False(settings.ShowDeathChance);
        Assert.False(settings.ShowIntentOdds);
        Assert.False(settings.ShowEventOdds);
        var on = new QolSettings();
        Assert.True(on.ShowDeathChance && on.ShowIntentOdds && on.ShowEventOdds);
    }
}
