namespace SpireQoL.Sim.Tests;

/// <summary>The repository's own source files, for the tests that scan them (names, doc comments, Data values).</summary>
internal static class RepoFiles
{
    /// <summary>The repository root: the folder above the test's that holds mods/ and core/.</summary>
    public static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "mods")) && Directory.Exists(Path.Combine(directory.FullName, "core"))) return directory.FullName;
        throw new DirectoryNotFoundException("the repository root (mods/ and core/) not found above the test's folder");
    }

    /// <summary>Every .cs file under a folder of the repository, as (its path from the root, its lines).</summary>
    public static IEnumerable<(string Path, string[] Lines)> Sources(string folder) =>
        Directory.EnumerateFiles(System.IO.Path.Combine(Root(), folder), "*.cs", SearchOption.AllDirectories)
            .Select(file => (System.IO.Path.GetRelativePath(Root(), file).Replace('\\', '/'), File.ReadAllLines(file)));
}
