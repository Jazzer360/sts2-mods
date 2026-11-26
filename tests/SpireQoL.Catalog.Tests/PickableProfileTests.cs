using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #394: a card of your draw pile that an end-of-turn pick could take once drawn (Stampede's, Simpleton's: SimDrawCard.Auto) keeps the costs
/// that pick and Mummified Hand's ask (SimHandCard.LocalCost, LocalStarCost, CostsBeforeModifiers, its cost facts), so two copies that differ only
/// there are two profiles, each with its own.
/// </summary>
[Collection(GameFightCollection.Name)]
public class PickableProfileTests
{
    [Fact]
    public void CopiesThatDifferOnlyInTheirBaseCostAreTwoProfiles()
    {
        var fight = new GameFight();
        var enemy = fight.Combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(enemy);
        ModelDb.Power<StampedePower>().ToMutable().ApplyInternal(fight.Me.Creature, amount: 1m);
        // Both cost 1 now: one through its base, the other a base of 0 raised by a cost of its own for the fight.
        fight.Add<StrikeIronclad>(PileType.Draw);
        var raised = fight.Add<StrikeIronclad>(PileType.Draw);
        raised.EnergyCost.SetCustomBaseCost(0);
        raised.EnergyCost.AddThisCombat(1);
        Assert.Equal(1, raised.EnergyCost.GetResolved());

        var start = SimAdapter.Build(fight.Me, fight.Combat, out _);
        var groups = start.You.Draw!.Rest;
        Assert.Equal(2, groups.Count);
        Assert.All(groups, group => Assert.Equal(1, group.Count));
        Assert.Equal(new bool?[] { false, true }, groups.Select(group => group.Card.Auto!.CostsBeforeModifiers).OrderBy(costs => costs));
    }
}
