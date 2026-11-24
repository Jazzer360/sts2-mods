using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A read model's own Flash() is a step at its place in the code (decision #199): logged in the flash log (SimState.Flashed), which the play
/// check holds to the game's flashes, apart from the acting log the tabs read (SimState.Fired). A model that acts without flashing keeps its
/// tab and isn't held to a flash; one that flashes without acting is held to it.
/// </summary>
public class ReadModelFlashTests
{
    private static readonly HookValue Attack = new HookMath(HookOperation.Equal, new HookFactValue(HookFact.CardType), new HookConstant((int)SimCardType.Attack));
    private static readonly HookValue Unpowered = new HookConstant((int)SimProps.Unpowered);

    private static SimCardPlay Played(SimCardType type) => new(new SimCard("Played", type), Owner: Who.You, Target: 0, PlayIndex: 0, PlayCount: 1, AutoPlay: false);

    private static SimState Run(SimCardType type, params HookStep[] steps)
    {
        var model = new ReadModel(new ModelSpec("TestFlashPower", ModelKind.Power, new[] { new HookProgram(HookName.AfterCardPlayed, steps) }));
        var state = new SimState { You = new SimPlayer { Hp = 50 }, Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 40 } } };
        state.You.Powers = state.You.Powers.With(new PowerId("TestFlashPower"), amount: 1);
        return Assert.Single(model.AfterCardPlayed(state, Who.You, Played(type)));
    }

    [Fact]
    public void AFlashIsLoggedWhereTheCodeFlashesApartFromWhatActs()
    {
        // if (Attack) { Flash(); GainBlock(4) }: both logs.
        var both = Run(SimCardType.Attack, new FlashStep { When = Attack }, new GainBlockStep(new HookConstant(4m), Unpowered) { When = Attack });
        Assert.Equal(1, both.Flashed["TestFlashPower"]);
        Assert.True(both.Fired.Has("TestFlashPower"));
        Assert.Equal(4, both.You.Block);
    }

    [Fact]
    public void AModelThatActsWithoutFlashingKeepsItsTabButIsntHeldToAFlash()
    {
        // GainBlock(4) with no Flash() (Lunar Pastry's stars, Monologue's Strength taken back): the acting log only.
        var acted = Run(SimCardType.Attack, new GainBlockStep(new HookConstant(4m), Unpowered));
        Assert.True(acted.Fired.Has("TestFlashPower"));
        Assert.False(acted.Flashed.Has("TestFlashPower"));
    }

    [Fact]
    public void AModelThatOnlyFlashesIsHeldToTheFlash()
    {
        // if (Attack) Flash(): no tab (nothing acts), but the flash log has it on an Attack and not on a Skill.
        var attack = Run(SimCardType.Attack, new FlashStep { When = Attack });
        Assert.Equal(1, attack.Flashed["TestFlashPower"]);
        Assert.False(attack.Fired.Has("TestFlashPower"));
        Assert.False(Run(SimCardType.Skill, new FlashStep { When = Attack }).Flashed.Has("TestFlashPower"));
    }

    [Fact]
    public void TheGamesOwnFlashesAreRead()
    {
        // LunarPastry.AfterSideTurnEnd gains stars with no Flash() (it never flashes); IterationPower.AfterCardDrawn flashes before it draws.
        Assert.Empty(Assert.IsType<ReadModel>(SimModels.Get(RelicId.LunarPastry.Name)).Spec.Steps().OfType<FlashStep>());
        Assert.NotEmpty(Assert.IsType<ReadModel>(SimModels.Get(PowerId.Iteration.Name)).Spec.Steps().OfType<FlashStep>());
    }

    [Fact]
    public void FlashesMergeLikeFiringsEveryAndAny()
    {
        var once = new SimState();
        once.LogFlash("X", Who.You);
        var none = new SimState();
        once.MergeResults(none);
        Assert.False(once.Flashed.Has("X"));
        Assert.True(once.FlashedAny.Has("X"));
    }
}
