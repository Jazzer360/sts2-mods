using SpireQoL.Procs.Programs;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// What a card does, read from its program (CardEffects, decision #182), on both game branches: each rule finds the cards it was
/// written from, so a change in how the game's code reads shows up here. A new card the rule finds is simulated the same way,
/// without a list.
/// </summary>
public class CardEffectsTests
{
    public static TheoryData<string> Branches => new() { "beta", "main" };

    [Theory]
    [MemberData(nameof(Branches))]
    public void EachRuleFindsTheCardsItWasWrittenFrom(string branch)
    {
        var path = branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;
        if (!File.Exists(path)) return;
        using var code = new BranchCode(path);
        var programs = code.Cards().ToDictionary(type => type.Name, type => CardProgramReader.Read(type, code.Read));
        void Finds(Func<CardProgram, bool> rule, params string[] cards)
        {
            foreach (var card in cards)
                if (programs.TryGetValue(card, out var program)) Assert.True(rule(program), $"{card} ({branch}): {program}");
            // A card the rule shouldn't find.
            Assert.False(rule(programs["StrikeIronclad"]));
        }
        Finds(CardEffects.KillsOsty, "Sacrifice", "BoneShards");
        Finds(CardEffects.SetsTargetsHp, "Judgement");
        Finds(CardEffects.TakesTargetsBlock, "Expose");
        Finds(CardEffects.DamagesTargetWithoutAttacking, "CaptureSpirit");
        Finds(CardEffects.BlocksAnother, "Lift", "Constellation", "DemonicShield");
        Finds(CardEffects.BlocksEachPlayer, "Rally");
        Finds(CardEffects.ChannelsForTarget, "Ignition");
        Finds(CardEffects.HitsAllOnKill, "Timber");
        // Your own block is yours: Iron Wave blocks you, not another.
        Assert.False(CardEffects.BlocksAnother(programs["IronWave"]));
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void EachRuleFindsExactlyTheTableItReplaced(string branch)
    {
        // Each rule gives exactly the cards of the per-card table it replaced on both branches (none more, none fewer): the cards
        // below are that table, as it was when it was deleted. A card a game update adds or changes is read by the rule, never listed.
        var path = branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;
        if (!File.Exists(path)) return;
        using var code = new BranchCode(path);
        var programs = code.Cards().ToDictionary(type => type.Name, type => CardProgramReader.Read(type, code.Read));
        var failures = new List<string>();
        void Exactly(string rule, Func<CardProgram, bool> finds, params string[] table)
        {
            var found = programs.Where(entry => finds(entry.Value)).Select(entry => entry.Key).OrderBy(name => name, StringComparer.Ordinal).ToList();
            var expected = table.Where(programs.ContainsKey).OrderBy(name => name, StringComparer.Ordinal).ToList();
            if (!found.SequenceEqual(expected)) failures.Add($"{rule} ({branch}): found {string.Join(", ", found)}; the table: {string.Join(", ", expected)}");
        }
        Exactly(nameof(CardEffects.DrawsForTarget), CardEffects.DrawsForTarget, Tables.AllyDrawCards);
        Exactly(nameof(CardEffects.DrawsForEachTeammate), CardEffects.DrawsForEachTeammate, Tables.TeammatesDrawCards);
        Exactly(nameof(CardEffects.ShufflesDiscardPileIn), CardEffects.ShufflesDiscardPileIn, Tables.ShuffleCards);
        Exactly(nameof(CardEffects.ShufflesWhenShort), CardEffects.ShufflesWhenShort, Tables.ShuffleWhenShortCards);
        Exactly(nameof(CardEffects.TakesIntoHand), CardEffects.TakesIntoHand, Tables.TakenToHandCards);
        Exactly(nameof(CardEffects.PutsBackFromHand), CardEffects.PutsBackFromHand, Tables.PutBackCards);
        Exactly(nameof(CardEffects.MovesCardsElsewhere), CardEffects.MovesCardsElsewhere, Tables.MovedElsewhereCards);
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>The per-card tables the rules replaced, as they were when they were deleted.</summary>
    private static class Tables
    {
        // Draws and shuffles (PlayFacts.AllyDrawCards, SimAdapter.TeammatesDrawCards, PlayFacts.ShuffleCards and ShuffleWhenShortCards).
        public static readonly string[] AllyDrawCards = { "Constellation" };
        public static readonly string[] TeammatesDrawCards = { "HuddleUp" };
        public static readonly string[] ShuffleCards = { "Reboot" };
        public static readonly string[] ShuffleWhenShortCards = { "KiChant" };
        // Cards moved between piles (PlayFacts.TakenToHandCards, PutBackCards, MovedElsewhereCards).
        public static readonly string[] TakenToHandCards =
        {
            "AllForOne", "Anointed", "ArrowStab", "Dredge", "Graveblast", "Hologram", "KiChant", "LockedIn", "NeowsFury", "SecretTechnique",
            "SecretWeapon", "SeekerStrike", "SummonForth", "TheWorld", "Wish",
        };
        public static readonly string[] PutBackCards = { "Glimmer", "PhotonCut", "ThinkingAhead" };
        public static readonly string[] MovedElsewhereCards = { "CosmicIndifference", "Headbutt", "Recall", "Tutor" };
    }
}
