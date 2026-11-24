using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Conditions apart from effects (decision #190): each test a card's code makes is translated once into a simulator condition
/// (ConditionReader), read here from each branch's code alone (no board), so the cards that ask it are found by the test, not listed. The
/// cards below are the ones whose condition used to be fused into an effect (a kill reward on the attack, Timber's kill attack inside its
/// hit, a draw's count per play index) or decided before the first play of a replay (Osty after Sacrifice, Impatience's and Restlessness'
/// hand).
/// </summary>
public class ConditionTranslationTests
{
    public static TheoryData<string> Branches => new() { "beta", "main" };

    private static string PathOf(string branch) => branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;

    /// <summary>Card, the command whose step is guarded (its first such step after the first of <c>Skip</c>), and what it's guarded by.</summary>
    private static readonly (string Card, string Command, int Skip, string Condition, bool AskedFirst)[] Known =
    {
        ("Sunder", "PlayerCmd.GainEnergy", 0, "AttackKilled", false),
        ("KnockoutBlow", "PlayerCmd.GainStars", 0, "AttackKilled", false),
        ("Timber", "DamageCmd.Attack", 1, "AttackKilled", false),
        ("Impatience", "CardPileCmd.Draw", 0, "not HandMatching>0", false),
        ("KiHop", "CardPileCmd.Draw", 0, "TargetImpaled", true),
        ("BowSmack", "PlayerCmd.GainEnergy", 0, "TargetImpaled", true),
        ("Wrest", "CardPileCmd.Draw", 0, "TargetImpaledByOwner", false),
        ("Ftl", "CardPileCmd.Draw", 0, "PlaysFinishedThisTurn<", false),
        ("Fetch", "CardPileCmd.Draw", 0, "all(not OstyMissing, not CardPlaysFinished>0)", false),
        ("Restlessness", "CardPileCmd.Draw", 0, "not HandCards>0", false),
        ("Restlessness", "PlayerCmd.GainEnergy", 0, "not HandCards>0", false),
        ("WheelOfFortune", "CardCmd.DiscardAndDraw", 0, "not HandCardsLessOrEqual", false),
        ("WheelOfFortune", "CardPileCmd.Draw", 0, "HandCardsLessOrEqual", false),
        ("Sacrifice", "CreatureCmd.Kill", 0, "not OstyMissing", false),
        ("Sacrifice", "CreatureCmd.GainBlock", 0, "not OstyMissing", false),
        ("BoneShards", "CreatureCmd.Kill", 0, "all(not OstyMissing, not OstyMissing)", false),
        ("Fetch", "DamageCmd.Attack", 0, "not OstyMissing", false),
        // The fight's log (decision #191): an exhaust this turn, a Cho played this turn (MelodyCmd's helper, its tag bound from the caller).
        ("ForgottenRitual", "PlayerCmd.GainEnergy", 0, "History>0", false),
        ("KiRest", "PowerCmd.Apply", 1, "any(History>0, History>0)", false),
        ("ArrowStab", "CardPileCmd.Add", 0, "all(ArrowsAvailable>0, HandCards<)", false),
        // The board the simulator keeps: the target's powers and intent, your exhaust pile.
        ("BubbleBubble", "PowerCmd.Apply", 0, "TargetPower>0", false),
        ("GoForTheEyes", "PowerCmd.Apply", 0, "TargetIntendsToAttackCondition", false),
        ("MoltenFist", "PowerCmd.Apply", 0, "not TargetPowerLessOrEqual", false),
        ("PactsEnd", "DamageCmd.Attack", 0, "ExhaustPileGreaterOrEqual", false),
    };

    /// <summary>Cards whose step main runs on every play (main's Forgotten Ritual gains its energy with no test).</summary>
    private static readonly HashSet<string> OnlyBetaTests = new() { "ForgottenRitual" };

    /// <summary>A condition as short text: its test, a count's comparison with its number left out (the board gives it).</summary>
    private static string Text(SimCondition condition) => condition switch
    {
        AttackKilledCondition => "AttackKilled",
        OstyMissingCondition => "OstyMissing",
        TargetImpaledCondition { ByOwner: true } => "TargetImpaledByOwner",
        TargetImpaledCondition => "TargetImpaled",
        CountCondition counted => counted.Counted + counted.Compare switch
        {
            SimCompare.Greater => ">0", SimCompare.Less => "<", SimCompare.Equal => "==", _ => counted.Compare.ToString(),
        },
        NotCondition not => "not " + Text(not.Inner),
        AllCondition all => "all(" + string.Join(", ", all.Parts.Select(Text)) + ")",
        AnyCondition any => "any(" + string.Join(", ", any.Parts.Select(Text)) + ")",
        _ => condition.GetType().Name,
    };

    [Theory]
    [MemberData(nameof(Branches))]
    public void EachKnownStepIsGuardedByItsCodesTest(string branch)
    {
        if (!File.Exists(PathOf(branch))) return; // no decompile of this branch here
        using var code = new BranchCode(PathOf(branch));
        var cards = code.Cards().ToDictionary(type => type.Name);
        var checkedCards = 0;
        foreach (var (card, command, skip, expected, askedFirst) in Known)
        {
            if (!cards.TryGetValue(card, out var type)) continue; // a card only the other branch has
            if (branch == "main" && OnlyBetaTests.Contains(card)) continue;
            var program = ProgramReader.Read(type, code.Read);
            var step = CardEffects.Find(program, candidate => candidate.Command.FullName == command)[skip];
            var reading = ConditionReader.Read(step.Condition, new ConditionReader.Context(type, code.Read, Board: null, program));
            Assert.True(reading.Way == ConditionWay.Simulated, $"{card} {command}: {reading.Way} ({reading.Reason})");
            Assert.Equal(expected, Text(reading.Condition!));
            Assert.Equal(askedFirst, reading.AskFirst.Count > 0);
            checkedCards++;
        }
        Assert.True(checkedCards > 0);
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void ATestTheCodeAsksOnceIsTheSameTestEverywhereItGuards(string branch)
    {
        // Restlessness' draws and energy are under one if: one test (one At), asked once a play, so the energy's isn't asked after the draws
        // filled your hand. Fetch's attack and draw share the Osty test the same way.
        if (!File.Exists(PathOf(branch))) return;
        using var code = new BranchCode(PathOf(branch));
        foreach (var card in new[] { "Restlessness", "Fetch" })
        {
            if (code.Cards().FirstOrDefault(type => type.Name == card) is not { } type) continue;
            var program = ProgramReader.Read(type, code.Read);
            var places = CardEffects.Find(program, candidate => candidate.Command.FullName is "CardPileCmd.Draw" or "PlayerCmd.GainEnergy" or "DamageCmd.Attack")
                .Select(found => ConditionReader.Read(found.Condition, new ConditionReader.Context(type, code.Read, Board: null, program)).Condition!)
                .Select(condition => SimEngine.ConditionParts(condition).Select(part => part.At).First(at => at != null))
                .Distinct().ToList();
            Assert.Single(places);
        }
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void TheCardsOwnValuesAndPicksAreDecidedBeforeThePlay(string branch)
    {
        // Spinner's channel on its upgrade, Mad Science's rider, Acrobatics' discard of its pick (decision #188): nothing in the play changes
        // them, so they're decided on the board, never asked by the simulator.
        if (!File.Exists(PathOf(branch))) return;
        using var code = new BranchCode(PathOf(branch));
        foreach (var (card, command) in new[] { ("Spinner", "OrbCmd.Channel"), ("MadScience", "CreatureCmd.GainBlock"), ("Acrobatics", "CardCmd.Discard") })
        {
            if (code.Cards().FirstOrDefault(type => type.Name == card) is not { } type) continue;
            var program = ProgramReader.Read(type, code.Read);
            var step = CardEffects.Find(program, candidate => candidate.Command.FullName == command)[0];
            var reading = ConditionReader.Read(step.Condition, new ConditionReader.Context(type, code.Read, Board: null, program));
            Assert.True(reading.Way == ConditionWay.Decided, $"{card} {command}: {reading.Way} ({reading.Reason})");
        }
    }
}
