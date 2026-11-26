using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A hand card's live energy cost (decision #334, CardEnergyCost.GetWithModifiers(CostModifiers.All)): its cost before global modifiers, then
/// Hook.ModifyEnergyCostInCombat's two passes over every listener in the game's order (TryModifyEnergyCostInCombat, then every Late), at least 0:
/// Curious, Borrowed Time and Corruption read from their code. Simpleton's pick and Mummified Hand's ask it as
/// the state stands then, so a cost a play changes is the one picked by; a pass the simulator doesn't run makes what asks unknown.
/// </summary>
public class HandCostTests
{
    private static SimHandCard Card(int id, string name, SimCardType type, int cost, params SimPlay[] plays) => new()
    {
        Id = id, Name = name, IsAttack = type == SimCardType.Attack, Cost = cost, LocalCost = cost, Card = new SimCard(name, type), Plays = plays.ToList(),
    };

    private static SimEnemy Enemy(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    private static SimPlay Strike(int target, int damage, int handCard) => new()
    {
        Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = target, HitDamage = { [target] = damage }, Hits = 1, HandCard = handCard, HandDelta = -1,
    };

    private static SimState WithPowers(params (PowerId Power, int Amount)[] powers)
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 6, hits: 4) } };
        foreach (var (power, amount) in powers) state.You.Powers = state.You.Powers.With(power, amount);
        return state;
    }

    private static int? Cost(SimState state, SimCardType type, int cost, int player = Who.You) =>
        SimEngine.HandCost(state, player, Card(id: 3, "Costed", type, cost));

    [Fact]
    public void CuriousLowersItsOwnersPowersThatCostSomething()
    {
        // CuriousPower.TryModifyEnergyCostInCombat: its owner's Powers that cost more than 0 cost its amount less, never below 0.
        var state = WithPowers((PowerId.Curious, 1));
        Assert.IsType<ReadModel>(SimModels.Get(PowerId.Curious.Name));
        Assert.Equal(1, Cost(state, SimCardType.Power, cost: 2));
        Assert.Equal(0, Cost(state, SimCardType.Power, cost: 0));
        Assert.Equal(2, Cost(state, SimCardType.Skill, cost: 2));
        Assert.Equal(0, Cost(WithPowers((PowerId.Curious, 3)), SimCardType.Power, cost: 2));
    }

    [Fact]
    public void BorrowedTimeRaisesItsOwnersCardsAlone()
    {
        // BorrowedTimePower.TryModifyEnergyCostInCombat: card.Owner.Creature == base.Owner, its amount more. A teammate's card isn't its owner's.
        var state = WithPowers((PowerId.BorrowedTime, 1));
        state.Teammates.Add(new SimPlayer { Hp = 40 });
        Assert.Equal(2, Cost(state, SimCardType.Attack, cost: 1));
        Assert.Equal(1, Cost(state, SimCardType.Attack, cost: 1, player: Who.Teammate(teammateIndex: 0)));
    }

    [Fact]
    public void TheLatePassRunsAfterEveryFirstPass()
    {
        // Borrowed Time's +1 in the first pass, then Corruption's Late pass sets its owner's Skill to 0: 0, whichever power came first.
        Assert.IsType<ReadModel>(SimModels.Get(PowerId.Corruption.Name));
        Assert.Equal(0, Cost(WithPowers((PowerId.Corruption, 1), (PowerId.BorrowedTime, 1)), SimCardType.Skill, cost: 1));
        Assert.Equal(2, Cost(WithPowers((PowerId.Corruption, 1), (PowerId.BorrowedTime, 1)), SimCardType.Attack, cost: 1));
    }

    [Fact]
    public void SimpletonPicksByTheCostAPlayLeaves()
    {
        // The Strike costs 1, the Defend 2: Simpleton plays the Strike (the enemy dies). A play that gives you Corruption makes the Defend 0 by the
        // turn's end: Simpleton plays the Defend (5 block, the enemy lives).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 6, hits: 4) } };
        state.You.Simpleton = 1;
        state.You.AutoHand = new()
        {
            Card(id: 11, "Strike", SimCardType.Attack, cost: 1, Strike(target: 1, damage: 6, handCard: 11)),
            Card(id: 12, "Defend", SimCardType.Skill, cost: 2, new SimPlay { Name = "Defend", BlockAmount = 5, BlockGains = 1, HandCard = 12, HandDelta = -1 }),
        };
        Assert.True(SimEngine.Run(state).DeadInAll(enemyId: 1));
        var corruption = new SimPlay
        {
            Name = "Corruption", HandDelta = -1,
            Card = new SimCardPlay(new SimCard("Corruption", SimCardType.Power), Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false),
            Body = { new ApplyPowerAction(PowerId.Corruption, Amount: 1) { To = ApplyPowerAction.Where.You } },
        };
        var outcome = SimEngine.Run(state, corruption);
        Assert.False(outcome.DeadInAny(enemyId: 1));
        Assert.All(outcome.Branches, branch => Assert.Null(branch.Unmodelled));
    }

    [Fact]
    public void ACostPassTheSimulatorDoesntRunMakesThePickUnknown()
    {
        // A listener whose cost pass isn't run (SimState.CostsUnknown: a class not read whole, a card's own hook): Simpleton's pick is unknown.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 6, hits: 4) }, CostsUnknown = "ChoLimbs's TryModifyEnergyCostInCombat isn't run" };
        state.You.Simpleton = 1;
        state.You.AutoHand = new() { Card(id: 11, "Strike", SimCardType.Attack, cost: 1, Strike(target: 1, damage: 6, handCard: 11)) };
        Assert.All(SimEngine.Run(state).Branches, branch => Assert.Equal(state.CostsUnknown, branch.Unmodelled));
    }

    [Fact]
    public void AFreeCopyKeepsTheListenersPasses()
    {
        // CardModel.SetToFreeThisTurn is a local modifier of 0: Borrowed Time's +1 still lands on it.
        var free = Card(id: 3, "Costed", SimCardType.Attack, cost: 2).FreeCopy();
        Assert.Equal(1, SimEngine.HandCost(WithPowers((PowerId.BorrowedTime, 1)), Who.You, free));
    }

    [Fact]
    public void ACardReadBeforeTheRuleCostsWhatTheGameShowed()
    {
        // A replay case's hand card with no local cost: the cost the game showed when it was read.
        var old = new SimHandCard { Id = 3, Name = "Old", Cost = 2 };
        Assert.Equal(2, SimEngine.HandCost(WithPowers((PowerId.Corruption, 1)), Who.You, old));
    }
}
