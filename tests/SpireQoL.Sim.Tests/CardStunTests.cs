using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A card's stun of the enemy it's played at (CreatureCmd.Stun(cardPlay.Target): Whistle, decision #299): Creature.StunInternal makes its next move
/// the stunned move, so it doesn't act this enemy turn; on one that's dead (StunInternal's IsDead test), nothing.
/// </summary>
public class CardStunTests
{
    private static SimPlay Whistle(int damage) => new()
    {
        Name = "Whistle", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 0,
        Body = { ScriptBuilder.Attack(baseDamage: damage, mode: SimPlay.Target.Single), new StunTargetAction() },
    };

    private static SimState Fight() => new()
    {
        Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 30, Hits = new() { 8 }, IntendsToAttack = true }, new SimEnemy { Id = 1, Name = "E1", Hp = 30, Hits = new() { 5 }, IntendsToAttack = true } },
    };

    [Fact]
    public void TheEnemyItsPlayedAtDoesntAct()
    {
        var outcome = SimEngine.Run(Fight(), Whistle(damage: 3));
        var branch = Assert.Single(outcome.Branches);
        Assert.True(branch.Enemy(id: 0)!.Stunned);
        Assert.False(branch.Enemy(id: 1)!.Stunned);
        Assert.Equal(5, outcome.TakenMax); // only the other enemy's hit
    }

    [Fact]
    public void ADeadEnemyIsntStunned()
    {
        var branch = Assert.Single(SimEngine.Run(Fight(), Whistle(damage: 40)).Branches);
        Assert.False(branch.Enemy(id: 0)!.Alive);
        Assert.False(branch.Enemy(id: 0)!.Stunned);
    }
}
