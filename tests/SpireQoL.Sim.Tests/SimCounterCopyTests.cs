using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Counter powers whose every application is a copy of its own (PowerInstanceType.Instanced), read from their code (decision #196): each copy
/// counts on its own in its own state (your fields: "Name@id" its amount, "Name@id.Data.energySpent" its count) and gives its own amount, and
/// starts over after it gets there. A report: two Orbits, one with 1 energy to go, were read as one count, so Eternal Armor's energy back
/// wasn't predicted.
/// </summary>
public class SimCounterCopyTests
{
    /// <summary>Copies of a power on you, each with its amount and its own state, as the adapter lists them.</summary>
    private static void Copies(SimState state, PowerId power, params (int Amount, (string Field, int Value)[] Fields)[] copies)
    {
        state.You.Powers = state.You.Powers.With(power, copies.Sum(copy => copy.Amount));
        for (var copy = 1; copy <= copies.Length; copy++)
        {
            state.You.Fields = state.You.Fields.With($"{power.Name}@{copy}", copies[copy - 1].Amount);
            foreach (var (field, value) in copies[copy - 1].Fields) state.You.Fields = state.You.Fields.With($"{power.Name}@{copy}.{field}", value);
        }
    }

    /// <summary>An Orbit with this much energy to go before it gives its amount (OrbitPower: 4 - energySpent % 4), none given yet.</summary>
    private static (int Amount, (string Field, int Value)[] Fields) Orbit(int toGo, int amount = 1) => (amount, new[] { ("Data.energySpent", 4 - toGo), ("Data.triggerCount", 0) });

    /// <summary>A Panache with this many cards to go (its CardsLeft), counting since it landed.</summary>
    private static (int Amount, (string Field, int Value)[] Fields) Panache(int toGo, int amount) => (amount, new[] { ("Var.CardsLeft", toGo), ("Data.alreadyApplied", 1) });

    /// <summary>Each copy's count left: Orbit's energy to go, read back from its state.</summary>
    private static IEnumerable<int> OrbitsToGo(SimState state) =>
        state.You.Fields.Items.Where(entry => entry.Name.StartsWith(PowerId.Orbit.Name + "@", StringComparison.Ordinal) && entry.Name.EndsWith(".Data.energySpent", StringComparison.Ordinal)).Select(entry => 4 - entry.Amount % 4);

    /// <summary>With as much energy as the play spends, so what's left after it is what the Orbits gave back.</summary>
    private static SimState State(int energy, params (int Amount, (string Field, int Value)[] Fields)[] orbits)
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 100) } };
        state.You.Energy = energy;
        Copies(state, PowerId.Orbit, orbits);
        return state;
    }

    private static SimScript Spending(int energy, params SimAction[] more) =>
        Play(Card("EternalArmor", SimCardType.Skill), target: Who.None, plays: 1, new SimAction[] { new CardEventAction(CardEvent.EnergySpent, Count: energy) }.Concat(more).ToArray());

    private static SimState After(SimState state, SimScript script) => Assert.Single(ScriptBuilder.After(state, script));

    [Fact]
    public void EachOrbitCountsOnItsOwn()
    {
        // One Orbit with 1 energy to go, one with 4: spending 3 sets off the first only (1 energy), as in the report.
        var after = After(State(energy: 3, Orbit(toGo: 1), Orbit(toGo: 4)), Spending(energy: 3));
        Assert.Equal(1, after.You.Energy);
        Assert.Equal(new[] { 2, 1 }, OrbitsToGo(after));
    }

    [Fact]
    public void ACopyStartsOverAndCanGetThereTwiceInOnePlay()
    {
        // 2 to go, then 6 spent: there at the 2nd and again at the 6th (each 4), 2 energy.
        var after = After(State(energy: 6, Orbit(toGo: 2)), Spending(energy: 6));
        Assert.Equal(2, after.You.Energy);
        Assert.Equal(4, Assert.Single(OrbitsToGo(after)));
    }

    [Fact]
    public void AnOrbitPlayedNowIsANewCopyFromTheStart()
    {
        // A second Orbit applied during the play: its own count from 4 (a new instance's data), beside the first's 1 to go.
        var state = State(energy: 0, Orbit(toGo: 1));
        var after = After(state, Spending(energy: 0, new ApplyPowerAction(PowerId.Orbit, Amount: 1) { To = ApplyPowerAction.Where.You }));
        var orbit = Assert.IsType<ReadModel>(SimModels.Get(PowerId.Orbit.Name));
        Assert.Equal(new[] { 1, 2 }, orbit.CopiesOrOne(after, Who.You));
        Assert.Equal(3, after.You.Fields[orbit.CopyFieldKey(1, "Data.energySpent")]);
        Assert.False(after.You.Fields.Has(orbit.CopyFieldKey(2, "Data.energySpent")));
        Assert.Equal(2, after.You.Powers[PowerId.Orbit]);
    }

    [Fact]
    public void ACopyOnItsOwnIsThePowersEntry()
    {
        // No copies listed (a state built by hand): the entry is the one copy, its state a new instance's (nothing spent yet).
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 100) } };
        state.You.Energy = 5;
        state.You.Powers = PowerSet.Empty.With(PowerId.Orbit, amount: 2);
        var after = After(state, Spending(energy: 5));
        Assert.Equal(2, after.You.Energy); // there at the 4th: 2
        Assert.Equal(3, Assert.Single(OrbitsToGo(after)));
    }

    [Fact]
    public void PanacheCopiesDealTheirOwnDamage()
    {
        // A Panache with 1 card to go (10) and an upgraded one with 3 (14): the next card sets off the first only.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 100) } };
        Copies(state, PowerId.Panache, Panache(toGo: 1, amount: 10), Panache(toGo: 3, amount: 14));
        var after = After(state, Play(Card("Defend", SimCardType.Skill), target: Who.None, plays: 1));
        Assert.Equal(90, after.Enemies[0].Hp);
        var panache = Assert.IsType<ReadModel>(SimModels.Get(PowerId.Panache.Name));
        Assert.Equal(new[] { 5, 2 }, new[] { 1, 2 }.Select(copy => after.You.Fields[panache.CopyFieldKey(copy, "Var.CardsLeft")]));
    }
}
