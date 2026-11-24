using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A monster's conditional move branches as simulator conditions (decision #195): its lambdas' tests of itself (its HP against a breakpoint,
/// a power it has, its side's living creatures) asked of the simulated state as that monster, and a flag the simulator doesn't follow as it
/// was read (KnownCondition). IntentForecast.NextOn picks the move after a move from them, as MonsterMoveStateMachine does.
/// </summary>
public class SimMonsterConditionTests
{
    private static SimState Fight(int frogHp = 30, bool allyAlive = true) => new()
    {
        Enemies = { new SimEnemy { Id = 0, Name = "FrogKnight", Hp = frogHp }, new SimEnemy { Id = 1, Name = "Ally", Hp = allyAlive ? 10 : 0 } },
    };

    [Fact]
    public void AnHpBreakpointIsTheMonstersOwnHp()
    {
        // Frog Knight: CurrentHp < MaxHp / 2 (its max HP 50 read as 25).
        var belowHalf = new CountCondition(SimCounted.MonsterHp, SimCompare.Less, Than: 25);
        Assert.False(SimEngine.Holds(Fight(frogHp: 30), belowHalf, monster: 0));
        Assert.True(SimEngine.Holds(Fight(frogHp: 20), belowHalf, monster: 0));
        Assert.True(SimEngine.Holds(Fight(frogHp: 30), belowHalf, monster: 1)); // asked as the other enemy: its own 10 HP
        Assert.True(SimEngine.Holds(Fight(frogHp: 30, allyAlive: false), belowHalf, monster: 1)); // a dead monster's HP is 0: below too
    }

    [Fact]
    public void APowerItHasIsItsOwn()
    {
        var state = Fight();
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Asleep, amount: 3);
        var asleep = new CountCondition(SimCounted.MonsterPower, SimCompare.Greater, Than: 0) { Power = PowerId.Asleep };
        Assert.True(SimEngine.Holds(state, asleep, monster: 0));
        Assert.False(SimEngine.Holds(state, asleep, monster: 1));
    }

    [Fact]
    public void ItsSideCountsTheLivingWithOrWithoutItself()
    {
        // Living Shield: allies but itself, alive; Fabricator: its whole side alive, itself included.
        var allies = new CountCondition(SimCounted.MonsterSideAlive, SimCompare.Greater, Than: 0) { ExcludingItself = true };
        Assert.True(SimEngine.Holds(Fight(allyAlive: true), allies, monster: 0));
        Assert.False(SimEngine.Holds(Fight(allyAlive: false), allies, monster: 0));
        var room = new CountCondition(SimCounted.MonsterSideAlive, SimCompare.Less, Than: 2);
        Assert.True(SimEngine.Holds(Fight(allyAlive: false), room, monster: 0));
    }

    [Fact]
    public void AKnownTestIsWhatItWasRead()
    {
        Assert.True(SimEngine.Holds(Fight(), new KnownCondition(Holds: true), monster: 0));
        Assert.False(SimEngine.Holds(Fight(), new NotCondition(new KnownCondition(Holds: true)), monster: 0));
    }

    [Fact]
    public void TheMoveAfterAMoveComesFromTheSimulatedState()
    {
        // Frog Knight's rules, in short: after its Strike, Tongue Lash while it's at half HP or more (or has charged), else Beetle Charge.
        var halfOrMore = new AnyCondition(new SimCondition[] { new CountCondition(SimCounted.MonsterHp, SimCompare.GreaterOrEqual, Than: 25), new KnownCondition(Holds: false) });
        var graph = new MoveGraph(new Dictionary<string, MoveNode>
        {
            ["STRIKE"] = new MoveNodeMove("STRIKE", FollowUp: "BRANCH"),
            ["BRANCH"] = new MoveNodeConditional("BRANCH", new[] { ("LASH", (bool?)null), ("CHARGE", (bool?)null) })
                { Conditions = new SimCondition?[] { halfOrMore, new NotCondition(halfOrMore) } },
            ["LASH"] = new MoveNodeMove("LASH", FollowUp: "STRIKE"),
            ["CHARGE"] = new MoveNodeMove("CHARGE", FollowUp: "STRIKE"),
        }, InitialId: "STRIKE");
        Assert.Equal("LASH", Assert.Single(IntentForecast.NextOn(graph, moveId: "STRIKE", new[] { "STRIKE" }, Fight(frogHp: 30), enemyId: 0)!).Move);
        Assert.Equal("CHARGE", Assert.Single(IntentForecast.NextOn(graph, moveId: "STRIKE", new[] { "STRIKE" }, Fight(frogHp: 20), enemyId: 0)!).Move);
        // A branch with no condition read: the rules can't say.
        var unread = graph with { Nodes = new Dictionary<string, MoveNode>(graph.Nodes) { ["BRANCH"] = new MoveNodeConditional("BRANCH", new[] { ("LASH", (bool?)true) }) } };
        Assert.Null(IntentForecast.NextOn(unread, moveId: "STRIKE", new[] { "STRIKE" }, Fight(), enemyId: 0));
    }
}
