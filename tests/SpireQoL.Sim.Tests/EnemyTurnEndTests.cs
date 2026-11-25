using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The enemies' side's turn end (CombatManager.EndEnemyTurnInternal, after the last enemy's move: Hook.BeforeSideTurnEnd, then Hook.AfterSideTurnEnd,
/// its side CombatSide.Enemy and its participants the enemies in the fight), run from the listeners' own code (decision #302): the countdowns
/// (WeakPower's, VulnerablePower's, FrailPower's TickDownDuration on side == CombatSide.Enemy), the powers of the players' side that last through the
/// enemy turn (FlameBarrierPower: base.Owner.Side != side), the enemies' own (RitualPower: participants.Contains(base.Owner)), and a duration's tick
/// skipped for a debuff new on the players' side (PowerCmd.Apply's SkipNextDurationTick; main's rule: BranchRules.DebuffTickSkippedOnTheEnemiesTurnOnly).
/// </summary>
public class EnemyTurnEndTests
{
    /// <summary>An enemy attacking once, its hit through the damage math as it lands (its intent: Weak on it and Vulnerable on you count).</summary>
    private static SimEnemy Attacker(int id, int hp, int hit) =>
        new() { Id = id, Name = $"E{id}", Hp = hp, Hits = new() { hit }, Intent = new List<IntentHit> { new(Base: hit) }, IntendsToAttack = true };

    private static SimState Fight(params SimEnemy[] enemies)
    {
        var state = new SimState();
        state.You.Hp = 60;
        state.Enemies.AddRange(enemies);
        return state;
    }

    private static SimState AfterTheTurn(SimState state, SimScript? play = null) => Assert.Single(play == null ? SimEngine.Run(state).Branches : SimEngine.Run(state, play).Branches);

    [Fact]
    public void AnEnemysWeakTicksDownAsItsTurnEndsAfterWeakeningItsAttack()
    {
        // WeakPower.AfterSideTurnEnd: side == CombatSide.Enemy. Its attack this turn is still weakened (10 × 0.75 = 7), then Weak 2 becomes 1, and Weak 1 goes.
        var state = Fight(Attacker(id: 0, hp: 40, hit: 10));
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Weak, amount: 2);
        var after = AfterTheTurn(state);
        Assert.Equal(7, after.Taken);
        Assert.Equal(1, after.Enemy(id: 0)!.Powers[PowerId.Weak]);
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Weak, amount: 1);
        Assert.False(AfterTheTurn(state).Enemy(id: 0)!.Powers.Has(PowerId.Weak));
    }

    [Fact]
    public void YourVulnerableAndFrailTickDownOnlyAsTheEnemiesTurnEnds()
    {
        // VulnerablePower's and FrailPower's AfterSideTurnEnd tick on the enemies' side only: your own turn's end leaves them, so the enemy's hit meets
        // Vulnerable 1 (10 × 1.5 = 15), and they go as the enemies' turn ends.
        var state = Fight(Attacker(id: 0, hp: 40, hit: 10));
        state.You.Powers = PowerSet.Empty.With(PowerId.Vulnerable, amount: 1).With(PowerId.Frail, amount: 2);
        var after = AfterTheTurn(state);
        Assert.Equal(15, after.Taken);
        Assert.False(after.You.Powers.Has(PowerId.Vulnerable));
        Assert.Equal(1, after.You.Powers[PowerId.Frail]);
    }

    [Fact]
    public void AWeakTheEnemysMoveGivesYouSkipsThatTurnsTick()
    {
        // PowerCmd.Apply: a debuff new on a creature of the players' side during the enemies' turn gets SkipNextDurationTick, so TickDownDuration spends it
        // at that turn's end and Weak 2 stays. Weak you already had ticks (more of one already there is PowerCmd.ModifyAmount: no skip).
        SimState Board(int weakAlready)
        {
            var enemy = Attacker(id: 0, hp: 40, hit: 4);
            enemy.Move = new SimAction[] { new MoveAttackAction(FirstHit: 0, Hits: 1), new MovePowerAction(PowerId.Weak, Amount: 2, To: new[] { Who.You }, Debuff: true) };
            var state = Fight(enemy);
            if (weakAlready > 0) state.You.Powers = PowerSet.Empty.With(PowerId.Weak, amount: weakAlready);
            return state;
        }
        var fresh = AfterTheTurn(Board(weakAlready: 0));
        Assert.Equal(2, fresh.You.Powers[PowerId.Weak]);
        Assert.Equal(0, fresh.TickSkips.Count);
        Assert.Equal(2, AfterTheTurn(Board(weakAlready: 1)).You.Powers[PowerId.Weak]);
    }

    [Fact]
    public void ASkipReadFromTheGameIsSpentOnce()
    {
        // The adapter reads SkipNextDurationTick from the game's powers (SimState.TickSkips): the tick is spent on it, and the skip is gone.
        var state = Fight(Attacker(id: 0, hp: 40, hit: 4));
        state.You.Powers = PowerSet.Empty.With(PowerId.Weak, amount: 2);
        state.TickSkips = state.TickSkips.With(SimKeys.Owned(PowerId.Weak.Name, Who.You), amount: 1);
        var after = AfterTheTurn(state);
        Assert.Equal(2, after.You.Powers[PowerId.Weak]);
        Assert.Equal(0, after.TickSkips.Count);
    }

    [Fact]
    public void AWeakLandingOnYouInYourTurnTicksOnBetaAndSkipsOnMain()
    {
        // Beta's PowerCmd.Apply sets the skip only during the enemies' turn (CurrentSide == CombatSide.Enemy); main's whenever a debuff lands new on
        // the players' side (MainRule.DebuffSkipsTickOnAnyTurn).
        SimState Board(MainRule rules)
        {
            var state = Fight(Attacker(id: 0, hp: 40, hit: 4));
            state.MainRules = rules;
            return state;
        }
        var play = Play(Card("Self-Weaken", SimCardType.Skill), target: Who.None, plays: 1, new ApplyPowerAction(PowerId.Weak, Amount: 2) { To = ApplyPowerAction.Where.You, Debuff = true });
        Assert.Equal(1, AfterTheTurn(Board(MainRule.None), play).You.Powers[PowerId.Weak]);
        Assert.Equal(2, AfterTheTurn(Board(MainRule.DebuffSkipsTickOnAnyTurn), play).You.Powers[PowerId.Weak]);
    }

    [Fact]
    public void RitualGivesItsStrengthAsItsOwnersTurnEndsNotYours()
    {
        // RitualPower.AfterSideTurnEnd: participants.Contains(base.Owner), the enemies as their turn ends. Its attack this turn has no Strength from it.
        var state = Fight(Attacker(id: 0, hp: 40, hit: 6));
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Ritual, amount: 3);
        var after = AfterTheTurn(state);
        Assert.Equal(6, after.Taken);
        Assert.Equal(3, after.Enemy(id: 0)!.Powers[PowerId.Strength]);
    }

    [Fact]
    public void FlameBarrierLastsThroughTheEnemyTurnAndGoesAtItsEnd()
    {
        // FlameBarrierPower.AfterSideTurnEnd: base.Owner.Side != side. Your turn's end leaves it (it hits the attacker back for 4), the enemies' takes it off.
        var state = Fight(Attacker(id: 0, hp: 40, hit: 6));
        state.You.FlameBarrier = 4;
        var after = AfterTheTurn(state);
        Assert.Equal(36, after.Enemy(id: 0)!.Hp);
        Assert.False(after.You.Powers.Has(PowerId.FlameBarrier));
    }

    [Fact]
    public void ADeadEnemysPowersHearNoTurnEnd()
    {
        // Your play kills it: its Ritual goes with it (CreatureCmd.Kill), so no Strength comes as the enemies' turn ends; the other's Weak still ticks.
        var state = Fight(Attacker(id: 0, hp: 5, hit: 6), Attacker(id: 1, hp: 40, hit: 4));
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Ritual, amount: 3);
        state.Enemies[1].Powers = PowerSet.Empty.With(PowerId.Weak, amount: 2);
        var strike = new SimPlay { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 0, HitDamage = { [0] = 9 }, Hits = 1 };
        var after = AfterTheTurn(state, SimScript.Of(strike));
        Assert.False(after.Enemy(id: 0)!.Alive);
        Assert.Equal(0, after.Enemy(id: 0)!.Powers[PowerId.Strength]);
        Assert.Equal(1, after.Enemy(id: 1)!.Powers[PowerId.Weak]);
    }

    [Fact]
    public void AsleepsLastStackGoingIsUnknownOnlyPastTheEnemyTurn()
    {
        // AsleepPower.AfterSideTurnEnd: a stack less as its owner's turn ends; the last one going wakes it (LagavulinMatriarch.WakeUpMove), which
        // isn't followed: the state after the enemies' turn end is unknown (SimState.UnmodelledAtTheirTurnEnd), what's read through the enemy turn isn't.
        var state = Fight(new SimEnemy { Id = 0, Name = "E0", Hp = 40 });
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Asleep, amount: 2);
        var after = AfterTheTurn(state);
        Assert.Equal(1, after.Enemy(id: 0)!.Powers[PowerId.Asleep]);
        Assert.Null(after.UnmodelledAtTheirTurnEnd);
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Asleep, amount: 1);
        var woken = AfterTheTurn(state);
        Assert.Null(woken.Unmodelled);
        Assert.NotNull(woken.UnmodelledAtTheirTurnEnd);
    }

    [Fact]
    public void DoomOnAnEnemyMakesTheStateAfterTheirTurnEndUnknown()
    {
        // DoomPower.BeforeSideTurnEnd (side != CombatSide.Player): the doomed enemies die as their side's turn ends, which the reader can't follow
        // (commands in a callback: turn-end-gaps.json, decision #303). The enemy turn before it stays known; the state after it is unknown, with why.
        var state = Fight(Attacker(id: 0, hp: 10, hit: 6));
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Doom, amount: 12);
        var after = AfterTheTurn(state);
        Assert.Null(after.Unmodelled);
        Assert.Equal(6, after.Taken);
        Assert.Contains(PowerId.Doom.Name, after.UnmodelledAtTheirTurnEnd, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnmodelledPowerThatWaitsOnItsOwnerTakingPartLeavesTheirTurnEndKnown()
    {
        // NoDrawPower.AfterSideTurnEnd: participants.Contains(base.Owner), which no player is as the enemies' side's turn ends: nothing unknown there.
        // A dead enemy's Doom hears nothing (its powers go with it).
        var state = Fight(Attacker(id: 0, hp: 40, hit: 6), Attacker(id: 1, hp: 5, hit: 3));
        state.You.Powers = PowerSet.Empty.With(PowerId.NoDraw, amount: 1);
        state.Enemies[1].Powers = PowerSet.Empty.With(PowerId.Doom, amount: 3);
        var strike = new SimPlay { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 1, HitDamage = { [1] = 9 }, Hits = 1 };
        var after = AfterTheTurn(state, SimScript.Of(strike));
        Assert.False(after.Enemy(id: 1)!.Alive);
        Assert.Null(after.UnmodelledAtTheirTurnEnd);
    }

    [Fact]
    public void ATurnsEndToldItsSideByTheSideEnding()
    {
        // The side a turn's-end hook is told (SimState.SideEnding), not the enemy turn's flag: Weak on you ticks at the enemies' side's turn end
        // (side == CombatSide.Enemy), not at yours; Flame Barrier on you goes at the enemies' (base.Owner.Side != side), not at yours.
        var state = Fight(Attacker(id: 0, hp: 40, hit: 6));
        state.You.Powers = PowerSet.Empty.With(PowerId.Weak, amount: 2).With(PowerId.FlameBarrier, amount: 4);
        var after = AfterTheTurn(state);
        Assert.Equal(1, after.You.Powers[PowerId.Weak]);
        Assert.False(after.You.Powers.Has(PowerId.FlameBarrier));
        Assert.Equal(TurnSide.None, after.SideEnding);
    }

    [Fact]
    public void TheBlockYourTurnLeftComesBackAfterTheEnemiesTurnEnd()
    {
        // The enemy's block this turn (cleared, then its move's) is what its turn's end meets; once it's over, the outcome reads what your turn left it.
        var enemy = Attacker(id: 0, hp: 40, hit: 4);
        enemy.Block = 5;
        enemy.Move = new SimAction[] { new MoveAttackAction(FirstHit: 0, Hits: 1), new MoveBlockAction(Amount: 8, To: new[] { 0 }) };
        var after = AfterTheTurn(Fight(enemy));
        Assert.Equal(5, after.Enemy(id: 0)!.Block);
        Assert.Equal(-1, after.Enemy(id: 0)!.TurnEndBlock);
    }

    [Fact]
    public void CoverAndInterceptGoAsTheEnemiesTurnEnds()
    {
        // CoveredPower's and InterceptPower's AfterSideTurnEnd: side == CombatSide.Enemy, each takes itself off once the enemy turn it covers is over.
        var state = Fight(Attacker(id: 0, hp: 40, hit: 4));
        var covered = new SimPlayer { Hp = 50, HandCount = 0 };
        covered.Powers = covered.Powers.With(PowerId.Covered, amount: 1, data: Models.CoveredPower.CovererBit(Who.You));
        state.Teammates.Add(covered);
        state.You.Powers = state.You.Powers.With(PowerId.Intercept, amount: 1);
        SimEngine.SetField(state, Who.You, Models.CoveredPower.CoveredCount, 1);
        var after = AfterTheTurn(state);
        Assert.Equal(0, after.Teammates[0].Taken);
        Assert.False(after.Teammates[0].Powers.Has(PowerId.Covered));
        Assert.False(after.You.Powers.Has(PowerId.Intercept));
    }

    [Fact]
    public void SkittishIsReadyAgainOnceYourTurnEnds()
    {
        // SkittishPower.AfterSideTurnEnd: side != base.Owner.Side clears HasGainedBlockThisTurn as your turn ends, not as its own does.
        var state = Fight(Attacker(id: 0, hp: 40, hit: 4));
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Skittish, amount: 5, data: Models.SkittishPower.UsedThisTurn);
        Assert.Equal(0, AfterTheTurn(state).Enemy(id: 0)!.Powers.DataOf(PowerId.Skittish) & Models.SkittishPower.UsedThisTurn);
    }
}
