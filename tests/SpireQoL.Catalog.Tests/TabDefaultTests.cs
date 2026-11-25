using SpireQoL.Menu;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #153: the tabs of powers that act on every card you play, whatever the card, and never run out start off, and so do the
/// relics' crossed tabs nearly every card gets (Cloak Clasp, Obsidian Vice: decision #245); every other tab starts on, Tender's and Slow's
/// too (decision #263), and one you've switched keeps your choice.
/// </summary>
public class TabDefaultTests
{
    private static readonly string[] EveryCard =
    {
        "AfterimagePower", "GravityPower", "SerpentFormPower", "MonologuePower", "TheSealedThronePower",
        "StranglePower", "OblivionPower", "CloakClasp", "ObsidianVice",
    };

    [Fact]
    public void EveryCardPowersStartOff()
    {
        Assert.Equal(EveryCard.Select(power => "proc:" + power).OrderBy(key => key), SettingsCatalog.TabsOffByDefault.OrderBy(key => key));
        var settings = new QolSettings();
        foreach (var power in EveryCard) Assert.False(settings.TabEnabled("proc:" + power), power);
    }

    [Fact]
    public void OtherTabsStartOnAndASwitchedTabKeepsItsChoice()
    {
        var settings = new QolSettings();
        Assert.True(settings.TabEnabled("proc:VigorPower")); // on every card until it's used up: stays on
        Assert.True(settings.TabEnabled("proc:RagePower"));  // attacks only
        // Tender and Slow on every card start on (decision #263: the owner's call).
        Assert.True(settings.TabEnabled("proc:TenderPower"));
        Assert.True(settings.TabEnabled("proc:SlowPower"));
        settings.Tabs["proc:SerpentFormPower"] = true;
        Assert.True(settings.TabEnabled("proc:SerpentFormPower"));
    }
}
