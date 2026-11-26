using System.Reflection;
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

    /// <summary>
    /// The ledger's hooks for main (decision #323) are main's assembly's: each ledger class there overrides the hooks its main row names, or, with no
    /// main row, beta's. Otherwise a main player's game reports a reviewed class as changed (one DLL serves both branches: decision #163).
    /// </summary>
    [Fact]
    public void MainsHooksAreTheLedgersForMain()
    {
        if (!File.Exists(BranchCode.MainPath)) return;
        var code = BranchCode.Of(BranchCode.MainPath);
        var hooks = CoverageLedger.ReviewedHooks.ToHashSet(StringComparer.Ordinal);
        var onMain = CoverageLedger.MainHooks.ToDictionary(row => (row.Folder, row.Type), row => row.Hooks);
        var types = code.Assembly.GetTypes().Where(type => type is { IsClass: true, IsAbstract: false, IsNested: false } && type.Namespace?.StartsWith("MegaCrit.Sts2.Core.Models.", StringComparison.Ordinal) == true)
            .ToDictionary(type => (type.Namespace!["MegaCrit.Sts2.Core.Models.".Length..], type.Name), type => type);
        var wrong = new List<string>();
        foreach (var (folder, typeName, betaHooks) in CoverageLedger.BetaHooks)
        {
            if (!types.TryGetValue((folder, typeName), out var type)) continue;
            var onCard = folder is "Enchantments" or "Afflictions";
            var actual = string.Join(",", type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(method => (hooks.Contains(method.Name) || onCard && method.Name == "OnPlay") && method.IsVirtual && (method.Attributes & MethodAttributes.NewSlot) == 0) // an override (a metadata-only type has no GetBaseDefinition)
                .Select(method => method.Name).Distinct().OrderBy(name => name, StringComparer.Ordinal));
            var expected = onMain.TryGetValue((folder, typeName), out var mainHooks) ? mainHooks : betaHooks;
            if (actual != expected) wrong.Add($"{typeName}: the ledger has {expected} for main, main's code {actual}");
        }
        Assert.True(wrong.Count == 0, "The ledger's hooks for main aren't main's (rerun scripts/coverage-ledger.mjs with the main decompile; docs/DEVELOPMENT.md \"When main updates\"):\n" + string.Join("\n", wrong));
    }
}
