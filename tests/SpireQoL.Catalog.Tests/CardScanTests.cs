using System.Reflection;
using SpireQoL.Diagnostics;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The card scan (Diagnostics/CardCoverage) against every card in the installed game, at release time instead of only in
/// players' games: each card whose code does something a per-card rule covers (draws, channels, applies a power, removes block,
/// sets off poison, reacts to its kills, draws on a condition...) is counted or reviewed. A card added in a game update fails
/// here before anyone plays it.
/// </summary>
public class CardScanTests
{
    /// <summary>
    /// Cards that act on every player, which the simulator applies to you only: open multiplayer gaps (docs/TRACKER.md), reported
    /// in game only with teammates. A card leaving this list (now simulated) or joining it fails the test, so the list stays true.
    /// </summary>
    private static readonly string[] KnownMultiplayerGaps =
        { "BladeSymphony", "GlimpseBeyond", "HuddleUp", "LegionOfBone" };

    private static IReadOnlyDictionary<string, List<string>> Issues()
    {
        Assert.True(GameModels.EnsureLoaded() > 500, "the game's cards didn't load");
        return CardCoverage.Issues;
    }

    [Fact]
    public void EveryCardIsCountedOrReviewed()
    {
        var missing = Issues()
            .Select(kv => (kv.Key, Single: kv.Value.Where(i => !i.StartsWith(CardCoverage.TeammatesOnly)).ToList()))
            .Where(x => x.Single.Count > 0)
            .Select(x => $"{x.Key}: {string.Join("; ", x.Single)}").OrderBy(s => s).ToList();
        Assert.True(missing.Count == 0, "Cards the scan flags (count them in their rule, or add a Reviewed reason):\n" + string.Join("\n", missing));
    }

    [Fact]
    public void MultiplayerGapsAreTheKnownOnes()
    {
        var found = Issues().Where(kv => kv.Value.Any(i => i.StartsWith(CardCoverage.TeammatesOnly))).Select(kv => kv.Key).OrderBy(n => n).ToArray();
        Assert.Equal(KnownMultiplayerGaps, found);
    }

    [Fact]
    public void TheScanSeesWhatItsChecksLookFor()
    {
        // An empty scan would pass silently: each check's trigger must be found in a card known to have it.
        GameModels.EnsureLoaded();
        var onPlayCalls = typeof(CardCoverage).GetMethod("OnPlayCalls", BindingFlags.Static | BindingFlags.NonPublic)!;
        HashSet<string> Calls(string card) =>
            (HashSet<string>)onPlayCalls.Invoke(null, new object[] { typeof(MegaCrit.Sts2.Core.Models.CardModel).Assembly.GetTypes().Single(t => t.Name == card) })!;
        Assert.Contains("CardPileCmd.Draw", Calls("Impatience"));
        Assert.Contains("DamageResult.get_WasTargetKilled", Calls("EchoingSlash"));
        Assert.Contains("CreatureCmd.LoseBlock", Calls("Expose"));
        Assert.Contains("PoisonPower.Trigger", Calls("Outbreak"));
        Assert.Contains("OrbCmd.Channel", Calls("Zap"));
    }
}
