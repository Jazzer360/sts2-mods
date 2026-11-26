using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// What an enemy's move adds to the fight, read from code (decision #376): Fogmog's Illusion move (CreatureCmd.Add&lt;EyeWithTeeth&gt;), The Obscura's
/// (CreatureCmd.Add&lt;Parafright&gt;). The creature joins as the step runs, with the HP it starts with and its AfterAddedToRoom's powers (Illusion, and
/// the Minion Illusion's AfterApplied gives it); the enemies that act were listed as the turn started (CombatManager.ExecuteEnemyTurn's
/// Enemies.ToList()), so it makes no move this turn, and it's in the fight for the rest of the turn (Hook.AfterCreatureAddedToCombat, the turn's end).
/// </summary>
public class MoveAddsTests
{
    private static SimSpawn Eye(params int[] hp) => new("Eye with Teeth", hp)
    {
        Monster = "EyeWithTeeth", Powers = PowerSet.Empty.With(PowerId.Illusion, amount: 1).With(PowerId.Minion, amount: 1), UniqueFromEarlier = true,
    };

    private static SimEnemy Fogmog(SimSpawn spawn) => new() { Id = 0, Name = "Fogmog", Monster = "Fogmog", Hp = 70, Move = new SimAction[] { new MoveSpawnAction(spawn) } };

    private static SimState Fight(params SimEnemy[] enemies)
    {
        var state = new SimState { TurnNumber = 1 };
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        state.Enemies.AddRange(enemies);
        return state;
    }

    [Fact]
    public void TheCreatureAMoveAddsJoinsWithItsPowers()
    {
        var biter = new SimEnemy { Id = 1, Name = "Biter", Hp = 30, Hits = new() { 5 } };
        var outcome = SimEngine.Run(Fight(Fogmog(Eye(6)), biter));
        var branch = Assert.Single(outcome.Branches);
        Assert.Null(branch.Unmodelled);
        var eye = branch.Enemy(id: 2)!;
        Assert.Equal(("Eye with Teeth", "EyeWithTeeth", 6, 0), (eye.Name, eye.Monster, eye.Hp, eye.AddedBy!.Value));
        Assert.Equal(1, eye.Powers[PowerId.Illusion]);
        Assert.Equal(1, eye.Powers[PowerId.Minion]);
        // The enemy after Fogmog still makes its move; the eye, listed after the turn started, makes none.
        Assert.Equal(5, outcome.TakenMax);
    }

    [Fact]
    public void PhilosophersStoneGivesTheCreatureAMoveAddsStrength()
    {
        // Hook.AfterCreatureAddedToCombat as it joins, in the enemy turn: Philosopher's Stone (read from its code) gives it Strength.
        Assert.IsType<ReadModel>(SimModels.Get(RelicId.PhilosophersStone.Name));
        var fogmog = Fogmog(Eye(6));
        fogmog.Powers = fogmog.Powers.With(PowerId.Strength, amount: 1); // the board's enemies have it already, as the adapter reads them
        var state = Fight(fogmog);
        state.You.Relics = state.You.Relics.With(RelicId.PhilosophersStone, 0);
        var branch = Assert.Single(SimEngine.Run(state).Branches);
        Assert.Null(branch.Unmodelled);
        Assert.Equal(1, branch.Enemy(id: 1)!.Powers[PowerId.Strength]);
    }

    [Fact]
    public void AnHpItCouldStartWithAmongSeveralLeavesTheTurnUnknown()
    {
        // The enemy turn runs on one state: a roll of its HP isn't branched on there, so what follows is unknown, never a pick.
        var outcome = SimEngine.Run(Fight(Fogmog(Eye(5, 6))));
        Assert.True(outcome.AnyUnmodelled);
        Assert.Single(Assert.Single(outcome.Branches).Enemies);
    }

