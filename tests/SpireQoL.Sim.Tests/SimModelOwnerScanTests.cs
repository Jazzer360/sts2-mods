using System.Text.RegularExpressions;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Every model runs for its owner, you or a teammate (decision #75: the team is one evaluation). A model line that names your
/// player directly (Who.You, or your SimPlayer through s.P) would make a teammate's copy of that relic or power act as yours or
/// not at all, so each one is reviewed here with why it's right (decision #80). A new one fails until it's reviewed: use the
/// owner (SimEngine.PlayerOf, PowersOf, the hook's owner) instead, or add it with its reason.
/// </summary>
public class SimModelOwnerScanTests
{
    /// <summary>A fragment of each reviewed line, and why naming you there is right.</summary>
    private static readonly Dictionary<string, string> Reviewed = new()
    {
        ["private static int SlotOf(int applier) => applier == Who.You ? 0"] = "Tag Team's Data names each applier's slot (one copy per applier, instanced): you and each teammate by id, not the owner",
        ["state.CurrentPlay is not { Owner: Who.You }"] = "ImpaledPower is your arrows in an enemy, counted down by the hits from your cards (a teammate's arrows are TeammatesImpaledPower)",
        ["CardEvent.ArrowDislodged, Who.You"] = "ImpaledPower is your arrows: they come back to you",
        ["play.Owner != Who.You"] = "Withering Presence counts its Target's cards; the adapter loads only the one aimed at you (SimReaders.Powers)",
        ["(owner == Who.You && breaker == Who.YourOsty)"] = "Hand Drill: your Osty breaking block counts as you (only your Osty is a dealer in the simulator; a teammate's Osty's hits are captured numbers)",
        ["owner == Who.You ? new(Harmful: false, outcome => outcome.PlayBreaksBlockInAll)"] = "a tab judged on how your turn ends, on your cards: your relic's or power's (as before the rules moved to the models; a teammate's shows on their Incoming Damage)",
        ["if (owner != Who.You) return null;"] = "a tab judged on how your turn ends, on your cards: your relic's or power's (as before the rules moved to the models; a teammate's shows on their Incoming Damage)",
        ["var hp = start.You.Hp;"] = "Red Skull's outcome tab is yours only (the line before returns for anyone else): your HP",
        ["(owner == Who.You && damage.Dealer == Who.YourOsty)"] = "your Osty's attacks count as yours for your powers",
    };

    private static string ModelsDir()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var models = Path.Combine(directory.FullName, "mods", "SpireQoL", "Sim", "Models");
            if (Directory.Exists(models)) return models;
        }
        throw new DirectoryNotFoundException("mods/SpireQoL/Sim/Models not found above the test's folder");
    }

    private static readonly Regex NamesYou = new(@"Who\.You\b|\b(?!Who\b)[a-z]\w*\.You\b");

    [Fact]
    public void EveryModelLineThatNamesYouIsReviewed()
    {
        var used = new HashSet<string>();
        var unreviewed = new List<string>();
        foreach (var file in Directory.GetFiles(ModelsDir(), "*.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("//", StringComparison.Ordinal) || !NamesYou.IsMatch(line)) continue;
                var key = Reviewed.Keys.FirstOrDefault(line.Contains);
                if (key == null) unreviewed.Add($"{Path.GetFileName(file)}:{i + 1}: {line}");
                else used.Add(key);
            }
        }
        Assert.True(unreviewed.Count == 0,
            "Model lines that name your player (a teammate's copy wouldn't act as theirs): use the owner, or review them here:\n" + string.Join("\n", unreviewed));
        var stale = Reviewed.Keys.Except(used).ToList();
        Assert.True(stale.Count == 0, "Reviewed entries no longer in the models: " + string.Join(", ", stale));
    }
}
