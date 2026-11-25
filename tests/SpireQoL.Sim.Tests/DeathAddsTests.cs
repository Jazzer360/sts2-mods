using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// What a death brings in, read from code (decision #276). InfestedPower.AfterDeath: four times a Wriggler made, StartStunned = true, then CreatureCmd.Add,
/// each made as it's added, so its HP is none the ones before it took (Creature.SetUniqueMonsterHpValue over the enemies then): 5, 4, 3 and 2 values,
/// a branch each, never a roll. StockPower.AfterDeath while its Amount is above 0: the next Axebot, StockAmount = Amount - 1. SteamEruptionPower.AfterDeath:
/// the Waterfall Giant's TriggerAboutToBlowState: its HP set to 999999999 (it's in the fight again: ShouldCreatureBeRemovedFromCombatAfterDeath false,
/// only Steam Eruption staying on it), its next move About to Blow (no attack). Each ShouldStopCombatFromEnding is true: an enemy that dies during its own
/// move goes on with it while one of these holds the fight's end back (CombatManager.IsEnding).
/// </summary>
public class DeathAddsTests
{
    private static SimPlay Strike(int target, int damage, int hits = 1, SimPlay.Target mode = SimPlay.Target.Single)
    {
        var play = new SimPlay { Name = "Strike", IsAttack = true, HitTarget = mode, TargetId = target, Hits = hits,
            Card = new SimCardPlay(new SimCard("Strike", SimCardType.Attack) { Energy = 1 }, Who.You, Target: target, PlayIndex: 0, PlayCount: 1, AutoPlay: false) };
        play.HitDamage[target] = damage;
        return play;
    }

    private static SimState Fight(params SimEnemy[] enemies)
    {
        var state = new SimState { TurnNumber = 1 };
        state.You.Hp = 50;
        state.You.MaxHp = 70;
        state.Enemies.AddRange(enemies);
        return state;
    }

    private static SimEnemy Parasite() => new()
    {
        Id = 0, Name = "Phrog Parasite", Hp = 5, Powers = PowerSet.Empty.With(PowerId.Infested, amount: 1),
        Spawns = Enumerable.Range(0, 4).Select(_ => new SimSpawn("Wriggler", new[] { 17, 18, 19, 20, 21 }) { Source = PowerId.Infested, Monster = "Wriggler", UniqueFromEarlier = true }).ToList(),
    };

    [Fact]
    public void InfestedsFourWrigglersJoinEachWithAnHpNoneBeforeItTook()
    {
        var outcome = SimEngine.Run(Fight(Parasite()), Strike(target: 0, damage: 6));
        Assert.Null(outcome.Branches.FirstOrDefault(branch => branch.Unmodelled != null)?.Unmodelled);
        // 5 × 4 × 3 × 2 orders of HP, each its share.
        Assert.Equal(120, outcome.Branches.Count);
        Assert.All(outcome.Branches, branch =>
        {
            var wrigglers = branch.Enemies.Skip(1).ToList();
            Assert.Equal(new[] { 1, 2, 3, 4 }, wrigglers.Select(wriggler => wriggler.Id));
            Assert.Equal(4, wrigglers.Select(wriggler => wriggler.Hp).Distinct().Count());
            Assert.All(wrigglers, wriggler => Assert.InRange(wriggler.Hp, 17, 21));
            Assert.All(wrigglers, wriggler => Assert.Equal(0, wriggler.AddedBy));
            Assert.Equal(1.0 / 120, branch.Weight, precision: 9);
        });
        // They join doing nothing this turn: the parasite's death is the play's kill.
        Assert.Contains(0, outcome.KilledByPlayInAll);
        Assert.Equal(0, outcome.TakenMax);
    }

