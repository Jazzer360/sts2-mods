using MegaCrit.Sts2.Core.Models.Relics;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A flash on a condition the reader can't translate is an unknown flash (FlashStep.Unknown), which only the play check treats as not known:
/// the rest of the hook's body still reads. Chemical X's BeforeCardPlayed is `if (cardPlay.Card.EnergyCost.CostsX) Flash();` (v0.113.1), a
/// test the reader has no fact for: the body reads, as its unknown flash alone.
/// </summary>
public class UnknownFlashTests
{
    [Fact]
    public void AFlashOnATestNotReadIsUnknownAndTheBodyStillReads()
    {
        var reading = HookReader.Read(typeof(ChemicalX), RuntimeCode.Read);
        Assert.Contains(reading.Hooks, hook => hook.Hook == "BeforeCardPlayed" && hook.Translated);
        var program = Assert.Single(reading.Spec.Hooks, hook => hook.Hook == HookName.BeforeCardPlayed);
        var flash = Assert.IsType<FlashStep>(Assert.Single(program.Steps));
        Assert.Contains("CostsX", flash.Unknown, StringComparison.Ordinal);
        Assert.Null(flash.When);
    }
}
