using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using SpireQoL.Procs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Every card in the installed game on a few standard boards built from the game's own objects (GameFight): what PlayFacts reads
/// from it and the play the adapter builds (SimAdapter.Play), written as text and compared with Snapshots/card-plays.txt. The safety
/// net under moving per-card tables onto the card programs (decision #182, docs/CARD_PROGRAMS.md): a change that should only move
/// where a value comes from leaves it unchanged. A difference that is a real fix regenerates it (SPIREQOL_UPDATE_SNAPSHOT=1), with
/// the card and why in the commit. A game update regenerates it too.
/// </summary>
[Collection(GameFightCollection.Name)]
public class CardPlaySnapshotTests
{
    private const string FileName = "card-plays.txt";

    /// <summary>The boards each card is read on.</summary>
    private enum Board
    {
        /// <summary>One enemy; a Strike, a Defend and a Dazed beside it in hand; six cards to draw, three in the discard pile.</summary>
        Standard,
        /// <summary>The same with the card upgraded.</summary>
        Upgraded,
        /// <summary>Two enemies, a full hand with a Sly card and Drum of Battle, an empty draw pile and three cards in the discard pile.</summary>
        Crowded,
        /// <summary>The standard board with the card played twice (as Echo Form or Burst gives): its later play's actions and counts.</summary>
        Replayed,
        /// <summary>
        /// The standard board with a teammate in the fight (a Strike and a Defend in their hand, three cards to draw, one discarded): a
        /// card aimed at an ally is aimed at them, and what it gives or makes for teammates shows.
        /// </summary>
        Teammate,
        /// <summary>
        /// The standard board with the card alone in hand (outcomes only): conditions on your hand take the other way here (Impatience's
        /// "no Attack in hand", Restlessness' empty hand), so each is seen both ways.
        /// </summary>
        Alone,
    }

    /// <summary>The boards the play snapshot reads (Alone is for outcomes only).</summary>
    private static readonly Board[] PlayBoards = { Board.Standard, Board.Upgraded, Board.Crowded, Board.Replayed, Board.Teammate };

    [Fact]
    public void EveryCardsFactsAndPlayAreUnchanged()
    {
        GameModels.EnsureLoaded();
        var actual = new List<string>();
        foreach (var canon in ModelDb.AllCards.OrderBy(card => card.GetType().Name, StringComparer.Ordinal).ToList())
            foreach (var board in PlayBoards)
                actual.Add($"{canon.GetType().Name} [{board}] {Read(canon, board).Play}");
        Compare(FileName, actual);
    }

    /// <summary>
    /// What each card's play results in, on every board: the play run through the simulator (SimEngine.ApplyPlayTo), and for each branch
    /// it ends in (identical ones merged) its weight, your HP, block, energy, hand and piles, each teammate's, the enemies' HP and block,
    /// and the card events of the play (its last play's, for a replay). A change to what a play does, not just to how it's built, shows
    /// here even when the play's actions read the same.
    /// </summary>
    [Fact]
    public void EveryCardsOutcomeIsUnchanged()
    {
        GameModels.EnsureLoaded();
        var actual = new List<string>();
        foreach (var canon in ModelDb.AllCards.OrderBy(card => card.GetType().Name, StringComparer.Ordinal).ToList())
            foreach (var board in Enum.GetValues<Board>())
                actual.Add($"{canon.GetType().Name} [{board}] {Read(canon, board).Outcome}");
        Compare(OutcomesFileName, actual);
    }

    private const string OutcomesFileName = "card-outcomes.txt";

