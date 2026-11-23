using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Regen heals its owner at the end of their side's turn (RegenPower.BeforeSideTurnEndEarly: before the enemies act), up to max HP
/// (CreatureCmd.Heal), and ticks down. Void takes its energy when it's drawn (its own AfterCardDrawn: PlayerCmd.LoseEnergy).
/// </summary>
public class SimRegenAndVoidTests
{
    private static SimState Facing(int hit, int hp, int maxHp, int regen)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 50, Hits = new() { hit } } } };
        state.You.Hp = hp;
        state.You.MaxHp = maxHp;
        if (regen > 0) state.You.Powers = state.You.Powers.With(PowerId.Regen, amount: regen);
        return state;
    }

    [Fact]
    public void RegenHealsBeforeTheEnemyHits()
    {
        // 5 HP, an 8 coming: Regen 4 heals to 9 first, so you live on 1; without it you die.
        var regen = Assert.Single(SimEngine.Run(Facing(hit: 8, hp: 5, maxHp: 50, regen: 4)).Branches);
        Assert.False(regen.You.Died);
        Assert.Equal(1, regen.You.Hp);
        Assert.Equal(3, regen.You.Powers[PowerId.Regen]);
        Assert.True(Assert.Single(SimEngine.Run(Facing(hit: 8, hp: 5, maxHp: 50, regen: 0)).Branches).You.Died);
    }

    [Fact]
    public void RegenStopsAtMaxHp()
    {
        var branch = Assert.Single(SimEngine.Run(Facing(hit: 0, hp: 48, maxHp: 50, regen: 4)).Branches);
        Assert.Equal(50, branch.You.Hp);
    }

    [Fact]
    public void DrawingAVoidTakesItsEnergy()
    {
        // A draw of 1 from a Void and a Skill, with 2 energy: half the time it's the Void, and you're left with 1.
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 50 } } };
        state.You.Energy = 2;
        state.You.HandCount = 3;
        state.You.Cards = state.You.Cards.With(CardId.Void, amount: 1); // its own AfterCardDrawn (VoidCard)
        state.You.Draw = new SimDrawPile
        {
            Rest = { new SimDrawGroup(new SimDrawCard("void") { Type = SimCardType.Status, EnergyLostOnDraw = 1 }, Count: 1), new SimDrawGroup(new SimDrawCard("skill") { Type = SimCardType.Skill }, Count: 1) },
        };
        var outcome = SimEngine.Run(state, new SimPlay { Name = "Draw1", HandCard = 7, HandDelta = 0, Draws = 1, Body = { new CardEventAction(CardEvent.Drawn, Count: 1) } });
        Assert.Equal(0.5, outcome.Branches.Where(branch => branch.You.Energy == 1).Sum(branch => branch.Weight), precision: 9);
        Assert.Equal(0.5, outcome.Branches.Where(branch => branch.You.Energy == 2).Sum(branch => branch.Weight), precision: 9);
    }
}
