using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A power a hook puts on the player who played the card (cardPlay.Card.Owner.Creature: HookTarget.PlayOwner), read from its code: Vital Spark's
/// Tainted on whoever plays a Tainted card (VitalSparkPower.AfterCardPlayed: PowerCmd.Apply&lt;TaintedPower&gt;(ctx, cardPlay.Card.Owner.Creature,
/// base.Amount, null, null)), applied by no one, a debuff (TaintedPower.Type).
/// </summary>
public class PlayOwnerPowerTests
{
    private static SimState Board()
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 60, Hits = new List<int> { 6 } } } };
        state.Enemies[0].Intent = new List<IntentHit> { new(Base: 6) }; // its hit through the damage math: Tainted raises it
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.VitalSpark, amount: 2);
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        state.You.HandCount = 3;
        state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 60, HandCount = 2 });
        return state;
    }

    private static SimPlay Defend(int owner, SimAffliction affliction) => new()
    {
        Name = "Defend", HandCard = 7, HandDelta = -1,
        Card = new SimCardPlay(new SimCard("Defend", SimCardType.Skill) { Affliction = affliction }, owner, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false),
    };

    [Fact]
    public void ATaintedCardPutsTaintedOnItsPlayer()
    {
        // Vital Spark's 2: the enemy's 6 lands as 8 on you (TaintedPower.ModifyDamageAdditive on a powered attack at its owner).
        SimEngine.ApplyPlayTo(Board(), Defend(Who.You, SimAffliction.Tainted), out var after, out _);
        Assert.Equal(2, Assert.Single(after).You.Powers[PowerId.Tainted]);
        Assert.Equal(8, SimEngine.Run(Board(), Defend(Who.You, SimAffliction.Tainted)).TakenMax);
        // A card that isn't Tainted: nothing.
        SimEngine.ApplyPlayTo(Board(), Defend(Who.You, SimAffliction.None), out after, out _);
        Assert.False(Assert.Single(after).You.Powers.Has(PowerId.Tainted));
    }

    [Fact]
    public void ATeammatesTaintedCardPutsTaintedOnThem()
    {
        SimEngine.ApplyPlayTo(Board(), Defend(Who.Teammate(0), SimAffliction.Tainted), out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(2, branch.Teammates[0].Powers[PowerId.Tainted]);
        Assert.False(branch.You.Powers.Has(PowerId.Tainted));
    }

    [Fact]
    public void ArtifactStopsIt()
    {
        // TaintedPower is a Debuff (its Type): ArtifactPower.TryModifyPowerAmountReceived stops it and goes down by one, whoever applied it.
        var state = Board();
        state.You.Powers = PowerSet.Empty.With(PowerId.Artifact, amount: 1);
        SimEngine.ApplyPlayTo(state, Defend(Who.You, SimAffliction.Tainted), out var after, out _);
        var branch = Assert.Single(after);
        Assert.False(branch.You.Powers.Has(PowerId.Tainted));
        Assert.False(branch.You.Powers.Has(PowerId.Artifact));
    }
}