    private static void Compare(string fileName, List<string> actual)
    {
        var path = Path.Combine(SourceFolder(), "Snapshots", fileName);
        if (Environment.GetEnvironmentVariable("SPIREQOL_UPDATE_SNAPSHOT") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, actual);
            return;
        }
        Assert.True(File.Exists(path), $"missing {path}: generate it with SPIREQOL_UPDATE_SNAPSHOT=1");
        var expected = File.ReadAllLines(path);
        var expectedByKey = expected.ToDictionary(KeyOf, line => line);
        var changed = actual.Where(line => !expectedByKey.TryGetValue(KeyOf(line), out var before) || before != line).ToList();
        var gone = expected.Select(KeyOf).Except(actual.Select(KeyOf)).ToList();
        var report = new StringBuilder();
        foreach (var line in changed.Take(12))
        {
            report.AppendLine("now:    " + line);
            report.AppendLine("before: " + (expectedByKey.TryGetValue(KeyOf(line), out var before) ? before : "(none)"));
        }
        foreach (var key in gone.Take(5)) report.AppendLine("gone: " + key);
        Assert.True(changed.Count == 0 && gone.Count == 0, $"{fileName}: {changed.Count} lines changed, {gone.Count} gone:\n{report}");
    }

    /// <summary>"Card [Board]": what a line is about.</summary>
    private static string KeyOf(string line) => line[..(line.IndexOf(']') + 1)];

    private static string SourceFolder()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder != null && !File.Exists(Path.Combine(folder.FullName, "SpireQoL.Catalog.Tests.csproj"))) folder = folder.Parent;
        return folder?.FullName ?? throw new InvalidOperationException("test project folder not found");
    }

    /// <summary>The card's facts and play on a board, and what the play results in, each as one line.</summary>
    private sealed record Reading(string Play, string Outcome);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type, Board), Reading> Readings = new();

    /// <summary>The card on that board, read once for both snapshots.</summary>
    private static Reading Read(CardModel canon, Board board) => Readings.GetOrAdd((canon.GetType(), board), _ => Describe(canon, board));

    private static Reading Describe(CardModel canon, Board board)
    {
        try
        {
            var gameFight = new GameFight(teammates: board == Board.Teammate ? 1 : 0);
            foreach (var teammate in gameFight.Teammates)
            {
                teammate.Creature.SetMaxHpInternal(70);
                teammate.Creature.SetCurrentHpInternal(70);
                teammate.PlayerCombatState!.Energy = 3;
                gameFight.AddFor<StrikeIronclad>(teammate, PileType.Hand);
                gameFight.AddFor<DefendIronclad>(teammate, PileType.Hand);
                for (var i = 0; i < 3; i++) gameFight.AddFor<StrikeIronclad>(teammate, PileType.Draw);
                gameFight.AddFor<DefendIronclad>(teammate, PileType.Discard);
            }
            gameFight.Me.Creature.SetMaxHpInternal(80);
            gameFight.Me.Creature.SetCurrentHpInternal(80);
            gameFight.Me.PlayerCombatState!.Energy = 3;
            var enemies = new List<Creature> { Enemy(gameFight, hp: 200) };
            if (board == Board.Crowded) enemies.Add(Enemy(gameFight, hp: 30));
            var card = gameFight.Combat.CreateCard(canon, gameFight.Me);
            if (board == Board.Upgraded)
            {
                if (card.MaxUpgradeLevel < 1) return new Reading("(no upgrade)", "(no upgrade)");
                card.UpgradeInternal();
                card.FinalizeUpgradeInternal();
            }
            PileType.Hand.GetPile(gameFight.Me).AddInternal(card);
            if (board != Board.Alone)
            {
                gameFight.Add<StrikeIronclad>();
                gameFight.Add<DefendIronclad>();
                gameFight.Add<Dazed>();
            }
            if (board == Board.Crowded)
            {
                // A Sly card (Reflex: it plays itself when discarded) and a card that acts when exhausted (Drum of Battle).
                gameFight.Add<Reflex>();
                gameFight.Add<DrumOfBattle>();
                for (var i = 0; i < 4; i++) gameFight.Add<StrikeIronclad>();
            }
            else
                for (var i = 0; i < 3; i++)
                {
                    gameFight.Add<StrikeIronclad>(PileType.Draw);
                    gameFight.Add<DefendIronclad>(PileType.Draw);
                }
            gameFight.Add<StrikeIronclad>(PileType.Discard);
            gameFight.Add<StrikeIronclad>(PileType.Discard);
            gameFight.Add<DefendIronclad>(PileType.Discard);

            var start = SimAdapter.Build(gameFight.Me, gameFight.Combat, out var ids);
            var facts = PlayFacts.For(card, gameFight.Me, gameFight.Combat, plays: board == Board.Replayed ? 2 : 1);
            var targetId = card.TargetType switch
            {
                TargetType.AnyEnemy => ids[enemies[0]],
                TargetType.AnyAlly when gameFight.Teammates.Count > 0 => Who.Teammate(teammateIndex: 0),
                _ => Who.None,
            };
            var play = SimAdapter.Play(card, facts, gameFight.Me, gameFight.Combat, ids, targetId);
            return new Reading(FactsText(facts, enemies) + " play " + PlayText(play, gameFight), OutcomeText(start, play));
        }
        catch (Exception ex)
        {
            var error = "error " + ex.GetType().Name;
            return new Reading(error, error);
        }
    }

    /// <summary>
    /// The play's outcome: each branch it ends in, in the simulator's order, as "weight you=HP/block e(energy) h(hand) d(draw)/(discard)
    /// [teammates] enemies [the play's card events] ?(what isn't modelled)", and "capped" when the search stopped at a cap.
    /// </summary>
    private static string OutcomeText(SimState start, SimPlay play)
    {
        List<SimState> branches;
        bool capped;
        try { SimEngine.ApplyPlayTo(start, play, out branches, out capped); }
        catch (Exception ex) { return "sim error " + ex.GetType().Name; }
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        static string Player(SimPlayer player) =>
            $"{player.Hp}/{player.Block} e{player.Energy} h{player.HandCount}" + (player.Draw is { } piles ? $" d{piles.Count}/{piles.DiscardCount}" : "");
        var lines = branches.Select(branch =>
        {
            var parts = new List<string> { branch.Weight.ToString("0.####", culture), "you=" + Player(branch.You) };
            for (var i = 0; i < branch.Teammates.Count; i++) parts.Add($"mate{i}=" + Player(branch.Teammates[i]));
            parts.AddRange(branch.Enemies.Select(enemy => $"enemy{enemy.Id}={enemy.Hp}/{enemy.Block}"));
            var events = Enum.GetValues<CardEvent>().Where(cardEvent => branch.PlayEvents[cardEvent] > 0).Select(cardEvent => $"{cardEvent}{branch.PlayEvents[cardEvent]}");
            parts.Add("[" + string.Join(" ", events) + "]");
            if (branch.Unmodelled != null) parts.Add("?" + branch.Unmodelled);
            return string.Join(" ", parts);
        });
        return (capped ? "capped " : "") + string.Join(" | ", lines);
    }

    private static Creature Enemy(GameFight gameFight, int hp)
    {
        var enemy = gameFight.Combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, slot: null);
        gameFight.Combat.AddCreature(enemy);
        enemy.SetMaxHpInternal(hp);
        enemy.SetCurrentHpInternal(hp);
        return enemy;
    }

    /// <summary>What the proc check and the simulation's own results set (none of it is read from the card).</summary>
    private static readonly HashSet<string> SetLater = new()
    {
        nameof(PlayFacts.Card), nameof(PlayFacts.Me), nameof(PlayFacts.Combat), nameof(PlayFacts.Sim), nameof(PlayFacts.Drawn),
        nameof(PlayFacts.DeathByAim), nameof(PlayFacts.OutcomeByAim), nameof(PlayFacts.FrostBlockByAim), nameof(PlayFacts.FiresEveryByAim),
        nameof(PlayFacts.FiresAnyByAim), nameof(PlayFacts.Unmodelled), nameof(PlayFacts.SimPending), nameof(PlayFacts.SimIncomplete),
    };

    /// <summary>Every fact PlayFacts reads from the card and the board, by name, the ones at their default left out; then hits and damage per enemy.</summary>
    private static string FactsText(PlayFacts facts, List<Creature> enemies)
    {
        var parts = new List<string>();
        foreach (var property in typeof(PlayFacts).GetProperties(BindingFlags.Instance | BindingFlags.Public).OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            if (SetLater.Contains(property.Name)) continue;
            var value = ValueText(property.GetValue(facts), enemies);
            if (value is "0" or "False" or "" or null) continue;
            parts.Add(property.Name + "=" + value);
        }
        for (var i = 0; i < enemies.Count; i++)
        {
            var hits = facts.HitsOn(enemies[i]);
            if (hits > 0) parts.Add($"enemy{i}={hits}x{facts.DamagePerHit(enemies[i])}");
        }
        return "{" + string.Join(" ", parts) + "}";
    }

    private static string? ValueText(object? value, List<Creature> enemies) => value switch
    {
        null => null,
        Enum enumValue when Convert.ToInt32(enumValue) == 0 => null,
        Type type => type.Name,
        CardModel card => card.GetType().Name,
        IDictionary dictionary => string.Join(",", dictionary.Keys.Cast<object>()
            .Select(key => (key is Creature creature ? "enemy" + enemies.IndexOf(creature) : key.ToString()) + ":" + dictionary[key])),
        string text => text,
        IEnumerable items => string.Join(",", items.Cast<object?>().Select(item => ValueText(item, enemies))),
        _ => value.ToString(),
    };

    /// <summary>The simulator's JSON (SimJson), without the members at their default: shorter lines, the same differences.</summary>
    private static readonly JsonSerializerOptions PlayJson = new(SimJson.Options) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault };

    /// <summary>
    /// The play as JSON, each hand card's id (SimAdapter.HandId: the object's hash, different every run) written as where the card
    /// is ("Hand0" is the card played, "Draw2" the third card of the draw pile).
    /// </summary>
    private static string PlayText(SimPlay play, GameFight gameFight)
    {
        var places = new Dictionary<string, string>();
        foreach (var (player, prefix) in gameFight.Teammates.Select((teammate, index) => (teammate, "Teammate" + index)).Prepend((gameFight.Me, "")))
            foreach (var pile in new[] { PileType.Hand, PileType.Draw, PileType.Discard })
            {
                var cards = pile.GetPile(player).Cards;
                for (var i = 0; i < cards.Count; i++) places.TryAdd(SimAdapter.HandId(cards[i]).ToString(), "\"" + prefix + pile + i + "\"");
            }
        try
        {
            var json = JsonSerializer.Serialize(play, PlayJson);
            return System.Text.RegularExpressions.Regex.Replace(json, @"(?<=""(HandId|HandCard|Id)"":)\d+", match => places.GetValueOrDefault(match.Value, match.Value));
        }
        catch (Exception ex) { return "(not written: " + ex.GetType().Name + ")"; }
    }
}
