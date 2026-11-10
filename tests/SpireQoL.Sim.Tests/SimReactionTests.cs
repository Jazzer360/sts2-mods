using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// What happens during a card play besides the card's own numbers: enemy reactions (Curl Up, Sneaky,
/// Skittish), Envenom and Snecko Skull poison, poison stacks with several ticks (Accelerant), and
/// intents the play changes (Weak, Enrage). Decompile v0.111.0.
/// </summary>
public class SimReactionTests
{
    private static SimEnemy Enemy(int id, int hp, int block = 0, params int[] hits)
        => new() { Id = id, Name = $"E{id}", Hp = hp, Block = block, Hits = hits.ToList() };

    private static SimPlay Strike(int target, int damage, int hits = 1)
        => new() { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, TargetId = target, HitDamage = { [target] = damage }, Hits = hits };

    private static SimState AfterPlay(SimState s, SimPlay p)
    {
        SimEngine.ApplyPlayTo(s, p, out var branches, out _);
        Assert.Single(branches);
        return branches[0];
    }

    [Fact]
    public void EnvenomPoisonsOnEachHitThatGetsThroughBlock()
    {
        // EnvenomPower.AfterDamageGiven: powered attack, UnblockedDamage > 0, per hit.
        var s = new SimState { Enemies = { Enemy(1, 50, 5, 10) } };
        s.P.Envenom = 2;
        var after = AfterPlay(s, Strike(1, 4, hits: 3)); // 4 blocked (block 5 → 1), then 3 through, 4 through
        Assert.Equal(4, after.Enemy(1)!.PoisonStacks);
        Assert.Equal(4, after.Enemy(1)!.Poison);
    }

    [Fact]
    public void EnvenomPoisonCanKillBeforeTheEnemyActs()
    {
        // Poison ticks at the start of its turn, before its move: the attack plus poison is lethal.
        var s = new SimState { Enemies = { Enemy(1, 8, 0, 20) } };
        s.P.Envenom = 3;
        var o = SimEngine.Run(s, Strike(1, 5));
        Assert.True(o.DeadInAll(1));
        Assert.Equal(0, o.TakenMax);
    }

    [Fact]
    public void SneckoSkullAddsToEnvenomAndCardPoison()
    {
        var s = new SimState { Enemies = { Enemy(1, 50) } };
        s.P.Envenom = 1;
        s.P.PoisonBonus = 1;
        var after = AfterPlay(s, Strike(1, 3, hits: 2));
        Assert.Equal(4, after.Enemy(1)!.PoisonStacks); // (1 + 1) per hit
    }

    [Fact]
    public void PoisonWithSeveralTicksAddsTheStacksOfEachTick()
    {
        // PoisonPower.CalculateTotalDamageNextTurn: min(stacks, triggers) ticks of stacks, stacks-1, ...
        var e = new SimEnemy { Id = 1, Hp = 50, PoisonStacks = 3, PoisonTriggers = 2, Poison = 5 };
        e.AddPoison(2);
        Assert.Equal(5, e.PoisonStacks);
        Assert.Equal(9, e.Poison); // 5 + 4
    }

    [Fact]
    public void CurlUpBlocksAfterTheCardSoRelicProcsHitTheBlock()
    {
        // CurlUpPower: any damage from a card's attack marks it; after the card is played it gains block
        // (powers before relics), then the power is removed.
        var s = new SimState { Enemies = { Enemy(1, 30) } };
        s.Enemies[0].CurlUp = 7;
        var p = Strike(1, 6);
        p.Procs.Add(new ProcHit { Spread = SimPlay.Target.Single, TargetId = 1, Damage = { [1] = 5 } });
        var after = AfterPlay(s, p);
        Assert.Equal(24, after.Enemy(1)!.Hp);     // the proc's 5 went into the block
        Assert.Equal(2, after.Enemy(1)!.Block);
        Assert.Equal(0, after.Enemy(1)!.CurlUp);
    }

    [Fact]
    public void CurlUpIgnoresCardsThatDontAttackIt()
    {
        var s = new SimState { Enemies = { Enemy(1, 30), Enemy(2, 30) } };
        s.Enemies[0].CurlUp = 7;
        var after = AfterPlay(s, Strike(2, 6));
        Assert.Equal(0, after.Enemy(1)!.Block);
        Assert.Equal(7, after.Enemy(1)!.CurlUp);
    }

