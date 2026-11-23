using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireQoL.Data;
using SpireQoL.Features;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The reminders check reads what a relic an event option gave you does as you obtain it (RunReminders.ObtainEffects:
/// its AfterObtained), so that isn't counted against the option's code. A report: This or That's Ornate gave a random relic,
/// Cursed Pearl, whose gold was reported as the option's.
/// </summary>
[Collection(GameFightCollection.Name)]
public class ObtainEffectsTests
{
    [Fact]
    public void RelicsThatGiveGoldWhenObtainedReadAsGold()
    {
        GameModels.EnsureLoaded();
        foreach (var relic in new RelicModel[] { ModelDb.Relic<CursedPearl>(), ModelDb.Relic<GoldenPearl>(), ModelDb.Relic<OldCoin>(), ModelDb.Relic<SignetRing>() })
            Assert.True(RunReminders.ObtainEffects(relic).HasFlag(ChoiceEffect.GoldGain), relic.GetType().Name);
    }

    [Fact]
    public void ARelicThatDoesNothingWhenObtainedReadsAsNothing()
    {
        GameModels.EnsureLoaded();
        Assert.Equal(ChoiceEffect.None, RunReminders.ObtainEffects(ModelDb.Relic<Anchor>()));
    }
}
