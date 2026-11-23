using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The game commands' pipelines as the simulator runs them (SimPipelines; PipelineParityTests in the catalog tests compares their hooks
/// with the game's), each step as the decompile has it (v0.113.1).
/// </summary>
public class SimPipelineTests
{
    private static SimState HardToKill(int amount)
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50) } };
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.HardToKill, amount);
        return state;
    }

    private static readonly string HardToKillFlash = SimKeys.Owned(PowerId.HardToKill.Name, owner: 0);

    [Fact]
    public void ACapFlashesOnACardsDamageThatIsntAnAttack()
    {
        // CreatureCmd.Damage runs Hook.ModifyDamage, then AfterModifyingDamageAmount for every source that changed the amount: Hard
        // to Kill flashes (HardToKillPower.AfterModifyingDamageAmount) on any damage it caps, not only an attack's.
        var after = Assert.Single(After(HardToKill(amount: 5), Play(Card("CaptureSpirit", SimCardType.Skill), target: 0, plays: 1,
            new DamageAction(new DamageSpec(Base: 12, SimProps.Unblockable | SimProps.Unpowered)))));
        Assert.Equal(45, after.Enemy(id: 0)!.Hp);
        Assert.True(after.Fired.Has(HardToKillFlash));
    }

    [Fact]
    public void ACapFlashesOnAProcsDamage()
    {
        var state = HardToKill(amount: 3);
        var after = Assert.Single(SimEngine.ProcDamageAll(state, owner: Who.You, name: RelicId.LetterOpener.Name, baseDamage: 5, SimProps.Unpowered));
        Assert.Equal(47, after.Enemy(id: 0)!.Hp);
        Assert.True(after.Fired.Has(HardToKillFlash));
        // Under the cap, it changes nothing and doesn't flash.
        var under = Assert.Single(SimEngine.ProcDamageAll(HardToKill(amount: 10), owner: Who.You, name: RelicId.LetterOpener.Name, baseDamage: 5, SimProps.Unpowered));
        Assert.False(under.Fired.Has(HardToKillFlash));
    }

    [Fact]
    public void ACapFlashesOnDamageGivenAsAFinalNumber()
    {
        // Poison's tick (PoisonPower: CreatureCmd.Damage of its amount, no dealer) goes through the caps too: Hard to Kill flashes.
        var state = HardToKill(amount: 4);
        Assert.Equal(4, SimEngine.Damage(state, state.Enemies[0], amount: 9, dealer: null, SimProps.Unblockable | SimProps.Unpowered));
        Assert.True(state.Fired.Has(HardToKillFlash));
    }

    [Fact]
    public void EveryPipelineNamesItsGameCommandAndItsHooksOnce()
    {
        foreach (var pipeline in SimPipelines.All)
        {
            Assert.Matches(@"^\w+\.\w+$", pipeline.GameCommand);
            Assert.Equal(pipeline.Hooks.Count, pipeline.Hooks.Distinct().Count());
        }
        Assert.Equal(SimPipelines.All.Count, SimPipelines.All.Select(pipeline => pipeline.Name).Distinct().Count());
    }
}
