using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The potion scan (decision #313), as CardScanTests is the cards': every potion's use read from its code (its OnUse) on both branches, each
/// command it calls one the card translation follows (SimAdapter.CommandsTranslated: potions are translated by it, decision #335) or a named gap (the card lists' commands not
/// followed, outside the fight, or hook-only); and on the installed game, what each usable potion's use is built into, in a fight made of the
/// game's own objects (Snapshots/potion-uses.txt: SPIREQOL_UPDATE_SNAPSHOT=1 regenerates). Every `?` there is an open TRACKER item.
/// </summary>
[Collection(GameFightCollection.Name)]
public class PotionScanTests
{
    public static TheoryData<string> Branches => new() { "beta", "main" };

    private static string PathOf(string branch) => branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;

    private static IEnumerable<Type> Potions(Assembly assembly) => assembly.GetTypes()
        .Where(type => type.Namespace == "MegaCrit.Sts2.Core.Models.Potions" && !type.IsAbstract && !type.IsNested)
        .OrderBy(type => type.Name, StringComparer.Ordinal);

    /// <summary>A command a potion's step can call that a list names: followed, a named gap, outside the fight, or one whose only effect is a hook.</summary>
    private static bool Named(string command) =>
        SimAdapter.CommandsNotTranslated.ContainsKey(command) || SimAdapter.CommandsOutsideTheFight.ContainsKey(command)
        || SimAdapter.CommandsTranslated.ContainsKey(command);

    [Theory]
    [MemberData(nameof(Branches))]
    public void EveryPotionsCommandsAreFollowedOrNamed(string branch)
    {
        if (!File.Exists(PathOf(branch))) return; // no decompile of this branch here (docs/DEVELOPMENT.md "Decompiled game source")
        var code = BranchCode.Of(PathOf(branch));
        var unnamed = new List<string>();
        var read = 0;
        foreach (var potion in Potions(code.Assembly))
        {
            var program = ProgramReader.Read(potion, code.Read, "OnUse");
            if (program.Steps.Count > 0) read++;
            foreach (var command in program.Commands)
                if (!Named(command.Command.FullName)) unnamed.Add($"{potion.Name}: {command.Command.FullName}");
            foreach (var unknown in program.Unknowns) unnamed.Add($"{potion.Name}: the reader can't follow {unknown}");
        }
        Assert.True(read >= 60, $"only {read} potions' uses read on {branch}");
        Assert.True(unnamed.Count == 0, $"Potion commands no list names ({branch}): translate each (SimAdapter.Steps, the one translator: decision #335) or name it as a gap:\n" + string.Join("\n", unnamed));
    }

    /// <summary>
    /// What every potion you can drink is built into on the installed game, aimed where it can be (an enemy, you, a teammate), in a fight of the
    /// game's own objects: its actions, and each unknown with why. A change to what the translation makes of a potion shows here.
    /// </summary>
    [Fact]
    public void EveryPotionsUseIsTheSnapshot()
    {
        GameModels.EnsureLoaded();
        var lines = new List<string>();
        foreach (var type in Potions(typeof(PotionModel).Assembly))
        {
            if (typeof(ModelDb).GetMethod(nameof(ModelDb.Potion))!.MakeGenericMethod(type).Invoke(null, null) is not PotionModel canonical) continue;
            if (!SimAdapter.Usable(canonical)) { lines.Add($"{type.Name}: not drunk ({canonical.Usage})"); continue; }
            var fight = new GameFight(teammates: 1);
            var enemy = fight.Combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, slot: null);
            fight.Combat.AddCreature(enemy);
            var potion = canonical.ToMutable();
            fight.Me.AddPotionInternal(potion);
            SimAdapter.Build(fight.Me, fight.Combat, out var ids);
            var aims = canonical.TargetType switch
            {
                TargetType.AnyEnemy => new[] { ids[enemy] },
                TargetType.AnyPlayer => new[] { Who.You, Who.Teammate(0) },
                TargetType.Self => new[] { Who.You },
                _ => new[] { Who.None },
            };
            foreach (var aim in aims)
            {
                var play = SimAdapter.Potion(potion, fight.Me, fight.Combat, ids, aim);
                lines.Add($"{type.Name} [{AimName(aim)}]: " + (play.Body.Count == 0 ? "nothing" : string.Join("; ", play.Body.Select(Describe))));
            }
        }
        var path = Path.Combine(CardPlaySnapshotTests.SourceFolder(), "Snapshots", "potion-uses.txt");
        if (Environment.GetEnvironmentVariable("SPIREQOL_UPDATE_SNAPSHOT") == "1")
        {
            File.WriteAllText(path, string.Join("\n", lines) + "\n");
            return;
        }
        Assert.True(File.Exists(path), $"missing {path}: generate it with SPIREQOL_UPDATE_SNAPSHOT=1");
        var expected = File.ReadAllLines(path);
        var changed = lines.Except(expected).Take(12).ToList();
        var gone = expected.Except(lines).Take(12).ToList();
        Assert.True(changed.Count == 0 && gone.Count == 0, "potion-uses.txt isn't what the translation makes now: regenerate it with SPIREQOL_UPDATE_SNAPSHOT=1 and explain the change\nnow:\n"
            + string.Join("\n", changed) + "\nbefore:\n" + string.Join("\n", gone));
    }

    /// <summary>A known potion's use, step by step, on the installed game: Fire Potion's damage on its target, unpowered.</summary>
    [Fact]
    public void AFirePotionDamagesItsTargetUnpowered()
    {
        GameModels.EnsureLoaded();
        var fight = new GameFight();
        var enemy = fight.Combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(enemy);
        var potion = ModelDb.Potion<MegaCrit.Sts2.Core.Models.Potions.FirePotion>().ToMutable();
        fight.Me.AddPotionInternal(potion);
        var start = SimAdapter.Build(fight.Me, fight.Combat, out var ids);
        var play = SimAdapter.Potion(potion, fight.Me, fight.Combat, ids, ids[enemy]);
        var damage = Assert.IsType<DamageAction>(Assert.Single(play.Body));
        Assert.Equal(ids[enemy], damage.TargetId);
        Assert.Equal(20m, damage.Damage.Base);
        Assert.True(damage.Damage.Props.HasFlag(SimProps.Unpowered));
        SimEngine.ApplyPlayTo(start, play, out var after, out _);
        Assert.Equal(enemy.CurrentHp - 20, Assert.Single(after).Enemy(ids[enemy])!.Hp);
        Assert.Equal(0, Assert.Single(after).You.Potions);
    }

    /// <summary>
    /// Distilled Chaos (decision #338): its three auto-plays off your draw pile's top, from the pile's contents (two Strikes and a Defend: every order
    /// plays all three), on its own read of the board with each pile card's play built. The forecast's read builds none, and its use there is unknown.
    /// </summary>
    [Fact]
    public void DistilledChaosPlaysThePilesCardsOnItsOwnBoard()
    {
        GameModels.EnsureLoaded();
        var fight = new GameFight();
        var enemy = fight.Combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(enemy);
        fight.Add<MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad>(PileType.Draw);
        fight.Add<MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad>(PileType.Draw);
        fight.Add<MegaCrit.Sts2.Core.Models.Cards.DefendIronclad>(PileType.Draw);
        var potion = ModelDb.Potion<MegaCrit.Sts2.Core.Models.Potions.DistilledChaos>().ToMutable();
        fight.Me.AddPotionInternal(potion);
        Assert.True(SimAdapter.PlaysFromDrawPile(potion, Who.You));

        // The forecast's board: one plain profile for the three, no play built, so what the use plays is unknown.
        var forecast = SimAdapter.Build(fight.Me, fight.Combat, out var forecastIds);
        Assert.Single(forecast.You.Draw!.Rest);
        SimEngine.ApplyPlayTo(forecast, SimAdapter.Potion(potion, fight.Me, fight.Combat, forecastIds, Who.You), out var unknown, out _);
        Assert.All(unknown, branch => Assert.NotNull(branch.Unmodelled));

        // Its own board: a profile per card, each with its play from the pile.
        var start = SimAdapter.Build(fight.Me, fight.Combat, out var ids, pileAutoPlays: true);
        Assert.Equal(2, start.You.Draw!.Rest.Count);
        Assert.All(start.You.Draw.Rest, group => Assert.NotNull(group.Card.FromPile));
        SimEngine.ApplyPlayTo(start, SimAdapter.Potion(potion, fight.Me, fight.Combat, ids, Who.You), out var after, out _);
        var hp = enemy.CurrentHp;
        Assert.All(after, branch =>
        {
            Assert.Null(branch.Unmodelled);
            Assert.Equal(hp - 12, branch.Enemy(ids[enemy])!.Hp);
            Assert.Equal(5, branch.You.Block);
            Assert.Equal(3, branch.AutoPlayed);
        });
    }

    /// <summary>
    /// The cards a list holds that the code filled itself (decision #339): Orobic Acid's three AddRanges of a factory's one card make 3; Pot of Ghouls'
    /// Soul.CreateInHand makes its Cards value of Souls (read through Soul.Create's count); Bottled Song's loop over its melody tags, an array the
    /// reader can't size, stays unknown rather than one.
    /// </summary>
    [Fact]
    public void ACardListsFillsAreItsCount()
    {
        GameModels.EnsureLoaded();
        var fight = new GameFight();
        var enemy = fight.Combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, slot: null);
        fight.Combat.AddCreature(enemy);
        var start = SimAdapter.Build(fight.Me, fight.Combat, out var ids);
        CardEventAction Made<T>() where T : PotionModel
        {
            var potion = ModelDb.Potion<T>().ToMutable();
            fight.Me.AddPotionInternal(potion);
            return Assert.IsType<CardEventAction>(Assert.Single(SimAdapter.Potion(potion, fight.Me, fight.Combat, ids, Who.You).Body));
        }
        Assert.Equal(3, Made<MegaCrit.Sts2.Core.Models.Potions.OrobicAcid>().Count);
        var souls = Made<MegaCrit.Sts2.Core.Models.Potions.PotOfGhouls>();
        Assert.Equal(nameof(MegaCrit.Sts2.Core.Models.Cards.Soul), souls.Card?.Name);
        Assert.Equal(2, souls.Count);
        // Bottled Song: one a round of its loop over its three melody tags, a constant array the compiler fills from its data (decision #369).
        Assert.Equal(3, Made<MegaCrit.Sts2.Core.Models.Potions.BottledSong>().Count);
        Assert.NotNull(start);
    }

    private static string AimName(int aim) => aim switch
    {
        Who.You => "you",
        Who.None => "no one",
        _ when Who.IsEnemy(aim) => "enemy",
        _ => "teammate",
    };

    /// <summary>An action as the snapshot shows it: its kind and what matters of it (a captured term's numbers aren't the translation's).</summary>
    private static string Describe(SimAction action) => action switch
    {
        DamageAction damage => $"damage {damage.Damage.Base} {damage.Damage.Props}" + (damage.Players is { Count: > 0 } players ? $" to {string.Join(", ", players.Select(AimName))} then" : "")
            + (damage.EveryEnemy ? " to every enemy" : damage.TargetId is { } target ? $" to {AimName(target)}" : ""),
        GainBlockAction block => $"block {block.Base} {block.Props} to {AimName(block.To)}",
        ApplyPowerAction power => $"{power.Power.Name} {power.Amount} on {(power.To == ApplyPowerAction.Where.Creature ? AimName(power.Creature) : power.To.ToString())}",
        CardEventAction cardEvent => $"{cardEvent.Event} {cardEvent.Count}" + (cardEvent.Player is { } player ? $" for {AimName(player)}" : ""),
        OrbAction orb => $"orb {orb.Kind} {orb.Orb} {orb.Slots} to {AimName(orb.To)}",
        NotModelledAction unknown => "? " + unknown.Reason,
        _ => action.ToString(),
    };
}
