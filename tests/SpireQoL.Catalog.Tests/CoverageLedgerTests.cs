using SpireQoL.Diagnostics;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Every game class with combat hooks in the installed game has a reviewed entry (covered, ignore or gap), with the hooks the ledger was
/// reviewed for: the coverage script only prints a missing one, and the game only logs it at load (v0.114.0's Hard to Kill went unreviewed
/// through the update until a dev review read the log).
/// </summary>
[Collection(GameFightCollection.Name)]
public class CoverageLedgerTests
{
    [Fact]
    public void EveryClassWithCombatHooksIsReviewed()
    {
        _ = new GameFight(); // the game's models loaded
        var unreviewed = CoverageLedger.ByType.Values
            .Where(entry => entry.Status is CoverageLedger.Status.Unreviewed or CoverageLedger.Status.New or CoverageLedger.Status.Changed)
            .Select(entry => $"{entry.Type}: {entry.Status}").Order(StringComparer.Ordinal).ToList();
        Assert.True(unreviewed.Count == 0,
            "Classes with combat hooks the ledger hasn't reviewed (read their hook bodies, add CoverageLedger.Reviewed entries; docs/DEVELOPMENT.md \"After a game patch\"):\n"
            + string.Join("\n", unreviewed));
    }
}
