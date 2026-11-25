using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A player's pets are creatures with their own powers (decision #265): Wuppy's Loyalty and Die For You read from their code
/// (WuppysLoyaltyPower.ModifyPreBlockDamageTarget, DieForYouPower.ModifyUnblockedDamageTarget), listening at the pet's place, and each pet's own
/// result of a hit it takes in the fight's log and told to the damage hooks, as CreatureCmd.Damage's results loop tells each (beta's
/// DamageResults.AllResults: the player's, the pre-block redirect's, the unblocked redirect's).
/// </summary>
public class PetRedirectTests
{
    private static SimEnemy Attacker(int hits, int strengthDown = 0)
    {
        var enemy = new SimEnemy { Id = 0, Name = "E0", Hp = 50, Hits = { hits } };
        if (strengthDown > 0) enemy.Powers = enemy.Powers.With(PowerId.MonarchsGaze, strengthDown);
        return enemy;
    }

    [Fact]
    public void WuppysResultIsInTheFightsLog()
    {
        // CombatHistory.DamageReceived of each result: Wuppy's (it lost 7) as well as yours (0 got through). A question of anyone's damage that got
        // through counts Wuppy's.
        var anyone = new HistoryQuery(HistoryKind.DamageReceived, HistoryScope.ThisFight, HistoryWhose.Anyone) { Details = HistoryDetail.Unblocked };
        var state = new SimState { Enemies = { Attacker(hits: 7) }, History = new List<HistoryQuery> { anyone } };
        state.You.WithWuppy(hp: 10);
        var branch = Assert.Single(SimEngine.Run(state).Branches);
        Assert.Equal(1, branch.You.Fight[anyone.Key]);
    }

    [Fact]
    public void ATeammatesOstysResultIsInTheirLog()
    {
        // Their Osty's DamageReceivedEntry: its receiver their Osty (e.Receiver == Owner.Osty asked of their ledger), as yours is in yours.
        var ostys = new HistoryQuery(HistoryKind.DamageReceived, HistoryScope.ThisFight, HistoryWhose.OwnersOsty);
        var state = new SimState { Enemies = { Attacker(hits: 5) }, History = new List<HistoryQuery> { ostys } };
        state.Enemies[0].TeammateHits = new() { new() { 5 } };
        state.Teammates.Add(new SimPlayer { Hp = 30 }.WithOsty(hp: 8));
        var branch = Assert.Single(SimEngine.Run(state).Branches);
        Assert.Equal(1, branch.Teammates[0].Fight[ostys.Key]);
        Assert.Equal(0, branch.You.Fight[ostys.Key]);
    }

    [Fact]
    public void WuppysResultIsToldToTheDamageHooks()
    {
        // MonarchsGazePower.AfterDamageGiven (read from its code) of each result of its owner's powered attack: its Strength down lands on you and on
        // Wuppy, which took the hit (its own powers).
        var state = new SimState { Enemies = { Attacker(hits: 7, strengthDown: 2) } };
        state.You.WithWuppy(hp: 10);
        var branch = Assert.Single(SimEngine.Run(state).Branches);
        Assert.Equal(3, branch.You.WuppyHp);
        Assert.True(branch.You.WuppyPowers[PowerId.Strength] < 0);
        Assert.True(branch.You.Powers[PowerId.Strength] < 0);
    }

    [Fact]
    public void WuppyKilledByTheHitReceivesNoPower()
    {
        // Wuppy's result killed it: Creature.CanReceivePowers says no for a dead creature (Wuppy's Loyalty's ShouldAllowHitting, read from its code).
        var state = new SimState { Enemies = { Attacker(hits: 12, strengthDown: 2) } };
        state.You.WithWuppy(hp: 10);
        var branch = Assert.Single(SimEngine.Run(state).Branches);
        Assert.Equal(0, branch.You.WuppyHp);
        Assert.False(branch.You.WuppyPowers.Has(PowerId.Strength));
        Assert.True(branch.You.WuppyPowers.Has(PowerId.WuppysLoyalty)); // it stays on its dead owner (ShouldPowerBeRemovedAfterOwnerDeath: false)
        Assert.Equal(2, branch.Taken);
    }

    [Fact]
    public void AnUnpoweredDamageIsntRedirected()
    {
        // Both redirects test props.IsPoweredAttack(): a card's HP cost (Unblockable, Unpowered) reaches you, past Wuppy and Osty.
        var state = new SimState();
        state.You.Hp = 50;
        state.You.WithWuppy(hp: 10).WithOsty(hp: 10);
        SimEngine.DamagePlayer(state, "cost", Who.You, baseDamage: 3, SimEngine.HpCostProps, dealer: Who.You, card: null);
        Assert.Equal(47, state.You.Hp);
        Assert.Equal(10, state.You.WuppyHp);
        Assert.Equal(10, state.You.OstyHp);
    }

    [Fact]
    public void ADeadPetRedirectsNothing()
    {
        // Each redirect returns the target as it was given when its owner is dead (base.Owner.IsDead).
        var state = new SimState { Enemies = { Attacker(hits: 6) } };
        state.You.WithWuppy(hp: 0).WithOsty(hp: 0);
        Assert.Equal(6, SimEngine.Run(state).TakenMax);
    }

    [Fact]
    public void ATeammatesOstyTakesTheirHitsAndHisResultIsTold()
    {
        // Die For You on a teammate's Osty (his own id, Who.OstyOf): their powered hit past block is his, and Monarch's Gaze lands on him.
        var state = new SimState { Enemies = { Attacker(hits: 5) } };
        state.Enemies[0].TeammateHits = new() { new() { 5 } };
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.MonarchsGaze, 2);
        state.Teammates.Add(new SimPlayer { Hp = 30 }.WithOsty(hp: 8));
        var branch = Assert.Single(SimEngine.Run(state).Branches);
        var mate = branch.Teammates[0];
        Assert.Equal(30, mate.Hp);
        Assert.Equal(3, mate.OstyHp);
        Assert.True(mate.OstyPowers[PowerId.Strength] < 0);
    }
}
