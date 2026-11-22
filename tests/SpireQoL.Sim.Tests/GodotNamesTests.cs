using System.Text.RegularExpressions;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Decision #173: a string passed where Godot takes a StringName or NodePath is converted on every call, a new object each time
/// (garbage every frame in code that runs every frame). Theme items and node paths go through GodotNames, which makes each once.
/// </summary>
public class GodotNamesTests
{
    private static readonly Regex Literal = new(
        @"\b(?:(?:Add|Remove|Has)Theme(?:Color|Constant|Font|FontSize|Icon|Stylebox)Override|GetTheme(?:Color|Constant|Font|FontSize|Icon|Stylebox))\(""" +@"|\b(?:GetNodeOrNull|GetNode|HasNode)(?:<[^>()]*>)?\(""");

    [Fact]
    public void GodotNamesAreMadeOnce()
    {
        var found = new List<string>();
        foreach (var folder in new[] { "core", "mods/SpireQoL" })
            foreach (var (path, lines) in RepoFiles.Sources(folder))
            {
                if (path.Contains("/obj/") || path.Contains("/bin/")) continue;
                for (var i = 0; i < lines.Length; i++)
                    if (Literal.IsMatch(lines[i])) found.Add($"{path}:{i + 1}");
            }
        Assert.True(found.Count == 0, "Godot names as string literals (use GodotNames.Name or GodotNames.Path): " + string.Join(", ", found));
    }
}
