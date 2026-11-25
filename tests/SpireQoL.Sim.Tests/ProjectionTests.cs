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
