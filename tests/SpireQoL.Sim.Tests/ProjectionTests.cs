using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The Intent Forecast's later turns, projected (decision #304: SimEngine.Project): the board through this turn's end with no plays, the enemy turn and
/// the enemies' turn end, then each forecast turn (your next turn's start and end with no plays, the enemies' turn start, each enemy's forecast move,
/// their turn's end), each move's hits on you as they land and as its intent reads when your turn starts. What's predictable counts (Ritual's
/// Strength, Weak wearing off, a move's own buff before its hits); a roll or what the simulator can't follow leaves the number unknown.
/// </summary>
public class ProjectionTests
{
    private static SimEnemy Attacker(int id, int hp, int hit) =>
        new() { Id = id, Name = $"E{id}", Hp = hp, Hits = new() { hit }, Intent = new List<IntentHit> { new(Base: hit) }, IntendsToAttack = true };

    private static SimState Fight(params SimEnemy[] enemies)
    {
        var state = new SimState();
        state.You.Hp = 80;
        state.Enemies.AddRange(enemies);
        return state;
    }

    /// <summary>An attack move of <paramref name="hits"/> hits of <paramref name="damage"/>, with its code's steps (its attacks, in order).</summary>
    private static ProjectedMove Attack(string id, int damage, int hits = 1) => new(id)
    {
        Intent = Enumerable.Repeat(new IntentHit(Base: damage), hits).ToList(),
        Actions = new SimAction[] { new MoveAttackAction(FirstHit: 0, Hits: hits) },
        OnlyAttacks = true,
    };

    private static ProjectedChoice Certain(int enemyId, ProjectedMove move) => new(enemyId, new[] { (move, 1.0) });

    private static IReadOnlyList<IReadOnlyList<ProjectedChoice>> Turns(params ProjectedChoice[][] turns) => turns;

