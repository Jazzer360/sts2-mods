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
        var state = new SimState { Enemies = { Enemy(1, enemyHp, 10) } };
        state.You.HandCount = 5;
        state.You.Hp = 5;
        state.You.Powers = state.You.Powers.With(PowerId.Hellraiser, 1, 0);
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(new SimDrawCard("strike") { Strike = true }, 1), new SimDrawGroup(new SimDrawCard("plain"), 1) } };
        return state;
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
        var outcome = SimEngine.Run(Hellraiser(50), HitThenDraw(1));
        Assert.True(outcome.AnyUnmodelled);
        Assert.Contains("Hellraiser auto-plays a drawn Strike whose play isn't built", outcome.UnmodelledReasons);
        var chance = outcome.YouDieChance();
        Assert.Equal(0.5, chance.Low, 9);
        Assert.Equal(1.0, chance.High, 9);
        Assert.False(chance.Exact);
    }

    [Fact]
    public void ADeathThatHappenedStandsAnyway()
    {
        // The card's own hit kills it before the draw: certain, whatever the drawn Strike would have done.
        var outcome = SimEngine.Run(Hellraiser(5), HitThenDraw(9));
        Assert.True(outcome.AnyUnmodelled);
        Assert.True(outcome.DeadInAll(1));
        Assert.True(outcome.DeadChanceRange(1).Certain);
    }

    [Fact]
    public void AnEnemyStillAliveWhereItIsntModelledIsUnknown()
    {
        // 6 damage of 10 HP: alive where modelled, and unknown where the Strike would have been played: 0–50%.
        var outcome = SimEngine.Run(Hellraiser(10), HitThenDraw(6));
        var chance = outcome.DeadChanceRange(1);
        Assert.Equal(0, chance.Low, 9);
        Assert.Equal(0.5, chance.High, 9);
    }

    [Fact]
    public void FullyModelledStaysExact()
    {
        var state = Hellraiser(50);
        state.You.Powers = PowerSet.Empty;
        var outcome = SimEngine.Run(state, HitThenDraw(1));
        Assert.False(outcome.AnyUnmodelled);
        Assert.True(outcome.YouDieChance().Exact);
    }
}
