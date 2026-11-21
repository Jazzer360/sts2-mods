using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Attacks Osty makes for your cards (DamageCmd.Attack(...).FromOsty: Bone Shards, Poke, Squeeze...): enemy Thorns hits the
/// dealer, Osty, not you; your Envenom (EnvenomPower.AfterDamageGiven: you as the dealer) doesn't apply; the enemy reacts
/// as to any card hit (Curl Up, Slippery).
/// </summary>
public class SimOstyAttackTests
{
    private static SimEnemy E(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    private static SimPlay Poke(int target, int damage, bool byOsty) => new()
    {
        Name = "Poke", HitTarget = SimPlay.Target.Single, TargetId = target, Hits = 1, HitDamage = { [target] = damage }, ByOsty = byOsty,
    };

    [Fact]
    public void ThornsHitsOstyNotYou()
    {
        var state = new SimState { Enemies = { E(id: 1, hp: 30) } };
        state.Enemies[0].Thorns = 3;
        state.You.OstyHp = 10;
        SimEngine.ApplyPlayTo(state, Poke(target: 1, damage: 6, byOsty: true), out var branches, out _);
        Assert.All(branches, branch =>
        {
            Assert.Equal(24, branch.Enemy(id: 1)!.Hp);
            Assert.Equal(7, branch.You.OstyHp);
            Assert.Equal(0, branch.Taken);
        });
        // Your own attack takes the Thorns yourself.
        SimEngine.ApplyPlayTo(state, Poke(target: 1, damage: 6, byOsty: false), out var mine, out _);
        Assert.All(mine, branch => Assert.Equal(3, branch.Taken));
    }

    [Fact]
    public void YourEnvenomDoesntApply()
    {
        var state = new SimState { Enemies = { E(id: 1, hp: 30) } };
        state.You.Envenom = 2;
        SimEngine.ApplyPlayTo(state, Poke(target: 1, damage: 6, byOsty: true), out var branches, out _);
        Assert.All(branches, branch => Assert.Equal(0, branch.Enemy(id: 1)!.Poison));
    }

    [Fact]
    public void SlipperyReactsToOstysHit()
    {
        var state = new SimState { Enemies = { E(id: 1, hp: 30) } };
        state.Enemies[0].Slippery = 1;
        Assert.True(SimEngine.Run(state, Poke(target: 1, damage: 6, byOsty: true)).SlipperyInAny(enemyId: 1));
    }

    [Fact]
    public void NecroMasteryHitsEveryEnemyThroughBlockWhenOstyDies()
    {
        // Bone Shards kills Osty at 4 HP with Necro Mastery 2: 8 unblockable damage to every enemy.
        var state = new SimState { Enemies = { E(id: 1, hp: 30), E(id: 2, hp: 5) } };
        state.Enemies[0].Block = 20;
        state.You.OstyHp = 4;
        state.You.Powers = state.You.Powers.With(PowerId.NecroMastery, amount: 2);
        var play = new SimPlay { Name = "BoneShards", KillsOsty = true };
        SimEngine.ApplyPlayTo(state, play, out var branches, out _);
        Assert.All(branches, branch =>
        {
            Assert.Equal(22, branch.Enemy(id: 1)!.Hp);
            Assert.Equal(20, branch.Enemy(id: 1)!.Block);
            Assert.False(branch.Enemy(id: 2)!.Alive);
        });
    }

    [Fact]
    public void SicEmSummonsWhenOstyDamagesItsEnemyNotWhenYouDo()
    {
        // SicEmPower.AfterDamageGiven (a dev-mode report: High Five's Osty hit on a Thieving Hopper with Sic 'Em 4 summoned 4).
        var state = new SimState { Enemies = { E(id: 1, hp: 30), E(id: 2, hp: 30) } };
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.SicEm, amount: 4);
        var byOsty = SimEngine.Run(state, Poke(target: 1, damage: 6, byOsty: true));
        Assert.All(byOsty.Branches, branch => { Assert.Equal(1, branch.Fired["SicEmPower"]); Assert.Equal(4, branch.Fired.DataOf("SicEmPower")); });
        // Your own hit on it, and Osty's on another enemy: nothing.
        Assert.All(SimEngine.Run(state, Poke(target: 1, damage: 6, byOsty: false)).Branches, branch => Assert.False(branch.Fired.Has("SicEmPower")));
        Assert.All(SimEngine.Run(state, Poke(target: 2, damage: 6, byOsty: true)).Branches, branch => Assert.False(branch.Fired.Has("SicEmPower")));
    }

    [Fact]
    public void NecroMasterysHitsInTheEnemyTurnAreNotYourEndOfTurns()
    {
        // A dev-mode report (the Knowledge Demon): its 9 hit Osty in its turn, and Necro Mastery hit it back for 9. That hit through its
        // block is its turn's, not your end of turn's: the outcome audit counts your end of turn's (UnblockedAtTurnEnd), the wake
        // rules every hit (UnblockedInstances).
        var state = new SimState { Enemies = { E(id: 1, hp: 100, hits: 9) } };
        state.You.OstyHp = 20;
        state.You.OstyShields = true;
        state.You.Powers = state.You.Powers.With(PowerId.NecroMastery, amount: 1);
        var outcome = SimEngine.Run(state);
        Assert.Equal(0, outcome.UnblockedAtTurnEndMax(enemyId: 1));
        Assert.Equal(1, outcome.UnblockedMin(enemyId: 1));
        Assert.Equal(100, outcome.HpAtTurnEndMin(enemyId: 1));
    }
}
