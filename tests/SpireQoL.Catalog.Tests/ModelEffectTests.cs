using SpireQoL.Diagnostics;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #156: a relic or power model whose game class changes the fight must change the simulation too, not only fire its tab
/// (ModelEffectCheck; a report: Mini Regent fired and its Strength never landed). A model reported here gets its effect, or a
/// reviewed reason in ModelEffectCheck.Reviewed.
/// </summary>
public class ModelEffectTests
{
    [Fact]
    public void EveryModelDoesWhatItsGameClassDoes()
    {
        GameModels.EnsureLoaded();
        var unexplained = ModelEffectCheck.Unexplained();
        Assert.True(unexplained.Count == 0,
            $"{unexplained.Count} models fire without doing what their game class does:\n" + string.Join("\n", unexplained.Select(entry => $"  {entry.Model}: {entry.GameCommands}")));
    }
}
