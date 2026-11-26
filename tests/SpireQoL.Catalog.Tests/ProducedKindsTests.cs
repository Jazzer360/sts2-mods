using System.Text.RegularExpressions;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Everything the mod handles is something the game still does (decision #260): the game's code, read on both branches' assemblies (every
/// card program, every power, relic and enchantment read from its code, every power's rules), produces each kind of step, value, fact and
/// condition the simulator runs, and every action a card's play is made of (the card snapshot: every card of the installed game). A kind
/// nothing produces is a handler for a mechanic the game took out (v0.114.0's Quietus arrow, Free Card): remove it, or say here what
/// still produces it. The tables keyed by a game class's name (the coverage ledger, the relic reasons, the card rules' reasons) name only
/// classes a branch has.
/// </summary>
[Collection(GameFightCollection.Name)]
public class ProducedKindsTests
{
    /// <summary>Kinds nothing above produces, and what does (or why they stay). A name only, never a card or relic to quiet one.</summary>
    private static readonly Dictionary<string, string> ProducedElsewhere = new(StringComparer.Ordinal)
    {
        // Facts the reader translates for code only the hand-written models hold today (their conversions are queued: decision #195).
        ["fact CreatureIsMonster"] = "the reader's Creature.IsMonster test: asked by monster-side hooks the hand-written models hold (the monster-state conversions)",
        // A card's cost hooks read (decision #332), not yet run: no simulation works out a hand card's live cost (TRACKER: mid-play card costs).
        ["fact CostSoFar"] = "the reader's originalCost of a card's cost hook (Curious's, Borrowed Time's), read into a program no simulation runs yet",
        // Conditions of a monster's move rules (MoveConditions), not of a card's steps.
        ["condition KnownCondition"] = "MoveConditions: a monster move rule's condition the adapter settles on the board (always, never, or worked out)",
        // Actions the engine or the adapter makes, not a card's program.
        ["action CardPlayAction"] = "SimPlay: the play itself, as the engine runs it (and SimWarmUp's made-up fight)",
        ["action PotionUseAction"] = "SimPlay.Of: a potion's use, its body read from its OnUse (SimAdapter.Potion, decision #313)",
        ["action LeaveHandAction"] = "SimEngine.Actions: the card played leaving its owner's hand",
        ["action MoveAttackAction"] = "SimAdapter.Moves: an enemy's move's attack, read from its intent",
        ["action MoveBlockAction"] = "SimAdapter.Moves: an enemy's move's block",
        ["action MovePowerAction"] = "SimAdapter.Moves: an enemy's move's power",
        ["action MoveStunAction"] = "SimAdapter.Moves: an enemy's move stunning itself (Bowlbug Rock's Headbutt when off balance)",
        ["action UnreadAction"] = "SimAdapter.Play: a step whose code couldn't be read (unknown, never a guess)",
        ["action TriggerPoisonAction"] = "an older report's play, replayed (PoisonTriggerTests): Outbreak's poison is ModelCommandAction now",
        // The play shorthand (SimPlay's own fields): what the simulator tests, the golden fights and older reports' replays write a play as.
        ["action CardResolvedAction"] = "SimPlay.Of: every play's script ends with the card resolved (the cards it leaves in your hand, its draws)",
        ["action FixedHitsAction"] = "SimPlay.Of: the shorthand's Procs (damage procs in final numbers), written by the simulator tests and the golden fights",
        ["action HitYouAction"] = "SimPlay.Of: the shorthand's DamageToYou, written by the simulator tests and the golden fights",
    };

    private static IEnumerable<string> Paths() => new[] { BranchCode.BetaPath, BranchCode.MainPath }.Where(File.Exists);

    /// <summary>Every kind the game's code produces, as "step X", "value X", "fact X", "condition X".</summary>
    private static HashSet<string> Produced()
    {
        var produced = new HashSet<string>(StringComparer.Ordinal);
        void Value(HookValue? value)
        {
            if (value == null) return;
            produced.Add("value " + value.GetType().Name);
            switch (value)
            {
                case HookFactValue fact: produced.Add("fact " + fact.Fact); break;
                case HookMath math: Value(math.Left); Value(math.Right); break;
                case HookNot not: Value(not.Inner); break;
                case HookTest test: Value(test.Test); break;
                case HookWays ways: foreach (var way in ways.Ways) { Value(way.When); Value(way.Value); } break;
                case HookOrbValue orbValue: Value(orbValue.Base); break;
            }
        }
        void Condition(SimCondition condition)
        {
            produced.Add("condition " + condition.GetType().Name);
            switch (condition)
            {
                case NotCondition not: Condition(not.Inner); break;
                case AllCondition all: foreach (var part in all.Parts) Condition(part); break;
                case AnyCondition any: foreach (var part in any.Parts) Condition(part); break;
            }
        }
        foreach (var path in Paths())
        {
            var code = BranchCode.Of(path);
            foreach (var spec in code.Specs())
            {
                foreach (var step in spec.Steps()) produced.Add("step " + step.GetType().Name);
                foreach (var value in spec.Values()) Value(value);
            }
            foreach (var rule in ReadModels.PowerRules(code.Assembly.GetTypes(), code.Read)) Value(rule.ScaledForPlayers);
            foreach (var card in code.Cards())
            {
                var program = ProgramReader.Read(card, code.Read);
                foreach (var found in CardEffects.Find(program, _ => true))
                    if (ConditionReader.Read(found.Condition, new ConditionReader.Context(card, code.Read, Board: null, program)).Condition is { } condition)
                        Condition(condition);
            }
        }
        // Every card's play in the installed game, as the adapter builds it (the card snapshot holds each one's actions).
        var plays = Path.Combine(CardPlaySnapshotTests.SourceFolder(), "Snapshots", "card-plays.txt");
        foreach (Match action in Regex.Matches(File.ReadAllText(plays), "\"\\$action\":\"(\\w+)\""))
            produced.Add("action " + action.Groups[1].Value);
        return produced;
    }

    /// <summary>Every kind the simulator handles: its step, value, condition and action types, and its facts.</summary>
    private static IEnumerable<string> Handled()
    {
        var types = typeof(HookStep).Assembly.GetTypes().Where(type => !type.IsAbstract && type.IsClass).ToList();
        IEnumerable<string> Of<T>(string kind) => types.Where(typeof(T).IsAssignableFrom).Select(type => kind + " " + type.Name);
        return Of<HookStep>("step").Concat(Of<HookValue>("value")).Concat(Of<SimCondition>("condition")).Concat(Of<SimAction>("action"))
            .Concat(Enum.GetValues<HookFact>().Select(fact => "fact " + fact));
    }

    [Fact]
    public void EveryKindTheSimulatorHandlesIsOneTheGamesCodeProduces()
    {
        if (!Paths().Any()) return; // no decompile here
        var produced = Produced();
        var unproduced = Handled().Where(kind => !produced.Contains(kind) && !ProducedElsewhere.ContainsKey(kind)).Order().ToList();
        Assert.True(unproduced.Count == 0,
            "Kinds the simulator handles that no game code produces (a mechanic the game took out: remove the handler, or say in ProducedElsewhere what produces it):\n"
            + string.Join("\n", unproduced));
        var stale = ProducedElsewhere.Keys.Where(kind => produced.Contains(kind) || !Handled().Contains(kind)).ToList();
        Assert.True(stale.Count == 0, "ProducedElsewhere entries the game's code produces now, or the simulator no longer has (remove them):\n" + string.Join("\n", stale));
    }

    [Fact]
    public void EveryTableKeyedByAGameClassNamesOneABranchHas()
    {
        if (!Paths().Any()) return;
        var classes = Paths().SelectMany(path => BranchCode.Of(path).Assembly.GetTypes()).Select(type => type.Name).ToHashSet(StringComparer.Ordinal);
        var tables = new (string Name, IEnumerable<string> Keys)[]
        {
            ("CoverageLedger.Reviewed", Diagnostics.CoverageLedger.ReviewedEntries.Keys),
            ("RelicTracking.Reasons", Diagnostics.RelicTracking.Reasons.Keys),
            ("RelicChoiceTracking.Reasons", Diagnostics.RelicChoiceTracking.Reasons.Keys),
            ("CardAffinity.Reviewed", Data.CardAffinity.Reviewed.Keys),
        };
        var gone = tables.SelectMany(table => table.Keys.Where(key => !classes.Contains(key)).Select(key => $"{table.Name}: {key}")).ToList();
        Assert.True(gone.Count == 0, "Entries for game classes neither branch has any more (remove them):\n" + string.Join("\n", gone));
    }
}
