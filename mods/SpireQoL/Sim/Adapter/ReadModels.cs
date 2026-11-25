using System.Reflection;
using SpireQoL.Procs.Programs;

namespace SpireQoL.Sim;

/// <summary>
/// The powers and relics the simulator models from their own code (decision #195): each one's hooks read from the running game (HookReader)
/// once, when the simulator first asks for a model (SimModels.Read), and run by ReadModel. A main player gets main's code.
/// </summary>
public static partial class ReadModels
{
    /// <summary>
    /// The read models of the running game's classes, for SimModels (on a worker: SimModels.StartReading). A class whose hooks read whole at
    /// the reviewed builds and don't now (a game update) is kept for Report: SimModels reads them once a launch.
    /// </summary>
    public static IEnumerable<ModelSpec> Specs() => Specs(Diagnostics.CoverageLedger.ModelTypes(), RuntimeCode.Read, unread: Unread);

    /// <summary>
    /// The read models of <paramref name="types"/> (a branch's), each read with <paramref name="read"/>, by one rule (decision #195): every
    /// power and relic class with no hand-written model (SimModels.IsHandWritten: it keeps its model until a parity proof converts it) whose
    /// hooks read whole and that answers a hook the simulator runs is modelled from its code. A class that read whole at the reviewed builds
    /// (<see cref="ReadWholeAtReview"/>) and doesn't now is an unread spec, with why (ModelSpec.Unread: every simulation it's in is unknown,
    /// decision #92), told to <paramref name="unread"/>; any other that doesn't read whole is a gap the scan lists (HookProgramTests), as before.
    /// A read model's tab is its commands' only where a settings row for it exists (SettingsCatalog): what's shown doesn't grow without one.
    /// </summary>
    public static List<ModelSpec> Specs(IEnumerable<Type> types, Func<MethodBase, List<CodeOp>> read, Action<string, string>? unread = null)
    {
        var all = types.ToList();
        var specs = new List<ModelSpec>();
        foreach (var type in HookReader.Powers(all).Concat(HookReader.Relics(all)).OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            if (SimModels.IsHandWritten(type.Name)) continue;
            ModelReading reading;
            try { reading = HookReader.Read(type, read); }
            catch (Exception exception)
            {
                reading = new ModelReading(new ModelSpec(type.Name, ModelKind.Power, Array.Empty<HookProgram>()), new[] { new HookReading("(the class)", $"gap: not read ({exception.GetType().Name}: {exception.Message})") });
            }
            if (reading.Whole && reading.Spec.Hooks.Count == 0) continue;
            // Read whole, and the simulator can compile it (a command on a creature its engine functions name): modelled from its code.
            if (reading.Whole)
            {
                if (Compiles(reading.Spec) is not { } notCompiled)
                {
                    // Its payouts too (decision #239): a class whose code pays out on a condition but has no row (Orichalcum) gets no tab from them.
                    specs.Add(HasTabRow(type.Name) ? reading.Spec : reading.Spec with { Tab = null, Outcome = null, Payouts = null, TermActs = false });
                    continue;
                }
                reading = reading with { Hooks = reading.Hooks.Append(new HookReading("(compiled)", "gap: " + notCompiled)).ToList() };
            }
            if (!ReadWholeAtReview.Contains(type.Name)) continue;
            var why = $"{type.Name}'s hooks couldn't be read from the game's code (a game update?): what it does isn't simulated ({string.Join("; ", reading.Gaps)})";
            specs.Add(new ModelSpec(type.Name, reading.Spec.Kind, Array.Empty<HookProgram>()) { Unread = why });
            unread?.Invoke(type.Name, why);
        }
        // The orbs (decision #214): every orb class read whole is the engine's to run; one read whole at the reviewed builds that isn't now is unread.
        foreach (var type in HookReader.Orbs(all).OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            ModelReading reading;
            try { reading = HookReader.ReadOrb(type, read); }
            catch (Exception exception)
            {
                reading = new ModelReading(new ModelSpec(type.Name, ModelKind.Orb, Array.Empty<HookProgram>()), new[] { new HookReading("(the class)", $"gap: not read ({exception.GetType().Name}: {exception.Message})") });
            }
            if (reading.Whole && Compiles(reading.Spec) is null)
            {
                specs.Add(reading.Spec);
                continue;
            }
            if (!ReadWholeAtReview.Contains(type.Name)) continue;
            var why = $"{type.Name}'s code couldn't be read from the game (a game update?): what it does isn't simulated ({string.Join("; ", reading.Gaps)})";
            specs.Add(new ModelSpec(type.Name, ModelKind.Orb, Array.Empty<HookProgram>()) { Orb = HookReader.OrbKindOf(type.Name), Unread = why });
            unread?.Invoke(type.Name, why);
        }
        // The enchantments (decision #217): one read whole with a program to run is the engine's to run on its card's plays; one read whole at the
        // reviewed builds that isn't now is unread.
        foreach (var type in HookReader.Enchantments(all).OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            ModelReading reading;
            try { reading = HookReader.ReadEnchantment(type, read); }
            catch (Exception exception)
            {
                reading = new ModelReading(new ModelSpec(type.Name, ModelKind.Enchantment, Array.Empty<HookProgram>()), new[] { new HookReading("(the class)", $"gap: not read ({exception.GetType().Name}: {exception.Message})") });
            }
            // One with nothing to run but a fact of its card the engine reads (Perfect Fit's card on top when shuffled) is kept for that fact.
            if (reading.Whole && reading.Spec.Hooks.Count == 0 && !reading.Spec.OnTopWhenShuffled) continue;
            if (reading.Whole && Compiles(reading.Spec) is null)
            {
                specs.Add(reading.Spec);
                continue;
            }
            if (!ReadWholeAtReview.Contains(type.Name)) continue;
            var why = $"{type.Name}'s code couldn't be read from the game (a game update?): what it does isn't simulated ({string.Join("; ", reading.Gaps)})";
            specs.Add(new ModelSpec(type.Name, ModelKind.Enchantment, Array.Empty<HookProgram>()) { Unread = why });
            unread?.Invoke(type.Name, why);
        }
        // The afflictions (decision #225): one read whole is kept for the record (a fact of its card, nothing to run); one read whole at the reviewed
        // builds that isn't now is unread, and a card with it plays unknown.
        foreach (var type in HookReader.Afflictions(all).OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            ModelReading reading;
            try { reading = HookReader.ReadAffliction(type, read); }
            catch (Exception exception)
            {
                reading = new ModelReading(new ModelSpec(type.Name, ModelKind.Affliction, Array.Empty<HookProgram>()), new[] { new HookReading("(the class)", $"gap: not read ({exception.GetType().Name}: {exception.Message})") });
            }
            if (reading.Whole)
            {
                specs.Add(reading.Spec);
                continue;
            }
            if (!ReadWholeAtReview.Contains(type.Name)) continue;
            var why = $"{type.Name}'s code couldn't be read from the game (a game update?): what it does isn't simulated ({string.Join("; ", reading.Gaps)})";
            specs.Add(new ModelSpec(type.Name, ModelKind.Affliction, Array.Empty<HookProgram>()) { Unread = why });
            unread?.Invoke(type.Name, why);
        }
        return specs;
    }

