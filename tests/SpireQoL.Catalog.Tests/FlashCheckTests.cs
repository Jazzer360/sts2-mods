using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using SpireQoL.Diagnostics;
using SpireQoL.Features;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>Issues #425 and #426: which powers the play check holds to their flash, and canonical stand-in powers.</summary>
public class FlashCheckTests
{
    [Fact]
    public void SlowFlashesOnlyWhenItRaisesAHit() => Assert.False(CodeIndex.FlashesOutsideAmountHooks(typeof(SlowPower)));

    [Fact]
    public void GravityFlashesWhenItFires() => Assert.True(CodeIndex.FlashesOutsideAmountHooks(typeof(GravityPower)));

    [Fact]
    public void ACanonicalPowerHasNoOwnerRatherThanThrowing()
    {
        GameModels.EnsureLoaded();
        var canon = ModelDb.GetById<PowerModel>(ModelDb.GetId(typeof(ChildOfTheStarsPower)))!;
        Assert.False(canon.IsMutable);
        Assert.Null(SpireQoL.Combat.CardPredictions.OwnerOf(canon));
    }
}
