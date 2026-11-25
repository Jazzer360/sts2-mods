using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A test the board decides for a card's first play only (decision #311: Memento Mori's any enemy hittable, read before its attack): its
/// actions run on the first play, and a replay's later plays run what stands in for them there (unknown, with the board's answer).
/// </summary>
public class FirstPlayOnlyTests
{
    private static AttackAction Hit(int damage) =>
        new(1, SimPlay.Target.Single, new DamageSpec(Base: 0m, SimProps.Move) { BaseOn = new Dictionary<int, decimal> { [0] = damage }, Fixed = true });

    private static SimState Board()
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 50 } } };
        state.You.HandCount = 4;
        return state;
    }

    private static SimPlay Replayed(params SimAction[] body)
    {
        var play = new SimPlay { Name = "MementoMori", TargetId = 0, HandCard = 0, HandDelta = 0 };
        play.Body.AddRange(body);
        play.Card = new SimCardPlay(new SimCard("MementoMori", SimCardType.Attack) { Energy = 0 }, Who.You, Target: 0, PlayIndex: 0, PlayCount: 2, AutoPlay: false);
        return play;
    }

    [Fact]
    public void TheFirstPlayRunsTheBoardsAnswerAndALaterPlayItsOwn()
    {
        var play = Replayed(new FirstPlayOnly(new SimAction[] { Hit(damage: 5) }) { LaterPlays = new SimAction[] { new NotModelledAction("decided on the first play's board"), Hit(damage: 5) } });
        SimEngine.RunScriptTo(Board(), SimScript.Of(play), out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(40, branch.Enemies[0].Hp);
        Assert.Equal("decided on the first play's board", branch.Unmodelled);
    }

    [Fact]
    public void WithNoLaterPlaysALaterPlayRunsNothingInItsPlace()
    {
        var play = Replayed(new FirstPlayOnly(new SimAction[] { Hit(damage: 5) }));
        SimEngine.RunScriptTo(Board(), SimScript.Of(play), out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(45, branch.Enemies[0].Hp);
        Assert.Null(branch.Unmodelled);
    }
}
