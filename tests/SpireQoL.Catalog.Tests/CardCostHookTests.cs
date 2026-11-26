using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A card's cost hooks read as Try hooks (decision #332): TryModifyEnergyCostInCombat, its Late pass and TryModifyStarCost, each its answer and the
/// cost its out parameter holds, the costed card's owner, type and affliction and originalCost (the cost so far) read. No simulation works out a hand
/// card's live cost through the listeners yet (TRACKER: mid-play card costs), so a body that reads is still the engine's gap, never run.
/// </summary>
public class CardCostHookTests
{
    private static string? GapOf(Type type, string hook) =>
        HookReader.Read(type, RuntimeCode.Read).Hooks.Single(reading => reading.Hook == hook).Gap;

    [Theory]
    [InlineData(typeof(CuriousPower), "TryModifyEnergyCostInCombat")]
    [InlineData(typeof(BorrowedTimePower), "TryModifyEnergyCostInCombat")]
    [InlineData(typeof(TangledPower), "TryModifyEnergyCostInCombat")]
    [InlineData(typeof(CorruptionPower), "TryModifyEnergyCostInCombatLate")]
    [InlineData(typeof(SpikedGauntlets), "TryModifyEnergyCostInCombat")]
    public void ACostHooksBodyReadsAndWaitsForTheLiveCostRule(Type type, string hook)
    {
        GameModels.EnsureLoaded();
        Assert.Equal($"gap: its body reads, but no simulation works out a hand card's live cost through the listeners' {hook} yet", GapOf(type, hook));
    }

    [Fact]
    public void ACostHookThatTestsTheCardsPileStopsThere()
    {
        // card.Pile?.Type is Hand or Play (Free Attack's, Veilpiercer's, Void Form's): the next capability the live cost rule needs read.
        GameModels.EnsureLoaded();
        Assert.Contains("card.Pile", GapOf(typeof(FreeAttackPower), "TryModifyEnergyCostInCombatLate"));
    }
}
