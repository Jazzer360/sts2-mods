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
        state.You.Powers = state.You.Powers.With(PowerId.CoveringFire, amount: 3);
        // Played twice, with no arrow: each shot it tries still counts (TryShootArrow calls AfterArrowShot).
        var outcome = SimEngine.Run(state, ScriptBuilder.Play(ScriptBuilder.Card("QuickShot"), target: 1, plays: 2, new ShootAction(), new ImpaleAction()));
        Assert.All(outcome.Branches, branch => Assert.Equal(6, branch.Teammates[0].BlockGained));
        Assert.All(outcome.Branches, branch => Assert.Equal(0, branch.Teammates[1].BlockGained));
        Assert.Equal(10, outcome.TakenMax); // none for you
    }

    [Fact]
    public void ItsFilterIsAskedOfEachTeammateItself()
    {
        // Its filter (Where(Players.Select(Creature), c => c != null && c.IsAlive && c.IsPlayer && c != Owner.Creature)) is a lambda the game calls
        // for each player. Its hook keeps its tests' answers for the run (it guards two steps on one test), so each item must start afresh: you
        // first (not another player), then a dead teammate, then a living one, each answered for itself.
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50, Hits = new() { 10 } } } };
        state.Teammates.Add(new SimPlayer { Hp = 0 });
        state.Teammates.Add(new SimPlayer { Hp = 40 });
        state.You.Powers = state.You.Powers.With(PowerId.CoveringFire, amount: 3);
        var outcome = SimEngine.Run(state, ScriptBuilder.Play(ScriptBuilder.Card("QuickShot"), target: 1, plays: 1, new ShootAction(), new ImpaleAction()));
        Assert.All(outcome.Branches, branch => Assert.Equal(0, branch.Teammates[0].BlockGained));
        Assert.All(outcome.Branches, branch => Assert.Equal(3, branch.Teammates[1].BlockGained));
    }
}