    [Fact]
    public void AnAttackOnEveryEnemyAfterTheKillHitsTheWrigglers()
    {
        // The first hit kills the parasite; the second, on every enemy (AttackCommand.TargetingAllOpponents: its targets found again for each hit, "new
        // creatures added to the combat will become valid targets"), lands on the four Wrigglers that joined, 6 off each.
        var play = Strike(target: 0, damage: 6, hits: 2, mode: SimPlay.Target.All);
        for (var wriggler = 1; wriggler <= 4; wriggler++) play.HitDamage[wriggler] = 6;
        var outcome = SimEngine.Run(Fight(Parasite()), play);
        Assert.All(outcome.Branches, branch => Assert.All(branch.Enemies.Skip(1), wriggler => Assert.InRange(wriggler.Hp, 17 - 6, 21 - 6)));
    }

    [Fact]
    public void StocksNextAxebotJoinsWithOneStockLess()
    {
        var axebot = new SimEnemy
        {
            Id = 0, Name = "Axebot", Hp = 4, Powers = PowerSet.Empty.With(PowerId.Stock, amount: 2),
            Spawns = new List<SimSpawn> { new("Axebot", new[] { 90 }) { Source = PowerId.Stock, Monster = "Axebot", UniqueFromEarlier = true, Powers = PowerSet.Empty.With(PowerId.Stock, amount: 1) } },
        };
        var branch = Assert.Single(SimEngine.Run(Fight(axebot), Strike(target: 0, damage: 6)).Branches);
        var next = branch.Enemy(id: 1)!;
        Assert.Equal(90, next.Hp);
        Assert.Equal(1, next.Powers[PowerId.Stock]);
        Assert.Equal("Axebot", next.Monster);
    }

    [Fact]
    public void ACreatureADeathAddsThatWasntReadMakesItUnknown()
    {
        var parasite = Parasite();
        parasite.Spawns = new List<SimSpawn> { new("", Array.Empty<int>()) { Source = PowerId.Infested, Unread = "creatures added in a loop the reader can't count" } };
        var outcome = SimEngine.Run(Fight(parasite), Strike(target: 0, damage: 6));
        Assert.True(outcome.AnyUnmodelled);
    }

    [Fact]
    public void ThornsKillingTheLastAxebotDuringItsAttackLeavesItsMoveGoingOn()
    {
        // HammerUppercutMove: its attack, then Weak on its targets. Your Thorns kills it during the attack; its Stock holds the fight's end back (its next
        // Axebot comes in), so its Weak lands. With no power holding the end back, the fight is over and its move stops there.
        SimState Board(bool stock)
        {
            var axebot = new SimEnemy
            {
                Id = 0, Name = "Axebot", Hp = 2, Hits = new() { 5 }, IntendsToAttack = true,
                Move = new SimAction[] { new MoveAttackAction(FirstHit: 0, Hits: 1), new MovePowerAction(PowerId.Weak, Amount: 2, To: new[] { Who.You }, Debuff: true) },
            };
            if (stock) axebot.Powers = PowerSet.Empty.With(PowerId.Stock, amount: 1);
            var state = Fight(axebot);
            state.You.Powers = PowerSet.Empty.With(PowerId.Thorns, amount: 3);
            return state;
        }
        var held = Assert.Single(SimEngine.Run(Board(stock: true)).Branches);
        Assert.Equal(2, held.You.Powers[PowerId.Weak]);
        var ended = Assert.Single(SimEngine.Run(Board(stock: false)).Branches);
        Assert.False(ended.You.Powers.Has(PowerId.Weak));
    }

    private static SimEnemy Giant(int hp) => new()
    {
        Id = 0, Name = "Waterfall Giant", Monster = "WaterfallGiant", Hp = hp, Hits = new() { 20 }, IntendsToAttack = true,
        Powers = PowerSet.Empty.With(PowerId.SteamEruption, amount: 10).With(PowerId.Strength, amount: 3),
        SetMoves = new Dictionary<string, SimMove?> { ["AboutToBlowState"] = new SimMove(Array.Empty<int>(), Array.Empty<SimAction>()) },
    };