    /// <summary>
    /// A big board for a projection: four enemies (one with Ritual, one Weak), three forecast turns, two random picks on the last (three moves and
    /// two), three Lightning orbs (a random enemy each, at each of your turns' ends) and Juggernaut on your block gains.
    /// </summary>
    internal static (SimState State, IReadOnlyList<IReadOnlyList<ProjectedChoice>> Turns) BigBoard()
    {
        var state = Fight(Attacker(id: 0, hp: 90, hit: 6), Attacker(id: 1, hp: 80, hit: 9), Attacker(id: 2, hp: 70, hit: 5), Attacker(id: 3, hp: 85, hit: 7));
        state.You.Hp = 400; // you live through all three turns
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Ritual, amount: 2);
        state.Enemies[2].Powers = PowerSet.Empty.With(PowerId.Weak, amount: 2);
        state.You.Orbs = Enumerable.Range(0, 3).Select(_ => new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 }).ToList();
        state.You.OrbCapacity = 3;
        state.You.Juggernaut = 3;
        state.You.Plating = 4;
        var shout = new ProjectedMove("SHOUT") { Actions = new SimAction[] { new MovePowerAction(PowerId.Strength, Amount: 2, To: new[] { 0, 1, 2, 3 }, Debuff: false) } };
        var guard = new ProjectedMove("GUARD") { Actions = new SimAction[] { new MoveBlockAction(Amount: 10, To: new[] { 2 }) } };
        IReadOnlyList<ProjectedChoice> Certain4(int damage) => new[]
        {
            Certain(enemyId: 0, Attack("CLAW", damage: damage, hits: 2)), Certain(enemyId: 1, Attack("SLAM", damage: damage + 3)),
            Certain(enemyId: 2, Attack("JAB", damage: damage - 1, hits: 3)), Certain(enemyId: 3, Attack("BITE", damage: damage + 1)),
        };
        var last = new[]
        {
            Certain(enemyId: 0, Attack("CLAW", damage: 6, hits: 2)),
            new ProjectedChoice(EnemyId: 1, new[] { (shout, 0.4), (Attack("SLAM", damage: 9), 0.4), (guard, 0.2) }),
            new ProjectedChoice(EnemyId: 2, new[] { (Attack("JAB", damage: 5, hits: 3), 0.5), (guard, 0.5) }),
            Certain(enemyId: 3, Attack("BITE", damage: 7)),
        };
        return (state, new[] { Certain4(damage: 6), Certain4(damage: 6), last });
    }

    [Fact]
    public void ABigBoardProjectsEveryTurn()
    {
        // Every turn is projected; on the last, the third enemy's random pick (after the second's) leaves the fourth's hit unknown only where a
        // pick changes it: the second's Shout gives it 2 Strength on one branch.
        var (state, turns) = BigBoard();
        var projection = SimEngine.Project(state, turns);
        Assert.True(projection.Turns.Count == 3, string.Join("; ", projection.Turns.SelectMany(row => row.Values.SelectMany(moves => moves.Values)).Select(hits => hits.Unknown).Where(why => why != null).Distinct()));
        Assert.Null(projection.CapHit);
        Assert.NotNull(projection.Of(turn: 1, enemyId: 0, move: "CLAW")!.Lands);
        Assert.Null(projection.Of(turn: 2, enemyId: 3, move: "BITE")!.Lands);
        Assert.Equal(new[] { 8, 8 }, projection.Of(turn: 0, enemyId: 0, move: "CLAW")!.Lands); // Ritual's 2 Strength from this turn's end
    }

    [Fact]
    public async Task AProjectionOnAWorkerIsTheSameAsOnTheSpot()
    {
        // SimJob.StartProjection (decision #107): the frame waits for it as for any search; its result is the projection made on the spot, whether it
        // finished before its background turn or after.
        var (state, turns) = BigBoard();
        var here = SimEngine.Project(state, turns);
        var job = SimJob.StartProjection(state, turns, "test", firstBudget: 1);
        await job.Ready.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(job.Finished);
        Assert.Null(job.Latest);
        Assert.Equal(Describe(here), Describe(job.Projected!));
    }

    [Fact]
    public async Task ACancelledProjectionHasNoResult()
    {
        var (state, turns) = BigBoard();
        var job = SimJob.StartProjection(state, turns, "test", firstBudget: 1);
        job.Cancel();
        await Task.WhenAny(job.Ready, Task.Delay(TimeSpan.FromMilliseconds(500)));
        Assert.True(job.Projected == null || job.Finished); // cancelled before it was over: nothing; over first: the whole projection
        Assert.False(job.Ready.IsFaulted);
    }

    private static string Describe(Projection projection) => string.Join("\n", projection.Turns.SelectMany((row, turn) => row.OrderBy(entry => entry.Key)
        .SelectMany(entry => entry.Value.OrderBy(move => move.Key, StringComparer.Ordinal).Select(move =>
            $"{turn} {entry.Key} {move.Key}: {(move.Value.Lands is { } lands ? string.Join(",", lands) : "?")} / {(move.Value.AtYourTurnStart is { } start ? string.Join(",", start) : "?")} {move.Value.Unknown}"))));

    [Fact]
    public void RitualsStrengthCountsInTheNextAttack()
    {
        // RitualPower.AfterSideTurnEnd: its owner taking part, the enemies as their turn ends: 3 Strength after this turn's attack, so the next 6 hits for 9,
        // the one after that for 12.
        var state = Fight(Attacker(id: 0, hp: 60, hit: 6));
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Ritual, amount: 3);
        var projection = SimEngine.Project(state, Turns(new[] { Certain(enemyId: 0, Attack("CLAW", damage: 6)) }, new[] { Certain(enemyId: 0, Attack("CLAW", damage: 6)) }));
        Assert.Equal(new[] { 9 }, projection.Of(turn: 0, enemyId: 0, move: "CLAW")!.Lands);
        Assert.Equal(new[] { 9 }, projection.Of(turn: 0, enemyId: 0, move: "CLAW")!.AtYourTurnStart);
        Assert.Equal(new[] { 12 }, projection.Of(turn: 1, enemyId: 0, move: "CLAW")!.Lands);
    }

    [Fact]
    public void WeakWearsOffOnSchedule()
    {
        // WeakPower.AfterSideTurnEnd: side == CombatSide.Enemy. Weak 1 weakens this turn's attack, then goes: the next 10 hits for 10.
        var state = Fight(Attacker(id: 0, hp: 60, hit: 10));
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Weak, amount: 1);
        var projection = SimEngine.Project(state, Turns(new[] { Certain(enemyId: 0, Attack("BITE", damage: 10)) }));
        Assert.Equal(new[] { 10 }, projection.Of(turn: 0, enemyId: 0, move: "BITE")!.Lands);
        // Vulnerable on you lasts its turns: 2 stacks make the next attack hit for 50% more, then 1 is left.
        state.Enemies[0].Powers = PowerSet.Empty;
        state.You.Powers = PowerSet.Empty.With(PowerId.Vulnerable, amount: 2);
        var vulnerable = SimEngine.Project(state, Turns(new[] { Certain(enemyId: 0, Attack("BITE", damage: 10)) }, new[] { Certain(enemyId: 0, Attack("BITE", damage: 10)) }));
        Assert.Equal(new[] { 15 }, vulnerable.Of(turn: 0, enemyId: 0, move: "BITE")!.Lands);
        Assert.Equal(new[] { 10 }, vulnerable.Of(turn: 1, enemyId: 0, move: "BITE")!.Lands);
    }

    [Fact]
    public void AMoveThatBuffsItselfFirstCountsItsBuff()
    {
        // Its code gives it 2 Strength before its attack: its hit lands for 8; as your turn starts its intent reads 6 (what the game shows then).
        var state = Fight(Attacker(id: 0, hp: 60, hit: 5));
        var roar = new ProjectedMove("ROAR")
        {
            Intent = new List<IntentHit> { new(Base: 6) },
            Actions = new SimAction[] { new MovePowerAction(PowerId.Strength, Amount: 2, To: new[] { 0 }, Debuff: false), new MoveAttackAction(FirstHit: 0, Hits: 1) },
        };
        var projection = SimEngine.Project(state, Turns(new[] { Certain(enemyId: 0, roar) }));
        Assert.Equal(new[] { 8 }, projection.Of(turn: 0, enemyId: 0, move: "ROAR")!.Lands);
        Assert.Equal(new[] { 6 }, projection.Of(turn: 0, enemyId: 0, move: "ROAR")!.AtYourTurnStart);
    }

    [Fact]
    public void ARandomPickBeforeItDecidesItsHitsOnlyWhenItMatters()
    {
        // The first enemy picks at random between giving the second 3 Strength and attacking; the second, acting after it, hits for 7 or 10: a roll
        // decides it, so it's unknown, though its intent as your turn starts (before either moves) reads 7.
        var state = Fight(Attacker(id: 0, hp: 60, hit: 4), Attacker(id: 1, hp: 60, hit: 7));
        var shout = new ProjectedMove("SHOUT") { Actions = new SimAction[] { new MovePowerAction(PowerId.Strength, Amount: 3, To: new[] { 1 }, Debuff: false) } };
        var pick = new ProjectedChoice(EnemyId: 0, new[] { (shout, 0.5), (Attack("JAB", damage: 4), 0.5) });
        var projection = SimEngine.Project(state, Turns(new[] { pick, Certain(enemyId: 1, Attack("CLUB", damage: 7)) }));
        Assert.Null(projection.Of(turn: 0, enemyId: 1, move: "CLUB")!.Lands);
        Assert.NotNull(projection.Of(turn: 0, enemyId: 1, move: "CLUB")!.Unknown);
        Assert.Equal(new[] { 7 }, projection.Of(turn: 0, enemyId: 1, move: "CLUB")!.AtYourTurnStart);
        // Each of the first enemy's own moves is known on its own branch.
        Assert.Equal(new[] { 4 }, projection.Of(turn: 0, enemyId: 0, move: "JAB")!.Lands);
        Assert.Empty(projection.Of(turn: 0, enemyId: 0, move: "SHOUT")!.Lands!);
    }

    [Fact]
    public void AMoveNotKnownLeavesOnlyWhatComesAfterItUnknown()
    {
        // The second enemy has no move known that turn (past its first random pick): the first, acting before it, is known; the turn after isn't.
        var state = Fight(Attacker(id: 0, hp: 60, hit: 5), Attacker(id: 1, hp: 60, hit: 3));
        var projection = SimEngine.Project(state, Turns(new[] { Certain(enemyId: 0, Attack("JAB", damage: 5)) }, new[] { Certain(enemyId: 0, Attack("JAB", damage: 5)) }));
        Assert.Equal(new[] { 5 }, projection.Of(turn: 0, enemyId: 0, move: "JAB")!.Lands);
        Assert.Contains("E1", projection.Of(turn: 1, enemyId: 0, move: "JAB")!.Unknown, StringComparison.Ordinal);
        Assert.Null(projection.Of(turn: 1, enemyId: 0, move: "JAB")!.Lands);
    }

    [Fact]
    public void WhatTheEnemiesTurnEndCantFollowLeavesTheLaterTurnsUnknown()
    {
        // Doom on it (decision #303: its kill isn't followed as the enemies' turn ends): the state after this turn is unknown, so the next turn is.
        var state = Fight(Attacker(id: 0, hp: 60, hit: 5));
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Doom, amount: 3);
        var projection = SimEngine.Project(state, Turns(new[] { Certain(enemyId: 0, Attack("JAB", damage: 5)) }));
        var hits = projection.Of(turn: 0, enemyId: 0, move: "JAB")!;
        Assert.Null(hits.Lands);
        Assert.Null(hits.AtYourTurnStart);
        Assert.Contains(PowerId.Doom.Name, hits.Unknown, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEnemyTheTurnKillsHasNoLaterHits()
    {
        // Poison 20 on it with 10 HP: it dies as the enemies' turn starts, before it moves; its later turn has no number to show.
        var state = Fight(Attacker(id: 0, hp: 10, hit: 5));
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Poison, amount: 20);
        var projection = SimEngine.Project(state, Turns(new[] { Certain(enemyId: 0, Attack("JAB", damage: 5)) }));
        Assert.Null(projection.Of(turn: 0, enemyId: 0, move: "JAB"));
    }
}
