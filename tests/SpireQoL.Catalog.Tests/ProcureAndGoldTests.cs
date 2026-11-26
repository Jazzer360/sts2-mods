using SpireQoL.Sim;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A card's PotionCmd.TryToProcure and PlayerCmd.GainGold (decision #362), translated from its code: Alchemize's random potion into your belt
/// (ProcurePotionAction: CreateRandomPotionInCombat never makes a Fairy in a Bottle), Hand of Greed's gold on its kill (GainGoldAction, then
/// AfterGoldGained: Dragon Fruit).
/// </summary>
[Collection(GameFightCollection.Name)]
public class ProcureAndGoldTests
{
    [Fact]
    public void NoPotionMadeInAFightIsAFairyInABottle()
    {
        GameModels.EnsureLoaded();
        Assert.DoesNotContain(ModelDb.AllPotions, potion => potion.CanBeGeneratedInCombat && SimAdapter.CountsAsFairy(potion));
    }

    [Fact]
    public void AlchemizeProcuresAPotion()
    {
        GameModels.EnsureLoaded();
        var gameFight = new GameFight();
        var card = gameFight.Add<Alchemize>();
        SimAdapter.Build(gameFight.Me, gameFight.Combat, out var ids);
        var play = SimAdapter.Play(card, gameFight.Me, gameFight.Combat, ids, Who.None);
        Assert.Contains(play.Body, action => action is ProcurePotionAction);
    }

    [Fact]
    public void HandOfGreedGainsGoldOnItsKill()
    {
        GameModels.EnsureLoaded();
        var gameFight = new GameFight();
        var enemy = gameFight.Combat.CreateCreature(ModelDb.Monster<MegaCrit.Sts2.Core.Models.Monsters.Axebot>().ToMutable(), MegaCrit.Sts2.Core.Combat.CombatSide.Enemy, slot: null);
        gameFight.Combat.AddCreature(enemy);
        var card = gameFight.Add<HandOfGreed>();
        SimAdapter.Build(gameFight.Me, gameFight.Combat, out var ids);
        var play = SimAdapter.Play(card, gameFight.Me, gameFight.Combat, ids, ids[enemy]);
        Assert.Contains(play.Body.OfType<WhenAction>(), when => when.Then.Any(action => action is GainGoldAction));
    }
}
