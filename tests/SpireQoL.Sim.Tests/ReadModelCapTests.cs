using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The damage cap and AfterModifyingDamageAmount read from the code (decision #229): a Modify hook's cap (ModifyDamageCap: decimal.MaxValue for a
/// damage it doesn't cap), a term that changed a damage acting there (its AfterModifyingDamageAmount's flash), and a play that changes the state
/// its damage term reads acting on that play (Slow).
/// </summary>
public class ReadModelCapTests
{
    private static SimState Board()
    {
        var state = new SimState { You = new SimPlayer { Hp = 50, MaxHp = 70 } };
        state.Enemies.Add(new SimEnemy { Id = 0, Name = "E0", Hp = 30 });
        return state;
    }

    [Fact]
    public void HardToKillCapsTheHpLostAndIntangibleTheDamage()
    {
        // v0.114.0: HardToKillPower.ModifyHpLostBeforeOstyLate: target == base.Owner and amount > 0 ? Math.Min(amount, Amount) : amount, the HP a hit takes
        // past block (before v0.114.0 it capped the damage before block, ModifyDamageCap); AfterModifyingHpLostBeforeOsty flashes. IntangiblePower's
        // ModifyDamageCap: 1.
        var hardToKill = Assert.IsType<ReadModel>(SimModels.Get(PowerId.HardToKill.Name));
        var intangible = Assert.IsType<ReadModel>(SimModels.Get(PowerId.Intangible.Name));
        var state = Board();
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.HardToKill, amount: 7).With(PowerId.Intangible, amount: 1);
        var onIt = new DamageCtx(Who.You, Target: 0, SimProps.Move, Card: null, Play: null);
        var onYou = new DamageCtx(0, Target: Who.You, SimProps.Move, Card: null, Play: null);
        Assert.Equal(7m, hardToKill.ModifyHpLostBeforeOstyLate(state, owner: 0, onIt, amount: 12m));
        Assert.Equal(5m, hardToKill.ModifyHpLostBeforeOstyLate(state, owner: 0, onIt, amount: 5m));
        Assert.Equal(12m, hardToKill.ModifyHpLostBeforeOstyLate(state, owner: 0, onYou, amount: 12m));
        Assert.Equal(decimal.MaxValue, hardToKill.ModifyDamageCap(state, owner: 0, onIt));
        Assert.Equal(1m, intangible.ModifyDamageCap(state, owner: 0, onIt));
        // Its term changed the HP lost: it acts there (its AfterModifyingHpLostBeforeOsty flashes).
        hardToKill.AfterModifyingHpLost(state, owner: 0, afterOsty: false);
        Assert.True(state.Fired.Has(PowerId.HardToKill.Name));
    }

    [Fact]
    public void SlowCountsEveryCardPlayedAndActsOnIt()
    {
        // SlowPower.AfterCardPlayed: DynamicVars["SlowAmount"].BaseValue++, which its ModifyDamageMultiplicative reads (1 + 0.1 × SlowAmount).
        var slow = Assert.IsType<ReadModel>(SimModels.Get(PowerId.Slow.Name));
        var state = Board();
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.Slow, amount: 1);
        var defend = new SimCardPlay(new SimCard("Defend", SimCardType.Skill), Who.You, Target: Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false);
        var after = Assert.Single(slow.AfterCardPlayed(state, owner: 0, defend));
        Assert.Equal(1, after.Enemies[0].Fields[slow.FieldKey(HookField.VarPrefix + "SlowAmount")]);
        Assert.True(after.Fired.Has(PowerId.Slow.Name));
        Assert.Equal(1.1m, slow.ModifyDamageMultiplicative(after, owner: 0, new DamageCtx(Who.You, Target: 0, SimProps.Move, Card: null, Play: null)));
    }
}
