using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The enemy turn runs each enemy's move as its code runs it (decision #195, MonsterModel.PerformMove: move.PerformMove(targets), each
/// command awaited in turn; CombatManager.ExecuteEnemyTurn: each enemy in order): what an earlier enemy's move does lands before a later
/// enemy's hit (Vulnerable on you: VulnerablePower.ModifyDamageMultiplicative; Strength to an ally), and within one move, what comes
/// before its attack counts for it and what comes after doesn't. The intent's hits stay the pipeline's inputs (SimEnemy.Intent).
/// </summary>
public class SimEnemyMoveTests
{
    private static SimEnemy Attacker(int id, int hp, params decimal[] hits) =>
        new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.Select(hit => (int)hit).ToList(), Intent = hits.Select(hit => new IntentHit(hit)).ToList() };

    private static MovePowerAction Vulnerable(int amount, params int[] on) => new(PowerId.Vulnerable, amount, on, Debuff: true);

    [Fact]
    public void AVulnerableAnEarlierEnemyPutsOnYouRaisesALaterEnemysHit()
    {
        // Mawler's Roar (PowerCmd.Apply<VulnerablePower>(targets, 3)) before another enemy's 10: that hit lands for 15.
        var roar = new SimEnemy { Id = 0, Name = "Mawler", Hp = 50, Move = new SimAction[] { Vulnerable(amount: 3, Who.You) } };
        var state = new SimState { Enemies = { roar, Attacker(id: 1, hp: 50, hits: 10) } };
        Assert.Equal(15, SimEngine.Run(state).TakenMax);
    }

    [Fact]
    public void WithoutItsMoveAnEnemyIsItsIntentsHitsAlone()
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "Mawler", Hp = 50 }, Attacker(id: 1, hp: 50, hits: 10) } };
        Assert.Equal(10, SimEngine.Run(state).TakenMax);
    }

    [Fact]
    public void ItsOwnVulnerableAfterItsAttackLandsAfterThatAttack()
    {
        // Ovicopter's Tenderizer: DamageCmd.Attack, then Vulnerable on its targets. Its own hit is 10; the next enemy's 10 is 15.
        var tenderizer = Attacker(id: 0, hp: 50, hits: 10);
        tenderizer.Move = new SimAction[] { new MoveAttackAction(FirstHit: 0, Hits: 1), Vulnerable(amount: 2, Who.You) };
        var state = new SimState { Enemies = { tenderizer, Attacker(id: 1, hp: 50, hits: 10) } };
        Assert.Equal(10 + 15, SimEngine.Run(state).TakenMax);
    }

    [Fact]
    public void StrengthGivenToItsAlliesRaisesTheirLaterHits()
    {
        // The Obscura's Wail: 3 Strength to every enemy of its side, then a Parafright hits twice for 5: 8 each.
        var wail = new SimEnemy { Id = 0, Name = "TheObscura", Hp = 80, Move = new SimAction[] { new MovePowerAction(PowerId.Strength, Amount: 3, To: new[] { 0, 1 }, Debuff: false) } };
        var state = new SimState { Enemies = { wail, Attacker(id: 1, hp: 20, hits: new[] { 5m, 5m }) } };
        Assert.Equal(16, SimEngine.Run(state).TakenMax);
    }

    [Fact]
    public void StrengthBeforeItsOwnAttackCountsForIt()
    {
        var enemy = Attacker(id: 0, hp: 50, hits: 6);
        enemy.Move = new SimAction[] { new MovePowerAction(PowerId.Strength, Amount: 2, To: new[] { 0 }, Debuff: false), new MoveAttackAction(FirstHit: 0, Hits: 1) };
        Assert.Equal(8, SimEngine.Run(new SimState { Enemies = { enemy } }).TakenMax);
    }

    [Fact]
    public void YourArtifactTakesTheDebuff()
    {
        var roar = new SimEnemy { Id = 0, Name = "Mawler", Hp = 50, Move = new SimAction[] { Vulnerable(amount: 3, Who.You) } };
        var state = new SimState { Enemies = { roar, Attacker(id: 1, hp: 50, hits: 10) } };
        state.You.Powers = state.You.Powers.With(PowerId.Artifact, amount: 1);
        var branch = Assert.Single(SimEngine.Run(state).Branches);
        Assert.Equal(10, branch.Taken);
        Assert.False(branch.You.Powers.Has(PowerId.Artifact));
    }

    [Fact]
    public void AStunnedEnemysMoveDoesNothing()
    {
        var roar = new SimEnemy { Id = 0, Name = "Mawler", Hp = 50, Stunned = true, Move = new SimAction[] { Vulnerable(amount: 3, Who.You) } };
        Assert.Equal(10, SimEngine.Run(new SimState { Enemies = { roar, Attacker(id: 1, hp: 50, hits: 10) } }).TakenMax);
    }

    [Fact]
    public void AnEnemyDeadBeforeItActsAppliesNothing()
    {
        // Its poison kills it as the enemies' turn starts.
        var roar = new SimEnemy { Id = 0, Name = "Mawler", Hp = 4, Poison = 4, PoisonStacks = 4, Move = new SimAction[] { Vulnerable(amount: 3, Who.You) } };
        Assert.Equal(10, SimEngine.Run(new SimState { Enemies = { roar, Attacker(id: 1, hp: 50, hits: 10) } }).TakenMax);
    }

    [Fact]
    public void TheDebuffLandsOnEveryPlayerItNames()
    {
        // PowerCmd.Apply(targets, ...): every player. A teammate's hit from the next enemy is raised too.
        var roar = new SimEnemy { Id = 0, Name = "Mawler", Hp = 50, Move = new SimAction[] { Vulnerable(amount: 3, Who.You, Who.Teammate(teammateIndex: 0)) } };
        var state = new SimState { Enemies = { roar, Attacker(id: 1, hp: 50, hits: 10) } };
        state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 40 });
        var outcome = SimEngine.Run(state);
        Assert.Equal(15, outcome.TakenMax);
        Assert.Equal(15, outcome.TeammateTakenMax(teammateIndex: 0));
    }

    [Fact]
    public void BlockAMoveGivesAnAllyTakesYourThorns()
    {
        // Guardbot's Guard (CreatureCmd.GainBlock on an ally) before that ally attacks into your 5 Thorns: its block takes them.
        var guard = new SimEnemy { Id = 0, Name = "Guardbot", Hp = 30, Move = new SimAction[] { new MoveBlockAction(Amount: 15, To: new[] { 1 }) } };
        var state = new SimState { Enemies = { guard, Attacker(id: 1, hp: 20, hits: 8) } };
        state.You.Thorns = 5;
        var branch = Assert.Single(SimEngine.Run(state).Branches);
        Assert.Equal(20, branch.Enemies[1].Hp);
        state.Enemies[0] = new SimEnemy { Id = 0, Name = "Guardbot", Hp = 30 };
        Assert.Equal(15, Assert.Single(SimEngine.Run(state).Branches).Enemies[1].Hp);
    }

    [Fact]
    public void AnAttackerYourThornsKillFinishesItsMove()
    {
        // MonsterModel.PerformMove runs the move to its end; PowerCmd.Apply asks only that the fight isn't ending: an enemy your Thorns
        // kill during its attack still puts its Vulnerable on you, and the next enemy's 10 lands for 15.
        var tenderizer = Attacker(id: 0, hp: 3, hits: 5);
        tenderizer.Move = new SimAction[] { new MoveAttackAction(FirstHit: 0, Hits: 1), Vulnerable(amount: 2, Who.You) };
        var state = new SimState { Enemies = { tenderizer, Attacker(id: 1, hp: 50, hits: 10) } };
        state.You.Thorns = 5;
        Assert.Equal(5 + 15, SimEngine.Run(state).TakenMax);
    }

    [Fact]
    public void AMovesPowersOnADeadCreatureDoNothing()
    {
        // The Strength meant for an ally your end of turn killed: no one gets it, and nothing breaks.
        var wail = new SimEnemy { Id = 0, Name = "TheObscura", Hp = 80, Move = new SimAction[] { new MovePowerAction(PowerId.Strength, Amount: 3, To: new[] { 0, 1 }, Debuff: false) } };
        var state = new SimState { Enemies = { wail, new SimEnemy { Id = 1, Name = "E1", Hp = 0 } } };
        var branch = Assert.Single(SimEngine.Run(state).Branches);
        Assert.Equal(3, branch.Enemies[0].Powers[PowerId.Strength]);
        Assert.False(branch.Enemies[1].Powers.Has(PowerId.Strength));
    }

    [Fact]
    public void AnEnemysVigorIsSpentByItsFirstAttack()
    {
        // AttackCommand.Execute: Hook.BeforeAttack, its hits, Hook.AfterAttack. VigorPower.BeforeAttack keeps the attack (a monster's has no
        // ModelSource), ModifyDamageAdditive adds its amount to that attack's hits, AfterAttack takes it back (PowerCmd.ModifyAmount: 0, gone).
        // A move of two attacks: 3 + 5, then 3.
        var enemy = Attacker(id: 0, hp: 50, hits: new[] { 3m, 3m });
        enemy.Move = new SimAction[] { new MoveAttackAction(FirstHit: 0, Hits: 1), new MoveAttackAction(FirstHit: 1, Hits: 1) };
        enemy.Powers = enemy.Powers.With(PowerId.Vigor, amount: 5);
        var branch = Assert.Single(SimEngine.Run(new SimState { Enemies = { enemy } }).Branches);
        Assert.Equal(8 + 3, branch.Taken);
        Assert.False(branch.Enemies[0].Powers.Has(PowerId.Vigor));
    }

    [Fact]
    public void AnEnemyDeadBeforeItsAttackMakesNoAttack()
    {
        // AttackCommand.Execute returns before Hook.BeforeAttack when its attacker is dead: your Thorns killing it on its first attack's hit
        // leaves its Vigor for no attack (none is made), and the second attack makes no hits.
        var enemy = Attacker(id: 0, hp: 2, hits: new[] { 3m, 3m });
        enemy.Move = new SimAction[] { new MoveAttackAction(FirstHit: 0, Hits: 1), new MoveAttackAction(FirstHit: 1, Hits: 1) };
        enemy.Powers = enemy.Powers.With(PowerId.Vigor, amount: 5);
        var state = new SimState { Enemies = { enemy, new SimEnemy { Id = 1, Name = "E1", Hp = 50 } } };
        state.You.Thorns = 2;
        var branch = Assert.Single(SimEngine.Run(state).Branches);
        Assert.Equal(8, branch.Taken);
        Assert.Equal(1, branch.CancelledHits);
    }
}
