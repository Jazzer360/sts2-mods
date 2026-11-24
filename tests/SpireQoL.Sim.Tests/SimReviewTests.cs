using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Rules from the first logic review of the simulator (each checked against the v0.111.0 decompile).
/// </summary>
public class SimReviewTests
{
    private static SimEnemy Enemy(int id, int hp, int block = 0, params int[] hits)
        => new() { Id = id, Name = $"E{id}", Hp = hp, Block = block, Hits = hits.ToList() };

    [Fact]
    public void PoisonTicksOnAStunnedEnemyToo()
    {
        // PoisonPower.AfterSideTurnStart ticks for every enemy on its side's turn start, stunned or not.
        var enemy = Enemy(id: 1, hp: 4, block: 0, hits: 10);
        enemy.Stunned = true;
        enemy.AddPoison(4);
        var outcome = SimEngine.Run(new SimState { Enemies = { enemy } });
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.Equal(0, outcome.TakenMax); // stunned: it wouldn't have attacked anyway
    }

    [Fact]
    public void HpAtTurnEndCoversEveryRandomOutcome()
    {
        // One Lightning bolt between two enemies: each ends your turn at 10 or 7.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 10), Enemy(id: 2, hp: 10) } };
        state.You.Orbs = new() { new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 } };
        state.You.OrbCapacity = 3;
        var outcome = SimEngine.Run(state);
        Assert.Equal(7, outcome.HpAtTurnEndMin(enemyId: 1));
        Assert.Equal(10, outcome.HpAtTurnEndMax(enemyId: 1));
        Assert.Equal(0, outcome.UnblockedMin(enemyId: 1));
        Assert.Equal(1, outcome.UnblockedMax(enemyId: 1));
    }

    [Fact]
    public void HpAtTurnEndIsBeforeTheEnemyTurn()
    {
        // Poison and retaliation happen on its turn: not in HpAtTurnEnd.
        var enemy = Enemy(id: 1, hp: 10, block: 0, hits: 5);
        enemy.AddPoison(3);
        var state = new SimState { Enemies = { enemy } };
        state.You.Thorns = 2;
        var outcome = SimEngine.Run(state);
        Assert.Equal(10, outcome.HpAtTurnEndMax(enemyId: 1));
        Assert.Equal(5, outcome.Worst.Enemy(id: 1)!.Hp); // 10 - 3 poison - 2 Thorns
    }

    [Fact]
    public void FullyBlockedFirstHitInSomeBranchesOnly()
    {
        // Juggernaut from Plating hits one of two enemies at random; if it kills the first attacker,
        // the second enemy's hit is the first to land on your block.
        var imbalanced = Enemy(id: 2, hp: 30, block: 0, hits: 6);
        imbalanced.Imbalanced = true;
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 3, block: 0, hits: 4), imbalanced } };
        state.You.Plating = 6;
        state.You.Juggernaut = 3;
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.FirstHitBlockedInAny(enemyId: 2));  // the 3 HP enemy died: 6 block covers the 6 hit
        Assert.False(outcome.FirstHitBlockedInAll(enemyId: 2)); // it survived: its 4 hit used block first
    }

    /// <summary>Beating Remnant on you: its MaxHpLoss as the adapter reads it, and the HP its own count says you lost this turn.</summary>
    internal static SimState WithRemnant(SimState state, int maxHpLoss, int lost)
    {
        state.WithRelic(Who.You, RelicId.BeatingRemnant, "MaxHpLoss", maxHpLoss);
        var remnant = Assert.IsType<ReadModel>(SimModels.Get(RelicId.BeatingRemnant.Name));
        state.You.Fields = state.You.Fields.With(remnant.FieldKey("DamageReceivedThisTurn"), lost);
        return state;
    }

    [Fact]
    public void BeatingRemnantBudgetCarriesFromYourTurnIntoTheEnemyTurn()
    {
        // BeatingRemnant.BeforeSideTurnStart resets only at the start of YOUR turn.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, block: 0, hits: 10) } };
        WithRemnant(state, maxHpLoss: 20, lost: 15); // already lost 15 this turn
        Assert.Equal(5, SimEngine.Run(state).TakenMax);
    }

    [Fact]
    public void BufferAbsorbingAHitDoesNotSpendTheRemnantBudget()
    {
        // Remnant counts result.UnblockedDamage after Buffer (ModifyHpLostAfterOstyLate).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, block: 0, hits: [6, 6]) } };
        WithRemnant(state, maxHpLoss: 6, lost: 0);
        state.You.Buffer = 1;
        Assert.Equal(6, SimEngine.Run(state).TakenMax); // first hit buffered, second hit uses the full 6
    }

    [Fact]
    public void EtherealCardsExhaustBeforeBurnDealsItsDamage()
    {
        // CombatManager.DoTurnEnd: orb passives, then Ethereal exhausts (Feel No Pain block), then
        // DoTurnEndCards (Burn...). The block is up before Burn hits.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50) } };
        state.You.FeelNoPain = 3;
        state.You.Fading = 1;
        state.You.TurnEndCards.Add(new TurnEndCard("Burn", "Burn", Amount: 2));
        Assert.Equal(0, SimEngine.Run(state).TakenMax);
    }

    [Fact]
    public void GlassEvokeShrinksWithItsPassive()
    {
        // GlassOrb.EvokeVal => PassiveVal * 2; the passive loses 1 per trigger. Consuming Shadow evokes
        // after the passives (AfterSideTurnEnd): Glass 4 → passive 4, then evoke (3*2)=6, total 10.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 11) } };
        state.You.Orbs = new() { new SimOrb { Kind = OrbKind.Glass, Passive = 4, Evoke = 8, Fields = NamedCounts.Empty.With("_passiveVal", amount: 4) } };
        state.You.OrbCapacity = 3;
        state.You.ConsumingShadow = 1;
        var outcome = SimEngine.Run(state);
        Assert.Equal(1, outcome.Worst.Enemy(id: 1)!.Hp);
        Assert.False(outcome.DeadInAll(enemyId: 1));
    }

    [Fact]
    public void ConsumingShadowsFrostEvokeIsInTheEndOfTurnBlock()
    {
        // Dev report 20261120-042856-c2ac65: ConsumingShadowPower.AfterSideTurnEnd evokes the last orb (OrbCmd.EvokeLast); a Frost
        // orb gives its passive 2 (orb passives, before) then its evoke 5. Both are end-of-turn block: the breakdown Incoming Damage
        // shows and the forecast check compares with the block the game gave (it listed only the passive's 2).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, block: 0, hits: 15) }, Log = true };
        state.You.Block = 10;
        state.You.Orbs = new() { new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 } };
        state.You.OrbCapacity = 3;
        state.You.ConsumingShadow = 1;
        var branch = SimEngine.Run(state).Worst;
        Assert.Equal(0, branch.Taken);
        Assert.Equal(7, branch.EndOfTurnBlock.Sum(nameAmount => nameAmount.Amount));
    }

    [Fact]
    public void ConsumingShadowsLightningEvokeIsInTheEndOfTurnDamage()
    {
        // The evoke's damage is listed with your other end-of-turn damage (the forecast log's breakdown), as the passive's is.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 50, block: 0, hits: 5) }, Log = true };
        state.You.Orbs = new() { new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 } };
        state.You.OrbCapacity = 3;
        state.You.ConsumingShadow = 1;
        var branch = SimEngine.Run(state).Worst;
        Assert.Equal(11, branch.EndOfTurnDamage.Sum(nameAmount => nameAmount.Amount));
    }

    [Fact]
    public void ADecayedGlassOrbStillDealsYourFocus()
    {
        // GlassOrb: PassiveVal = base + Focus; only the base decays (to 0). Base 0 with Focus 2 keeps
        // dealing 2 to every enemy each time it fires, and its evoke stays 4.
        var glass = new SimOrb { Kind = OrbKind.Glass, Passive = 2, Evoke = 4, Fields = NamedCounts.Empty.With("_passiveVal", amount: 0) };
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Hp = 4 } } };
        state.You.Focus = 2;
        state.You.Relics = state.You.Relics.With(RelicId.GoldPlatedCables, amount: 1); // two triggers
        state.You.Orbs = new() { glass };
        state.You.OrbCapacity = 3;
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.DeadInAll(enemyId: 1)); // 2 + 2
        Assert.Equal(4, outcome.Worst.You.Orbs[0].Evoke);
    }

    [Fact]
    public void GlassDecaysItsBaseNotItsFocusIncludedValue()
    {
        // Base 3 with Focus 2: fires for 5, then 4 (base 2 + 2).
        var glass = new SimOrb { Kind = OrbKind.Glass, Passive = 5, Evoke = 10, Fields = NamedCounts.Empty.With("_passiveVal", amount: 3) };
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Hp = 20 } } };
        state.You.Focus = 2;
        state.You.Relics = state.You.Relics.With(RelicId.GoldPlatedCables, amount: 1); // two triggers
        state.You.Orbs = new() { glass };
        state.You.OrbCapacity = 3;
        var outcome = SimEngine.Run(state);
        Assert.Equal(11, outcome.Worst.Enemy(id: 1)!.Hp); // 20 - 5 - 4
        Assert.Equal(6, outcome.Worst.You.Orbs[0].Evoke); // base 1 + Focus 2 = 3, x2
    }

    [Fact]
    public void GoldPlatedCablesBonusFollowsTheFrontOrb()
    {
        // GoldPlatedCables: +1 trigger for whichever orb is Orbs[0] when its passive fires. After Zap
        // evicts the front Frost orb, the bonus goes to the next orb (a Lightning).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 7, block: 0, hits: 10) } };
        state.You.Orbs = new() { new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 0 }, new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 } };
        state.You.OrbCapacity = 2;
        state.You.Relics = state.You.Relics.With(RelicId.GoldPlatedCables, amount: 1);
        var zap = new SimPlay { OrbOps = { new OrbOp { Op = OrbOp.Kind.Channel, Orb = new SimOrb { Kind = OrbKind.Lightning, Passive = 0, Evoke = 0 } } } };
        var outcome = SimEngine.Run(state, zap);
        Assert.Equal(1, outcome.Worst.Enemy(id: 1)!.Hp); // the front Lightning fires twice: 3 + 3
    }

    [Fact]
    public void LethalMeansDeadBeforeItActsNotKilledByRetaliationDuringItsAttack()
    {
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 3, block: 0, hits: [5, 5]) } };
        state.You.FlameBarrier = 5; // kills it after its first hit lands
        var outcome = SimEngine.Run(state);
        Assert.Equal(0, outcome.Worst.Enemy(id: 1)!.Hp > 0 ? 1 : 0);
        Assert.False(outcome.DeadInAll(enemyId: 1)); // it still acted
        Assert.Equal(5, outcome.TakenMax);
    }

    [Fact]
    public void PowersFireBeforeRelicsAtTurnEnd()
    {
        // IterateHookListeners: your powers before your relics. Hailstorm (power) kills the 3 HP enemy
        // before Orichalcum's block (relic) sets off Juggernaut, which then can only hit the other one.
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 3, block: 0, hits: 10), Enemy(id: 2, hp: 4, block: 0, hits: 10) } };
        state.You.Hailstorm = 3;
        state.Vars[$"{Who.You}:{PowerId.Hailstorm.Name}:FrostOrbs"] = 0; // HailstormPower's FrostOrbs value
        state.WithRelic(Who.You, RelicId.Orichalcum, "Block", value: 5);
        state.You.Juggernaut = 1;
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.DeadInAll(enemyId: 1));
        Assert.True(outcome.DeadInAll(enemyId: 2)); // 3 from Hailstorm + 1 from Juggernaut
    }

    [Fact]
    public void DisintegrationLandsAfterParryingShieldChecksYourBlock()
    {
        // DisintegrationPower: AfterSideTurnEndLate, after Parrying Shield (AfterSideTurnEnd).
        var state = new SimState { Enemies = { Enemy(id: 1, hp: 5, block: 0, hits: 1) } };
        state.You.Block = 10;
        state.WithRelic(Who.You, RelicId.ParryingShield, "Block", value: 10);
        state.WithRelic(Who.You, RelicId.ParryingShield, "Damage", value: 5);
        state.You.Powers = state.You.Powers.With(PowerId.Disintegration, amount: 4);
        Assert.True(SimEngine.Run(state).DeadInAll(enemyId: 1));
    }

    [Fact]
    public void ImbalancedCountsAHitThatBufferStops()
    {
        // WasFullyBlocked = (blocked > 0 || block > 0) && no HP lost after the AfterOsty modifiers.
        var enemy = Enemy(id: 1, hp: 40, block: 0, hits: 8);
        enemy.Imbalanced = true;
        var state = new SimState { Enemies = { enemy } };
        state.You.Block = 3;
        state.You.Buffer = 1;
        Assert.True(SimEngine.Run(state).FirstHitBlockedInAll(enemyId: 1));
    }

    [Fact]
    public void DamageStoppedByAnExhaustedHardenedShellDoesNotWakeIt()
    {
        var enemy = Enemy(id: 1, hp: 50, block: 0, hits: 0);
        enemy.ShellLeft = 0;
        enemy.ShellPerTurn = 10;
        var hit = new SimPlay { HitTarget = SimPlay.Target.Single, TargetId = 1, Hits = 1, HitDamage = { [1] = 6 } };
        Assert.Equal(0, SimEngine.Run(new SimState { Enemies = { enemy } }, hit).UnblockedMax(enemyId: 1));
    }
}
