using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Procs;
using SpireQoL.Sim;
using Sts2Mods.Core.Game;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Strength a card takes from enemies or gives them, read from its program (decision #182: SimAdapter.EnemyStrengthOf). Issue #371: a
/// card that takes enemies' Strength this turn (Piercing Wail: PiercingWailPower, a TemporaryStrengthPower) applies one debuff, so one
/// Artifact charge takes all of it. Its simulated play has exactly one Strength debuff per card, never the temporary power as well
/// (Artifact took that one and the Strength still landed).
/// </summary>
[Collection(GameFightCollection.Name)]
public class StrengthLossTests
{
    /// <summary>Each card that changes enemies' Strength, in a fight.</summary>
    private static IEnumerable<(string Name, CardModel Card, GameFight Fight)> StrengthCards()
    {
        GameModels.EnsureLoaded();
        foreach (var canon in ModelDb.AllCards)
        {
            var gameFight = new GameFight();
            var card = gameFight.Combat.CreateCard(canon, gameFight.Me);
            PileType.Hand.GetPile(gameFight.Me).AddInternal(card);
            if (SimAdapter.EnemyStrengthOf(card).Count > 0) yield return (canon.GetType().Name, card, gameFight);
        }
    }

    [Fact]
    public void EachStrengthCardAppliesOneDebuff()
    {
        var wrong = new List<string>();
        foreach (var (name, card, gameFight) in StrengthCards())
        {
            var facts = PlayFacts.For(card, gameFight.Me, gameFight.Combat, plays: 1);
            var play = SimAdapter.Play(card, facts, gameFight.Me, gameFight.Combat, new Dictionary<Creature, int>(), targetId: -1);
            var debuffs = play.Body.OfType<ApplyPowerAction>().Where(applyPowerAction => applyPowerAction.To is ApplyPowerAction.Where.Target or ApplyPowerAction.Where.AllEnemies && applyPowerAction.Debuff).ToList();
            var temporary = debuffs.Count(applyPowerAction => applyPowerAction.Power != PowerId.Strength && ModelDb.AllPowers.Any(power => power.GetType().Name == applyPowerAction.Power.Name && power is MegaCrit.Sts2.Core.Models.Powers.TemporaryStrengthPower));
            if (temporary > 0) wrong.Add($"{name}: {string.Join(", ", debuffs.Select(applyPowerAction => applyPowerAction.Power))}");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void TheStrengthCardsAreTheTablesItReplaced()
    {
        // SimAdapter.EnemyStrengthCards as it was when it was deleted: the value's key ("" for 1), every enemy or the target, the sign.
        var table = new Dictionary<string, (string Var, bool All, int Sign)>
        {
            ["PiercingWail"] = ("StrengthLoss", true, -1), ["DarkShackles"] = ("StrengthLoss", false, -1), ["EnfeeblingTouch"] = ("StrengthLoss", false, -1),
            ["CrushUnder"] = ("StrengthLoss", true, -1), ["Mangle"] = ("StrengthLoss", false, -1), ["DyingStar"] = ("StrengthLoss", true, -1),
            ["SharedFate"] = ("EnemyStrengthLoss", false, -1), ["Resonance"] = ("", true, -1), ["FightMe"] = ("EnemyStrength", false, 1),
        };
        var read = StrengthCards().ToDictionary(entry => entry.Name, entry => (Card: entry.Card, Changes: SimAdapter.EnemyStrengthOf(entry.Card)));
        Assert.Equal(table.Keys.Where(name => ModelDb.AllCards.Any(card => card.GetType().Name == name)).Order(), read.Keys.Order());
        foreach (var (name, (variable, all, sign)) in table)
        {
            if (!read.TryGetValue(name, out var entry)) continue;
            var (_, amount, isAll) = Assert.Single(entry.Changes);
            Assert.Equal((sign * (variable == "" ? 1 : Sts2State.Var(entry.Card, variable)), all), (amount, isAll));
        }
    }
}
