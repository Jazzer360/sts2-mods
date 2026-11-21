using System.Text.RegularExpressions;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Names say what a thing is, in every scope (decision #138): a reader should know what a line does without tracing back up to where
/// each name came from. So no locals, parameters or lambda parameters of one or two letters (a state in a lambda is a branch, a damage
/// event a hit, a card a card), except a for loop's own counter (i, j) and the words id, me, ex.
/// </summary>
public class NamingTests
{
    /// <summary>Short words that are the word itself: hp is the game's own name for the stat, id an id.</summary>
    private static readonly HashSet<string> Words = new() { "id", "hp", "me", "ex", "ok", "to", "at", "on", "_" };

    private static readonly Regex[] Declarations =
    {
        new(@"\b(?:var|out var|foreach \(var|is \{[^}]*\}|is not \{[^}]*\}) (?<name>[a-z_]\w?)\b(?! *\()"),
        new(@"(?:\(|, )(?<name>[a-z_]\w?)(?:, \w+)* *\) *=>"),
        new(@"(?<![\w.])(?<name>[a-z_]\w?) *=>"),
        new(@"\b(?:[A-Z]\w*(?:<[^>()]*>)?\??|int|string|bool|decimal|double|long|float|object) (?<name>[a-z_]\w?)(?=[,)=;])"),
    };

    private static string RepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "mods")) && Directory.Exists(Path.Combine(directory.FullName, "core"))) return directory.FullName;
        throw new DirectoryNotFoundException("the repository root (mods/ and core/) not found above the test's folder");
    }

    [Fact]
    public void NoOneOrTwoLetterNames()
    {
        var root = RepoRoot();
        var found = new List<string>();
        foreach (var dir in new[] { "mods", "core", "tests" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, dir), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj") || file.EndsWith(".g.cs")) continue;
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = Regex.Replace(lines[i], "\"(?:[^\"\\\\]|\\\\.)*\"", "\"\""); // not inside strings
                    if (line.TrimStart().StartsWith("//")) continue;
                    foreach (var pattern in Declarations)
                        foreach (Match match in pattern.Matches(line))
                        {
                            var name = match.Groups["name"].Value;
                            if (Words.Contains(name)) continue;
                            if (name is "i" or "j" && line.Contains("for (var " + name + " = ")) continue;
                            found.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {name}");
                        }
                }
            }
        }
        Assert.True(found.Count == 0, $"{found.Count} short names (say what each is): " + string.Join("; ", found.Distinct().Take(60)));
    }
}
