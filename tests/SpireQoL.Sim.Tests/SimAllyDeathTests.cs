using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Enemies reacting to an ally's death (decompile v0.111.0): Ravenous (stunned, CorpseSlug) and Crab
/// Rage (Strength and block, once), as soon as the ally dies, in your play or at your end of turn.
/// Intent changes from cards (Weak, Strength loss) come from the adapter as SetHits.
/// </summary>
public class SimAllyDeathTests
{
    private static SimEnemy Enemy(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    private static SimPlay Strike(int target, int damage)
        => new() { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = target, HitDamage = { [target] = damage }, Hits = 1 };

    [Fact]
    public void KillingAnAllyStunsRavenous()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5, hits: 4), Enemy(id: 2, hp: 30, hits: 12) } };
        state.Enemies[1].Ravenous = true;
        Assert.Equal(16, SimEngine.Run(state).TakenMax);
        var outcome = SimEngine.Run(state, Strike(target: 1, damage: 5));
        Assert.Equal(0, outcome.TakenMax); // one dead, the slug stunned
        Assert.True(outcome.StunnedInAll(enemyId: 2));
    }

    [Fact]
    public void CrabRageHitsHarderAndBlocksOnceAnAllyDies()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5, hits: 4), Enemy(id: 2, hp: 30, hits: 8) } };
        state.Enemies[1].HitsAfterAllyDies = new List<int> { 14 };
        state.Enemies[1].CrabRageBlock = 6;
        SimEngine.ApplyPlayTo(state, Strike(target: 1, damage: 5), out var after, out _);
        Assert.Equal(new[] { 14 }, after[0].Enemy(id: 2)!.Hits);
        Assert.Equal(6, after[0].Enemy(id: 2)!.Block);
        Assert.Equal(14, SimEngine.Run(state, Strike(target: 1, damage: 5)).TakenMax);
    }

    [Fact]
    public void AnAllyDyingAtEndOfTurnCountsToo()
    {
        // A Lightning orb kills the 3-HP ally at end of turn: Crab Rage's hits are up when it attacks.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 3, hits: 4), Enemy(id: 2, hp: 30, hits: 8) } };
        state.Enemies[1].HitsAfterAllyDies = new List<int> { 14 };
        state.You.Orbs = new() { new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 } };
        state.You.OrbCapacity = 3;
        var outcome = SimEngine.Run(state);
        // Random target: the bolt hits the ally (dies, crab hits 14) or the crab (ally hits 4, crab 8).
        Assert.Equal(14, outcome.TakenMax);
        Assert.Equal(12, outcome.TakenMin);
    }

    [Fact]
    public void BranchesThatDifferOnlyInIntentAreNotMerged()
    {
        // The key includes hits, so the Crab Rage branch and the other survive as two branches.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 3, hits: 0), Enemy(id: 2, hp: 30, hits: 8) } };
        state.Enemies[1].HitsAfterAllyDies = new List<int> { 14 };
        var play = new SimPlay { Name = "Sword Boomerang", IsAttack = true, HitTarget = SimPlay.Target.Random, HitDamage = { [1] = 3, [2] = 0 }, Hits = 1 };
        SimEngine.ApplyPlayTo(state, play, out var branches, out _);
        Assert.Contains(branches, branch => branch.Enemy(id: 2)!.Hits.SequenceEqual(new[] { 14 }));
        Assert.Contains(branches, branch => branch.Enemy(id: 2)!.Hits.SequenceEqual(new[] { 8 }));
    }
}
