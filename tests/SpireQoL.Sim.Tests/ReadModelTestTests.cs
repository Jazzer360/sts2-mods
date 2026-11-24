using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// When a read model asks its code's tests (decision #195, HookTest): once at its place a hook run, as the game asks it once at its branch, so
/// the steps an if guards all run on the one answer, even when the first changes what it tests; and in a counter loop's body once a round, as
/// the game goes through the body's branch again each round.
/// </summary>
public class ReadModelTestTests
{
    private static readonly HookValue NoBlock = new HookTest("Loop.Body:0010", new HookMath(HookOperation.Equal, new HookFactValue(HookFact.OwnerBlock), new HookConstant(0m)));

    private static SimState Board() => new() { You = new SimPlayer { Hp = 50, HandCount = 3, Energy = 3 }, Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 40 } } };

    private static SimCardPlay Played() => new(new SimCard("Played", SimCardType.Skill), Owner: Who.You, Target: 0, PlayIndex: 0, PlayCount: 1, AutoPlay: false);

    /// <summary>A power of yours whose AfterCardPlayed is <paramref name="steps"/>, on the board at 1.</summary>
    private static (ReadModel Model, SimState State) Power(params HookStep[] steps)
    {
        var model = new ReadModel(new ModelSpec("TestLoopPower", ModelKind.Power, new[] { new HookProgram(HookName.AfterCardPlayed, steps) }));
        var state = Board();
        state.You.Powers = state.You.Powers.With(new PowerId("TestLoopPower"), amount: 1);
        return (model, state);
    }

    private static readonly HookValue Unpowered = new HookConstant((int)SimProps.Unpowered);

    [Fact]
    public void AnIfsStepsRunOnOneAnswer()
    {
        // if (base.Owner.Block == 0) { GainBlock(5); GainEnergy(1); }: the block doesn't stop the energy (the test was asked once, before it).
        var (model, state) = Power(new GainBlockStep(new HookConstant(5m), Unpowered) { When = NoBlock }, new GainEnergyStep(new HookConstant(1m)) { When = NoBlock });
        var after = Assert.Single(model.AfterCardPlayed(state, Who.You, Played()));
        Assert.Equal(5, after.You.Block);
        Assert.Equal(4, after.You.Energy);
    }

    [Fact]
    public void ACounterLoopsBodyAsksItsTestEachRound()
    {
        // for (i = 0; i < 2; i++) { if (base.Owner.Block == 0) { GainBlock(5); GainEnergy(1); } }: the first round gains both; the second
        // finds the block and gains neither.
        var body = new HookStep[] { new GainBlockStep(new HookConstant(5m), Unpowered) { When = NoBlock }, new GainEnergyStep(new HookConstant(1m)) { When = NoBlock } };
        var (model, state) = Power(new RepeatStep(new HookConstant(2m), body));
        var after = Assert.Single(model.AfterCardPlayed(state, Who.You, Played()));
        Assert.Equal(5, after.You.Block);
        Assert.Equal(4, after.You.Energy);
    }

    [Fact]
    public void ACounterLoopsBodyIsAskedAgainWhenTheHookStoresToo()
    {
        // The same loop in a hook that also keeps a count of its own (its tests asked once a run): still once a round, not once a run.
        var body = new HookStep[] { new GainBlockStep(new HookConstant(5m), Unpowered) { When = NoBlock }, new GainEnergyStep(new HookConstant(1m)) { When = NoBlock } };
        var (model, state) = Power(new StoreFieldStep("Count", new HookMath(HookOperation.Add, new HookField("Count"), new HookConstant(1m))), new RepeatStep(new HookConstant(2m), body));
        var after = Assert.Single(model.AfterCardPlayed(state, Who.You, Played()));
        Assert.Equal(5, after.You.Block);
        Assert.Equal(4, after.You.Energy);
        Assert.Equal(1, after.You.Fields["TestLoopPower.Count"]);
    }
}

/// <summary>
/// An amount per card in its owner's hand (its code's cards.Count * value) is named with the count in Incoming Damage's breakdown, as "Cloak
/// Clasp ×3": the read model's own value, as the hand-written model named it.
/// </summary>
public class PerCardLabelTests
{
    [Fact]
    public void CloakClaspsBlockIsNamedWithTheCardsItCounts()
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50, Hits = new List<int> { 10 } } }, Log = true };
        state.You.HandCount = 3;
        state.WithRelic(Who.You, RelicId.CloakClasp, "Block", value: 2);
        state.Titles[$"{Who.You}:{RelicId.CloakClasp.Name}"] = "Cloak Clasp";
        var branch = SimEngine.Run(state).Worst;
        Assert.Contains(("Cloak Clasp ×3", 6), branch.EndOfTurnBlock);
    }

    [Fact]
    public void ObsidianVicesDamageIsNamedWithTheCardsItCounts()
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50, Hits = new List<int> { 10 } } }, Log = true };
        state.You.HandCount = 4;
        state.WithRelic(Who.You, RelicId.ObsidianVice, "Damage", value: 1);
        state.Titles[$"{Who.You}:{RelicId.ObsidianVice.Name}"] = "Obsidian Vice";
        var branch = SimEngine.Run(state).Worst;
        Assert.Contains(("Obsidian Vice ×4", 4), branch.EndOfTurnDamage);
    }
}
