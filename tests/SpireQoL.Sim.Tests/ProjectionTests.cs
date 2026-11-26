using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The Intent Forecast's later turns, projected (decisions #304, #308: SimEngine.Project): the board through this turn's end with no plays, the enemy
/// turn and the enemies' turn end, then each forecast turn (your next turn's start and end with no plays, the enemies' turn start, each enemy's
/// forecast move, their turn's end), each move's hits on you as they land and as its intent reads when your turn starts. What's predictable counts
/// (Ritual's Strength, Weak wearing off, a move's own buff before its hits); it follows only what the forecast shows, never anyone's HP or death, so a
/// random hit on an enemy doesn't split it; where a roll changes what it follows only what's certain counts, and what the simulator can't follow
/// leaves the number unknown.
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
        // Every turn is projected; on the last, the second enemy's random pick (its Shout gives every enemy 2 Strength on one branch) changes the
        // fourth's hit, which acts after it: only what's certain counts, its intent as your turn starts (7), and it says so.
        var (state, turns) = BigBoard();
        var projection = SimEngine.Project(state, turns);
        Assert.True(projection.Turns.Count == 3, string.Join("; ", projection.Turns.SelectMany(row => row.Values.SelectMany(moves => moves.Values)).Select(hits => hits.Unknown).Where(why => why != null).Distinct()));
        Assert.Null(projection.CapHit);
        Assert.Equal(new[] { 7 }, projection.Of(turn: 2, enemyId: 3, move: "BITE")!.Lands);
        Assert.NotNull(projection.Of(turn: 2, enemyId: 3, move: "BITE")!.Uncertain);
        Assert.Equal(new[] { 8, 8 }, projection.Of(turn: 0, enemyId: 0, move: "CLAW")!.Lands); // Ritual's 2 Strength from this turn's end
        Assert.Equal(new[] { 10, 10 }, projection.Of(turn: 1, enemyId: 0, move: "CLAW")!.Lands);
        Assert.Equal(new[] { 3, 3, 3 }, projection.Of(turn: 0, enemyId: 2, move: "JAB")!.Lands); // Weak 2: this turn's attack and the next
        Assert.Equal(new[] { 5, 5, 5 }, projection.Of(turn: 1, enemyId: 2, move: "JAB")!.Lands);
    }

    private const string SteamEruptionDamage = "Monster.WaterfallGiant.SteamEruptionDamage";

    /// <summary>
    /// A Waterfall Giant brought back about to blow (a dev review): About to Blow stores its Steam Eruption as SteamEruptionDamage
    /// (WaterfallGiant.AboutToBlowMove), which its Explode's Death Blow intent is (() => SteamEruptionDamage): 0 until then.
    /// </summary>
    private static SimState AboutToBlow(int steam)
    {
        var giant = new SimEnemy { Id = 0, Name = "Waterfall Giant", Monster = "WaterfallGiant", Hp = 999999999 };
        giant.Powers = PowerSet.Empty.With(PowerId.SteamEruption, amount: steam);
        giant.Move = new SimAction[] { new MoveStoreAction(SteamEruptionDamage, Amount: 0, OwnPower: PowerId.SteamEruption) };
        return Fight(giant);
    }

    private static ProjectedMove Explode() => new("EXPLODE_MOVE")
    {
        Intent = new[] { new IntentHit(Base: 0) { BaseFrom = SteamEruptionDamage } },
        Actions = new SimAction[] { new MoveAttackAction(FirstHit: 0, Hits: 1) },
        OnlyAttacks = true,
    };

    [Fact]
    public void AGiantAboutToBlowExplodesForItsSteamEruption()
    {
        // Decision #337: the move on screen stores the Steam Eruption it has then; the Explode after it reads it, where its intent read now says 0.
        var projection = SimEngine.Project(AboutToBlow(steam: 41), Turns(new[] { Certain(enemyId: 0, Explode()) }));
        var hits = projection.Of(turn: 0, enemyId: 0, move: "EXPLODE_MOVE")!;
        Assert.Null(hits.Unknown);
        Assert.Equal(new[] { 41 }, hits.AtYourTurnStart);
        Assert.Equal(new[] { 41 }, hits.Lands);
    }

    [Fact]
    public void AStoreThatAddsCountsEachTimeItsMoveIsMade()
    {
        // Pressure Gun's CurrentPressureGunDamage += 5 after its attack: each Pressure Gun hits 5 more than the last, from the value read now.
        const string pressure = "Monster.WaterfallGiant.CurrentPressureGunDamage";
        var gun = new ProjectedMove("PRESSURE_GUN_MOVE")
        {
            Intent = new[] { new IntentHit(Base: 17) { BaseFrom = pressure } },
            Actions = new SimAction[] { new MoveAttackAction(FirstHit: 0, Hits: 1), new MoveStoreAction(pressure, Amount: 5, Adds: true, Now: 17) },
            OnlyAttacks = true,
        };
        var projection = SimEngine.Project(Fight(Attacker(id: 0, hp: 200, hit: 0)), Turns(new[] { Certain(enemyId: 0, gun) }, new[] { Certain(enemyId: 0, gun) }));
        Assert.Equal(new[] { 17 }, projection.Of(turn: 0, enemyId: 0, move: "PRESSURE_GUN_MOVE")!.Lands);
        Assert.Equal(new[] { 22 }, projection.Of(turn: 1, enemyId: 0, move: "PRESSURE_GUN_MOVE")!.Lands);
    }

    [Fact]
    public void AStoreItCantReadLeavesTheProjectionUnknown()
    {
        var state = AboutToBlow(steam: 41);
        state.Enemies[0].Move = new SimAction[] { new MoveStoreAction(SteamEruptionDamage, Amount: 0, Unknown: "a value not read") };
        var hits = SimEngine.Project(state, Turns(new[] { Certain(enemyId: 0, Explode()) })).Of(turn: 0, enemyId: 0, move: "EXPLODE_MOVE")!;
        Assert.NotNull(hits.Unknown);
    }

    [Fact]
    public void AStoreDoesNothingOutsideAProjection()
    {
        // Only a projection has a later turn whose hit reads it: the turn's own simulation leaves the enemy's fields as they were.
        var branch = Assert.Single(SimEngine.Run(AboutToBlow(steam: 41)).Branches);
        Assert.False(branch.Enemy(id: 0)!.Fields.Has(SteamEruptionDamage));
    }

    [Fact]
    public void RandomHitsOnTheEnemiesDontSplitTheProjection()
    {
        // The orbs' Lightning and Juggernaut hit a random enemy at each of your turns' ends: which one changes only HP and block, which a projection
        // doesn't follow, so every target is one branch. The last turn's picks (three moves, then two: six branches, and the step making them) are
        // all it holds at once.
        var (state, turns) = BigBoard();
        var projection = SimEngine.Project(state, turns);
        Assert.True(projection.PeakHeld <= 12, $"{projection.PeakHeld} states held at once");
        Assert.True(projection.Copies < 200, $"{projection.Copies} state copies");
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
    public void AnotherEnemysRandomPickCountsOnlyWhatsCertain()
    {
        // The first enemy picks at random between giving the second 3 Strength and attacking; the second, acting after it, hits for 7 or 10: a roll
        // decides it, so only what's certain counts (its intent as your turn starts, 7), and it says why. The turn after, the Strength only one
        // outcome gave isn't counted either.
        var state = Fight(Attacker(id: 0, hp: 60, hit: 4), Attacker(id: 1, hp: 60, hit: 7));
        var shout = new ProjectedMove("SHOUT") { Actions = new SimAction[] { new MovePowerAction(PowerId.Strength, Amount: 3, To: new[] { 1 }, Debuff: false) } };
        var pick = new ProjectedChoice(EnemyId: 0, new[] { (shout, 0.5), (Attack("JAB", damage: 4), 0.5) });
        var projection = SimEngine.Project(state, Turns(new[] { pick, Certain(enemyId: 1, Attack("CLUB", damage: 7)) }, new[] { Certain(enemyId: 1, Attack("CLUB", damage: 7)) }));
        var club = projection.Of(turn: 0, enemyId: 1, move: "CLUB")!;
        Assert.Equal(new[] { 7 }, club.Lands);
        Assert.Equal(new[] { 7 }, club.AtYourTurnStart);
        Assert.NotNull(club.Uncertain);
        Assert.Null(club.Unknown);
        Assert.Equal(new[] { 7 }, projection.Of(turn: 1, enemyId: 1, move: "CLUB")!.Lands);
        // Each of the first enemy's own moves is known on its own branch.
        Assert.Equal(new[] { 4 }, projection.Of(turn: 0, enemyId: 0, move: "JAB")!.Lands);
        Assert.Empty(projection.Of(turn: 0, enemyId: 0, move: "SHOUT")!.Lands!);
        Assert.Null(projection.Of(turn: 0, enemyId: 0, move: "JAB")!.Uncertain);
    }

    [Fact]
    public void AnEnemyPastItsForecastMakesNoMoveTheProjectionCounts()
    {
        // The second enemy has no move given that turn (past its first random pick): what it would do isn't certain, so it isn't counted, and the
        // first enemy's later turn is known.
        var state = Fight(Attacker(id: 0, hp: 60, hit: 5), Attacker(id: 1, hp: 60, hit: 3));
        var projection = SimEngine.Project(state, Turns(new[] { Certain(enemyId: 0, Attack("JAB", damage: 5)) }, new[] { Certain(enemyId: 0, Attack("JAB", damage: 5)) }));
        Assert.Equal(new[] { 5 }, projection.Of(turn: 0, enemyId: 0, move: "JAB")!.Lands);
        Assert.Equal(new[] { 5 }, projection.Of(turn: 1, enemyId: 0, move: "JAB")!.Lands);
        Assert.Null(projection.Of(turn: 1, enemyId: 0, move: "JAB")!.Unknown);
    }

    [Fact]
    public void WhatTheEnemiesTurnEndCantFollowLeavesTheLaterTurnsUnknown()
    {
        // Shrink on it (ShrinkPower.AfterSideTurnEnd acts there, its AfterApplied not read: turn-end-gaps.json, decision #303), which changes its
        // attacks: the state after this turn is unknown, so the next turn is.
        var state = Fight(Attacker(id: 0, hp: 60, hit: 5));
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Shrink, amount: 3);
        var projection = SimEngine.Project(state, Turns(new[] { Certain(enemyId: 0, Attack("JAB", damage: 5)) }));
        var hits = projection.Of(turn: 0, enemyId: 0, move: "JAB")!;
        Assert.Null(hits.Lands);
        Assert.Null(hits.AtYourTurnStart);
        Assert.Contains(PowerId.Shrink.Name, hits.Unknown, StringComparison.Ordinal);
    }

    [Fact]
    public void DoomAtTheEnemiesTurnEndIsFollowedButKillsNoOneInAProjection()
    {
        // DoomPower.BeforeSideTurnEnd is read (decision #310), so the turn after it is projected; a projection works out no one's death (decision
        // #308), so the enemy its Doom would kill (3 HP under 5 Doom) still shows its next attack.
        var state = Fight(Attacker(id: 0, hp: 3, hit: 5));
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Doom, amount: 5);
        var hits = SimEngine.Project(state, Turns(new[] { Certain(enemyId: 0, Attack("JAB", damage: 5)) })).Of(turn: 0, enemyId: 0, move: "JAB")!;
        Assert.True(hits.Unknown == null, hits.Unknown);
        Assert.Equal(new[] { 5 }, hits.Lands);
    }

    [Fact]
    public void NoOnesHpOrDeathIsWorkedOut()
    {
        // Poison 20 on it with 10 HP would kill it as the enemies' turn starts; a projection doesn't work out whether an enemy lives (decision #308),
        // so its later moves show their hits. Nor yours: with 3 HP under its 50 a turn, every turn is still projected.
        var state = Fight(Attacker(id: 0, hp: 10, hit: 50));
        state.You.Hp = 3;
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Poison, amount: 20);
        var projection = SimEngine.Project(state, Turns(new[] { Certain(enemyId: 0, Attack("JAB", damage: 50)) }, new[] { Certain(enemyId: 0, Attack("JAB", damage: 50)) }));
        Assert.Equal(new[] { 50 }, projection.Of(turn: 0, enemyId: 0, move: "JAB")!.Lands);
        Assert.Equal(new[] { 50 }, projection.Of(turn: 1, enemyId: 0, move: "JAB")!.Lands);
    }

    // ------------------------------------------------------------------ the moves, rolled on the projected state (decision #309)

    /// <summary>
    /// A sleeper (decision #309): its move rules as the game builds them (WakeTabParityTests.SleeperRules: its sleeping move, then a branch on its
    /// power), its sleeping move on screen, and its moves read: sleeping does nothing, its first attack hits for 10.
    /// </summary>
    private static (SimState State, Dictionary<int, ProjectedRules> Rules) Sleeper(PowerId power, int stacks)
    {
        var rules = power == PowerId.Asleep
            ? WakeTabParityTests.SleeperRules("SLEEP_MOVE", "SLEEP_BRANCH", power, "SLASH_MOVE", new[] { ("SLASH_MOVE", "DISEMBOWEL_MOVE"), ("DISEMBOWEL_MOVE", "SLASH_MOVE") }, new HashSet<string> { "SLASH_MOVE" })
            : WakeTabParityTests.SleeperRules("SNORE_MOVE", "SNORE_NEXT", power, "ROLL_OUT_MOVE", new[] { ("ROLL_OUT_MOVE", "ROLL_OUT_MOVE") }, new HashSet<string> { "ROLL_OUT_MOVE" });
        var sleep = rules.Current;
        var moves = new Dictionary<string, ProjectedMove>
        {
            [sleep] = new(sleep) { Actions = Array.Empty<SimAction>() },
            ["SLASH_MOVE"] = Attack("SLASH_MOVE", damage: 10), ["DISEMBOWEL_MOVE"] = Attack("DISEMBOWEL_MOVE", damage: 4, hits: 2), ["ROLL_OUT_MOVE"] = Attack("ROLL_OUT_MOVE", damage: 10),
        };
        var state = Fight(new SimEnemy { Id = 0, Name = "E0", Hp = 60, Powers = PowerSet.Empty.With(power, stacks) });
        state.TurnNumber = state.RoundNumber = 2;
        var followUp = ((MoveNodeMove)rules.Graph.Nodes[sleep]).FollowUp;
        return (state, new Dictionary<int, ProjectedRules> { [0] = new(rules.Graph, sleep, followUp, rules.Log, moves) });
    }

    private static string Moves(Projection projection) =>
        string.Join(" | ", projection.ForecastOf(enemyId: 0)!.Select(turn => string.Join(", ", turn.Moves.Select(move => move.Chance < 1 ? $"{move.Move} {move.Chance:0.##}" : move.Move))));

    [Fact]
    public void ASleeperWakesAfterItsLastStack()
    {
        // SlumberPower.AfterSideTurnEnd: its stack down by one as the enemies' turn ends, gone at 0 (and WakeUpMove). Its move rules ask
        // HasPower<SlumberPower> as your next turn starts: on the projected state its last stack is gone, so it rolls ROLL_OUT_MOVE, where the game's
        // lambda as it answers now (it has Slumber) would keep it snoring.
        var (state, rules) = Sleeper(PowerId.Slumber, stacks: 1);
        var projection = SimEngine.Project(state, Array.Empty<IReadOnlyList<ProjectedChoice>>(), rules);
        Assert.Equal("ROLL_OUT_MOVE | ROLL_OUT_MOVE | ROLL_OUT_MOVE", Moves(projection));
        Assert.Equal(new[] { 10 }, projection.Of(turn: 0, enemyId: 0, move: "ROLL_OUT_MOVE")!.Lands);
        // Three stacks: two more turns asleep, then it wakes.
        (state, rules) = Sleeper(PowerId.Slumber, stacks: 3);
        Assert.Equal("SNORE_MOVE | SNORE_MOVE | ROLL_OUT_MOVE", Moves(SimEngine.Project(state, Array.Empty<IReadOnlyList<ProjectedChoice>>(), rules)));
    }

    [Fact]
    public void LagavulinWakesAfterItsLastStack()
    {
        // AsleepPower.AfterSideTurnEnd: the same for Lagavulin Matriarch, whose SLEEP_BRANCH asks HasPower<AsleepPower>: two stacks sleep one more
        // turn, then it slashes and goes on through its cycle.
        var (state, rules) = Sleeper(PowerId.Asleep, stacks: 2);
        var projection = SimEngine.Project(state, Array.Empty<IReadOnlyList<ProjectedChoice>>(), rules);
        Assert.Equal("SLEEP_MOVE | SLASH_MOVE | DISEMBOWEL_MOVE", Moves(projection));
        Assert.Equal(new[] { 4, 4 }, projection.Of(turn: 2, enemyId: 0, move: "DISEMBOWEL_MOVE")!.Lands);
    }

    [Fact]
    public void ARolledRandomPickEndsTheForecast()
    {
        // After its move, a random pick between two attacks: that turn shows both with their chances and its hits on each, and nothing after it.
        var graph = new MoveGraph(new Dictionary<string, MoveNode>
        {
            ["A"] = new MoveNodeMove("A", FollowUp: "PICK"),
            ["PICK"] = new MoveNodeRandom("PICK", new[] { new RandomBranch("B", MoveRepeat.CanRepeatForever, MaxTimes: 0, Cooldown: 0, Weight: 1), new RandomBranch("C", MoveRepeat.CanRepeatForever, MaxTimes: 0, Cooldown: 0, Weight: 1) }),
            ["B"] = new MoveNodeMove("B", FollowUp: "A"),
            ["C"] = new MoveNodeMove("C", FollowUp: "A"),
        }, InitialId: "A");
        var moves = new Dictionary<string, ProjectedMove> { ["A"] = Attack("A", damage: 3), ["B"] = Attack("B", damage: 5), ["C"] = Attack("C", damage: 8) };
        var state = Fight(Attacker(id: 0, hp: 60, hit: 3));
        var projection = SimEngine.Project(state, Array.Empty<IReadOnlyList<ProjectedChoice>>(), new Dictionary<int, ProjectedRules> { [0] = new(graph, "A", "PICK", new[] { "A" }, moves) });
        Assert.Equal("B 0.5, C 0.5", Moves(projection));
        Assert.Single(projection.Turns);
        Assert.Equal(new[] { 5 }, projection.Of(turn: 0, enemyId: 0, move: "B")!.Lands);
        Assert.Equal(new[] { 8 }, projection.Of(turn: 0, enemyId: 0, move: "C")!.Lands);
    }
}
