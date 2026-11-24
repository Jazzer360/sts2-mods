using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A flag of the damage's or block's props a read model's code tests (props.HasFlag(ValueProp.X): HookFact.PropsHave, the game's flag by its
/// name): No Block's Unpowered, Dislike You's FromArrow.
/// </summary>
public class ReadModelPropsTests
{
    private static SimState Board()
    {
        var state = new SimState { You = new SimPlayer { Hp = 50, MaxHp = 70 } };
        state.Enemies.Add(new SimEnemy { Id = 0, Name = "E0", Hp = 30 });
        return state;
    }

    [Fact]
    public void NoBlockStopsBlockFromACardThatIsntUnpowered()
    {
        // NoBlockPower.ModifyBlockMultiplicative: target == base.Owner, !props.HasFlag(ValueProp.Unpowered), cardSource != null: 0.
        var noBlock = Assert.IsType<ReadModel>(SimModels.Get(PowerId.NoBlock.Name));
        var state = Board();
        state.You.Powers = state.You.Powers.With(PowerId.NoBlock, amount: 1);
        var defend = new SimCard("Defend", SimCardType.Skill);
        var play = new SimCardPlay(defend, Who.You, Target: Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false);
        Assert.Equal(0m, noBlock.ModifyBlockMultiplicative(state, Who.You, new BlockCtx(Who.You, SimProps.Move, defend, play)));
        Assert.Equal(1m, noBlock.ModifyBlockMultiplicative(state, Who.You, new BlockCtx(Who.You, SimProps.Move | SimProps.Unpowered, defend, play)));
        Assert.Equal(1m, noBlock.ModifyBlockMultiplicative(state, Who.You, new BlockCtx(Who.You, SimProps.Move, Card: null, Play: null)));
        Assert.Equal(1m, noBlock.ModifyBlockMultiplicative(state, Who.You, new BlockCtx(0, SimProps.Move, defend, play)));
    }

    [Fact]
    public void DislikeYouRaisesAPoweredArrowAttackOnItsOwner()
    {
        // DislikeYouPower.ModifyDamageMultiplicative: target == base.Owner, props.IsPoweredAttack(), props.HasFlag(ValueProp.FromArrow): 1 + Amount / 100.
        var dislike = Assert.IsType<ReadModel>(SimModels.Get(PowerId.DislikeYou.Name));
        var state = Board();
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.DislikeYou, amount: 50);
        Assert.Equal(1.5m, dislike.ModifyDamageMultiplicative(state, owner: 0, new DamageCtx(Who.You, Target: 0, SimProps.Move | SimProps.FromArrow, Card: null, Play: null)));
        Assert.Equal(1m, dislike.ModifyDamageMultiplicative(state, owner: 0, new DamageCtx(Who.You, Target: 0, SimProps.Move, Card: null, Play: null)));
        Assert.Equal(1m, dislike.ModifyDamageMultiplicative(state, owner: 0, new DamageCtx(Who.You, Target: 0, SimProps.Move | SimProps.Unpowered | SimProps.FromArrow, Card: null, Play: null)));
    }
}
