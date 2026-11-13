using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Your HP and what prevents your death (CreatureCmd.Kill → Hook.ShouldDie, then ShouldDieLate): Fairy in a Bottle
/// (FairyInABottle.ShouldDie: heals 30% of max HP, used up) before Lizard Tail (LizardTail.ShouldDieLate: 50%, once).
/// Each heals you from 0; the rest of the hit that took you there is lost.
/// </summary>
public class SimReviveTests
{
    private static SimEnemy E(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    [Fact]
    public void LosingAllYourHpWithNothingToSaveYouIsDeath()
    {
        var s = new SimState { Enemies = { E(1, 50, 12) } };
        s.P.Hp = 10;
        var o = SimEngine.Run(s);
        Assert.True(o.Worst.Died);
        Assert.Equal(12, o.Worst.Taken); // all of it, overkill included (as the forecast check counts it)
    }

    [Fact]
    public void SurvivingWithOneHpIsntDeath()
    {
        var s = new SimState { Enemies = { E(1, 50, 9) } };
        s.P.Hp = 10;
        var o = SimEngine.Run(s);
        Assert.False(o.Worst.Died);
        Assert.Equal(1, o.Worst.P.Hp);
    }

    [Fact]
    public void LizardTailHealsYouFromZeroAndLaterHitsStillLand()
    {
        // 10 HP: the first hit (12) takes you to 0, Lizard Tail heals 40 (50% of 80); the second hit (6) leaves 34.
        var s = new SimState { Enemies = { E(1, 50, 12, 6) } };
        s.P.Hp = 10;
        s.P.LizardTailHeal = 40;
        var o = SimEngine.Run(s);
        var w = o.Worst;
        Assert.False(w.Died);
        Assert.True(w.LizardTailUsed);
        Assert.Equal(34, w.P.Hp);
        Assert.Equal(18, w.Taken); // 12 (overkill included), then 6
    }

    [Fact]
    public void FairyInABottleGoesFirstAndLizardTailStaysForLater()
    {
        var s = new SimState { Enemies = { E(1, 50, 12) } };
        s.P.Hp = 10;
        s.P.Fairies = 1;
        s.P.FairyHeal = 24;
        s.P.LizardTailHeal = 40;
        var w = SimEngine.Run(s).Worst;
        Assert.Equal(1, w.FairiesUsed);
        Assert.False(w.LizardTailUsed);
        Assert.Equal(24, w.P.Hp);
    }

    [Fact]
    public void BothCanBeUsedUpInOneEnemyTurn()
    {
        // 5 HP: 10 → Fairy (heals 8), 10 → Lizard Tail (heals 20), 10 → 10 left.
        var s = new SimState { Enemies = { E(1, 50, 10, 10, 10) } };
        s.P.Hp = 5;
        s.P.Fairies = 1;
        s.P.FairyHeal = 8;
        s.P.LizardTailHeal = 20;
        var w = SimEngine.Run(s).Worst;
        Assert.False(w.Died);
        Assert.Equal(1, w.FairiesUsed);
        Assert.True(w.LizardTailUsed);
        Assert.Equal(10, w.P.Hp);
    }

    [Fact]
    public void UnblockableEndOfTurnDamageCanKillToo()
    {
        var s = new SimState { Enemies = { E(1, 50) } };
        s.P.Hp = 3;
        s.P.HandUnblockable.Add(("Decay", 2));
        s.P.RegretCards = 1;
        s.P.HandCount = 2;
        Assert.True(SimEngine.Run(s).Worst.Died);
    }

    [Fact]
    public void UntrackedHpNeverDies()
    {
        // The generated fights and older replay cases don't set your HP: nothing about them changes.
        var s = new SimState { Enemies = { E(1, 50, 999) } };
        var w = SimEngine.Run(s).Worst;
        Assert.False(w.Died);
        Assert.Equal(999, w.Taken);
    }
}
