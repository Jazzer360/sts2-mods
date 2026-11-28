using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Decision #411: a random branch's weight read from its lambda's code (RandomBranch.Ways), asked of the projected state as the enemy rolls, never as
/// the game's lambda answered it during your turn: a number of its own its moves count (Two-Tailed Rat's TurnsUntilSummonable, counted down by its move
/// before the roll), forgotten when a move writes it in a way that isn't read; the encounter's free slots; a teammate's next move, rolled before it in
/// the fight's order, each outcome of a random one asked on its own.
/// </summary>
public class ProjectedWeightTests
{
    private const string Wait = "Monster.Rat.Wait";

    private static SimState Fight(params SimEnemy[] enemies)
    {
        var state = new SimState { TurnNumber = 1, RoundNumber = 1 };
        state.You.Hp = 50;
        state.You.MaxHp = 50;
        state.Enemies.AddRange(enemies);
        return state;
    }

    private static SimEnemy Rat(int id) => new() { Id = id, Name = $"Rat{id}", Monster = "Rat", Hp = 20, NextMoveId = "A" };

    private static RandomBranch Branch(string to, double weight, IReadOnlyList<(SimCondition When, double Weight)>? ways = null) =>
        new(to, MoveRepeat.CanRepeatForever, MaxTimes: 0, Cooldown: 0, Weight: (float)weight) { Ways = ways };

    /// <summary>A after its move goes to PICK: B weighted as <paramref name="bWays"/> say (its weight now 0), C 1.</summary>
    private static ProjectedRules Rules(IReadOnlyList<(SimCondition When, double Weight)> bWays)
    {
        var graph = new MoveGraph(new Dictionary<string, MoveNode>
        {
            ["A"] = new MoveNodeMove("A", FollowUp: "PICK"),
            ["PICK"] = new MoveNodeRandom("PICK", new[] { Branch("B", weight: 0, bWays), Branch("C", weight: 1) }),
            ["B"] = new MoveNodeMove("B", FollowUp: "A"),
            ["C"] = new MoveNodeMove("C", FollowUp: "A"),
        }, InitialId: "A");
        var moves = new[] { "A", "B", "C" }.ToDictionary(move => move, move => new ProjectedMove(move) { Actions = Array.Empty<SimAction>() });
        return new ProjectedRules(graph, "A", "PICK", new[] { "A" }, moves);
    }

    private static readonly IReadOnlyList<(SimCondition When, double Weight)> OnceItsWaitIsOver = new (SimCondition, double)[]
    {
        (new CountCondition(SimCounted.MonsterCounter, SimCompare.LessOrEqual, Than: 0) { Field = Wait }, 3),
        (new NotCondition(new CountCondition(SimCounted.MonsterCounter, SimCompare.LessOrEqual, Than: 0) { Field = Wait }), 0),
    };

    private static double Chance(Projection projection, int enemyId, string move) =>
        projection.ForecastOf(enemyId) is [var first, ..] ? first.Moves.FirstOrDefault(option => option.Move == move).Chance : -1;

    private static Projection Project(SimState state, Dictionary<int, ProjectedRules> rules) =>
        SimEngine.Project(state, Array.Empty<IReadOnlyList<ProjectedChoice>>(), rules);

    [Theory]
    [InlineData(1, 0.75)]
    [InlineData(2, 0.0)]
    public void ACountItsMoveCountsDownIsAskedAfterTheMove(int wait, double chance)
    {
        // Its move on screen counts its wait down by one (this.Wait--) before the roll: one left now is none at the roll, so B (3 to C's 1) can come.
        var rat = Rat(id: 0);
        rat.Fields = rat.Fields.With(Wait, wait);
        rat.Move = new SimAction[] { new MoveStoreAction(Wait, Amount: -1, Adds: true, Counter: true) };
        var projection = Project(Fight(rat), new() { [0] = Rules(OnceItsWaitIsOver) });
        if (chance == 0) Assert.Equal(1.0, Chance(projection, enemyId: 0, "C"));
        else Assert.Equal(chance, Chance(projection, enemyId: 0, "B"), precision: 6);
    }

    [Fact]
    public void ACountItsMoveSetsUnreadIsForgottenAndTheRollUnknown()
    {
        // Its move sets the count where the reader doesn't follow it (a lambda it doesn't read): the roll asks what isn't known, so the forecast ends
        // before it, with the enemy listed as one whose roll couldn't be told.
        var rat = Rat(id: 0);
        rat.Fields = rat.Fields.With(Wait, 0);
        rat.Move = new SimAction[] { new MoveStoreAction(Wait, Amount: 0, Unknown: "set in a lambda", Counter: true) };
        var projection = Project(Fight(rat), new() { [0] = Rules(OnceItsWaitIsOver) });
        Assert.Contains(0, projection.UnknownRolls);
        Assert.Empty(projection.ForecastOf(enemyId: 0)!);
    }

    [Fact]
    public void ACountNotReadIsNotKnown()
    {
        var projection = Project(Fight(Rat(id: 0)), new() { [0] = Rules(OnceItsWaitIsOver) });
        Assert.Contains(0, projection.UnknownRolls);
    }

