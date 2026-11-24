using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A read model's question to the fight's log (decision #195, round four: HookHistory): the history ledger's count for its owner's player, as a
/// card's condition reads it (decision #191); a count the ledger doesn't know makes the simulation unknown, never 0.
/// </summary>
public class ReadModelHistoryTests
{
    private static readonly HistoryQuery ExhaustedThisTurn = new(HistoryKind.CardExhausted, HistoryScope.ThisTurn, HistoryWhose.Owner);

    /// <summary>A power that gains 1 energy for each of its owner's plays once a card of theirs was exhausted this turn.</summary>
    private static ReadModel Asking() => new(new ModelSpec("TestLogPower", ModelKind.Power, new[]
    {
        new HookProgram(HookName.AfterCardPlayed, new HookStep[] { new GainEnergyStep(new HookConstant(1m)) { When = new HookHistory(ExhaustedThisTurn) { Any = true } } }),
    }));

    private static SimState Board()
    {
        var state = new SimState { You = new SimPlayer { Hp = 50, Energy = 0 } };
        state.Enemies.Add(new SimEnemy { Id = 0, Name = "E0", Hp = 30 });
        state.You.Powers = state.You.Powers.With(new PowerId("TestLogPower"), amount: 1);
        return state;
    }

    private static SimCardPlay Played() => new(new SimCard("Played", SimCardType.Attack), Owner: Who.You, Target: 0, PlayIndex: 0, PlayCount: 1, AutoPlay: false);

    [Fact]
    public void ItReadsItsOwnersCount()
    {
        var state = Board();
        state.History = new[] { ExhaustedThisTurn };
        Assert.Equal(0, Assert.Single(Asking().AfterCardPlayed(state.Clone(), Who.You, Played())).You.Energy);
        state.You.Turn = state.You.Turn.Add(ExhaustedThisTurn.Key, delta: 1);
        var after = Assert.Single(Asking().AfterCardPlayed(state, Who.You, Played()));
        Assert.Equal(1, after.You.Energy);
        Assert.Null(after.Unmodelled);
    }

    [Fact]
    public void ACountTheLedgerDoesntKnowIsUnknown()
    {
        // No seed for the question (the adapter seeds every question the board's models ask): unknown, never 0.
        var after = Assert.Single(Asking().AfterCardPlayed(Board(), Who.You, Played()));
        Assert.Equal(SimEngine.HistoryUnknown, after.Unmodelled);
    }
}
