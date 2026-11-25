using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using SpireQoL.Diagnostics;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #254: the code a fight's first board read asks (its enemies' moves and hooks, its cards' energy commands, the questions its models
/// ask of the fight's log, the branch rules) is read as the mod loads, on a worker (ModEntry.StartReadingCode), so the first frame of the first
/// fight reads none of it on the game's thread (a dev session's first fight took 142 ms in the turn forecast, most of it first reads).
/// </summary>
[Collection(GameFightCollection.Name)]
public class FirstBoardReadTests
{
    private static Creature Enemy<T>(GameFight fight, string moveId) where T : MonsterModel
    {
        var creature = fight.Combat.CreateCreature(ModelDb.Monster<T>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(creature);
        var monster = creature.Monster!;
        monster.SetUpForCombat();
        var move = (MoveState)monster.MoveStateMachine!.States[moveId];
        typeof(MonsterModel).GetProperty(nameof(MonsterModel.NextMove))!.SetValue(monster, move);
        monster.MoveStateMachine!.ForceCurrentState(move);
        return creature;
    }

    [Fact]
    public void AfterTheLoadsReadTheFirstBoardReadsNoCode()
    {
        // What the load's workers read (ModEntry.StartReadingCode): the simulator's models, every card's program, and the code a fight's board asks.
        var types = CoverageLedger.ModelTypes().ToList();
        _ = SimModels.Count;
        CardPrograms.ReadAll(types.Where(type => typeof(CardModel).IsAssignableFrom(type)));
        Assert.True(SimAdapter.ReadFightCode(types) > 1000);

        // A first fight: a Seapunk about to Bubble Burp (block and Strength: its move's program), a Gremlin Merc whose Surprise adds two
        // gremlins at its death (its AfterDeath, theirs AfterAddedToRoom), a Frog Knight, and a Silent's cards in hand and piles.
        var fight = new GameFight();
        fight.Add<StrikeSilent>();
        fight.Add<DefendSilent>();
        fight.Add<Neutralize>();
        fight.Add<Survivor>();
        fight.Add<Backflip>();
        fight.Add<StrikeSilent>(PileType.Draw);
        fight.Add<Acrobatics>(PileType.Draw);
        fight.Add<DeadlyPoison>(PileType.Discard);
        Enemy<Seapunk>(fight, "BUBBLE_BURP_MOVE");
        var merc = Enemy<GremlinMerc>(fight, "GIMME_MOVE");
        ModelDb.Power<SurprisePower>().ToMutable().ApplyInternal(merc, amount: 1m);
        // A Frog Knight, whose move rules branch on its HP (a conditional branch's lambda: Intent Forecast).
        Enemy<FrogKnight>(fight, "TONGUE_LASH");

        var (runtimeReads, harmonyReads) = (RuntimeCode.ReadsOnThisThread, GameCode.ReadsOnThisThread);
        var state = SimAdapter.Build(fight.Me, fight.Combat, out _);
        // The intents' reads on the same frame: each move's powers and block (Intent Details), the move rules with their conditions.
        foreach (var enemy in fight.Combat.Enemies)
        {
            var monster = enemy.Monster!;
            _ = MovePowers.Of(monster, monster.NextMove);
            _ = MovePowers.BlockOf(monster, monster.NextMove);
            Assert.NotNull(IntentReader.Graph(monster.MoveStateMachine!, monster));
        }
        Assert.Equal((0, 0), (RuntimeCode.ReadsOnThisThread - runtimeReads, GameCode.ReadsOnThisThread - harmonyReads));
        Assert.NotNull(state.Enemies[0].Move); // the move was read (from the load's read)
        Assert.NotNull(state.Enemies[1].Spawns);
    }
}