    [Fact]
    public void SteamEruptionBringsItsGiantBackAboutToBlow()
    {
        var outcome = SimEngine.Run(Fight(Giant(hp: 5)), Strike(target: 0, damage: 6));
        var branch = Assert.Single(outcome.Branches);
        Assert.Null(branch.Unmodelled);
        var giant = branch.Enemy(id: 0)!;
        Assert.True(giant.Revived);
        Assert.Equal(999999999, giant.Hp);
        // Its powers came off but Steam Eruption, and its move is About to Blow: no hit on you, and it doesn't die before acting.
        Assert.Equal(new[] { PowerId.SteamEruption }, giant.Powers.Items.Select(entry => entry.Name));
        Assert.Equal(0, outcome.TakenMax);
        Assert.Equal(0, outcome.DeadChance(enemyId: 0));
        // The play killed it (its death was told): its crossed tab's rule sees the kill.
        Assert.Contains(0, outcome.KilledByPlayInAll);
    }

    [Fact]
    public void AGiantYourEndOfTurnKillsDiesBeforeActingForTheDeathSkull()
    {
        // Decision #295: your end of turn kills it (Lightning's passive), Steam Eruption brings it straight back. The Death Skull says it dies
        // before acting (DeathSkullChance); the lethal tag's "dead when it would act" (DeadChanceRange) and the enemy turn are unchanged.
        var state = Fight(Giant(hp: 3));
        state.You.Orbs = new() { new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 } };
        state.You.OrbCapacity = 1;
        var outcome = SimEngine.Run(state);
        var giant = Assert.Single(outcome.Branches).Enemy(id: 0)!;
        Assert.True(giant.Revived);
        Assert.False(giant.DiedBeforeActing);
        Assert.True(outcome.DeathSkullChance(enemyId: 0).Certain);
        Assert.False(outcome.DeadChanceRange(enemyId: 0).Possible);
        Assert.Equal(0, outcome.TakenMax);
    }

    [Fact]
    public void AGiantYourEndOfTurnLeavesAliveHasNoDeathSkull()
    {
        var state = Fight(Giant(hp: 10));
        state.You.Orbs = new() { new SimOrb { Kind = OrbKind.Lightning, Passive = 3, Evoke = 8 } };
        state.You.OrbCapacity = 1;
        var outcome = SimEngine.Run(state);
        Assert.False(outcome.DeathSkullChance(enemyId: 0).Possible);
        Assert.False(Assert.Single(outcome.Branches).Enemy(id: 0)!.RevivedBeforeActing);
    }

    [Fact]
    public void ARevivedGiantIsATargetForTheHitsAfterIt()
    {
        // The first random hit can kill the Giant; the second lands on it or the other enemy, half the time each, where a dead Giant left the other the
        // only target. The other takes exactly one hit half the time (Giant then other, other then Giant), where it was three quarters.
        var other = new SimEnemy { Id = 1, Name = "Other", Hp = 30 };
        var play = Strike(target: 0, damage: 6, hits: 2, mode: SimPlay.Target.Random);
        play.HitDamage[1] = 6;
        var outcome = SimEngine.Run(Fight(Giant(hp: 5), other), play);
        Assert.Equal(0.5, outcome.Chance(branch => branch.Enemy(id: 1)!.Hp == 24).Low, precision: 9);
        Assert.Equal(0.25, outcome.Chance(branch => branch.Enemy(id: 1)!.Hp == 30).Low, precision: 9);
    }

    [Fact]
    public void AMoveSetThatWasntReadMakesItUnknown()
    {
        var giant = Giant(hp: 5);
        giant.SetMoves = new Dictionary<string, SimMove?> { ["AboutToBlowState"] = null };
        Assert.True(SimEngine.Run(Fight(giant), Strike(target: 0, damage: 6)).AnyUnmodelled);
    }
}
