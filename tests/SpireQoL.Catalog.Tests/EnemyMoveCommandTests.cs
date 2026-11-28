using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #417: a move's heal, kill, escape and power taken away, read from the monster's code into the enemy turn's actions (SimAdapter.MoveOf), on a
/// real fight built from the game's objects.
/// </summary>
[Collection(GameFightCollection.Name)]
public class EnemyMoveCommandTests
{
    /// <summary>A monster of type T in the fight, about to make the move <paramref name="moveId"/>.</summary>
    private static Creature Enemy<T>(GameFight fight, string moveId) where T : MonsterModel
    {
        var creature = fight.Combat.CreateCreature(ModelDb.Monster<T>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(creature);
        var monster = creature.Monster!;
        monster.SetUpForCombat();
        var move = (MoveState)monster.MoveStateMachine!.States[moveId];
        typeof(MonsterModel).GetProperty(nameof(MonsterModel.NextMove))!.SetValue(monster, move);
        monster.MoveStateMachine.ForceCurrentState(move);
        return creature;
    }

    private static SimState Read(GameFight fight, out Dictionary<Creature, int> ids) => SimAdapter.Build(fight.Me, fight.Combat, out ids);

    [Fact]
    public void KnowledgeDemonsPonderHealsItAfterItsAttack()
    {
        // PonderMove: its attack, CreatureCmd.Heal(base.Creature, 30 * players), then Strength: the heal is 30 for one player, capped at its max HP.
        var fight = new GameFight();
        var demon = Enemy<KnowledgeDemon>(fight, "PONDER_MOVE");
        demon.SetCurrentHpInternal(demon.MaxHp - 40);
        var state = Read(fight, out var ids);
        var heal = Assert.Single(state.Enemy(ids[demon])!.Move!.OfType<MoveHealAction>());
        Assert.Equal(30, heal.Amount);
        Assert.Equal(new[] { ids[demon] }, heal.To);
        var branch = Assert.Single(SimEngine.Run(state).Branches);
        Assert.Null(branch.Unmodelled);
        Assert.Equal(demon.MaxHp - 10, branch.Enemy(ids[demon])!.Hp);
    }

    [Fact]
    public void OwlMagistratesVerdictTakesItsSoarAway()
    {
        var fight = new GameFight();
        var owl = Enemy<OwlMagistrate>(fight, "VERDICT");
        var state = Read(fight, out var ids);
        var removal = Assert.Single(state.Enemy(ids[owl])!.Move!.OfType<MoveRemovePowerAction>());
        Assert.Equal(PowerId.Of(nameof(SoarPower)), removal.Power);
        Assert.Equal(new[] { ids[owl] }, removal.To);
    }

    [Fact]
    public void GasBombKillsItselfOnItsTestThatItIsntDeadYet()
    {
        // ExplodeMove: its attack, then CreatureCmd.Kill(base.Creature) if !base.Creature.IsDead: the kill's own test (it kills a living one only).
        var fight = new GameFight();
        var bomb = Enemy<GasBomb>(fight, "EXPLODE_MOVE");
        var state = Read(fight, out var ids);
        var kill = Assert.Single(state.Enemy(ids[bomb])!.Move!.OfType<MoveKillAction>());
        Assert.Equal(new[] { ids[bomb] }, kill.To);
        var branch = Assert.Single(SimEngine.Run(state).Branches);
        Assert.False(branch.Enemy(ids[bomb])!.Alive);
    }

    [Fact]
    public void FatGremlinsFleeLeavesTheFight()
    {
        var fight = new GameFight();
        var gremlin = Enemy<FatGremlin>(fight, "FLEE_MOVE");
        var state = Read(fight, out var ids);
        Assert.Equal(new[] { ids[gremlin] }, Assert.Single(state.Enemy(ids[gremlin])!.Move!.OfType<MoveEscapeAction>()).To);
        Assert.True(Assert.Single(SimEngine.Run(state).Branches).Enemy(ids[gremlin])!.Escaped);
    }
}
