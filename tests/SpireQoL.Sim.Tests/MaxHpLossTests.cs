using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// CreatureCmd.LoseMaxHp (decision #327): with HP above the new max, that much is lost first as a damage (Unblockable, Unpowered, no dealer), then
/// the max is set (at least 1) and HP above it comes down to it. Paper Cuts is read from its code: its AfterDamageGiven takes its amount of max HP
/// from a player its owner's powered attack got HP through to.
/// </summary>
public class MaxHpLossTests
{
    private static SimState Fight(int hp, int block, int hit)
    {
        var cutter = Enemy(id: 0, hp: 40);
        cutter.Hits = new List<int> { hit };
        cutter.IntendsToAttack = true;
        cutter.Powers = PowerSet.Empty.With(PowerId.PaperCuts, amount: 2);
        var state = new SimState { Enemies = { cutter } };
        state.You.Hp = hp;
        state.You.MaxHp = 70;
        state.You.Block = block;
        return state;
    }

    private static SimPlayer YouAfterTheirTurn(SimState state) => Assert.Single(SimEngine.Run(state).Branches).You;

    [Fact]
    public void PaperCutsTakesMaxHpWhereTheHitGetsThrough()
    {
        Assert.IsType<ReadModel>(SimModels.Get(PowerId.PaperCuts.Name));
        var you = YouAfterTheirTurn(Fight(hp: 50, block: 0, hit: 5));
        Assert.Equal(68, you.MaxHp);
        Assert.Equal(45, you.Hp);
    }

    [Fact]
    public void HpAboveTheNewMaxIsLostToo()
    {
        // 70 of 70, a hit of 1 through: 69, the max 68, so 1 more is lost (Unblockable: the block left doesn't take it).
        var you = YouAfterTheirTurn(Fight(hp: 70, block: 0, hit: 1));
        Assert.Equal(68, you.MaxHp);
        Assert.Equal(68, you.Hp);
    }

    [Fact]
    public void ABlockedHitTakesNoMaxHp()
    {
        var you = YouAfterTheirTurn(Fight(hp: 70, block: 5, hit: 3));
        Assert.Equal(70, you.MaxHp);
        Assert.Equal(70, you.Hp);
    }
}
