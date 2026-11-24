using System.Text;
using SpireQoL.Menu;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// What each Proc Tags row shows of its bonus (SourceEntry.Shows: decision #250), as the simulator's tests read it (ReadModels/tab-rows.json: the
/// rows that say otherwise than TabShows.Default). The tests run the tab rules the game shows with the rows' own display configuration, so the file
/// is the catalog's (SPIREQOL_UPDATE_SNAPSHOT=1 regenerates it).
/// </summary>
public class TabRowsTests
{
    private static string TabRowsFile => Path.Combine(HookProgramTests.RepoRoot(), "tests", "SpireQoL.Sim.Tests", "ReadModels", "tab-rows.json");

    /// <summary>The rows that show otherwise than the default, by class, as the file holds them.</summary>
    internal static string Text()
    {
        var rows = SettingsCatalog.Sources.Where(source => source.TagPrefix == "proc:" && source.Tag != null && source.Shows != TabShows.Default)
            .OrderBy(source => source.Type, StringComparer.Ordinal).ToList();
        var text = new StringBuilder("{\n");
        for (var i = 0; i < rows.Count; i++)
            text.Append($"  \"{rows[i].Type}\": \"{rows[i].Shows}\"{(i < rows.Count - 1 ? "," : "")}\n");
        return text.Append("}\n").ToString();
    }

    [Fact]
    public void TheSimulatorTestsReadTheRowsAsTheCatalogHasThem()
    {
        var actual = Text();
        if (Environment.GetEnvironmentVariable("SPIREQOL_UPDATE_SNAPSHOT") == "1")
        {
            File.WriteAllText(TabRowsFile, actual);
            return;
        }
        Assert.True(File.Exists(TabRowsFile), $"missing {TabRowsFile}: generate it with SPIREQOL_UPDATE_SNAPSHOT=1");
        Assert.True(File.ReadAllText(TabRowsFile).ReplaceLineEndings("\n") == actual,
            $"{TabRowsFile} isn't what the settings rows show: regenerate it with SPIREQOL_UPDATE_SNAPSHOT=1");
    }

    [Fact]
    public void ARowShowsSomething()
    {
        // A row whose tab shows none of its bonus's directions would be a switch for nothing.
        Assert.DoesNotContain(SettingsCatalog.Sources, source => source.Tag != null && source.Shows == TabShows.None);
    }
}