    /// <summary>The rules of a monster that joins (decision #378): BITE (4 damage) first, then REST, then BITE again, read as ProjectedRules.Joining.</summary>
    private static Dictionary<string, ProjectedRules> BiteThenRest()
    {
        var graph = new MoveGraph(new Dictionary<string, MoveNode>
        {
            ["BITE"] = new MoveNodeMove("BITE", FollowUp: "REST"),
            ["REST"] = new MoveNodeMove("REST", FollowUp: "BITE"),
        }, InitialId: "BITE");
        var moves = new Dictionary<string, ProjectedMove>
        {
            ["BITE"] = new("BITE") { Intent = new List<IntentHit> { new(Base: 4) }, Actions = new SimAction[] { new MoveAttackAction(FirstHit: 0, Hits: 1) }, OnlyAttacks = true },
            ["REST"] = new("REST") { Actions = Array.Empty<SimAction>() },
        };
        return new Dictionary<string, ProjectedRules>(StringComparer.Ordinal) { ["EyeWithTeeth"] = ProjectedRules.Joining(graph, moves) };
    }

    private static string Moves(Projection projection, int enemyId) =>
        string.Join(" | ", projection.ForecastOf(enemyId)!.Select(turn => string.Join(", ", turn.Moves.Select(move => move.Chance < 1 ? $"{move.Move} {move.Chance:0.##}" : move.Move))));

    [Fact]
    public void TheCreatureAMoveAddsRollsItsMovesInTheProjection()
    {
        // It joins in the enemy turn (no roll then: CreatureCmd.Add rolls only on the players' side), so its first move is rolled as your next turn
        // starts, from its new machine's initial state (MonsterMoveStateMachine.RollMove keeps a move it starts on), then its rules go on from there.
        var projection = SimEngine.Project(Fight(Fogmog(Eye(6))), Array.Empty<IReadOnlyList<ProjectedChoice>>(), rules: null, joining: BiteThenRest());
        Assert.Equal("BITE | REST | BITE", Moves(projection, enemyId: 1));
        Assert.Equal(new[] { 4 }, projection.Of(turn: 0, enemyId: 1, move: "BITE")!.AtYourTurnStart);
        Assert.Equal(new[] { 4 }, projection.Of(turn: 2, enemyId: 1, move: "BITE")!.AtYourTurnStart);
    }

    [Fact]
    public void ACreatureWhoseRulesArentReadMakesNoMoveCounted()
    {
        // Its monster's rules not read: it makes no move the projection counts, as before.
        var projection = SimEngine.Project(Fight(Fogmog(Eye(6))), Array.Empty<IReadOnlyList<ProjectedChoice>>(), rules: null, joining: null);
        Assert.Null(projection.ForecastOf(enemyId: 1));
        Assert.Empty(projection.Turns);
    }

    [Fact]
    public void AJoiningCreaturesRandomFirstMoveEndsItsForecast()
    {
        // Its initial state a random pick: that turn shows both moves with their chances, and nothing after it (the first random pick ends it).
        var graph = new MoveGraph(new Dictionary<string, MoveNode>
        {
            ["PICK"] = new MoveNodeRandom("PICK", new[] { new RandomBranch("BITE", MoveRepeat.CanRepeatForever, MaxTimes: 0, Cooldown: 0, Weight: 1), new RandomBranch("REST", MoveRepeat.CanRepeatForever, MaxTimes: 0, Cooldown: 0, Weight: 3) }),
            ["BITE"] = new MoveNodeMove("BITE", FollowUp: "PICK"),
            ["REST"] = new MoveNodeMove("REST", FollowUp: "PICK"),
        }, InitialId: "PICK");
        var joining = new Dictionary<string, ProjectedRules>(StringComparer.Ordinal) { ["EyeWithTeeth"] = ProjectedRules.Joining(graph, BiteThenRest()["EyeWithTeeth"].Moves) };
        var projection = SimEngine.Project(Fight(Fogmog(Eye(6))), Array.Empty<IReadOnlyList<ProjectedChoice>>(), rules: null, joining);
        Assert.Equal("REST 0.75, BITE 0.25", Moves(projection, enemyId: 1));
        Assert.Single(projection.Turns);
    }

