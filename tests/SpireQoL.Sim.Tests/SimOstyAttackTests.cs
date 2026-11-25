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
    public void ThornsOnOstyMeetsYourBlockFirst()
    {
        // CreatureCmd.GetDamageResults (v0.114.0; main's Damage the same): a pet's damage is blocked by its owner's block
        // (target.PetOwner?.Creature ?? target), the blockRedirect result. Thorns 3 on Osty's hit with your 2 block: your block goes, Osty loses 1.
        var state = new SimState { Enemies = { E(id: 1, hp: 30) } };
        state.Enemies[0].Thorns = 3;
        state.You.OstyHp = 10;
        state.You.Block = 2;
        SimEngine.ApplyPlayTo(state, Poke(target: 1, damage: 6, byOsty: true), out var branches, out _);
        Assert.All(branches, branch =>
        {
            Assert.Equal(9, branch.You.OstyHp);
            Assert.Equal(0, branch.You.Block);
            Assert.Equal(0, branch.Taken);
        });
    }

    [Fact]
    public void OstysTakenHitIsToldToTheDamageHooksAndTheLog()
    {
        // CreatureCmd.Damage's results loop: Osty's result (the unblocked redirect) is in the fight's log (DamageReceivedEntry: its receiver Osty)
        // and told to AfterDamageGiven and AfterDamageReceived. The player's own entry is there too, with nothing lost.
        var ostys = new HistoryQuery(HistoryKind.DamageReceived, HistoryScope.ThisTurn, HistoryWhose.OwnersOsty);
        var yours = new HistoryQuery(HistoryKind.DamageReceived, HistoryScope.ThisTurn, HistoryWhose.Owner) { Details = HistoryDetail.Unblocked };
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 30, Hits = { 4 } } }, History = new[] { ostys, yours } };
        state.You.OstyHp = 10;
        var branch = SimEngine.Run(state).Worst;
        Assert.Equal(4, branch.OstyTaken);
        Assert.Equal(1, branch.You.Turn[ostys.Key]);
        Assert.Equal(0, branch.You.Turn[yours.Key]);
    }

    [Fact]
    public void APowerOnOstyLandsNowhere()
    {
        // A hook told of Osty's result may act on its hit target (MonarchsGazePower.AfterDamageGiven: PowerCmd.Apply on him): his powers aren't
        // kept, so nothing lands, on you or on an enemy (Who.YourOsty is no one's power set).
        var state = new SimState { Enemies = { E(id: 1, hp: 30) } };
        state.You.OstyHp = 5;
        Assert.False(SimEngine.ApplyPower(state, applier: 1, target: Who.YourOsty, PowerId.Weak, amount: 1, debuff: true));
        Assert.False(state.You.Powers.Has(PowerId.Weak));
        Assert.Null(state.Unmodelled);
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
        Assert.True(SimEngine.Run(state, Poke(target: 1, damage: 6, byOsty: true)).ActedInAny(PowerId.Slippery.Name, owner: 1));
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
        // Read from its code: dealer.Monster is Osty, its pet owner your applier (its Data, as the adapter reads it), target == base.Owner:
        // OstyCmd.Summon(dealer.PetOwner, base.Amount), so Osty has the 4 HP more by the enemy turn.
        var state = new SimState { Enemies = { E(id: 1, hp: 30), E(id: 2, hp: 30) } };
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.SicEm, amount: 4, data: DataCreature.Of(Who.You));
        state.You.OstyHp = 5;
        SimEngine.ApplyPlayTo(state, Poke(target: 1, damage: 6, byOsty: true), out var summoned, out _);
        Assert.All(summoned, branch => Assert.Equal(9, branch.You.OstyHp));
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
