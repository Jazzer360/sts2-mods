using System.Text.Json;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// What each source's settings row shows of its bonus (TabShows: decision #250), from ReadModels/tab-rows.json, which the catalog tests write
/// from SettingsCatalog and hold to it (TabRowsTests): the game isn't here, and the rows are the mod's. A class the file doesn't name shows what
/// every row shows unless it says otherwise (TabShows.Default).
/// </summary>
internal static class TabRowsFile
{
    private static readonly Lazy<Dictionary<string, TabShows>> Rows = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ReadModels", "tab-rows.json");
        if (!File.Exists(path)) return new Dictionary<string, TabShows>(StringComparer.Ordinal);
        var rows = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new Dictionary<string, string>();
        return rows.ToDictionary(row => row.Key, row => Enum.Parse<TabShows>(row.Value), StringComparer.Ordinal);
    });

    /// <summary>What the class's row shows of its bonus.</summary>
    public static TabShows ShowsOf(string sourceClass) => Rows.Value.TryGetValue(sourceClass, out var shows) ? shows : TabShows.Default;
}
