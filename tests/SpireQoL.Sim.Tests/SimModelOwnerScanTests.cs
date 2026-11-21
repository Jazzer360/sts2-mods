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
        ["if (owner == Who.You) state.ParryingShieldFired = true;"] = "the forecast audit's flag for your Parrying Shield; a teammate's still deals its damage",
        ["ProcBlockFor(branch, owner, Name, Who.You, amount, Unpowered)"] = "a teammate's power giving you block (Covering Fire gives every other player its block)",
        ["hit.Dealer != Who.You || !hit.Props.IsPoweredAttack()"] = "ImpaledPower is your arrows in an enemy (a teammate's are TeammatesImpaledPower)",
        ["CardEvent.ArrowDislodged, Who.You"] = "ImpaledPower is your arrows: they come back to you",
        ["play.Owner != Who.You"] = "Withering Presence counts its Target's cards; the adapter loads only the one aimed at you (SimReaders.Powers)",
        ["owner == Who.You && attack.Dealer == Who.YourOsty && state.You.OstyHp"] = "your Osty's attacks (a teammate's Osty doesn't attack in your turn)",
        ["dealer == Who.YourOsty ? Who.You : dealer"] = "your Osty's damage counts as yours",
        ["OstyLoses(state, Who.You, hit)"] = "Thorns hitting back at your Osty",
        ["owner != Who.You ? null"] = "a tab judged on how your turn ends, on your cards: your relic's or power's (as before the rules moved to the models; a teammate's shows on their Incoming Damage)",
        ["owner == Who.You ? new(Harmful: false, outcome => outcome.PlayBreaksBlockInAll)"] = "a tab judged on how your turn ends, on your cards: your relic's or power's (as before the rules moved to the models; a teammate's shows on their Incoming Damage)",
        ["owner == Who.You ? OutcomeRules.YouLoseHp"] = "a tab judged on how your turn ends, on your cards: your relic's or power's (as before the rules moved to the models; a teammate's shows on their Incoming Damage)",
        ["owner == Who.You && !((RelicUse)SimEngine.RelicsOf(start, owner).DataOf(ThisRelic)).HasFlag(RelicUse.UsedThisTurn)"] = "a tab judged on how your turn ends, on your cards: your relic's or power's (as before the rules moved to the models; a teammate's shows on their Incoming Damage)",
        ["if (owner != Who.You) return null;"] = "a tab judged on how your turn ends, on your cards: your relic's or power's (as before the rules moved to the models; a teammate's shows on their Incoming Damage)",
        ["owner == Who.You ? OutcomeRules.KillsOne"] = "a tab judged on how your turn ends, on your cards: your relic's or power's (as before the rules moved to the models; a teammate's shows on their Incoming Damage)",
        ["var hp = start.You.Hp;"] = "Red Skull's outcome tab is yours only (the line before returns for anyone else): your HP",
        ["(owner == Who.You && damage.Dealer == Who.YourOsty)"] = "your Osty's attacks count as yours for your powers",
        ["(owner == Who.You && hit.Dealer == Who.YourOsty)"] = "your Osty's attacks count as yours for your powers",
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
                if (line.StartsWith("//") || !NamesYou.IsMatch(line)) continue;
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
