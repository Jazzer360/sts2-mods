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
        using var code = new BranchCode(path);
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var power in code.Models().Where(type => type.Namespace == "MegaCrit.Sts2.Core.Models.Powers"))
        {
            var program = ProgramReader.Read(power, code.Read, "AfterDeath");
            foreach (var step in program.Steps)
            {
                var inLoop = step is LoopStep;
                foreach (var add in (step is LoopStep loop ? loop.Body : new[] { step }).OfType<CommandStep>().Where(command => command.Command.FullName == "CreatureCmd.Add"))
                    found.Add($"{power.Name}: " + (inLoop ? "in a loop" : SimAdapter.SpawnedMonster(add) ?? "set up before it joins"));
            }
        }
        Assert.Equal(new[] { "InfestedPower: in a loop", "StockPower: set up before it joins", "SurprisePower: FatGremlin", "SurprisePower: SneakyGremlin" }, found);
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
        ["Axebot: PowerCmd.Apply<StockPower>"] = "its stock when StockAmount > 0, a number Stock sets on the next Axebot before it joins (that spawn is a gap: set up before it joins)",
        ["Crusher: PowerCmd.Apply<PlatingPower>"] = "Plating on a condition (PlatingAmount > 0); it starts the fight, never added by a death",
        ["GremlinMerc: PowerCmd.Apply"] = "Thievery made first, one for each player; it starts the fight, never added by a death",
        ["Rocket: PowerCmd.Apply<SurroundedPower>"] = "Surrounded on its opponents, not itself; it starts the fight, never added by a death",
        ["Rocket: PowerCmd.Apply<PlatingPower>"] = "Plating on a condition (PlatingAmount > 0); it starts the fight, never added by a death",
        ["ToughEgg: PowerCmd.Apply<HatchPower>"] = "Hatch on a condition (not hatched yet), its amount by whose turn it is; eggs join from Ovicopter's move (a summon the enemy turn doesn't follow yet)",
    };
}
