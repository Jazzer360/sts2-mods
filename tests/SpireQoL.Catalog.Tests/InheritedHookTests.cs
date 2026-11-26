using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #400: a power whose hooks are all on the abstract game class it's built on (PiercingWailPower: TemporaryStrengthPower) is read from
/// those hooks. Before, it read as having none: an enemy's Piercing Wail never gave its Strength back as the enemies' turn ended, in a projection.
/// </summary>
[Collection(GameFightCollection.Name)]
public class InheritedHookTests
{
    [Fact]
    public void AnEnemysPiercingWailGivesItsStrengthBackForItsNextAttack()
    {
        // A dev review: a Knowledge Demon Piercing Wailed (Strength -6, PiercingWailPower 6) about to Slap: as the enemies' turn ends its Piercing Wail
        // gives the 6 back (TemporaryStrengthPower.AfterSideTurnEnd), so its Knowledge Overwhelming next turn hits as it would with none; the projection
        // said 4 a hit where the game's intent said 9.
        var fight = new GameFight();
        var creature = fight.Combat.CreateCreature(ModelDb.Monster<KnowledgeDemon>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(creature);
        var monster = creature.Monster!;
        monster.SetUpForCombat();
        var slap = (MoveState)monster.MoveStateMachine!.States["SLAP_MOVE"];
        typeof(MonsterModel).GetProperty(nameof(MonsterModel.NextMove))!.SetValue(monster, slap);
        monster.MoveStateMachine.ForceCurrentState(slap);
        var overwhelming = ((MoveState)monster.MoveStateMachine.States["KNOWLEDGE_OVERWHELMING_MOVE"]).Intents.OfType<AttackIntent>().Single();
        var unweakened = overwhelming.GetSingleDamage(fight.Combat.PlayerCreatures.ToList(), creature);
        ModelDb.Power<StrengthPower>().ToMutable().ApplyInternal(creature, amount: -6m);
        ModelDb.Power<PiercingWailPower>().ToMutable().ApplyInternal(creature, amount: 6m);
        var input = SimAdapter.ProjectionStart(fight.Me, fight.Combat)!;
        var projection = SimEngine.Project(input.State, input.Turns, input.Rules);
        var hits = projection.Of(turn: 0, input.Ids[creature], "KNOWLEDGE_OVERWHELMING_MOVE")!;
        Assert.Null(hits.Unknown);
        Assert.All(hits.AtYourTurnStart!, hit => Assert.Equal(unweakened, hit));
    }

    [Theory]
    [InlineData(typeof(PiercingWailPower))]
    [InlineData(typeof(DarkShacklesPower))]
    [InlineData(typeof(FlexPotionPower))]
    [InlineData(typeof(HelicalDartPower))]
    public void ATemporaryPowerReadsItsBaseClasssHooks(Type power)
    {
        GameModels.EnsureLoaded();
        var reading = HookReader.Read(power, RuntimeCode.Read);
        var lines = string.Join("\n", reading.Hooks.Select(hook => $"{hook.Hook}: {hook.Gap ?? "read"}"));
        Assert.Contains(reading.Hooks, hook => hook.Hook == "AfterSideTurnEnd");
        Assert.True(reading.Whole, lines);
    }
}
