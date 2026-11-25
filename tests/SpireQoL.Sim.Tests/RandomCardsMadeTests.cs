using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Cards of a type made at random into a hand (decision #268: MakeRandomInHandStep, Calamity's CardFactory.GetForCombat of its owner's character's
/// Attacks): each joins the hand and is told to Hook.AfterCardGeneratedForCombat in turn, a card of that type whose class is the roll's, so a test of
/// its class is unknown, never a certainty.
/// </summary>
public class RandomCardsMadeTests
{
    private static SimState Board()
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50) } };
        state.You.Hp = 50;
        state.You.HandCount = 2;
        return state;
    }

    [Fact]
    public void CalamityMakesItsAmountOfRandomAttacksOnAnAttack()
    {
        // CalamityPower: BeforeCardPlayed binds an Attack of its owner's; AfterCardPlayed makes base.Amount Attacks into their hand.
        var state = Board();
        state.You.Powers = state.You.Powers.With(PowerId.Calamity, amount: 2);
        var after = Assert.Single(After(state, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 6))));
        Assert.Equal(4, after.You.HandCount);
        Assert.True(after.Fired.Has(PowerId.Calamity.Name));
        Assert.Null(after.Unmodelled);
        // A Skill isn't bound: nothing made.
        var skill = Assert.Single(After(Board().WithPower(PowerId.Calamity, amount: 2), Play(Card("Defend", SimCardType.Skill), target: Who.None, plays: 1, Block(baseBlock: 5))));
        Assert.Equal(2, skill.You.HandCount);
        Assert.False(skill.Fired.Has(PowerId.Calamity.Name));
    }

    [Fact]
    public void ARandomCardsClassIsUnknownToAHookThatTestsIt()
    {
        // A created-card hook asking `card is Shiv` of a card made at random: the roll decides it, so the simulation is unknown.
        var asksShiv = new HookTest("Test.AfterCardGeneratedForCombat:0001", new HookFactValue(HookFact.CardIs) { Of = CardId.Shiv.Name });
        var model = new ReadModel(new ModelSpec("TestShivPower", ModelKind.Power, new[] { new HookProgram(HookName.AfterCardGeneratedForCombat, new HookStep[] { new DrawStep(new HookConstant(1m)) { When = asksShiv } }) }));
        var state = Board().WithPower(new PowerId("TestShivPower"), amount: 1);
        state.EventCard = new SimCard("", SimCardType.Attack) { RandomClass = true };
        var after = Assert.Single(model.AfterCardEvent(state, Who.You, CardEvent.Generated, Who.You));
        Assert.NotNull(after.Unmodelled);
        // A card whose class is known is answered.
        var known = Board().WithPower(new PowerId("TestShivPower"), amount: 1);
        known.EventCard = new SimCard(CardId.Shiv.Name, SimCardType.Attack);
        Assert.Null(Assert.Single(model.AfterCardEvent(known, Who.You, CardEvent.Generated, Who.You)).Unmodelled);
    }
}

internal static class RandomCardsMadeBoards
{
    public static SimState WithPower(this SimState state, PowerId power, int amount)
    {
        state.You.Powers = state.You.Powers.With(power, amount);
        return state;
    }
}
