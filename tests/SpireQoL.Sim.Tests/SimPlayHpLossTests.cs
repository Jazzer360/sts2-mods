using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// HP you lose during a play (decompile v0.113.1), for the HP-loss tabs. A card's own HP cost (its HpLoss value:
/// CreatureCmd.Damage on you, Unblockable and Unpowered, before its hits) and enemy Thorns (ThornsPower.BeforeDamageReceived,
/// blockable, before each hit) go through your block, then the HP-loss modifiers (Tungsten Rod, Beating Remnant, Buffer).
///  - Tungsten Rod (ModifyHpLostAfterOsty) flashes when it changes the amount: some got past your block (PlayHpReached).
///  - Self-Forming Clay, Emotion Chip, Centennial Puzzle, Demon Tongue, Inferno and Rupture react to AfterDamageReceived with
///    UnblockedDamage &gt; 0, which is the HP actually lost, after Tungsten Rod (PlayHpLosses).
/// Good tabs, so each count is the smallest over the play's branches.
/// </summary>
public class SimPlayHpLossTests
{
    private static SimEnemy E(int id, int hp, int thorns = 0) => new() { Id = id, Name = $"E{id}", Hp = hp, Thorns = thorns, Hits = new() { 5 } };

    private static SimPlay Attack(int target, int damage, int hits = 1)
        => new() { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = target, HitDamage = { [target] = damage }, Hits = hits };

    private static SimPlay Cost(int hp, int times = 1) => new() { Name = "Bloodletting", HpCost = hp, HpCostTimes = times };

    [Fact]
    public void ThornsYourBlockStopsIsNoHpLoss()
    {
        var state = new SimState { Enemies = { E(1, 50, thorns: 3) } };
        state.You.Block = 5;
        var outcome = SimEngine.Run(state, Attack(1, 6));
        Assert.Equal(0, outcome.PlayHpReachedMin);
        Assert.Equal(0, outcome.PlayHpLossesMin);
    }

    [Fact]
    public void ThornsPastYourBlockIsHpLoss()
    {
        var state = new SimState { Enemies = { E(1, 50, thorns: 3) } };
        var outcome = SimEngine.Run(state, Attack(1, 6));
        Assert.Equal(1, outcome.PlayHpReachedMin);
        Assert.Equal(1, outcome.PlayHpLossesMin);
        Assert.Equal(3, outcome.PlayHpLostMin);
    }

    [Fact]
    public void EachHitSetsThornsOffBlockRunsOutPartWay()
    {
        // Thorns 3 before each of 2 hits, 4 block: the first is blocked, the second loses 2 HP.
        var state = new SimState { Enemies = { E(1, 50, thorns: 3) } };
        state.You.Block = 4;
        var outcome = SimEngine.Run(state, Attack(1, 2, hits: 2));
        Assert.Equal(1, outcome.PlayHpReachedMin);
        Assert.Equal(1, outcome.PlayHpLossesMin);
        Assert.Equal(2, outcome.PlayHpLostMin);
    }

    [Fact]
    public void TungstenRodTakesOneThornsToNothing()
    {
        // 1 Thorns past your block: Tungsten Rod takes it to 0, so it flashes but you lose no HP (no Self-Forming Clay).
        var state = new SimState { Enemies = { E(1, 50, thorns: 1) } };
        state.You.TungstenRod = 1;
        var outcome = SimEngine.Run(state, Attack(1, 6));
        Assert.Equal(1, outcome.PlayHpReachedMin);
        Assert.Equal(0, outcome.PlayHpLossesMin);
    }

    [Fact]
    public void CardHpCostThroughTungstenRod()
    {
        var state = new SimState { Enemies = { E(1, 50) } };
        state.You.TungstenRod = 1;
        var one = SimEngine.Run(state, Cost(1));
        Assert.Equal(1, one.PlayHpReachedMin);
        Assert.Equal(0, one.PlayHpLossesMin);
        var three = SimEngine.Run(state, Cost(3));
        Assert.Equal(1, three.PlayHpLossesMin);
        Assert.Equal(2, three.PlayHpLostMin);
    }

    [Fact]
    public void CardHpCostIgnoresBlock()
    {
        var state = new SimState { Enemies = { E(1, 50) } };
        state.You.Block = 10;
        var outcome = SimEngine.Run(state, Cost(3, times: 2));
        Assert.Equal(2, outcome.PlayHpLossesMin);
        Assert.Equal(6, outcome.PlayHpLostMin);
        Assert.Equal(6, outcome.TakenMax); // the enemy's 5 then goes into the untouched 10 block
    }

    [Fact]
    public void BufferStopsTheCostAndIsGoneForTheEnemyTurn()
    {
        // Buffer takes the card's HP cost, so nothing reacts to HP loss and the enemy's 5 then gets through.
        var state = new SimState { Enemies = { E(1, 50) } };
        state.You.Buffer = 1;
        var outcome = SimEngine.Run(state, Cost(3));
        Assert.Equal(1, outcome.PlayHpReachedMin);
        Assert.Equal(0, outcome.PlayHpLossesMin);
        Assert.Equal(5, outcome.TakenMax);
    }

    [Fact]
    public void HpCostSetsOffInfernoOnce()
    {
        var state = new SimState { Enemies = { E(1, 50) } };
        state.You.Inferno = 5;
        var outcome = SimEngine.Run(state, Cost(2));
        Assert.Equal(45, outcome.Worst.Enemy(1)!.HpAtTurnEnd);
    }

    [Fact]
    public void RandomHitNeedsThornsInEveryBranch()
    {
        // A random hit: only E1 has Thorns, so in E2's branch you lose nothing.
        var state = new SimState { Enemies = { E(1, 50, thorns: 3), E(2, 50) } };
        var play = new SimPlay { Name = "Sword Boomerang", IsAttack = true, HitTarget = SimPlay.Target.Random, Hits = 1, HitDamage = { [1] = 3, [2] = 3 } };
        var outcome = SimEngine.Run(state, play);
        Assert.Equal(0, outcome.PlayHpLossesMin);
        Assert.Contains(outcome.Branches, branch => branch.PlayHpLosses > 0);
    }
}
