using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// What PowerCmd does with a power's amount, read from each power class's code (PowerRule, decision #234): PowerCmd.Apply scales a new one on an
/// enemy for the players when its class's ShouldScaleInMultiplayer is true (GetScaledAmountForMultiplayer: its own, or PowerModel's amount ×
/// players × the fight's scaling), and PowerCmd.ModifyAmount takes one away once ShouldRemoveDueToAmount says so (at 0 for a power that can go
/// below 0, its AllowNegative; at 0 or below for any other).
/// </summary>
public class PowerCmdRuleTests
{
    private static SimEnemy Enemy(int id, int hp) => new() { Id = id, Name = $"E{id}", Hp = hp };

    private static SimState TwoPlayers(params SimEnemy[] enemies)
    {
        var state = new SimState { EncounterScaling = 1.2m };
        state.Enemies.AddRange(enemies);
        state.Teammates.Add(new SimPlayer { Hp = 50, MaxHp = 50 });
        return state;
    }

    [Fact]
    public void ASpawnsStartingPowersAreScaledForThePlayers()
    {
        // Its AfterAddedToRoom's PowerCmd.Apply<SlipperyPower>(creature, 2): SlipperyPower.GetScaledAmountForMultiplayer is amount × players, 4.
        var state = TwoPlayers(Enemy(id: 0, hp: 5));
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Surprise, amount: 1);
        state.Enemies[0].Spawns = new List<SimSpawn> { new("Slick", new[] { 20 }) { Powers = PowerSet.Empty.With(PowerId.Slippery, amount: 2) } };
        var strike = new SimPlay { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 0, Hits = 1, HitDamage = { [0] = 6 } };
        var branch = Assert.Single(SimEngine.Run(state, strike).Branches);
        Assert.Equal(4, branch.Enemy(id: 1)!.Powers[PowerId.Slippery]);
    }

    [Fact]
    public void AMovesNewPowerOnAnEnemyIsScaledAndMoreOfOneIsnt()
    {
        // PowerCmd.Apply scales a new one (PowerModel's own scaling, Plow's: 10 × 2 players × 1.2 = 24); PowerCmd.ModifyAmount adds to one there unscaled.
        var state = TwoPlayers(Enemy(id: 0, hp: 50), Enemy(id: 1, hp: 50));
        state.Enemies[1].Powers = PowerSet.Empty.With(PowerId.Plow, amount: 5);
        state.Enemies[0].Move = new SimAction[] { new MovePowerAction(PowerId.Plow, Amount: 10, To: new[] { 0, 1 }, Debuff: false) };
        var branch = Assert.Single(SimEngine.Run(state).Branches);
        Assert.Equal(24, branch.Enemy(id: 0)!.Powers[PowerId.Plow]);
        Assert.Equal(15, branch.Enemy(id: 1)!.Powers[PowerId.Plow]);
    }

    [Fact]
    public void ScalingWithTheFightsScalingUnreadIsUnknown()
    {
        var state = TwoPlayers(Enemy(id: 0, hp: 50));
        state.EncounterScaling = null;
        state.Enemies[0].Move = new SimAction[] { new MovePowerAction(PowerId.Plow, Amount: 10, To: new[] { 0 }, Debuff: false) };
        Assert.NotNull(Assert.Single(SimEngine.Run(state).Branches).Unmodelled);
    }

    [Fact]
    public void OnePlayersPowersAreNeverScaled()
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50) } };
        state.Enemies[0].Move = new SimAction[] { new MovePowerAction(PowerId.Plow, Amount: 10, To: new[] { 0 }, Debuff: false) };
        Assert.Equal(10, Assert.Single(SimEngine.Run(state).Branches).Enemy(id: 0)!.Powers[PowerId.Plow]);
    }

    [Fact]
    public void APowerLeftAtZeroGoes()
    {
        // StrengthPower.AllowNegative: gone at exactly 0, kept below it; VulnerablePower can't go below 0: gone at 0 or below.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50) } };
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Strength, amount: 2).With(PowerId.Vulnerable, amount: 1);
        SimEngine.ApplyPower(state, applier: Who.You, target: 0, PowerId.Strength, amount: -2, debuff: true);
        Assert.False(state.Enemies[0].Powers.Has(PowerId.Strength));
        SimEngine.ApplyPower(state, applier: Who.You, target: 0, PowerId.Strength, amount: -3, debuff: true);
        Assert.Equal(-3, state.Enemies[0].Powers[PowerId.Strength]);
        SimEngine.ApplyPower(state, applier: Who.You, target: 0, PowerId.Vulnerable, amount: -2, debuff: false);
        Assert.False(state.Enemies[0].Powers.Has(PowerId.Vulnerable));
    }
}
