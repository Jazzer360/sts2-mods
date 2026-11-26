using MegaCrit.Sts2.Core.Models;
using SpireQoL.Data;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The Run Reminders rules of every relic in the installed game (Data/ChoiceReminders, Data/CardAffinity), against Snapshots/reminder-rules.txt
/// (regenerated with SPIREQOL_UPDATE_SNAPSHOT=1): where each relic's icon shows, plain or crossed, what the check expects of it, and which of the
/// game's cards (as you'd get them) its card rule applies to. A change to a rule, or to what a rule finds in a game update, shows here relic by relic.
/// </summary>
[Collection(GameFightCollection.Name)]
public class ReminderRulesTests
{
    /// <summary>The readings judge card tests on the game's cards: they're made first.</summary>
    public ReminderRulesTests() => GameModels.EnsureLoaded();

    private static List<Type> Relics() => typeof(RelicModel).Assembly.GetTypes()
        .Where(type => !type.IsAbstract && typeof(RelicModel).IsAssignableFrom(type)).OrderBy(type => type.Name, StringComparer.Ordinal).ToList();

    private static string Cards(IEnumerable<CardModel> cards, Func<CardModel, bool> test)
    {
        var names = cards.Where(test).Select(card => card.GetType().Name).Order(StringComparer.Ordinal).ToList();
        return names.Count + (names.Count > 0 ? ": " + string.Join(", ", names) : "");
    }

    /// <summary>Each relic's rule as one line: what the snapshot holds.</summary>
    internal static List<string> Lines()
    {
        GameModels.EnsureLoaded();
        var cards = ModelDb.AllCards.ToList();
        var lines = new List<string>();
        foreach (var type in Relics())
        {
            var name = type.Name;
            var parts = new List<string>();
            if (ChoiceReminders.For(name) is { } rule)
            {
                parts.Add("on " + rule.On);
                if (rule.CrossedOn != ChoiceEffect.None) parts.Add("crossed on " + rule.CrossedOn);
                if (rule.OnCardReward) parts.Add("on card rewards");
                if (rule.Expect != ChoiceEffect.None) parts.Add("expects " + rule.Expect);
                if (!rule.ShopIcon) parts.Add("no shop icon");
                if (rule.CardTab) parts.Add("card tab");
                if (rule.Map != PathRoom.None) parts.Add("map " + rule.Map + (rule.MapCrossed != PathRoom.None ? " crossed " + rule.MapCrossed : ""));
                parts.Add("shows at " + string.Join(",", Enum.GetValues<ReminderPlace>().Where(rule.In)));
            }
            if (CardAffinity.For(name) is { } card)
            {
                parts.Add($"card rule {card.Kind}" + (card.Change != CardChange.None ? " " + card.Change : ""));
                parts.Add("applies to " + Cards(cards, candidate => CardAffinity.AppliesTo(card, candidate)));
                if (card.Base is { } test) parts.Add("base " + Cards(cards, candidate => test(candidate)));
            }
            if (parts.Count > 0) lines.Add(name + ": " + string.Join(" | ", parts));
        }
        return lines;
    }

    [Fact]
    public void EveryRelicsRuleIsTheSnapshot()
    {
        var lines = Lines();
        var path = Path.Combine(HookProgramTests.RepoRoot(), "tests", "SpireQoL.Catalog.Tests", "Snapshots", "reminder-rules.txt");
        if (Environment.GetEnvironmentVariable("SPIREQOL_UPDATE_SNAPSHOT") == "1")
        {
            File.WriteAllText(path, string.Join("\n", lines) + "\n");
            return;
        }
        Assert.True(File.Exists(path), $"missing {path}: generate it with SPIREQOL_UPDATE_SNAPSHOT=1");
        var expected = File.ReadAllText(path).ReplaceLineEndings("\n").TrimEnd().Split('\n');
        var changed = lines.Except(expected).Concat(expected.Except(lines).Select(line => "was: " + line)).Take(10).ToList();
        Assert.True(changed.Count == 0, "reminder rules changed (regenerate with SPIREQOL_UPDATE_SNAPSHOT=1 and explain):\n" + string.Join("\n", changed));
    }

