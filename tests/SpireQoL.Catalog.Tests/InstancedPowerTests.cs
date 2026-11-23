using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using SpireQoL.Procs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Powers whose every application is a copy of its own (PowerInstanceType.Instanced), each with its own amount and values: every
/// such power the simulator models says so (SimModel.Instanced) and says how its copies act, and the adapter reads each the way its
/// model needs, from the game's own objects (Monologue's values totalled over its copies, The Bomb's copies one by one, the players
/// covering a Covered teammate, the player any Imitation Learning copies).
/// </summary>
[Collection(GameFightCollection.Name)]
public class InstancedPowerTests
{
    [Fact]
    public void EveryInstancedPowerModelSaysSo()
    {
        GameModels.EnsureLoaded();
        var instanced = ModelDb.AllPowers.ToDictionary(power => power.GetType().Name, power => power.InstanceType == PowerInstanceType.Instanced);
        var wrong = SimModels.Names.Where(instanced.ContainsKey).Where(name => SimModels.Get(name)!.Instanced != instanced[name])
            .Select(name => $"{name}: the game's is {(instanced[name] ? "" : "not ")}instanced").OrderBy(line => line, StringComparer.Ordinal).ToList();
        Assert.True(wrong.Count == 0, "models whose Instanced isn't their game power's (review how its copies act, then set it):\n" + string.Join("\n", wrong));
    }

    private static T Power<T>(Creature creature, int amount) where T : PowerModel
    {
        var power = (T)ModelDb.Power<T>().ToMutable();
        power.ApplyInternal(creature, amount);
        return power;
    }

    [Fact]
    public void TwoMonologuesGiveTwoStrengthACard()
    {
        // Each Monologue played applies a copy whose Strength the card sets (Monologue.OnPlay); each copy gives its own on every card
        // its owner plays (MonologuePower.BeforeCardPlayed binds it, AfterCardPlayed applies it).
        var gameFight = new GameFight();
        gameFight.Me.PlayerCombatState!.Energy = 3;
        foreach (var copy in new[] { Power<MonologuePower>(gameFight.Me.Creature, amount: 1), Power<MonologuePower>(gameFight.Me.Creature, amount: 1) })
            copy.DynamicVars.Strength.BaseValue = 1;
        var card = gameFight.Add<DefendIronclad>();
        var start = SimAdapter.Build(gameFight.Me, gameFight.Combat, out var ids);
        var facts = PlayFacts.For(card, gameFight.Me, gameFight.Combat, plays: 1);
        Assert.NotNull(facts);
        SimEngine.ApplyPlayTo(start, SimAdapter.Play(card, facts!, gameFight.Me, gameFight.Combat, ids, Who.None), out var branches, out var truncated);
        Assert.False(truncated);
        Assert.Equal(2, Assert.Single(branches).You.Powers[PowerId.Strength]);
    }

    [Fact]
    public void EachBombIsReadWithItsOwnTurnsAndDamage()
    {
        var gameFight = new GameFight();
        var first = Power<TheBombPower>(gameFight.Me.Creature, amount: 1);
        first.SetDamage(40);
        var second = Power<TheBombPower>(gameFight.Me.Creature, amount: 3);
        second.SetDamage(50);
        Assert.Equal(new[] { (1, 40), (3, 50) }, SimReaders.Counters(gameFight.Me.Creature).Select(copy => (copy.Left, copy.Amount)));
    }

    /// <summary>Three players in a fight (you first) and an enemy, with the adapter's ids (you, teammate 0, teammate 1).</summary>
    private static (Player[] Players, Func<Creature, int?> IdOf) ThreePlayers()
    {
        GameModels.EnsureLoaded();
        var players = Enumerable.Range(1, 3).Select(netId => Player.CreateForNewRun<Deprived>(MegaCrit.Sts2.Core.Unlocks.UnlockState.all, (ulong)netId)).ToArray();
        var run = RunState.CreateForTest(players, seed: "TESTSEED");
        var combat = new CombatState(null, run);
        foreach (var player in players)
        {
            player.ResetCombatState();
            combat.AddPlayer(player);
        }
        var enemy = combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, slot: null);
        combat.AddCreature(enemy);
        int? IdOf(Creature creature) => creature == players[0].Creature ? Who.You : creature == players[1].Creature ? Who.Teammate(teammateIndex: 0)
            : creature == players[2].Creature ? Who.Teammate(teammateIndex: 1) : null;
        return (players, IdOf);
    }

    [Fact]
    public void ACoveredTeammateIsReadWithEveryPlayerCoveringThem()
    {
        // Two Intercepts on teammate 1, yours and teammate 0's: a Covered each (instanced), both coverers in its Data.
        var (players, idOf) = ThreePlayers();
        var covered = players[2].Creature;
        var fromYou = Power<CoveredPower>(covered, amount: 1);
        fromYou.Applier = players[0].Creature;
        var fromTeammate = Power<CoveredPower>(covered, amount: 1);
        fromTeammate.Applier = players[1].Creature;
        var expected = Sim.Models.CoveredPower.CovererBit(Who.You) | Sim.Models.CoveredPower.CovererBit(Who.Teammate(teammateIndex: 0));
        Assert.Equal(expected, SimReaders.Powers(covered, idOf).DataOf(PowerId.Covered));
    }

    [Fact]
    public void ImitationLearningIsReadAsCopyingYouWhenAnyCopyDoes()
    {
        // Teammate 0 imitates you, then teammate 1 (a copy per player: ImitationLearning.OnPlay): your Power cards set it off,
        // whichever copy is read last.
        var (players, idOf) = ThreePlayers();
        var imitator = players[1].Creature;
        var playerTarget = AccessTools.Field(typeof(ImitationLearningPower), "_playerTarget");
        playerTarget.SetValue(Power<ImitationLearningPower>(imitator, amount: 2), players[0]);
        playerTarget.SetValue(Power<ImitationLearningPower>(imitator, amount: 2), players[2]);
        Assert.Equal(DataCreature.Of(Who.You), SimReaders.Powers(imitator, idOf).DataOf(PowerId.ImitationLearning));
    }
}
