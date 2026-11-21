using System.Text.Json;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Replays recorded situations (Cases/*.json): a simulator start state, usually copied from an error
/// report's "Simulator start state", and what actually happened in the game. Each case reruns the
/// simulator offline and checks that what happened is something it allows:
///  - the replay fails: the simulator's rules are wrong for this situation (fix the rule; the case
///    stays as the regression test);
///  - the replay passes but the game reported a mismatch: the adapter (reading the game) or the audit's
///    comparison is wrong, not the simulator.
/// Expectation fields (all optional): takenMin/takenMax (HP you lose), deadBeforeActing and
/// aliveBeforeActing (enemy ids, every branch), hpAtTurnEnd ({ id: [min, max] }), throughBlock
/// ({ id: [min, max] }).
/// </summary>
public class ReplayTests
{
    public static IEnumerable<object[]> Cases()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Cases");
        return Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*.json").Order().Select(file => new object[] { Path.GetFileName(file) })
            : Enumerable.Empty<object[]>();
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ReplayMatchesWhatHappened(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Cases", file)));
        var root = doc.RootElement;
        var state = SimState.FromJson(root.GetProperty("state").GetRawText());
        var outcome = SimEngine.Run(state);
        var expect = root.GetProperty("expect");

        if (expect.TryGetProperty("takenMin", out var tmin)) Assert.Equal(tmin.GetInt32(), outcome.TakenMin);
        if (expect.TryGetProperty("takenMax", out var tmax)) Assert.Equal(tmax.GetInt32(), outcome.TakenMax);
        if (expect.TryGetProperty("deadBeforeActing", out var dead))
            foreach (var id in dead.EnumerateArray()) Assert.True(outcome.DeadInAll(id.GetInt32()), $"{file}: enemy {id} should die before acting in every branch");
        if (expect.TryGetProperty("aliveBeforeActing", out var alive))
            foreach (var id in alive.EnumerateArray()) Assert.False(outcome.DeadInAny(id.GetInt32()), $"{file}: enemy {id} should act in every branch");
        if (expect.TryGetProperty("hpAtTurnEnd", out var jsonElement))
        {
            foreach (var jsonProperty in jsonElement.EnumerateObject())
            {
                var id = int.Parse(jsonProperty.Name);
                Assert.Equal(jsonProperty.Value[0].GetInt32(), outcome.HpAtTurnEndMin(id));
                Assert.Equal(jsonProperty.Value[1].GetInt32(), outcome.HpAtTurnEndMax(id));
            }
        }
        if (expect.TryGetProperty("throughBlock", out var through))
        {
            foreach (var jsonProperty in through.EnumerateObject())
            {
                var id = int.Parse(jsonProperty.Name);
                Assert.Equal(jsonProperty.Value[0].GetInt32(), outcome.UnblockedMin(id));
                Assert.Equal(jsonProperty.Value[1].GetInt32(), outcome.UnblockedMax(id));
            }
        }
    }

    [Fact]
    public void ReportedStateRoundTripsThroughJson()
    {
        // What a report carries (SimState.ToJson) must load back into the same simulation.
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "A", Hp = 12, Block = 3, Hits = new() { 5, 5 }, Imbalanced = true, HitCap = 4 } } };
        state.You.Orbs = new() { new SimOrb { Kind = OrbKind.Glass, Passive = 5, Evoke = 10, GlassBase = 3, GlassBonus = 2, Triggers = 2 } };
        state.You.OrbCapacity = 3;
        state.You.Juggernaut = 2;
        state.You.Plating = 4;
        state.You.TurnEndCards.Add(new TurnEndCard("Burn", "Burn", Amount: 2));
        state.You.RemnantCap = 10;
        state.You.RemnantLeftPlayerTurn = 6;

        var back = SimState.FromJson(state.ToJson());
        var outcome = SimEngine.Run(state);
        var simOutcome = SimEngine.Run(back);
        Assert.Equal(outcome.TakenMax, simOutcome.TakenMax);
        Assert.Equal(outcome.HpAtTurnEndMax(enemyId: 0), simOutcome.HpAtTurnEndMax(enemyId: 0));
        Assert.Equal(outcome.UnblockedMax(enemyId: 0), simOutcome.UnblockedMax(enemyId: 0));
        Assert.Equal(outcome.FirstHitBlockedInAll(enemyId: 0), simOutcome.FirstHitBlockedInAll(enemyId: 0));
        Assert.Equal(state.ToJson(), back.ToJson());
    }
}
