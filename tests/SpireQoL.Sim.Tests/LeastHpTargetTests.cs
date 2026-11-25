using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// An attack aimed at the hittable enemy with the least HP (decision #320): Memento Mori's `HittableEnemies.MinBy(c =&gt; c.CurrentHp)`, picked as the
/// attack starts, in each branch. MinBy keeps the first it meets among equals: on a tie, the first in the fight's order (no roll, nothing hidden).
/// </summary>
public class LeastHpTargetTests
{
    private static AttackAction AtLeastHp(int damage) => Attack(baseDamage: damage, mode: SimPlay.Target.LeastHp);

    private static SimScript MementoMori(int plays = 1, params SimAction[] before) =>
        Play(Card("MementoMori"), target: Who.None, plays, before.Append(AtLeastHp(damage: 19)).ToArray());

    private static int HpOf(SimState state, int enemyId) => state.Enemy(enemyId)!.Hp;

    [Fact]
    public void ItHitsTheEnemyWithTheLeastHpOfTwo()
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50), Enemy(id: 1, hp: 30) } };
        var after = Assert.Single(After(state, MementoMori()));
        Assert.Equal(50, HpOf(after, enemyId: 0));
        Assert.Equal(11, HpOf(after, enemyId: 1));
    }

    [Fact]
    public void OfThreeItHitsTheLeastAndOnATieTheFirstInOrder()
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 40), Enemy(id: 1, hp: 25), Enemy(id: 2, hp: 60) } };
        var after = Assert.Single(After(state, MementoMori()));
        Assert.Equal(new[] { 40, 6, 60 }, after.Enemies.Select(enemy => enemy.Hp));

        // A tie at 25: the first of them in the fight's order, one outcome (no branch).
        var tied = new SimState { Enemies = { Enemy(id: 0, hp: 40), Enemy(id: 1, hp: 25), Enemy(id: 2, hp: 25) } };
        var hit = Assert.Single(After(tied, MementoMori()));
        Assert.Equal(new[] { 40, 6, 25 }, hit.Enemies.Select(enemy => enemy.Hp));
    }

    [Fact]
    public void BlockDoesntCountOnlyHp()
    {
        // CurrentHp, not HP plus block: the 30 HP enemy behind 20 block is still the one hit.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 40), Enemy(id: 1, hp: 30, block: 20) } };
        var after = Assert.Single(After(state, MementoMori()));
        Assert.Equal(40, HpOf(after, enemyId: 0));
        Assert.Equal(30, HpOf(after, enemyId: 1));
        Assert.Equal(1, after.Enemy(1)!.Block);
    }

    [Fact]
    public void AKillEarlierInThePlayMovesItToTheNextLeast()
    {
        // The play's first attack kills the 10 HP enemy at its target; the least-HP attack then picks among those left: the 30 HP one.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 10), Enemy(id: 1, hp: 50), Enemy(id: 2, hp: 30) } };
        var script = Play(Card("MementoMori"), target: 0, plays: 1, Attack(baseDamage: 12), AtLeastHp(damage: 19));
        var after = Assert.Single(After(state, script));
        Assert.False(after.Enemy(0)!.Alive);
        Assert.Equal(50, HpOf(after, enemyId: 1));
        Assert.Equal(11, HpOf(after, enemyId: 2));
    }

    [Fact]
    public void EachPlayOfAReplayPicksAgain()
    {
        // Played twice: the first play takes the 25 HP enemy to 6, the second picks again and hits it, still the least, to its death.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 25), Enemy(id: 1, hp: 30) } };
        var after = Assert.Single(After(state, MementoMori(plays: 2)));
        Assert.False(after.Enemy(0)!.Alive);
        Assert.Equal(30, HpOf(after, enemyId: 1));

        // 20 and 15 HP: the first play kills the 15, the second picks the 20 (the only one left) and takes it to 1.
        var two = new SimState { Enemies = { Enemy(id: 0, hp: 20), Enemy(id: 1, hp: 15) } };
        var both = Assert.Single(After(two, MementoMori(plays: 2)));
        Assert.Equal(1, HpOf(both, enemyId: 0));
        Assert.False(both.Enemy(1)!.Alive);
    }

    [Fact]
    public void AnUnhittableEnemyIsNeverTheTarget()
    {
        // Not among HittableEnemies, however low its HP.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 40), Bystander(id: 1) } };
        state.Enemies[1].Hp = 5;
        var after = Assert.Single(After(state, MementoMori()));
        Assert.Equal(21, HpOf(after, enemyId: 0));
        Assert.Equal(5, HpOf(after, enemyId: 1));
    }
}
