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
    /// The read models of the running game's classes, for SimModels. A class whose hooks read whole at the reviewed builds and don't now (a
    /// game update) is reported once, here: SimModels reads them once a launch.
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
            if (reading.Whole)
            {
                if (reading.Spec.Hooks.Count == 0) continue;
                specs.Add(reading.Spec with { Tab = HasTabRow(type.Name) ? reading.Spec.Tab : null });
                continue;
            }
            if (!ReadWholeAtReview.Contains(type.Name)) continue;
            var why = $"{type.Name}'s hooks couldn't be read from the game's code (a game update?): what it does isn't simulated ({string.Join("; ", reading.Gaps)})";
            specs.Add(new ModelSpec(type.Name, reading.Spec.Kind, Array.Empty<HookProgram>()) { Unread = why });
            unread?.Invoke(type.Name, why);
        }
        return specs;
    }

    /// <summary>A Proc Tags row exists for the class (SettingsCatalog.Sources): a read model gets its commands' tab only then.</summary>
    internal static bool HasTabRow(string name) => Menu.SettingsCatalog.Sources.Any(source => source.Type == name && source.Tag != null && source.TagPrefix == "proc:");

    /// <summary>A class that read whole at the reviewed builds and doesn't now: logged, and reported once (the reporter keeps one per class).</summary>
    private static void Unread(string name, string why)
    {
        ModEntry.Log.Warn($"[sim] {why}");
        _unread.Add((name, why));
        Report();
    }

    private static readonly List<(string Name, string Why)> _unread = new();

    /// <summary>
    /// Reports each class read at the reviewed builds whose hooks the running game's code no longer reads (once a launch: the models are read
    /// once, and the reporter keeps one report per class). Called again once the reporter exists, for a read made before it.
    /// </summary>
    public static void Report()
    {
        if (Sts2Mods.Core.Diagnostics.ErrorReporter.Instance is not { } reporter) return;
        foreach (var (name, why) in _unread)
            reporter.Mismatch("read-models", $"Model not read from the game's code: {name}", new[] { "read-models", name },
                $"{why}\nGame {Diagnostics.CoverageLedger.SafeGameVersion()}, reviewed {Diagnostics.SupportedVersions.Beta} (beta) and {Diagnostics.SupportedVersions.Main} (main). " +
                "Every simulation with it is shown as unknown until the reader reads it again (HookProgramTests on the new decompile).",
                tags: new Dictionary<string, string> { ["check"] = "read-models", ["source"] = name });
    }
}
