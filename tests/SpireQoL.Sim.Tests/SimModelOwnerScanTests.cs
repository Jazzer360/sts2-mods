using System.Text.RegularExpressions;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Every model runs for its owner, you or a teammate (decision #75: the team is one evaluation). A model line that names your
/// player directly (Who.You, or your SimPlayer through s.P), in a hand-written model or the read model, would make a teammate's copy of that relic or power act as yours or
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
        ["if (owner != Who.You) return null;"] = "a tab judged on how your turn ends, on your cards: your relic's or power's (as before the rules moved to the models; a teammate's shows on their Incoming Damage)",
        ["var hp = start.You.Hp;"] = "Red Skull's outcome tab is yours only (the line before returns for anyone else): your HP",
        ["var threshold = Threshold(start, owner, start.You);"] = "Red Skull's outcome tab is yours only: its threshold on your max HP",
        // The read models (Sim/Programs/ReadModel.cs): every class read from its code runs through it.
        ["Owner = Who.You, Hook = HookName.OnPlay, Card = card"] = "an enchantment's OnPlay draws counted outside a simulation (the Return tags' hand size): the card in your hand, yours",
        ["HookOutcome.KillsOne => owner == Who.You ||"] = "a tab judged on how your turn ends, on your cards: yours, or any player's whose death code rewards each player on its side (Compost: decision #277)",
        ["HookOutcome.BreaksBlock => owner == Who.You"] = "Hand Drill's tab judged on the outcome, on your cards: the play's breaks are its own player's (a teammate's Hand Drill isn't set off by your play: decision #285)",
        ["if (owner == Who.You)"] = "a reaction to its owner losing HP, on your cards: the play's HP loss is its own player's (a teammate's tab would be judged on HP your play doesn't cost them)",
        ["query.Whose == HistoryWhose.Anyone ? Who.You : Who.None"] = "an enemy's model asking the fight's log for anyone's entries: every player's ledger holds them, yours among them",
        // SimModels.IsVisible (decision #331): PowerModel.IsVisible asks LocalContext.IsMe, the local player, who is always you in a simulation.
        ["else if (aimedAt != Who.None && aimedAt != Who.You && !Who.IsEnemy(aimedAt)) return false;"] = "PowerModel.IsVisible: a power whose Target is a player other than the local one (LocalContext.IsMe: you) is hidden",
        ["case PowerVisibility.ShownWhenYouApplied: return applier == Who.You;"] = "Impaled's IsVisibleInternal, LocalContext.IsMe(base.Applier): shown when the local player (you) applied it",
    };

    /// <summary>The model files the scan reads: the hand-written models (Sim/Models) and the read model every class read from its code runs through.</summary>
    private static IEnumerable<string> ModelFiles()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var models = Path.Combine(directory.FullName, "mods", "SpireQoL", "Sim", "Models");
            if (Directory.Exists(models)) return Directory.GetFiles(models, "*.cs").Append(Path.Combine(directory.FullName, "mods", "SpireQoL", "Sim", "Programs", "ReadModel.cs"));
        }
        throw new DirectoryNotFoundException("mods/SpireQoL/Sim/Models not found above the test's folder");
    }

    private static readonly Regex NamesYou = new(@"Who\.You\b|\b(?!Who\b)[a-z]\w*\.You\b");

    [Fact]
    public void EveryModelLineThatNamesYouIsReviewed()
    {
        var used = new HashSet<string>();
        var unreviewed = new List<string>();
        foreach (var file in ModelFiles())
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
