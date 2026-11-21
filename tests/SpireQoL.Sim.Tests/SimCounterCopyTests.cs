using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Counter powers whose every application is a copy of its own (PowerInstanceType.Instanced): each copy counts on its own and gives
/// its own amount (SimPlayer.Counters, CounterPower), and starts over after it gets there. A report: two Orbits, one with 1 energy
/// to go, were read as one count, so Eternal Armor's energy back wasn't predicted.
/// </summary>
public class SimCounterCopyTests
{
    /// <summary>With as much energy as the play spends, so what's left after it is what the Orbits gave back.</summary>
    private static SimState State(int energy, params CounterCopy[] copies)
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 100) } };
        state.You.Energy = energy;
        state.You.Powers = PowerSet.Empty.With(PowerId.Orbit, copies.Sum(copy => copy.Amount), data: copies[0].Left);
        state.You.Counters = copies.ToList();
        return state;
    }

    private static SimScript Spending(int energy, params SimAction[] more) =>
        Play(Card("EternalArmor", SimCardType.Skill), target: Who.None, plays: 1, new SimAction[] { new CardEventAction(CardEvent.EnergySpent, Count: energy) }.Concat(more).ToArray());

    private static SimState After(SimState state, SimScript script) => Assert.Single(ScriptBuilder.After(state, script));

    [Fact]
    public void EachOrbitCountsOnItsOwn()
    {
        // One Orbit with 1 energy to go, one with 4: spending 3 sets off the first only (1 energy), as in the report.
        var after = After(State(energy: 3, new CounterCopy(PowerId.Orbit, Left: 1, Amount: 1), new CounterCopy(PowerId.Orbit, Left: 4, Amount: 1)), Spending(energy: 3));
        Assert.Equal(1, after.You.Energy);
        Assert.Equal(new[] { 2, 1 }, after.You.Counters.Select(copy => copy.Left));
    }

    [Fact]
    public void ACopyStartsOverAndCanGetThereTwiceInOnePlay()
    {
        // 2 to go, then 6 spent: there at the 2nd and again at the 6th (each 4), 2 energy.
        var after = After(State(energy: 6, new CounterCopy(PowerId.Orbit, Left: 2, Amount: 1)), Spending(energy: 6));
        Assert.Equal(2, after.You.Energy);
        Assert.Equal(4, Assert.Single(after.You.Counters).Left);
    }

    [Fact]
    public void AnOrbitPlayedNowIsANewCopyFromTheStart()
    {
        // A second Orbit applied during the play: its own count from 4, beside the first's 1 to go.
        var state = State(energy: 0, new CounterCopy(PowerId.Orbit, Left: 1, Amount: 1));
        var after = After(state, Spending(energy: 0, new ApplyPowerAction(PowerId.Orbit, Amount: 1) { To = ApplyPowerAction.Where.You }));
        Assert.Equal(new[] { (1, 1), (4, 1) }, after.You.Counters.Select(copy => (copy.Left, copy.Amount)));
        Assert.Equal(2, after.You.Powers[PowerId.Orbit]);
    }

    [Fact]
    public void ACopyOnItsOwnIsThePowersEntry()
    {
        // Nothing in Counters (a state built by hand): the entry is the one copy, and it starts over too.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 100) } };
        state.You.Energy = 5;
        state.You.Powers = PowerSet.Empty.With(PowerId.Orbit, amount: 2, data: 1);
        var after = After(state, Spending(energy: 5));
        Assert.Equal(4, after.You.Energy); // there at the 1st and the 5th: 2 each
        Assert.Equal(4, after.You.Powers.DataOf(PowerId.Orbit));
    }

    [Fact]
    public void PanacheCopiesDealTheirOwnDamage()
    {
        // A Panache with 1 card to go (10) and an upgraded one with 3 (14): the next card sets off the first only.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 100) } };
        state.You.Powers = PowerSet.Empty.With(PowerId.Panache, amount: 24, data: 1);
        state.You.Counters = new() { new CounterCopy(PowerId.Panache, Left: 1, Amount: 10), new CounterCopy(PowerId.Panache, Left: 3, Amount: 14) };
        var after = After(state, Play(Card("Defend", SimCardType.Skill), target: Who.None, plays: 1));
        Assert.Equal(90, after.Enemies[0].Hp);
        Assert.Equal(new[] { 5, 2 }, after.You.Counters.Select(copy => copy.Left));
    }
}
