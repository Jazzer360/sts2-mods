using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

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
        var state = new SimState();
        state.Enemies.AddRange(enemies);
        state.You.Powers = state.You.Powers.With(PowerId.MonarchsGaze, amount: 2);
        return state;
    }

    [Fact]
    public void EachBranchWeakensTheEnemiesItHit()
    {
        // Two random hits on two enemies attacking for 10; each hit takes 2 Strength off the enemy it lands on.
        var outcome = SimEngine.Run(Gazing(Attacker(id: 0, hp: 50, hit: 10), Attacker(id: 1, hp: 50, hit: 10)),
            Play(Card("SwordBoomerang"), target: Who.None, plays: 1, Attack(baseDamage: 3, hits: 2, mode: SimPlay.Target.Random)));
        // Both hits on one enemy: 6 + 10; one each: 8 + 8. Either way 16.
        Assert.Equal(16, outcome.TakenMax);
        Assert.Equal(16, outcome.TakenMin);
        Assert.Contains(outcome.Branches, branch => branch.Enemy(id: 0)!.Powers[PowerId.Strength] == -4 && !branch.Enemy(id: 1)!.Powers.Has(PowerId.Strength));
        Assert.Contains(outcome.Branches, branch => branch.Enemy(id: 0)!.Powers[PowerId.Strength] == -2 && branch.Enemy(id: 1)!.Powers[PowerId.Strength] == -2);
    }

    [Fact]
    public void ASingleEnemyTakesEveryHit()
    {
        var outcome = SimEngine.Run(Gazing(Attacker(id: 0, hp: 50, hit: 10)), Play(Card("SwordBoomerang"), target: Who.None, plays: 1, Attack(baseDamage: 3, hits: 3, mode: SimPlay.Target.Random)));
        Assert.Equal(4, outcome.TakenMax);
    }
}
