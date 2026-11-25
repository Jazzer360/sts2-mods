using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// PlowPower read from its code (decision #275). AfterDamageReceived: `target == base.Owner`, HP through (`result.UnblockedDamage > 0`) and its owner at or
/// below its amount (`target.CurrentHp &lt;= base.Amount`): Flash(); every Temporary Strength of its owner's removed (a loop over
/// GetPowerInstances&lt;TemporaryStrengthPower&gt;().ToList(), PowerCmd.Remove of each), its Strength removed (PowerCmd.Remove&lt;StrengthPower&gt;); on
/// Ceremonial Beast its own SetStunned (two flags its looks read; RitualPower of its RitualAmount, 2 on Dread, applied to itself when above 0) then a
/// stun with its own stunned move, else a stun; then the power goes.
/// </summary>
public class PlowTests
{
    private static SimState Fight(int hp, int plow)
    {
        var state = new SimState { TurnNumber = 1 };
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        var enemy = new SimEnemy { Id = 0, Name = "Beast", Hp = hp, Hits = new() { 18 }, IntendsToAttack = true };
        enemy.Powers = enemy.Powers.With(PowerId.Plow, plow).With(PowerId.Strength, amount: 4);
        state.Enemies.Add(enemy);
        return state;
    }

    private static SimPlay Strike(int damage)
    {
        var play = new SimPlay { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = 0, Hits = 1,
            Card = new SimCardPlay(new SimCard("Strike", SimCardType.Attack) { Energy = 1 }, Who.You, Target: 0, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
        play.HitDamage[0] = damage;
        return play;
    }

    private static SimEnemy After(SimState state, SimPlay play)
    {
        SimEngine.ApplyPlayTo(state, play, out var branches, out _);
        var branch = Assert.Single(branches);
        Assert.Null(branch.Unmodelled);
        return branch.Enemy(id: 0)!;
    }

    [Fact]
    public void ItsThresholdStunsItAndTakesItsStrength()
    {
        var beast = After(Fight(hp: 15, plow: 10), Strike(damage: 6));
        Assert.True(beast.Stunned);
        Assert.False(beast.Powers.Has(PowerId.Strength));
        Assert.False(beast.Powers.Has(PowerId.Plow));
        // Above its amount: nothing.
        var above = After(Fight(hp: 20, plow: 10), Strike(damage: 6));
        Assert.False(above.Stunned);
        Assert.Equal(4, above.Powers[PowerId.Strength]);
    }

    [Fact]
    public void EveryTemporaryStrengthGoesWithIt()
    {
        // Dark Shackles' Strength down on it (a TemporaryStrengthPower): removed with the Strength, so nothing gives it back as its turn ends.
        var state = Fight(hp: 15, plow: 10);
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.DarkShackles, amount: 9);
        var beast = After(state, Strike(damage: 6));
        Assert.False(beast.Powers.Has(PowerId.DarkShackles));
        Assert.False(beast.Powers.Has(PowerId.Strength));
    }

    [Fact]
    public void CeremonialBeastGainsRitualOnDread()
    {
        // CeremonialBeast.SetStunned: `if (RitualAmount > 0)` PowerCmd.Apply<RitualPower>(base.Creature, RitualAmount), RitualAmount 2 on Dread, else 0.
        var dread = Fight(hp: 15, plow: 10);
        dread.Enemies[0].Monster = "CeremonialBeast";
        dread.Dread = true;
        var beast = After(dread, Strike(damage: 6));
        Assert.True(beast.Stunned);
        Assert.Equal(2, beast.Powers[PowerId.Ritual]);
        var plain = Fight(hp: 15, plow: 10);
        plain.Enemies[0].Monster = "CeremonialBeast";
        Assert.False(After(plain, Strike(damage: 6)).Powers.Has(PowerId.Ritual));
    }
}
