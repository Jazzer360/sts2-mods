using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Parrying Shield (decompile v0.111.0, ParryingShield.AfterSideTurnEnd): when your block is at least
/// 10 after your end of turn, 6 damage to a random enemy. The tab compares "fires" before and after
/// a play, so whatever changes your end-of-turn block (Orichalcum arming, Plating, Burns) counts.
/// </summary>
public class SimParryingShieldTests
{
    private static SimState Start(int block = 0)
    {
        var s = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50, Hits = { 3 } } } };
        s.P.Block = block;
        s.P.Plating = 4;          // Gorget's Plating on turn one
        s.P.OrichalcumBlock = 6;  // only when you end your turn with no block
        s.P.ParryingShieldBlock = 10;
        s.P.ParryingShieldDamage = 6;
        return s;
    }

    private static readonly SimPlay Defend = new() { Name = "Defend", BlockGains = 1, BlockAmount = 5 };

    [Fact]
    public void OneDefendStopsItAndASecondBringsItBack()
    {
        // No block: Plating 4 + Orichalcum 6 = 10, it fires.
        var now = SimEngine.Run(Start());
        Assert.True(now.ParryingShieldInAll);

        // One Defend (5): Orichalcum no longer arms, 5 + 4 = 9, it doesn't (a crossed tab).
        var oneDefend = SimEngine.Run(Start(), Defend);
        Assert.False(oneDefend.ParryingShieldInAny);

        // From 5 block, a second Defend: 10 + 4 = 14, it fires again (a plain tab).
        var afterOne = SimEngine.Run(Start(block: 5));
        Assert.False(afterOne.ParryingShieldInAny);
        Assert.True(SimEngine.Run(Start(block: 5), Defend).ParryingShieldInAll);
    }

    [Fact]
    public void ABurnLandingFirstCountsAgainstIt()
    {
        // 10 block + 4 Plating = 14, but a Burn in hand lands at end of turn, before it checks: a 5 Burn
        // leaves 9 (it doesn't fire); a 4 Burn leaves exactly 10 (it does).
        var s = Start(block: 10);
        s.P.Plating = 4;
        s.P.HandBlockable.Add(("Burn", 5));
        Assert.False(SimEngine.Run(s).ParryingShieldInAny);
        s.P.HandBlockable.Clear();
        s.P.HandBlockable.Add(("Burn", 4));
        Assert.True(SimEngine.Run(s).ParryingShieldInAll);
    }
}
