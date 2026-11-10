using System.Text.Json;
using SpireQoL.Sim;
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
            ? Directory.GetFiles(dir, "*.json").OrderBy(f => f).Select(f => new object[] { Path.GetFileName(f) })
            : Enumerable.Empty<object[]>();
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ReplayMatchesWhatHappened(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Cases", file)));
        var root = doc.RootElement;
        var state = SimState.FromJson(root.GetProperty("state").GetRawText());
        var o = SimEngine.Run(state);
        var expect = root.GetProperty("expect");

        if (expect.TryGetProperty("takenMin", out var tmin)) Assert.Equal(tmin.GetInt32(), o.TakenMin);
        if (expect.TryGetProperty("takenMax", out var tmax)) Assert.Equal(tmax.GetInt32(), o.TakenMax);
        if (expect.TryGetProperty("deadBeforeActing", out var dead))
            foreach (var id in dead.EnumerateArray()) Assert.True(o.DeadInAll(id.GetInt32()), $"{file}: enemy {id} should die before acting in every branch");
        if (expect.TryGetProperty("aliveBeforeActing", out var alive))
            foreach (var id in alive.EnumerateArray()) Assert.False(o.DeadInAny(id.GetInt32()), $"{file}: enemy {id} should act in every branch");
        if (expect.TryGetProperty("hpAtTurnEnd", out var hp))
        {
            foreach (var p in hp.EnumerateObject())
            {
                var id = int.Parse(p.Name);
                Assert.Equal(p.Value[0].GetInt32(), o.HpAtTurnEndMin(id));
                Assert.Equal(p.Value[1].GetInt32(), o.HpAtTurnEndMax(id));
            }
        }
        if (expect.TryGetProperty("throughBlock", out var through))
        {
            foreach (var p in through.EnumerateObject())
            {
                var id = int.Parse(p.Name);
                Assert.Equal(p.Value[0].GetInt32(), o.UnblockedMin(id));
                Assert.Equal(p.Value[1].GetInt32(), o.UnblockedMax(id));
            }
        }
    }

    [Fact]
    public void ReportedStateRoundTripsThroughJson()
    {
        // What a report carries (SimState.ToJson) must load back into the same simulation.
        var s = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "A", Hp = 12, Block = 3, Hits = new() { 5, 5 }, Imbalanced = true, HitCap = 4 } } };
        s.P.Orbs = new() { new SimOrb { Kind = OrbKind.Glass, Passive = 5, Evoke = 10, GlassBase = 3, GlassBonus = 2, Triggers = 2 } };
        s.P.OrbCapacity = 3;
        s.P.Juggernaut = 2;
        s.P.Plating = 4;
        s.P.HandBlockable.Add(("Burn", 2));
        s.P.RemnantCap = 10;
        s.P.RemnantLeftPlayerTurn = 6;

        var back = SimState.FromJson(s.ToJson());
        var a = SimEngine.Run(s);
        var b = SimEngine.Run(back);
        Assert.Equal(a.TakenMax, b.TakenMax);
        Assert.Equal(a.HpAtTurnEndMax(0), b.HpAtTurnEndMax(0));
        Assert.Equal(a.UnblockedMax(0), b.UnblockedMax(0));
        Assert.Equal(a.FirstHitBlockedInAll(0), b.FirstHitBlockedInAll(0));
        Assert.Equal(s.ToJson(), back.ToJson());
    }
}
