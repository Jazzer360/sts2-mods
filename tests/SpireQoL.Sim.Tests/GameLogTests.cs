using System.Text.RegularExpressions;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Decision #158: the game's log is shared with the game and every other mod. Routine diagnostics (each check's lines, its
/// summaries, timings, sessions, coverage) go through ModLog.Detail, which reaches the game's log only in dev mode; Info and Warn are
/// for what's exceptional or rare. A routine family logged at Info or Warn fails here.
/// </summary>
public class GameLogTests
{
    /// <summary>A routine family at Info; a timing at Warn; a mismatch's headline or details at Warn (a failure stays a warning).</summary>
    private static readonly Regex Routine = new(
        @"\.Info\(\$?""\[(audit|perf|session|coverage)\]" + @"|\.Warn\(\$?""\[perf\]" + @"|\.Warn\(\$?""\[audit\] (played|turn|  )");

    [Fact]
    public void RoutineDiagnosticsStayOutOfTheGameLog()
    {
        var root = RepoFiles.Root();
        var found = new List<string>();
        var separator = Path.DirectorySeparatorChar;
        foreach (var dir in new[] { "mods", "core" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, dir), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{separator}bin{separator}") || file.Contains($"{separator}obj{separator}")) continue;
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                    if (Routine.IsMatch(lines[i])) found.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
            }
        Assert.True(found.Count == 0, "routine diagnostics logged at Info or Warn (use ModLog.Detail): " + string.Join(", ", found));
    }
}
