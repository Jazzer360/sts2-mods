using Xunit;
using static SpireQoL.Sim.Tests.V2.V2Script;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Monarch's Gaze (decompile v0.113.1): each of your powered attack hits takes Strength from the enemy it hits
/// (MonarchsGazePower.AfterDamageGiven), so that enemy's intent hits for less. On a random-target attack, which enemy loses how
/// much is a branch.
/// </summary>
public class SimMonarchsGazeTests
{
    private static SimEnemy Attacker(int id, int hp, int hit) =>
        new() { Id = id, Name = $"E{id}", Hp = hp, Hits = new() { hit }, Intent = new List<IntentHit> { new(hit) } };

    private static SimState Gazing(params SimEnemy[] enemies)
    {
        var s = new SimState();
        s.Enemies.AddRange(enemies);
        s.P.Powers = s.P.Powers.With("MonarchsGazePower", 2);
        return s;
    }

    [Fact]
    public void EachBranchWeakensTheEnemiesItHit()
    {
        // Two random hits on two enemies attacking for 10; each hit takes 2 Strength off the enemy it lands on.
        var o = SimEngine.Run(Gazing(Attacker(0, 50, 10), Attacker(1, 50, 10)),
            Play(Card("SwordBoomerang"), -1, 1, Attack(3, hits: 2, mode: SimPlay.Target.Random)));
        // Both hits on one enemy: 6 + 10; one each: 8 + 8. Either way 16.
        Assert.Equal(16, o.TakenMax);
        Assert.Equal(16, o.TakenMin);
        Assert.Contains(o.Branches, b => b.Enemy(0)!.Powers["StrengthPower"] == -4 && !b.Enemy(1)!.Powers.Has("StrengthPower"));
        Assert.Contains(o.Branches, b => b.Enemy(0)!.Powers["StrengthPower"] == -2 && b.Enemy(1)!.Powers["StrengthPower"] == -2);
    }

    [Fact]
    public void ASingleEnemyTakesEveryHit()
    {
        var o = SimEngine.Run(Gazing(Attacker(0, 50, 10)), Play(Card("SwordBoomerang"), -1, 1, Attack(3, hits: 3, mode: SimPlay.Target.Random)));
        Assert.Equal(4, o.TakenMax);
    }
}
