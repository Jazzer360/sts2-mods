using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Intent Forecast (decision #72): an enemy's next moves from its move rules, as MonsterMoveStateMachine picks them
/// (decompile v0.113.1: FindNextMoveState, RandomBranchState.GetStateWeight, ConditionalBranchState.GetNextState).
/// </summary>
public class IntentForecastTests
{
    private static MoveGraph Graph(string initial, params MoveNode[] nodes) => new(nodes.ToDictionary(moveNode => moveNode.Id), initial);
    private static MoveNodeMove Move(string id, string? next = null) => new(id, next);
    private static RandomBranch Br(string to, MoveRepeat repeat = MoveRepeat.CanRepeatForever, float weight = 1, int max = 0, int cooldown = 0) =>
        new(to, repeat, max, cooldown, weight);

    private static string Show(List<ForecastTurn> turns) =>
        string.Join(" | ", turns.Select(forecastTurn => string.Join(", ", forecastTurn.Moves.Select(move => move.Chance == 1 ? move.Move : $"{move.Move} {move.Chance:0.###}"))));

    [Fact]
    public void AFixedPatternIsCertainTurnAfterTurnUpToTheCap()
    {
        // A → B → C → A ...: every turn certain; three turns past the one on screen.
        var moveGraph = Graph("A", Move("A", "B"), Move("B", "C"), Move("C", "A"));
        var forecastTurns = IntentForecast.Forecast(moveGraph, Move("A", "B"), new[] { "A" });
        Assert.Equal("B | C | A", Show(forecastTurns));
        Assert.All(forecastTurns, forecastTurn => Assert.True(forecastTurn.Certain));
    }

    [Fact]
    public void AMoveWithNoFollowUpGoesBackToTheInitialState()
    {
        var moveGraph = Graph("A", Move("A", "B"), Move("B"));
        Assert.Equal("A | B | A", Show(IntentForecast.Forecast(moveGraph, Move("B"), new[] { "A", "B" })));
    }

    [Fact]
    public void ARandomPickListsEveryMoveWithItsChanceAndEndsTheForecast()
    {
        var moveGraph = Graph("R", Move("A", "R"), Move("B", "R"), Move("C", "R"),
            new MoveNodeRandom("R", new[] { Br("A", weight: 2), Br("B", weight: 1), Br("C", weight: 1) }));
        var forecastTurns = IntentForecast.Forecast(moveGraph, Move("A", "R"), new[] { "A" });
        Assert.Single(forecastTurns);
        Assert.Equal("A 0.5, B 0.25, C 0.25", Show(forecastTurns));
    }

    [Fact]
    public void CertainTurnsComeBeforeTheFirstRoll()
    {
        // A always leads into B, then a coin flip.
        var moveGraph = Graph("A", Move("A", "B"), Move("B", "R"), Move("C", "A"), Move("D", "A"),
            new MoveNodeRandom("R", new[] { Br("C"), Br("D") }));
        Assert.Equal("B | C 0.5, D 0.5", Show(IntentForecast.Forecast(moveGraph, Move("A", "B"), new[] { "A" })));
    }

    [Fact]
    public void CannotRepeatRulesOutTheMoveJustMade()
    {
        var moveGraph = Graph("R", Move("A", "R"), Move("B", "R"),
            new MoveNodeRandom("R", new[] { Br("A", MoveRepeat.CannotRepeat), Br("B", MoveRepeat.CannotRepeat) }));
        // A on screen: A can't come again, so B is certain; then B can't repeat, so A.
        Assert.Equal("B | A | B", Show(IntentForecast.Forecast(moveGraph, Move("A", "R"), new[] { "A" })));
    }

    [Fact]
    public void CanRepeatXTimesStopsAfterThatManyInARow()
    {
        var moveGraph = Graph("R", Move("A", "R"), Move("B", "R"),
            new MoveNodeRandom("R", new[] { Br("A", MoveRepeat.CanRepeatXTimes, max: 2), Br("B", MoveRepeat.CanRepeatXTimes, max: 2) }));
        // A twice in a row: A is out.
        Assert.Equal("B | A 0.5, B 0.5", Show(IntentForecast.Forecast(moveGraph, Move("A", "R"), new[] { "B", "A", "A" })));
        // A once: both possible.
        Assert.Equal("A 0.5, B 0.5", Show(IntentForecast.Forecast(moveGraph, Move("A", "R"), new[] { "B", "A" })));
    }

    [Fact]
    public void UseOnlyOnceIsGoneAfterItsFirstUse()
    {
        var moveGraph = Graph("R", Move("A", "R"), Move("Big", "R"),
            new MoveNodeRandom("R", new[] { Br("A"), Br("Big", MoveRepeat.UseOnlyOnce, weight: 3) }));
        Assert.Equal("Big 0.75, A 0.25", Show(IntentForecast.Forecast(moveGraph, Move("A", "R"), new[] { "A" })));
        Assert.Equal("A | A | A", Show(IntentForecast.Forecast(moveGraph, Move("A", "R"), new[] { "Big", "A" })));
    }

    [Fact]
    public void ACooldownRulesOutAMoveMadeInTheLastFewTurns()
    {
        var moveGraph = Graph("R", Move("A", "R"), Move("B", "R"), Move("C", "R"),
            new MoveNodeRandom("R", new[] { Br("A"), Br("B"), Br("C", cooldown: 2) }));
        Assert.Equal("A 0.5, B 0.5", Show(IntentForecast.Forecast(moveGraph, Move("A", "R"), new[] { "C", "A" })));
        Assert.Equal("A 0.333, B 0.333, C 0.333", Show(IntentForecast.Forecast(moveGraph, Move("A", "R"), new[] { "C", "B", "A" })));
    }

    [Fact]
    public void AConditionalBranchTakesTheFirstThatHolds()
    {
        var moveGraph = Graph("A", Move("A", "If"), Move("Charge", "A"), Move("Hit", "A"),
            new MoveNodeConditional("If", new (string, bool?)[] { ("Charge", false), ("Hit", true) }));
        Assert.Equal("Hit | A | Hit", Show(IntentForecast.Forecast(moveGraph, Move("A", "If"), new[] { "A" })));
    }

    [Fact]
    public void BranchesNestAndTheirChancesMultiply()
    {
        // A random pick between a move and another random pick.
        var moveGraph = Graph("R", Move("A", "R"), Move("B", "R"), Move("C", "R"),
            new MoveNodeRandom("R", new[] { Br("A"), Br("R2") }),
            new MoveNodeRandom("R2", new[] { Br("B"), Br("C") }));
        Assert.Equal("A 0.5, B 0.25, C 0.25", Show(IntentForecast.Forecast(moveGraph, Move("A", "R"), new[] { "A" })));
    }

    [Fact]
    public void AStunnedEnemyMakesTheMoveItLostNext()
    {
        // CreatureCmd.Stun: a STUNNED move (outside the graph) whose follow-up is the move it was going to make.
        var moveGraph = Graph("A", Move("A", "B"), Move("B", "A"));
        Assert.Equal("B | A | B", Show(IntentForecast.Forecast(moveGraph, Move("STUNNED", "B"), new[] { "A", "B" })));
    }

    [Fact]
    public void RulesWithNowhereToGoForecastNothing()
    {
        var noWeight = Graph("R", Move("A", "R"), new MoveNodeRandom("R", new[] { Br("A", MoveRepeat.UseOnlyOnce) }));
        Assert.Empty(IntentForecast.Forecast(noWeight, Move("A", "R"), new[] { "A" }));
        var noBranch = Graph("A", Move("A", "If"), new MoveNodeConditional("If", new (string, bool?)[] { ("A", false) }));
        Assert.Empty(IntentForecast.Forecast(noBranch, Move("A", "If"), new[] { "A" }));
        var missing = Graph("A", Move("A", "Nope"));
        Assert.Empty(IntentForecast.Forecast(missing, Move("A", "Nope"), new[] { "A" }));
    }

    [Fact]
    public void AnUnreadableWeightOrConditionMattersOnlyWhereItsReached()
    {
        // A weight that couldn't be read: no forecast past that pick, but a branch never reached doesn't matter.
        var moveGraph = Graph("A", Move("A", "B"), Move("B", "R"), Move("C", "A"),
            new MoveNodeRandom("R", new[] { Br("C"), Br("A", weight: float.NaN) }));
        Assert.Equal("B", Show(IntentForecast.Forecast(moveGraph, Move("A", "B"), new[] { "A" })));
        var cond = Graph("A", Move("A", "If"), Move("Hit", "A"), Move("Charge", "A"),
            new MoveNodeConditional("If", new (string, bool?)[] { ("Hit", true), ("Charge", null) }));
        Assert.Equal("Hit | A | Hit", Show(IntentForecast.Forecast(cond, Move("A", "If"), new[] { "A" })));
        var condFirst = Graph("A", Move("A", "If"), Move("Hit", "A"),
            new MoveNodeConditional("If", new (string, bool?)[] { ("Hit", null), ("A", true) }));
        Assert.Empty(IntentForecast.Forecast(condFirst, Move("A", "If"), new[] { "A" }));
    }
}
