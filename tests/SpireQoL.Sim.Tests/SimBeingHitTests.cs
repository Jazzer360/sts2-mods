using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Enemy powers that react to being hit (decompile v0.111.0): Burrowed, Flutter, Plow / Shriek,
/// The Gambit, Slippery; plus the play counts behind the Juggernaut, Sneaky and Hand Drill tabs.
/// </summary>
public class SimBeingHitTests
{
    private static SimEnemy Enemy(int id, int hp, int block = 0, params int[] hits)
        => new() { Id = id, Name = $"E{id}", Hp = hp, Block = block, Hits = hits.ToList() };

    private static SimPlay Strike(int target, int damage, int hits = 1)
        => new() { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = target, HitDamage = { [target] = damage }, Hits = hits };

    [Fact]
    public void BreakingBurrowedBlockStunsItSoItDoesntAttack()
    {
        // BurrowedPower.AfterBlockBroken: stunned, power and block removed. Its block stays between turns.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 40, block: 10, hits: 15) } };
        state.Enemies[0].Burrowed = true;
        state.Enemies[0].Barricade = true;
        Assert.Equal(15, SimEngine.Run(state).TakenMax);
        Assert.Equal(15, SimEngine.Run(state, Strike(target: 1, damage: 6)).TakenMax); // block not broken
        var outcome = SimEngine.Run(state, Strike(target: 1, damage: 12));
        Assert.True(outcome.StunnedInAll(enemyId: 1));
        Assert.Equal(0, outcome.TakenMax);
        Assert.True(outcome.PlayBreaksBlockInAll);
    }

    [Fact]
    public void FlutterStunsAfterEnoughPoweredHitsGetThrough()
    {
        // FlutterPower.AfterDamageReceived: unblocked powered-attack hits decrement it; 0 stuns.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 40, block: 0, hits: 10) } };
        state.Enemies[0].Flutter = 2;
        Assert.False(SimEngine.Run(state, Strike(target: 1, damage: 3)).StunnedInAll(enemyId: 1));
        Assert.True(SimEngine.Run(state, Strike(target: 1, damage: 3, hits: 2)).StunnedInAll(enemyId: 1));
        // Proc damage isn't a powered attack.
        var proc = new SimPlay { Name = "Defend" };
        proc.Procs.Add(new ProcHit { Spread = SimPlay.Target.Single, TargetId = 1, Damage = { [1] = 3 }, Times = 2 });
        Assert.False(SimEngine.Run(state, proc).StunnedInAll(enemyId: 1));
    }

