using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Decision #155: Incoming Damage shows the most likely way the enemy turn goes, with its chance, and lists every other way on hover,
/// each against it. Outcomes group the branches that leave you the same (TurnOutcomes.Of); a tie goes to the worse for you.
/// </summary>
public class TurnOutcomeTests
{
    private static readonly SimDrawCard Plain = new("plain");
    private static readonly SimDrawCard Invincible = new("invincible") { InvincibleBlock = 10 };

    /// <summary>An enemy hitting for 12, and your draw pile with I Am Invincible among other cards (its top unknown).</summary>
    private static SimState WithInvincible(int copies, int others, int hp = 50)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "Mecha Knight", Hp = 100, Hits = { 12 } } } };
        state.You.Hp = hp;
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(Invincible, copies), new SimDrawGroup(Plain, others) } };
        state.Log = true;
        return state;
    }

    [Fact]
    public void TheMostLikelyOutcomeComesFirstWithItsChance()
    {
        // One I Am Invincible among ten: 90% you lose 12, 10% it plays and you lose 2.
        var outcomes = TurnOutcomes.Of(SimEngine.Run(WithInvincible(copies: 1, others: 9)), hpIfUnknown: 50);
        Assert.Equal(2, outcomes.Count);
        Assert.Equal(12, outcomes[0].Taken);
        Assert.Equal("90%", outcomes[0].Chance.Text());
        Assert.Equal(2, outcomes[1].Taken);
        Assert.Equal("10%", outcomes[1].Chance.Text());
        Assert.Equal("I Am Invincible block +10", TurnOutcomes.Differences(outcomes[1].Branch, outcomes[0].Branch));
    }

    [Fact]
    public void ATieGoesToTheWorseOutcome()
    {
        // One copy in two: 50/50, and the one shown is the 12, not the 2.
        var outcomes = TurnOutcomes.Of(SimEngine.Run(WithInvincible(copies: 1, others: 1)), hpIfUnknown: 50);
        Assert.Equal(12, outcomes[0].Taken);
        Assert.Equal(0.5, outcomes[0].Share, 9);
    }

    [Fact]
    public void DyingIsAnOutcomeOfItsOwn()
    {
        // 10 HP: without I Am Invincible on top you die (90%), with it you lose 2.
        var outcomes = TurnOutcomes.Of(SimEngine.Run(WithInvincible(copies: 1, others: 9, hp: 10)), hpIfUnknown: 10);
        Assert.True(outcomes[0].Dies);
        Assert.Equal("90%", outcomes[0].Chance.Text());
        Assert.False(outcomes[1].Dies);
    }

    [Fact]
    public void BeingSavedIsAnOutcomeOfItsOwn()
    {
        // With Fairy in a Bottle at 10 HP: the 90% outcome is the fairy bringing you back (24 HP), not a death.
        var state = WithInvincible(copies: 1, others: 9, hp: 10);
        state.You.Fairies = 1;
        state.You.FairyHeal = 24;
        var outcomes = TurnOutcomes.Of(SimEngine.Run(state), hpIfUnknown: 10);
        Assert.True(outcomes[0].Revived);
        Assert.Equal(24, outcomes[0].HpAfter);
        Assert.False(outcomes[1].Revived);
    }

    [Fact]
    public void NoRollIsOneCertainOutcome()
    {
        var state = WithInvincible(copies: 0, others: 9);
        var outcome = Assert.Single(TurnOutcomes.Of(SimEngine.Run(state), hpIfUnknown: 50));
        Assert.True(outcome.Chance.Certain);
        Assert.Equal(12, outcome.Taken);
    }
}
