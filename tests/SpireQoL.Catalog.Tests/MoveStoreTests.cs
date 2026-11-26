using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A move that sets a property of its own monster that one of its attack intents' damage is (decision #337), read from the game's code: Waterfall
/// Giant's About to Blow stores its Steam Eruption as SteamEruptionDamage (WaterfallGiant.AboutToBlowMove), which its Explode's Death Blow intent is
/// (() => SteamEruptionDamage), and Pressure Gun adds PressureGunIncrease to CurrentPressureGunDamage after its attack.
/// </summary>
[Collection(GameFightCollection.Name)]
public class MoveStoreTests
{
    private static MegaCrit.Sts2.Core.Entities.Creatures.Creature Giant(GameFight fight, string moveId, int steam)
    {
        var creature = fight.Combat.CreateCreature(ModelDb.Monster<WaterfallGiant>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(creature);
        var monster = creature.Monster!;
        monster.SetUpForCombat();
        var move = (MoveState)monster.MoveStateMachine!.States[moveId];
        typeof(MonsterModel).GetProperty(nameof(MonsterModel.NextMove))!.SetValue(monster, move);
        monster.MoveStateMachine.ForceCurrentState(move);
        ModelDb.Power<SteamEruptionPower>().ToMutable().ApplyInternal(creature, amount: steam);
        return creature;
    }

    [Fact]
    public void AGiantAboutToBlowIsProjectedToExplodeForItsSteamEruption()
    {
        // A dev review: the projection read the Explode's damage as its intent reads now (0, About to Blow not yet made) where the game's said 41.
        var fight = new GameFight();
        var giant = Giant(fight, "ABOUT_TO_BLOW_MOVE", steam: 41);
        var input = SimAdapter.ProjectionStart(fight.Me, fight.Combat)!;
        var store = Assert.IsType<MoveStoreAction>(Assert.Single(input.State.Enemy(input.Ids[giant])!.Move!));
        Assert.Equal(("Monster.WaterfallGiant.SteamEruptionDamage", PowerId.SteamEruption), (store.Field, store.OwnPower));
        var projection = SimEngine.Project(input.State, input.Turns, input.Rules);
        Assert.Equal(new[] { 41 }, projection.Of(turn: 0, input.Ids[giant], "EXPLODE_MOVE")!.AtYourTurnStart);
    }

    [Fact]
    public void PressureGunAddsToItsDamageForTheNextOne()
    {
        var fight = new GameFight();
        var giant = Giant(fight, "PRESSURE_GUN_MOVE", steam: 10);
        var state = SimAdapter.Build(fight.Me, fight.Combat, out var ids);
        var store = Assert.Single(state.Enemy(ids[giant])!.Move!.OfType<MoveStoreAction>());
        Assert.Equal(("Monster.WaterfallGiant.CurrentPressureGunDamage", true, 5), (store.Field, store.Adds, store.Amount));
        Assert.Null(store.Unknown);
    }
}
