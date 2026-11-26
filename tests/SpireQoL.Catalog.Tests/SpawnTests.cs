using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The creatures a death adds, read from code (decision #195: SimAdapter.SpawnsOf over each power's AfterDeath), and the powers a monster
/// starts with (its AfterAddedToRoom: SimAdapter.StartingPowers). They replaced the hand-written Surprise spawns (SurpriseSpawns), proven
/// equal on a real fight first; every creature any power's death adds on both branches is read or a reviewed gap.
/// </summary>
[Collection(GameFightCollection.Name)]
public class SpawnTests
{
    [Fact]
    public void SurprisesGremlinsAreReadFromItsCodeAsTheyWereWrittenByHand()
    {
        // The Gremlin Merc's Surprise: a Sneaky Gremlin, then the Fat Gremlin, each with every HP in its range no enemy has.
        var fight = new GameFight();
        var merc = fight.Combat.CreateCreature(ModelDb.Monster<GremlinMerc>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(merc);
        ModelDb.Power<SurprisePower>().ToMutable().ApplyInternal(merc, amount: 1m);
        var spawns = SimAdapter.SpawnsOf(merc, fight.Combat);
        // What SurpriseSpawns gave (its formula, kept here): the two gremlins in the game's order, every free HP of each.
        var taken = fight.Combat.Enemies.Select(creature => creature.MaxHp).ToHashSet();
        string Expected(MonsterModel monster)
        {
            var range = Enumerable.Range(monster.MinInitialHp, monster.MaxInitialHp - monster.MinInitialHp + 1).ToList();
            var free = range.Where(hp => !taken.Contains(hp)).ToList();
            return $"{Combat.TurnEndModel.SafeTitle(monster)} {string.Join(",", free.Count == 0 ? range : free)}";
        }
        Assert.NotNull(spawns);
        Assert.Equal(new[] { Expected(ModelDb.Monster<SneakyGremlin>()), Expected(ModelDb.Monster<FatGremlin>()) },
            spawns.Select(spawn => $"{spawn.Name} {string.Join(",", spawn.Hp)}"));
        Assert.All(spawns, spawn => Assert.Equal(0, spawn.Powers.Count)); // neither gremlin's AfterAddedToRoom applies a power
        // Each keyed by its own CreatureCmd.Add (decision #345): two adds, two offsets.
        Assert.Equal(2, spawns.Select(spawn => spawn.At).Distinct().Count(at => at != null));
    }

    [Fact]
    public void SurprisesFatGremlinJoinsWithAHeistForEachThievery()
    {
        // SurprisePower.AfterDeath gives its Fat Gremlin a Heist for each Thievery on its owner (PowerCmd.Apply of a HeistPower, that Thievery's own
        // DynamicVars.Gold.IntValue) before adding it (decision #346): with none, none (the gremlins above); with two, both, each its own gold. The
        // Sneaky Gremlin, added in between, has none.
        var fight = new GameFight();
        var merc = fight.Combat.CreateCreature(ModelDb.Monster<GremlinMerc>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(merc);
        ModelDb.Power<SurprisePower>().ToMutable().ApplyInternal(merc, amount: 1m);
        var thieveries = new[] { 15, 20 }.Select(gold =>
        {
            var thievery = ModelDb.Power<ThieveryPower>().ToMutable();
            thievery.ApplyInternal(merc, amount: 1m);
            thievery.DynamicVars.Gold.BaseValue = gold;
            return thievery;
        }).ToList();
        var spawns = SimAdapter.SpawnsOf(merc, fight.Combat)!;
        Assert.Equal(2, spawns.Count);
        Assert.All(spawns, spawn => Assert.Null(spawn.Unread));
        Assert.Equal(0, spawns[0].Powers.Count);
        Assert.Equal(thieveries.Sum(thievery => thievery.DynamicVars.Gold.IntValue), spawns[1].Powers[PowerId.Heist]);
    }

    [Fact]
    public void ACreatureWithoutADeathThatAddsOneAddsNone()
    {
        var fight = new GameFight();
        var byrdonis = fight.Combat.CreateCreature(ModelDb.Monster<Byrdonis>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(byrdonis);
        Assert.Null(SimAdapter.SpawnsOf(byrdonis, fight.Combat));
    }

    public static TheoryData<string> Branches => new() { "beta", "main" };

    [Theory]
    [MemberData(nameof(Branches))]
    public void EveryCreatureADeathAddsIsReadOrAReviewedGap(string branch)
    {
        var path = branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;
        if (!File.Exists(path)) return;
        var code = BranchCode.Of(path);
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var power in code.Models().Where(type => type.Namespace == "MegaCrit.Sts2.Core.Models.Powers"))
        {
            var program = ProgramReader.Read(power, code.Read, "AfterDeath");
            foreach (var step in program.Steps)
            {
                var inLoop = step is LoopStep;
                foreach (var add in (step is LoopStep loop ? loop.Body : new[] { step }).OfType<CommandStep>().Where(command => command.Command.FullName == "CreatureCmd.Add"))
                {
                    var madeClass = add.Values.Count > 0 ? ProgramReader.MadeMonster(add.Values[0]) : null;
                    found.Add($"{power.Name}: " + (SimAdapter.SpawnedMonster(add) ?? (madeClass != null ? madeClass + " set up before it joins" : "unread")) + (inLoop ? " in a counter loop" : ""));
                }
            }
        }
        Assert.Equal(new[] { "InfestedPower: Wriggler set up before it joins in a counter loop", "StockPower: Axebot set up before it joins", "SurprisePower: FatGremlin", "SurprisePower: SneakyGremlin" }, found);
    }

    [Fact]
    public void InfestedAddsFourWrigglersEachWithAnHpNoneBeforeItTook()
    {
        // InfestedPower.AfterDeath: four times, (Wriggler)ModelDb.Monster<Wriggler>().ToMutable(), StartStunned = true, CreatureCmd.Add: each made as it's
        // added (CombatState.CreateCreature: SetUniqueMonsterHpValue over the enemies then), so each has an HP none of the earlier ones took.
        var fight = new GameFight();
        var parasite = fight.Combat.CreateCreature(ModelDb.Monster<PhrogParasite>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(parasite);
        ModelDb.Power<InfestedPower>().ToMutable().ApplyInternal(parasite, amount: 1m);
        var spawns = SimAdapter.SpawnsOf(parasite, fight.Combat);
        Assert.NotNull(spawns);
        var wriggler = ModelDb.Monster<Wriggler>();
        var range = Enumerable.Range(wriggler.MinInitialHp, wriggler.MaxInitialHp - wriggler.MinInitialHp + 1).Where(hp => hp != parasite.MaxHp).ToList();
        Assert.Equal(4, spawns.Count);
        Assert.All(spawns, spawn =>
        {
            Assert.Equal(PowerId.Infested, spawn.Source);
            Assert.Equal(nameof(Wriggler), spawn.Monster);
            Assert.Equal(range, spawn.Hp);
            Assert.True(spawn.UniqueFromEarlier);
            Assert.Equal(0, spawn.Powers.Count);
        });
    }

    [Fact]
    public void StocksNextAxebotHasOneStockLessAndTheHpThatGives()
    {
        // StockPower.AfterDeath while Amount > 0: (Axebot)ModelDb.Monster<Axebot>().ToMutable(), StockAmount = Amount - 1, CreatureCmd.Add. Axebot's
        // MinInitialHp is its base + RespawnCount (2 - StockAmount) × 10; its AfterAddedToRoom applies Stock of its StockAmount while above 0.
        var fight = new GameFight();
        var axebot = fight.Combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(axebot);
        ModelDb.Power<StockPower>().ToMutable().ApplyInternal(axebot, amount: 2m);
        var spawns = SimAdapter.SpawnsOf(axebot, fight.Combat);
        var next = (Axebot)ModelDb.Monster<Axebot>().ToMutable();
        next.StockAmount = 1;
        Assert.NotNull(spawns);
        var spawn = Assert.Single(spawns);
        Assert.Equal(PowerId.Stock, spawn.Source);
        Assert.Equal(new[] { next.MinInitialHp }, spawn.Hp);
        Assert.Equal(1, spawn.Powers[PowerId.Stock]);
        // The last Axebot's (Stock 1): its next has none, and an HP 10 higher again.
        var last = fight.Combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(last);
        ModelDb.Power<StockPower>().ToMutable().ApplyInternal(last, amount: 1m);
        var final = Assert.Single(SimAdapter.SpawnsOf(last, fight.Combat)!);
        Assert.Equal(next.MinInitialHp + 10, final.Hp.Single());
        Assert.False(final.Powers.Has(PowerId.Stock));
    }

    [Fact]
    public void StocksNextAxebotIsReadWithTheAmountItsDeathAsksAsItRuns()
    {
        // StockPower.AfterDeath tests its own Amount (> 0) and sets the next Axebot up from it (StockAmount = Amount - 1): a play can change it before the
        // death, so it's never decided on the board alone (decision #315). Each spawn keeps the amount it was read with (the death asks it as it runs);
        // a test of it that doesn't hold on the board is asked as it dies too: listed unread, never a death that adds nothing.
        var fight = new GameFight();
        var axebot = fight.Combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(axebot);
        ModelDb.Power<StockPower>().ToMutable().ApplyInternal(axebot, amount: 2m);
        Assert.Equal(2, Assert.Single(SimAdapter.SpawnsOf(axebot, fight.Combat)!).ReadAtAmount);
        var empty = fight.Combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(empty);
        // A Stock of none on the board (as a play could leave it: the game removes it at 0, so set here past its apply).
        var none = ModelDb.Power<StockPower>().ToMutable();
        none.ApplyInternal(empty, amount: 1m);
        none.SetAmount(0, silent: true);
        var unread = Assert.Single(SimAdapter.SpawnsOf(empty, fight.Combat)!);
        Assert.Equal(PowerId.Stock, unread.Source);
        Assert.Equal(SimAdapter.SpawnGaps["asked as it dies"], unread.Unread);
        // Infested's and Surprise's read only what the death is given: nothing to ask again.
        var parasite = fight.Combat.CreateCreature(ModelDb.Monster<PhrogParasite>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(parasite);
        ModelDb.Power<InfestedPower>().ToMutable().ApplyInternal(parasite, amount: 1m);
        Assert.All(SimAdapter.SpawnsOf(parasite, fight.Combat)!, spawn => Assert.Null(spawn.ReadAtAmount));
    }

    /// <summary>
    /// A creature a death adds joins doing nothing this turn (SimEngine.Spawn adds it with no hits): its first move, as its class's move rules make it
    /// for the monster set up as the death's code sets it up, has no attack intent (the Wrigglers' and gremlins' SPAWNED_MOVE, the next Axebot's Boot
    /// Up: block and Strength, after every other enemy's move, as it joins last).
    /// </summary>
    [Fact]
    public void ACreatureADeathAddsAttacksNoOneThisTurn()
    {
        GameModels.EnsureLoaded();
        static MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine.MonsterState FirstOf(MonsterModel monster)
        {
            var machine = monster.GetType().GetMethod("GenerateMoveStateMachine", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(monster, null)!;
            return (MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine.MonsterState)machine.GetType().GetField("_initialState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(machine)!;
        }
        var wriggler = (Wriggler)ModelDb.Monster<Wriggler>().ToMutable();
        wriggler.StartStunned = true;
        var axebot = (Axebot)ModelDb.Monster<Axebot>().ToMutable();
        axebot.StockAmount = 1;
        foreach (var monster in new MonsterModel[] { wriggler, axebot, ModelDb.Monster<SneakyGremlin>().ToMutable(), ModelDb.Monster<FatGremlin>().ToMutable() })
        {
            var first = Assert.IsType<MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine.MoveState>(FirstOf(monster));
            Assert.DoesNotContain(first.Intents, intent => intent is MegaCrit.Sts2.Core.MonsterMoves.Intents.AttackIntent);
        }
    }

    [Fact]
    public void EveryMonstersStartingPowersAreReadOrReviewed()
    {
        // A monster's AfterAddedToRoom powers on itself, read on its class (what a creature joining mid-fight starts with).
        GameModels.EnsureLoaded();
        var gaps = new List<string>();
        foreach (var monster in ModelDb.All.OfType<MonsterModel>()) SimAdapter.StartingPowers(monster.GetType(), monster, gaps);
        var unreviewed = gaps.Where(gap => !StartingPowerGaps.ContainsKey(gap)).ToList();
        Assert.True(unreviewed.Count == 0, "Starting powers not read, unreviewed:\n" + string.Join("\n", unreviewed));
        var stale = StartingPowerGaps.Keys.Where(gap => !gaps.Contains(gap)).ToList();
        Assert.True(stale.Count == 0, "Reviewed, but read now:\n" + string.Join("\n", stale));
    }

    /// <summary>Powers a monster's AfterAddedToRoom gives that aren't read as a spawn's starting powers, and why that's right.</summary>
    private static readonly Dictionary<string, string> StartingPowerGaps = new()
    {
        ["Aeonglass: PowerCmd.Apply"] = "Withering Presence made first, on each of its opponents, not itself; it starts the fight, never added by a death",
        ["GremlinMerc: PowerCmd.Apply"] = "Thievery made first, one for each player; it starts the fight, never added by a death",
        ["Rocket: PowerCmd.Apply<SurroundedPower>"] = "Surrounded on its opponents, not itself; it starts the fight, never added by a death",
        ["ToughEgg: PowerCmd.Apply<HatchPower>"] = "Hatch on a condition (not hatched yet), its amount by whose turn it is; eggs join from Ovicopter's move (a summon the enemy turn doesn't follow yet)",
    };
}
