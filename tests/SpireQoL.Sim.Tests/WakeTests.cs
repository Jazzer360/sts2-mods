using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Asleep (Lagavulin Matriarch) and Slumber (Slumbering Beetle), read from their code (decision #307): what waking them does, and their tab, which
/// compares the enemy's next move (rolled as your next turn starts) with and without the play, from its move rules on the simulated states. The
/// game's intents are untouched: the comparison is the simulation's own.
/// </summary>
public class WakeTests
{
    private static SimState Sleeper(PowerId power, int stacks, int block = 0, int plating = 0)
    {
        var state = new SimState { TurnNumber = 2, RoundNumber = 2 };
        state.You.Hp = 50;
        var enemy = new SimEnemy { Id = 0, Name = "E0", Hp = 80, Block = block, Powers = PowerSet.Empty.With(power, amount: stacks) };
        if (plating > 0) enemy.Powers = enemy.Powers.With(PowerId.Plating, amount: plating);
        WakeTabParityTests.WithSleeperRules(enemy);
        state.Enemies.Add(enemy);
        return state;
    }

    private static SimPlay Strike(int damage, int hits = 1) =>
        new() { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 0, HitDamage = { [0] = damage }, Hits = hits };

    private static OutcomeRule? Tab(PowerId power, SimState state) => SimModels.Get(power.Name)!.OutcomeTab(state, SimEngine.Run(state), owner: 0);

    [Fact]
    public void WakingASleeperThatStaysAsleepOtherwiseIsCrossed()
    {
        // AsleepPower.AfterDamageReceived: HP through its block wakes it: CreatureCmd.Stun(WakeUpMove, "SLASH_MOVE"). On 3 stacks, ending the turn
        // now leaves it asleep (2 stacks: its next move SLEEP_MOVE again); a hit through its block makes its next move SLASH_MOVE, an attack.
        var state = Sleeper(PowerId.Asleep, stacks: 3);
        var rule = Tab(PowerId.Asleep, state)!;
        Assert.True(rule.Harmful);
        var woken = SimEngine.Run(state, Strike(damage: 6));
        Assert.True(rule.SetOff(woken));
        Assert.Equal(true, SimEngine.NextMoveAttacks(woken.Branches[0], enemyId: 0));
        // A hit its block takes whole doesn't wake it.
        var blocked = Sleeper(PowerId.Asleep, stacks: 3, block: 10);
        Assert.False(Tab(PowerId.Asleep, blocked)!.SetOff(SimEngine.Run(blocked, Strike(damage: 6))));
    }

    [Fact]
    public void OnItsLastStackThereIsNoTag()
    {
        // The owner's rule: on its last stack its turn's end takes the last stack (AfterSideTurnEnd) and the move rolled as your next turn starts finds
        // the power gone (SLEEP_BRANCH: SLASH_MOVE): it attacks next either way, so waking it now does no harm.
        var state = Sleeper(PowerId.Asleep, stacks: 1);
        var rule = Tab(PowerId.Asleep, state);
        Assert.False(rule?.SetOff(SimEngine.Run(state, Strike(damage: 6))) ?? false);
        var beetle = Sleeper(PowerId.Slumber, stacks: 1);
        Assert.False(Tab(PowerId.Slumber, beetle)?.SetOff(SimEngine.Run(beetle, Strike(damage: 6))) ?? false);
    }

    [Fact]
    public void SlumberWakesWhenThePlayLeavesItsLastStackToItsTurnsEnd()
    {
        // SlumberPower.AfterDamageReceived takes a stack per hit through its block; its AfterSideTurnEnd takes one more. On 3 stacks, one hit leaves 2
        // (1 after its turn: still asleep next turn); two hits leave 1, which its turn's end takes: it rolls ROLL_OUT_MOVE next, crossed.
        var state = Sleeper(PowerId.Slumber, stacks: 3);
        var rule = Tab(PowerId.Slumber, state)!;
        Assert.False(rule.SetOff(SimEngine.Run(state, Strike(damage: 4))));
        Assert.True(rule.SetOff(SimEngine.Run(state, Strike(damage: 4, hits: 2))));
    }

    [Fact]
    public void SlumbersStunnedMoveTakesItsPlatingAsItActs()
    {
        // Its last stack taken by your hit: CreatureCmd.Stun(SlumberingBeetle.WakeUpMove, "ROLL_OUT_MOVE"). WakeUpMove is the stunned move, run as it
        // acts in the enemy turn (StunStep.Move): PowerCmd.Remove of its Plating, so no Plating block as its turn ends.
        var state = Sleeper(PowerId.Slumber, stacks: 1, plating: 9);
        var woken = SimEngine.Run(state, Strike(damage: 5)).Branches.Single();
        Assert.False(woken.Enemy(id: 0)!.Powers.Has(PowerId.Plating));
        Assert.Null(woken.Unmodelled);
        Assert.Null(woken.UnmodelledAtTheirTurnEnd);
        // Not woken: its Plating stays (its turn's end takes a stack of it).
        var asleep = Sleeper(PowerId.Slumber, stacks: 3, plating: 9);
        Assert.True(SimEngine.Run(asleep).Branches.Single().Enemy(id: 0)!.Powers.Has(PowerId.Plating));
    }

    [Fact]
    public void AKilledSleeperHasNoNextMove()
    {
        // A play that kills it: no next move, so nothing it wakes into (the hand-written model warned on any HP through its block).
        var state = Sleeper(PowerId.Asleep, stacks: 3);
        state.Enemies[0].Hp = 5;
        Assert.False(Tab(PowerId.Asleep, state)!.SetOff(SimEngine.Run(state, Strike(damage: 9))));
    }

    [Fact]
    public void WithNoMoveRulesTheStunsOwnRuleStands()
    {
        // The adapter reads the rules for every enemy whose power compares (SimEnemy.Rules); without them the comparison can't be made.
        var state = Sleeper(PowerId.Asleep, stacks: 3);
        state.Enemies[0].Rules = null;
        Assert.False(Tab(PowerId.Asleep, state)?.Harmful ?? false);
    }
}
