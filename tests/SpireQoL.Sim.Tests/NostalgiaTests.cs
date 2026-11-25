using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Nostalgia read from its code (decision #290): ModifyCardPlayResultLocation's `(uint)(type - 1) > 1u` (neither an Attack nor a Skill) leaves the
/// location, `location.pileType != PileType.Discard` too, and the fight's log's count of its owner's card plays started this turn whose card is an
/// Attack or a Skill (one question, the compiler's `(uint)(type2 - 1) &lt;= 1u` on a flag: a count of each type, summed) against its amount; under it,
/// the card goes on top of its owner's draw pile, which is its tab.
/// </summary>
public class NostalgiaTests
{
    private static SimState Board(int amount, int attacksStarted, int skillsStarted)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 40 } } };
        state.You.Powers = state.You.Powers.With(PowerId.Nostalgia, amount);
        state.You.Turn = state.You.Turn.Add(SimKeys.Plays(PlayStage.Started, SimCardType.Attack), delta: attacksStarted)
            .Add(SimKeys.Plays(PlayStage.Started, SimCardType.Skill), delta: skillsStarted);
        return state;
    }

    private static SimResultLocation Where(SimState state, SimCardType type, SimResultLocation soFar = SimResultLocation.Default)
    {
        var model = SimModels.Get(PowerId.Nostalgia.Name);
        Assert.IsType<ReadModel>(model);
        var play = new SimCardPlay(new SimCard("Played", type), Who.You, 0, PlayIndex: 0, PlayCount: 1, AutoPlay: false);
        return model!.ModifyCardPlayResultLocation(state, Who.You, play, soFar);
    }

    [Fact]
    public void ItCountsTheAttacksAndSkillsStartedThisTurnTogether()
    {
        Assert.Equal(SimResultLocation.DrawTop, Where(Board(amount: 2, attacksStarted: 1, skillsStarted: 0), SimCardType.Skill));
        Assert.Equal(SimResultLocation.DrawTop, Where(Board(amount: 2, attacksStarted: 0, skillsStarted: 1), SimCardType.Attack));
        // One of each: 2, its amount reached.
        Assert.Equal(SimResultLocation.Default, Where(Board(amount: 2, attacksStarted: 1, skillsStarted: 1), SimCardType.Attack));
    }

    [Fact]
    public void APowerOrACardBoundElsewhereIsLeftWhereItGoes()
    {
        Assert.Equal(SimResultLocation.Default, Where(Board(amount: 2, attacksStarted: 0, skillsStarted: 0), SimCardType.Power));
        Assert.Equal(SimResultLocation.Exhaust, Where(Board(amount: 2, attacksStarted: 0, skillsStarted: 0), SimCardType.Skill, SimResultLocation.Exhaust));
    }
}
