using SpireQoL.Cards;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #122: the settings by kind of tab and Show chances, as a tab is added to a card. The audits still see every tab
/// predicted (CardDecor.Predicted), whatever is shown.
/// </summary>
public class TabKindSettingsTests
{
    private static CardDecor Decor(QolSettings s) => new CardDecorFrame { Filter = s.TabEnabled, Shape = s.Shown }.For(new MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad());

    [Fact]
    public void WarningsOnlyHidesPlainTabsButKeepsThemPredicted()
    {
        var d = Decor(new QolSettings { PlainTabs = false });
        Assert.False(d.AddTab(new TabSpec("relic:PenNib", null)));
        Assert.True(d.AddTab(new TabSpec("relic:ArtOfWar", null, Crossed: true)));
        Assert.Equal(new[] { "relic:ArtOfWar" }, d.Tabs.Select(t => t.Key));
        Assert.Equal(2, d.Predicted.Count);
    }

    [Fact]
    public void CrossedOffHidesWarnings()
    {
        var d = Decor(new QolSettings { CrossedTabs = false });
        Assert.False(d.AddTab(new TabSpec("relic:ArtOfWar", null, Crossed: true)));
        Assert.True(d.AddTab(new TabSpec("relic:PenNib", null)));
    }

    [Fact]
    public void AimMarkerOffShowsTheTabWithoutIt()
    {
        var d = Decor(new QolSettings { AimMarker = false });
        Assert.True(d.AddTab(new TabSpec("lethal", null, Aimed: true)));
        Assert.False(Assert.Single(d.Tabs).Aimed);
        Assert.True(Assert.Single(d.Predicted).Aimed);
    }

    [Fact]
    public void ChancesOffHidesTheDrawChanceTabAndEveryChance()
    {
        var s = new QolSettings { Chances = false };
        Assert.False(Decor(s).AddTab(new TabSpec(QolSettings.DrawChanceKey, null, Text: "40%")));
        Assert.False(s.ShowDeathChance);
        Assert.False(s.ShowIntentOdds);
        Assert.False(s.ShowEventOdds);
        var on = new QolSettings();
        Assert.True(on.ShowDeathChance && on.ShowIntentOdds && on.ShowEventOdds);
    }
}
