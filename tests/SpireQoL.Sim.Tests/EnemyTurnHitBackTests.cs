using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// What hits an enemy back during its own attack (ThornsPower.BeforeDamageReceived, FlameBarrierPower.AfterDamageReceived,
/// ReflectPower.AfterDamageReceived): each is CreatureCmd.Damage(dealer, amount, Unpowered, owner), so the damage math's caps (Intangible, Hard to
/// Kill: their ModifyDamageCap, read from their code), the enemy's block this turn and the HP-loss pipeline (Hardened Shell, Slippery,
/// Intangible's ModifyHpLostAfterOsty) all apply, as they do to any damage.
/// </summary>
public class EnemyTurnHitBackTests
{
    private static SimEnemy Enemy(int id, int hp, params int[] hits) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hits.ToList() };

    private static int HpAfter(SimState state) => Assert.Single(SimEngine.Run(state).Branches).Enemy(0)!.Hp;

    [Fact]
    public void ThornsHitsBackThroughSlippery()
    {
        // SlipperyPower.ModifyHpLostAfterOsty: at most 1 HP while it has a charge. Two hits, Thorns 3 before each: 1 HP (a charge), then 3.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 10, hits: [5, 5]) } };
        state.Enemies[0].Slippery = 1;
        state.You.Thorns = 3;
        state.You.Hp = 50;
        Assert.Equal(6, HpAfter(state));
    }

    [Fact]
    public void FlameBarrierIsCappedByTheEnemysReadCaps()
    {
        // IntangiblePower.ModifyDamageCap: 1 for any damage to its owner; HardToKillPower's: its amount.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 20, hits: [3, 3]) } };
        state.You.FlameBarrier = 5;
        state.You.Hp = 50;
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.Intangible, amount: 1);
        Assert.Equal(18, HpAfter(state));
        state.Enemies[0].Powers = state.Enemies[0].Powers.Without(PowerId.Intangible).With(PowerId.HardToKill, amount: 4);
        Assert.Equal(12, HpAfter(state));
    }

    [Fact]
    public void HitsBackTakeTheEnemysBlockThisTurnFirst()
    {
        // Its block from this turn's move (Barricade's kept block too) takes a hit back before its HP.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 20, hits: [3]) } };
        state.Enemies[0].Block = 4;
        state.Enemies[0].Barricade = true;
        state.You.Thorns = 6;
        state.You.Hp = 50;
        Assert.Equal(18, HpAfter(state));
    }
}
