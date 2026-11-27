using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Decision #412: a card's own result location (its GetResultLocationForCardPlay, read by the adapter into CardPlayAction.OwnResult), from the base's:
/// Mourning Blow back into your hand where it would be discarded; a base location its code's ways can't say leaves the play unknown.
/// </summary>
public class OwnResultLocationTests
{
    private static SimState Board()
    {
        var state = WithBystander(new SimState { Enemies = { Enemy(id: 0, hp: 40) } });
        state.You.HandCount = 2;
        return state;
    }

    private static SimScript Blow(IReadOnlyDictionary<SimResultLocation, SimResultLocation>? own, string? unknown = null) => new()
    {
        Name = "MourningBlow",
        Actions =
        {
            new CardPlayAction(new SimCardPlay(Card("MourningBlow"), Who.You, Target: 0, PlayIndex: 0, PlayCount: 1, AutoPlay: false), new SimAction[] { Attack(baseDamage: 10) })
                { OwnResult = own, OwnResultUnknown = unknown },
        },
    };

    [Fact]
    public void ACardWhoseCodeSendsItBackGoesBackIntoYourHand()
    {
        var after = Assert.Single(After(Board(), Blow(new Dictionary<SimResultLocation, SimResultLocation> { [SimResultLocation.Default] = SimResultLocation.HandTop })));
        Assert.Equal(3, after.You.HandCount);
        Assert.Null(after.Unmodelled);
        // Without its own location, it's discarded: the hand as it was (the card's leaving is the card play's end, not in this script).
        Assert.Equal(2, Assert.Single(After(Board(), Blow(own: null))).You.HandCount);
    }

    [Fact]
    public void ABaseLocationItsCodeCantSayIsUnknown()
    {
        var after = Assert.Single(After(Board(), Blow(new Dictionary<SimResultLocation, SimResultLocation>(), unknown: "another player's pile")));
        Assert.Equal("another player's pile", after.Unmodelled);
    }
}
