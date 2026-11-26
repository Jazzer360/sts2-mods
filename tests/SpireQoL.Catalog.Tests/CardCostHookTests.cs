using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A card's cost hooks read as Try hooks (decisions #332, #334): TryModifyEnergyCostInCombat and its Late pass, each its answer and the cost its out
/// parameter holds, the costed card's owner, type, keywords, affliction and pile (card.Pile?.Type is Hand or Play) and originalCost (the cost so far)
/// read, run by the engine wherever a simulation reads a hand card's cost (SimEngine.HandCost); TryModifyStarCost the same way, wherever a simulation
/// reads a hand card's star cost (SimEngine.HandStarCost: decision #386).
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
    [InlineData(typeof(EnsemblePower), "TryModifyEnergyCostInCombatLate")]
    [InlineData(typeof(VeilpiercerPower), "TryModifyEnergyCostInCombatLate")]
    [InlineData(typeof(VoidFormPower), "TryModifyEnergyCostInCombatLate")]
    [InlineData(typeof(BrilliantScarf), "TryModifyEnergyCostInCombatLate")]
    public void ACostPassReads(Type type, string hook)
    {
        GameModels.EnsureLoaded();
        Assert.Null(GapOf(type, hook));
    }

    [Fact]
    public void TheCostedCardsPileReads()
    {
        // switch (card.Pile?.Type) { case Hand: case Play: ... }: its HasValue (every costed card is in a pile), then the pile against each.
        GameModels.EnsureLoaded();
        var late = HookReader.Read(typeof(FreeAttackPower), RuntimeCode.Read).Spec.Hooks.Single(program => program.Hook == HookName.TryModifyEnergyCostInCombatLate);
        Assert.Contains(new ModelSpec("FreeAttackPower", ModelKind.Power, new[] { late }).Values(), value => value is HookFactValue { Fact: HookFact.CardPile });
    }

    [Theory]
    [InlineData(typeof(FreeAttackPower))]
    [InlineData(typeof(FreeSkillPower))]
    [InlineData(typeof(FreePowerPower))]
    [InlineData(typeof(VeilpiercerPower))]
    public void ThePlayedCardsPileReadsAsThePlayPile(Type type)
    {
        // BeforeCardPlayed's switch (cardPlay.Card.Pile?.Type) { case Hand: case Play: PowerCmd.Decrement }: the played card is in the play pile as its
        // play begins (CardModel.OnPlayWrapper), so the test holds and the class reads whole (decision #380).
        GameModels.EnsureLoaded();
        Assert.Null(GapOf(type, "BeforeCardPlayed"));
        var before = HookReader.Read(type, RuntimeCode.Read).Spec.Hooks.Single(program => program.Hook == HookName.BeforeCardPlayed);
        Assert.DoesNotContain(new ModelSpec(type.Name, ModelKind.Power, new[] { before }).Values(), value => value is HookFactValue { Fact: HookFact.CardPile });
    }

    [Theory]
    [InlineData(typeof(VoidFormPower))]
    [InlineData(typeof(BrilliantScarf))]
    public void AStarCostPassReads(Type type)
    {
        GameModels.EnsureLoaded();
        Assert.Null(GapOf(type, "TryModifyStarCost"));
    }
}
