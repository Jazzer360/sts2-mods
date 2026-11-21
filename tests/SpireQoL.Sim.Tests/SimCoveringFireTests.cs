using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Covering Fire (decompile v0.113.1, CoveringFirePower.AfterArrowShot): each shot of yours from a card gives every other
/// living player its amount of block (unpowered).
/// </summary>
public class SimCoveringFireTests
{
    [Fact]
    public void EachShotGivesEveryLivingTeammateBlock()
    {
        var s = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50, Hits = new() { 10 } } } };
        s.Teammates.Add(new SimPlayer { Hp = 40 });
        s.Teammates.Add(new SimPlayer { Hp = 0 });
        s.You.Powers = s.You.Powers.With(PowerId.CoveringFire, 3);
        // Played twice, with no arrow: each shot it tries still counts (TryShootArrow calls AfterArrowShot).
        var o = SimEngine.Run(s, V2.V2Script.Play(V2.V2Script.Card("QuickShot"), 1, 2, new ShootAction(), new ImpaleAction()));
        Assert.All(o.Branches, b => Assert.Equal(6, b.Teammates[0].BlockGained));
        Assert.All(o.Branches, b => Assert.Equal(0, b.Teammates[1].BlockGained));
        Assert.Equal(10, o.TakenMax); // none for you
    }
}
