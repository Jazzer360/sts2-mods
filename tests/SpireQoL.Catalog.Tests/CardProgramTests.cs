using System.Reflection;
using SpireQoL.Procs.Programs;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Card programs (decision #182): a card's play read from its code, its commands in the order it calls them. Read on both game
/// branches' assemblies (each decompile's assembly/sts2.dll), the way the mod reads the loaded game: beta's from the installed game
/// gives the same programs.
/// </summary>
public class CardProgramTests
{
    public static TheoryData<string> Branches => new() { "beta", "main" };

    private static string PathOf(string branch) => branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;

    private static readonly Dictionary<string, string> Known = new()
    {
        // Block, then the attack: Thorns' answer hits the block.
        ["IronWave"] = "IronWave: CreatureCmd.GainBlock, DamageCmd.Attack",
        // Exhausts the hand one card at a time (a loop over your hand's cards), then attacks once per card.
        ["FiendFire"] = "FiendFire: loop over each of Hand.Cards.ToList [CardCmd.Exhaust], DamageCmd.Attack",
        // Energy only when a hit killed (a condition on the attack's results).
        ["Sunder"] = "Sunder: DamageCmd.Attack, PlayerCmd.GainEnergy if Any",
        // Exhaust, then block, card by card.
        ["SecondWind"] = "SecondWind: loop over each of GetCards.ToList [CardCmd.Exhaust, CreatureCmd.GainBlock]",
        // Draws, then the pick, then the discard of what was picked.
        ["Acrobatics"] = "Acrobatics: CardPileCmd.Draw, choice FromHandForDiscard, CardCmd.Discard if FirstOrDefault",
        // Draws before its No Draw.
        ["BattleTrance"] = "BattleTrance: CardPileCmd.Draw, PowerCmd.Apply<NoDrawPower>",
        // A do-while that draws while it draws Attacks.
        ["Pillage"] = "Pillage: DamageCmd.Attack, loop over ? [CardPileCmd.Draw]",
        // The attack runs inside the shot (a local function the shot is given).
        ["BlossomShot"] = "BlossomShot: ArrowCmd.ShootArrowAtTarget [DamageCmd.Attack, PowerCmd.Apply<StretchPower>] if not IsDead",
        ["Bash"] = "Bash: DamageCmd.Attack, PowerCmd.Apply<VulnerablePower>",
    };

    [Theory]
    [MemberData(nameof(Branches))]
    public void KnownCardsReadAsTheirCodeRuns(string branch)
    {
        if (!File.Exists(PathOf(branch))) return; // no decompile of this branch here (docs/DEVELOPMENT.md "Decompiled game source")
        using var code = new BranchCode(PathOf(branch));
        var cards = code.Cards().ToDictionary(type => type.Name);
        foreach (var (card, expected) in Known)
        {
            if (!cards.TryGetValue(card, out var type)) continue; // a card only the other branch has
            Assert.Equal(expected, CardProgramReader.Read(type, code.Read).ToString());
        }
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void EveryCommandACardCallsIsInItsProgram(string branch)
    {
        // The reader drops nothing: each game command a card's play code calls (its OnPlay, the local functions and delegates it
        // declares) is a step of its program, or the program says what it couldn't follow.
        if (!File.Exists(PathOf(branch))) return;
        using var code = new BranchCode(PathOf(branch));
        var missing = new List<string>();
        var unknown = new List<string>();
        foreach (var type in code.Cards())
        {
            var program = CardProgramReader.Read(type, code.Read);
            unknown.AddRange(program.Unknowns.Select(reason => $"{type.Name}: {reason}"));
            var read = program.All().Select(step => step switch
            {
                CommandStep command => command.Command.FullName,
                ChoiceStep choice => choice.Command.FullName,
                _ => null,
            }).OfType<string>().ToHashSet();
            var called = OnPlayCode(type).SelectMany(code.Read)
                .Where(instruction => instruction.IsCall && instruction.Member is { } member
                    && (CardProgramReader.IsCommand(member) || member.DeclaringType == "CardSelectCmd" && member.Namespace == "MegaCrit.Sts2.Core.Commands"))
                .Select(instruction => instruction.Member!.FullName).ToHashSet();
            foreach (var command in called.Where(command => !read.Contains(command)))
                if (program.Unknowns.All(reason => !reason.Contains("callback"))) missing.Add($"{type.Name}: {command}");
        }
        Assert.True(missing.Count == 0, $"Commands a card calls that its program doesn't have ({branch}):\n" + string.Join("\n", missing));
        // What the reader couldn't follow: each is a gap to close (a recognizer for its shape), never a guess.
        Assert.True(unknown.Count == 0, $"Steps the reader couldn't follow ({branch}):\n" + string.Join("\n", unknown));
    }

    /// <summary>OnPlay, its state machine, the local functions and delegates it declares, and theirs.</summary>
    private static IEnumerable<MethodBase> OnPlayCode(Type card)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var method in card.GetMethods(all).Where(method => method.Name == "OnPlay" || method.Name.StartsWith("<OnPlay>", StringComparison.Ordinal)))
            yield return method;
        foreach (var nested in Procs.CardCode.NestedTypes(card))
            foreach (var method in nested.GetMethods(all))
                if (method.Name.StartsWith("<OnPlay>", StringComparison.Ordinal) || method.Name == "MoveNext" && nested.Name.Contains("<OnPlay>"))
                    yield return method;
    }

    [Fact]
    public void TheInstalledGameReadsAsItsAssemblyFile()
    {
        // The mod reads the loaded game (RuntimeCode); the tests read the decompile's copy of the same build (BranchCode).
        if (!File.Exists(BranchCode.BetaPath)) return;
        GameModels.EnsureLoaded();
        using var code = new BranchCode(BranchCode.BetaPath);
        var loaded = typeof(MegaCrit.Sts2.Core.Models.CardModel).Assembly;
        foreach (var type in code.Cards())
        {
            if (loaded.GetType(type.FullName!) is not { } runtime) continue;
            Assert.Equal(CardProgramReader.Read(type, code.Read).ToString(), CardProgramReader.Read(runtime, RuntimeCode.Read).ToString());
        }
    }
}
