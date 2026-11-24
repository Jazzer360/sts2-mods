using System.Text.Json;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The owner board: which kinds of creature each power can be on (a player, an enemy, a pet), read from where both game branches' code applies
/// it (the catalog tests' OwnerBoard writes ReadModels/power-owners.json and holds it to the code). A power the file doesn't name can be on
/// anything. The parity test compares a read model only on owners the game can give it.
/// </summary>
internal static class OwnerBoardFile
{
    public const string Player = "Player", Enemy = "Enemy", Pet = "Pet";

    private static readonly Lazy<Dictionary<string, string[]>> Board = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ReadModels", "power-owners.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(path)) ?? new() : new();
    });

    /// <summary>The kinds of creature the power can be on (every kind for one the board doesn't name).</summary>
    public static IReadOnlyCollection<string> KindsOf(string power) => Board.Value.TryGetValue(power, out var kinds) ? kinds : new[] { Player, Enemy, Pet };
}
