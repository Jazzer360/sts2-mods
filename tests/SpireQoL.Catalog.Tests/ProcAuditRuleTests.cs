using MegaCrit.Sts2.Core.Models.Powers;
using SpireQoL.Diagnostics;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The Play audit's rules for powers that act without a play setting them off, read from their code where the audit had names: a damage cap
/// that flashes as it caps (ProcAudit.Modifier), a power an attack spends (ProcAudit.SpentByAttack).
/// </summary>
public class ProcAuditRuleTests
{
    [Fact]
    public void TheCapsThatFlashAreTheOnesTheCodeSays()
    {
        // Hard to Kill and Intangible (ModifyDamageCap, AfterModifyingDamageAmount), Hardened Shell (ModifyHpLostBeforeOstyLate,
        // AfterModifyingHpLostBeforeOsty): the names the audit kept; Buffer (ModifyHpLostAfterOstyLate, AfterModifyingHpLostAfterOsty) joins them.
        var modifiers = HookReader.Powers(CoverageLedger.ModelTypes()).Where(ProcAudit.Modifier).Select(type => type.Name).Order(StringComparer.Ordinal);
        Assert.Equal(new[] { nameof(BufferPower), nameof(HardToKillPower), nameof(HardenedShellPower), nameof(IntangiblePower) }, modifiers);
    }

    [Fact]
    public void ThePowersAnAttackSpendsAreTheOnesTheCodeSays()
    {
        // Vigor (PowerCmd.ModifyAmount of itself in AfterAttack) and Gigantification (its Decrement): the names the audit kept; Aim and Calcify's
        // kin read from their code too wherever their AfterAttack takes their own stacks.
        var spent = SimModels.Names.Where(ProcAudit.SpentByAttack).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(nameof(VigorPower), spent);
        Assert.Contains(nameof(GigantificationPower), spent);
        Assert.DoesNotContain(nameof(StrengthPower), spent);
    }

    [Fact]
    public void WhatASourceExplainsIsWhatItsCodeDoesBeyondItsSimulation()
    {
        // A modelled source's card events are its simulation's (Dark Embrace's draws, Juggling's copy): it explains only the powers it applies and the
        // summons it makes (Kunai's Dexterity, Sic 'Em's summon, which the audit's names had left out); a source that plays cards explains everything.
        Assert.Equal(new[] { ProcAudit.Effect.Power }, ProcAudit.EffectsOf(nameof(MegaCrit.Sts2.Core.Models.Relics.Kunai)));
        Assert.Empty(ProcAudit.EffectsOf(nameof(DarkEmbracePower)));
        Assert.Empty(ProcAudit.EffectsOf(nameof(JugglingPower)));
        Assert.Contains(ProcAudit.Effect.Summon, ProcAudit.EffectsOf(nameof(SicEmPower)));
        Assert.Equal(Enum.GetValues<ProcAudit.Effect>(), ProcAudit.EffectsOf(nameof(LostLutePower)));
        Assert.Equal(Enum.GetValues<ProcAudit.Effect>(), ProcAudit.EffectsOf(nameof(ImitationLearningPower)));
        Assert.Empty(ProcAudit.EffectsOf("NotAGameClass"));
    }
}
