using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A dead enemy's powers don't listen (CreatureCmd.Kill: Creature.RemoveAllPowersAfterDeath after its AfterDeath), for every model, in the
/// engine's listener list: no model asks whether its owner lives. The hit that killed it and its own death are told to them still, as the
/// game runs both before the removal.
/// </summary>
public class DeathListenerTests
{
    private static SimEnemy Enemy(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    private static SimPlay Strike(int target, int damage)
        => new() { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = target, HitDamage = { [target] = damage }, Hits = 1 };

    [Fact]
    public void ADeadEnemysPowersAreNotListeners()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 0, hits: 4), Enemy(id: 2, hp: 30, hits: 8) } };
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.Strength, 3);
        state.Enemies[1].Powers = state.Enemies[1].Powers.With(PowerId.Strength, 2);
        var listeners = SimEngine.ListenerOrder(state);
        Assert.DoesNotContain(listeners, listener => listener.Owner == 1);
        Assert.Contains(listeners, listener => listener.Owner == 2 && listener.Name == PowerId.Strength.Name);
    }

    [Fact]
    public void AnEnemyKilledMidTurnStopsListening()
    {
        // The listeners are cached on the state: a death rebuilds them, though no power set changed.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 30, hits: 4) } };
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.Strength, 3);
        Assert.Contains(SimEngine.Listeners(state), listener => listener.Owner == 1);
        state.Enemies[0].Hp = 0;
        Assert.DoesNotContain(SimEngine.Listeners(state), listener => listener.Owner == 1);
    }

    [Fact]
    public void ADeadEnemysEnrageTakesNoSkill()
    {
        // EnragePower.AfterCardPlayed gives its owner Strength on every Skill: not once its owner is dead (its powers went with it).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 0, hits: 4), Enemy(id: 2, hp: 30, hits: 8) } };
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.Enrage, 2);
        var skill = new SimPlay { Name = "Defend", Card = new SimCardPlay(new SimCard("Defend", SimCardType.Skill), Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
        SimEngine.ApplyPlayTo(state, skill, out var after, out _);
        Assert.All(after, branch => Assert.False(branch.Enemy(id: 1)!.Powers.Has(PowerId.Strength)));
    }

    [Fact]
    public void TheHitThatKillsIsToldToItsOwnPowers()
    {
        // SkittishPower.AfterDamageReceived notes the attack's first hit on its owner: the hit that kills it too (the game kills it after
        // its damage hooks, CreatureCmd.Damage).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5, hits: 4), Enemy(id: 2, hp: 30, hits: 8) } };
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.Skittish, 3);
        SimEngine.ApplyPlayTo(state, Strike(target: 1, damage: 9), out var after, out _);
        var dead = after[0].Enemy(id: 1)!;
        Assert.False(dead.Alive);
        Assert.NotEqual(0, dead.Powers.DataOf(PowerId.Skittish));
    }
}
