using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Obsidian Vice (game v0.113.0, ObsidianVice.BeforeSideTurnEnd): its damage per card in your hand, as one hit on
/// a random hittable enemy, at the start of your end of turn. Nothing with an empty hand.
/// </summary>
public class SimObsidianViceTests
{
    private static SimEnemy E(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    [Fact]
    public void OneHitOfDamageTimesCardsInHand()
    {
        var state = new SimState { Enemies = { E(1, 30, 10) } };
        state.You.ObsidianViceDamage = 1;
        state.You.HandCount = 4;
        var outcome = SimEngine.Run(state);
        Assert.Equal(26, outcome.Worst.Enemy(1)!.HpAtTurnEnd);
    }

    [Fact]
    public void ItCanKillBeforeTheEnemyActsAndThenItDoesntAttack()
    {
        var state = new SimState { Enemies = { E(1, 5, 12) } };
        state.You.ObsidianViceDamage = 1;
        state.You.HandCount = 6;
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.DeadInAll(1));
        Assert.Equal(0, outcome.TakenMax);
    }

    [Fact]
    public void ARandomEnemyIsABranchNeverAGuarantee()
    {
        // Two enemies at 5 HP, 6 damage: each dies in one branch, neither in every branch; you take the
        // survivor's hit either way.
        var state = new SimState { Enemies = { E(1, 5, 7), E(2, 5, 9) } };
        state.You.ObsidianViceDamage = 1;
        state.You.HandCount = 6;
        var outcome = SimEngine.Run(state);
        Assert.False(outcome.DeadInAll(1));
        Assert.False(outcome.DeadInAll(2));
        Assert.True(outcome.DeadInAny(1));
        Assert.True(outcome.DeadInAny(2));
        Assert.Equal(9, outcome.TakenMax);
        Assert.Equal(7, outcome.TakenMin);
    }

    [Fact]
    public void AnEmptyHandDealsNothing()
    {
        var state = new SimState { Enemies = { E(1, 30, 10) } };
        state.You.ObsidianViceDamage = 1;
        state.You.HandCount = 0;
        Assert.Equal(30, SimEngine.Run(state).Worst.Enemy(1)!.HpAtTurnEnd);
    }

    [Fact]
    public void ThePlayChangesTheHandItCounts()
    {
        // A play that draws 2 (hand 3 → 3 - 1 + 2 = 4 at end of turn).
        var state = new SimState { Enemies = { E(1, 30, 10) } };
        state.You.ObsidianViceDamage = 1;
        state.You.HandCount = 3;
        var outcome = SimEngine.Run(state, new SimPlay { HandDelta = 1 });
        Assert.Equal(26, outcome.Worst.Enemy(1)!.HpAtTurnEnd);
    }
}
