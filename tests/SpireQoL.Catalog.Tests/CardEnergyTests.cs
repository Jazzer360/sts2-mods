using MegaCrit.Sts2.Core.Models;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A card's own energy (decision #117): the reading of every PlayerCmd.GainEnergy in a card's code, and the scan that every card in
/// the installed game that gains energy is read plainly, has a rule (CardEnergy.Rules) or a reviewed reason, so the simulator never
/// misses energy a card gives back (the Pael's Tears tab and the energy check after each play depend on it).
/// </summary>
public class CardEnergyTests
{
    private static IEnumerable<Type> CardTypes()
    {
        // The game's cards (ModelDb.AllCards), not its test mocks.
        GameModels.EnsureLoaded();
        return ModelDb.AllCards.Select(c => c.GetType()).Distinct();
    }

    private static Type Card(string name) => CardTypes().Single(t => t.Name == name);

    [Fact]
    public void APlainGainIsReadFromTheCardsCode()
    {
        // In OnPlay, on every way through it, its energy value, to its owner.
        foreach (var name in new[] { "Production", "Turbo", "Supercritical", "Bloodletting", "Offering" })
            Assert.True(CardEnergy.IsPlain(Card(name)), name);
    }

    [Fact]
    public void ConditionalOrRedirectedGainsAreNotPlain()
    {
        Assert.Contains(CardEnergy.CallsOf(Card("Sunder")), c => c is { Method: "OnPlay", Certain: false });         // inside if (killed)
        Assert.Contains(CardEnergy.CallsOf(Card("Restlessness")), c => c is { Method: "OnPlay", Certain: false });   // only card in hand
        Assert.Contains(CardEnergy.CallsOf(Card("BelieveInYou")), c => c is { ToOwner: false });                    // to the target
        Assert.Contains(CardEnergy.CallsOf(Card("DoubleEnergy")), c => c is { FromEnergyVar: false });              // your energy
        Assert.Contains(CardEnergy.CallsOf(Card("DrumOfBattle")), c => c.Method != "OnPlay");                      // when exhausted
        foreach (var name in new[] { "Sunder", "Restlessness", "BelieveInYou", "DoubleEnergy", "DrumOfBattle", "EpicRetrieval" })
            Assert.False(CardEnergy.IsPlain(Card(name)), name);
    }

    [Fact]
    public void EveryCardThatGainsEnergyIsCounted()
    {
        var missing = CardTypes()
            .Where(t => CardEnergy.CallsOf(t).Count > 0)
            .Where(t => !CardEnergy.IsPlain(t) && !CardEnergy.Rules.ContainsKey(t.Name) && !CardEnergy.Reviewed.ContainsKey(t.Name))
            .Select(t => $"{t.Name}: {string.Join("; ", CardEnergy.CallsOf(t))}")
            .ToList();
        Assert.True(missing.Count == 0, "Cards that gain energy with no rule or reviewed reason (CardEnergy):\n" + string.Join("\n", missing));
    }

    [Fact]
    public void EveryRuleAndReviewIsAboutACardThatStillGainsEnergy()
    {
        var gaining = CardTypes().Where(t => CardEnergy.CallsOf(t).Count > 0).Select(t => t.Name).ToHashSet();
        foreach (var name in CardEnergy.Rules.Keys.Concat(CardEnergy.Reviewed.Keys))
            Assert.True(gaining.Contains(name), $"{name} is in CardEnergy but no longer gains energy in its code");
        // A rule for a plain card would count its energy twice.
        foreach (var name in CardEnergy.Rules.Keys)
            Assert.False(CardEnergy.IsPlain(Card(name)), $"{name} has a rule but reads plainly");
    }
}
