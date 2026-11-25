using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A power's amount as it's given (Hook.ModifyPowerAmountGiven, decision #287): every listener's ModifyPowerAmountGivenAdditive on the amount so far,
/// then every listener's ModifyPowerAmountGivenMultiplicative, then Hook.AfterModifyingPowerAmountGiven told to each that changed it. Snecko Skull
/// is read from its code: `power is PoisonPower` and `giver == base.Owner.Creature`, its Poison value added.
/// </summary>
public class PowerAmountGivenTests
{
    private static SimState Board(params RelicId[] relics)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 40, Hits = new List<int> { 3 }, IntendsToAttack = true } } };
        foreach (var relic in relics)
            state.You.Relics = relic == RelicId.UnsettlingLamp ? state.You.Relics.With(relic, 0, (int)RelicUse.Armed) : state.You.Relics.With(relic, 0);
        return state;
    }

    /// <summary>A card of yours that poisons the enemy, and the poison it has once the play is over (before its turn's tick).</summary>
    private static int PoisonGiven(SimState state, int poison)
    {
        var play = new SimPlay { Name = "Played", TargetId = 0, Poison = poison, Card = new SimCardPlay(new SimCard("Played", SimCardType.Skill) { Energy = 1 }, Who.You, 0, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
        // The enemy's turn ticks its poison by 1 (PoisonPower.AfterSideTurnStart): what was given is one more than what's left.
        return SimEngine.Run(state, play).Branches.Single().Enemy(0)!.Powers[PowerId.Poison] + 1;
    }

    [Fact]
    public void SneckoSkullAddsItsPoisonToWhatYouGive()
    {
        Assert.Equal(3, PoisonGiven(Board(), poison: 3));
        Assert.Equal(4, PoisonGiven(Board(RelicId.SneckoSkull), poison: 3));
        Assert.IsType<ReadModel>(SimModels.Get(RelicId.SneckoSkull.Name));
    }

    [Fact]
    public void TheAdditiveTermsComeBeforeTheMultiplicativeOnesWhateverTheRelicsOrder()
    {
        // Snecko Skull's +1, then Unsettling Lamp's doubling: (3 + 1) × 2 either way. The engine's one pass in the relics' order gave 3 × 2 + 1 = 7
        // with the Lamp first (the recording in PowerGivenTabParityTests, reviewed by its LampBeforeSkull).
        Assert.Equal(8, PoisonGiven(Board(RelicId.SneckoSkull, RelicId.UnsettlingLamp), poison: 3));
        Assert.Equal(8, PoisonGiven(Board(RelicId.UnsettlingLamp, RelicId.SneckoSkull), poison: 3));
    }

    [Fact]
    public void SneckoSkullAddsOnlyToPoisonItsOwnerGives()
    {
        // power is PoisonPower and giver == base.Owner.Creature: a Weak isn't Poison, and an enemy's Poison on you isn't yours.
        var state = Board(RelicId.SneckoSkull);
        state.CurrentPlay = new SimCardPlay(new SimCard("Played", SimCardType.Skill), Who.You, 0, PlayIndex: 0, PlayCount: 1, AutoPlay: false);
        Assert.Equal(5, SimEngine.AmountGiven(state, Who.You, target: 0, PowerId.Poison, amount: 4, debuff: true));
        Assert.Equal(2, SimEngine.AmountGiven(state, Who.You, target: 0, PowerId.Weak, amount: 2, debuff: true));
        Assert.Equal(4, SimEngine.AmountGiven(state, applier: 0, target: Who.You, PowerId.Poison, amount: 4, debuff: true));
    }
}
