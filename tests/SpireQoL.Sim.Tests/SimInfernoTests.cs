using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Inferno (InfernoPower.AfterDamageReceived): each time you lose HP on your turn (UnblockedDamage &gt; 0 while it's your
/// side's turn), every hittable enemy takes its amount, unpowered (their block first).
/// </summary>
public class SimInfernoTests
{
    private static SimEnemy E(int id, int hp, int block = 0, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Block = block, Hits = hits.ToList() };

    [Fact]
    public void RegretAtEndOfTurnSetsOffInfernoOnEveryEnemy()
    {
        // Regret with 2 cards in hand: 2 HP lost on your turn, so 6 to each enemy (E2's 4 block takes some).
        var s = new SimState { Enemies = { E(1, 30), E(2, 30, 4) } };
        s.P.RegretCards = 1;
        s.P.HandCount = 2;
        s.P.Inferno = 6;
        var o = SimEngine.Run(s);
        Assert.Equal(24, o.Worst.Enemy(1)!.HpAtTurnEnd);
        Assert.Equal(28, o.Worst.Enemy(2)!.HpAtTurnEnd);
        Assert.Equal(2, o.TakenMax);
    }

    [Fact]
    public void EachBurnThatGetsThroughSetsItOffAgain()
    {
        // Two Burns of 2 with 3 block: the first is blocked (no HP lost), the second loses 1 HP: one Inferno.
        var s = new SimState { Enemies = { E(1, 30) } };
        s.P.Block = 3;
        s.P.HandBlockable.Add(("Burn", 2));
        s.P.HandBlockable.Add(("Burn", 2));
        s.P.Inferno = 6;
        var o = SimEngine.Run(s);
        Assert.Equal(24, o.Worst.Enemy(1)!.HpAtTurnEnd);
    }

    [Fact]
    public void InfernoCanKillBeforeTheEnemyActs()
    {
        var s = new SimState { Enemies = { E(1, 5, 0, 10) } };
        s.P.RegretCards = 1;
        s.P.HandCount = 1;
        s.P.Inferno = 6;
        var o = SimEngine.Run(s);
        Assert.True(o.Worst.Enemy(1)!.DiedBeforeActing);
        Assert.Equal(1, o.TakenMax); // Regret only
    }

    [Fact]
    public void DamageToYouDuringAPlaySetsItOff()
    {
        // Galvanic's 6 with no block: HP lost on your turn.
        var s = new SimState { Enemies = { E(1, 30) } };
        s.P.Inferno = 4;
        var play = new SimPlay { DamageToYou = { ("GalvanicPower", 6) } };
        SimEngine.ApplyPlayTo(s, play, out var branches, out _);
        Assert.Equal(26, branches[0].Enemy(1)!.Hp);
    }

    [Fact]
    public void NoInfernoWhenNothingGetsThrough()
    {
        var s = new SimState { Enemies = { E(1, 30) } };
        s.P.Block = 10;
        s.P.HandBlockable.Add(("Burn", 2));
        s.P.Inferno = 6;
        var o = SimEngine.Run(s);
        Assert.Equal(30, o.Worst.Enemy(1)!.HpAtTurnEnd);
    }
}
