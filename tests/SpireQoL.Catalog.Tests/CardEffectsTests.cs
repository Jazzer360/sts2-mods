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
        var programs = code.Cards().ToDictionary(type => type.Name, type => ProgramReader.Read(type, code.Read));
        void Finds(Func<ModelProgram, bool> rule, params string[] cards)
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
        var programs = code.Cards().ToDictionary(type => type.Name, type => ProgramReader.Read(type, code.Read));
        var failures = new List<string>();
        void Exactly(string rule, Func<ModelProgram, bool> finds, params string[] table)
        {
            var found = programs.Where(entry => finds(entry.Value)).Select(entry => entry.Key).OrderBy(name => name, StringComparer.Ordinal).ToList();
            var expected = table.Where(programs.ContainsKey).OrderBy(name => name, StringComparer.Ordinal).ToList();
            if (!found.SequenceEqual(expected)) failures.Add($"{rule} ({branch}): found {string.Join(", ", found)}; the table: {string.Join(", ", expected)}");
        }
        // The first eight rules (KillsOstyCards, ExecuteCards, ...).
        Exactly(nameof(CardEffects.KillsOsty), CardEffects.KillsOsty, "Sacrifice", "BoneShards");
        Exactly(nameof(CardEffects.SetsTargetsHp), CardEffects.SetsTargetsHp, "Judgement");
        Exactly(nameof(CardEffects.TakesTargetsBlock), CardEffects.TakesTargetsBlock, "Expose");
        // Also Omnislice's and Rain Shot's first hit (CreatureCmd.Damage on the target): attacks, which the adapter and the scan leave
        // out (only a card that isn't an attack is asked), as ExecuteCards' table did.
        Exactly(nameof(CardEffects.DamagesTargetWithoutAttacking), CardEffects.DamagesTargetWithoutAttacking, "CaptureSpirit", "Omnislice", "RainShot");
        Exactly(nameof(CardEffects.BlocksAnother), CardEffects.BlocksAnother, "Lift", "Constellation", "DemonicShield");
        Exactly(nameof(CardEffects.BlocksEachPlayer), CardEffects.BlocksEachPlayer, "Rally");
        Exactly(nameof(CardEffects.ChannelsForTarget), CardEffects.ChannelsForTarget, "Ignition");
        Exactly(nameof(CardEffects.DrawsForTarget), CardEffects.DrawsForTarget, Tables.AllyDrawCards);
        Exactly(nameof(CardEffects.DrawsForEachTeammate), CardEffects.DrawsForEachTeammate, Tables.TeammatesDrawCards);
        Exactly(nameof(CardEffects.ShufflesDiscardPileIn), CardEffects.ShufflesDiscardPileIn, Tables.ShuffleCards);
        Exactly(nameof(CardEffects.ShufflesWhenShort), CardEffects.ShufflesWhenShort, Tables.ShuffleWhenShortCards);
        Exactly(nameof(CardEffects.TakesIntoHand), CardEffects.TakesIntoHand, Tables.TakenToHandCards);
        Exactly(nameof(CardEffects.PutsBackFromHand), CardEffects.PutsBackFromHand, Tables.PutBackCards);
        Exactly(nameof(CardEffects.MovesCardsElsewhere), CardEffects.MovesCardsElsewhere, Tables.MovedElsewhereCards);
        Exactly(nameof(CardEffects.MakesSadness), CardEffects.MakesSadness, Tables.SadnessCards);
        Exactly(nameof(CardEffects.Forges), CardEffects.Forges, Tables.ForgeCards);
        Exactly(nameof(CardEffects.HitsXTimes), CardEffects.HitsXTimes, Tables.XHitCards);
        Exactly(nameof(CardEffects.AttacksOnceRepeatingSomethingElse), CardEffects.AttacksOnceRepeatingSomethingElse, Tables.SingleHitRepeatCards);
        Exactly(nameof(CardEffects.AutoPlaysFromDrawPile), CardEffects.AutoPlaysFromDrawPile, Tables.PlaysFromDrawPile);
        Exactly(nameof(CardEffects.SummonsEachPlayer), CardEffects.SummonsEachPlayer, Tables.AllPlayersSummonCards);
        Exactly(nameof(CardEffects.DislodgesArrows), CardEffects.DislodgesArrows, Tables.DislodgeCards);
        Exactly(nameof(CardEffects.KeepsStretchForReplays), CardEffects.KeepsStretchForReplays, Tables.KeepsStretchCards);
        Exactly(nameof(CardEffects.TakesWholeHand), CardEffects.TakesWholeHand, Tables.WholeHandCards);
        // Other cards exhausted (BoardCounts.ExhaustOthers' cases), and how each takes them (ExhaustPickOf, ExhaustTakesNoActingCard).
        Exactly(nameof(CardEffects.ExhaustsOthers), CardEffects.ExhaustsOthers, Tables.ExhaustOthersCards);
        foreach (var (card, ways) in Tables.ExhaustWays)
        {
            if (!programs.TryGetValue(card, out var program)) continue;
            var read = string.Join(", ", CardEffects.ExhaustsOfOthers(program).Select(exhaust =>
                exhaust.Way + (exhaust.FromDrawPile ? " from the draw pile" : "") + (exhaust.Filter != null || !exhaust.ItemTest.IsAlways ? " filtered" : "")));
            if (read != ways) failures.Add($"{card} ({branch}) exhausts: {read}; the table: {ways}");
        }
        // Cards discarded, transformed and moved into your hand (BoardCounts.DiscardCount's, TransformCount's and TakenToHandCount's cases,
        // SimAdapter.HandTakes), and how each takes them.
        Exactly(nameof(CardEffects.Discards), program => CardEffects.Discards(program).Count > 0, Tables.DiscardCards.Keys.ToArray());
        Exactly(nameof(CardEffects.Transforms), program => CardEffects.Transforms(program).Count > 0, Tables.TransformCards.Keys.ToArray());
        Exactly(nameof(CardEffects.TakesIntoHandOf), program => CardEffects.TakesIntoHandOf(program).Count > 0, Tables.HandTakes.Keys.ToArray());
        void Ways(string rule, Func<ModelProgram, IReadOnlyList<CardEffects.CardsTaken>> takes, Dictionary<string, string> table)
        {
            foreach (var (card, ways) in table)
            {
                if (!programs.TryGetValue(card, out var program)) continue;
                var read = string.Join(", ", takes(program).Select(Described));
                if (read != ways) failures.Add($"{card} ({branch}) {rule}: {read}; the table: {ways}");
            }
        }
        Ways("discards", CardEffects.Discards, Tables.DiscardCards);
        Ways("transforms", CardEffects.Transforms, Tables.TransformCards);
        Ways("takes into hand", CardEffects.TakesIntoHandOf, Tables.HandTakes);
        // Orbs (SimAdapter.Channels, RepeatChannels, SlotChange, the evoke cards in OrbsOf and EvokedOrbs; BoardCounts.ChannelCount):
        // each channel's orb, in order, "each round" inside a loop.
        Exactly(nameof(CardEffects.OrbChannels), program => CardEffects.OrbChannels(program).Count > 0, Tables.ChannelCards.Keys.ToArray());
        foreach (var (card, orbs) in Tables.ChannelCards)
        {
            if (!programs.TryGetValue(card, out var program)) continue;
            var read = string.Join(", ", CardEffects.OrbChannels(program).Select(channel =>
                (CardEffects.OrbOf(channel.Command)?.ToString() ?? "?") + (channel.Loops.Count > 0 ? " each round" : "")));
            if (read != orbs) failures.Add($"{card} ({branch}) channels: {read}; the table: {orbs}");
        }
        Exactly(nameof(CardEffects.OrbSlotChanges), program => CardEffects.OrbSlotChanges(program).Count > 0, "BulkUp", "Capacitor", "Modded", "TheMagician");
        Exactly(nameof(CardEffects.OrbEvokes), program => CardEffects.OrbEvokes(program).Count > 0, "Dualcast", "Quadcast", "MultiCast", "Shatter");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>How a take reads: its way, its pile, one or all, a filter, a choice of how many.</summary>
    private static string Described(CardEffects.CardsTaken take)
    {
        var parts = new List<string> { take.Way.ToString() };
        var piles = take.Way == CardEffects.TakeWay.Picked
            ? take.PickPile is { } pile ? new[] { pile.ToString() } : Array.Empty<string>()
            : take.Items?.Parts().OfType<QueryValue>().Where(value => value is { Name: "GetPile", Arguments: [ConstantValue, ..] })
                .Select(value => ((MegaCrit.Sts2.Core.Entities.Cards.PileType)((ConstantValue)value.Arguments[0]).Value).ToString()).Distinct().ToArray() ?? Array.Empty<string>();
        if (piles.Length > 0) parts.Add("from " + string.Join("+", piles));
        if (take.Single) parts.Add("one");
        if (take.Way == CardEffects.TakeWay.Picked && take.PerPick is not ConstantValue { Value: 1 }) parts.Add("several");
        if (take.Filter != null || take.PickFilter != null || !take.ItemTest.IsAlways) parts.Add("filtered");
        if (take.ChoosesHowMany) parts.Add("how many chosen");
        return string.Join(" ", parts);
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
        // Attacks (PlayFacts.XHitCards, SingleHitRepeatCards).
        public static readonly string[] XHitCards = { "Eradicate", "Skewer", "Volley", "Whirlwind", "RepeatShot" };
        public static readonly string[] SingleHitRepeatCards = { "IceLance", "HeirloomHammer" };
        // The rest (SimAdapter.PlaysFromDrawPile, AllPlayersSummonCards, KeepsStretchCards; PlayFacts.KillStarCards; Fool.DislodgeCards).
        public static readonly string[] PlaysFromDrawPile = { "Havoc", "Cascade", "TheDevil" };
        public static readonly string[] AllPlayersSummonCards = { "LegionOfBone" };
        public static readonly string[] KillStarCards = { "KnockoutBlow" };
        public static readonly string[] DislodgeCards = { "Recall", "Uproot", "Wrest" };
        public static readonly string[] KeepsStretchCards = { "ChoKi" };
        // The whole hand (PlayFacts.WholeHandCards).
        public static readonly string[] WholeHandCards = { "FiendFire", "Stoke", "TheDevil", "CalculatedGamble", "ShadowStep", "StormOfSteel", "WheelOfFortune", "Reboot" };
        // Other cards exhausted (BoardCounts.ExhaustOthers), and how (ExhaustPickOf: Chosen, Random, All; ExhaustTakesNoActingCard: a filter).
        public static readonly string[] ExhaustOthersCards =
        {
            "Brand", "BurningPact", "TrueGrit", "Scavenge", "Cinder", "Thrash", "Cleanse", "FiendFire", "Stoke", "SecondWind", "TheDevil", "Growth",
            "Florescence", "Purity", "TheHermit", "FlakCannon",
        };
        public static readonly Dictionary<string, string> ExhaustWays = new()
        {
            ["Cinder"] = "Random", ["Thrash"] = "Random filtered",
            // Upgraded: a pick (CardSelectCmd); else a roll.
            ["TrueGrit"] = "Picked, Random",
            ["Brand"] = "Picked", ["BurningPact"] = "Picked", ["Scavenge"] = "Picked", ["Purity"] = "Picked",
            ["Cleanse"] = "Picked from the draw pile",
            ["FiendFire"] = "Each", ["Stoke"] = "Each", ["TheDevil"] = "Each", ["SecondWind"] = "Each filtered",
            // ExhaustTakesNoActingCard: only Sadness, only Shoot cards, only status cards, only a Curse.
            ["Growth"] = "Each filtered", ["Florescence"] = "Each filtered", ["FlakCannon"] = "Each filtered", ["TheHermit"] = "Picked filtered",
        };
        // Draws on a condition (PlayFacts.ConditionalDrawCards, TargetDrawCards).
        public static readonly string[] ConditionalDrawCards = { "Impatience", "Fetch", "Ftl", "Restlessness", "KiHop", "Wrest" };
        public static readonly string[] TargetDrawCards = { "KiHop", "Wrest" };
        // Made cards (PlayFacts.SadnessCards, ForgeCards).
        public static readonly string[] SadnessCards = { "BrightenUp", "Jest", "MourningBlow", "Rummage", "SelfDeprecate", "SorrowfulStrike", "Vitriol" };
        public static readonly string[] ForgeCards =
        {
            "BeatIntoShape", "BigBang", "Bulwark", "Conqueror", "RefineBlade", "SeekingEdge", "SpoilsOfBattle", "SummonForth", "TheSmith", "WroughtInWar",
        };
        // Channels (SimAdapter.Channels: the kinds in order, Glacier two Frost, Meteor Strike three Plasma, Chill one Frost per enemy;
        // RepeatChannels: one kind its Repeat times, Chaos and The Magician random; Tempest's and Voltaic's Lightning).
        public static readonly Dictionary<string, string> ChannelCards = new()
        {
            ["BallLightning"] = "Lightning", ["Zap"] = "Lightning", ["Chill"] = "Frost each round", ["ColdSnap"] = "Frost", ["Coolheaded"] = "Frost",
            ["Glacier"] = "Frost each round", ["Fusion"] = "Plasma", ["Ignition"] = "Plasma", ["MeteorStrike"] = "Plasma each round",
            ["Glasswork"] = "Glass", ["Spinner"] = "Glass", ["Null"] = "Dark", ["ShadowShield"] = "Dark", ["Darkness"] = "Dark",
            ["Rainbow"] = "Lightning, Frost, Dark", ["ConsumingShadow"] = "Dark each round", ["Hibernate"] = "Frost each round",
            ["IceLance"] = "Frost each round", ["Refract"] = "Glass each round", ["Chaos"] = "Random each round", ["TheMagician"] = "Random each round",
            ["Tempest"] = "Lightning each round", ["Voltaic"] = "Lightning each round",
        };
        // Discards (BoardCounts.DiscardCount), with how each takes them: a pick from your hand (one, or its Cards), your whole hand, the
        // cards it drew (Scrape), the ones looked at on top of your draw pile (Ki Chant).
        public static readonly Dictionary<string, string> DiscardCards = new()
        {
            ["Acrobatics"] = "Picked from Hand", ["DaggerThrow"] = "Picked from Hand", ["Prepared"] = "Picked from Hand several", ["Survivor"] = "Picked from Hand",
            ["HiddenDaggers"] = "Picked from Hand several",
            ["CalculatedGamble"] = "Each from Hand", ["ShadowStep"] = "Each from Hand", ["StormOfSteel"] = "Each from Hand", ["WheelOfFortune"] = "Each from Hand",
            // The Hermit: the pick is discarded when it isn't a Curse; Ki Chant: what it looked at, less the card it took.
            ["TheHermit"] = "Picked from Hand filtered", ["KiChant"] = "LookedAt from Draw filtered", ["Scrape"] = "Drawn filtered",
        };
        // Transforms (BoardCounts.TransformCount).
        public static readonly Dictionary<string, string> TransformCards = new()
        {
            ["Begone"] = "Picked from Hand", ["Guards"] = "Picked from Hand several how many chosen", ["Charge"] = "Picked from Draw several",
            ["Seance"] = "Picked from Draw several", ["Compact"] = "Each from Hand filtered", ["PrimalForce"] = "Each from Hand filtered", ["PunchOfJoy"] = "Each from Hand one",
        };
        // Moved into your hand (BoardCounts.TakenToHandCount, SimAdapter.HandTakes: FromDiscard, Eligible, Random, AllAtOnce).
        public static readonly Dictionary<string, string> HandTakes = new()
        {
            ["Anointed"] = "Random from Draw filtered", ["Wish"] = "Picked from Draw", ["SeekerStrike"] = "Picked from Draw filtered", ["KiChant"] = "Picked from Draw filtered",
            ["SecretWeapon"] = "Picked from Draw filtered", ["SecretTechnique"] = "Picked from Draw filtered",
            ["LockedIn"] = "Picked from Draw filtered", ["AllForOne"] = "Each from Discard filtered", ["ArrowStab"] = "Picked from Discard filtered",
            ["Dredge"] = "Picked from Discard several", ["TheWorld"] = "Random from Discard", ["Graveblast"] = "Picked from Discard",
            // Summon Forth: every Sovereign Blade not in your hand, from every pile; Neow's Fury: you choose how many.
            ["Hologram"] = "Picked from Discard", ["SummonForth"] = "Each filtered", ["NeowsFury"] = "Picked from Discard several how many chosen",
        };
    }
}
