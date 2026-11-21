using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Surrounded (Kaiser Crab, decompile v0.113.1 SurroundedPower): the crab behind you hits 1.5× (ModifyDamageMultiplicative),
/// a card aimed at the crab behind you turns you (BeforeCardPlayed), and when an enemy dies and every hittable enemy
/// left is on one side, you turn to face the first of them (AfterDeath).
/// </summary>
public class SimSurroundedTests
{
    // Facing left (Data 1): the crab with BackAttackRightPower is behind you.
    private static SimState Fight(int leftHp = 5, int rightHp = 100)
    {
        var s = new SimState
        {
            Enemies =
            {
                new SimEnemy { Id = 1, Name = "Crusher", Hp = leftHp, Hits = new() { 8 }, Intent = new() { new(8) }, Powers = PowerSet.Empty.With(PowerId.BackAttackLeft, 1) },
                new SimEnemy { Id = 2, Name = "Rocket", Hp = rightHp, Hits = new() { 10 }, Intent = new() { new(10) }, Powers = PowerSet.Empty.With(PowerId.BackAttackRight, 1) },
            },
        };
        s.P.Powers = s.P.Powers.With(PowerId.Surrounded, 1, 1);
        return s;
    }

    private static SimPlay Strike(int target, int damage)
        => new() { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = target, HitDamage = { [target] = damage }, Hits = 1 };

    [Fact]
    public void TheCrabBehindYouHitsHalfAgainAsHard()
        => Assert.Equal(8 + 15, SimEngine.Run(Fight()).TakenMax);

    [Fact]
    public void AimingAtTheCrabBehindYouTurnsYou()
    {
        // Strike the Rocket (behind you): you face it, so the Crusher is now behind you.
        Assert.Equal(12 + 10, SimEngine.Run(Fight(), Strike(2, 1)).TakenMax);
    }

    [Fact]
    public void KillingTheCrabInFrontTurnsYouToFaceTheOneLeft()
    {
        // Issue #336: the Crusher dies, only the Rocket is left, so you turn to face it and it no longer back-attacks.
        var s = Fight();
        SimEngine.ApplyPlayTo(s, Strike(1, 5), out var after, out _);
        Assert.Equal(0, after[0].P.Powers.DataOf(PowerId.Surrounded));
        Assert.Equal(10, SimEngine.Run(s, Strike(1, 5)).TakenMax);
    }

    [Fact]
    public void KillingTheCrabInFrontWhileFacingRightTurnsYouLeft()
    {
        // Facing right, the Crusher is behind you. Killing the Rocket in front leaves only the Crusher: you turn to face it.
        var s = Fight(leftHp: 100, rightHp: 5);
        s.P.Powers = s.P.Powers.With(PowerId.Surrounded, 1, 0); // facing right: the Crusher is behind you
        SimEngine.ApplyPlayTo(s, new SimPlay { Name = "Bash", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 2, HitDamage = { [2] = 5 }, Hits = 1 }, out var after, out _);
        // The Rocket was in front (no turn on the play); with only the Crusher left, you turn to face it.
        Assert.Equal(1, after[0].P.Powers.DataOf(PowerId.Surrounded));
    }

    [Fact]
    public void ADeathWithBothSidesStillThereDoesntTurnYou()
    {
        // A third enemy without a back-attack marker dies: the crabs are on both sides, so you keep facing left.
        var s = Fight();
        s.Enemies.Insert(0, new SimEnemy { Id = 3, Name = "Add", Hp = 3, Hits = new() { 2 }, Intent = new() { new(2) } });
        SimEngine.ApplyPlayTo(s, Strike(3, 3), out var after, out _);
        Assert.Equal(1, after[0].P.Powers.DataOf(PowerId.Surrounded));
    }
}
