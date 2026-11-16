using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Pael's Eye (decompile v0.113.1): unused and with no card played this turn (auto-plays don't count), its
/// BeforeSideTurnEndEarly exhausts your whole hand, before every other end-of-turn effect. Each card exhausted sets off
/// Feel No Pain, Charon's Ashes and Forgotten Soul; nothing is left in hand to act at end of turn (Burn, Regret).
/// </summary>
public class SimPaelsEyeTests
{
    private static SimEnemy Enemy(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    private static SimState Armed()
    {
        var s = new SimState { Enemies = { Enemy(1, 50, 10) } };
        s.P.PaelsEyeExhaust = true;
        s.P.HandCount = 3;
        s.P.FeelNoPain = 3;
        s.P.HandBlockable.Add(("Burn", 2));
        return s;
    }

    [Fact]
    public void ItExhaustsYourHandFirst()
    {
        // Three exhausts: 9 block from Feel No Pain; the Burn is exhausted before it can burn you.
        var o = SimEngine.Run(Armed());
        Assert.Equal(1, o.TakenMax);
    }

    [Fact]
    public void ACardYouPlayDisarmsIt()
    {
        // Played: the hand (2 left) stays, the Burn burns you (2), no Feel No Pain block.
        var o = SimEngine.Run(Armed(), new SimPlay { Name = "Defend", HandDelta = -1 });
        Assert.Equal(12, o.TakenMax);
    }

    [Fact]
    public void AnAutoPlayDoesnt()
    {
        var o = SimEngine.Run(Armed(), new SimPlay { Name = "Strike", HandDelta = -1, AutoPlay = true });
        Assert.Equal(4, o.TakenMax); // two cards left to exhaust: 6 block
    }

    [Fact]
    public void OrichalcumLooksBeforeItsBlock()
    {
        // Orichalcum checks your block in BeforeSideTurnEndVeryEarly, before Pael's Eye exhausts your hand (Early): with no block
        // then, it arms, and gives its 6 on top of Feel No Pain's 9.
        var s = Armed();
        s.P.HandBlockable.Clear();
        s.P.OrichalcumBlock = 6;
        Assert.Equal(0, SimEngine.Run(s).TakenMax);
    }

    [Fact]
    public void CharonsAshesFiresPerCard()
    {
        var s = new SimState { Enemies = { Enemy(1, 9, 10) } };
        s.P.PaelsEyeExhaust = true;
        s.P.HandCount = 3;
        s.P.CharonsAshesDamage = 3;
        Assert.True(SimEngine.Run(s).DeadInAll(1));
    }
}
