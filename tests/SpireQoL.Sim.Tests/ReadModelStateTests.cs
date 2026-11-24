using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A read model's own state (decision #195): the fields and properties its code stores and reads are the branch's own (SimPlayer.Fields), part of
/// its key, and its code's tests are asked once a hook run, where the game asks them.
/// </summary>
public class ReadModelStateTests
{
    private static SimState WithRelic(RelicId relic, params (string Var, int Value)[] vars)
    {
        var state = new SimState { You = new SimPlayer { Hp = 50, HandCount = 3, Energy = 3 } };
        state.Enemies.Add(new SimEnemy { Id = 0, Name = "E0", Hp = 40 });
        state.You.Relics = state.You.Relics.With(relic, amount: 0, data: (int)RelicUse.Armed);
        foreach (var (name, value) in vars) state.Vars[$"{Who.You}:{relic.Name}:{name}"] = value;
        return state;
    }

    private static SimCardPlay Played(SimCardType type) => new(new SimCard("Played", type), Owner: Who.You, Target: 0, PlayIndex: 0, PlayCount: 1, AutoPlay: false);

    [Fact]
    public void TwoBranchesThatDifferOnlyInAModelsFieldStayApart()
    {
        var state = WithRelic(RelicId.Kunai);
        var other = state.Clone();
        other.You.Fields = other.You.Fields.With("Kunai.AttacksPlayedThisTurn", amount: 1);
        Assert.NotEqual(state.Key(), other.Key());
        var enemy = state.Clone();
        enemy.Enemies[0].Fields = enemy.Enemies[0].Fields.With("SomePower.Count", amount: 1);
        Assert.NotEqual(state.Key(), enemy.Key());
    }

    [Fact]
    public void KunaiCountsItsOwnAttacksThisTurn()
    {
        // Kunai.AfterCardPlayed: AttacksPlayedThisTurn++, then Dexterity when it's a multiple of Cards.
        var kunai = Assert.IsType<ReadModel>(SimModels.Get(RelicId.Kunai.Name));
        IEnumerable<SimState> states = new[] { WithRelic(RelicId.Kunai, ("Cards", 3), ("DexterityPower", 1)) };
        foreach (var type in new[] { SimCardType.Attack, SimCardType.Skill, SimCardType.Attack, SimCardType.Attack })
            states = states.SelectMany(branch => kunai.AfterCardPlayed(branch, Who.You, Played(type))).ToList();
        var after = Assert.Single(states);
        Assert.Equal(3, after.You.Fields["Kunai.AttacksPlayedThisTurn"]);
        Assert.Equal(1, after.You.Powers[PowerId.Dexterity]);
        Assert.Equal(1, after.Fired["Kunai"]);
    }

    [Fact]
    public void MiniRegentAsksItsFlagOnceAsTheGameDoes()
    {
        // MiniRegent.AfterStarsSpent: if (!UsedThisTurn) { UsedThisTurn = true; Apply<StrengthPower> }: the Strength after the store still
        // goes on (the test was asked before it), and only once a turn.
        var regent = Assert.IsType<ReadModel>(SimModels.Get(RelicId.MiniRegent.Name));
        var state = WithRelic(RelicId.MiniRegent, ("StrengthPower", 1));
        var first = Assert.Single(regent.AfterCardEvent(state, Who.You, CardEvent.StarsSpent, Who.You));
        Assert.Equal(1, first.You.Powers[PowerId.Strength]);
        Assert.Equal(1, first.You.Fields["MiniRegent.UsedThisTurn"]);
        var second = Assert.Single(regent.AfterCardEvent(first, Who.You, CardEvent.StarsSpent, Who.You));
        Assert.Equal(1, second.You.Powers[PowerId.Strength]);
    }
}
