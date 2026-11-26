using System.Reflection;
using HarmonyLib;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The Relic Trader never offers a relic you hold, which the reading of its trades assumes (decision #354: of the relics its hover tips show, the one you
/// don't hold is the one it gives; the other is the one you give up). Held from the game's code on each branch: its new relics are pulled from the front
/// of your relic grab bag (RelicFactory.PullNextRelicFromFront, through Player.RelicGrabBag's PullFromFront), and obtaining a relic that doesn't stack
/// takes it out of that bag (RelicCmd.Obtain: RelicGrabBag.Remove when it isn't IsStackable), which a player's bag (made without refresh) never refills.
/// A stackable relic, or the fallback Circlet of an empty bag, can be one you hold: then its tips show no relic you don't hold, and its trade stays a gap.
/// </summary>
public class RelicTraderOfferTests
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public static TheoryData<string> Branches() => new() { "beta", "main" };

    /// <summary>Whether the code of <paramref name="type"/>'s methods named <paramref name="method"/> (an async one's state machine too) calls <paramref name="called"/>.</summary>
    private static bool Calls(BranchCode code, string type, string method, string called)
    {
        var declaring = code.Assembly.GetTypes().Single(each => each.Name == type && each.Namespace?.StartsWith("MegaCrit.Sts2.Core", StringComparison.Ordinal) == true);
        var bodies = declaring.GetMethods(All).Where(each => each.Name == method).Cast<MethodBase>()
            .Concat(declaring.GetNestedTypes(All).Where(nested => nested.Name.StartsWith("<" + method + ">", StringComparison.Ordinal)).SelectMany(nested => nested.GetMethods(All)));
        return bodies.Any(body => code.Read(body).Any(instruction => instruction.IsCall && instruction.Member?.FullName == called));
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void ItsNewRelicsComeFromYourGrabBagWhichLosesEveryRelicYouObtain(string branch)
    {
        var path = branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;
        if (!File.Exists(path)) return; // no decompile of this branch here: GameBranchTests says so
        var code = BranchCode.Of(path);
        Assert.True(Calls(code, "RelicTrader", "get_NewRelics", "RelicFactory.PullNextRelicFromFront"), $"{branch}: the Relic Trader's new relics aren't pulled from your grab bag");
        Assert.True(Calls(code, "RelicFactory", "PullNextRelicFromFront", "Player.get_RelicGrabBag") && Calls(code, "RelicFactory", "PullNextRelicFromFront", "RelicGrabBag.PullFromFront"),
            $"{branch}: RelicFactory.PullNextRelicFromFront doesn't pull from the player's grab bag");
        Assert.True(Calls(code, "RelicCmd", "Obtain", "RelicModel.get_IsStackable") && Calls(code, "RelicCmd", "Obtain", "RelicGrabBag.Remove"),
            $"{branch}: obtaining a relic doesn't take it out of your grab bag");
        Assert.True(Calls(code, "Player", "CreateForNewRun", "RelicGrabBag..ctor"), $"{branch}: a new run's player doesn't get its own grab bag");
    }

    [Fact]
    public void APlayersGrabBagIsNeverRefilled()
    {
        // The bag a new run's player gets (new RelicGrabBag()) doesn't refresh an empty rarity with the relics it started with (RefreshRarity), so a relic
        // taken out of it never comes back.
        var refresh = AccessTools.Field(typeof(MegaCrit.Sts2.Core.Runs.RelicGrabBag), "_refreshAllowed");
        Assert.NotNull(refresh);
        Assert.Equal(false, refresh!.GetValue(new MegaCrit.Sts2.Core.Runs.RelicGrabBag()));
    }
}
