using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The history ledger (decision #191): every question a card, relic or power asks of the game's log of the fight is read from its code on
/// both branches and translated into a query the ledger counts, or is a gap of a reviewed kind (HistoryQueryReader.GapKinds); a question of
/// a shape the reader doesn't know fails here. The adapter's seed of each query is the game's own answer to the code's own question, on a
/// fight built from the game's objects.
/// </summary>
[Collection(GameFightCollection.Name)]
public class HistoryLedgerTests
{
    public static TheoryData<string> Branches => new() { "beta", "main" };

    private static string PathOf(string branch) => branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;

    [Theory]
    [MemberData(nameof(Branches))]
    public void EveryQuestionToTheLogIsAQueryOrAReviewedGap(string branch)
    {
        if (!File.Exists(PathOf(branch))) return;
        using var code = new BranchCode(PathOf(branch));
        var unknown = new List<string>();
        var gapsUsed = new HashSet<string>();
        var translated = 0;
        foreach (var type in code.Models())
            foreach (var (method, query) in CardProgramReader.HistoryQueries(type, code.Read))
            {
                var reading = HistoryQueryReader.Read((QueryValue)query, code.EnumName);
                if (reading.Query != null) translated++;
                else if (reading.Gap is { } gap && HistoryQueryReader.GapKinds.ContainsKey(gap)) gapsUsed.Add(gap);
                else unknown.Add($"{type.Name} {method}: {reading.Gap} ({reading.Why})");
            }
        Assert.True(unknown.Count == 0, "Questions to the fight's log of no reviewed kind:\n" + string.Join("\n", unknown));
        Assert.True(translated > 20, $"only {translated} questions read");
        if (branch == "beta")
            Assert.Empty(HistoryQueryReader.GapKinds.Keys.Except(gapsUsed)); // a reviewed kind nothing asks any more
    }

    /// <summary>A model, the method its question is in, and the query it reads as.</summary>
    private static readonly (string Model, string Method, string Query, bool Any)[] Known =
    {
        ("ForgottenRitual", "get_WasCardExhaustedThisTurn", "history:CardExhausted/turn/Owner", true),
        ("EvilEye", "get_WasCardExhaustedThisTurn", "history:CardExhausted/turn/Owner", true),
        ("Spite", "MoveNext", "history:DamageReceived/turn/Owner/Unblocked", true),
        ("DeathsDoor", "get_WasDoomAppliedThisTurn", "history:PowerReceived/turn/Owner/power DoomPower", true),
        ("Flatten", "get_HasOstyAttackedThisTurn", "history:CreatureAttacked/turn/OwnersOsty", true),
        ("Rattle", "b__", "history:CreatureAttacked/turn/OwnersOsty", false),
        ("Murder", "b__", "history:CardDrawn/fight/Owner", false),
        ("Finisher", "b__", "Finished:Attack", false),
        ("FeralPower", "AfterApplied", "history:CardPlayStarted/turn/Owner/ZeroEnergy/type Attack", false),
        ("EchoFormPower", "ModifyCardPlayCount", "history:CardPlayStarted/turn/Owner/FirstInSeries", false),
        ("Voltaic", "b__", "history:OrbChanneled/fight/Owner/orb Lightning", false),
        ("Anguish", "b__", "history:CardGenerated/fight/Owner/card Sadness", false),
        ("Midnight", "AfterCardEnteredCombat", "history:CardExhausted/fight/Anyone", false),
    };

    [Theory]
    [MemberData(nameof(Branches))]
    public void KnownQuestionsReadAsTheirQueries(string branch)
    {
        if (!File.Exists(PathOf(branch))) return;
        using var code = new BranchCode(PathOf(branch));
        var models = code.Models().GroupBy(type => type.Name).ToDictionary(group => group.Key, group => group.First());
        var checkedModels = 0;
        foreach (var (model, method, expected, any) in Known)
        {
            if (!models.TryGetValue(model, out var type)) continue; // a model only the other branch has
            var readings = CardProgramReader.HistoryQueries(type, code.Read).Where(found => found.Method.Contains(method, StringComparison.Ordinal))
                .Select(found => HistoryQueryReader.Read((QueryValue)found.Query, code.EnumName)).ToList();
            if (readings.Count == 0 && branch == "main") continue; // main asks nothing there (its Forgotten Ritual gains energy on every play)
            var reading = Assert.Single(readings.Where(candidate => candidate.Query?.Key == expected).DistinctBy(candidate => candidate.Query));
            Assert.Equal(any, reading.AsksAny);
            checkedModels++;
        }
        Assert.True(checkedModels > 5);
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void EachModelsQuestionsAreItsGameCodes(string branch)
    {
        // A model that reads the ledger (Feral's AfterApplied, Echo Form's ModifyCardPlayCount) declares its questions; each is one its game
        // class's code asks, read and translated from that code on each branch.
        if (!File.Exists(PathOf(branch))) return;
        using var code = new BranchCode(PathOf(branch));
        var models = code.Models().GroupBy(type => type.Name).ToDictionary(group => group.Key, group => group.First());
        var checkedModels = 0;
        foreach (var name in SimModels.Names)
        {
            if (SimModels.Get(name) is not { History.Count: > 0 } model || !models.TryGetValue(name, out var type)) continue;
            var asked = CardProgramReader.HistoryQueries(type, code.Read).Select(found => HistoryQueryReader.Read((QueryValue)found.Query, code.EnumName).Query).ToList();
            foreach (var query in model.History) Assert.True(asked.Contains(query), $"{name} asks {query}; its code asks {string.Join(", ", asked)}");
            checkedModels++;
        }
        Assert.True(checkedModels >= 2);
    }

    // ---- the seed: the game's own answer

    private static CardPlay PlayOf(CardModel card, Player player, int energy = 1, int index = 0, bool auto = false) => new()
    {
        Card = card, Player = player, Target = null, ResultPile = PileType.Discard, IsAutoPlay = auto, PlayIndex = index, PlayCount = index + 1,
        Resources = new ResourceInfo { EnergySpent = energy, EnergyValue = energy, StarsSpent = 0, StarValue = 0 },
    };

    [Fact]
    public void TheSeedIsTheGamesOwnAnswerToEachQuestion()
    {
        var gameFight = new GameFight(teammates: 1);
        var me = gameFight.Me;
        var mate = gameFight.Teammates[0];
        var combat = gameFight.Combat;
        var enemy = combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, slot: null);
        combat.AddCreature(enemy);
        var strike = gameFight.Add<StrikeIronclad>();
        var defend = gameFight.Add<DefendIronclad>();
        var dazed = gameFight.Add<Dazed>();
        var sadness = gameFight.Add<Sadness>(PileType.Discard);
        var theirs = gameFight.AddFor<StrikeIronclad>(mate, PileType.Hand);
        var doom = (PowerModel)ModelDb.Power<DoomPower>().ToMutable();
        doom.ApplyInternal(enemy, amount: 3);
        var strength = (PowerModel)ModelDb.Power<StrengthPower>().ToMutable();
        strength.ApplyInternal(me.Creature, amount: 1);
        var lightning = ModelDb.Orb<LightningOrb>().ToMutable();
        lightning.Owner = me;
        var round = combat.RoundNumber;
        var side = combat.CurrentSide;
        var players = combat.Players;
        var history = CombatManager.Instance.History;
        var entries = GameFight.Log;
        entries.Clear();
        try
        {
            void Now(CombatHistoryEntry entry) => entries.Add(entry);
            // Plays: an Attack for 0 and its replay, a Skill for 1, an auto-played Dazed (Ethereal), one earlier this fight, a teammate's.
            foreach (var play in new[] { PlayOf(strike, me, energy: 0), PlayOf(strike, me, energy: 0, index: 1), PlayOf(defend, me), PlayOf(dazed, me, energy: 0, auto: true), PlayOf(theirs, mate) })
            {
                Now(new CardPlayStartedEntry(play, round, side, history, players));
                Now(new CardPlayFinishedEntry(play, round, side, history, players));
            }
            entries.Add(new CardPlayFinishedEntry(PlayOf(defend, me), round - 1, side, history, players));
            // Exhausts, draws (the turn's hand draw and a card's), made cards, yours and a teammate's, this turn and before.
            Now(new CardExhaustedEntry(defend, round, side, history, players));
            entries.Add(new CardExhaustedEntry(strike, round - 1, side, history, players));
            Now(new CardExhaustedEntry(theirs, round, side, history, players));
            Now(new CardDrawnEntry(dazed, round, side, fromHandDraw: false, history, players));
            Now(new CardDrawnEntry(strike, round, side, fromHandDraw: true, history, players));
            Now(new CardDrawnEntry(theirs, round, side, fromHandDraw: false, history, players));
            Now(new CardGeneratedEntry(sadness, me, round, side, history, players));
            Now(new CardGeneratedEntry(dazed, me, round, side, history, players));
            Now(new CardGeneratedEntry(theirs, mate, round, side, history, players));
            // Damage you took through your block, damage your block stopped, an earlier turn's, the enemy's from your attack.
            Now(new DamageReceivedEntry(new DamageResult(me.Creature, ValueProp.Move) { UnblockedDamage = 4 }, me.Creature, enemy, null, round, side, history, players));
            Now(new DamageReceivedEntry(new DamageResult(me.Creature, ValueProp.Move) { UnblockedDamage = 0, BlockedDamage = 5 }, me.Creature, enemy, null, round, side, history, players));
            entries.Add(new DamageReceivedEntry(new DamageResult(me.Creature, ValueProp.Move) { UnblockedDamage = 3 }, me.Creature, enemy, null, round - 1, side, history, players));
            Now(new DamageReceivedEntry(new DamageResult(enemy, ValueProp.Move) { UnblockedDamage = 6 }, enemy, me.Creature, strike, round, side, history, players));
            // Powers applied: Doom by you, Strength by you, Doom by a teammate. Attacks, a Lightning channelled.
            Now(new PowerReceivedEntry(doom, 3, me.Creature, round, side, history, players));
            Now(new PowerReceivedEntry(strength, 1, me.Creature, round, side, history, players));
            Now(new PowerReceivedEntry(doom, 2, mate.Creature, round, side, history, players));
            Now(new CreatureAttackedEntry(me.Creature, Array.Empty<DamageResult>(), round, side, history, players));
            Now(new OrbChanneledEntry(lightning, round, side, history, players));

            var compared = 0;
            var counted = 0;
            var mismatches = new List<string>();
            foreach (var type in typeof(CardModel).Assembly.GetTypes().Where(type => type.Namespace is "MegaCrit.Sts2.Core.Models.Cards" or "MegaCrit.Sts2.Core.Models.Powers" or "MegaCrit.Sts2.Core.Models.Relics" && !type.IsNested && !type.IsAbstract))
                foreach (var (method, found) in CardProgramReader.HistoryQueries(type, RuntimeCode.Read))
                {
                    if (HistoryQueryReader.Read((QueryValue)found).Query is not { } query) continue;
                    // The code's own question, its own lambda, evaluated on this fight: "this" a model of its kind owned by you, a calculated
                    // value's card your Strike.
                    object self = typeof(PowerModel).IsAssignableFrom(type) ? strength : strike;
                    var board = new ProgramBoard(strike) { Self = self };
                    var answer = ProgramEvaluator.Value(found, board, CardProgram.Empty, new Dictionary<string, object?> { ["card"] = strike });
                    var expected = answer switch { bool any => any ? 1 : 0, decimal count => (int)count, int count => count, _ => (int?)null };
                    var turn = NamedSet<string>.Empty;
                    var fight = HistoryLedger.Seed(new[] { query }, me, combat, ref turn);
                    var seeded = (query.Scope == HistoryScope.ThisTurn ? turn : fight)[query.Key];
                    if (expected == null) mismatches.Add($"{type.Name} {method}: the game's answer couldn't be read ({found})");
                    else if ((answer is bool ? Math.Min(1, seeded) : seeded) != expected) mismatches.Add($"{type.Name} {method}: seeded {seeded}, the game's {expected} ({query})");
                    compared++;
                    if (expected > 0) counted++;
                }
            Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
            Assert.True(compared > 20, $"only {compared} compared");
            Assert.True(counted > 15, $"only {counted} questions the fight has entries for"); // the fight answers most of them with entries, not 0
        }
        finally
        {
            entries.Clear();
        }
    }
}
