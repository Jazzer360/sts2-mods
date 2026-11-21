using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using SpireQoL.Diagnostics;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A power or relic whose only hooks change a block, damage or HP-lost amount needs no tab: its flash is the play's own numbers,
/// which the checks compare (CodeIndex.OnlyModifies); one that has a tab anyway (The Boot) is still held to it.
/// </summary>
[Collection(GameFightCollection.Name)]
public class OnlyModifiesTests
{
    [Fact]
    public void NumberModifiersAreKnownByTheirHooks()
    {
        GameModels.EnsureLoaded();
        Assert.True(CodeIndex.OnlyModifies(typeof(TheBoot)));            // HP lost raised to its minimum
        Assert.True(CodeIndex.OnlyModifies(typeof(FastenPower)));        // block on Defends
        Assert.False(CodeIndex.OnlyModifies(typeof(HardenedShellPower))); // keeps a count across hits and turns
        Assert.False(CodeIndex.OnlyModifies(typeof(SignalBoostPower)));   // plays a card twice: a proc with a tab
    }
}
