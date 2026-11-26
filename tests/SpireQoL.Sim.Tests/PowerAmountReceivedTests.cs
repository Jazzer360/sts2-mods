using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A power's amount as it's received (Hook.ModifyPowerAmountReceived, after the amount given: PowerCmd.Apply and ModifyAmount): every listener's
/// TryModifyPowerAmountReceived in turn, the out value of each that answers true, then Hook.AfterModifyingPowerAmountReceived told to those.
/// Ruined Helmet is read from its code: `canonicalPower is StrengthPower`, `target == base.Owner.Creature`, `amount &gt; 0`, `!UsedThisCombat`
/// doubles it; its AfterModifyingPowerAmountReceived sets UsedThisCombat. Artifact is read from its code too (decision #331).
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

    // ---- Artifact, read from its code (decision #331): ArtifactPower.TryModifyPowerAmountReceived answers 0 when the power lands on its owner,
    // is a debuff at the amount so far (canonicalPower.GetTypeForAmount(amount)) and is shown (canonicalPower.IsVisible); its
    // AfterModifyingPowerAmountReceived decrements it (PowerCmd.Decrement).

    private static SimState WithArtifact(int charges)
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 60) } };
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Artifact, amount: charges);
        return state;
    }

    [Fact]
    public void ArtifactIsReadFromItsCodeAndTakesADebuffACharge()
    {
        Assert.IsType<ReadModel>(SimModels.Get(PowerId.Artifact.Name));
        var state = WithArtifact(charges: 2);
        var weak = new ApplyPowerAction(PowerId.Weak, Amount: 2) { To = ApplyPowerAction.Where.Target, Debuff = true };
        var branch = Assert.Single(After(state, Play(Card("Weaken"), target: 0, plays: 1, weak)));
        Assert.False(branch.Enemy(id: 0)!.Powers.Has(PowerId.Weak));
        Assert.Equal(1, branch.Enemy(id: 0)!.Powers[PowerId.Artifact]);
        // Its last charge goes with the next one, and the third lands.
        branch = Assert.Single(After(branch, Play(Card("Weaken"), target: 0, plays: 2, weak)));
        Assert.Equal(2, branch.Enemy(id: 0)!.Powers[PowerId.Weak]);
        Assert.False(branch.Enemy(id: 0)!.Powers.Has(PowerId.Artifact));
    }

    [Fact]
    public void ArtifactLetsABuffThroughAndTakesStrengthBelowZero()
    {
        // GetTypeForAmount: Strength given more is a buff; Strength, a counter that can go below 0, given less is a debuff; Weak, a debuff that
        // can't, given less is a buff.
        var state = WithArtifact(charges: 2);
        Assert.Equal(3, SimEngine.AmountReceived(state, applier: 0, target: 0, PowerId.Strength, amount: 3, debuff: false));
        Assert.Equal(-1, SimEngine.AmountReceived(state, applier: Who.You, target: 0, PowerId.Weak, amount: -1, debuff: false));
        Assert.Equal(2, state.Enemy(id: 0)!.Powers[PowerId.Artifact]);
        Assert.Equal(0, SimEngine.AmountReceived(state, applier: Who.You, target: 0, PowerId.Strength, amount: -2, debuff: true));
        Assert.Equal(1, state.Enemy(id: 0)!.Powers[PowerId.Artifact]);
    }

    [Fact]
    public void ArtifactGuardsOnlyItsOwner()
    {
        // target != base.Owner: a debuff on you lands whatever the enemy's Artifact.
        var state = WithArtifact(charges: 1);
        Assert.Equal(2, SimEngine.AmountReceived(state, applier: 0, target: Who.You, PowerId.Weak, amount: 2, debuff: true));
        Assert.Equal(1, state.Enemy(id: 0)!.Powers[PowerId.Artifact]);
    }

    [Fact]
    public void ArtifactAndRuinedHelmetEachTakeTheirOwnInTheListenersOrder()
    {
        // Your Artifact and Ruined Helmet: a Strength loss is a debuff, Artifact's (the helmet asks for amount > 0 and stays armed); the gain
        // after it is the helmet's, doubled, and Artifact, spent, has nothing to say.
        var state = Board(helmet: true);
        state.You.Powers = state.You.Powers.With(PowerId.Artifact, amount: 1);
        Assert.Equal(6 + 4, Dealt(state, -1, 2));
        Assert.Equal(0, SimEngine.AmountReceived(state, Who.You, target: Who.You, PowerId.Strength, amount: -1, debuff: true));
        Assert.False(state.You.Powers.Has(PowerId.Artifact));
        Assert.Equal(4, SimEngine.AmountReceived(state, Who.You, target: Who.You, PowerId.Strength, amount: 2, debuff: false));
    }

    [Fact]
    public void ArtifactLetsAPowerHiddenByItsTargetThrough()
    {
        // PowerModel.IsVisible: a power whose Target is a player other than you is hidden, and Artifact asks for a shown one.
        var state = WithArtifact(charges: 1);
        Assert.Equal(2, SimEngine.AmountReceived(state, applier: Who.You, target: 0, PowerId.Weak, amount: 2, debuff: true, powerTarget: Who.Teammate(0)));
        Assert.Equal(1, state.Enemy(id: 0)!.Powers[PowerId.Artifact]);
        Assert.Null(state.Unmodelled);
        // Aimed at you, or at an enemy, it's shown.
        Assert.Equal(0, SimEngine.AmountReceived(state, applier: Who.You, target: 0, PowerId.Weak, amount: 2, debuff: true, powerTarget: Who.You));
        Assert.False(state.Enemy(id: 0)!.Powers.Has(PowerId.Artifact));
    }

    [Fact]
    public void APowerWhoseTargetIsntKnownMakesArtifactsAnswerUnknown()
    {
        // A copy aimed at a teammate (its aim may be a player of its class's own, not its Target): whether it's shown isn't known.
        var state = WithArtifact(charges: 1);
        SimEngine.AmountReceived(state, applier: Who.You, target: 0, PowerId.Weak, amount: 2, debuff: true, powerTarget: null);
        Assert.NotNull(state.Unmodelled);
    }

    [Fact]
    public void AClassesOwnVisibilityIsReadFromItsCode()
    {
        // ImpaledPower.IsVisibleInternal is LocalContext.IsMe(base.Applier): shown when you applied it (power-rules.json: ShownWhenYouApplied).
        var state = WithArtifact(charges: 1);
        Assert.True(SimModels.IsVisible(state, PowerId.Impaled, applier: Who.You, powerTarget: Who.None));
        Assert.False(SimModels.IsVisible(state, PowerId.Impaled, applier: Who.Teammate(0), powerTarget: Who.None));
        Assert.True(SimModels.IsVisible(state, PowerId.Weak, applier: Who.Teammate(0), powerTarget: Who.None));
        Assert.Null(state.Unmodelled);
    }
}
