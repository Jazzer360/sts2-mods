using System.Text.RegularExpressions;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The docs name the code they describe (`SimEngine.ChannelOrb`, `CardEnergy.Reviewed`), and a rename leaves them pointing at nothing
/// (decision #150). Every backticked Type.Member in the docs whose type is declared in this repo must still name a member of it: an
/// identifier in the code (comments left out) of a file that declares that type. The game's own names (CardCmd.Exhaust, and a model
/// named as the game class it models: FeralPower) aren't this repo's to check. History stays as written: only the docs that describe
/// the code as it is (FEATURES, SIMULATOR, DEVELOPMENT, CLAUDE.md, README), active decisions (not their Why and Replaced lines) and
/// open tracker items are checked.
/// </summary>
public class DocNamesTests
{
    private static readonly Regex Declared = new(@"\b(?:class|record|struct|interface|enum)\s+(?:struct\s+)?([A-Z]\w*)");
    private static readonly Regex Identifier = new(@"\b[A-Za-z_]\w*\b");
    private static readonly Regex Named = new(@"`([A-Z]\w*)\.([A-Za-z_]\w*)[^`]*`");
    private static readonly Regex GameName = new(@"new\(""(\w+)""\)");
    private static readonly HashSet<string> FileExtensions = new() { "cs", "md", "json", "mjs", "ps1", "txt", "csproj", "props", "targets", "g" };

    /// <summary>Each type this repo declares, with the identifiers in the code of the files that declare it.</summary>
    private static Dictionary<string, HashSet<string>> Types()
    {
        var types = new Dictionary<string, HashSet<string>>();
        foreach (var folder in new[] { "mods", "core", "tests" })
        foreach (var (_, lines) in RepoFiles.Sources(folder))
        {
            var code = lines.Select(line => line.Contains("//", StringComparison.Ordinal) ? line[..line.IndexOf("//", StringComparison.Ordinal)] : line).ToList();
            var words = code.SelectMany(line => Identifier.Matches(line).Select(match => match.Value)).ToHashSet();
            foreach (var line in code)
            foreach (Match match in Declared.Matches(line))
            {
                if (!types.TryGetValue(match.Groups[1].Value, out var members)) types[match.Groups[1].Value] = members = new HashSet<string>();
                members.UnionWith(words);
            }
        }
        return types;
    }

    /// <summary>The game's own class names (Sim/GameNames.g.cs): a doc citing one means the game's class, even where a model shares it.</summary>
    private static HashSet<string> GameClasses() =>
        File.ReadAllLines(Path.Combine(RepoFiles.Root(), "mods", "SpireQoL", "Sim", "GameNames.g.cs"))
            .Select(line => GameName.Match(line)).Where(match => match.Success).Select(match => match.Groups[1].Value).ToHashSet();

    /// <summary>The lines of a doc that describe the code as it is: not a superseded decision, what a decision replaced, or a closed tracker item.</summary>
    private static IEnumerable<(int Line, string Text)> Current(string doc, string[] lines)
    {
        var name = Path.GetFileName(doc);
        var active = true;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (name == "DECISIONS.md" && line.StartsWith("### ", StringComparison.Ordinal)) active = line.Contains("— active", StringComparison.Ordinal);
            // A decision's Why and Replaced lines tell what came before it.
            if (name == "DECISIONS.md" && (!active || line.StartsWith("*Replaced:*", StringComparison.Ordinal) || line.StartsWith("*Why:*", StringComparison.Ordinal))) continue;
            // Only open tracker items: a done or partly done one ([x], [~]) is a log of how it went.
            if (name == "TRACKER.md" && !line.TrimStart().StartsWith("- [ ]", StringComparison.Ordinal)) continue;
            yield return (i + 1, line);
        }
    }

    [Fact]
    public void EveryCodeNameInTheDocsStillExists()
    {
        var types = Types();
        var game = GameClasses();
        var docs = Directory.EnumerateFiles(Path.Combine(RepoFiles.Root(), "docs"), "*.md")
            .Append(Path.Combine(RepoFiles.Root(), "CLAUDE.md")).Append(Path.Combine(RepoFiles.Root(), "README.md"));
        var stale = new List<string>();
        foreach (var doc in docs)
        foreach (var (line, text) in Current(doc, File.ReadAllLines(doc)))
        foreach (Match match in Named.Matches(text))
        {
            var (type, member) = (match.Groups[1].Value, match.Groups[2].Value);
            if (FileExtensions.Contains(member) || game.Contains(type)) continue;
            if (types.TryGetValue(type, out var members) && !members.Contains(member))
                stale.Add($"{Path.GetFileName(doc)}:{line}: {type}.{member}");
        }
        Assert.True(stale.Count == 0, $"Code names in the docs that no longer exist ({stale.Count}):\n" + string.Join("\n", stale.Distinct()));
    }
}
