using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Plays still resolving or queued run first (decision #27): a card's predictions describe the state
/// after everything before it in the queue has resolved, and a queued play's random outcomes are
/// branches like any other, so a card is lethal only if it is in every branch.
/// </summary>
public class SimLeadInTests
{
    private static SimEnemy Enemy(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    private static SimPlay Strike(int target, int damage) =>
        new() { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = target, Hits = 1, HitDamage = { [target] = damage } };

    [Fact]
    public void AQueuedPlaysKillIsntTheCardsKill()
    {
        // A queued Strike kills E1 (6 HP); the card's own Strike on E2 (6 HP) kills only E2.
        var s = new SimState { Enemies = { Enemy(1, 6, 5), Enemy(2, 6, 5) } };
        var o = SimEngine.Run(s, new[] { Strike(1, 6) }, Strike(2, 6));
        Assert.Equal(new HashSet<int> { 2 }, o.KilledByPlayInAll);
        Assert.Equal(0, o.TakenMax); // both dead before acting
    }

    [Fact]
    public void ACardAfterAQueuedRandomPlayIsLethalOnlyInEveryBranch()
    {
        // A queued Bouncing Flask: 3 poison on a random enemy (E1 at 3 HP, E2 at 10). The card hits E1
        // for 1, leaving it at 2: it dies to the poison tick only in the branches where the poison landed
        // on it, so it isn't certainly dead, and the card's own hit kills it in no branch.
        var s = new SimState { Enemies = { Enemy(1, 3, 5), Enemy(2, 10, 5) } };
        var flask = new SimPlay { Name = "BouncingFlask", RandomPoison = 3, RandomPoisonTimes = 1 };
        var o = SimEngine.Run(s, new[] { flask }, Strike(1, 1));
        Assert.Empty(o.KilledByPlayInAll);
        Assert.False(o.DeadInAll(1)); // the poison landed on E2 in half the branches: E1 survives at 2
        Assert.True(o.DeadInAny(1));
        Assert.Equal(0.5, o.DeadChance(1), 6);
    }

    [Fact]
    public void TheCardsBlockGainsAndBlockBreaksCountFromAfterTheLeadIn()
    {
        // The queued play breaks E1's block; the card breaks nothing and gains block once.
        var s = new SimState { Enemies = { Enemy(1, 30, 5) } };
        s.Enemies[0].Block = 4;
        var queued = Strike(1, 5);
        queued.BlockGains = 1;
        queued.BlockAmount = 3;
        var card = new SimPlay { Name = "Defend", BlockGains = 1, BlockAmount = 5 };
        var o = SimEngine.Run(s, new[] { queued }, card);
        Assert.False(o.PlayBreaksBlockInEveryBranch);
        Assert.Equal(1, o.PlayBlockGainsMax);
        Assert.Equal(0, o.TakenMax); // 3 + 5 block against 5
    }

    [Fact]
    public void NoLeadInIsTheSameAsBefore()
    {
        var s = new SimState { Enemies = { Enemy(1, 6, 5), Enemy(2, 9, 5) } };
        var a = SimEngine.Run(s, Strike(1, 6));
        var b = SimEngine.Run(s, Array.Empty<SimPlay>(), Strike(1, 6));
        Assert.Equal(a.KilledByPlayInAll, b.KilledByPlayInAll);
        Assert.Equal(a.TakenMax, b.TakenMax);
        Assert.Equal(a.Branches.Count, b.Branches.Count);
    }
}
