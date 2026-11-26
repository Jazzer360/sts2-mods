using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A power's amount as it's received (Hook.ModifyPowerAmountReceived, after the amount given: PowerCmd.Apply and ModifyAmount): every listener's
/// TryModifyPowerAmountReceived in turn, the out value of each that answers true, then Hook.AfterModifyingPowerAmountReceived told to those.
/// Ruined Helmet is read from its code: `canonicalPower is StrengthPower`, `target == base.Owner.Creature`, `amount &gt; 0`, `!UsedThisCombat`
/// doubles it; its AfterModifyingPowerAmountReceived sets UsedThisCombat.
/// </summary>
public class PowerAmountReceivedTests
{
    private static SimState Board(bool helmet, bool used = false)
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 60) } };
        if (!helmet) return state;
        state.You.Relics = state.You.Relics.With(RelicId.RuinedHelmet, 0);
        if (used && SimModels.Get(RelicId.RuinedHelmet.Name) is ReadModel read) state.You.Fields = state.You.Fields.With(read.FieldKey("UsedThisCombat"), 1);
        return state;
    }

    /// <summary>A card that gains you Strength, then hits the enemy for 6: what the hit dealt.</summary>
    private static int Dealt(SimState state, params int[] strength)
    {
        var body = strength.Select(amount => (SimAction)new ApplyPowerAction(PowerId.Strength, amount) { To = ApplyPowerAction.Where.You, Debuff = amount < 0 })
            .Append(new AttackAction(Hits: 1, SimPlay.Target.Single, new DamageSpec(Base: 6, SimProps.Move))).ToArray();
        return 60 - Assert.Single(After(state, Play(Card("Flex and hit"), target: 0, plays: 1, body))).Enemy(id: 0)!.Hp;
    }

    [Fact]
    public void RuinedHelmetDoublesTheFightsFirstStrengthGainForTheHitsAfterIt()
    {
        Assert.IsType<ReadModel>(SimModels.Get(RelicId.RuinedHelmet.Name));
        Assert.Equal(6 + 2, Dealt(Board(helmet: false), 2));
        Assert.Equal(6 + 4, Dealt(Board(helmet: true), 2));
    }

    [Fact]
    public void RuinedHelmetDoublesOnlyTheFirstGain()
    {
        // The first gain spends it (UsedThisCombat): the second lands as given, in the same play or once it's already spent.
        Assert.Equal(6 + 4 + 3, Dealt(Board(helmet: true), 2, 3));
        Assert.Equal(6 + 2, Dealt(Board(helmet: true, used: true), 2));
    }

    [Fact]
    public void RuinedHelmetLeavesALossAloneAndStaysArmed()
    {
        // amount <= 0: not doubled, and not spent (no AfterModifyingPowerAmountReceived): the gain after it is doubled.
        Assert.Equal(6 - 1 + 4, Dealt(Board(helmet: true), -1, 2));
    }

    [Fact]
    public void RuinedHelmetDoublesOnlyItsOwnersStrength()
    {
        // target == base.Owner.Creature: an enemy's Strength lands as given, and the helmet stays armed.
        var state = Board(helmet: true);
        Assert.Equal(3, SimEngine.AmountReceived(state, applier: 0, target: 0, PowerId.Strength, amount: 3, debuff: false));
        Assert.Equal(5, SimEngine.AmountReceived(state, Who.You, target: Who.You, PowerId.Dexterity, amount: 5, debuff: false));
        Assert.Equal(6, SimEngine.AmountReceived(state, Who.You, target: Who.You, PowerId.Strength, amount: 3, debuff: false));
        Assert.Equal(3, SimEngine.AmountReceived(state, Who.You, target: Who.You, PowerId.Strength, amount: 3, debuff: false));
    }
}
