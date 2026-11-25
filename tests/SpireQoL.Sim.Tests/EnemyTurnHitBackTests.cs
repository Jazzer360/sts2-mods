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

    [Fact]
    public void ReflectAndFlameBarrierHitBackInTheOrderTheyLanded()
    {
        // Hook.AfterDamageReceived tells the listeners in their order (your powers in the order they landed): Reflect on you before Flame Barrier
        // hits back first, and its 10 kills the 7-HP enemy before Flame Barrier's 5 (CreatureCmd.Damage skips a dead target): -3. Flame Barrier
        // first: 5, then Reflect's 10: -8.
        SimState Board(params PowerId[] order)
        {
            var state = new SimState { Enemies = { Enemy(id: 0, hp: 7, hits: [10]) } };
            state.You.Block = 20;
            state.You.Hp = 50;
            foreach (var power in order) state.You.Powers = state.You.Powers.With(power, amount: power == PowerId.FlameBarrier ? 5 : 1);
            return state;
        }
        Assert.Equal(-3, HpAfter(Board(PowerId.Reflect, PowerId.FlameBarrier)));
        Assert.Equal(-8, HpAfter(Board(PowerId.FlameBarrier, PowerId.Reflect)));
    }

    [Fact]
    public void AnAttackerKilledByThornsExplodesBeforeTheHitLandsAsWorkedOut()
    {
        // v0.114.0 (CreatureCmd.GetDamageResults, then Hook.BeforeDamageReceived, then ApplyDamageResults): the hit (5) is worked out first, your
        // Buffer taking it to 0 HP (BufferPower.ModifyHpLostAfterOstyLate) with its stack spent only as the hit is applied. Your Thorns then kills the
        // 2-HP attacker in BeforeDamageReceived; its CreatureCmd.Kill runs ExplosivePower.AfterDeath there and then, whose 6 is worked out with Buffer
        // still on (0) and spends it. The hit lands as worked out: 0. Before v0.114.0 the explosion spent Buffer first and the hit's 5 got through.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 2, hits: [5]) } };
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.Explosive, amount: 6);
        state.You.Hp = 50;
        state.You.Thorns = 3;
        state.You.Powers = state.You.Powers.With(PowerId.Buffer, amount: 1);
        Assert.Equal(0, Assert.Single(SimEngine.Run(state).Branches).Taken);
    }

    [Fact]
    public void OnMainTheExplosionSpendsBufferBeforeTheHitIsWorkedOut()
    {
        // Main (MainRule.ReceivedBeforeTheResults, BranchRules.DamageWorkedOutFirst): CreatureCmd.Damage tells BeforeDamageReceived first, so your Thorns
        // kills the attacker and its explosion (6) spends your Buffer before the hit (5) is worked out: you take 5.
        // Another enemy keeps the fight going: the last one's death would end it, and its explosion with it (decision #301).
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 2, hits: [5]), ScriptBuilder.Bystander(id: 1) }, MainRules = MainRule.ReceivedBeforeTheResults };
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.Explosive, amount: 6);
        state.You.Hp = 50;
        state.You.Thorns = 3;
        state.You.Powers = state.You.Powers.With(PowerId.Buffer, amount: 1);
        Assert.Equal(5, Assert.Single(SimEngine.Run(state).Branches).Taken);
    }

    [Fact]
    public void YourResultIsToldBeforeOstysOnBetaAndAfterItOnMain()
    {
        // CreatureCmd.Damage's results loop, in DamageResults.AllResults' order on beta (v0.114.0): your result (Flame Barrier hits back its 3), then
        // Osty's (Hook.AfterCurrentHpChanged: Necro Mastery's 2 × the 5 he lost to every enemy). A 10-HP attacker: 3, then 10, ends at -3. Main's
        // CreatureCmd.Damage lists Osty's result first (MainRule.ReceivedBeforeTheResults): Necro Mastery's 10 kills it, Flame Barrier finds it dead: 0.
        SimState Board(MainRule rules)
        {
            var state = new SimState { Enemies = { Enemy(id: 0, hp: 10, hits: [12]) }, MainRules = rules };
            state.You.Hp = 50;
            state.You.WithOsty(hp: 5);
            state.You.FlameBarrier = 3;
            state.You.Powers = state.You.Powers.With(PowerId.NecroMastery, amount: 2);
            return state;
        }
        Assert.Equal(-3, HpAfter(Board(MainRule.None)));
        Assert.Equal(0, HpAfter(Board(MainRule.ReceivedBeforeTheResults)));
    }

    [Fact]
    public void FlameBarrierHitsBackWhenWuppyTakesTheWholeHit()
    {
        // WuppysLoyaltyPower.ModifyPreBlockDamageTarget: Wuppy takes the 6 before your block. CreatureCmd.Damage still tells Hook.AfterDamageReceived
        // of your result (0 taken), so Flame Barrier hits back its 4.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 30, hits: [6]) } };
        state.You.Hp = 50;
        state.You.WithWuppy(hp: 20);
        state.You.FlameBarrier = 4;
        Assert.Equal(26, HpAfter(state));
    }

    [Fact]
    public void ThornsOnOstyIsWorkedOutWithOstyAsItsTarget()
    {
        // ThornsPower.BeforeDamageReceived: CreatureCmd.Damage(dealer, ...) with Osty the dealer, so the damage math's target is Osty: your own
        // Intangible (IntangiblePower.ModifyDamageCap: target == base.Owner) doesn't cap what he takes. Thorns 4 on his Poke: he loses 4, not 1.
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 30 } } };
        state.Enemies[0].Thorns = 4;
        state.You.WithOsty(hp: 10);
        state.You.Powers = state.You.Powers.With(PowerId.Intangible, amount: 1);
        var poke = new SimPlay { Name = "Poke", HitTarget = SimPlay.Target.Single, TargetId = 1, Hits = 1, HitDamage = { [1] = 6 }, ByOsty = true };
        SimEngine.ApplyPlayTo(state, poke, out var branches, out _);
        Assert.All(branches, branch => Assert.Equal(6, branch.You.OstyHp));
    }
}
