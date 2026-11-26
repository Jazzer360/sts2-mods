using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Play Preview: what an enemy's intent shows right after a play (SimOutcome.IntentsAfterPlay), as the game recalculates it then:
/// a hit through the damage math with the play's Weak, Strength and kills in.
/// </summary>
public class PlayPreviewTests
{
    private static SimState Fight(int hp = 50)
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: hp) } };
        state.Enemies[0].Intent = new List<IntentHit> { new(Base: 12m), new(Base: 12m) };
        return state;
    }

    [Fact]
    public void WeakAppliedByThePlayLowersTheIntentItShows()
    {
        // Weak: the enemy's attacks deal 25% less (12 → 9), shown on its intent as soon as it lands.
        var weak = Play(Card("Neutralize"), target: 0, plays: 1, new ApplyPowerAction(PowerId.Weak, Amount: 1) { Debuff = true });
        var intent = SimEngine.Run(Fight(), weak).IntentsAfterPlay[0];
        Assert.Equal(12, intent.Before);
        Assert.Equal(9, intent.Most);
        Assert.Equal(2, intent.Hits);
        Assert.True(intent.Changed);
        Assert.False(intent.DeadInSome);
    }

    [Fact]
    public void AnEnemyThePlayKillsIsDeadInItsPreview()
    {
        var kill = Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 20));
        var intent = SimEngine.Run(Fight(hp: 10), kill).IntentsAfterPlay[0];
        Assert.True(intent.DeadInAll);
        Assert.True(intent.Changed);
    }

    [Fact]
    public void WhatThePlayTakesFromYouIsInThePreviewsTurnNotInTheBoardItLeaves()
    {
        // A dev review (Foolish Shot and Strike into a Toadpole's Thorns 2, its intent 4×3): the preview is the play's whole turn, so the 2 its Thorns
        // takes as you hit is in its HP lost (14), where the board the play left, with that 2 gone already, gives 12. The play check adds back what the
        // history shows the play took from you (ProcAudit.PreviewIssues).
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 23) } };
        state.You.Hp = 59;
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Thorns, amount: 2);
        state.Enemies[0].Intent = new List<IntentHit> { new(Base: 4m), new(Base: 4m), new(Base: 4m) };
        var strike = Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 6));
        Assert.Equal(14, SimEngine.Run(state, strike).TakenMax);
        var left = state.Clone();
        left.You.Hp = 57;
        left.Enemies[0].Hp = 17;
        Assert.Equal(12, SimEngine.Run(left).TakenMax);
    }

    [Fact]
    public void APlayThatDoesntTouchTheEnemyLeavesItsIntent()
    {
        var block = Play(Card("Defend", SimCardType.Skill), target: Who.None, plays: 1, new GainBlockAction(Times: 1, Base: 5, SimProps.Move));
        var intent = SimEngine.Run(Fight(), block).IntentsAfterPlay[0];
        Assert.False(intent.Changed);
        Assert.Equal(12, intent.Most);
    }
}
