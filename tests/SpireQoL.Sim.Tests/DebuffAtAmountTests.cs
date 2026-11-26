using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A power lands as a debuff by its type at the amount given (PowerModel.GetTypeForAmount: SimModels.IsDebuffAt), as PowerCmd tells its listeners:
/// a counter that can go below 0 given less than 0 is a debuff (Strength -2), a debuff that can't go below 0 given less than 0 is a buff (Vulnerable
/// taken down by 1), anything else its type. Sleight of Flesh hears it (power.GetTypeForAmount(amount) == Debuff, read from its code): it deals its
/// amount to the enemy a debuff of its owner's lands on.
/// </summary>
public class DebuffAtAmountTests
{
    private static SimState WithSleightOfFlesh()
    {
        var state = new SimState { You = new SimPlayer { Hp = 50 }, Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 40 } } };
        state.You.Powers = state.You.Powers.With(PowerId.SleightOfFlesh, amount: 3);
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.Vulnerable, amount: 2);
        return state;
    }

    private static SimCardPlay Played() => new(new SimCard("Played", SimCardType.Skill), Owner: Who.You, Target: 0, PlayIndex: 0, PlayCount: 1, AutoPlay: false);

    /// <summary>A read power whose AfterCardPlayed applies <paramref name="amount"/> of <paramref name="power"/> to every enemy, read as a debuff.</summary>
    private static SimState AfterReadStep(PowerId power, int amount)
    {
        var step = new ApplyPowerStep(power.Name, HookTarget.AllEnemies, new HookConstant(amount)) { Debuff = true };
        var model = new ReadModel(new ModelSpec("TestTakingPower", ModelKind.Power, new[] { new HookProgram(HookName.AfterCardPlayed, new HookStep[] { step }) }));
        var state = WithSleightOfFlesh();
        state.You.Powers = state.You.Powers.With(new PowerId("TestTakingPower"), amount: 1);
        return Assert.Single(model.AfterCardPlayed(state, Who.You, Played()));
    }

    [Fact]
    public void AReadStepsDebuffTakenDownIsABuff()
    {
        // VulnerablePower can't go below 0: -1 of it is a buff (GetTypeForAmount), so Sleight of Flesh doesn't hear a debuff; +1 is one.
        var takenDown = AfterReadStep(PowerId.Vulnerable, amount: -1);
        Assert.Equal(1, takenDown.Enemy(id: 0)!.Powers[PowerId.Vulnerable]);
        Assert.Equal(40, takenDown.Enemy(id: 0)!.Hp);
        Assert.Equal(37, AfterReadStep(PowerId.Vulnerable, amount: 1).Enemy(id: 0)!.Hp);
    }

    [Fact]
    public void AReadStepsCounterBelowZeroIsADebuff()
    {
        // StrengthPower is a counter that can go below 0: -2 of it is a debuff, whatever the step was read as.
        Assert.Equal(37, AfterReadStep(PowerId.Strength, amount: -2).Enemy(id: 0)!.Hp);
    }

    [Fact]
    public void ACardsDebuffTakenDownIsABuff()
    {
        // A card's application read as a debuff on its target (its type) given less than 0: a buff at that amount.
        var play = new SimPlay
        {
            Name = "Played", HandDelta = -1, TargetId = 0, Card = Played(),
            Body = { new ApplyPowerAction(PowerId.Vulnerable, Amount: -1) { Debuff = true } },
        };
        SimEngine.ApplyPlayTo(WithSleightOfFlesh(), play, out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(1, branch.Enemy(id: 0)!.Powers[PowerId.Vulnerable]);
        Assert.Equal(40, branch.Enemy(id: 0)!.Hp);
    }

    [Fact]
    public void AnAimedCopyTakenDownIsTypedAtTheOffset()
    {
        // PowerCmd.ModifyAmount tells its listeners of the offset: the enemy's Vulnerable aimed at you taken down by 1 is a buff (no Sleight of
        // Flesh), raised by 1 a debuff, whatever the copy held before.
        SimState Aimed()
        {
            var state = WithSleightOfFlesh();
            state.Enemies[0].Fields = state.Enemies[0].Fields.With(SimKeys.Aimed(PowerId.Vulnerable.Name, Who.You), 2);
            return state;
        }
        var takenDown = Assert.Single(SimEngine.ModifyAimedCopy(Aimed(), holder: 0, PowerId.Vulnerable, Who.You, offset: -1, applier: Who.You));
        Assert.Equal(1, SimEngine.AimedAmount(takenDown, holder: 0, PowerId.Vulnerable, Who.You));
        Assert.Equal(40, takenDown.Enemy(id: 0)!.Hp);
        var raised = Assert.Single(SimEngine.ModifyAimedCopy(Aimed(), holder: 0, PowerId.Vulnerable, Who.You, offset: 1, applier: Who.You));
        Assert.Equal(37, raised.Enemy(id: 0)!.Hp);
    }
}
