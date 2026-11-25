using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The Play audit's reason a power on an enemy went down with no tab (CardTabs.AlreadySetOff): the source's own outcome rule, given the turn ended
/// now, has none for it, where it happens anyway. It names no class: the stuns (Shriek and Flutter read from their code, Plow and Burrowed
/// hand-written) and Asleep's wake are their models' own rules, as the audit's names had them.
/// </summary>
public class AlreadySetOffTests
{
    private static (SimState Start, SimOutcome EndingNow) Board(PowerId power, bool stunned)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 30, StunnedThisTurn = stunned, Stunned = stunned } } };
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(power, 2);
        return (state, SimEngine.Run(state));
    }

    public static readonly TheoryData<string> Stuns = new() { PowerId.Shriek.Name, PowerId.Flutter.Name, PowerId.Plow.Name, PowerId.Burrowed.Name };

    [Theory]
    [MemberData(nameof(Stuns))]
    public void AStunTheTurnsEndAlreadyMakesIsSetOff(string power)
    {
        var model = SimModels.Get(power)!;
        var (start, stunned) = Board(new PowerId(power), stunned: true);
        Assert.True(CardTabs.AlreadySetOff(model, start, stunned, owner: 0));
        var (otherStart, awake) = Board(new PowerId(power), stunned: false);
        Assert.False(CardTabs.AlreadySetOff(model, otherStart, awake, owner: 0));
    }

    [Fact]
    public void AsleepUnwokenIsntSetOff()
    {
        var (start, endingNow) = Board(PowerId.Asleep, stunned: false);
        Assert.False(CardTabs.AlreadySetOff(SimModels.Get(PowerId.Asleep.Name)!, start, endingNow, owner: 0));
    }

    [Fact]
    public void ADeathWarningIsNeverSetOffByTheTurnsEnd()
    {
        // Crab Rage's rule is about a play that kills its owner: there's always one for an enemy.
        var (start, endingNow) = Board(PowerId.CrabRage, stunned: false);
        Assert.False(CardTabs.AlreadySetOff(SimModels.Get(PowerId.CrabRage.Name)!, start, endingNow, owner: 0));
    }
}
