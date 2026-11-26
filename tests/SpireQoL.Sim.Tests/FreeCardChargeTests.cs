using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The free-card powers read from their code (decision #384): Free Attack's, Free Skill's, Free Power's and Veilpiercer's BeforeCardPlayed takes a
/// charge (PowerCmd.Decrement) for each play of their owner's card of their kind, whose pile (cardPlay.Card.Pile?.Type is Hand or Play) is the
/// play pile as its play begins (CardModel.OnPlayWrapper moves it there first), and their Late cost pass makes such a card in hand cost 0 while a
/// charge is left (SimEngine.HandCost).
/// </summary>
public class FreeCardChargeTests
{
    private static SimEnemy Enemy(int id, int hp) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = { 4 } };

    private static SimState WithPower(PowerId power, int amount)
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50) } };
        state.You.Powers = state.You.Powers.With(power, amount);
        return state;
    }

    private static SimPlay Play(string name, SimCardType type, bool ethereal = false, int playCount = 1) => new()
    {
        Name = name, IsAttack = type == SimCardType.Attack, HandDelta = -1, Plays = playCount,
        Card = new SimCardPlay(new SimCard(name, type) { Ethereal = ethereal }, Who.You, Who.None, PlayIndex: 0, PlayCount: playCount, AutoPlay: false),
    };

    private static SimHandCard InHand(SimCardType type, int cost, bool ethereal = false) => new()
    {
        Id = 3, Name = "Costed", IsAttack = type == SimCardType.Attack, Cost = cost, LocalCost = cost, Card = new SimCard("Costed", type) { Ethereal = ethereal },
    };

    [Theory]
    [InlineData(SimCardType.Attack)]
    [InlineData(SimCardType.Skill)]
    [InlineData(SimCardType.Power)]
    public void ACardOfItsKindTakesTheCharge(SimCardType kind)
    {
        var power = kind switch { SimCardType.Attack => PowerId.FreeAttack, SimCardType.Skill => PowerId.FreeSkill, _ => PowerId.FreePower };
        var other = kind == SimCardType.Skill ? SimCardType.Attack : SimCardType.Skill;
        Assert.IsType<ReadModel>(SimModels.Get(power.Name));
        var taken = Assert.Single(SimEngine.Run(WithPower(power, amount: 1), Play("Played", kind)).Branches);
        Assert.False(taken.You.Powers.Has(power));
        var kept = Assert.Single(SimEngine.Run(WithPower(power, amount: 1), Play("Played", other)).Branches);
        Assert.Equal(1, kept.You.Powers[power]);
    }

    [Fact]
    public void VeilpiercerIsTakenByAnEtherealCard()
    {
        var taken = Assert.Single(SimEngine.Run(WithPower(PowerId.Veilpiercer, amount: 2), Play("Played", SimCardType.Skill, ethereal: true)).Branches);
        Assert.Equal(1, taken.You.Powers[PowerId.Veilpiercer]);
        var kept = Assert.Single(SimEngine.Run(WithPower(PowerId.Veilpiercer, amount: 2), Play("Played", SimCardType.Skill)).Branches);
        Assert.Equal(2, kept.You.Powers[PowerId.Veilpiercer]);
    }

    [Fact]
    public void EachPlayOfAReplayedCardTakesACharge()
    {
        // Hook.BeforeCardPlayed runs once per play (CardModel.OnPlayWrapper's loop): a card played twice takes two charges.
        var branch = Assert.Single(SimEngine.Run(WithPower(PowerId.FreeAttack, amount: 3), Play("Played", SimCardType.Attack, playCount: 2)).Branches);
        Assert.Equal(1, branch.You.Powers[PowerId.FreeAttack]);
    }

    [Fact]
    public void ACardOfItsKindInHandCostsNothingWhileAChargeIsLeft()
    {
        // TryModifyEnergyCostInCombatLate: its owner's Attack in hand or being played costs 0; the power gone with its last charge, it costs its own.
        var state = WithPower(PowerId.FreeAttack, amount: 1);
        Assert.Equal(0, SimEngine.HandCost(state, Who.You, InHand(SimCardType.Attack, cost: 2)));
        Assert.Equal(2, SimEngine.HandCost(state, Who.You, InHand(SimCardType.Skill, cost: 2)));
        var after = Assert.Single(SimEngine.Run(state, Play("Played", SimCardType.Attack)).Branches);
        Assert.Equal(2, SimEngine.HandCost(after, Who.You, InHand(SimCardType.Attack, cost: 2)));
        Assert.Equal(0, SimEngine.HandCost(WithPower(PowerId.Veilpiercer, amount: 1), Who.You, InHand(SimCardType.Skill, cost: 1, ethereal: true)));
    }
}
