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
        // Its Steam Eruption read into the store first, then taken away (PowerCmd.Remove: decision #417).
        var move = input.State.Enemy(input.Ids[giant])!.Move!;
        Assert.Equal(2, move.Count);
        var store = Assert.IsType<MoveStoreAction>(move[0]);
        Assert.Equal(PowerId.SteamEruption, Assert.IsType<MoveRemovePowerAction>(move[1]).Power);
        Assert.Equal(("Monster.WaterfallGiant.SteamEruptionDamage", PowerId.SteamEruption), (store.Field, store.OwnPower));
        var projection = SimEngine.Project(input.State, input.Turns, input.Rules);
        Assert.Equal(new[] { 41 }, projection.Of(turn: 0, input.Ids[giant], "EXPLODE_MOVE")!.AtYourTurnStart);
    }

    [Fact]
    public void ThePlowAfterAPlowHitsForTheStrengthTheFirstGave()
    {
        // 2.0.0's audit: Plow (18, 20 on Deadly Enemies) gives the Ceremonial Beast PlowStrength (2) after its attack while it still has Plow
        // (CeremonialBeast.PlowMove), so the next Plow hits for 2 more; the projection said the same as the first.
        var fight = new GameFight();
        var creature = fight.Combat.CreateCreature(ModelDb.Monster<CeremonialBeast>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(creature);
        var monster = creature.Monster!;
        monster.SetUpForCombat();
        var plow = (MoveState)monster.MoveStateMachine!.States["PLOW_MOVE"];
        typeof(MonsterModel).GetProperty(nameof(MonsterModel.NextMove))!.SetValue(monster, plow);
        monster.MoveStateMachine.ForceCurrentState(plow);
        ModelDb.Power<PlowPower>().ToMutable().ApplyInternal(creature, amount: 150m);
        var input = SimAdapter.ProjectionStart(fight.Me, fight.Combat)!;
        var projection = SimEngine.Project(input.State, input.Turns, input.Rules);
        var first = plow.Intents.OfType<MegaCrit.Sts2.Core.MonsterMoves.Intents.AttackIntent>().Single().GetSingleDamage(fight.Combat.PlayerCreatures.ToList(), creature);
        Assert.Equal(new[] { first + 2 }, projection.Of(turn: 0, input.Ids[creature], "PLOW_MOVE")!.AtYourTurnStart);
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

    [Fact]
    public void TheForgottensDreadAfterMiasmaHitsForItsNewDexterity()
    {
        // Decision #344: Dread is () => DreadDamage, 13 (or its Ascension value) + base.Creature.GetPowerAmount<DexterityPower>(); Miasma before it gives
        // it DebilitatingSmogDexStealAmount Dexterity. On screen Miasma, the projected Dread reads its Dexterity after it, not the 0 it has now.
        var fight = new GameFight();
        var creature = fight.Combat.CreateCreature(ModelDb.Monster<TheForgotten>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(creature);
        var monster = creature.Monster!;
        monster.SetUpForCombat();
        var miasma = (MoveState)monster.MoveStateMachine!.States["MIASMA"];
        typeof(MonsterModel).GetProperty(nameof(MonsterModel.NextMove))!.SetValue(monster, miasma);
        monster.MoveStateMachine.ForceCurrentState(miasma);
        var dread = (MoveState)monster.MoveStateMachine.States["DREAD"];
        Assert.Equal(typeof(DexterityPower), SimAdapter.DamagePower(typeof(TheForgotten), dread.Intents.OfType<MegaCrit.Sts2.Core.MonsterMoves.Intents.AttackIntent>().Single()));
        var input = SimAdapter.ProjectionStart(fight.Me, fight.Combat)!;
        var projection = SimEngine.Project(input.State, input.Turns, input.Rules);
        var now = (int)dread.Intents.OfType<MegaCrit.Sts2.Core.MonsterMoves.Intents.AttackIntent>().Single().DamageCalc!();
        var lands = projection.Of(turn: 0, input.Ids[creature], "DREAD")!.AtYourTurnStart!;
        Assert.True(lands[0] > now, $"Dread after Miasma {string.Join(",", lands)}, read now {now}");
    }
}
