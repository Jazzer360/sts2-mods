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

    /// <summary>The turn's-end hooks in the order a side's turn end runs them (Hook.BeforeSideTurnEnd: VeryEarly, Early, its own; Hook.AfterSideTurnEnd, then Late).</summary>
    private static readonly string[] TurnEndRunOrder = { "BeforeSideTurnEndVeryEarly", "BeforeSideTurnEndEarly", "BeforeSideTurnEnd", "AfterSideTurnEnd", "AfterSideTurnEndLate" };

    /// <summary>The running game's turn's-end listeners the simulator doesn't follow (TurnEndGap, decision #303), for SimModels (on a worker, with the models).</summary>
    public static IEnumerable<TurnEndGap> TurnEndGaps() => TurnEndGaps(Diagnostics.CoverageLedger.ModelTypes(), RuntimeCode.Read);

    /// <summary>
    /// Every power and relic class of <paramref name="types"/> with no model (SimModels.IsHandWritten, or read whole and compiled: ReadModels.Specs)
    /// whose code acts as a side's turn ends (a turn's-end hook it overrides: HookReader.EndsATurn), with why, and for which owners it may act as the
    /// enemies' side's turn ends (decision #303). A class read whole at the reviewed builds that isn't now is unread already (every simulation it's
    /// in is unknown: Specs), so it isn't listed.
    /// </summary>
    public static List<TurnEndGap> TurnEndGaps(IEnumerable<Type> types, Func<MethodBase, List<CodeOp>> read)
    {
        var all = types.ToList();
        var gaps = new List<TurnEndGap>();
        foreach (var type in HookReader.Powers(all).Concat(HookReader.Relics(all)).OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            if (SimModels.IsHandWritten(type.Name) || ReadWholeAtReview.Contains(type.Name)) continue;
            var turnEnds = HookReader.Hooks(type).Where(hook => HookReader.EndsATurn(hook.Name)).Select(hook => hook.Name).ToHashSet(StringComparer.Ordinal);
            if (turnEnds.Count == 0) continue;
            ModelReading reading;
            try { reading = HookReader.Read(type, read); }
            catch (Exception exception)
            {
                gaps.Add(new TurnEndGap(type.Name, $"{type.Name}'s hooks couldn't be read ({exception.GetType().Name}): what it does as the enemies' turn ends isn't simulated") { ActsForAnEnemy = true, ActsForAPlayer = true });
                continue;
            }
            var compiled = reading.Whole && Compiles(reading.Spec) == null;
            if (compiled) continue; // modelled from its code: it runs there itself
            var (forEnemy, forPlayer) = (false, false);
            string? first = null;
            foreach (var hook in TurnEndRunOrder.Where(turnEnds.Contains)) // in the order a turn's end runs them
            {
                var hookReading = reading.Hooks.FirstOrDefault(each => each.Hook == hook);
                bool enemy, player;
                if (hookReading is not { Translated: true } || !Enum.TryParse<HookName>(hook, out var name))
                    (enemy, player) = (hookReading?.NotRun == null, hookReading?.NotRun == null); // a body not read acts for both
                else if (reading.Spec.Of(name) is not { } program) (enemy, player) = (false, false); // read: nothing it runs there
                else (enemy, player) = (ActsAtTheirTurnEnd(program, enemyOwner: true), ActsAtTheirTurnEnd(program, enemyOwner: false));
                if ((enemy || player) && first == null) first = hookReading is { Read: false } ? $"{hook} ({hookReading.Gap})" : $"{hook}, its other hooks not read ({string.Join("; ", reading.Gaps.Take(1))})";
                forEnemy |= enemy;
                forPlayer |= player;
            }
            var why = first != null ? $"{type.Name} acts as the enemies' turn ends and isn't simulated there: {first}"
                : $"{type.Name} isn't modelled, and its turn's-end hooks never act as the enemies' turn ends";
            gaps.Add(new TurnEndGap(type.Name, why) { ActsForAnEnemy = forEnemy, ActsForAPlayer = forPlayer });
        }
        return gaps;
    }

    /// <summary>
    /// Some step of a read turn's-end <paramref name="program"/> may run as the enemies' side's turn ends for an owner of that side or the players':
    /// its condition isn't known to fail there, the side ending the enemies' (HookFact.EnemiesTurn) and its owner taking part only when an enemy
    /// (HookFact.TakesPart, OwnersTurn, CreatureOnEndingSide of the owner).
    /// </summary>
    private static bool ActsAtTheirTurnEnd(HookProgram program, bool enemyOwner) => program.Steps.Any(step => Holds(step.When, enemyOwner, enemiesSide: true) != 0m);

    /// <summary>The hooks of the players' next turn's start, in the order it runs them (CombatManager.StartTurn, SetupPlayerTurn, the AutoPrePlay phase).</summary>
    private static readonly string[] TurnStartRunOrder =
    {
        "BeforeSideTurnStart", "AfterBlockCleared", "AfterEnergyReset", "AfterEnergyResetLate", "BeforeHandDraw", "BeforeHandDrawLate",
        "AfterPlayerTurnStartEarly", "AfterPlayerTurnStart", "AfterPlayerTurnStartLate", "AfterSideTurnStart", "AfterSideTurnStartLate", "AfterAutoPrePlayPhaseEntered",
    };

    /// <summary>The running game's listeners the players' next turn's start doesn't follow (TurnStartGap, decision #306), for SimModels (on a worker, with the models).</summary>
    public static IEnumerable<TurnStartGap> TurnStartGaps() => TurnStartGaps(Diagnostics.CoverageLedger.ModelTypes(), RuntimeCode.Read);

    /// <summary>
    /// Every power and relic class of <paramref name="types"/> whose code acts as the players' next turn starts (a turn's-start hook it overrides:
    /// TurnStartRunOrder) and that a projection doesn't follow there, with why, and for which owners it may act (decision #306): one with no model
    /// (not hand-written, not read whole and compiled), or a hand-written one whose model doesn't answer that hook. A hook that doesn't read acts
    /// for both kinds of owner; a read one acts unless every step waits on what doesn't hold as the players' turn starts (an enemy owner taking
    /// part, the enemies' side: HookFact.TakesPart, EnemiesTurn; the hook's player its owner: EventIsOwners). A class read whole at the reviewed
    /// builds that isn't now is unread already (every simulation it's in is unknown: Specs), so it isn't listed.
    /// </summary>
    public static List<TurnStartGap> TurnStartGaps(IEnumerable<Type> types, Func<MethodBase, List<CodeOp>> read)
    {
        var all = types.ToList();
        var gaps = new List<TurnStartGap>();
        foreach (var type in HookReader.Powers(all).Concat(HookReader.Relics(all)).OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            var handWritten = SimModels.HandWritten(type.Name);
            if (handWritten == null && ReadWholeAtReview.Contains(type.Name)) continue;
            var starts = HookReader.Hooks(type).Select(hook => hook.Name).Where(TurnStartRunOrder.Contains).ToHashSet(StringComparer.Ordinal);
            if (starts.Count == 0) continue;
            ModelReading reading;
            try { reading = HookReader.Read(type, read); }
            catch (Exception exception)
            {
                gaps.Add(new TurnStartGap(type.Name, $"{type.Name}'s hooks couldn't be read ({exception.GetType().Name}): what it does as your turn starts isn't simulated") { ActsForAnEnemy = true, ActsForAPlayer = true });
                continue;
            }
            if (handWritten == null && reading.Whole && Compiles(reading.Spec) == null) continue; // modelled from its code: it runs there itself
            var (forEnemy, forPlayer) = (false, false);
            string? first = null;
            foreach (var hook in TurnStartRunOrder.Where(starts.Contains))
            {
                if (handWritten != null && handWritten.Overrides(hook)) continue; // its hand-written model answers it
                var hookReading = reading.Hooks.FirstOrDefault(each => each.Hook == hook);
                bool enemy, player;
                if (hookReading is not { Translated: true } || !Enum.TryParse<HookName>(hook, out var name))
                    (enemy, player) = (hookReading?.NotRun == null, hookReading?.NotRun == null); // a body not read acts for both
                else if (reading.Spec.Of(name) is not { } program) (enemy, player) = (false, false); // read: nothing it runs there
                else (enemy, player) = (ActsAsYourTurnStarts(program, enemyOwner: true), ActsAsYourTurnStarts(program, enemyOwner: false));
                if ((enemy || player) && first == null)
                    first = hookReading is { Read: false } ? $"{hook} ({hookReading.Gap})" : hookReading is { Unknown: { } unknown } ? $"{hook} ({unknown})"
                        : handWritten != null ? $"{hook}, which its hand-written model doesn't answer" : $"{hook}, its other hooks not read ({string.Join("; ", reading.Gaps.Take(1))})";
                forEnemy |= enemy;
                forPlayer |= player;
            }
            if (handWritten != null && !forEnemy && !forPlayer) continue; // answered, or never acting there
            var why = first != null ? $"{type.Name} acts as your turn starts and isn't simulated there: {first}"
                : $"{type.Name} isn't modelled, and its turn's-start hooks never act as your turn starts";
            gaps.Add(new TurnStartGap(type.Name, why) { ActsForAnEnemy = forEnemy, ActsForAPlayer = forPlayer });
        }
        return gaps;
    }

    /// <summary>Some step of a read turn's-start <paramref name="program"/> may run as the players' next turn starts for an owner of that side or the enemies'.</summary>
    private static bool ActsAsYourTurnStarts(HookProgram program, bool enemyOwner) => program.Steps.Any(step => Holds(step.When, enemyOwner, enemiesSide: false) != 0m);

    /// <summary>
    /// A condition's value as a side's turn ends or starts (the enemies' when <paramref name="enemiesSide"/>, else the players') for an owner of a
    /// side (null: not known there).
    /// </summary>
    private static decimal? Holds(HookValue? value, bool enemyOwner, bool enemiesSide)
    {
        static decimal? Truth(bool holds) => holds ? 1m : 0m;
        switch (value)
        {
            case null: return 1m;
            case HookConstant constant: return constant.Value;
            case HookTest test: return Holds(test.Test, enemyOwner, enemiesSide);
            case HookNot not: return Holds(not.Inner, enemyOwner, enemiesSide) is { } inner ? Truth(inner == 0m) : null;
            case HookFactValue { Fact: HookFact.EnemiesTurn }: return Truth(enemiesSide);
            case HookFactValue { Fact: HookFact.TakesPart or HookFact.OwnersTurn }: return Truth(enemyOwner == enemiesSide);
            case HookFactValue { Fact: HookFact.CreatureOnEndingSide, Creature.Who: HookWho.Owner }: return Truth(enemyOwner == enemiesSide);
            // The player whose turn starts is its owner (a SetupPlayerTurn hook's player == base.Owner): never an enemy.
            case HookFactValue { Fact: HookFact.EventIsOwners } when !enemiesSide && enemyOwner: return 0m;
            case HookMath { Operation: HookOperation.And } both:
            {
                var (left, right) = (Holds(both.Left, enemyOwner, enemiesSide), Holds(both.Right, enemyOwner, enemiesSide));
                return left == 0m || right == 0m ? 0m : left != null && right != null ? 1m : null;
            }
            case HookMath { Operation: HookOperation.Or } either:
            {
                var (left, right) = (Holds(either.Left, enemyOwner, enemiesSide), Holds(either.Right, enemyOwner, enemiesSide));
                return left is { } leftValue && leftValue != 0m || right is { } rightValue && rightValue != 0m ? 1m : left == 0m && right == 0m ? 0m : null;
            }
            case HookMath { Operation: HookOperation.Equal or HookOperation.NotEqual } compare:
            {
                if (Holds(compare.Left, enemyOwner, enemiesSide) is not { } left || Holds(compare.Right, enemyOwner, enemiesSide) is not { } right) return null;
                return Truth((left == right) == (compare.Operation == HookOperation.Equal));
            }
            default: return null;
        }
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
