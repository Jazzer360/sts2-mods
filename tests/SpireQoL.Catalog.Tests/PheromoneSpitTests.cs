using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #400: Entomancer's Pheromone Spit (EntomancerSpitMove) gives Strength by the Personal Hive it has as the move starts: below 3, one more
/// Personal Hive and 1 Strength; at 3 (or none), 2 Strength. Its test fetches the power (Powers.OfType&lt;PersonalHivePower&gt;().FirstOrDefault()) and
/// is made once, before its first step adds to the hive. The projection gave it no Strength: Bees after it hit 1 less a hit than the game's intent.
/// </summary>
[Collection(GameFightCollection.Name)]
public class PheromoneSpitTests
{
    private static (GameFight Fight, MegaCrit.Sts2.Core.Entities.Creatures.Creature Creature) Spitting(int hive)
    {
        var fight = new GameFight();
        var creature = fight.Combat.CreateCreature(ModelDb.Monster<Entomancer>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(creature);
        var monster = creature.Monster!;
        monster.SetUpForCombat();
        var spit = (MoveState)monster.MoveStateMachine!.States["PHEROMONE_SPIT_MOVE"];
        typeof(MonsterModel).GetProperty(nameof(MonsterModel.NextMove))!.SetValue(monster, spit);
        monster.MoveStateMachine.ForceCurrentState(spit);
        foreach (var power in creature.Powers.OfType<PersonalHivePower>().ToList()) creature.RemovePowerInternal(power);
        if (hive > 0) ModelDb.Power<PersonalHivePower>().ToMutable().ApplyInternal(creature, amount: hive);
        return (fight, creature);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    public void TheBeesAfterAPheromoneSpitHitForTheStrengthItGave(int hive, int strength)
    {
        var (fight, creature) = Spitting(hive);
        var bees = ((MoveState)creature.Monster!.MoveStateMachine!.States["BEES_MOVE"]).Intents.OfType<MegaCrit.Sts2.Core.MonsterMoves.Intents.AttackIntent>().Single();
        var now = bees.GetSingleDamage(fight.Combat.PlayerCreatures.ToList(), creature);
        var input = SimAdapter.ProjectionStart(fight.Me, fight.Combat)!;
        var projection = SimEngine.Project(input.State, input.Turns, input.Rules);
        var hits = projection.Of(turn: 0, input.Ids[creature], "BEES_MOVE")!;
        Assert.Null(hits.Unknown);
        Assert.All(hits.AtYourTurnStart!, hit => Assert.Equal(now + strength, hit));
    }
}
