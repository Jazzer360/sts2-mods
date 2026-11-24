using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A made card's event carries the card (Hook.AfterCardGeneratedForCombat's card: its class and type), however it's made: a hook tests the
/// card's facts (its id, its type), never a kind of event for one card. All Is Well asks `card is Sadness`, Smokestack `card.Type == Status`.
/// </summary>
public class MadeCardEventTests
{
    private static readonly SimCard Sadness = new(CardId.Sadness.Name, SimCardType.Status);
    private static readonly SimCard Dazed = new(CardId.Dazed.Name, SimCardType.Status);
    private static readonly SimCard Shiv = new(CardId.Shiv.Name, SimCardType.Attack);

    private static SimState State()
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 100) } };
        state.You.HandCount = 2;
        return state;
    }

    private static SimState Power(SimState state, PowerId power, int amount)
    {
        state.You.Powers = state.You.Powers.With(power, amount);
        return state;
    }

    private static SimState After(SimState state, params SimAction[] body) =>
        Assert.Single(ScriptBuilder.After(state, Play(Card("Maker", SimCardType.Skill), target: Who.None, plays: 1, body)));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMadeCardIsHeardByItsIdAndType(bool joinsHand)
    {
        // Made into your hand (one at a time, each taking a slot) or anywhere else: the hooks hear the card either way.
        var state = Power(Power(Power(State(), PowerId.AllIsWell, amount: 4), PowerId.Smokestack, amount: 3), PowerId.PillarOfCreation, amount: 2);
        var after = After(state,
            new CardEventAction(CardEvent.Generated, Count: 1) { Card = Sadness, JoinsHand = joinsHand, Indirect = true },
            new CardEventAction(CardEvent.Generated, Count: 1) { Card = Dazed, JoinsHand = joinsHand, Indirect = true },
            new CardEventAction(CardEvent.Generated, Count: 1) { Card = Shiv, JoinsHand = joinsHand, Indirect = true });
        Assert.Equal(3 * 2 + 4, after.You.Block); // Pillar of Creation on each, All Is Well on the Sadness
        Assert.Equal(94, after.Enemy(id: 0)!.Hp);  // Smokestack on the Dazed and the Sadness: both Statuses (Sadness: base(-1, CardType.Status, ...))
        Assert.Equal(3, after.PlayEvents[CardEvent.Generated]);
    }

    [Fact]
    public void AMadeCardOfAClassTheCodeDoesntNameIsNoneOfThem()
    {
        // A card made by a roll (no class read): not the played card, so neither a Sadness nor a Status, whatever was played.
        var state = Power(Power(State(), PowerId.AllIsWell, amount: 4), PowerId.Smokestack, amount: 3);
        var after = Assert.Single(ScriptBuilder.After(state, Play(Card("Status maker", SimCardType.Status), target: Who.None, plays: 1,
            new CardEventAction(CardEvent.Generated, Count: 1) { Indirect = true })));
        Assert.Equal(0, after.You.Block);
        Assert.Equal(100, after.Enemy(id: 0)!.Hp);
    }
}
