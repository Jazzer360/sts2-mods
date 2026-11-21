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
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50, Hits = new() { 10 } } } };
        state.Teammates.Add(new SimPlayer { Hp = 40 });
        state.Teammates.Add(new SimPlayer { Hp = 0 });
        state.You.Powers = state.You.Powers.With(PowerId.CoveringFire, 3);
        // Played twice, with no arrow: each shot it tries still counts (TryShootArrow calls AfterArrowShot).
        var outcome = SimEngine.Run(state, ScriptBuilder.Play(ScriptBuilder.Card("QuickShot"), 1, 2, new ShootAction(), new ImpaleAction()));
        Assert.All(outcome.Branches, branch => Assert.Equal(6, branch.Teammates[0].BlockGained));
        Assert.All(outcome.Branches, branch => Assert.Equal(0, branch.Teammates[1].BlockGained));
        Assert.Equal(10, outcome.TakenMax); // none for you
    }
}
