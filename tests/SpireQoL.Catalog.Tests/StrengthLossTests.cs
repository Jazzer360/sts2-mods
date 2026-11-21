using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Procs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Issue #371: a card that takes enemies' Strength this turn (Piercing Wail: PiercingWailPower, a TemporaryStrengthPower) applies
/// one debuff, so one Artifact charge takes all of it. Its simulated play has exactly one Strength debuff per card, never the
/// temporary power as well (Artifact took that one and the Strength still landed).
/// </summary>
[Collection(GameFightCollection.Name)]
public class StrengthLossTests
{
    [Fact]
    public void EachStrengthCardAppliesOneDebuff()
    {
        GameModels.EnsureLoaded();
        var wrong = new List<string>();
        foreach (var name in SimAdapter.EnemyStrengthCards.Keys)
        {
            var canon = ModelDb.AllCards.FirstOrDefault(canonicalCard => canonicalCard.GetType().Name == name);
            if (canon == null) continue;
            var gameFight = new GameFight();
            var card = gameFight.Combat.CreateCard(canon, gameFight.Me);
            PileType.Hand.GetPile(gameFight.Me).AddInternal(card);
            var facts = PlayFacts.For(card, gameFight.Me, gameFight.Combat, 1);
            if (facts == null) continue;
            var play = SimAdapter.Play(card, facts, gameFight.Me, gameFight.Combat, new Dictionary<Creature, int>(), -1);
            var debuffs = play.Body.OfType<ApplyPowerAction>().Where(applyPowerAction => applyPowerAction.To is ApplyPowerAction.Where.Target or ApplyPowerAction.Where.AllEnemies && applyPowerAction.Debuff).ToList();
            var temporary = debuffs.Count(applyPowerAction => applyPowerAction.Power != PowerId.Strength && ModelDb.AllPowers.Any(power => power.GetType().Name == applyPowerAction.Power.Name && power is MegaCrit.Sts2.Core.Models.Powers.TemporaryStrengthPower));
            if (temporary > 0) wrong.Add($"{name}: {string.Join(", ", debuffs.Select(applyPowerAction => applyPowerAction.Power))}");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }
}