    [Fact]
    public void SneakyBlocksAfterEveryAttackPlayed()
    {
        // SneakyPower.AfterCardPlayed: any Attack card, whatever it targets; once per play.
        var s = new SimState { Enemies = { Enemy(1, 30), Enemy(2, 30) } };
        s.Enemies[0].Sneaky = 3;
        var p = Strike(2, 6);
        p.Plays = 2;
        Assert.Equal(6, AfterPlay(s, p).Enemy(1)!.Block);
        Assert.Equal(0, AfterPlay(s, new SimPlay { Name = "Defend", BlockAmount = 5, BlockGains = 1 }).Enemy(1)!.Block);
    }

    [Fact]
    public void SkittishBlocksOnlyWhenTheAttackGetsThrough()
    {
        // SkittishPower.AfterAttack: a card's attack with unblocked damage, once per turn.
        var s = new SimState { Enemies = { Enemy(1, 30, 10) } };
        s.Enemies[0].Skittish = 4;
        Assert.Equal(4, AfterPlay(s, Strike(1, 6)).Enemy(1)!.Block); // fully blocked: 10 - 6 = 4, no gain
        var through = AfterPlay(s, Strike(1, 12));
        Assert.Equal(4, through.Enemy(1)!.Block); // 0 left + 4
        Assert.Equal(0, through.Enemy(1)!.Skittish);
    }

    [Fact]
    public void IntentChangedByThePlayIsWhatTheEnemyAttacksWith()
    {
        // Weak (or Enrage's Strength) changes the enemy's hits; the adapter computes them with the
        // game's damage math and the play sets them.
        var s = new SimState { Enemies = { Enemy(1, 30, 0, 12) } };
        var weak = new SimPlay { Name = "Neutralize", SetHits = { [1] = new List<int> { 9 } } };
        Assert.Equal(12, SimEngine.Run(s).TakenMax);
        Assert.Equal(9, SimEngine.Run(s, weak).TakenMax);
    }

    [Fact]
    public void RandomHitsKeepTheirReactionsPerBranch()
    {
        // A random attack on a Curl Up enemy or a plain one: only the branch that hit it curls up.
        var s = new SimState { Enemies = { Enemy(1, 30), Enemy(2, 30) } };
        s.Enemies[0].CurlUp = 5;
        var p = new SimPlay { Name = "Sword Boomerang", IsAttack = true, HitTarget = SimPlay.Target.Random, HitDamage = { [1] = 3, [2] = 3 }, Hits = 1 };
        SimEngine.ApplyPlayTo(s, p, out var branches, out _);
        Assert.Equal(2, branches.Count);
        Assert.Contains(branches, b => b.Enemy(1)!.Block == 5 && b.Enemy(1)!.Hp == 27);
        Assert.Contains(branches, b => b.Enemy(1)!.Block == 0 && b.Enemy(2)!.Hp == 27);
    }
[Fact]
    public void FullKeyChangesWhenAnIntentChangesButTheBranchKeyDoesNot()
    {
        // Card simulations are cached by FullKey: Weak or Enrage changing an intent must invalidate them.
        var a = new SimState { Enemies = { Enemy(1, 30, 0, 12) } };
        var b = new SimState { Enemies = { Enemy(1, 30, 0, 9) } };
        Assert.Equal(a.Key(), b.Key());
        Assert.NotEqual(a.FullKey(), b.FullKey());
    }

    [Fact]
    public void ParallelRunsFromOneStartStateMatchSequentialRuns()
    {
        // Card simulations share the start state across worker threads; runs never change it.
        var s = new SimState { Enemies = { Enemy(1, 9, 0, 6, 6), Enemy(2, 12, 2, 10), Enemy(3, 7, 0, 3) } };
        s.P.Orbs = new() { new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 }, new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 } };
        s.P.OrbCapacity = 3;
        s.P.Juggernaut = 3;
        var plays = Enumerable.Range(0, 24).Select(i => new SimPlay { Name = "P" + i, BlockAmount = i % 5, BlockGains = i % 3, IsAttack = true, HitTarget = SimPlay.Target.Random, HitDamage = { [1] = 4, [2] = 4, [3] = 4 }, Hits = 1 + i % 3 }).ToList();
        var before = s.ToJson();
        var sequential = plays.Select(p => SimEngine.Run(s, p)).ToList();
        var parallel = Task.WhenAll(plays.Select(p => Task.Run(() => SimEngine.Run(s, p)))).Result;
        Assert.Equal(before, s.ToJson());
        for (var i = 0; i < plays.Count; i++)
        {
            Assert.Equal(sequential[i].TakenMax, parallel[i].TakenMax);
            Assert.Equal(sequential[i].TakenMin, parallel[i].TakenMin);
            Assert.Equal(sequential[i].Branches.Count, parallel[i].Branches.Count);
        }
    }
}

