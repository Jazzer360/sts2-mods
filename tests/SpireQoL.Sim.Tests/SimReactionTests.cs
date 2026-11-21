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

    private static SimState AfterPlay(SimState state, SimPlay play)
    {
        SimEngine.ApplyPlayTo(state, play, out var branches, out _);
        Assert.Single(branches);
        return branches[0];
    }

    [Fact]
    public void EnvenomPoisonsOnEachHitThatGetsThroughBlock()
    {
        // EnvenomPower.AfterDamageGiven: powered attack, UnblockedDamage > 0, per hit.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, block: 5, hits: 10) } };
        state.You.Envenom = 2;
        var after = AfterPlay(state, Strike(target: 1, damage: 4, hits: 3)); // 4 blocked (block 5 → 1), then 3 through, 4 through
        Assert.Equal(4, after.Enemy(id: 1)!.PoisonStacks);
        Assert.Equal(4, after.Enemy(id: 1)!.Poison);
    }

    [Fact]
    public void EnvenomPoisonCanKillBeforeTheEnemyActs()
    {
        // Poison ticks at the start of its turn, before its move: the attack plus poison is lethal.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 8, block: 0, hits: 20) } };
        state.You.Envenom = 3;
        var outcome = SimEngine.Run(state, Strike(target: 1, damage: 5));
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.Equal(0, outcome.TakenMax);
    }

    [Fact]
    public void SneckoSkullAddsToEnvenomAndCardPoison()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50) } };
        state.You.Envenom = 1;
        state.You.PoisonBonus = 1;
        var after = AfterPlay(state, Strike(target: 1, damage: 3, hits: 2));
        Assert.Equal(4, after.Enemy(id: 1)!.PoisonStacks); // (1 + 1) per hit
    }

    [Fact]
    public void PoisonWithSeveralTicksAddsTheStacksOfEachTick()
    {
        // PoisonPower.CalculateTotalDamageNextTurn: min(stacks, triggers) ticks of stacks, stacks-1, ...
        var enemy = new SimEnemy { Id = 1, Hp = 50, PoisonStacks = 3, PoisonTriggers = 2, Poison = 5 };
        enemy.AddPoison(stacks: 2);
        Assert.Equal(5, enemy.PoisonStacks);
        Assert.Equal(9, enemy.Poison); // 5 + 4
    }

    [Fact]
    public void CurlUpBlocksAfterTheCardSoRelicProcsHitTheBlock()
    {
        // CurlUpPower: any damage from a card's attack marks it; after the card is played it gains block
        // (powers before relics), then the power is removed.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 30) } };
        state.Enemies[0].CurlUp = 7;
        var play = Strike(target: 1, damage: 6);
        play.Procs.Add(new ProcHit { Spread = SimPlay.Target.Single, TargetId = 1, Damage = { [1] = 5 } });
        var after = AfterPlay(state, play);
        Assert.Equal(24, after.Enemy(id: 1)!.Hp);     // the proc's 5 went into the block
        Assert.Equal(2, after.Enemy(id: 1)!.Block);
        Assert.Equal(0, after.Enemy(id: 1)!.CurlUp);
    }

    [Fact]
    public void CurlUpIgnoresCardsThatDontAttackIt()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 30), Enemy(id: 2, hp: 30) } };
        state.Enemies[0].CurlUp = 7;
        var after = AfterPlay(state, Strike(target: 2, damage: 6));
        Assert.Equal(0, after.Enemy(id: 1)!.Block);
        Assert.Equal(7, after.Enemy(id: 1)!.CurlUp);
    }

    [Fact]
    public void SneakyBlocksAfterEveryAttackPlayed()
    {
        // SneakyPower.AfterCardPlayed: any Attack card, whatever it targets; once per play.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 30), Enemy(id: 2, hp: 30) } };
        state.Enemies[0].Sneaky = 3;
        var play = Strike(target: 2, damage: 6);
        play.Plays = 2;
        Assert.Equal(6, AfterPlay(state, play).Enemy(id: 1)!.Block);
        Assert.Equal(0, AfterPlay(state, new SimPlay { Name = "Defend", BlockAmount = 5, BlockGains = 1 }).Enemy(id: 1)!.Block);
    }

    [Fact]
    public void SkittishBlocksOnlyWhenTheAttackGetsThrough()
    {
        // SkittishPower.AfterAttack: a card's attack with unblocked damage, once per turn.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 30, block: 10) } };
        state.Enemies[0].Skittish = 4;
        Assert.Equal(4, AfterPlay(state, Strike(target: 1, damage: 6)).Enemy(id: 1)!.Block); // fully blocked: 10 - 6 = 4, no gain
        var through = AfterPlay(state, Strike(target: 1, damage: 12));
        Assert.Equal(4, through.Enemy(id: 1)!.Block); // 0 left + 4
        // Once per turn: the power stays (HasGainedBlockThisTurn), and a second attack that gets through gains nothing.
        Assert.Equal(0, AfterPlay(through, Strike(target: 1, damage: 12)).Enemy(id: 1)!.Block);
    }

    [Fact]
    public void IntentChangedByThePlayIsWhatTheEnemyAttacksWith()
    {
        // Weak from the card changes the enemy's intent: its hits go through the damage pipeline as they land.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 30, block: 0, hits: 12) } };
        state.Enemies[0].Intent = new List<IntentHit> { new(Base: 12) };
        var weak = ScriptBuilder.Play(ScriptBuilder.Card("Neutralize", SimCardType.Skill), target: 1, plays: 1, new ApplyPowerAction(PowerId.Weak, Amount: 1) { Debuff = true });
        Assert.Equal(12, SimEngine.Run(state).TakenMax);
        Assert.Equal(9, SimEngine.Run(state, weak).TakenMax);
    }

    [Fact]
    public void RandomHitsKeepTheirReactionsPerBranch()
    {
        // A random attack on a Curl Up enemy or a plain one: only the branch that hit it curls up.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 30), Enemy(id: 2, hp: 30) } };
        state.Enemies[0].CurlUp = 5;
        var play = new SimPlay { Name = "Sword Boomerang", IsAttack = true, HitTarget = SimPlay.Target.Random, HitDamage = { [1] = 3, [2] = 3 }, Hits = 1 };
        SimEngine.ApplyPlayTo(state, play, out var branches, out _);
        Assert.Equal(2, branches.Count);
        Assert.Contains(branches, branch => branch.Enemy(id: 1)!.Block == 5 && branch.Enemy(id: 1)!.Hp == 27);
        Assert.Contains(branches, branch => branch.Enemy(id: 1)!.Block == 0 && branch.Enemy(id: 2)!.Hp == 27);
    }
