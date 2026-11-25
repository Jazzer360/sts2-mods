using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A power's AfterRemoved run mid-fight (decision #288): PowerCmd.Remove takes it off (RemoveInternal), then calls its AfterRemoved(power.Owner), the
/// amount it had left as it was; PowerCmd.ModifyAmount and PowerCmd.Decrement remove it so once ShouldRemoveDueToAmount says so. A read power's
/// AfterRemoved is its program (Burrowed's CreatureCmd.LoseBlock of its old owner), one that doesn't read is unknown where it runs (Intercept's,
/// Vital Spark's loops), and a relic's stays outside fights (RelicCmd.Remove: events).
/// </summary>
public class AfterRemovedTests
{
    private static SimState Board()
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 40, block: 7) } };
        state.You.Hp = 50;
        return state;
    }

    [Fact]
    public void ItsAmountIsTheOneItHadAsItWent()
    {
        // base.Amount in AfterRemoved: the power is off its owner by then (RemoveInternal), its amount left as it was.
        var model = new ReadModel(new ModelSpec("TestRemovedPower", ModelKind.Power, new[]
        {
            new HookProgram(HookName.AfterRemoved, new HookStep[] { new GainBlockStep(new HookFactValue(HookFact.Amount), new HookConstant(0m)) }),
        }));
        var state = Board();
        model.AfterRemoved(state, Who.You, amount: 4);
        Assert.Equal(4, state.You.Block);
        Assert.True(model.Overrides(nameof(SimModel.AfterRemoved)));
    }

    [Fact]
    public void BurrowedsOwnerLosesItsBlockAsItGoes()
    {
        // BurrowedPower.AfterRemoved: CreatureCmd.LoseBlock(oldOwner, 999999999, null), read as a LoseBlockStep of its owner by no one.
        var burrowed = SimModels.Get(PowerId.Burrowed.Name);
        Assert.IsType<ReadModel>(burrowed);
        var state = Board();
        burrowed!.AfterRemoved(state, owner: 0, amount: 1);
        Assert.Equal(0, state.Enemy(0)!.Block);
        Assert.True(state.Enemy(0)!.BlockBroken);
        Assert.Null(state.Unmodelled);
    }

    [Fact]
    public void ABlockLossThatBreaksTheBlockTellsAfterBlockBrokenByItsRemover()
    {
        // CreatureCmd.LoseBlock: Hook.AfterBlockBroken once the block goes from above 0 to 0, its breaker the remover (Hand Drill's needs its owner
        // to be the breaker: a loss by no one gives no Vulnerable).
        var state = Board();
        state.You.Relics = state.You.Relics.With(RelicId.HandDrill, 0);
        SimEngine.LoseBlock(state, creature: 0, amount: 3, remover: Who.You);
        Assert.Equal(4, state.Enemy(0)!.Block);
        Assert.False(state.Enemy(0)!.Powers.Has(PowerId.Vulnerable));
        SimEngine.LoseBlock(state, creature: 0, amount: 10, remover: Who.You);
        Assert.Equal(0, state.Enemy(0)!.Block);
        Assert.Equal(2, state.Enemy(0)!.Powers[PowerId.Vulnerable]);
        var byNoOne = Board();
        byNoOne.You.Relics = byNoOne.You.Relics.With(RelicId.HandDrill, 0);
        SimEngine.LoseBlock(byNoOne, creature: 0, amount: 10, remover: null);
        Assert.False(byNoOne.Enemy(0)!.Powers.Has(PowerId.Vulnerable));
    }

    [Fact]
    public void AnAfterRemovedTheReaderDoesntReadIsUnknownWhereItRuns()
    {
        // InterceptPower.AfterRemoved removes each teammate's Covered its owner applied (a loop the reader doesn't read): Intercept stays read for its
        // damage term, and a simulation that removes it is unknown, never a certainty.
        var intercept = SimModels.Get(PowerId.Intercept.Name);
        Assert.IsType<ReadModel>(intercept);
        var state = Board();
        state.You.Powers = state.You.Powers.With(PowerId.Intercept, 1);
        SimEngine.RemovePower(state, Who.You, PowerId.Intercept);
        Assert.False(state.You.Powers.Has(PowerId.Intercept));
        Assert.Contains("InterceptPower's AfterRemoved", state.Unmodelled);
        // An application that takes it to 0 removes it the same way (PowerCmd.ModifyAmount, then PowerCmd.Remove).
        var applied = Board();
        applied.You.Powers = applied.You.Powers.With(PowerId.Intercept, 1);
        SimEngine.ApplyPower(applied, Who.You, Who.You, PowerId.Intercept, amount: -1, debuff: false);
        Assert.False(applied.You.Powers.Has(PowerId.Intercept));
        Assert.Contains("InterceptPower's AfterRemoved", applied.Unmodelled);
        // Kept, nothing is told.
        var kept = Board();
        kept.You.Powers = kept.You.Powers.With(PowerId.Intercept, 1);
        SimEngine.ApplyPower(kept, Who.You, Who.You, PowerId.Intercept, amount: 1, debuff: false);
        Assert.Null(kept.Unmodelled);
    }

    [Fact]
    public void ADecrementToZeroRemovesItAndTellsIt()
    {
        // PowerCmd.Decrement: PowerCmd.ModifyAmount by -1, then PowerCmd.Remove at 0, its AfterRemoved told with the 0 it was left at.
        var state = Board();
        state.You.Powers = state.You.Powers.With(PowerId.Intercept, 2);
        SimEngine.DecrementPower(state, Who.You, PowerId.Intercept);
        Assert.Equal(1, state.You.Powers[PowerId.Intercept]);
        Assert.Null(state.Unmodelled);
        SimEngine.DecrementPower(state, Who.You, PowerId.Intercept);
        Assert.False(state.You.Powers.Has(PowerId.Intercept));
        Assert.NotNull(state.Unmodelled);
    }
}