    [Fact]
    public void PlowAndShriekStunAtTheirHpThresholdFromAnyDamage()
    {
        // PlowPower / ShriekPower: any damage through that leaves it at or below the amount.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 30, block: 0, hits: 10) } };
        state.Enemies[0].StunAtHp = 20;
        Assert.False(SimEngine.Run(state, Strike(target: 1, damage: 9)).StunnedInAll(enemyId: 1));
        Assert.True(SimEngine.Run(state, Strike(target: 1, damage: 10)).StunnedInAll(enemyId: 1));
        // A Lightning orb's end-of-turn hit counts too, so the card isn't needed then.
        state.You.Orbs = new() { new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 } };
        state.You.OrbCapacity = 3;
        Assert.True(SimEngine.Run(state, Strike(target: 1, damage: 7)).StunnedInAll(enemyId: 1));
    }

    [Fact]
    public void APoisonTickThatReachesTheThresholdStunsItBeforeItMoves()
    {
        // The tick is damage (PoisonPower.Trigger: CreatureCmd.Damage): Shriek hears it. A report: a Terror Eel poisoned from 82 to
        // 60 (Shriek 75) was stunned and skipped its hits, which the forecast still counted.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 82, block: 0, hits: 10) } };
        state.Enemies[0].StunAtHp = 75;
        state.Enemies[0].AddPoison(6);
        Assert.Equal(10, SimEngine.Run(state).TakenMax); // 76: above it
        state.Enemies[0].AddPoison(22);
        Assert.Equal(0, SimEngine.Run(state).TakenMax);
    }

    [Fact]
    public void SlipperyCapsAPoisonTickAtOneHp()
    {
        // Poison through the HP-loss pipeline: Slippery lets 1 HP of a 30-poison tick through, so it lives to attack.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5, block: 0, hits: 10) } };
        state.Enemies[0].Slippery = 1;
        state.Enemies[0].AddPoison(30);
        var outcome = SimEngine.Run(state);
        Assert.False(outcome.DeadInAll(enemyId: 1));
        Assert.Equal(10, outcome.TakenMax);
    }

    [Fact]
    public void TheGambitDiesToAPoweredHitThatGetsThrough()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 60, block: 5, hits: 20) } };
        state.Enemies[0].Gambit = true;
        Assert.False(SimEngine.Run(state, Strike(target: 1, damage: 5)).GambitInAll(enemyId: 1)); // blocked
        var outcome = SimEngine.Run(state, Strike(target: 1, damage: 6));
        Assert.True(outcome.GambitInAll(enemyId: 1));
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.Equal(0, outcome.TakenMax);
    }

    [Fact]
    public void SlipperyCapsEachHitAtOneHp()
    {
        // SlipperyPower.ModifyHpLostAfterOsty: at most 1 HP per hit while it has charges.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5, block: 0, hits: 10) } };
        state.Enemies[0].Slippery = 2;
        var outcome = SimEngine.Run(state, Strike(target: 1, damage: 20, hits: 2));
        Assert.True(outcome.SlipperyInAny(enemyId: 1));
        Assert.False(outcome.DeadInAll(enemyId: 1)); // two hits of 20 lose it only 2 HP
        Assert.Equal(10, outcome.TakenMax);
    }

    [Fact]
    public void SlipperyRunsOutOfCharges()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5, block: 0, hits: 10) } };
        state.Enemies[0].Slippery = 2;
        SimEngine.ApplyPlayTo(state, Strike(target: 1, damage: 20, hits: 2), out var two, out _);
        Assert.Equal(3, two[0].Enemy(id: 1)!.Hp);
        SimEngine.ApplyPlayTo(state, Strike(target: 1, damage: 20, hits: 3), out var three, out _);
        Assert.Equal(0, Math.Max(0, three[0].Enemy(id: 1)!.Hp)); // the third hit has no charge left
    }

    [Fact]
    public void JuggernautCountsEveryBlockGainOfThePlay()
    {
        // The card's own block, a block proc, and a Frost evoke: three gains.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, block: 0, hits: 5) } };
        state.You.Orbs = new() { new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 } };
        state.You.OrbCapacity = 1;
        var play = new SimPlay { Name = "Coolheaded", BlockAmount = 5, BlockGains = 1, ProcBlock = { 3 } };
        play.OrbOps.Add(new OrbOp { Op = OrbOp.Kind.EvokeFront });
        var outcome = SimEngine.Run(state, play);
        Assert.Equal(3, outcome.PlayBlockGainsMax);
    }

    [Fact]
    public void SneakyCountsAsABlockReaction()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 30, block: 0, hits: 5) } };
        state.Enemies[0].Sneaky = 4;
        SimEngine.ApplyPlayTo(state, Strike(target: 1, damage: 3), out var attacked, out _);
        Assert.All(attacked, branch => Assert.True(branch.Enemy(id: 1)!.ReactedWithBlock));
        SimEngine.ApplyPlayTo(state, new SimPlay { Name = "Defend", BlockAmount = 5, BlockGains = 1 }, out var blocked, out _);
        Assert.All(blocked, branch => Assert.False(branch.Enemy(id: 1)!.ReactedWithBlock));
    }

    [Fact]
    public void TheBootRaisesSmallAttackHitsToItsMinimum()
    {
        // TheBoot.ModifyHpLostAfterOstyLate: a powered hit losing the enemy 1..4 HP loses it 5.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 20, block: 0, hits: 6) } };
        state.You.BootMinimum = 5;
        SimEngine.ApplyPlayTo(state, Strike(target: 1, damage: 2, hits: 3), out var states, out _);
        Assert.Equal(5, states[0].Enemy(id: 1)!.Hp); // 3 hits of 5, not 2
        // A fully blocked hit loses no HP: unchanged.
        var blocked = new SimState { Enemies = { Enemy(id: 1, hp: 20, block: 10, hits: 6) } };
        blocked.You.BootMinimum = 5;
        SimEngine.ApplyPlayTo(blocked, Strike(target: 1, damage: 3), out var otherStates, out _);
        Assert.Equal(20, otherStates[0].Enemy(id: 1)!.Hp);
        // Proc damage isn't a powered attack.
        var proc = new SimPlay { Name = "Defend" };
        proc.Procs.Add(new ProcHit { Spread = SimPlay.Target.Single, TargetId = 1, Damage = { [1] = 2 } });
        SimEngine.ApplyPlayTo(state, proc, out var nextStates, out _);
        Assert.Equal(18, nextStates[0].Enemy(id: 1)!.Hp);
    }

    [Fact]
    public void TheBootsTabIsAPlayWithAHitItRaises()
    {
        // Its tab: a hit that gets past block for less than its minimum. 6 into 5 block loses the enemy 1, raised to 5.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 20, block: 5, hits: 6) } };
        state.You.BootMinimum = 5;
        SimEngine.ApplyPlayTo(state, Strike(target: 1, damage: 6), out var states, out _);
        Assert.Equal(15, states[0].Enemy(id: 1)!.Hp);
        Assert.True(states[0].Fired.Has("TheBoot"));
        // A hit already at its minimum or more, or one block stops, isn't raised: no tab.
        SimEngine.ApplyPlayTo(state, Strike(target: 1, damage: 10), out var bigStates, out _);
        Assert.Equal(15, bigStates[0].Enemy(id: 1)!.Hp);
        Assert.False(bigStates[0].Fired.Has("TheBoot"));
        SimEngine.ApplyPlayTo(state, Strike(target: 1, damage: 5), out var blockedStates, out _);
        Assert.Equal(20, blockedStates[0].Enemy(id: 1)!.Hp);
        Assert.False(blockedStates[0].Fired.Has("TheBoot"));
    }

    [Fact]
    public void TheBootComesAfterSlipperysCap()
    {
        // Slippery caps HP lost at 1 (AfterOsty); The Boot then raises it to its minimum (AfterOstyLate).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 20, block: 0, hits: 6) } };
        state.Enemies[0].Slippery = 3;
        state.You.BootMinimum = 5;
        SimEngine.ApplyPlayTo(state, Strike(target: 1, damage: 12), out var states, out _);
        Assert.Equal(15, states[0].Enemy(id: 1)!.Hp);
    }

    [Fact]
    public void EnemyThornsHitsYourBlockOnEachCardHit()
    {
        // ThornsPower.BeforeDamageReceived: each powered attack hit on it deals its Thorns to you
        // first (unpowered, so your block takes it). Auto-report: FTL into Spiny Toad (Thorns 5) with
        // 4 block left you with 0 block and 1 HP lost.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 98, block: 0, hits: 25) } };
        state.You.Block = 4;
        state.Enemies[0].Thorns = 5;
        SimEngine.ApplyPlayTo(state, Strike(target: 1, damage: 6), out var states, out _);
        Assert.Equal(0, states[0].You.Block);
        Assert.Equal(1, states[0].Taken);
        Assert.Equal(92, states[0].Enemy(id: 1)!.Hp);

        // Two hits: twice. The hit that kills it still sets Thorns off (before the damage lands).
        state.You.Block = 20;
        SimEngine.ApplyPlayTo(state, Strike(target: 1, damage: 6, hits: 2), out states, out _);
        Assert.Equal(10, states[0].You.Block);
        state.Enemies[0].Hp = 5;
        SimEngine.ApplyPlayTo(state, Strike(target: 1, damage: 6), out states, out _);
        Assert.Equal(15, states[0].You.Block);
        Assert.False(states[0].Enemy(id: 1)!.Alive);

        // Proc damage and orb evokes aren't a card's attack: no Thorns.
        state.Enemies[0].Hp = 98;
        var proc = new SimPlay { Name = "Defend" };
        proc.Procs.Add(new ProcHit { Spread = SimPlay.Target.Single, TargetId = 1, Damage = { [1] = 3 } });
        SimEngine.ApplyPlayTo(state, proc, out states, out _);
        Assert.Equal(20, states[0].You.Block);
    }

    [Fact]
    public void AsleepWakesStunnedOnAnyDamageThroughItsBlock()
    {
        // AsleepPower.AfterDamageReceived: unblocked damage from anything (a proc too) wakes it, stunned: no attack.
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "Lagavulin", Hp = 200, Block = 7, Asleep = true, Hits = new() { 20 } } } };
        var play = new SimPlay { Procs = { new ProcHit { Spread = SimPlay.Target.Single, TargetId = 1, Damage = { [1] = 12 } } } };
        var outcome = SimEngine.Run(state, play);
        Assert.True(outcome.StunnedInAll(enemyId: 1));
        Assert.Equal(0, outcome.TakenMax);
    }

    [Fact]
    public void AsleepStaysAsleepWhenItsBlockTakesEverything()
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "Lagavulin", Hp = 200, Block = 7, Asleep = true, Hits = new() { 20 } } } };
        var play = new SimPlay { HitTarget = SimPlay.Target.Single, TargetId = 1, Hits = 2, HitDamage = { [1] = 2 } };
        var outcome = SimEngine.Run(state, play);
        Assert.False(outcome.StunnedInAll(enemyId: 1));
    }

    [Fact]
    public void SlumberWakesStunnedWhenItsLastStackGoes()
    {
        // SlumberPower.AfterDamageReceived: one stack per instance through block; the last wakes it, stunned.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 60, block: 0, hits: 12) } };
        state.Enemies[0].Slumber = 3;
        Assert.False(SimEngine.Run(state, Strike(target: 1, damage: 5, hits: 2)).StunnedInAll(enemyId: 1));
        var outcome = SimEngine.Run(state, Strike(target: 1, damage: 5, hits: 3));
        Assert.True(outcome.StunnedInAll(enemyId: 1));
        Assert.Equal(0, outcome.TakenMax);
    }

    [Fact]
    public void SlumberIgnoresHitsItsBlockStops()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 60, block: 20, hits: 12) } };
        state.Enemies[0].Slumber = 1;
        Assert.False(SimEngine.Run(state, Strike(target: 1, damage: 6, hits: 3)).StunnedInAll(enemyId: 1)); // 18 into 20 block
        Assert.True(SimEngine.Run(state, Strike(target: 1, damage: 6, hits: 4)).StunnedInAll(enemyId: 1));
    }
}