    private static SimSpawn Bot(string monster) => new(monster, new[] { 18, 19 }) { Monster = monster, UniqueFromEarlier = true };

    /// <summary>A Fabricator whose move adds a bot it picks at random from Zapbot and Stabbot, other than the one it made last (decision #379).</summary>
    private static SimEnemy Fabricator(string? lastNow) => new()
    {
        Id = 0, Name = "Fabricator", Monster = "Fabricator", Hp = 150,
        Move = new SimAction[] { new MovePickSpawnAction(new[] { Bot("Zapbot"), Bot("Stabbot") }, Field: "_lastSpawned", lastNow) { AfterJoin = PowerSet.Empty.With(PowerId.Minion, amount: 1) } },
    };

    [Fact]
    public void ABotOtherThanTheLastItMadeJoinsWithItsMinion()
    {
        // SpawnBot: NextItem of the set less _lastSpawned (a Zapbot, read on the board): the Stabbot, certainly; Minion lands on it as it joins (its
        // summoner the applier), and the projection's later turns go on with it (its HP among several isn't followed there).
        var projection = SimEngine.Project(Fight(Fabricator(lastNow: "Zapbot")), Array.Empty<IReadOnlyList<ProjectedChoice>>(), rules: null, joining: StabbotRules());
        Assert.Equal("STAB", Moves(projection, enemyId: 1).Split(" | ")[0]);
        Assert.Null(projection.Of(turn: 0, enemyId: 1, move: "STAB")!.Unknown);
    }

    [Fact]
    public void ARandomBotIsABranchEachAndTheProjectionCantShareIt()
    {
        // No bot made yet: Zapbot or Stabbot, half each (never rolled). The branches don't share which creature joined, so the turns after are unknown.
        var projection = SimEngine.Project(Fight(Fabricator(lastNow: null)), Array.Empty<IReadOnlyList<ProjectedChoice>>(), rules: null, joining: StabbotRules());
        Assert.Contains(projection.Turns.SelectMany(row => row.Values).SelectMany(moves => moves.Values), hits => hits.Unknown == "a roll decides which enemies are in the fight");
    }

    [Fact]
    public void AfterAPlayTheRandomBotAddsNothing()
    {
        // The enemy turn after a play runs on one state: the pick isn't branched there (its HP is a roll), so it adds nothing, as before it was read.
        var branch = Assert.Single(SimEngine.Run(Fight(Fabricator(lastNow: "Zapbot"))).Branches);
        Assert.Null(branch.Unmodelled);
        Assert.Single(branch.Enemies);
    }

    /// <summary>Stabbot's rules as it joins: STAB (6 damage) every turn.</summary>
    private static Dictionary<string, ProjectedRules> StabbotRules()
    {
        var graph = new MoveGraph(new Dictionary<string, MoveNode> { ["STAB"] = new MoveNodeMove("STAB", FollowUp: "STAB") }, InitialId: "STAB");
        var moves = new Dictionary<string, ProjectedMove>
        {
            ["STAB"] = new("STAB") { Intent = new List<IntentHit> { new(Base: 6) }, Actions = new SimAction[] { new MoveAttackAction(FirstHit: 0, Hits: 1) }, OnlyAttacks = true },
        };
        return new Dictionary<string, ProjectedRules>(StringComparer.Ordinal) { ["Stabbot"] = ProjectedRules.Joining(graph, moves), ["Zapbot"] = ProjectedRules.Joining(graph, moves) };
    }

    [Fact]
    public void ADeadSummonerAddsNothing()
    {
        // Killed before the enemy turn: it makes no move, so nothing joins.
        var fogmog = Fogmog(Eye(6));
        fogmog.Hp = 0;
        var branch = Assert.Single(SimEngine.Run(Fight(fogmog)).Branches);
        Assert.Single(branch.Enemies);
    }
}