    /// <summary>The running game's power classes' rules (PowerRule, decision #234), for SimModels (on a worker, with the models).</summary>
    public static IEnumerable<PowerRule> PowerRules() => PowerRules(Diagnostics.CoverageLedger.ModelTypes(), RuntimeCode.Read, unread: Unread);

    /// <summary>
    /// What PowerCmd does with each power class's amounts, read from its code (decision #234): whether it can go below 0, and its scaling for the
    /// players. Every power class, whatever models it (hand-written, read or none); only the ones that differ from PowerModel's own are listed. A
    /// class whose scaling can't be read is listed with why (PowerRule.Unread: a new one on an enemy in multiplayer makes the simulation unknown),
    /// told to <paramref name="unread"/>.
    /// </summary>
    public static List<PowerRule> PowerRules(IEnumerable<Type> types, Func<MethodBase, List<CodeOp>> read, Action<string, string>? unread = null)
    {
        var rules = new List<PowerRule>();
        foreach (var type in HookReader.Powers(types).OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            var rule = HookReader.RuleOf(type, read);
            if (rule.Unread is { } why) unread?.Invoke(type.Name, why);
            if (rule.Debuff || rule.AllowNegative || rule.ScaledForPlayers != null || rule.Unread != null) rules.Add(rule);
        }
        return rules;
    }

    /// <summary>Why the simulator can't compile a spec read whole (ReadModel's constructor refuses it), or null when it can.</summary>
    private static string? Compiles(ModelSpec spec)
    {
        try { _ = new ReadModel(spec); return null; }
        catch (InvalidOperationException exception) { return exception.Message; }
    }

    /// <summary>A Proc Tags row exists for the class (SettingsCatalog.Sources): a read model gets its commands' tab only then.</summary>
    internal static bool HasTabRow(string name) => Menu.SettingsCatalog.Sources.Any(source => source.Type == name && source.Tag != null && source.TagPrefix == "proc:");

    /// <summary>
    /// A class that read whole at the reviewed builds and doesn't now: kept for Report, on the game's thread (the models are read on a worker:
    /// SimModels.StartReading). Written once, by the read, before the models are published.
    /// </summary>
    private static void Unread(string name, string why) => _unread.Enqueue((name, why));

    private static readonly System.Collections.Concurrent.ConcurrentQueue<(string Name, string Why)> _unread = new();
    private static bool _logged;

    /// <summary>
    /// Reports each class read at the reviewed builds whose hooks the running game's code no longer reads (once a launch: the models are read
    /// once, and the reporter keeps one report per class), logged once. On the game's thread: at the mod's load, once the reporter starts, and
    /// once the read is over (SimWait.Models); nothing before the read is over.
    /// </summary>
    public static void Report()
    {
        if (!SimModels.Loaded) return;
        if (!_logged)
        {
            _logged = true;
            foreach (var (_, why) in _unread) ModEntry.Log.Warn($"[sim] {why}");
        }
        if (Sts2Mods.Core.Diagnostics.ErrorReporter.Instance is not { } reporter) return;
        foreach (var (name, why) in _unread)
            reporter.Mismatch("read-models", $"Model not read from the game's code: {name}", new[] { "read-models", name },
                $"{why}\nGame {Diagnostics.CoverageLedger.SafeGameVersion()}, reviewed {Diagnostics.SupportedVersions.Beta} (beta) and {Diagnostics.SupportedVersions.Main} (main). " +
                "Every simulation with it is shown as unknown until the reader reads it again (HookProgramTests on the new decompile).",
                tags: new Dictionary<string, string> { ["check"] = "read-models", ["source"] = name });
    }
}
