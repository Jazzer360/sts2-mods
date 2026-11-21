using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Intent Forecast (decision #72) reads every enemy's move rules (IntentReader): each enemy in the installed game builds its
/// move state machine (as a fight copy does) and the reader turns it into the forecast model. A new enemy, or a new kind of
/// move state the forecast doesn't know, fails here before anyone fights it.
/// </summary>
public class IntentRulesTests
{
    private static readonly MethodInfo Generate =
        typeof(MonsterModel).GetMethod("GenerateMoveStateMachine", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Fact]
    public void EveryEnemysMoveRulesAreRead()
    {
        GameModels.EnsureLoaded();
        var monsters = ModelDb.All.OfType<MonsterModel>().ToList();
        Assert.True(monsters.Count > 100, "the game's enemies didn't load");
        var unread = new List<string>();
        foreach (var monster in monsters)
        {
            MonsterMoveStateMachine machine;
            try { machine = (MonsterMoveStateMachine)Generate.Invoke(monster.ToMutable(), null)!; }
            catch (Exception ex) { unread.Add($"{monster.GetType().Name}: rules not built ({(ex.InnerException ?? ex).Message})"); continue; }
            if (IntentReader.Graph(machine) == null) unread.Add($"{monster.GetType().Name}: rules not read");
        }
        Assert.True(unread.Count == 0, "Enemies whose move rules the forecast can't read:\n" + string.Join("\n", unread));
    }

    /// <summary>Every enemy's moves, each with its monster (a fight copy, so amounts read as in a fight).</summary>
    private static IEnumerable<(MonsterModel Monster, MoveState Move)> EveryMove()
    {
        GameModels.EnsureLoaded();
        foreach (var canon in ModelDb.All.OfType<MonsterModel>())
        {
            var monster = canon.ToMutable();
            MonsterMoveStateMachine machine;
            try { machine = (MonsterMoveStateMachine)Generate.Invoke(monster, null)!; } catch { continue; }
            foreach (var move in machine.States.Values.OfType<MoveState>()) yield return ((MonsterModel)monster, move);
        }
    }

    [Fact]
    public void AMovesPowersAreReadFromItsCode()
    {
        // Corpse Slug's Goop: PowerCmd.Apply<FrailPower>(..., GoopFrailAmt) with GoopFrailAmt 2.
        var goop = EveryMove().Single(x => x.Monster.GetType().Name == "CorpseSlug" && x.Move.Id == "GOOP_MOVE");
        var applied = MovePowers.Of(goop.Monster, goop.Move);
        Assert.Equal("FrailPower 2", string.Join(", ", applied.Select(otherApplied => $"{otherApplied.Power.Name} {otherApplied.Amount}")));
    }

    [Fact]
    public void EveryBuffAndDebuffMoveNamesItsPowers()
    {
        // A buff or debuff intent whose move applies no power the reader finds (a power applied another way): listed, so a
        // new pattern is seen. Moves whose amount isn't readable show the icon alone and aren't listed.
        var missing = new List<string>();
        var counted = 0;
        foreach (var (monster, move) in EveryMove())
        {
            var kinds = move.Intents.Select(abstractIntent => abstractIntent.GetType().Name).ToList();
            if (!kinds.Any(k => k.Contains("Buff") || k.Contains("Debuff"))) continue;
            counted++;
            var id = $"{monster.GetType().Name}.{move.Id}";
            if (MovePowers.Of(monster, move).Count == 0 && !WithoutPowers.ContainsKey(id)) missing.Add($"{monster.GetType().Name}.{move.Id} ({string.Join(", ", kinds)})");
        }
        Assert.True(counted > 50, $"only {counted} buff or debuff moves found");
        Assert.True(missing.Count == 0, $"{missing.Count} buff/debuff moves with no power read:\n" + string.Join("\n", missing));
    }

    /// <summary>Buff and debuff moves that apply no power, reviewed from their code.</summary>
    private static readonly Dictionary<string, string> WithoutPowers = new()
    {
        ["KnowledgeDemon.CURSE_OF_KNOWLEDGE_MOVE"] = "a choice of curse cards for each player, not a power",
    };
}
