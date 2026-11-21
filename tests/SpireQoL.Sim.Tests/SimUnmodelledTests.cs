using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Decision #92, rule 2: a branch with something the simulator doesn't model yet carries the reason and counts as unknown in
/// every chance (a range: it can go either way), so nothing shown is exact past what's modelled. An enemy's death that already
/// happened in it still counts: nothing later undoes it. The reasons are the gaps the audits count.
/// </summary>
public class SimUnmodelledTests
{
    private static SimEnemy Enemy(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    /// <summary>Draws one card from a pile of a Strike and a plain card, with Hellraiser: half the time a Strike it doesn't play.</summary>
    private static SimState Hellraiser(int enemyHp)
    {
        var s = new SimState { Enemies = { Enemy(1, enemyHp, 10) } };
        s.You.HandCount = 5;
        s.You.Hp = 5;
        s.You.Powers = s.You.Powers.With(PowerId.Hellraiser, 1, 0);
        s.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(new SimDrawCard("strike") { Strike = true }, 1), new SimDrawGroup(new SimDrawCard("plain"), 1) } };
        return s;
    }

    private static SimPlay HitThenDraw(int damage) => new()
    {
        Name = "PommelStrike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 1, HitDamage = { [1] = damage }, Hits = 1,
        HandCard = 7, HandDelta = 0, Draws = 1, Body = { new CardEventAction(CardEvent.Drawn, 1) },
    };

    [Fact]
    public void YourDeathIsARangeWhereABranchIsntModelled()
    {
        // The enemy hits 10 and you have 5: you die where it's modelled (half), and the Strike Hellraiser plays might have
        // killed it first (the other half; its play isn't built here): 50–100%, not 100%.
        var o = SimEngine.Run(Hellraiser(50), HitThenDraw(1));
        Assert.True(o.AnyUnmodelled);
        Assert.Contains("Hellraiser auto-plays a drawn Strike whose play isn't built", o.UnmodelledReasons);
        var c = o.YouDieChance();
        Assert.Equal(0.5, c.Low, 9);
        Assert.Equal(1.0, c.High, 9);
        Assert.False(c.Exact);
    }

    [Fact]
    public void ADeathThatHappenedStandsAnyway()
    {
        // The card's own hit kills it before the draw: certain, whatever the drawn Strike would have done.
        var o = SimEngine.Run(Hellraiser(5), HitThenDraw(9));
        Assert.True(o.AnyUnmodelled);
        Assert.True(o.DeadInAll(1));
        Assert.True(o.DeadChanceRange(1).Certain);
    }

    [Fact]
    public void AnEnemyStillAliveWhereItIsntModelledIsUnknown()
    {
        // 6 damage of 10 HP: alive where modelled, and unknown where the Strike would have been played: 0–50%.
        var o = SimEngine.Run(Hellraiser(10), HitThenDraw(6));
        var c = o.DeadChanceRange(1);
        Assert.Equal(0, c.Low, 9);
        Assert.Equal(0.5, c.High, 9);
    }

    [Fact]
    public void FullyModelledStaysExact()
    {
        var s = Hellraiser(50);
        s.You.Powers = PowerSet.Empty;
        var o = SimEngine.Run(s, HitThenDraw(1));
        Assert.False(o.AnyUnmodelled);
        Assert.True(o.YouDieChance().Exact);
    }
}
