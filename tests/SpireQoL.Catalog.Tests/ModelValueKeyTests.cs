using SpireQoL.Diagnostics;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Every value key the simulator names (a model's Var(..., "Key"), VarTotal, PropsOf, its relic helpers' "Block" and "Damage",
/// SimEngine.Var with a literal model, SimReaders.RelicAmountVars) is one of its game class's DynamicVars keys on the installed game
/// (ModelValueKeyCheck): a key the class doesn't have reads nothing, and the model runs on its fallback (Kunai read "Dexterity",
/// the key is "DexterityPower"). A key that's deliberately not the game's gets a reviewed entry there.
/// </summary>
[Collection(GameFightCollection.Name)]
public class ModelValueKeyTests
{
    [Fact]
    public void EveryValueKeyTheSimulatorNamesIsItsGameClasss()
    {
        GameModels.EnsureLoaded();
        var report = ModelValueKeyCheck.Run();
        Assert.True(report.Compared > 40, $"only {report.Compared} keys compared: the check stopped reading the models");
        Assert.True(report.Wrong.Count + report.Unread.Count + report.StaleReviews.Count == 0,
            $"{report.Compared} keys compared.\n{report.Wrong.Count} aren't their game class's DynamicVars keys:\n" + string.Join("\n", report.Wrong.Select(key => "  " + key))
            + $"\n{report.Unread.Count} couldn't be read:\n" + string.Join("\n", report.Unread.Select(entry => "  " + entry))
            + $"\n{report.StaleReviews.Count} reviewed entries nothing names any more (remove them):\n" + string.Join("\n", report.StaleReviews.Select(entry => "  " + entry)));
    }

    [Fact]
    public void TheCheckReadsAModelsKeysAndTheGamesKeys()
    {
        // Kunai's model reads "DexterityPower" (KunaiRelic: PowerVar<DexterityPower>) and counts its "Cards" (CounterRelic).
        GameModels.EnsureLoaded();
        var named = ModelValueKeyCheck.Named(new List<string>());
        Assert.Contains(named, key => key.Model == "Kunai" && key.Key == "DexterityPower");
        Assert.Contains(named, key => key.Model == "Kunai" && key.Key == "Cards");
        Assert.Contains(named, key => key.Model == "MonologuePower" && key.Key == "StrengthPower");
        Assert.Contains("DexterityPower", ModelValueKeyCheck.GameKeys(typeof(MegaCrit.Sts2.Core.Models.Relics.Kunai)));
        Assert.DoesNotContain("Dexterity", ModelValueKeyCheck.GameKeys(typeof(MegaCrit.Sts2.Core.Models.Relics.Kunai)));
    }
}
