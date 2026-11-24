using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A card in the hand made free this turn, read from its code (FreeInHandStep): MummifiedHand.AfterCardPlayed, on a Power its owner plays, calls
/// SetToFreeThisTurn on CombatCardSelection.NextItem over the hand's cards that cost energy or stars (CostsEnergyOrStars: an X cost doesn't), else
/// the rest. Which card is a roll: a branch for each, where a simulation reads the cost (Simpleton's cheapest card at the turn's end).
/// </summary>
public class HandCardFreeTests
{
    private static SimState Board()
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 6, Hits = new List<int> { 4 } } } };
        state.You.Relics = state.You.Relics.With(RelicId.MummifiedHand, amount: 0, data: 0);
        state.You.HandCount = 3;
        return state;
    }

    private static SimPlay Power(bool othersCost) => new()
    {
        Name = "Inflame", HandCard = 7, HandDelta = -1,
        Card = new SimCardPlay(new SimCard("Inflame", SimCardType.Power) { OthersInHandCost = othersCost, HandId = 7 }, Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false),
    };

    private static SimHandCard Strike(int id, int cost) => new()
    {
        Id = id, Name = "Strike", IsAttack = true, Cost = cost,
        Plays = { new SimPlay { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 1, HitDamage = { [1] = 6 }, Hits = 1, HandCard = id, HandDelta = -1 } },
    };

    private static SimHandCard Defend(int id, int cost) => new() { Id = id, Name = "Defend", Cost = cost, Plays = { new SimPlay { Name = "Defend", BlockAmount = 5, BlockGains = 1, HandCard = id, HandDelta = -1 } } };

    [Fact]
    public void APowerSetsItOffWhenAnotherCardCosts()
    {
        SimEngine.ApplyPlayTo(Board(), Power(othersCost: true), out var after, out _);
        Assert.Equal(1, Assert.Single(after).Fired[RelicId.MummifiedHand.Name]);
        // Nothing else in the hand costs: the card it makes free changes nothing (its tab stays off, as before).
        SimEngine.ApplyPlayTo(Board(), Power(othersCost: false), out after, out _);
        Assert.False(Assert.Single(after).Fired.Has(RelicId.MummifiedHand.Name));
    }

    [Fact]
    public void SimpletonsCheapestCardIsTheOneMadeFreeInABranch()
    {
        // The Defend costs 1 and the Strike 2: Simpleton would play the Defend. Mummified Hand makes one of them free, each equally likely: the
        // Defend free, still the Defend (5 block); the Strike free, the Strike now the cheapest (the enemy dies). A kill in some branches, not all.
        var state = Board();
        state.You.Simpleton = 1;
        state.You.AutoHand = new() { Defend(id: 12, cost: 1), Strike(id: 11, cost: 2) };
        var before = SimEngine.Run(state);
        Assert.False(before.DeadInAny(enemyId: 1));
        var outcome = SimEngine.Run(state, Power(othersCost: true));
        Assert.True(outcome.DeadInAny(enemyId: 1));
        Assert.False(outcome.DeadInAll(enemyId: 1));
        Assert.Equal(0.5, outcome.Branches.Where(branch => !branch.Enemy(id: 1)!.Alive).Sum(branch => branch.Weight), precision: 6);
    }

    [Fact]
    public void AnXCostIsntACardThatCosts()
    {
        // CostsEnergyOrStars: an X cost doesn't (EnergyCost.CostsX). The only card that costs is the Defend: it's the one made free.
        var state = Board();
        state.You.Simpleton = 1;
        state.You.AutoHand = new() { Defend(id: 12, cost: 1), Strike(id: 11, cost: 999999999) };
        var outcome = SimEngine.Run(state, Power(othersCost: true));
        Assert.False(outcome.DeadInAny(enemyId: 1));
        Assert.Single(outcome.Branches);
    }
}