    /// <summary>
    /// Every relic that overrides a hook through which a choice outside fights changes (Diagnostics/ChoiceHooks.txt) has a rule (its reading reacts, or a
    /// map rule) or a reviewed reason (Diagnostics/RelicChoiceTracking): read from the relics' compiled code, so a relic new in a game update, or one
    /// whose code the reading reads as doing nothing there, fails here before anyone holds one.
    /// </summary>
    [Fact]
    public void EveryRelicThatChangesAChoiceHasARuleOrAReason()
    {
        var hooks = SpireQoL.Diagnostics.CoverageLedger.ChoiceHooks.ToHashSet();
        var missing = Relics()
            .Where(type => ChoiceReminders.For(type.Name) == null && !SpireQoL.Diagnostics.RelicChoiceTracking.Reasons.ContainsKey(type.Name))
            .Select(type => (type.Name, Hooks: type.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly)
                .Where(method => method.IsVirtual && method.GetBaseDefinition().DeclaringType != type && hooks.Contains(method.Name)).Select(method => method.Name).Distinct().ToList()))
            .Where(relic => relic.Hooks.Count > 0).Select(relic => $"{relic.Name}: {string.Join(", ", relic.Hooks)}").ToList();
        Assert.True(missing.Count == 0, "Relics that change a choice outside fights with no rule and no reviewed reason:\n" + string.Join("\n", missing));
        var names = Relics().Select(type => type.Name).ToHashSet();
        var unknown = SpireQoL.Diagnostics.RelicChoiceTracking.Reasons.Keys.Concat(ChoiceReminders.MapRules.Keys).Where(name => !names.Contains(name)
            && !typeof(CardModel).Assembly.GetTypes().Any(type => type.Name == name && typeof(CardModel).IsAssignableFrom(type))).ToList();
        Assert.True(unknown.Count == 0, "Reasons or map rules for no relic or card of the game's: " + string.Join(", ", unknown));
    }

    /// <summary>Nothing a relic's hook does is left unread: a step the program reader can't follow, or a hook it can't read, is a gap to close.</summary>
    [Fact]
    public void NoRelicsReadingHasAGap()
    {
        var gaps = RelicReadings.LoadedRelics.Order(StringComparer.Ordinal).Select(RelicReadings.For).OfType<RelicReading>()
            .SelectMany(reading => reading.Notes.Where(note => note.Contains("? ", StringComparison.Ordinal)).Select(note => reading.Relic + ": " + note)).ToList();
        Assert.True(gaps.Count == 0, "Relic readings with something unread:\n" + string.Join("\n", gaps));
    }

    /// <summary>
    /// What a choice sets off and when it works against you, read from each relic's code (decision #359): a copy of the card that joined (Bing Bong's,
    /// a curse's copy against you), a test of the card (Darkstone Periapt's curses), a Modify hook's value (Ectoplasm's zero gold, Tungsten Rod's less
    /// HP lost; Beating Remnant's cap only in fights, The Boot's only on your hits), a Should hook's false (Sozu's potions), an effect that needs you to
    /// hold no potion (Belt Buckle's Dexterity) or a field a choice sets (Maw Bank's gold, ended by a purchase).
    /// </summary>
    [Theory]
    [InlineData("BingBong", ChoiceEffect.CardToDeck | ChoiceEffect.CurseToDeck, ChoiceEffect.CurseToDeck)]
    [InlineData("DarkstonePeriapt", ChoiceEffect.CurseToDeck, ChoiceEffect.None)]
    [InlineData("LuckyFysh", ChoiceEffect.CardToDeck | ChoiceEffect.CurseToDeck, ChoiceEffect.None)]
    [InlineData("Ectoplasm", ChoiceEffect.GoldGain, ChoiceEffect.GoldGain)]
    [InlineData("BowlerHat", ChoiceEffect.GoldGain, ChoiceEffect.None)]
    [InlineData("TungstenRod", ChoiceEffect.HpLoss, ChoiceEffect.None)]
    [InlineData("BeatingRemnant", ChoiceEffect.None, ChoiceEffect.None)]
    [InlineData("TheBoot", ChoiceEffect.None, ChoiceEffect.None)]
    [InlineData("Sozu", ChoiceEffect.Potion, ChoiceEffect.Potion)]
    [InlineData("BeltBuckle", ChoiceEffect.Potion, ChoiceEffect.Potion)]
    [InlineData("MawBank", ChoiceEffect.Purchase, ChoiceEffect.Purchase)]
    [InlineData("StoneHumidifier", ChoiceEffect.RestHeal, ChoiceEffect.None)]
    public void ARelicsChoicesAreReadFromItsCode(string relic, ChoiceEffect on, ChoiceEffect crossed)
    {
        var reading = RelicReadings.For(relic)!;
        Assert.Equal(on, reading.On);
        Assert.Equal(crossed, reading.CrossedOn);
    }

    /// <summary>
    /// The check expects a flash only where the code always flashes on the choice (decision #359): Darkstone Periapt on a curse, Lucky Fysh on any card;
    /// never Book of Five Rings (every fifth card), nor Ectoplasm (the gold it flashes on never comes), nor what the check doesn't see (a rest).
    /// </summary>
    [Theory]
    [InlineData("DarkstonePeriapt", ChoiceEffect.CurseToDeck)]
    [InlineData("LuckyFysh", ChoiceEffect.CardToDeck | ChoiceEffect.CurseToDeck)]
    [InlineData("BookOfFiveRings", ChoiceEffect.None)]
    [InlineData("Ectoplasm", ChoiceEffect.None)]
    [InlineData("BowlerHat", ChoiceEffect.GoldGain)]
    [InlineData("StoneHumidifier", ChoiceEffect.None)]
    public void TheCheckExpectsAFlashWhereTheCodeAlwaysFlashes(string relic, ChoiceEffect expect) => Assert.Equal(expect, RelicReadings.For(relic)!.Expect);

    /// <summary>
    /// A card rule is the test the relic's hook makes on the card, evaluated on the game's cards (decision #359): Fresnel Lens' Nimble.CanEnchant (a card
    /// that gains block, not a curse), Molten Egg's upgradable Attack, Miniature Cannon's upgraded Attack (the upgrade the change it looks for, the
    /// Attack its powered-attack test), Vitruvian Minion's Minion tag from both its damage and its block hooks.
    /// </summary>
    [Fact]
    public void ACardRuleIsTheTestItsHookMakes()
    {
        GameModels.EnsureLoaded();
        CardModel Card(string name) => ModelDb.AllCards.First(card => card.GetType().Name == name);
        var lens = CardAffinity.For("FresnelLens")!;
        Assert.Equal((AffinityKind.Modifies, CardChange.Enchant), (lens.Kind, lens.Change));
        Assert.True(lens.Applies(Card("DefendIronclad")));
        Assert.False(lens.Applies(Card("StrikeIronclad")));
        Assert.False(lens.Applies(Card("Injury")));
        var egg = CardAffinity.For("MoltenEgg")!;
        Assert.True(egg.Applies(Card("StrikeIronclad")));
        Assert.False(egg.Applies(Card("DefendIronclad")));
        var cannon = CardAffinity.For("MiniatureCannon")!;
        Assert.Equal((AffinityKind.Boosts, CardChange.Upgrade), (cannon.Kind, cannon.Change));
        Assert.False(cannon.Applies(Card("StrikeIronclad")));
        Assert.True(cannon.Base!(Card("StrikeIronclad")));
        Assert.False(cannon.Base!(Card("DefendIronclad")));
        var minion = RelicReadings.For("VitruvianMinion")!;
        Assert.Contains(minion.Notes, note => note.StartsWith("ModifyDamageMultiplicative: Boosts", StringComparison.Ordinal));
        Assert.Contains(minion.Notes, note => note.StartsWith("ModifyBlockMultiplicative: Boosts", StringComparison.Ordinal));
        Assert.True(CardAffinity.For("VitruvianMinion")!.Applies(Card("MinionStrike")));
        Assert.False(CardAffinity.For("VitruvianMinion")!.Applies(Card("StrikeIronclad")));
    }

    /// <summary>
    /// The choice hooks no relic of the game's overrides yet are read with their setting (decision #371): a curse kept out of your deck is for you
    /// (plain), a card leaving your deck is a reaction to a removal (flashing on every one); and a Modify hook's value is compared with its amount
    /// (decision #372): more HP lost is against you (crossed), less gold too, no HP lost at all is for you.
    /// </summary>
    [Theory]
    [InlineData(typeof(TestRelics.CurseWard), ChoiceEffect.CurseToDeck, ChoiceEffect.None, ChoiceEffect.None)]
    [InlineData(typeof(TestRelics.RemovalRefund), ChoiceEffect.CardRemoval, ChoiceEffect.None, ChoiceEffect.CardRemoval)]
    [InlineData(typeof(TestRelics.HeavierFall), ChoiceEffect.HpLoss, ChoiceEffect.HpLoss, ChoiceEffect.None)]
    [InlineData(typeof(TestRelics.ThinPurse), ChoiceEffect.GoldGain, ChoiceEffect.GoldGain, ChoiceEffect.None)]
    [InlineData(typeof(TestRelics.FeatherFall), ChoiceEffect.HpLoss, ChoiceEffect.None, ChoiceEffect.None)]
    [InlineData(typeof(TestRelics.SmithsToken), ChoiceEffect.None, ChoiceEffect.None, ChoiceEffect.None)]
    public void AHookNoGameRelicHasYetIsReadWithItsSetting(Type relic, ChoiceEffect on, ChoiceEffect crossed, ChoiceEffect expect)
    {
        var cards = ModelDb.AllCards.ToList();
        var reading = RelicReadings.Read(relic, SpireQoL.Procs.Programs.RuntimeCode.Read, () => cards);
        Assert.True((on, crossed, expect) == (reading.On, reading.CrossedOn, reading.Expect), string.Join(" | ", reading.Notes));
        Assert.NotEmpty(reading.Notes);
        Assert.DoesNotContain(reading.Notes, note => note.Contains("? ", StringComparison.Ordinal));
    }

    /// <summary>
    /// A Modify hook's value against its amount on the game's relics (decision #372), read with each relic's own values: Bowler Hat's more gold and Regal
    /// Pillow's bigger heal are plain, Tungsten Rod's less HP lost plain, Ectoplasm's no gold crossed.
    /// </summary>
    [Theory]
    [InlineData("BowlerHat", "more")]
    [InlineData("RegalPillow", "more")]
    [InlineData("TungstenRod", "less")]
    [InlineData("Ectoplasm", "zero")]
    public void AModifyHooksValueIsComparedWithItsAmount(string relic, string change) =>
        Assert.Contains(RelicReadings.For(relic)!.Notes, note => note.EndsWith(": " + change, StringComparison.Ordinal));

    /// <summary>One table of what each choice effect is (decision #373): every effect in it once; the option reader's meaning of a step read from it.</summary>
    [Fact]
    public void EveryChoiceEffectHasOneMeaning()
    {
        var effects = Enum.GetValues<ChoiceEffect>().Where(effect => effect != ChoiceEffect.None).Order().ToList();
        Assert.Equal(effects, ChoiceEffects.Table.Select(meaning => meaning.Effect).Order().ToList());
        Assert.Equal(ChoiceEffect.CardToDeck | ChoiceEffect.CardRemoval, SpireQoL.Features.OptionReader.EffectOf("CardCmd.TransformToRandom"));
        Assert.Equal(ChoiceEffect.MaxHpGain | ChoiceEffect.Heal, SpireQoL.Features.OptionReader.EffectOf("CreatureCmd.GainMaxHp"));
        Assert.Equal(ChoiceEffect.RestHeal | ChoiceEffect.Heal, SpireQoL.Features.OptionReader.EffectOf("PlayerCmd.MimicRestSiteHeal"));
        Assert.Equal(ChoiceEffect.None, SpireQoL.Features.OptionReader.EffectOf("CardCmd.Preview"));
        Assert.Equal(ChoiceEffect.CurseToDeck | ChoiceEffect.HpLoss | ChoiceEffect.MaxHpLoss | ChoiceEffect.GoldLoss | ChoiceEffect.PotionLoss | ChoiceEffect.CardRemoval
                     | ChoiceEffect.RelicLoss, ChoiceEffects.AgainstYou);
    }

    public static TheoryData<string> Branches() => new() { "beta", "main" };

    /// <summary>What each relic's hooks read as on a branch's code (decision #359), for every relic that overrides a hook the reading reads.</summary>
    private static List<string> Readings(BranchCode code)
    {
        GameModels.EnsureLoaded();
        var cards = ModelDb.AllCards.ToList();
        var hooks = RelicReadings.HookNames.ToHashSet();
        var lines = new List<string>();
        foreach (var type in RelicReadings.RelicTypes(code.Assembly).OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            // The relic's values are the loaded game's (a Modify hook's value against its amount: decision #372); its code is the branch's.
            var reading = RelicReadings.Read(type, code.Read, () => cards, key => RelicReadings.LoadedValue(type.Name, key));
            if (!reading.Reacts && reading.Notes.Count == 0) continue;
            var rule = new List<string>();
            if (reading.On != ChoiceEffect.None) rule.Add("on " + reading.On);
            if (reading.CrossedOn != ChoiceEffect.None) rule.Add("crossed on " + reading.CrossedOn);
            if (reading.Expect != ChoiceEffect.None) rule.Add("expects " + reading.Expect);
            if (reading.Joining != ChoiceEffect.None) rule.Add("joining " + reading.Joining);
            if (reading.ChangesShopCards) rule.Add("changes shop cards");
            if (reading.Card is { } card) rule.Add("card " + card);
            lines.Add($"{type.Name}: {(rule.Count == 0 ? "nothing" : string.Join(" | ", rule))} || {string.Join(" | ", reading.Notes)}");
        }
        return lines;
    }

    /// <summary>
    /// Every relic's reading on each branch's code, against Snapshots/relic-readings.txt (beta) and relic-readings-main.txt (main): what each read hook
    /// came to, so a game update or a reader change that reads a relic differently shows here.
    /// </summary>
    [Theory]
    [MemberData(nameof(Branches))]
    public void EveryRelicsReadingIsTheSnapshot(string branch)
    {
        var assembly = branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;
        if (!File.Exists(assembly)) return; // no decompile of this branch here
        var lines = Readings(BranchCode.Of(assembly));
        var path = Path.Combine(HookProgramTests.RepoRoot(), "tests", "SpireQoL.Catalog.Tests", "Snapshots", branch == "main" ? "relic-readings-main.txt" : "relic-readings.txt");
        if (Environment.GetEnvironmentVariable("SPIREQOL_UPDATE_SNAPSHOT") == "1")
        {
            File.WriteAllText(path, string.Join("\n", lines) + "\n");
            return;
        }
        Assert.True(File.Exists(path), $"missing {path}: generate it with SPIREQOL_UPDATE_SNAPSHOT=1");
        var expected = File.ReadAllText(path).ReplaceLineEndings("\n").TrimEnd().Split('\n');
        var changed = lines.Except(expected).Concat(expected.Except(lines).Select(line => "was: " + line)).Take(10).ToList();
        Assert.True(changed.Count == 0, "relic readings changed (regenerate with SPIREQOL_UPDATE_SNAPSHOT=1 and explain):\n" + string.Join("\n", changed));
    }
}
