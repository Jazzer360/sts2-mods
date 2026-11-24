using System.Text.RegularExpressions;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The replay rule (decision #189): the adapter describes one play of a card, and the simulator's loop repeats it, each play reading the
/// board the one before it left. A count worked out once before the play and multiplied or shared out by the play count is the shortcut
/// that rule replaced (a replayed Fiend Fire hit for a hand it no longer had), so PlayFacts and the adapter never multiply or divide by
/// the play count. And the room in your hand is one game rule (CardPileCmd.Add, CardPileCmd.Draw), held in one place in the engine
/// (SimEngine.RoomIn); anywhere else that compares with the hand's most is a reviewed exception below.
/// </summary>
public class PerPlayRuleTests
{
    private static readonly Regex TimesPlays = new(@"[*/]\s*(\(?\s*Math\.Max\(\s*1\s*,\s*)?(\w+\.)?[Pp]lays\b|\b(\w+\.)?[Pp]lays\s*\)?\s*[*/](?!/)", RegexOptions.Compiled);

    private static readonly Regex HandSize = new(@"\bMaxHand\b|\bMaxCardsInHand\b", RegexOptions.Compiled);

    /// <summary>The files the replay rule holds: what the adapter builds a play from, and the adapter.</summary>
    private static IEnumerable<(string Path, string[] Lines)> AdapterSources() =>
        RepoFiles.Sources("mods/SpireQoL/Sim/Adapter").Concat(RepoFiles.Sources("mods/SpireQoL/Procs").Where(file => file.Path.EndsWith("PlayFacts.cs", StringComparison.Ordinal)));

    private static bool IsComment(string line) => line.TrimStart().StartsWith("//", StringComparison.Ordinal);

    [Fact]
    public void NoCountIsMultipliedOrSharedOutByThePlayCount()
    {
        var found = AdapterSources().SelectMany(file => file.Lines.Select((line, index) => (file.Path, Line: index + 1, Text: line)))
            .Where(line => !IsComment(line.Text) && TimesPlays.IsMatch(line.Text))
            .Select(line => $"{line.Path}:{line.Line}: {line.Text.Trim()}").ToList();
        Assert.True(found.Count == 0, "a count multiplied or shared out by the play count (decision #189: the simulator repeats one play):\n" + string.Join("\n", found));
    }

    /// <summary>Where the hand's most may be compared outside SimEngine.RoomIn, file and what, each with why.</summary>
    private static readonly Dictionary<string, string> RoomReviewed = new()
    {
        ["mods/SpireQoL/Sim/SimEngine.Draws.cs: public const int MaxHand = 10;"] = "the constant itself (CardPile.MaxCardsInHand)",
        ["mods/SpireQoL/Sim/SimEngine.Draws.cs: internal static int RoomIn(SimPlayer player) => Math.Max(0, MaxHand - (player.HandCount - player.PlayingFromHand));"] = "the one rule",
        ["mods/SpireQoL/Sim/SimEngine.Plays.cs: player.HandCount = Math.Clamp(player.HandCount + handDelta, 0, player.Draw != null ? MaxHand + player.PlayingFromHand : int.MaxValue);"] =
            "the card leaving and the cards its own hooks bring back after its plays (HandDelta: Make It So, Whistling Arrows): a tracker item to make them adds",
        ["mods/SpireQoL/Sim/PlayChain.cs: public const int MaxHand = 10; // CardPile.MaxCardsInHand"] = "PlayChain: the facts' draw count for the play check, the Shuffle Tag and the No Draw tag, not the simulation: a tracker item to read them from the simulation",
        ["mods/SpireQoL/Sim/PlayChain.cs: room = MaxHand;"] = "PlayChain (above)",
        ["mods/SpireQoL/Sim/PlayChain.cs: room = Math.Max(0, MaxHand - (drawInputs.HandCount - 1 - drawInputs.ExhaustedFromHand - drawInputs.DiscardedBeforeDraw) - drawInputs.AddedToHandDuringPlay);"] = "PlayChain (above)",
        ["mods/SpireQoL/Procs/PlayFacts.cs: ? Math.Max(draws, CardPile.MaxCardsInHand - (hand.Count - 1)) + swiftDraws : 0,"] = "the play check's bound on Pillage's draws (DrawsAtMost), not the simulation",
        ["mods/SpireQoL/Procs/PlayFacts.cs: CardsBack = ReturnToHand.Returning(card, me, plays, Math.Min(CardPile.MaxCardsInHand, hand.Count - 1 + createdInHand) + draws + swiftDraws,"] =
            "cards' own hooks after the play (ReturnToHand, docs/CARD_PROGRAMS.md A5): a tracker item with HandDelta's",
        ["mods/SpireQoL/Procs/PlayFacts.cs: if (chosen.Count == 0 || handCount - 1 >= CardPile.MaxCardsInHand) return false;"] = "whether Neow's Fury's how-many is a choice at all (its NotModelledAction)",
        ["mods/SpireQoL/Procs/PlayFacts.cs: var room = Math.Max(0, CardPile.MaxCardsInHand - others.Count);"] = "GeneratedCount's first-play count for the facts; the simulator reads the room as it runs (SimCount.HandRoom)",
        ["mods/SpireQoL/Procs/PlayFacts.cs: case nameof(TheLovers): from = SpireQoL.Sim.SimCount.HandRoom; return Math.Max(0, CardPile.MaxCardsInHand - others.Count);"] = "GeneratedCount (above)",
        ["mods/SpireQoL/Procs/PlayFacts.cs: case nameof(TheMoon): from = SpireQoL.Sim.SimCount.HandRoom; return Math.Max(0, CardPile.MaxCardsInHand - others.Count);"] = "GeneratedCount (above)",
        ["mods/SpireQoL/Procs/Programs/CardEffects.cs: value != null && value.Parts().Any(part => part is QueryValue { Name: \"MaxCardsInHand\" } or NamedValue { Name: \"MaxCardsInHand\" });"] =
            "reads the game's own name in a card's code (a count of your hand's room: SimCount.HandRoom), compares nothing",
        ["mods/SpireQoL/Sim/Adapter/SimAdapter.Play.cs: var drawn = Math.Min(cards, Math.Min(Math.Max(0, CardPile.MaxCardsInHand - hand), drawPile + discard));"] =
            "a teammate's draws (Huddle Up): their piles aren't read into the simulator, so their count is the board's: a tracker item",
    };

    [Fact]
    public void TheHandsRoomIsOneRuleInTheEngine()
    {
        var sources = RepoFiles.Sources("mods/SpireQoL").Concat(RepoFiles.Sources("core"));
        var found = sources.SelectMany(file => file.Lines.Where(line => !IsComment(line) && HandSize.IsMatch(line) && !line.TrimStart().StartsWith("///", StringComparison.Ordinal))
                .Select(line => $"{file.Path}: {line.Trim()}"))
            .ToList();
        var unreviewed = found.Where(entry => !RoomReviewed.ContainsKey(entry.Replace('\\', '/'))).ToList();
        Assert.True(unreviewed.Count == 0, "the hand's most compared outside SimEngine.RoomIn (decision #189): use RoomIn, or review it here:\n" + string.Join("\n", unreviewed));
        var stale = RoomReviewed.Keys.Where(key => !found.Select(entry => entry.Replace('\\', '/')).Contains(key)).ToList();
        Assert.True(stale.Count == 0, "reviewed, but no longer there:\n" + string.Join("\n", stale));
    }
}
