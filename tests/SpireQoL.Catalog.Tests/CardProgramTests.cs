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
        // Energy only when a hit killed (a condition on the attack's results: a lambda over them).
        ["Sunder"] = "Sunder: DamageCmd.Attack, PlayerCmd.GainEnergy if Any(SelectMany(result of AttackCommand.Execute.Results, r => r), r => r.WasTargetKilled)",
        // Exhaust, then block, card by card.
        ["SecondWind"] = "SecondWind: loop over each of GetCards.ToList [CardCmd.Exhaust, CreatureCmd.GainBlock]",
        // Draws, then the pick, then the discard of what was picked.
        ["Acrobatics"] = "Acrobatics: CardPileCmd.Draw, choice FromHandForDiscard, CardCmd.Discard if FirstOrDefault(result of CardSelectCmd.FromHandForDiscard)",
        // Draws before its No Draw.
        ["BattleTrance"] = "BattleTrance: CardPileCmd.Draw, PowerCmd.Apply<NoDrawPower>",
        // A do-while that draws again while the card it drew is an Attack and your hand has room: its round's tests, in order.
        ["Pillage"] = "Pillage: DamageCmd.Attack, loop while result of CardPileCmd.Draw and result of CardPileCmd.Draw.Type == Attack"
                      + " and Count(GetCards(this.Owner, [2])) < MaxCardsInHand [CardPileCmd.Draw]",
        // The attack runs inside the shot (a local function the shot is given).
        ["BlossomShot"] = "BlossomShot: ArrowCmd.ShootArrowAtTarget [DamageCmd.Attack, PowerCmd.Apply<StretchPower>] if not cardPlay.Target.IsDead",
        ["Bash"] = "Bash: DamageCmd.Attack, PowerCmd.Apply<VulnerablePower>",
        // Conditions as values: a negated lambda over your hand (no Attack in it).
        ["Impatience"] = "Impatience: CardPileCmd.Draw if not Any(GetPile(2, this.Owner).Cards, c => c.Type == Attack)",
        // A local stored before the attack (whether the target was Impaled then), read after it.
        ["KiHop"] = "KiHop: DamageCmd.Attack, CardPileCmd.Draw if wasImpaled=CreatureIsImpaledByArrow(cardPlay.Target)",
        // An if holding a while: the loop and the draw after it both wait on the if's test.
        ["Wrest"] = "Wrest: loop while CreatureIsImpaledByPlayersArrow(cardPlay.Target, this.Owner) [ArrowCmd.DislodgeArrow] if CreatureIsImpaledByPlayersArrow(cardPlay.Target, this.Owner),"
                    + " CardPileCmd.Draw if CreatureIsImpaledByPlayersArrow(cardPlay.Target, this.Owner)",
        // The other way round: if (!IsOnlyCardInHand) return; then the draws and the energy. The test's direction is kept.
        ["Restlessness"] = "Restlessness: loop over Cards [CardPileCmd.Draw] if this.IsOnlyCardInHand, PlayerCmd.GainEnergy if this.IsOnlyCardInHand",
        // && in the test (a null check, then the drawn card's type).
        ["EscapePlan"] = "EscapePlan: CardPileCmd.Draw, CreatureCmd.GainBlock if FirstOrDefault(result of CardPileCmd.Draw) and FirstOrDefault(result of CardPileCmd.Draw).Type == Skill",
        // An Osty check, then a test of the card's own property, negated.
        ["Fetch"] = "Fetch: DamageCmd.Attack if not CheckMissingWithAnim(this.Owner), CardPileCmd.Draw if not CheckMissingWithAnim(this.Owner) and not this.HasBeenPlayedThisTurn",
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

    [Theory]
    [MemberData(nameof(Branches))]
    public void AValueTwoWaysLeaveIsEachWaysValue(string branch)
    {
        // Where two ways meet with a value each, the value is each way's on its condition, never one way's alone: bool flag = a && b (the
        // jump past b leaves false), a local stored on several ways (AllForOne's Filter: flag2 = flag; if (flag2) flag2 = a type test), and
        // a number (EvilEye: blockGains = WasCardExhaustedThisTurn ? 2 : 1).
        if (!File.Exists(PathOf(branch))) return;
        using var code = new BranchCode(PathOf(branch));
        var cards = code.Cards().ToDictionary(type => type.Name);
        if (cards.TryGetValue("AllForOne", out var allForOne))
        {
            var taken = CardProgramReader.Read(allForOne, code.Read).All().OfType<LoopStep>().Single(loop => loop.Items != null);
            Assert.DoesNotContain(taken.Items!.Parts(), part => part is UnknownValue);
            Assert.Contains(taken.Items!.Parts(), part => part is QueryValue { Name: "CostsX" });
            Assert.Contains(taken.Items!.Parts(), part => part is QueryValue { Name: "Type" });
        }
        if (cards.TryGetValue("EvilEye", out var evilEye))
        {
            var loop = CardProgramReader.Read(evilEye, code.Read).All().OfType<LoopStep>().Single();
            var ways = loop.While!.Literals.SelectMany(literal => literal.Test.Parts()).OfType<WaysValue>().Single();
            Assert.Equal(new long[] { 1, 2 }, ways.Ways.Select(way => ((ConstantValue)way.Value).Value).Order());
        }
    }

    [Fact]
    public void ConditionsRejoinWhereTheWaysMeet()
    {
        // The ways round an if meet again after it: what follows has no condition. Each test is its branch's (a test at another place
        // is another test, even of the same thing).
        var first = new ConditionLiteral("m:0010", new NamedValue("a"), Holds: true);
        var second = new ConditionLiteral("m:0020", new NamedValue("b"), Holds: true);
        var always = ProgramCondition.Always;
        Assert.True(always.And(first).Or(always.And(first.Negate())).IsAlways);
        // if (a && b) {...}: the else is not a, or a and not b; after it, every way.
        var elseWay = always.And(first.Negate()).Or(always.And(first).And(second.Negate()));
        Assert.Equal("not a or not b", elseWay.ToString());
        Assert.True(elseWay.Or(always.And(first).And(second)).IsAlways);
        // if (a || b): a, or not a and b, is a or b.
        Assert.Equal("a or b", always.And(first).Or(always.And(first.Negate()).And(second)).ToString());
        // Inside a loop that runs on a: its body's steps don't repeat it.
        Assert.Equal("b", always.And(first).And(second).Without(always.And(first)).ToString());
        // The same test at another branch is another literal.
        var again = new ConditionLiteral("m:0030", new NamedValue("a"), Holds: false);
        Assert.False(always.And(first).Or(always.And(again)).IsAlways);
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
