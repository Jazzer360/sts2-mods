using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Cards auto-played from the top of your draw pile (AutoPlayFromDrawAction; decompile v0.113.1: CardPileCmd.AutoPlayFromDrawPile at
/// CardPilePosition.Top, from Havoc, Cascade, The Devil and Lost Lute's power): the known top card, else a branch per profile by its
/// count; each plays free (CardCmd.AutoPlay), then goes to the discard pile, or the exhaust pile for Havoc (ExhaustOnNextPlay). An empty
/// draw pile shuffles the discard pile in first.
/// </summary>
public class SimAutoPlayFromDrawTests
{
    private static SimPlay Hits(int damage, int target = 1) => new()
    {
        Name = "StrikeIronclad", IsAttack = true, AutoPlay = true, HandDelta = 0, TargetId = target,
        Card = new SimCardPlay(new SimCard("StrikeIronclad", SimCardType.Attack), Who.You, target, PlayIndex: 0, PlayCount: 1, AutoPlay: true),
        Body = { new AttackAction(Hits: 1, SimPlay.Target.Single, new DamageSpec(Base: 0m, SimProps.Move) { BaseOn = new Dictionary<int, decimal> { [target] = damage }, Fixed = true }) },
    };

    private static SimPlay Blocks(int block) => new()
    {
        Name = "DefendIronclad", AutoPlay = true, HandDelta = 0, TargetId = Who.None,
        Card = new SimCardPlay(new SimCard("DefendIronclad", SimCardType.Skill), Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: true),
        Body = { new GainBlockAction(Times: 1, block, SimProps.Move) },
    };

    private static readonly SimDrawCard Strike = new("strike") { FromPile = new SimHandCard { Name = "StrikeIronclad", IsAttack = true, Plays = { Hits(damage: 6) } } };
    private static readonly SimDrawCard Defend = new("defend") { FromPile = new SimHandCard { Name = "DefendIronclad", Plays = { Blocks(block: 5) } } };
    private static readonly SimDrawCard Unbuilt = new("unbuilt");

    private static SimState You(List<SimDrawGroup> rest, List<SimDrawGroup>? discard = null)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50 } } };
        state.You.HandCount = 5;
        state.You.Hp = 50;
        state.You.Draw = new SimDrawPile { Rest = rest, Discard = discard ?? new() };
        return state;
    }

    private static SimPlay Havoc(int count = 1, bool exhaust = true) => new()
    {
        Name = "Havoc", HandCard = 7, HandDelta = -1,
        Body = { new AutoPlayFromDrawAction(count) { ForceExhaust = exhaust } },
    };

    [Fact]
    public void HavocPlaysTheTopCardAndExhaustsIt()
    {
        var outcome = SimEngine.Run(You(new() { new SimDrawGroup(Strike, 1) }), Havoc());
        var branch = Assert.Single(outcome.Branches);
        Assert.Equal(44, branch.Enemy(id: 1)!.Hp);
        Assert.Equal(0, branch.You.Draw!.Count);
        Assert.Equal(0, branch.You.Draw.DiscardCount); // exhausted, not discarded
        Assert.Equal(1, branch.AutoPlayed);
    }

    [Fact]
    public void AnUnknownTopIsABranchPerProfileAndTheCardIsDiscarded()
    {
        // Cascade at X 1 over a Strike and three Defends: the Strike 1/4.
        var outcome = SimEngine.Run(You(new() { new SimDrawGroup(Strike, 1), new SimDrawGroup(Defend, 3) }), Havoc(exhaust: false));
        Assert.Equal(2, outcome.Branches.Count);
        var hit = outcome.Chance(branch => branch.Enemy(id: 1)!.Hp < 50);
        Assert.Equal(0.25, hit.Low, precision: 9);
        Assert.Equal(0.25, hit.High, precision: 9);
        Assert.All(outcome.Branches, branch => Assert.Equal(1, branch.You.Draw!.DiscardCount));
    }

    [Fact]
    public void AnEmptyDrawPileShufflesTheDiscardPileInFirst()
    {
        var outcome = SimEngine.Run(You(new(), discard: new() { new SimDrawGroup(Strike, 1) }), Havoc());
        Assert.Equal(44, Assert.Single(outcome.Branches).Enemy(id: 1)!.Hp);
    }

    [Fact]
    public void ACardWhosePlayIsntBuiltIsUnknown()
    {
        var outcome = SimEngine.Run(You(new() { new SimDrawGroup(Unbuilt, 1) }), Havoc());
        Assert.NotNull(Assert.Single(outcome.Branches).Unmodelled);
    }
}
