using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Osty (Necrobinder): Sacrifice and Bone Shards kill him (CreatureCmd.Kill on Owner.Osty, decompile
/// v0.111.0), so attacks he'd have taken come to you.
/// </summary>
public class SimOstyTests
{
    [Fact]
    public void ACardThatKillsOstyLeavesTheHitsToYou()
    {
        var s = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 40, Hits = { 6 } } } };
        s.P.OstyHp = 10;
        Assert.Equal(0, SimEngine.Run(s).TakenMax); // Osty takes the 6

        // Sacrifice: kills Osty, then 8 block (the block math stays the adapter's).
        var sacrifice = new SimPlay { Name = "Sacrifice", KillsOsty = true, BlockGains = 1, BlockAmount = 8 };
        SimEngine.ApplyPlayTo(s, sacrifice, out var after, out _);
        Assert.Equal(0, after[0].P.OstyHp);
        var o = SimEngine.Run(s, sacrifice);
        Assert.Equal(0, o.TakenMax);           // 8 block against 6
        Assert.Equal(0, o.OstyTakenMax);

        var bigHit = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 40, Hits = { 12 } } } };
        bigHit.P.OstyHp = 10;
        Assert.Equal(4, SimEngine.Run(bigHit, sacrifice).TakenMax); // 12 - 8 block, no Osty
    }
}
