using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The branch key (SimState.Key, decision #107) is how branches merge: two states with one key are one branch. Its numbers are mixed
/// one by one before they're combined (decision #197), so a difference anywhere reaches every bit of the key: one in a number's high
/// bits, a list one entry shorter, two numbers whose differences would cancel when combined plainly.
/// </summary>
public class SimKeyMixTests
{
    private static long KeyOf(params long[] numbers)
    {
        var key = new BranchKey();
        foreach (var number in numbers) key.Add(number);
        return key.Value;
    }

    [Fact]
    public void ADifferenceInTwoNumbersHighBitsDoesntCancel()
    {
        // Combined plainly (FNV-1a over whole numbers), the top bit flipped in two numbers in a row cancels: x * odd keeps it the top bit.
        const long top = long.MinValue;
        Assert.NotEqual(KeyOf(5, 9, 3), KeyOf(5 ^ top, 9 ^ top, 3));
        Assert.NotEqual(KeyOf(1L << 40, 0), KeyOf(0, 1L << 40));
    }

    [Fact]
    public void AListOneEntryShorterHasAnotherKey()
    {
        Assert.NotEqual(KeyOf(2, 5, 5, 7), KeyOf(1, 5, 7));
        Assert.NotEqual(KeyOf(2, 5, 5), KeyOf(1, 5));
    }

    [Fact]
    public void AnEnemyWithOneHitLessHasAnotherKey()
    {
        var state = new SimState();
        state.Enemies.Add(new SimEnemy { Id = 0, Name = "E0", Hp = 20, Hits = new() { 6, 6 } });
        var shorter = state.Clone();
        shorter.Enemies[0] = shorter.Enemies[0].Clone();
        shorter.Enemies[0].Hits = new() { 6 };
        Assert.NotEqual(state.Key(), shorter.Key());
    }

    /// <summary>
    /// The two branches SimMergeTests merged when one number less was hashed: two enemies' HP past 0 (a negative number differs from
    /// a positive one in every high bit), which combined plainly cancel.
    /// </summary>
    [Fact]
    public void TwoBranchesWithHpPastZeroInAnotherPlaceDontMerge()
    {
        SimState Board(int hp1, int hp2)
        {
            var state = new SimState();
            state.Enemies.Add(new SimEnemy { Id = 0, Name = "E0", Hp = 6, UnblockedInstances = 1 });
            state.Enemies.Add(new SimEnemy { Id = 1, Name = "E1", Hp = hp1, UnblockedInstances = 2 });
            state.Enemies.Add(new SimEnemy { Id = 2, Name = "E2", Hp = hp2, UnblockedInstances = 1, BlockBroken = true });
            state.Enemies.Add(new SimEnemy { Id = 3, Name = "E3", Hp = 11, Block = 2 });
            return state;
        }
        Assert.NotEqual(Board(hp1: -1, hp2: 0).Key(), Board(hp1: 1, hp2: -2).Key());
    }

    /// <summary>
    /// Every spread of HP (some past 0, as a killing hit leaves it) and block over four enemies (each its own state) has its own key. Combined plainly, small numbers in
    /// neighbouring places cancel: SimMergeTests merged two different spreads of HP when one number less was hashed.
    /// </summary>
    [Fact]
    public void EverySpreadOfHpAndBlockOverFourEnemiesHasItsOwnKey()
    {
        var keys = new HashSet<long>();
        var states = 0;
        for (var hp0 = -4; hp0 < 8; hp0++)
            for (var hp1 = -4; hp1 < 8; hp1++)
                for (var hp2 = -4; hp2 < 8; hp2++)
                    for (var hp3 = -4; hp3 < 8; hp3++)
                        for (var block = 0; block < 4; block++)
                        {
                            var state = new SimState();
                            var hp = new[] { hp0, hp1, hp2, hp3 };
                            for (var id = 0; id < 4; id++) state.Enemies.Add(new SimEnemy { Id = id, Name = $"E{id}", Hp = hp[id], Block = id == block ? 3 : 0 });
                            keys.Add(state.Key());
                            states++;
                        }
        Assert.Equal(states, keys.Count);
    }
}
