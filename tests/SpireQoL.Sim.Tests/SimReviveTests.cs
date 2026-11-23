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
        var state = new SimState { Enemies = { E(id: 1, hp: 50, hits: 12) } };
        state.You.Hp = 10;
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.Worst.Died);
        Assert.Equal(12, outcome.Worst.Taken); // all of it, overkill included (as the forecast check counts it)
    }

    [Fact]
    public void SurvivingWithOneHpIsntDeath()
    {
        var state = new SimState { Enemies = { E(id: 1, hp: 50, hits: 9) } };
        state.You.Hp = 10;
        var outcome = SimEngine.Run(state);
        Assert.False(outcome.Worst.Died);
        Assert.Equal(1, outcome.Worst.You.Hp);
    }

    [Fact]
    public void LizardTailHealsYouFromZeroAndLaterHitsStillLand()
    {
        // 10 HP: the first hit (12) takes you to 0, Lizard Tail heals 40 (50% of 80); the second hit (6) leaves 34.
        var state = new SimState { Enemies = { E(id: 1, hp: 50, hits: [12, 6]) } };
        state.You.Hp = 10;
        state.You.LizardTailHeal = 40;
        var outcome = SimEngine.Run(state);
        var branch = outcome.Worst;
        Assert.False(branch.Died);
        Assert.True(branch.LizardTailUsed);
        Assert.Equal(34, branch.You.Hp);
        Assert.Equal(18, branch.Taken); // 12 (overkill included), then 6
    }

    [Fact]
    public void FairyInABottleGoesFirstAndLizardTailStaysForLater()
    {
        var state = new SimState { Enemies = { E(id: 1, hp: 50, hits: 12) } };
        state.You.Hp = 10;
        state.You.Fairies = 1;
        state.You.FairyHeal = 24;
        state.You.LizardTailHeal = 40;
        var branch = SimEngine.Run(state).Worst;
        Assert.Equal(1, branch.FairiesUsed);
        Assert.False(branch.LizardTailUsed);
        Assert.Equal(24, branch.You.Hp);
    }

    [Fact]
    public void BothCanBeUsedUpInOneEnemyTurn()
    {
        // 5 HP: 10 → Fairy (heals 8), 10 → Lizard Tail (heals 20), 10 → 10 left.
        var state = new SimState { Enemies = { E(id: 1, hp: 50, hits: [10, 10, 10]) } };
        state.You.Hp = 5;
        state.You.Fairies = 1;
        state.You.FairyHeal = 8;
        state.You.LizardTailHeal = 20;
        var branch = SimEngine.Run(state).Worst;
        Assert.False(branch.Died);
        Assert.Equal(1, branch.FairiesUsed);
        Assert.True(branch.LizardTailUsed);
        Assert.Equal(10, branch.You.Hp);
    }

    [Fact]
    public void UnblockableEndOfTurnDamageCanKillToo()
    {
        var state = new SimState { Enemies = { E(id: 1, hp: 50) } };
        state.You.Hp = 3;
        state.You.TurnEndCards.Add(new TurnEndCard("BadLuck", "Bad Luck", Amount: 2));
        state.You.TurnEndCards.Add(new TurnEndCard("Regret", "Regret", Amount: 0));
        state.You.Cards = state.You.Cards.With(CardId.Regret, amount: 1); // its BeforeSideTurnEnd counts the hand (RegretCard)
        state.You.HandCount = 2;
        Assert.True(SimEngine.Run(state).Worst.Died);
    }

    [Fact]
    public void UntrackedHpNeverDies()
    {
        // The generated fights and older replay cases don't set your HP: nothing about them changes.
        var state = new SimState { Enemies = { E(id: 1, hp: 50, hits: 999) } };
        var branch = SimEngine.Run(state).Worst;
        Assert.False(branch.Died);
        Assert.Equal(999, branch.Taken);
    }
}
