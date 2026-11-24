using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The board the simulator keeps, as conditions and amounts asked when the step runs (decision #190): your exhaust pile (Pact's End), the
/// target's powers (Bubble Bubble's poison, Molten Fist's Vulnerable as its amount) and its intent to attack (Go for the Eyes).
/// </summary>
public class SimBoardConditionTests
{
    private static SimState Board(int enemyHp = 60)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = enemyHp, IntendsToAttack = true, Hits = { 5 } } } };
        state.You.HandCount = 4;
        return state;
    }

    private static AttackAction Hit(int damage) =>
        new(Hits: 1, SimPlay.Target.Single, new DamageSpec(Base: damage, SimProps.Move));

    private static SimPlay Card(string name, int plays, params SimAction[] body)
    {
        var play = new SimPlay { Name = name, TargetId = 0, HandDelta = 0, Plays = plays };
        play.Body.AddRange(body);
        return play;
    }

    [Fact]
    public void PactsEndHitsOnceTheExhaustPileHoldsEnough()
    {
        // Pact's End: its attack only with 3 cards in your exhaust pile. Two there, and a card the play exhausts first: it hits.
        var enough = new CountCondition(SimCounted.ExhaustPile, SimCompare.GreaterOrEqual, Than: 3);
        var pact = Card("PactsEnd", plays: 1, new WhenAction(enough, new SimAction[] { Hit(damage: 17) }));
        var two = Board();
        two.You.ExhaustPile = 2;
        Assert.Equal(60, Assert.Single(SimEngine.Run(two, pact).Branches).Enemies[0].Hp);
        var exhaustingFirst = Card("PactsEnd", plays: 1, new CardEventAction(CardEvent.Exhausted, Count: 1) { Indirect = true }, new WhenAction(enough, new SimAction[] { Hit(damage: 17) }));
        Assert.Equal(43, Assert.Single(SimEngine.Run(two, exhaustingFirst).Branches).Enemies[0].Hp);
    }

    [Fact]
    public void BubbleBubblePoisonsOnlyAPoisonedTarget()
    {
        // Bubble Bubble: poison only if the target has poison (HasPower<PoisonPower>), asked as each play runs.
        var poisoned = new CountCondition(SimCounted.TargetPower, SimCompare.Greater, Than: 0) { Power = PowerId.Poison };
        var bubble = Card("BubbleBubble", plays: 2, new WhenAction(poisoned, new SimAction[] { new ApplyPowerAction(PowerId.Poison, Amount: 9) { Debuff = true } }));
        SimEngine.RunScriptTo(Board(), SimScript.Of(bubble), out var clean, out _);
        Assert.Equal(0, Assert.Single(clean).Enemies[0].PoisonStacks);
        var already = Board();
        already.Enemies[0].AddPoison(2);
        SimEngine.RunScriptTo(already, SimScript.Of(bubble), out var after, out _);
        Assert.Equal(2 + 9 + 9, Assert.Single(after).Enemies[0].PoisonStacks);
    }

    [Fact]
    public void MoltenFistDoublesTheVulnerableItFindsOnEachPlay()
    {
        // Molten Fist: Vulnerable equal to the target's (when it's alive and has some). Replayed three times: 2, then 4, then 8.
        var vulnerable = new CountCondition(SimCounted.TargetPower, SimCompare.Greater, Than: 0) { Power = PowerId.Vulnerable };
        var fist = Card("MoltenFist", plays: 3, new WhenAction(vulnerable, new SimAction[] { new ApplyPowerAction(PowerId.Vulnerable, Amount: 1) { Debuff = true, CopiesTarget = true } }));
        var state = Board();
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.Vulnerable, amount: 1);
        SimEngine.RunScriptTo(state, SimScript.Of(fist), out var after, out _);
        Assert.Equal(8, Assert.Single(after).Enemies[0].Powers[PowerId.Vulnerable]);
        SimEngine.RunScriptTo(Board(), SimScript.Of(fist), out var none, out _);
        Assert.Equal(0, Assert.Single(none).Enemies[0].Powers[PowerId.Vulnerable]);
    }

    [Fact]
    public void GoForTheEyesWeakensOnlyAnAttackerStillAttacking()
    {
        // Go for the Eyes: Weak only if the target intends to attack; a stun replaces its move (CreatureCmd.Stun), so a stunned one doesn't.
        var attacks = new TargetIntendsToAttackCondition();
        var eyes = Card("GoForTheEyes", plays: 1, new WhenAction(attacks, new SimAction[] { new ApplyPowerAction(PowerId.Weak, Amount: 1) { Debuff = true } }));
        SimEngine.RunScriptTo(Board(), SimScript.Of(eyes), out var attacking, out _);
        Assert.Equal(1, Assert.Single(attacking).Enemies[0].Powers[PowerId.Weak]);
        var stunned = Board();
        stunned.Enemies[0].Stunned = true;
        SimEngine.RunScriptTo(stunned, SimScript.Of(eyes), out var after, out _);
        Assert.Equal(0, Assert.Single(after).Enemies[0].Powers[PowerId.Weak]);
        var buffing = Board();
        buffing.Enemies[0].IntendsToAttack = false;
        SimEngine.RunScriptTo(buffing, SimScript.Of(eyes), out var notAttacking, out _);
        Assert.Equal(0, Assert.Single(notAttacking).Enemies[0].Powers[PowerId.Weak]);
    }
}