    [Fact]
    public void ALaterEnemyAsksAnEarlierOnesRollInEachOutcome()
    {
        // B only while no teammate's next move is B. The first rolls B or C, half each; the second, rolled after it, can't take B where the first did:
        // C there; half each where the first took C. So a quarter B.
        var noTeammateOnB = new (SimCondition, double)[]
        {
            (new CountCondition(SimCounted.SideNextMove, SimCompare.Greater, Than: 0) { Move = "B", ExcludingItself = true }, 0),
            (new NotCondition(new CountCondition(SimCounted.SideNextMove, SimCompare.Greater, Than: 0) { Move = "B", ExcludingItself = true }), 1),
        };
        var projection = Project(Fight(Rat(id: 0), Rat(id: 1)), new() { [0] = Rules(noTeammateOnB), [1] = Rules(noTeammateOnB) });
        Assert.Equal(0.5, Chance(projection, enemyId: 0, "B"), precision: 6);
        Assert.Equal(0.25, Chance(projection, enemyId: 1, "B"), precision: 6);
        Assert.Equal(0.75, Chance(projection, enemyId: 1, "C"), precision: 6);
    }

    [Fact]
    public void ATeammateWhoseNextMoveIsntKnownLeavesItUnknown()
    {
        var noTeammateOnB = new (SimCondition, double)[]
        {
            (new CountCondition(SimCounted.SideNextMove, SimCompare.Greater, Than: 0) { Move = "B", ExcludingItself = true }, 0),
            (new NotCondition(new CountCondition(SimCounted.SideNextMove, SimCompare.Greater, Than: 0) { Move = "B", ExcludingItself = true }), 1),
        };
        var hidden = Rat(id: 1);
        hidden.NextMoveId = null; // behind a hidden intent
        var state = Fight(Rat(id: 0), hidden);
        Assert.Null(SimEngine.Holds(state, noTeammateOnB[0].Item1, monster: 0));
    }

    [Fact]
    public void FreeSlotsAreTheEncountersLessTheLivingInThem()
    {
        var noSlotFree = new CountCondition(SimCounted.FreeSlots, SimCompare.Equal, Than: 0);
        var inSlot = Rat(id: 0);
        inSlot.InSlot = true;
        var dead = Rat(id: 1);
        dead.InSlot = true;
        dead.Hp = 0;
        var state = Fight(inSlot, dead);
        state.EncounterSlots = 2;
        Assert.Equal(false, SimEngine.Holds(state, noSlotFree, monster: 0));
        state.EncounterSlots = 1;
        Assert.Equal(true, SimEngine.Holds(state, noSlotFree, monster: 0));
        state.EncounterSlots = null;
        Assert.Null(SimEngine.Holds(state, noSlotFree, monster: 0));
    }

    private const string Calls = "Monster.Rat.Calls";

    /// <summary>B (3 to C's 1) once its count of calls is 1.</summary>
    private static readonly IReadOnlyList<(SimCondition When, double Weight)> OnceItCalled = new (SimCondition, double)[]
    {
        (new CountCondition(SimCounted.MonsterCounter, SimCompare.Equal, Than: 1) { Field = Calls }, 3),
        (new NotCondition(new CountCondition(SimCounted.MonsterCounter, SimCompare.Equal, Than: 1) { Field = Calls }), 0),
    };

    [Fact]
    public void ACountSetOnEveryEnemyOfItsClassIsTheMostOfThemPlusOne()
    {
        // Decision #416, Call for Backup: a rat joins (its count 0, as a new rat has it), then every rat's count is the most of theirs plus one. The rolls
        // after it ask the count: 1 on all three, so B can come for each.
        var caller = Rat(id: 0);
        caller.Fields = caller.Fields.With(Calls, 0);
        var joining = new SimSpawn("Rat", new[] { 20, 21 }) { Monster = "Rat", Fields = NamedCounts.Empty.With(Calls, 0), NextMoveId = "A" };
        caller.Move = new SimAction[] { new MoveSpawnAction(joining) { ProjectionOnly = true }, new MoveCounterOfEachAction("Rat", Calls, MostPlus: 1) };
        var other = Rat(id: 1);
        other.Fields = other.Fields.With(Calls, 0);
        var rules = Rules(OnceItCalled);
        var projection = SimEngine.Project(Fight(caller, other), Array.Empty<IReadOnlyList<ProjectedChoice>>(), new Dictionary<int, ProjectedRules> { [0] = rules, [1] = rules },
            joining: new Dictionary<string, ProjectedRules>(StringComparer.Ordinal) { ["Rat"] = rules });
        Assert.Empty(projection.UnknownRolls);
        foreach (var rat in new[] { 0, 1, 2 }) Assert.Equal(0.75, Chance(projection, enemyId: rat, "B"), precision: 6);
    }

    [Fact]
    public void ACountSetOnEveryEnemyOfItsClassIsForgottenWhereOneDoesntHoldIt()
    {
        var caller = Rat(id: 0);
        caller.Fields = caller.Fields.With(Calls, 0);
        caller.Move = new SimAction[] { new MoveCounterOfEachAction("Rat", Calls, MostPlus: 1) };
        var projection = Project(Fight(caller, Rat(id: 1)), new() { [0] = Rules(OnceItCalled), [1] = Rules(OnceItCalled) });
        Assert.Contains(0, projection.UnknownRolls);
    }
}
