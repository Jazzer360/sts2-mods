using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Doc comments stay on the member they describe. When code moves, its summary can be left behind on top of another member's,
/// and IntelliSense then shows the wrong text (or two texts run together) for that member. Two summary blocks in a row are that.
/// </summary>
public class DocCommentTests
{
    private static string RepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "mods")) && Directory.Exists(Path.Combine(directory.FullName, "core"))) return directory.FullName;
        throw new DirectoryNotFoundException("the repository root (mods/ and core/) not found above the test's folder");
    }

    private static IEnumerable<string> SourceFiles()
    {
        var root = RepoRoot();
        return new[] { "mods", "core" }
            .SelectMany(dir => Directory.EnumerateFiles(Path.Combine(root, dir), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => p is "bin" or "obj"));
    }

    [Fact]
    public void NoMemberHasTwoSummaries()
    {
        var stacked = new List<string>();
        foreach (var file in SourceFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 1; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("/// <summary>") && lines[i - 1].TrimEnd().EndsWith("</summary>"))
                    stacked.Add($"{Path.GetFileName(file)}:{i + 1}");
            }
        }
        Assert.True(stacked.Count == 0, "Two summaries in a row (one belongs to another member, or is out of date): " + string.Join(", ", stacked));
    }

    [Fact]
    public void NoMangledCharacters()
    {
        // UTF-8 text read back as Windows-1252 once ("→" became "â†’").
        var mangled = SourceFiles().Where(f => File.ReadAllText(f).Contains("â€") || File.ReadAllText(f).Contains("â†")).Select(Path.GetFileName).ToList();
        Assert.True(mangled.Count == 0, "Mangled characters in: " + string.Join(", ", mangled));
    }
}