[Fact]
    public void FullKeyChangesWithAnythingTheBranchKeyLeavesOut()
    {
        // Card simulations are cached by FullKey: a change in something read from the game once must invalidate them (Reflect),
        // though branches of one run never differ in it (the branch key leaves it out: SimKeyCoverageTests).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 30, block: 0, hits: 12) } };
        var branch = new SimState { Enemies = { Enemy(id: 1, hp: 30, block: 0, hits: 12) } };
        branch.You.Reflect = true;
        Assert.Equal(state.Key(), branch.Key());
        Assert.NotEqual(state.FullKey(), branch.FullKey());
        // Intents are in both (Crab Rage can change them in one branch).
        var copy = new SimState { Enemies = { Enemy(id: 1, hp: 30, block: 0, hits: 9) } };
        Assert.NotEqual(state.Key(), copy.Key());
        Assert.NotEqual(state.FullKey(), copy.FullKey());
    }

    [Fact]
    public async Task ParallelRunsFromOneStartStateMatchSequentialRuns()
    {
        // Card simulations share the start state across worker threads; runs never change it.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 9, block: 0, hits: [6, 6]), Enemy(id: 2, hp: 12, block: 2, hits: 10), Enemy(id: 3, hp: 7, block: 0, hits: 3) } };
        state.You.Orbs = new() { new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 }, new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 } };
        state.You.OrbCapacity = 3;
        state.You.Juggernaut = 3;
        var plays = Enumerable.Range(0, 24).Select(index => new SimPlay { Name = "P" + index, BlockAmount = index % 5, BlockGains = index % 3, IsAttack = true, HitTarget = SimPlay.Target.Random, HitDamage = { [1] = 4, [2] = 4, [3] = 4 }, Hits = 1 + index % 3 }).ToList();
        var before = state.ToJson();
        var sequential = plays.Select(play => SimEngine.Run(state, play)).ToList();
        var parallel = await Task.WhenAll(plays.Select(play => Task.Run(() => SimEngine.Run(state, play))));
        Assert.Equal(before, state.ToJson());
        for (var i = 0; i < plays.Count; i++)
        {
            Assert.Equal(sequential[i].TakenMax, parallel[i].TakenMax);
            Assert.Equal(sequential[i].TakenMin, parallel[i].TakenMin);
            Assert.Equal(sequential[i].Branches.Count, parallel[i].Branches.Count);
        }
    }

    [Fact]
    public void RandomPoisonBranchesOverItsTargets()
    {
        // Bouncing Flask: 3 poison on a random enemy, 3 times. With one enemy all of it lands there.
        var one = new SimState { Enemies = { Enemy(id: 1, hp: 9, block: 0, hits: 10) } };
        var flask = new SimPlay { Name = "Bouncing Flask", RandomPoison = 3, RandomPoisonTimes = 3 };
        Assert.True(SimEngine.Run(one, flask).DeadInAll(enemyId: 1)); // 9 poison ticks before it acts
        // With two enemies nothing is guaranteed, but killing either is possible.
        var two = new SimState { Enemies = { Enemy(id: 1, hp: 9, block: 0, hits: 10), Enemy(id: 2, hp: 9, block: 0, hits: 10) } };
        var outcome = SimEngine.Run(two, flask);
        Assert.False(outcome.DeadInAll(enemyId: 1));
        Assert.True(outcome.DeadInAny(enemyId: 1));
    }

    [Fact]
    public void FocusLossFromHyperbeamLowersYourOrbs()
    {
        // From a real fight: Lightning 3/8 and Glass 2/4 at 0 Focus; Hyperbeam takes 3 Focus:
        // the game showed Lightning 0/5 and Glass 0/0 afterwards.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 60) } };
        state.You.Orbs = new() { new SimOrb { Kind = OrbKind.Glass, Passive = 2, Evoke = 4, GlassBase = 2 }, new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 } };
        state.You.OrbCapacity = 3;
        SimEngine.ApplyPlayTo(state, new SimPlay { Name = "Hyperbeam", FocusDelta = -3 }, out var states, out _);
        var orbs = states[0].You.Orbs;
        Assert.Equal((0, 0), (orbs[0].Passive, orbs[0].Evoke));
        Assert.Equal((0, 5), (orbs[1].Passive, orbs[1].Evoke));
        Assert.Equal(-3, states[0].You.Focus);
    }

    [Fact]
    public void FocusGainRaisesOrbsFromZero()
    {
        // Negative Focus floors values at 0; gaining Focus back counts from the base, not the floor.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 60) } };
        state.You.Focus = -4;
        state.You.Orbs = new() { new SimOrb { Kind = OrbKind.Frost, Passive = 0, Evoke = 1 } };
        state.You.OrbCapacity = 3;
        SimEngine.ApplyPlayTo(state, new SimPlay { Name = "Defragment", FocusDelta = 3 }, out var states, out _);
        Assert.Equal((1, 4), (states[0].You.Orbs[0].Passive, states[0].You.Orbs[0].Evoke));
    }
}
