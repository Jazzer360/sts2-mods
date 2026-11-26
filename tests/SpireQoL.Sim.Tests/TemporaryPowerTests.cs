using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Decision #400. A temporary Strength power read from its base class's hooks (TemporaryStrengthPower.AfterSideTurnEnd: participants.Contains(base.Owner):
/// Flash(); PowerCmd.Remove(this); PowerCmd.Apply&lt;StrengthPower&gt;(base.Owner, -Sign * base.Amount)): the game's instance keeps its Amount once it's off
/// (PowerModel.RemoveInternal), so the Strength given back is the amount it had. And an enemy move's step on a test its code made once, before an earlier
/// step changed what it tests (WhenAction.AskedAtMoveStart: Entomancer's Pheromone Spit).
/// </summary>
public class TemporaryPowerTests
{
    private static SimEnemy Attacker(int hit) =>
        new() { Id = 0, Name = "E0", Hp = 40, Hits = new() { hit }, Intent = new List<IntentHit> { new(Base: hit) }, IntendsToAttack = true };

    private static SimState AfterTheTurn(SimState state) => Assert.Single(SimEngine.Run(state).Branches);

    [Fact]
    public void PiercingWailOnAnEnemyGivesItsStrengthBackAsItsTurnEnds()
    {
        // Piercing Wail's -6 (PiercingWailPower 6, IsPositive false: Sign -1) weakens this turn's attack (10 - 6 = 4), then gives the 6 back.
        var enemy = Attacker(hit: 10);
        enemy.Powers = PowerSet.Empty.With(PowerId.Strength, amount: -6).With(PowerId.PiercingWail, amount: 6);
        var state = new SimState { Enemies = { enemy } };
        state.You.Hp = 60;
        var after = AfterTheTurn(state);
        Assert.Null(after.Unmodelled);
        Assert.Equal(4, after.Taken);
        Assert.False(after.Enemy(id: 0)!.Powers.Has(PowerId.PiercingWail));
        Assert.Equal(0, after.Enemy(id: 0)!.Powers[PowerId.Strength]);
        Assert.Null(after.RemovedByItsHook);
    }

    [Fact]
    public void FlexPotionOnYouTakesItsStrengthAwayAsYourTurnEnds()
    {
        // Flex Potion's 5 (IsPositive true: Sign 1): as your turn ends it takes the 5 away (-1 * 5), your own Strength left.
        var state = new SimState { Enemies = { Attacker(hit: 0) } };
        state.You.Hp = 60;
        state.You.Powers = PowerSet.Empty.With(PowerId.Strength, amount: 7).With(PowerId.FlexPotion, amount: 5);
        var after = AfterTheTurn(state);
        Assert.Null(after.Unmodelled);
        Assert.False(after.You.Powers.Has(PowerId.FlexPotion));
        Assert.Equal(2, after.You.Powers[PowerId.Strength]);
    }

    [Theory]
    [InlineData(1, 2, 1)]
    [InlineData(2, 3, 1)]
    [InlineData(3, 3, 2)]
    public void AStepAskedAtTheMovesStartIsntChangedByTheStepBeforeIt(int hive, int hiveAfter, int strength)
    {
        // Pheromone Spit: below 3 Personal Hive, one more and 1 Strength; else 2 Strength. The hive at 2 becomes 3 after its first step, and the Strength
        // steps are still decided by the 2 it had.
        var below = new CountCondition(SimCounted.MonsterPower, SimCompare.Less, Than: 3) { Power = PowerId.PersonalHive };
        var enemy = new SimEnemy { Id = 0, Name = "Entomancer", Hp = 40, Hits = new(), IntendsToAttack = false };
        enemy.Powers = PowerSet.Empty.With(PowerId.PersonalHive, hive);
        enemy.Move = new SimAction[]
        {
            new WhenAction(below, new SimAction[] { new MovePowerAction(PowerId.PersonalHive, Amount: 1, To: new[] { 0 }, Debuff: false) }),
            new WhenAction(below, new SimAction[] { new MovePowerAction(PowerId.Strength, Amount: 1, To: new[] { 0 }, Debuff: false) }) { AskedAtMoveStart = true },
            new WhenAction(new NotCondition(below), new SimAction[] { new MovePowerAction(PowerId.Strength, Amount: 2, To: new[] { 0 }, Debuff: false) }) { AskedAtMoveStart = true },
        };
        var state = new SimState { Enemies = { enemy } };
        state.You.Hp = 60;
        var after = AfterTheTurn(state).Enemy(id: 0)!;
        Assert.Equal(hiveAfter, after.Powers[PowerId.PersonalHive]);
        Assert.Equal(strength, after.Powers[PowerId.Strength]);
    }
}
