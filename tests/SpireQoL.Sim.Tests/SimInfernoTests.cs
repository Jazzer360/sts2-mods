using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Inferno (InfernoPower.AfterDamageReceived): each time you lose HP on your turn (UnblockedDamage &gt; 0 while it's your
/// side's turn), every hittable enemy takes its amount, unpowered (their block first).
/// </summary>
public class SimInfernoTests
{
    private static SimEnemy E(int id, int hp, int block = 0, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Block = block, Hits = hits.ToList() };

    [Fact]
    public void RegretAtEndOfTurnSetsOffInfernoOnEveryEnemy()
    {
        // Regret with 2 cards in hand: 2 HP lost on your turn, so 6 to each enemy (E2's 4 block takes some).
        var state = new SimState { Enemies = { E(id: 1, hp: 30), E(id: 2, hp: 30, block: 4) } };
        state.You.TurnEndCards.Add(new TurnEndCard("Regret", "Regret", Amount: 0));
        state.You.HandCount = 2;
        state.You.Inferno = 6;
        var outcome = SimEngine.Run(state);
        Assert.Equal(24, outcome.Worst.Enemy(id: 1)!.HpAtTurnEnd);
        Assert.Equal(28, outcome.Worst.Enemy(id: 2)!.HpAtTurnEnd);
        Assert.Equal(2, outcome.TakenMax);
    }

    [Fact]
    public void EachBurnThatGetsThroughSetsItOffAgain()
    {
        // Two Burns of 2 with 3 block: the first is blocked (no HP lost), the second loses 1 HP: one Inferno.
        var state = new SimState { Enemies = { E(id: 1, hp: 30) } };
        state.You.Block = 3;
        state.You.TurnEndCards.Add(new TurnEndCard("Burn", "Burn", Amount: 2));
        state.You.TurnEndCards.Add(new TurnEndCard("Burn", "Burn", Amount: 2));
        state.You.Inferno = 6;
        var outcome = SimEngine.Run(state);
        Assert.Equal(24, outcome.Worst.Enemy(id: 1)!.HpAtTurnEnd);
    }

    [Fact]
    public void InfernoCanKillBeforeTheEnemyActs()
    {
        var state = new SimState { Enemies = { E(id: 1, hp: 5, block: 0, hits: 10) } };
        state.You.TurnEndCards.Add(new TurnEndCard("Regret", "Regret", Amount: 0));
        state.You.HandCount = 1;
        state.You.Inferno = 6;
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.Worst.Enemy(id: 1)!.DiedBeforeActing);
        Assert.Equal(1, outcome.TakenMax); // Regret only
    }

    [Fact]
    public void DamageToYouDuringAPlaySetsItOff()
    {
        // Galvanic's 6 with no block: HP lost on your turn.
        var state = new SimState { Enemies = { E(id: 1, hp: 30) } };
        state.You.Inferno = 4;
        var play = new SimPlay { DamageToYou = { ("GalvanicPower", 6) } };
        SimEngine.ApplyPlayTo(state, play, out var branches, out _);
        Assert.Equal(26, branches[0].Enemy(id: 1)!.Hp);
    }

    [Fact]
    public void NoInfernoWhenNothingGetsThrough()
    {
        var state = new SimState { Enemies = { E(id: 1, hp: 30) } };
        state.You.Block = 10;
        state.You.TurnEndCards.Add(new TurnEndCard("Burn", "Burn", Amount: 2));
        state.You.Inferno = 6;
        var outcome = SimEngine.Run(state);
        Assert.Equal(30, outcome.Worst.Enemy(id: 1)!.HpAtTurnEnd);
    }
}
