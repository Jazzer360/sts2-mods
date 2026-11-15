using SpireQoL.Sim;
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
        var shot = new SimPlay { Name = "QuickShot", IsAttack = true, TeammateBlock = (3, 2) };
        var o = SimEngine.Run(s, shot);
        Assert.All(o.Branches, b => Assert.Equal(6, b.Teammates[0].BlockGained));
        Assert.All(o.Branches, b => Assert.Equal(0, b.Teammates[1].BlockGained));
        Assert.Equal(10, o.TakenMax); // none for you
    }
}
