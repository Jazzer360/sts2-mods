using System.Text.RegularExpressions;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The mod's code in the game's hooks runs outside the frame's own run, so only OverlayHost.TimeHook shows it (the dev dashboard's
/// "hook:" entries, the slow-frame log's hook share). Every Spire QoL Harmony patch method starts with one (Main Saves has no frame loop
/// to show it in).
/// </summary>
public class HookTimingTests
{
    private static readonly Regex PatchMethod = new(@"new HarmonyMethod\(typeof\((\w+)\), nameof\((\w+)\)\)");

    [Fact]
    public void EveryHarmonyPatchIsTimed()
    {
        var sources = RepoFiles.Sources("mods/SpireQoL").Where(source => !source.Path.Contains("/obj/") && !source.Path.Contains("/bin/")).ToList();
        var untimed = new List<string>();
        foreach (var (path, lines) in sources)
            foreach (Match match in lines.SelectMany(line => PatchMethod.Matches(line)))
            {
                var typeName = match.Groups[1].Value;
                var method = match.Groups[2].Value;
                var declaration = new Regex($@"\bstatic\s+\w+\s+{method}\(");
                var found = false;
                foreach (var (file, fileLines) in sources.Where(source => source.Lines.Any(line => Regex.IsMatch(line, $@"\b(class|record)\s+{typeName}\b"))))
                    for (var i = 0; i < fileLines.Length && !found; i++)
                        if (declaration.IsMatch(fileLines[i]))
                            found = fileLines.Skip(i + 1).Take(3).Any(line => line.Contains("TimeHook("));
                if (!found) untimed.Add($"{typeName}.{method} (patched in {path})");
            }
        Assert.True(untimed.Count == 0, "Harmony patches that don't start with OverlayHost.TimeHook: " + string.Join(", ", untimed.Distinct()));
    }
}
