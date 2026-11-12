using SpireQoL.Sim;
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
        var s = new SimState { Enemies = { E(1, 30, 10) } };
        s.P.ObsidianViceDamage = 1;
        s.P.HandCount = 4;
        var o = SimEngine.Run(s);
        Assert.Equal(26, o.Worst.Enemy(1)!.HpAtTurnEnd);
    }

    [Fact]
    public void ItCanKillBeforeTheEnemyActsAndThenItDoesntAttack()
    {
        var s = new SimState { Enemies = { E(1, 5, 12) } };
        s.P.ObsidianViceDamage = 1;
        s.P.HandCount = 6;
        var o = SimEngine.Run(s);
        Assert.True(o.DeadInAll(1));
        Assert.Equal(0, o.TakenMax);
    }

    [Fact]
    public void ARandomEnemyIsABranchNeverAGuarantee()
    {
        // Two enemies at 5 HP, 6 damage: each dies in one branch, neither in every branch; you take the
        // survivor's hit either way.
        var s = new SimState { Enemies = { E(1, 5, 7), E(2, 5, 9) } };
        s.P.ObsidianViceDamage = 1;
        s.P.HandCount = 6;
        var o = SimEngine.Run(s);
        Assert.False(o.DeadInAll(1));
        Assert.False(o.DeadInAll(2));
        Assert.True(o.DeadInAny(1));
        Assert.True(o.DeadInAny(2));
        Assert.Equal(9, o.TakenMax);
        Assert.Equal(7, o.TakenMin);
    }

    [Fact]
    public void AnEmptyHandDealsNothing()
    {
        var s = new SimState { Enemies = { E(1, 30, 10) } };
        s.P.ObsidianViceDamage = 1;
        s.P.HandCount = 0;
        Assert.Equal(30, SimEngine.Run(s).Worst.Enemy(1)!.HpAtTurnEnd);
    }

    [Fact]
    public void ThePlayChangesTheHandItCounts()
    {
        // A play that draws 2 (hand 3 → 3 - 1 + 2 = 4 at end of turn).
        var s = new SimState { Enemies = { E(1, 30, 10) } };
        s.P.ObsidianViceDamage = 1;
        s.P.HandCount = 3;
        var o = SimEngine.Run(s, new SimPlay { HandDelta = 1 });
        Assert.Equal(26, o.Worst.Enemy(1)!.HpAtTurnEnd);
    }
}
