using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Where the card a damage comes from is (decision #231): in a pile (every card a simulation's damage comes from is), being played
/// (cardSource.Pile.Type == PileType.Play: the damage's card play there), which play of its card play (cardSource.CurrentPlayIndex), and the
/// Modify hook's card play there (cardPlay != null); a true-or-false kept as a number (int num2 = a ? 1 : 0) is its condition.
/// </summary>
public class ReadModelSourceTests
{
    [Fact]
    public void LethalityRaisesTheFirstAttackPlayOfTheTurnOnly()
    {
        // LethalityPower.ModifyDamageMultiplicative: its owner's card, not a later play of its card play (pile Play and CurrentPlayIndex > 0: 1), and
        // the Attack plays started this turn no more than the one in play (num > num2: 1); else 1 + Amount / 100.
        var lethality = Assert.IsType<ReadModel>(SimModels.Get(PowerId.Lethality.Name));
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 30 } } };
        state.You.Powers = state.You.Powers.With(PowerId.Lethality, amount: 50);
        var started = Assert.Single(lethality.Spec.Values().OfType<HookHistory>().Select(history => history.Query).Distinct());
        state.History = new[] { started };
        var strike = new SimCard("Strike", SimCardType.Attack);
        SimCardPlay Play(int index) => new(strike, Who.You, Target: 0, PlayIndex: index, PlayCount: 2, AutoPlay: false);
        state.You.Turn = state.You.Turn.Add(started.Key, delta: 1); // this Attack play started
        Assert.Equal(1.5m, lethality.ModifyDamageMultiplicative(state, Who.You, new DamageCtx(Who.You, Target: 0, SimProps.Move, strike, Play(index: 0))));
        Assert.Equal(1m, lethality.ModifyDamageMultiplicative(state, Who.You, new DamageCtx(Who.You, Target: 0, SimProps.Move, strike, Play(index: 1))));
        // A second Attack play started this turn: no more.
        state.You.Turn = state.You.Turn.Add(started.Key, delta: 1);
        Assert.Equal(1m, lethality.ModifyDamageMultiplicative(state, Who.You, new DamageCtx(Who.You, Target: 0, SimProps.Move, strike, Play(index: 0))));
    }

    [Fact]
    public void AimReadsHeavyShotsOwnMethod()
    {
        // AimPower.ModifyDamageAdditive: cardSource is HeavyShot heavyShot ? heavyShot.ModifyAimAmount(Amount) : Amount, on its owner's powered
        // arrow attacks; HeavyShot.ModifyAimAmount: originalAmount * DynamicVars["AimMultiplier"].BaseValue, the card's own value (decision #232).
        var aim = Assert.IsType<ReadModel>(SimModels.Get(PowerId.Aim.Name));
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 30 } } };
        state.You.Powers = state.You.Powers.With(PowerId.Aim, amount: 4);
        var arrow = SimProps.Move | SimProps.FromArrow;
        var heavy = new SimCard(CardId.HeavyShot.Name, SimCardType.Attack) { Vars = new Dictionary<string, decimal> { ["AimMultiplier"] = 3m } };
        var other = new SimCard("Shot", SimCardType.Attack);
        DamageCtx From(SimCard card) => new(Who.You, Target: 0, arrow, card, new SimCardPlay(card, Who.You, Target: 0, PlayIndex: 0, PlayCount: 1, AutoPlay: false));
        Assert.Equal(12m, aim.ModifyDamageAdditive(state, Who.You, From(heavy)));
        Assert.Equal(4m, aim.ModifyDamageAdditive(state, Who.You, From(other)));
        Assert.Equal(0m, aim.ModifyDamageAdditive(state, Who.You, new DamageCtx(Who.You, Target: 0, SimProps.Move, other, Play: null)));
    }
}
