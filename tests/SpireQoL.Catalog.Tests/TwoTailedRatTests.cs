using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #411, from the owner's Defect run: the Two-Tailed Rat's random weights ask CanSummon (TurnsUntilSummonable &lt;= 0, which each of its moves
/// counts down; CallForBackupCount &lt; 3; a free slot of the encounter; no teammate whose next move is Call for Backup). Read as they stood during
/// your turn, before its move counted down, the projection left Call for Backup out a turn early, where the game rolled it at 75 to 1/12s.
/// </summary>
[Collection(GameFightCollection.Name)]
public class TwoTailedRatTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static (GameFight Fight, List<Creature> Rats) Rats(int turnsUntilSummonable, params string[] moves)
    {
        var fight = new GameFight();
        typeof(CombatState).GetField("_encounter", Private)!.SetValue(fight.Combat, ModelDb.Encounter<TwoTailedRatsNormal>().ToMutable());
        var slots = new[] { "third", "fourth", "fifth" };
        var rats = new List<Creature>();
        for (var i = 0; i < moves.Length; i++)
        {
            var creature = fight.Combat.CreateCreature(ModelDb.Monster<TwoTailedRat>().ToMutable(), CombatSide.Enemy, slot: slots[i]);
            fight.Combat.AddCreature(creature);
            var monster = creature.Monster!;
            monster.SetUpForCombat();
            var move = (MoveState)monster.MoveStateMachine!.States[moves[i]];
            typeof(MonsterModel).GetProperty(nameof(MonsterModel.NextMove))!.SetValue(monster, move);
            monster.MoveStateMachine.ForceCurrentState(move);
            monster.MoveStateMachine.StateLog.Add(move);
            typeof(TwoTailedRat).GetProperty("TurnsUntilSummonable", Private)!.SetValue(monster, turnsUntilSummonable);
            rats.Add(creature);
        }
        return (fight, rats);
    }

    private static double Chance(Projection projection, int enemyId, string move) =>
        projection.ForecastOf(enemyId) is [var first, ..] ? first.Moves.FirstOrDefault(option => option.Move == move).Chance : -1;

    [Fact]
    public void ItsRulesAskTheNumbersItsMovesCount()
    {
        Assert.Equal(new[] { "Monster.TwoTailedRat.CallForBackupCount", "Monster.TwoTailedRat.TurnsUntilSummonable" },
            MoveConditions.CounterKeys(typeof(TwoTailedRat)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryNumberAMonstersRulesAskIsWrittenOnlyByItsOwnMoves()
    {
        // The projection follows a counter through its monster's moves (their stores, or forgetting it where the code isn't followed): a write anywhere
        // else in the game (a power's hook, its death, another monster) would leave it as it was read, a certainty the game doesn't keep.
        GameModels.EnsureLoaded();
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var assembly = typeof(MonsterModel).Assembly;
        var monsters = assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(MonsterModel)) && !type.IsAbstract).ToList();
        var setters = new Dictionary<string, (Type Monster, HashSet<string> Moves)>(StringComparer.Ordinal);
        foreach (var monster in monsters)
        {
            var keys = MoveConditions.CounterKeys(monster);
            if (keys.Count == 0) continue;
            if (ModelDb.Monsters.FirstOrDefault(each => each.GetType() == monster) is not { } canonical) continue;
            var model = (MonsterModel)canonical.ToMutable();
            model.SetUpForCombat();
            var moves = model.MoveStateMachine!.States.Values.OfType<MoveState>()
                .Select(move => Sts2Mods.Core.Game.Sts2State.Private<Delegate?>(move, "_onPerform", fallback: null)?.Method.Name).OfType<string>().ToHashSet(StringComparer.Ordinal);
            foreach (var key in keys) setters[monster.Name + ".set_" + key[(key.LastIndexOf('.') + 1)..]] = (monster, moves);
        }
        Assert.NotEmpty(setters);
        var elsewhere = new List<string>();
        foreach (var type in assembly.GetTypes())
            foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
            {
                List<SpireQoL.Procs.Programs.CodeOp> code;
                try { code = SpireQoL.Procs.Programs.RuntimeCode.Read(method); } catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NotSupportedException) { continue; }
                foreach (var call in code)
                {
                    if (!call.IsCall || call.Member is not { } member || !setters.TryGetValue(member.DeclaringType + "." + member.Name, out var counted)) continue;
                    var owner = type.DeclaringType ?? type;
                    var inAMove = owner == counted.Monster && counted.Moves.Any(move => type.Name.Contains("<" + move + ">", StringComparison.Ordinal) || method.Name.Contains("<" + move + ">", StringComparison.Ordinal) || method.Name == move);
                    if (!inAMove) elsewhere.Add($"{member.DeclaringType}.{member.Name} in {type.Name}.{method.Name}");
                }
            }
        Assert.True(elsewhere.Count == 0, "A number a monster's rules ask written outside its own moves (follow it there, or leave it unknown):\n" + string.Join("\n", elsewhere.Distinct()));
    }

    [Fact]
    public void ARatWhoseMoveCountsItsWaitDownCanCallForBackupNextTurn()
    {
        // One turn left to wait: its Screech counts it down to 0, so the roll after it can call for backup, 0.75 against two 1/12s (Screech can't
        // repeat). Read as it stood (1 left), it couldn't.
        var (fight, rats) = Rats(turnsUntilSummonable: 1, "SCREECH_MOVE");
        var input = SimAdapter.ProjectionStart(fight.Me, fight.Combat)!;
        var projection = SimEngine.Project(input.State, input.Turns, input.Rules, input.Joining);
        Assert.Equal(0.75 / (0.75 + 2 / 12.0), Chance(projection, input.Ids[rats[0]], "CALL_FOR_BACKUP_MOVE"), precision: 4);
    }

    [Fact]
    public void ARatTwoTurnsFromCallingCantCallNextTurn()
    {
        var (fight, rats) = Rats(turnsUntilSummonable: 2, "SCREECH_MOVE");
        var input = SimAdapter.ProjectionStart(fight.Me, fight.Combat)!;
        var projection = SimEngine.Project(input.State, input.Turns, input.Rules, input.Joining);
        Assert.Equal(0, Chance(projection, input.Ids[rats[0]], "CALL_FOR_BACKUP_MOVE"));
        Assert.Equal(0.5, Chance(projection, input.Ids[rats[0]], "SCRATCH_MOVE"), precision: 4);
    }

    [Fact]
    public void ALaterRatCantCallWhereAnEarlierOneRolledACall()
    {
        // The rats roll in the fight's order: the second asks the first's new move. Where the first rolled Call for Backup (0.818), the second can't
        // summon (Bite or Screech, half each); where it didn't, the second calls at 0.818 too.
        var (fight, rats) = Rats(turnsUntilSummonable: 1, "SCREECH_MOVE", "SCRATCH_MOVE");
        var input = SimAdapter.ProjectionStart(fight.Me, fight.Combat)!;
        var projection = SimEngine.Project(input.State, input.Turns, input.Rules, input.Joining);
        var call = 0.75 / (0.75 + 2 / 12.0);
        Assert.Equal(call, Chance(projection, input.Ids[rats[0]], "CALL_FOR_BACKUP_MOVE"), precision: 4);
        Assert.Equal((1 - call) * call, Chance(projection, input.Ids[rats[1]], "CALL_FOR_BACKUP_MOVE"), precision: 4);
        Assert.Equal(call * 0.5 + (1 - call) * (1 / 12.0) / (0.75 + 2 / 12.0), Chance(projection, input.Ids[rats[1]], "SCREECH_MOVE"), precision: 4);
    }

    [Fact]
    public void CallForBackupsCountIsReadAsEveryRatsMostPlusOne()
    {
        // Its ForEach sets every rat's CallForBackupCount to the most of them plus one (a lambda over the list of rats, the value it captures stored before).
        var program = Procs.Programs.ModelPrograms.Of(typeof(TwoTailedRat), "CallForBackup");
        var loop = Assert.Single(program.Steps.OfType<Procs.Programs.LoopStep>());
        var store = Assert.IsType<Procs.Programs.StoreStep>(Assert.Single(loop.Body));
        Assert.Equal(Procs.Programs.ProgramReader.ItemPrefix + "TwoTailedRat.CallForBackupCount", store.Field);
        Assert.Contains("Max(", store.Value.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AfterACallForBackupTheRatsNextRollsAreKnown()
    {
        // Call for Backup brings a rat into the encounter's last free slot and sets every rat's CallForBackupCount to the most of them plus one (1): the
        // rolls after it ask both, and both are followed. The caller can't call again (UseOnlyOnce): Scratch, Disease Bite and Screech, a third each.
        // The second rat can still call (1 < 3, two slots free, no teammate about to): 0.75 against Disease Bite's and Screech's 1/12 (Scratch can't
        // repeat). The rat that joined waits its two turns (its own TurnsUntilSummonable, as a new rat has it): it can't call on its first roll.
        var (fight, rats) = Rats(turnsUntilSummonable: 0, "CALL_FOR_BACKUP_MOVE", "SCRATCH_MOVE");
        var input = SimAdapter.ProjectionStart(fight.Me, fight.Combat)!;
        var projection = SimEngine.Project(input.State, input.Turns, input.Rules, input.Joining);
        Assert.DoesNotContain(input.Ids[rats[0]], projection.UnknownRolls);
        Assert.Equal(1 / 3.0, Chance(projection, input.Ids[rats[0]], "SCRATCH_MOVE"), precision: 4);
        Assert.Equal(0.75 / (0.75 + 2 / 12.0), Chance(projection, input.Ids[rats[1]], "CALL_FOR_BACKUP_MOVE"), precision: 4);
        var joined = input.Ids.Values.Max() + 1;
        Assert.Equal(0, Chance(projection, joined, "CALL_FOR_BACKUP_MOVE"));
        Assert.Equal(1 / 3.0, Chance(projection, joined, "SCRATCH_MOVE"), precision: 4);
    }
}
