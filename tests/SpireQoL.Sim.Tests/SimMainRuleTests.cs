using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Rules whose game code differs between Steam's branches (decision #169: SimState.MainRules, set by the adapter from the running game's
/// code): each played beta's way (no flag) and main's way (its flag), from each branch's decompile (beta v0.113.1, main v0.111.1).
/// </summary>
public class SimMainRuleTests
{
    private static SimState Board(MainRule rules, params SimEnemy[] enemies)
    {
        var state = new SimState { MainRules = rules, Enemies = enemies.ToList() };
        state.You.Hp = 50;
        return state;
    }

    [Fact]
    public void OmnislicesSplashIsPoweredOnBetaAndUnpoweredOnMain()
    {
        // Omnislice's 8 splashes onto the second enemy. Beta: a powered hit (ValueProp.Move), so your Envenom poisons it; main: unpowered,
        // so it doesn't. Thorns answers the powered splash on beta; main's ThornsPower answers the unpowered one by its own code (cardSource is
        // Omnislice), read from main's code in the game (GameBranchTests holds main's reading), not from these tests' beta models.
        SimScript Omnislice() => Play(Card("Omnislice"), target: 1, plays: 1, Attack(baseDamage: 8) with { Splash = true });
        SimState WithEnvenomAndThorns(MainRule rules)
        {
            var state = Board(rules, new SimEnemy { Id = 1, Name = "E1", Hp = 50 }, new SimEnemy { Id = 2, Name = "E2", Hp = 50, Thorns = 3 });
            state.You.Powers = state.You.Powers.With(PowerId.Envenom, amount: 2);
            return state;
        }
        var beta = Assert.Single(After(WithEnvenomAndThorns(MainRule.None), Omnislice()));
        Assert.Equal(42, beta.Enemy(id: 2)!.Hp);
        Assert.Equal(2, beta.Enemy(id: 2)!.PoisonStacks);
        Assert.Equal(3, beta.You.Taken);
        var main = Assert.Single(After(WithEnvenomAndThorns(MainRule.SplashIsUnpowered), Omnislice()));
        Assert.Equal(42, main.Enemy(id: 2)!.Hp);
        Assert.Equal(0, main.Enemy(id: 2)!.PoisonStacks);
    }

    [Fact]
    public void JugglingCopiesOnceTheThirdAttackIsPlayedOnBeta()
    {
        // The third Attack with a hand of 10 (the Attack among them) draws 1. Beta: the draw takes the one free slot, then the copy goes to
        // the discard pile (full hand). Main's JugglingPower copies it as it's played (BeforeCardPlayed), read from main's code in the game
        // (catalog HookProgramTests.JugglingCopiesWhereEachBranchsCodeDoes), where its MainRule was before.
        SimState Hand(MainRule rules)
        {
            var state = Board(rules, new SimEnemy { Id = 1, Name = "E1", Hp = 50 });
            state.You.Powers = state.You.Powers.With(PowerId.Juggling, amount: 1);
            SimEngine.SetField(state, Who.You, PowerId.Juggling.Name + ".Data.attacksPlayedThisTurn", 2);
            state.You.HandCount = 10;
            state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(new SimDrawCard("plain"), 5) } };
            return state;
        }
        SimPlay Third() => new()
        {
            Name = "PommelStrike", IsAttack = true, HandCard = 7, HandDelta = -1 + 1, Draws = 1, TargetId = 1,
            Card = new SimCardPlay(new SimCard("PommelStrike", SimCardType.Attack), Who.You, 1, PlayIndex: 0, PlayCount: 1, AutoPlay: false),
            Body = { new CardEventAction(CardEvent.Drawn, Count: 1) },
        };
        Assert.Equal(1, Assert.Single(SimEngine.Run(Hand(MainRule.None), Third()).Branches).You.Drawn);
    }
}
