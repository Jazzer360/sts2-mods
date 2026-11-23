using MegaCrit.Sts2.Core.Models.Relics;
using SpireQoL.Diagnostics;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The play check holds a relic's plain tab to a flash only when its code can flash (CodeIndex): Self-Forming Clay applies its power
/// with no Flash call, so "didn't fire" on Offering was the check's own mistake (a dev review), and Mummified Hand is the same. The
/// rule reads the code, so a relic added in a game update is judged the same way without a list.
/// </summary>
public class NonFlashingRelicTests
{
    [Fact]
    public void RelicsWhoseCodeNeverFlashesAreToldFromTheOnesThatDo()
    {
        GameModels.EnsureLoaded();
        Assert.False(CodeIndex.FlashesOutsideAmountHooks(typeof(SelfFormingClay)));
        Assert.False(CodeIndex.FlashesOutsideAmountHooks(typeof(MummifiedHand)));
        Assert.True(CodeIndex.FlashesOutsideAmountHooks(typeof(CharonsAshes)));
    }
}
