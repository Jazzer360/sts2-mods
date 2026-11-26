using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Data;
using SpireQoL.Features;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Event options read as programs (decision #350): every option's handler in every event, and every relic's AfterObtained, read by the option reader on
/// each branch's code. Snapshots/event-options.txt (beta) and event-options-main.txt (main), regenerated with SPIREQOL_UPDATE_SNAPSHOT=1, hold what each
/// reads and its coverage: how many are read whole, and the rest by the reason the reading stops. Every `?` there is an open TRACKER item.
/// </summary>
[Collection(GameFightCollection.Name)]
public class OptionProgramTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static TheoryData<string> Branches() => new() { "beta", "main" };

    private static OptionReader ReaderOf(BranchCode code) => code.Reading("option reader", branch =>
    {
        GameModels.EnsureLoaded();
        var curses = ModelDb.AllCards.Where(card => card.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Curse).Select(card => card.GetType().Name).ToHashSet();
        return new OptionReader(branch.Assembly, branch.Read, curses.Contains);
    });

    /// <summary>The game's events on a branch (not its test mocks), by name.</summary>
    private static IEnumerable<Type> Events(Assembly game) => game.GetTypes()
        .Where(type => !type.IsAbstract && !type.IsNested && Derives(type, "EventModel") && type.Namespace?.EndsWith(".Mocks", StringComparison.Ordinal) != true)
        .OrderBy(type => type.Name, StringComparer.Ordinal);

    private static bool Derives(Type type, string name)
    {
        for (var other = type.BaseType; other != null; other = other.BaseType)
            if (other.Name == name) return true;
        return false;
    }

    /// <summary>A handler's name as a reader would say it: the method, or the method a lambda or local function is written in.</summary>
    private static string Label(Type eventType, MethodBase handler, Type? relic, Dictionary<string, int> seen)
    {
        var name = handler.Name;
        if (name.StartsWith('<'))
        {
            var close = name.IndexOf('>');
            var inner = name[1..close];
            var local = name.IndexOf("g__", StringComparison.Ordinal) is var at and >= 0 ? "." + name[(at + 3)..].Split('|')[0] : " lambda";
            name = (inner.Length == 0 ? "" : inner) + local;
        }
        var label = eventType.Name + "." + name + (relic != null ? " [" + relic.Name + "]" : "");
        seen[label] = seen.GetValueOrDefault(label) + 1;
        return seen[label] > 1 ? label + " #" + seen[label] : label;
    }

    /// <summary>
    /// Every option of every event and every relic's obtaining, read on the branch's code, against the branch's snapshot: a change in what an option
    /// reads as, or a new gap, shows here.
    /// </summary>
    [Theory]
    [MemberData(nameof(Branches))]
    public void EveryOptionsReadingIsTheSnapshot(string branch)
    {
        var path = branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;
        if (!File.Exists(path)) return; // no decompile of this branch here
        var code = BranchCode.Of(path);
        var reader = ReaderOf(code);
        var options = new List<string>();
        var gaps = new Dictionary<string, int>();
        int whole = 0, whenShown = 0, total = 0;
        foreach (var eventType in Events(code.Assembly))
        {
            var seen = new Dictionary<string, int>();
            foreach (var (handler, relic) in reader.Options(eventType))
            {
                var reading = reader.Read(handler, relic);
                options.Add(Label(eventType, handler, relic, seen) + ": " + reading);
                total++;
                if (reading.Whole) whole++;
                else if (reading.Gaps.All(gap => gap.WhenShown)) whenShown++;
                foreach (var reason in reading.Gaps.Where(gap => !gap.WhenShown).Select(gap => gap.Reason).Distinct()) gaps[reason] = gaps.GetValueOrDefault(reason) + 1;
            }
        }
        var relics = new List<string>();
        var relicGaps = new Dictionary<string, int>();
        int relicsWhole = 0, relicsWhenShown = 0, relicsTotal = 0;
        foreach (var relic in code.Assembly.GetTypes().Where(type => !type.IsAbstract && Derives(type, "RelicModel") && type.Namespace == "MegaCrit.Sts2.Core.Models.Relics").OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            if (relic.GetMethod("AfterObtained", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) == null) continue;
            var reading = reader.ReadObtained(relic);
            relics.Add("relic " + relic.Name + ": " + reading);
            relicsTotal++;
            if (reading.Whole) relicsWhole++;
            else if (reading.Gaps.All(gap => gap.WhenShown)) relicsWhenShown++;
            foreach (var reason in reading.Gaps.Where(gap => !gap.WhenShown).Select(gap => gap.Reason).Distinct()) relicGaps[reason] = relicGaps.GetValueOrDefault(reason) + 1;
        }
        var text = new StringBuilder();
        text.Append($"# Event options read as programs on {branch} (OptionProgramTests: SPIREQOL_UPDATE_SNAPSHOT=1 regenerates; decision #350).\n");
        text.Append($"# Options: {total}; read whole {whole}; whole once shown (what the option and the run hold then: decisions #74, #354, #355) {whenShown}; with a gap {total - whole - whenShown}.\n");
        foreach (var (reason, count) in gaps.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key, StringComparer.Ordinal)) text.Append($"#   {count} {reason}\n");
        text.Append($"# Relics' AfterObtained: {relicsTotal}; read whole {relicsWhole}; whole once shown {relicsWhenShown}; with a gap {relicsTotal - relicsWhole - relicsWhenShown}.\n");
        foreach (var (reason, count) in relicGaps.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key, StringComparer.Ordinal)) text.Append($"#   {count} {reason}\n");
        foreach (var line in options.Concat(relics)) text.Append(line).Append('\n');
        var file = Path.Combine(CardPlaySnapshotTests.SourceFolder(), "Snapshots", branch == "main" ? "event-options-main.txt" : "event-options.txt");
        if (Environment.GetEnvironmentVariable("SPIREQOL_UPDATE_SNAPSHOT") == "1")
        {
            File.WriteAllText(file, text.ToString());
            return;
        }
        Assert.True(File.Exists(file), $"missing {file}: generate it with SPIREQOL_UPDATE_SNAPSHOT=1");
        var lines = text.ToString().Split('\n');
        var expected = File.ReadAllText(file).Split('\n');
        var changed = lines.Except(expected).Take(12).ToList();
        var gone = expected.Except(lines).Take(12).ToList();
        Assert.True(changed.Count == 0 && gone.Count == 0, $"{Path.GetFileName(file)} isn't what the option reader reads now: regenerate it with SPIREQOL_UPDATE_SNAPSHOT=1 and explain the change\nnow:\n"
            + string.Join("\n", changed) + "\nbefore:\n" + string.Join("\n", gone));
    }

    /// <summary>What a reading comes to (its effects by kind and step, its gaps, the other steps): a type's name in a condition reads differently from a file.</summary>
    private static string Summary(OptionReading reading) => string.Join(", ", reading.Effects.Select(effect => effect.Kind + " " + effect.Step + " " + effect.Via))
        + " / " + string.Join(", ", reading.Gaps) + " / " + string.Join(", ", reading.Others);

    /// <summary>The game's code as the mod reads it and the branch's assembly file read the same: the beta snapshot is what the game reads.</summary>
    [Fact]
    public void TheLoadedGameReadsAsItsFile()
    {
        if (!File.Exists(BranchCode.BetaPath)) return;
        GameModels.EnsureLoaded();
        var file = ReaderOf(BranchCode.Of(BranchCode.BetaPath));
        var fileEvents = Events(BranchCode.Of(BranchCode.BetaPath).Assembly).ToDictionary(type => type.Name);
        var differ = new List<string>();
        foreach (var eventType in Events(typeof(EventModel).Assembly))
        {
            if (!fileEvents.TryGetValue(eventType.Name, out var fileType)) continue;
            var loaded = OptionReader.Game.Options(eventType).Select(option => Summary(OptionReader.Game.Read(option.Handler, option.Relic))).ToList();
            var read = file.Options(fileType).Select(option => Summary(file.Read(option.Handler, option.Relic))).ToList();
            if (!loaded.SequenceEqual(read)) differ.Add(eventType.Name + ":\n  loaded " + string.Join(" | ", loaded) + "\n  file   " + string.Join(" | ", read));
        }
        Assert.True(differ.Count == 0, "the loaded game's options read otherwise than its file's (is the installed game the beta decompile's?):\n" + string.Join("\n", differ.Take(5)));
    }

    /// <summary>
    /// Options the flag-level scan (decision #37's, kept as this check) reads an effect in that the program reading doesn't, and why the reading is right.
    /// Keyed by event and handler.
    /// </summary>
    private static readonly Dictionary<string, string> ScanReviewed = new()
    {
        // The scan reads every call in the code: these options open a page whose options' hover tips name a curse (HoverTipFactory.FromCard<Guilty>),
        // which the option itself never adds.
        ["Trial.Accept"] = "the next page's hover tips name a curse",
        ["WaterfallOfObviousSecrets.EnterWaterfall"] = "the next page's hover tips name a curse",
    };

    /// <summary>
    /// The scan the program reading replaced finds nothing the reading misses, on every option of the loaded game: a game call that does something (a card
    /// to your deck, gold, a potion...) the reading doesn't follow shows here first, not in a player's report.
    /// </summary>
    [Fact]
    public void TheScanFindsNothingTheReadingMisses()
    {
        GameModels.EnsureLoaded();
        var missed = new List<string>();
        foreach (var eventType in Events(typeof(EventModel).Assembly))
            foreach (var (handler, relic) in OptionReader.Game.Options(eventType))
            {
                if (Option(handler, relic) is not { } option) continue;
                var extra = RunReminders.ScanOf(option) & ~RunReminders.EffectsOf(option);
                var key = eventType.Name + "." + handler.Name;
                if (extra != ChoiceEffect.None && !ScanReviewed.ContainsKey(key)) missed.Add($"{key}{(relic != null ? " [" + relic.Name + "]" : "")}: the scan reads {extra}");
                if (extra == ChoiceEffect.None) Assert.False(ScanReviewed.ContainsKey(key) && relic == null, $"{key} is reviewed but the scan reads nothing more now: take it out");
            }
        Assert.True(missed.Count == 0, "options whose code the scan reads an effect in that the program reading doesn't:\n" + string.Join("\n", missed));
    }

    /// <summary>An option of the loaded game's running <paramref name="handler"/> on an event or closure made without its constructor, carrying <paramref name="relic"/>.</summary>
    private static EventOption? Option(MethodBase handler, Type? relic)
    {
        if (handler is not MethodInfo method || method.GetParameters().Length != 0 || method.ReturnType != typeof(Task) || method.DeclaringType is not { } owner
            || owner.IsGenericTypeDefinition) return null;
        var onChosen = method.IsStatic ? Delegate.CreateDelegate(typeof(Func<Task>), method) : Delegate.CreateDelegate(typeof(Func<Task>), RuntimeHelpers.GetUninitializedObject(owner), method);
        var option = (EventOption)RuntimeHelpers.GetUninitializedObject(typeof(EventOption));
        typeof(EventOption).GetField("<OnChosen>k__BackingField", Instance)!.SetValue(option, onChosen);
        if (relic != null) typeof(EventOption).GetField("<Relic>k__BackingField", Instance)!.SetValue(option, ModelDb.AllRelics.First(model => model.GetType() == relic));
        return option;
    }

    private static OptionReading ReadOption(string eventName, string handler)
    {
        GameModels.EnsureLoaded();
        var eventType = Events(typeof(EventModel).Assembly).Single(type => type.Name == eventName);
        var (method, relic) = OptionReader.Game.Options(eventType).Single(option => option.Handler.Name == handler);
        return OptionReader.Game.Read(method, relic);
    }

    [Fact]
    public void ARelicTheOptionGivesIsReadThroughItsAfterObtained()
    {
        // Hungry for Mushrooms' Fragrant Mushroom: RelicCmd.Obtain<FragrantMushroom>, whose AfterObtained costs HP and upgrades cards (the scan read the
        // option as doing nothing: Tungsten Rod's icon was missing).
        var reading = ReadOption("HungryForMushrooms", "FragrantMushroom");
        Assert.Contains(reading.Effects, effect => effect.Kind.HasFlag(ChoiceEffect.HpLoss) && effect.Via == "FragrantMushroom");
        Assert.True(reading.Kinds.HasFlag(ChoiceEffect.RelicGain), reading.ToString());
        Assert.True(reading.Whole, reading.ToString());
    }

    [Fact]
    public void TheEventsBaseClassMethodIsReadThroughItsCode()
    {
        // Room Full of Cheese's Gorge: EventModel.SelectCardsToAddToDeckFromGrid adds the cards you pick (the scan didn't follow it: no icon at all).
        var reading = ReadOption("RoomFullOfCheese", "Gorge");
        Assert.True(reading.Kinds.HasFlag(ChoiceEffect.CardToDeck), reading.ToString());
        Assert.True(reading.Whole, reading.ToString());
    }

    [Fact]
    public void ANextPagesOptionIsntThisOptions()
    {
        // Trial's Accept opens a page whose options add curses (their hover tips name them): Accept itself does nothing a reminder looks at.
        var reading = ReadOption("Trial", "Accept");
        Assert.Equal(ChoiceEffect.None, reading.Kinds);
        Assert.True(reading.Whole, reading.ToString());
    }

    [Fact]
    public void AFightAndTheRewardAfterItAreRead()
    {
        // The Lantern Key's Fight: EnterCombatWithoutExitingEvent, with a SpecialCardReward of the key, read through the reward's own OnSelect.
        var reading = ReadOption("TheLanternKey", "Fight");
        Assert.True(reading.Kinds.HasFlag(ChoiceEffect.Fight), reading.ToString());
        Assert.Contains(reading.Effects, effect => effect.Kind.HasFlag(ChoiceEffect.CardToDeck) && effect.Via == "reward SpecialCardReward");
    }

    [Fact]
    public void TheEventsOwnPartIsRead()
    {
        // The Crystal Sphere's minigame and the items it hides: card rewards, a curse, gold, potions and a relic, each read through its own code.
        var reading = ReadOption("CrystalSphere", "UncoverFuture");
        foreach (var kind in new[] { ChoiceEffect.CardToDeck, ChoiceEffect.CurseToDeck, ChoiceEffect.GoldGain, ChoiceEffect.Potion, ChoiceEffect.RelicGain })
            Assert.True(reading.Kinds.HasFlag(kind), $"{kind}: {reading}");
    }

    [Fact]
    public void ADelegateGivenToAQueryIsRead()
    {
        // Neow's Bones: Select(r => new RelicReward(r, ...)) makes the relic rewards it offers: the lambda's reward is read, its relic a gap (rolled).
        GameModels.EnsureLoaded();
        var reading = OptionReader.Game.ReadObtained(typeof(MegaCrit.Sts2.Core.Models.Relics.NeowsBones));
        Assert.True(reading.Kinds.HasFlag(ChoiceEffect.RelicGain), reading.ToString());
        Assert.True(reading.Kinds.HasFlag(ChoiceEffect.CurseToDeck), reading.ToString());
        Assert.Contains(reading.Gaps, gap => gap.Reason == "obtains a reward's relic");
        Assert.DoesNotContain(reading.Gaps, gap => gap.Reason == "commands in a callback");
    }

    [Fact]
    public void AnAncientsRelicOptionIsReadThroughTheRelic()
    {
        // Neow's Hefty Tablet: the ancient's RelicOption obtains the option's relic, whose AfterObtained adds the rare you pick and an Injury.
        GameModels.EnsureLoaded();
        var tablet = OptionReader.Game.Read(OptionReader.Game.Options(Events(typeof(EventModel).Assembly).Single(type => type.Name == "Neow"))
            .First(option => option.Relic == typeof(MegaCrit.Sts2.Core.Models.Relics.HeftyTablet)).Handler, typeof(MegaCrit.Sts2.Core.Models.Relics.HeftyTablet));
        Assert.True(tablet.Kinds.HasFlag(ChoiceEffect.CurseToDeck) && tablet.Kinds.HasFlag(ChoiceEffect.RelicGain), tablet.ToString());
        Assert.Contains(tablet.Effects, effect => effect.Via == "HeftyTablet");
        Assert.True(tablet.Whole, tablet.ToString());
    }

    [Fact]
    public void ANeowModifierOptionIsReadThroughTheModifiersOption()
    {
        // A custom run's Neow modifier option runs the modifier's own option (OnModifierOptionSelected awaits the Func<Task> the closure holds): Draft's
        // ten card rewards add cards to your deck. Read through the delegate the option holds as it's shown (decision #74).
        GameModels.EnsureLoaded();
        var neow = typeof(EventModel).Assembly.GetTypes().Single(type => type.Name == "Neow");
        var closureType = neow.GetNestedTypes(BindingFlags.NonPublic).Single(type => type.GetField("neowOption", Instance) != null);
        var closure = RuntimeHelpers.GetUninitializedObject(closureType);
        var draft = (ModifierModel)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Models.Modifiers.Draft));
        closureType.GetField("neowOption", Instance)!.SetValue(closure, draft.GenerateNeowOption((EventModel)RuntimeHelpers.GetUninitializedObject(neow)));
        var handler = closureType.GetMethods(Instance).Single(method => method.Name.Contains("GenerateNeowModifiers", StringComparison.Ordinal) && method.ReturnType == typeof(Task));
        var option = (EventOption)RuntimeHelpers.GetUninitializedObject(typeof(EventOption));
        typeof(EventOption).GetField("<OnChosen>k__BackingField", Instance)!.SetValue(option, Delegate.CreateDelegate(typeof(Func<Task>), closure, handler));
        var reading = RunReminders.ReadingOf(option);
        Assert.True(reading.Kinds.HasFlag(ChoiceEffect.CardToDeck), reading.ToString());
        Assert.DoesNotContain(reading.Gaps, gap => gap.Reason == "runs a delegate the event holds");
    }

    [Fact]
    public void ACardRewardsAlternativesAreReadOnceShown()
    {
        // A card reward's alternatives (skip, a relic's sacrifice) run a delegate the reward's alternative holds: read without the run, the ones your relics
        // add are read once the option is shown (decision #355), never dropped.
        var reading = ReadOption("ColorfulPhilosophers", "<GenerateInitialOptions>b__0");
        Assert.True(reading.Kinds.HasFlag(ChoiceEffect.CardToDeck), reading.ToString());
        Assert.Contains(reading.Gaps, gap => gap.WhenShown && gap.Reason == "runs the delegates your relics give CardRewardAlternative (TryModifyCardRewardAlternatives)");
    }
}
