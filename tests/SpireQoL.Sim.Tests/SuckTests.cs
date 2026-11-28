using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// SuckPower (decompile v0.114.0, Fossil Stalker's): AfterAttack of its owner's own powered attack on the other side gives it Strength of its amount for
/// each of the attack's hits whose results (over every player it hit) got HP through on anyone, a hit on a pet leaving out its owner's result. From the
/// owner's Fool run: Lash's two hits got through, Fossil Stalker gained 6 Strength, and its next Latch hit for 18 where 12 was projected.
/// </summary>
public class SuckTests
{
    private static SimState Stalker(int suck, params int[] hits)
    {
        var state = new SimState();
        state.You.Hp = 60;
        state.Enemies.Add(new SimEnemy
        {
            Id = 0, Name = "FossilStalker", Hp = 50, Hits = hits.ToList(), Intent = hits.Select(hit => new IntentHit(Base: hit)).ToList(), IntendsToAttack = true,
            Powers = PowerSet.Empty.With(PowerId.Suck, suck),
        });
        return state;
    }

    private static SimState AfterTheTurn(SimState state) => Assert.Single(SimEngine.Run(state).Branches);

    [Fact]
    public void EachHitThatGetsThroughGivesItsAmountOfStrength()
    {
        var after = AfterTheTurn(Stalker(suck: 3, 3, 3));
        Assert.Equal(6, after.Enemy(id: 0)!.Powers[PowerId.Strength]);
        Assert.Equal(0, after.Enemy(id: 0)!.Powers.DataOf(PowerId.Suck));
    }

    [Fact]
    public void AHitYourBlockTakesWholeGivesNone()
    {
        // Block 4: the first hit (3) is all blocked, the second gets 2 through.
        var state = Stalker(suck: 3, 3, 3);
        state.You.Block = 4;
        Assert.Equal(3, AfterTheTurn(state).Enemy(id: 0)!.Powers[PowerId.Strength]);
        state.You.Block = 6;
        Assert.False(AfterTheTurn(state).Enemy(id: 0)!.Powers.Has(PowerId.Strength));
    }

    [Fact]
    public void AHitCountsOnceWhoeverItGotThroughTo()
    {
        // The report's fight: both Fools hit twice. Your block takes the first hit; your teammate's doesn't: each hit got through on someone, 2 × 3.
        var state = Stalker(suck: 3, 3, 3);
        state.You.Block = 3;
        state.Teammates.Add(new SimPlayer { Hp = 40 });
        Assert.Equal(6, AfterTheTurn(state).Enemy(id: 0)!.Powers[PowerId.Strength]);
        // Every player's block takes every hit: none.
        state.Teammates[0].Block = 6;
        state.You.Block = 6;
        Assert.False(AfterTheTurn(state).Enemy(id: 0)!.Powers.Has(PowerId.Strength));
    }
}
