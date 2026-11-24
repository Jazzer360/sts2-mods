using System.Collections.Immutable;
using System.Reflection;
using SpireQoL.Procs.Programs;

namespace SpireQoL.Sim;

/// <summary>One hook body of a power or a relic as read (decision #195): translated into the simulator's program, or a named gap with why.</summary>
public sealed record HookReading(string Hook, string? Gap)
{
    /// <summary>What the reader would need to read the body, where it stopped (set with every gap: decision #195, round eight).</summary>
    public ReaderCapability? Capability { get; init; }

    /// <summary>A reason the simulator never runs this hook body (outside fights, past the simulated turns): nothing of it to translate.</summary>
    public string? NotRun { get; init; }

    /// <summary>Read: translated, or never run in a simulation for a reason given.</summary>
    public bool Read => Gap == null;

    /// <summary>Translated into the simulator's program (not only never run).</summary>
    public bool Translated => Gap == null && NotRun == null;

    public override string ToString() => Hook + (Gap != null ? ": " + Gap : NotRun != null ? ": not run: " + NotRun : ": read");
}

/// <summary>A power's or a relic's hooks as read from its code: its program (ModelSpec), and each hook body read or a named gap.</summary>
public sealed record ModelReading(ModelSpec Spec, IReadOnlyList<HookReading> Hooks)
{
    /// <summary>Every hook body is read: the class can be modelled from its code alone.</summary>
    public bool Whole => Hooks.All(hook => hook.Read);

    public IEnumerable<string> Gaps => Hooks.Where(hook => !hook.Read).Select(hook => hook.ToString());
}

/// <summary>
/// Reads a power's or a relic's hooks (the game's virtual methods it overrides) into the simulator's hook programs (decision #195): each body
/// read by ProgramReader, each command translated once for every model that calls it (the commands' translators: GainBlock, Damage, Draw,
/// GainEnergy, Apply, a power's own removal), each test of the hook's parameters translated once into a HookFact (HookFacts), the values
/// (base.Amount, DynamicVars, constants, arithmetic) as expressions, and a Modify hook's returns as the value of the first way that holds.
/// Works on any branch's types (the loaded game's, or an assembly file's in the tests): types and members are matched by name.
/// </summary>
public static class HookReader
{
    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>The commands a hook's code calls that are translated, and into what (HookReaderTests holds every command every power and relic calls to this or a gap).</summary>
    internal static readonly IReadOnlyDictionary<string, string> CommandsTranslated = new Dictionary<string, string>
    {
        ["CreatureCmd.GainBlock"] = "GainBlockStep: block for the owner through the block pipeline (its amount and props, or its BlockVar's)",
        ["CreatureCmd.Damage"] = "DamageStep: damage to every hittable enemy, a random one (a branch each), the creature a power landed on or the owner, from the owner or no one",
        ["CreatureCmd.Heal"] = "HealStep: the owner heals, up to its max HP",
        ["CardPileCmd.Draw"] = "DrawStep: cards drawn by the owner, each a card event",
        ["PlayerCmd.GainEnergy"] = "GainEnergyStep: energy for the owner, each a card event",
        ["PlayerCmd.GainStars"] = "GainStarsStep: stars for the owner, one stars-gained event (as a card's)",
        ["PowerCmd.Apply"] = "ApplyPowerStep: the power on the owner, every hittable enemy or the creature a power landed on, applied by the owner",
        ["PowerCmd.Remove"] = "RemovePowerStep: the power itself goes (PowerCmd.Remove(this))",
        ["PowerCmd.Decrement"] = "DecrementPowerStep: the power itself goes down by one (PowerCmd.Decrement(this))",
        ["PowerCmd.TickDownDuration"] = "DecrementPowerStep: the power itself goes down by one (PowerCmd.TickDownDuration(this))",
        ["OrbCmd.Channel"] = "ChannelStep: an orb of the kind named channeled for the owner",
    };

    /// <summary>
    /// The hooks the simulator answers with a read program (HookName), and the hooks it never runs for any model, with why: outside the turns a
    /// simulation covers, or not combat at all. Any other hook a class overrides is a named gap.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> NotRun = new Dictionary<string, string>
    {
        ["InitInternalData"] = "makes the model's own data when it's added: its fields start as the adapter reads them",
        ["AfterCombatEnd"] = "after the fight: no prediction reaches it",
        ["AfterCombatVictory"] = "after the fight: no prediction reaches it",
        ["AfterCombatVictoryEarly"] = "after the fight: no prediction reaches it",
        ["BeforeCombatStart"] = "before the fight: the state is read after it",
        ["BeforeCombatStartLate"] = "before the fight: the state is read after it",
        ["AfterRoomEntered"] = "entering a room: outside the fight's turns",
        ["BeforeRoomEntered"] = "entering a room: outside the fight's turns",
        ["AfterObtained"] = "when the relic is obtained: outside the fight's turns",
    };

    /// <summary>
    /// The hooks that run only as the players' turn starts (CombatManager.SetupPlayerTurn, for the players starting a turn): after a simulation's
    /// horizon, which ends with the enemy turn, whatever their bodies do.
    /// </summary>
    private static readonly HashSet<string> PlayersTurnStartHooks = new(StringComparer.Ordinal) { "AfterPlayerTurnStartEarly", "AfterPlayerTurnStart", "AfterPlayerTurnStartLate", "AfterEnergyReset", "AfterEnergyResetLate", "BeforeHandDraw", "BeforeHandDrawLate" };

    /// <summary>
    /// The hooks that run as either side's turn starts (CombatManager.StartTurn): a simulation reaches the enemies' (HookName.BeforeSideTurnStart,
    /// AfterSideTurnStart, AfterSideTurnStartLate: its participants the enemies). A relic's body whose every command and store waits on its owner,
    /// a player, taking part acts only as the players' next turn starts: past the horizon.
    /// </summary>
    private static readonly HashSet<string> SideTurnStartHooks = new(StringComparer.Ordinal) { "BeforeSideTurnStart", "AfterSideTurnStart", "AfterSideTurnStartLate" };

    /// <summary>The hook runs as a side's turn starts (the side the simulator starts is the enemies').</summary>
    private static bool StartsATurn(string hook) => SideTurnStartHooks.Contains(hook);

    /// <summary>A collection's methods that only ask (they change nothing of the model's own data).</summary>
    private static readonly HashSet<string> CollectionQueries = new(StringComparer.Ordinal)
    {
        "Contains", "ContainsKey", "ContainsValue", "TryGetValue", "get_Count", "get_Item", "get_Keys", "get_Values", "GetEnumerator", "IndexOf", "Peek", "ToArray",
    };

    /// <summary>The virtual methods a class overrides that aren't hooks: how it looks, its text, its numbers' display.</summary>
    private static readonly HashSet<string> NotHooks = new(StringComparer.Ordinal) { "ToString", "Equals", "GetHashCode", "DeepCloneFields", "AfterCloned" };

    /// <summary>The game's power classes among <paramref name="types"/>.</summary>
    public static IEnumerable<Type> Powers(IEnumerable<Type> types) => types.Where(type => type.Namespace == "MegaCrit.Sts2.Core.Models.Powers" && !type.IsNested && !type.IsAbstract && Derives(type, "PowerModel"));

    /// <summary>The game's relic classes among <paramref name="types"/>.</summary>
    public static IEnumerable<Type> Relics(IEnumerable<Type> types) => types.Where(type => type.Namespace == "MegaCrit.Sts2.Core.Models.Relics" && !type.IsNested && !type.IsAbstract && Derives(type, "RelicModel"));

    /// <summary>The game's virtual methods a class overrides (its hooks), not its properties.</summary>
    public static IEnumerable<MethodInfo> Hooks(Type type) => type.GetMethods(Declared)
        .Where(method => method.IsVirtual && !method.Attributes.HasFlag(MethodAttributes.NewSlot) && !method.IsSpecialName && !NotHooks.Contains(method.Name));

    /// <summary>Reads a power's or a relic's hooks with <paramref name="read"/> (the loaded game's code, or an assembly file's).</summary>
    public static ModelReading Read(Type type, Func<MethodBase, List<CodeOp>> read)
    {
        var kind = Derives(type, "RelicModel") ? ModelKind.Relic : ModelKind.Power;
        var reader = new Translation(type, kind, read);
        var programs = new List<HookProgram>();
        var readings = new List<HookReading>();
        foreach (var hook in Hooks(type).OrderBy(method => method.Name, StringComparer.Ordinal))
        {
            var (program, gap, notRun) = reader.Hook(hook);
            readings.Add(new HookReading(hook.Name, gap?.Text) { NotRun = notRun, Capability = gap?.Capability });
            if (program != null) programs.Add(program);
        }
        // One of its own values its code stores (DynamicVars["CardsLeft"].BaseValue--: Panache's count) is its state: every read of it reads
        // the state, from the canonical value for a new instance.
        // Its own code may set it outside its hooks too (The Bomb's SetDamage, called as it lands: each copy its own Damage): state as well. A
        // power kept in copies has each of its whole values per copy (each instance's own DynamicVars: Monologue's Strength, which its card sets
        // as it lands); a value that isn't whole (Guarded's 0.5) is the class's (the simulator's fields keep whole numbers).
        var copied = kind == ModelKind.Power && reader.Copies() != PowerCopies.One;
        var storedVars = new ModelSpec(type.Name, kind, programs).Steps().OfType<StoreFieldStep>().Select(store => store.Field)
            .Where(field => field.StartsWith(VarFieldPrefix, StringComparison.Ordinal)).Concat(reader.VarsStoredOutsideHooks())
            .Concat(copied ? new ModelSpec(type.Name, kind, programs).Values().OfType<HookVar>().Where(variable => !variable.Key.EndsWith(".Props", StringComparison.Ordinal) && variable.Canonical == decimal.Truncate(variable.Canonical))
                .Select(variable => VarFieldPrefix + variable.Key) : Array.Empty<string>())
            .ToHashSet(StringComparer.Ordinal);
        if (storedVars.Count > 0) programs = programs.Select(program => VarsAsState(program, storedVars)).ToList();
        // A store to a field no program reads changes only what the game shows (Kunai's IsActivating): left out.
        var fieldsRead = new ModelSpec(type.Name, kind, programs).Values().OfType<HookField>().Select(field => field.Field).ToHashSet(StringComparer.Ordinal);
        programs = programs.Select(program => program with { Steps = program.Steps.Where(step => step is not StoreFieldStep store || fieldsRead.Contains(store.Field)).ToList() })
            .Where(program => program.Returns != null || program.Steps.Count > 0).ToList();
        var helpsOwner = HelpsOwnerOf(programs);
        var hurtsOwner = HurtsOwnerOf(programs);
        var outcome = OutcomeOf(programs);
        // A crossed tab carries no amount (only the four core resources' plain tabs do: decision #132): a trigger, as it is for every owner of a
        // class one owner's is crossed for (a teammate's Sneaky keeps the trigger an enemy's would have). A tab judged on the outcome is its only one.
        var spec = new ModelSpec(type.Name, kind, programs)
        {
            Tab = outcome != null ? null : (helpsOwner || hurtsOwner) && TabOf(programs) != null ? TabKind.Trigger : TabOf(programs),
            HelpsOwner = helpsOwner, HurtsOwner = hurtsOwner, Outcome = outcome,
            Copies = kind == ModelKind.Power ? reader.Copies() : PowerCopies.One,
        };
        return new ModelReading(spec, readings);
    }

    /// <summary>
    /// A model of a kind the engine runs no read program for (an enchantment, an affliction, a potion, an orb: decision #195's last kinds): each of
    /// its hook bodies read as far as the reader goes, to its first gap, or, read whole, the engine running no read effect of its kind
    /// (ReaderCapability.EffectNotRun). For the measurement (HookGapTests): nothing runs these readings.
    /// </summary>
    public static IReadOnlyList<HookReading> ReadEffects(Type type, Func<MethodBase, List<CodeOp>> read)
    {
        var reader = new Translation(type, ModelKind.Power, read);
        return Hooks(type).OrderBy(method => method.Name, StringComparer.Ordinal).Select(reader.Effect).ToList();
    }

    /// <summary>The prefix of one of a model's own values kept as its state ("Var.CardsLeft"), where its code stores it.</summary>
    public const string VarFieldPrefix = HookField.VarPrefix;

    /// <summary>A program with each read of a value its model stores (HookVar) read as that state (HookField "Var.key", from its canonical value).</summary>
    private static HookProgram VarsAsState(HookProgram program, IReadOnlySet<string> stored)
    {
        HookValue? Map(HookValue? value) => value switch
        {
            null => null,
            HookVar variable when stored.Contains(VarFieldPrefix + variable.Key) => new HookField(VarFieldPrefix + variable.Key) { Canonical = variable.Canonical },
            HookMath math => math with { Left = Map(math.Left)!, Right = Map(math.Right)! },
            HookNot not => not with { Inner = Map(not.Inner)! },
            HookTest test => test with { Test = Map(test.Test)! },
            HookWays ways => ways with { Ways = ways.Ways.Select(way => new HookWay(Map(way.When)!, Map(way.Value)!)).ToList() },
            _ => value,
        };
        HookStep Step(HookStep step) => (step switch
        {
            GainBlockStep gain => gain with { Amount = Map(gain.Amount)!, Props = Map(gain.Props)! },
            DamageStep damage => damage with { Amount = Map(damage.Amount)!, Props = Map(damage.Props)! },
            DrawStep draw => draw with { Count = Map(draw.Count)! },
            GainEnergyStep energy => energy with { Amount = Map(energy.Amount)! },
            GainStarsStep stars => stars with { Amount = Map(stars.Amount)! },
            HealStep heal => heal with { Amount = Map(heal.Amount)! },
            ApplyPowerStep apply => apply with { Amount = Map(apply.Amount)! },
            StoreFieldStep store => store with { Value = Map(store.Value)! },
            BindStep bind => bind with { Value = Map(bind.Value)! },
            RepeatStep repeat => repeat with { Times = Map(repeat.Times)!, Steps = repeat.Steps.Select(Step).ToList() },
            _ => step,
        }) with { When = Map(step.When) };
        return program with { Steps = program.Steps.Select(Step).ToList(), Returns = Map(program.Returns) };
    }

    /// <summary>
    /// A tab judged on the outcome, from what its AfterDeath's steps wait on: its owner's death (KillsIt), another creature's on its owner's
    /// side (KillsAnother: Crab Rage), one on the other side (KillsOne: Gremlin Horn). Null when it has no AfterDeath step, or one on
    /// none of these.
    /// </summary>
    internal static HookOutcome? OutcomeOf(IReadOnlyList<HookProgram> programs)
    {
        static bool DeadAndOwner(HookFactValue fact) => fact.Fact == HookFact.SameSide && fact.Creature is { } one && fact.Other is { } other
            && new[] { one.Who, other.Who }.OrderBy(who => who).SequenceEqual(new[] { HookWho.Owner, HookWho.Dead }) && !one.PetOwner && !other.PetOwner;
        foreach (var step in new ModelSpec("", ModelKind.Power, programs.Where(program => program.Hook == HookName.AfterDeath).ToList()).Steps())
        {
            if (Requires(step.When, fact => fact.Fact == HookFact.DeadIsOwner, holds: true)) return HookOutcome.KillsIt;
            if (Requires(step.When, DeadAndOwner, holds: true)) return HookOutcome.KillsAnother;
            if (Requires(step.When, DeadAndOwner, holds: false)) return HookOutcome.KillsOne;
        }
        return null;
    }

    /// <summary>The condition holds only when a fact <paramref name="matches"/> holds (or fails: <paramref name="holds"/> false): one of its ands.</summary>
    private static bool Requires(HookValue? when, Func<HookFactValue, bool> matches, bool holds) => when switch
    {
        HookFactValue fact => holds && matches(fact),
        HookNot not => Requires(not.Inner, matches, !holds),
        HookTest test => Requires(test.Test, matches, holds),
        HookMath { Operation: HookOperation.And } both when holds => Requires(both.Left, matches, holds) || Requires(both.Right, matches, holds),
        HookMath { Operation: HookOperation.Or } either when !holds => Requires(either.Left, matches, holds) || Requires(either.Right, matches, holds),
        _ => false,
    };

    /// <summary>
    /// A read model's tab, from what its commands give (decision #132's kinds): block, damage, cards drawn or energy, by its first command that
    /// gives one; a trigger for one that acts otherwise; none for a model that only changes the damage or block math.
    /// </summary>
    internal static TabKind? TabOf(IReadOnlyList<HookProgram> programs)
    {
        var steps = new ModelSpec("", ModelKind.Power, programs).Steps().ToList();
        foreach (var step in steps)
            switch (step)
            {
                case GainBlockStep: return TabKind.Block;
                case DamageStep: return TabKind.Damage;
                case DrawStep: return TabKind.Draw;
                case GainEnergyStep: return TabKind.Energy;
            }
        return steps.Any(step => step is ApplyPowerStep or ChannelStep or GainStarsStep) ? TabKind.Trigger : null;
    }

    /// <summary>
    /// A play anyone makes sets off something that helps the model's owner: a play hook's block for its owner, or a buff on it, on a condition
    /// that doesn't need the play to be its owner's (Enrage's Strength on every Skill, Sneaky's block on another's Attack). On an enemy, that's
    /// your play helping it: a crossed tab (CLAUDE.md "Plain vs crossed"); on a teammate, your play helping your side: plain (ReadModel.HarmfulFor).
    /// </summary>
    internal static bool HelpsOwnerOf(IReadOnlyList<HookProgram> programs) =>
        programs.Where(program => program.Hook is HookName.BeforeCardPlayed or HookName.AfterCardPlayed)
            .SelectMany(program => new ModelSpec("", ModelKind.Power, new[] { program }).Steps())
            .Any(step => step is GainBlockStep or ApplyPowerStep { Target: HookTarget.Owner, Debuff: false } && !Needs(step.When, HookFact.PlayIsOwners));

    /// <summary>
    /// A play sets off something that hurts the model's owner: a play hook's debuff on its owner (Tender's Strength and Dexterity
    /// down on each card you play). On a player, that's the play hurting you or your side: a crossed tab (CLAUDE.md "Plain vs crossed");
    /// on an enemy, it helps you: plain (ReadModel.HarmfulFor).
    /// </summary>
    internal static bool HurtsOwnerOf(IReadOnlyList<HookProgram> programs) =>
        programs.Where(program => program.Hook is HookName.BeforeCardPlayed or HookName.AfterCardPlayed)
            .SelectMany(program => new ModelSpec("", ModelKind.Power, new[] { program }).Steps())
            .Any(step => step is ApplyPowerStep { Target: HookTarget.Owner, Debuff: true });

    /// <summary>The condition holds only when the fact does (it's one of the condition's ands, not under a not).</summary>
    private static bool Needs(HookValue? when, HookFact fact) => when switch
    {
        HookFactValue value => value.Fact == fact,
        HookTest test => Needs(test.Test, fact),
        HookMath { Operation: HookOperation.And } both => Needs(both.Left, fact) || Needs(both.Right, fact),
        HookMath { Operation: HookOperation.Or } either => Needs(either.Left, fact) && Needs(either.Right, fact),
        _ => false,
    };

    private static bool Derives(Type type, string baseName)
    {
        for (var current = type.BaseType; current != null; current = current.BaseType)
            if (current.Name == baseName) return true;
        return false;
    }

    /// <summary>A gap in a body: what the reader or the translator can't follow, named.</summary>
    private sealed class Gap : Exception
    {
        public Gap(ReaderCapability capability, string reason) : base(reason) => Capability = capability;

        public ReaderCapability Capability { get; }
    }

    /// <summary>A gap as a hook's reading names it: its words and the capability it waits on.</summary>
    internal readonly record struct GapOf(ReaderCapability Capability, string Text);

    /// <summary>One class's translation: its hooks, each body's steps and values, with what's known about the class (its kind, its assembly).</summary>
    private sealed class Translation
    {
        private readonly Type _type;
        private readonly ModelKind _kind;
        private readonly Func<MethodBase, List<CodeOp>> _read;
        private string _hook = "";

        /// <summary>
        /// Its own values a method of its own that isn't a hook stores (The Bomb's SetDamage: DynamicVars.Damage.BaseValue = damage, which its card
        /// calls as it lands), as state fields ("Var.Damage"): each instance has its own.
        /// </summary>
        public IEnumerable<string> VarsStoredOutsideHooks()
        {
            var hooks = Hooks(_type).Select(hook => hook.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var method in _type.GetMethods(Declared).Where(method => !method.IsSpecialName && !method.Name.StartsWith('<') && !hooks.Contains(method.Name) && !method.IsVirtual))
            {
                ModelProgram program;
                try { program = ProgramReader.Read(_type, _read, method.Name); }
                catch { continue; }
                foreach (var store in program.All().OfType<StoreStep>().Where(store => store.Field.StartsWith(ProgramReader.OwnVarPrefix, StringComparison.Ordinal)))
                    yield return VarFieldPrefix + OwnVarKey(store.Field[ProgramReader.OwnVarPrefix.Length..]);
            }
        }

        /// <summary>How the power's applications are kept: its InstanceType, read from its code.</summary>
        public PowerCopies Copies() =>
            Returned(_type, "get_InstanceType") is ConstantValue instance ? EnumName(_type.Assembly, "PowerInstanceType", instance.Value) switch
            {
                "Instanced" => PowerCopies.EachApplication,
                "InstancedPerApplier" => PowerCopies.PerApplier,
                _ => PowerCopies.One,
            } : PowerCopies.One;

        public Translation(Type type, ModelKind kind, Func<MethodBase, List<CodeOp>> read)
        {
            _type = type;
            _kind = kind;
            _read = read;
        }

        /// <summary>One hook body of a kind the engine runs no read program for, read to its first gap (ReadEffects).</summary>
        public HookReading Effect(MethodInfo hook)
        {
            _hook = hook.Name;
            if (NotRun.TryGetValue(hook.Name, out var why)) return new HookReading(hook.Name, null) { NotRun = why };
            if (Diagnostics.CoverageLedger.OutOfCombatHooks.Contains(hook.Name)) return new HookReading(hook.Name, null) { NotRun = "it acts outside fights (Diagnostics/OutOfCombatHooks.txt)" };
            static HookReading Gapped(string hook, GapOf gap) => new(hook, gap.Text) { Capability = gap.Capability };
            try
            {
                if (hook.ReturnType.FullName != typeof(void).FullName && !hook.ReturnType.Name.StartsWith(nameof(Task), StringComparison.Ordinal)) _ = Pruned(Ways(ProgramReader.ReadReturnedWays(_type, hook, _read)));
                else
                {
                    var program = ProgramReader.Read(_type, _read, hook.Name);
                    if (program.Unknowns.FirstOrDefault() is { } unknown) return Gapped(hook.Name, new GapOf(ReaderCapability.ValueNotFollowed, "gap: the reader can't follow it: " + unknown));
                    if (OwnData(hook, program) is { } data) return Gapped(hook.Name, data);
                    foreach (var step in program.Steps) _ = Step(step).ToList();
                }
                return Gapped(hook.Name, new GapOf(ReaderCapability.EffectNotRun, $"gap: the engine runs no read {_type.BaseType?.Name}'s {hook.Name}"));
            }
            catch (Gap gap)
            {
                return Gapped(hook.Name, new GapOf(gap.Capability, gap.Message));
            }
            catch (Exception exception)
            {
                return Gapped(hook.Name, new GapOf(ReaderCapability.NotRead, $"gap: not read ({exception.GetType().Name}: {exception.Message})"));
            }
        }

        public (HookProgram? Program, GapOf? Gap, string? NotRun) Hook(MethodInfo hook)
        {
            _hook = hook.Name;
            if (NotRun.TryGetValue(hook.Name, out var why)) return (null, null, why);
            if (Diagnostics.CoverageLedger.OutOfCombatHooks.Contains(hook.Name)) return (null, null, "it acts outside fights (Diagnostics/OutOfCombatHooks.txt)");
            try
            {
                if (PlayersTurnStartHooks.Contains(hook.Name)) return (null, null, "it runs as the players' next turn starts, past the turns a simulation covers");
                if (StartsATurn(hook.Name) && PastTheHorizon(hook) is { } horizon)
                    return horizon.Length == 0 ? (null, null, "it acts as its owner's next turn starts, past the turns a simulation covers") : (null, new GapOf(ReaderCapability.ValueNotFollowed, horizon), null);
                if (!Enum.TryParse<HookName>(hook.Name, out var name)) return (null, new GapOf(ReaderCapabilities.OfHook(hook.Name), $"gap: the simulator runs no read model's {hook.Name}"), null);
                if (hook.ReturnType.Name is "Decimal" || hook.ReturnType.Name is "Int32" && name == HookName.ModifyCardPlayCount)
                    return (new HookProgram(name, Array.Empty<HookStep>()) { Returns = Pruned(Ways(ProgramReader.ReadReturnedWays(_type, hook, _read))) }, null, null);
                var program = ProgramReader.Read(_type, _read, hook.Name);
                if (program.Unknowns.FirstOrDefault() is { } unknown) return (null, new GapOf(ReaderCapability.ValueNotFollowed, "gap: the reader can't follow it: " + unknown), null);
                if (OwnData(hook, program) is { } data) return (null, data, null);
                var steps = new List<HookStep>();
                foreach (var step in program.Steps) steps.AddRange(Step(step));
                // A step its code runs on a side the simulator never ends (AfterSideTurnEnd of the enemies' side) never runs: left out.
                var never = steps.RemoveAll(step => step.When is HookConstant { Value: 0m });
                if (steps.Count == 0) return (null, null, never == 0 ? null : StartsATurn(hook.Name) ? "its commands wait on the players' side's turn starting, past the turns a simulation covers" : "its commands wait on the enemies' side's turn ending, which no simulation reaches");
                return (new HookProgram(name, steps), null, null);
            }
            catch (Gap gap)
            {
                return (null, new GapOf(gap.Capability, gap.Message), null);
            }
            catch (Exception exception)
            {
                return (null, new GapOf(ReaderCapability.NotRead, $"gap: not read ({exception.GetType().Name}: {exception.Message})"), null);
            }
        }

        /// <summary>
        /// The model's own data a hook's code works with beyond what the reader reads (its GetInternalData's object, a field of its class): a
        /// change to it the program shows no step for (a store to one of its fields, a call on one of its collections) changes the model's
        /// state unseen. Read: a store as a StoreStep at its place, a collection's change whose result isn't used as a DataStep at its place, a
        /// Remove whose result is tested (taken as a fact: HookFact.TakeBound); a query (Contains, Count...) changes nothing. Null: nothing unread.
        /// </summary>
        private GapOf? OwnData(MethodInfo hook, ModelProgram program)
        {
            List<CodeOp> ops;
            try { ops = _read(ProgramReader.BodyOf(hook)); }
            catch { return null; }
            var stepsAt = program.All().Where(step => step is DataStep or StoreStep).Select(step => step.Offset).ToHashSet();
            var usesData = ops.Any(instruction => instruction.IsCall && instruction.Member is { Name: "GetInternalData" });
            var nested = Procs.CardCode.NestedTypes(_type).Where(type => !type.Name.StartsWith('<')).Select(type => type.Name).ToHashSet(StringComparer.Ordinal);
            for (var index = 0; index < ops.Count; index++)
            {
                var instruction = ops[index];
                if (instruction.OpCode == System.Reflection.Emit.OpCodes.Stfld && instruction.Member is { } stored && nested.Contains(stored.DeclaringType) && !stepsAt.Contains(instruction.Offset))
                    return new GapOf(ReaderCapability.OwnData, $"gap: changes its own data ({stored.Name}) where the reader reads no store");
                if (instruction.IsCall && instruction.Member is { Kind: CodeMemberKind.Method, HasThis: true } call && usesData && call.Namespace.StartsWith("System.Collections", StringComparison.Ordinal)
                    && !CollectionQueries.Contains(call.Name) && !stepsAt.Contains(instruction.Offset)
                    && !(call.Name == "Remove" && call.ParameterCount == 2 && index + 1 < ops.Count && ops[index + 1].OpCode != System.Reflection.Emit.OpCodes.Pop))
                    return new GapOf(ReaderCapability.OwnData, $"gap: changes a collection ({call.FullName}) where the reader reads no step");
                if (instruction.IsCall && ChangesAGameObject(instruction.Member)) return new GapOf(SetOn(instruction.Member!.DeclaringType, call: true), $"gap: changes a game object outside a command ({instruction.Member!.FullName})");
                // A property set on one of the game's objects the reader reads no store for (Panache's DynamicVars["CardsLeft"].BaseValue--).
                // A text set (a StringVar's StringValue: words its tooltip shows) is what's shown, never a number anything reads.
                if (instruction.IsCall && instruction.Member is { Kind: CodeMemberKind.Method, HasThis: true } setter && setter.Name.StartsWith("set_", StringComparison.Ordinal)
                    && setter.Namespace.StartsWith("MegaCrit", StringComparison.Ordinal) && !setter.Namespace.Contains(".Nodes", StringComparison.Ordinal) && !stepsAt.Contains(instruction.Offset)
                    && setter.ParameterTypes is not ["String"])
                    return new GapOf(SetOn(setter.DeclaringType, call: false), $"gap: sets {setter.DeclaringType}.{setter.Name[4..]} where the reader reads no store");
                // A field stored on one of the game's objects (not the hook's own locals) the reader reads no store for.
                if (instruction.OpCode == System.Reflection.Emit.OpCodes.Stfld && instruction.Member is { } storedField && !storedField.DeclaringType.StartsWith('<') && !storedField.Name.StartsWith('<')
                    && !stepsAt.Contains(instruction.Offset))
                    return new GapOf(SetOn(storedField.DeclaringType, call: false), $"gap: stores {storedField.DeclaringType}.{storedField.Name} where the reader reads no store");
                // One of its own fields: a plain one (a flag, a count) is its state, read and stored as its properties are; any other (a list, a
                // card kept) is changed by calls on it the reader doesn't read.
                if (instruction.Member is { Kind: CodeMemberKind.Field } field && field.DeclaringType == _type.Name && !field.Name.StartsWith('<') && !IsPlainField(field))
                    return new GapOf(ReaderCapability.OwnData, $"gap: uses its own field ({field.Name}) directly: what a call on it changes isn't read");
            }
            return null;
        }

        /// <summary>
        /// What setting a value on a game object of this class would need: a monster's own state (its flag, its next move), a card changed, or
        /// another model's value (a power's Target, a DynamicVar's BaseValue, an affliction's Amount).
        /// </summary>
        private ReaderCapability SetOn(string declaring, bool call) =>
            _type.Assembly.GetType("MegaCrit.Sts2.Core.Models.Monsters." + declaring) != null ? ReaderCapability.MonsterStateSet
            : declaring is "CardModel" or "CardEnergyCost" ? ReaderCapability.CardChanged
            : call && Derives(_type, declaring) ? ReaderCapability.OwnMethod
            : ReaderCapability.OtherModelValueSet;

        /// <summary>What a command on a creature the reader can't act on would need: a potion's chosen target (OnUse's target), or the target's own kind.</summary>
        private ReaderCapability Other(ProgramValue creature) =>
            _hook == "OnUse" && Unstored(creature) is ParameterValue { Name: "target" } or NamedValue { Name: "target" } ? ReaderCapability.UseTarget : ReaderCapabilities.OfTarget(creature);

        /// <summary>
        /// A call that changes one of the game's objects outside a command (a card made free this turn, a keyword added): an instance method of
        /// the game's that gives nothing back and isn't a property, a command, a show or the model's own.
        /// </summary>
        private bool ChangesAGameObject(CodeMember? member) =>
            member is { Kind: CodeMemberKind.Method, HasThis: true } && member.Namespace.StartsWith("MegaCrit.Sts2.Core", StringComparison.Ordinal)
            && (member.ValueType == typeof(void).Name || member.ValueType.StartsWith("Task", StringComparison.Ordinal))
            && !member.Name.StartsWith("get_", StringComparison.Ordinal) && !member.Name.StartsWith("set_", StringComparison.Ordinal) && !member.Name.StartsWith('<') && member.Name != ".ctor"
            && member.DeclaringType != _type.Name && !member.Namespace.StartsWith("MegaCrit.Sts2.Core.Commands", StringComparison.Ordinal)
            && !member.Namespace.Contains(".Nodes", StringComparison.Ordinal) && member.Name is not ("Flash" or "InvokeDisplayAmountChanged" or "AssertMutable" or "SetResult" or "Start" or "AwaitUnsafeOnCompleted" or "SetStateMachine" or "SetException");

        /// <summary>
        /// A side's turn-start body of a relic whose every step waits on its owner, a player, taking part: it acts only as the players' next turn
        /// starts, past a simulation's horizon (""). Null: read it as the enemies' turn start runs it; a gap when the reader can't follow it.
        /// </summary>
        private string? PastTheHorizon(MethodInfo hook)
        {
            var program = ProgramReader.Read(_type, _read, hook.Name);
            if (program.Unknowns.Any()) return "gap: the reader can't follow it: " + program.Unknowns.First();
            if (!program.All().Any()) return "";
            if (_kind == ModelKind.Relic && program.All().All(step => step.Condition?.Terms is { Count: > 0 } terms && terms.All(term => term.Any(literal => literal.Holds && IsTakingPart(literal.Test)))))
                return "";
            return null;
        }

        private IEnumerable<HookStep> Step(ProgramStep step)
        {
            switch (step)
            {
                case CommandStep command:
                    var translated = Command(command);
                    return translated == null ? Array.Empty<HookStep>() : new[] { translated with { When = Condition(step.Condition) } };
                case StoreStep store:
                    // A store to its own state (a field or property of its class, a plain field of its data): simulator state its programs read (HookField).
                    // One of its own values set (DynamicVars["CardsLeft"].BaseValue = ...): its state too, under the value's key.
                    var field = store.Field.StartsWith(ProgramReader.OwnVarPrefix, StringComparison.Ordinal) ? VarFieldPrefix + OwnVarKey(store.Field[ProgramReader.OwnVarPrefix.Length..]) : store.Field;
                    // One of its own fields or properties that holds a node (Echo Form's Vfx: how it looks) is what's shown, never read by a hook.
                    if (OwnMemberType(store.Field) is { } memberType && (memberType.Namespace ?? "").Contains(".Nodes", StringComparison.Ordinal)) return Array.Empty<HookStep>();
                    return new[] { new StoreFieldStep(field, Value(store.Value)) { When = Condition(step.Condition) } };
                case DataStep data:
                    return new[] { Data(data) with { When = Condition(step.Condition) } };
                case LoopStep loop:
                {
                    // A counter loop (for i < n): its body n times, n worked out as the loop starts (the card programs read it the same way:
                    // ProgramEvaluator.Rounds). Any other loop is a gap.
                    if (loop.Items != null || loop.While is not { Terms: [[{ Holds: true, Test: CompareValue { Op: Comparison.Less or Comparison.LessOrEqual } bound }]] })
                        throw new Gap(ReaderCapability.LoopOverItems, "gap: a loop");
                    var times = Value(bound.Right);
                    if (bound.Op == Comparison.LessOrEqual) times = new HookMath(HookOperation.Add, times, new HookConstant(1m));
                    var body = loop.Body.SelectMany(Step).ToList();
                    return body.Count == 0 ? Array.Empty<HookStep>() : new[] { new RepeatStep(times, body) { When = Condition(step.Condition) } };
                }
                case FlashCall:
                {
                    // Its own Flash(): where the game shows it acting, for the play check (decision #199). On a test the reader can't translate,
                    // an unknown flash: it may flash or not, which only the play check asks (skipped there, with why), so the rest still reads.
                    HookValue? when;
                    try { when = Condition(step.Condition); }
                    catch (Gap gap) { return new HookStep[] { new FlashStep { Unknown = gap.Message } }; }
                    return new HookStep[] { new FlashStep { When = when } };
                }
                case ChoiceStep:
                    throw new Gap(ReaderCapability.Pick, "gap: a pick");
                case CreateStep create:
                    throw new Gap(ReaderCapability.CardMade, $"gap: makes a {create.Card}");
                default:
                    throw new Gap(ReaderCapability.ValueNotFollowed, $"gap: {step}");
            }
        }

        /// <summary>The key of one of its own values a store names (a DynamicVarSet property's own key, or an indexed key as written).</summary>
        private string OwnVarKey(string named) => named.StartsWith('"') ? named.Trim('"') : KeyOfProperty(named);

        /// <summary>
        /// A change to its own data: a value bound to the play in progress (Add(cardPlay.Card, value)), or taken back for its out value
        /// (Remove(cardPlay.Card, out value)); any other is a gap.
        /// </summary>
        private HookStep Data(DataStep data)
        {
            if (data.Values.Count == 0 || !IsPlayCard(data.Values[0])) throw new Gap(ReaderCapability.OwnData, $"gap: changes its own data ({data.Data}.{data.Operation}) by {(data.Values.Count > 0 ? data.Values[0] : "nothing")}");
            return data.Operation switch
            {
                "Add" when data.Values.Count == 2 => new BindStep(Value(data.Values[1])),
                "Remove" => new UnbindStep(),
                _ => throw new Gap(ReaderCapability.OwnData, $"gap: changes its own data ({data.Data}.{data.Operation})"),
            };
        }

        /// <summary>Its own data's Remove(cardPlay.Card, out value): what it bound to the play in progress, taken.</summary>
        private bool IsTakeBound(ProgramValue value) =>
            value is QueryValue { Name: "Remove", Of: QueryValue { Of: { } dataOf }, Arguments: [var key, _] } && ProgramReader.IsOwnData(dataOf) && IsPlayCard(key);

        // ---- commands

        private HookStep? Command(CommandStep command)
        {
            var member = command.Command;
            var types = member.ParameterTypes;
            var values = command.Values;
            switch (member.FullName)
            {
                case "CreatureCmd.GainBlock":
                {
                    if (!IsOwner(values[0])) throw new Gap(Other(values[0]), $"gap: block for {values[0]}");
                    if (types[1] == "BlockVar")
                    {
                        var key = VarKey(values[1]) ?? throw new Gap(ReaderCapabilities.OfValue(values[1]), $"gap: block of {values[1]}");
                        return new GainBlockStep(Var(key), Var(key + ".Props"));
                    }
                    return new GainBlockStep(Value(values[1]), Value(values[2]));
                }
                case "CreatureCmd.Damage":
                {
                    var targetAt = IndexOf(types, "Creature", "IEnumerable`1");
                    var amountAt = IndexOf(types, "Decimal", "DamageVar");
                    if (targetAt < 0 || amountAt < 0) throw new Gap(ReaderCapability.CommandNotTranslated, $"gap: damage overload {string.Join(",", types)}");
                    var dealerAt = IndexOf(types, from: amountAt + 1, "Creature");
                    HookValue amount, props;
                    if (types[amountAt] == "DamageVar")
                    {
                        var key = VarKey(values[amountAt]) ?? throw new Gap(ReaderCapabilities.OfValue(values[amountAt]), $"gap: damage of {values[amountAt]}");
                        (amount, props) = (Var(key), Var(key + ".Props"));
                    }
                    else (amount, props) = (Value(values[amountAt]), Value(values[amountAt + 1]));
                    var noDealer = dealerAt < 0 || values[dealerAt] is NullValue;
                    if (!noDealer && !IsOwner(values[dealerAt])) throw new Gap(ReaderCapability.CommandOnAnotherCreature, $"gap: damage dealt by {values[dealerAt]}");
                    // Damage the simulator's procs deal: to every hittable enemy, a roll over them, the creature a power landed on, or its owner.
                    var damaged = Targets(values[targetAt]);
                    if (damaged is not (HookTarget.AllEnemies or HookTarget.RandomEnemy or HookTarget.PowerOwner or HookTarget.Owner)) throw new Gap(ReaderCapability.CommandOnAnotherCreature, $"gap: damage to {values[targetAt]}");
                    return new DamageStep(damaged, amount, props) { NoDealer = noDealer };
                }
                case "CardPileCmd.Draw":
                {
                    var playerAt = IndexOf(types, "Player");
                    if (playerAt < 0 || !IsOwner(values[playerAt])) throw new Gap(ReaderCapability.CommandOnAnotherCreature, "gap: a draw for someone else");
                    var countAt = IndexOf(types, "Decimal");
                    return new DrawStep(countAt < 0 ? new HookConstant(1m) : Value(values[countAt]));
                }
                case "CreatureCmd.Heal":
                    if (!IsOwner(values[0])) throw new Gap(Other(values[0]), $"gap: a heal for {values[0]}");
                    return new HealStep(Value(values[1]));
                case "PlayerCmd.GainEnergy":
                    if (!IsOwner(values[1])) throw new Gap(ReaderCapability.CommandOnAnotherCreature, "gap: energy for someone else");
                    return new GainEnergyStep(Value(values[0]));
                case "PlayerCmd.GainStars":
                    if (!IsOwner(values[1])) throw new Gap(ReaderCapability.Stars, "gap: stars for someone else");
                    return new GainStarsStep(Value(values[0]));
                case "PowerCmd.Apply":
                {
                    var power = member.GenericArguments.Single();
                    var targetAt = IndexOf(types, "Creature", "IEnumerable`1");
                    var amountAt = IndexOf(types, "Decimal");
                    var applierAt = IndexOf(types, from: amountAt + 1, "Creature");
                    // Its applier: its owner, the applier the power keeps (base.Applier), or no one (null).
                    HookCreature? by = applierAt < 0 ? throw new Gap(ReaderCapability.CommandOnAnotherCreature, $"gap: {power} applied by someone else")
                        : IsOwner(values[applierAt]) ? null
                        : IsApplier(values[applierAt]) ? new HookCreature(HookWho.Applier)
                        : values[applierAt] is NullValue ? new HookCreature(HookWho.NoOne)
                        : throw new Gap(ReaderCapability.CommandOnAnotherCreature, $"gap: {power} applied by someone else");
                    var amount = Value(values[amountAt]);
                    if (Targets(values[targetAt]) is not (HookTarget.Owner or HookTarget.PowerOwner or HookTarget.AllEnemies or HookTarget.HitTarget)) throw new Gap(Other(values[targetAt]), $"gap: {power} on {values[targetAt]}");
                    // A temporary Strength or Dexterity power (Helical Dart's) lands as the Strength or Dexterity its BeforeApplied applies, by its sign
                    // (TemporaryStrengthPower, TemporaryDexterityPower: IsPositive), as the card programs land it (SimAdapter.PowersFrom).
                    if (TemporaryOf(power) is { } underlying)
                    {
                        var negative = LandsAsDebuff(power, amount);
                        return new ApplyPowerStep(underlying, Targets(values[targetAt]), negative ? new HookMath(HookOperation.Multiply, amount, new HookConstant(-1m)) : amount) { Debuff = negative, By = by };
                    }
                    return new ApplyPowerStep(power, Targets(values[targetAt]), amount)
                    {
                        Debuff = LandsAsDebuff(power, amount), DebuffWhenNegative = amount is not HookConstant && AllowsNegative(power), By = by,
                    };
                }
                case "OrbCmd.Channel":
                {
                    var playerAt = IndexOf(types, "Player");
                    if (playerAt < 0 || !IsOwner(values[playerAt])) throw new Gap(ReaderCapability.CommandOnAnotherCreature, "gap: an orb channeled for someone else");
                    var orb = member.GenericArguments.SingleOrDefault() ?? throw new Gap(ReaderCapability.Orbs, "gap: an orb made first");
                    return Enum.TryParse<OrbKind>(orb.EndsWith("Orb", StringComparison.Ordinal) ? orb[..^3] : orb, out var kind) && kind is not (OrbKind.Unknown or OrbKind.Random)
                        ? new ChannelStep(kind) : throw new Gap(ReaderCapability.Orbs, $"gap: channels a {orb}");
                }
                case "PowerCmd.Remove" or "PowerCmd.Decrement" or "PowerCmd.TickDownDuration":
                    if (values[0] is not NamedValue { Name: "this" }) throw new Gap(ReaderCapability.CommandOnAnotherPower, $"gap: {member.FullName} of another power");
                    return member.Name == "Remove" ? new RemovePowerStep() : new DecrementPowerStep();
                default:
                    throw new Gap(ReaderCapabilities.OfCommand(member.FullName), $"gap: {member} isn't translated");
            }
        }

        private readonly HashSet<string> _inlining = new(StringComparer.Ordinal);

        /// <summary>
        /// One of its own properties: state when it can be set (a counter it stores: HookField), else what its getter works out (TuningFork's
        /// SkillsThreshold, its Cards value), read from the getter's code.
        /// </summary>
        private HookValue OwnProperty(string name)
        {
            var property = _type.GetProperty(name, Declared);
            if (property == null || property.SetMethod != null) return new HookField(name);
            if (property.GetMethod is not { } getter || !_inlining.Add(name)) throw new Gap(ReaderCapability.ValueNotFollowed, $"gap: the property {name}");
            try { return Value(ProgramReader.ReadReturnedValue(_type, getter, _read) ?? throw new Gap(ReaderCapability.ValueNotFollowed, $"gap: the property {name}")); }
            finally { _inlining.Remove(name); }
        }

        /// <summary>A value the code holds as a whole number (an int: a constant, an int field or property, arithmetic over them), not a decimal.</summary>
        private static bool IsWhole(ProgramValue value) => value switch
        {
            StoredValue stored => IsWhole(stored.Value),
            ConstantValue => true,
            QueryValue { ValueType: "Int32" } => true,
            ArithmeticValue arithmetic => IsWhole(arithmetic.Left) && IsWhole(arithmetic.Right),
            _ => false,
        };

        /// <summary>The type of one of its own fields or properties (null: not one of its own, or its data's).</summary>
        private Type? OwnMemberType(string name)
        {
            for (var type = _type; type != null && type.Namespace?.StartsWith("MegaCrit", StringComparison.Ordinal) == true; type = type.BaseType)
                if ((type.GetProperty(name, Declared)?.PropertyType ?? type.GetField(name, Declared)?.FieldType) is { } found) return found;
            return null;
        }

        /// <summary>A plain field of its own class (a flag, a count: bool, int, decimal), whose loads and stores are all there is to it.</summary>
        private static bool IsPlainField(CodeMember field) => field.ValueType is "Boolean" or "Int32" or "Decimal";

        /// <summary>One of its own plain fields read (this._triggeredThisTurn): the field's name, else null.</summary>
        private string? OwnPlainField(ProgramValue value) =>
            value is QueryValue { Of: NamedValue { Name: "this" }, IsProperty: false, Arguments.Count: 0, Name: var name, Type: var declaring, ValueType: "Boolean" or "Int32" or "Decimal" }
            && declaring == _type.Name && _type.GetField(name, Declared) != null ? name : null;

        /// <summary>A field or property its own class declares (not a base class's: their state is the game's, not the model's).</summary>
        private bool IsOwnMember(string name) =>
            _type.GetProperty(name, Declared) != null || _type.GetField(name, Declared) != null;

        private static int IndexOf(IReadOnlyList<string> types, params string[] names) => IndexOf(types, from: 0, names);

        private static int IndexOf(IReadOnlyList<string> types, int from, params string[] names)
        {
            for (var index = Math.Max(0, from); index < types.Count; index++)
                if (names.Contains(types[index])) return index;
            return -1;
        }

        /// <summary>Who a command acts on: the owner, every hittable enemy, a roll over them, or the creature a power landed on.</summary>
        private HookTarget Targets(ProgramValue value)
        {
            if (value is StoredValue stored) return Targets(stored.Value);
            // The creature a hit landed on (AfterDamageGiven's, AfterDamageReceived's target).
            if (value is ParameterValue { Name: "target" } or NamedValue { Name: "target" } && _hook is "AfterDamageGiven" or "AfterDamageReceived") return HookTarget.HitTarget;
            if (IsOwner(value)) return HookTarget.Owner;
            if (value is QueryValue { Name: "HittableEnemies" }) return HookTarget.AllEnemies;
            if (value is QueryValue { Name: "NextItem", Arguments: [QueryValue { Name: "HittableEnemies" }] }) return HookTarget.RandomEnemy;
            if (value is QueryValue { Name: "Owner", Of: NamedValue { Name: "power" } }) return HookTarget.PowerOwner;
            throw new Gap(Other(value), $"gap: acts on {value}");
        }

        /// <summary>The power a temporary Strength or Dexterity power applies as it lands (its base class's BeforeApplied), or null for any other.</summary>
        private string? TemporaryOf(string power)
        {
            for (var type = _type.Assembly.GetType("MegaCrit.Sts2.Core.Models.Powers." + power)?.BaseType; type != null; type = type.BaseType)
                switch (type.Name)
                {
                    case nameof(MegaCrit.Sts2.Core.Models.Powers.TemporaryStrengthPower): return PowerId.Strength.Name;
                    case nameof(MegaCrit.Sts2.Core.Models.Powers.TemporaryDexterityPower): return PowerId.Dexterity.Name;
                }
            return null;
        }

        /// <summary>
        /// A power lands as a debuff (Artifact takes it, PowerCmd.Apply's power.GetTypeForAmount): a debuff type, or a power that can go below 0
        /// given less than 0 (TypeOfNegative), read from the power's own code.
        /// </summary>
        private bool LandsAsDebuff(string power, HookValue amount)
        {
            var powerType = _type.Assembly.GetType("MegaCrit.Sts2.Core.Models.Powers." + power) ?? throw new Gap(ReaderCapability.ValueNotFollowed, $"gap: no power class {power}");
            var negative = amount is HookConstant { Value: < 0m };
            if (negative && Returned(powerType, "get_AllowNegative") is ConstantValue { Value: 1 }) return true;
            return Returned(powerType, "get_Type") is ConstantValue type && EnumName(powerType.Assembly, "PowerType", type.Value) == "Debuff";
        }

        /// <summary>The power can go below 0 (its AllowNegative): given less than 0, it lands as a debuff.</summary>
        private bool AllowsNegative(string power) =>
            _type.Assembly.GetType("MegaCrit.Sts2.Core.Models.Powers." + power) is { } powerType && Returned(powerType, "get_AllowNegative") is ConstantValue { Value: 1 };

        private ProgramValue? Returned(Type type, string method)
        {
            for (var current = type; current != null; current = current.BaseType)
                if (current.GetMethods(Declared).FirstOrDefault(candidate => candidate.Name == method) is { } found)
                    return ProgramReader.ReadReturnedValue(current, found, _read);
            return null;
        }

        // ---- conditions

        /// <summary>A step's condition as a value: an or of ands of its tests (null: always).</summary>
        private HookValue? Condition(ProgramCondition? condition)
        {
            if (condition == null || condition.IsAlways) return null;
            if (condition.IsNever) return new HookConstant(0m);
            HookValue? any = null;
            foreach (var term in condition.Terms)
            {
                HookValue? all = null;
                foreach (var literal in term)
                {
                    var test = Test(literal.Test);
                    // Asked once a hook run, at its place (a constant needs no asking).
                    if (test is not HookConstant) test = new HookTest(literal.At, test);
                    var holds = literal.Holds ? test : Not(test);
                    all = all == null ? holds : new HookMath(HookOperation.And, all, holds);
                }
                all ??= new HookConstant(1m);
                any = any == null ? all : new HookMath(HookOperation.Or, any, all);
            }
            return Simplified(any!);
        }

        private static HookValue Not(HookValue value) => value switch
        {
            HookConstant constant => new HookConstant(constant.Value == 0m ? 1m : 0m),
            HookNot not => not.Inner,
            _ => new HookNot(value),
        };

        /// <summary>A test decided by constants is its answer (an and with a false part is false, a true part says nothing).</summary>
        private static HookValue Simplified(HookValue value)
        {
            if (value is not HookMath { Operation: HookOperation.And or HookOperation.Or } math) return value;
            var left = Simplified(math.Left);
            var right = Simplified(math.Right);
            if (math.Operation == HookOperation.And)
            {
                if (left is HookConstant { Value: 0m } || right is HookConstant { Value: 0m }) return new HookConstant(0m);
                if (left is HookConstant) return right;
                if (right is HookConstant) return left;
            }
            else
            {
                if (left is HookConstant { Value: not 0m } || right is HookConstant { Value: not 0m }) return new HookConstant(1m);
                if (left is HookConstant) return right;
                if (right is HookConstant) return left;
            }
            return math with { Left = left, Right = right };
        }

        /// <summary>One test the code makes, as a value that's 1 when it holds.</summary>
        private HookValue Test(ProgramValue test)
        {
            switch (test)
            {
                case CompareValue compare:
                    return Compare(compare);
                // A question to the fight's log tested: there's any (Any), or its count isn't 0.
                case QueryValue { Name: "Count" or "Any" } logged when ProgramReader.ReadsHistory(logged):
                    return logged.Name == "Any" ? History(logged) : new HookMath(HookOperation.NotEqual, History(logged), new HookConstant(0m));
                // The owner's hand is empty (GetPile(Hand, owner).IsEmpty), or has cards (its Count tested as true or false).
                case QueryValue { Name: "IsEmpty", Of: { } emptyPile } when IsOwnersHand(emptyPile):
                    return new HookMath(HookOperation.Equal, new HookFactValue(HookFact.OwnerHandCount), new HookConstant(0m));
                case QueryValue { Name: "Count", Of: { } countedCards } when IsOwnersHandCards(countedCards):
                    return new HookMath(HookOperation.NotEqual, new HookFactValue(HookFact.OwnerHandCount), new HookConstant(0m));
                // Its own data's Remove(cardPlay.Card, out value): what it bound to the play is taken, 1 when there was one.
                case var take when IsTakeBound(take):
                    return new HookFactValue(HookFact.TakeBound);
                // A true-or-false plain field of its own data (Yah's triggeredThisTurn): its state, 1 when set.
                case QueryValue { Name: var dataField, Of: { } dataOf, Arguments.Count: 0, ValueType: "Int32" or "Boolean" or "Decimal" } when ProgramReader.IsOwnData(dataOf):
                    return new HookMath(HookOperation.NotEqual, new HookField(ProgramReader.OwnDataPrefix + dataField), new HookConstant(0m));
                // A null check of the creator of a made card: the simulator's made cards always have one (a player).
                case NamedValue { Name: "creator" }:
                    return new HookConstant(1m);
                // A roll over the hittable enemies found one: the simulator's random damage hits none when there's none to hit (ProcDamageRandom).
                case StoredValue { Value: QueryValue { Name: "NextItem", Arguments: [QueryValue { Name: "HittableEnemies" }] } }:
                    return new HookConstant(1m);
                // A value the code kept in a local and tests (int count = ...; if (count != 0)): the value it kept.
                case StoredValue storedTest:
                    return Test(storedTest.Value);
                case QueryValue { Name: "Contains", Arguments: [QueryValue { Name: "Tags", Of: { } tagsOf }, ConstantValue tag] } when IsPlayCard(tagsOf):
                    return EnumName(_type.Assembly, "CardTag", tag.Value) is { } tagName && Enum.TryParse<SimCardTag>(tagName, out _)
                        ? new HookFactValue(HookFact.CardTag) { Of = tagName } : throw new Gap(ReaderCapability.CardFacts, $"gap: tests the card's tag {tag.Value}");
                // A card drawn in a simulation is never the turn's hand draw.
                case NamedValue { Name: "fromHandDraw" }:
                    return new HookConstant(0m);
                case ParameterValue { Name: "cardSource" } or NamedValue { Name: "cardSource" }:
                    return new HookFactValue(HookFact.FromCard);
                // A value of several ways (a null-conditional chain: base.Applier?.Player) tested as true: a way holds and its value is true.
                case WaysValue ways:
                {
                    HookValue? any = null;
                    foreach (var (when, wayValue) in ways.Ways)
                    {
                        var way = Simplified(new HookMath(HookOperation.And, Condition(when) ?? new HookConstant(1m), Test(wayValue)));
                        any = any == null ? way : new HookMath(HookOperation.Or, any, way);
                    }
                    return Simplified(any ?? new HookConstant(0m));
                }
                case NullValue:
                    return new HookConstant(0m);
                // A true-or-false the code worked out on several ways and kept (bool flag = a && b && c): its condition.
                case ConditionValue kept:
                    return Condition(kept.Condition) ?? new HookConstant(1m);
                // Who applied this power: there is one (base.Applier), a player (base.Applier.Player).
                case var applier when IsApplier(applier):
                    return new HookFactValue(HookFact.HasApplier);
                case var applierPlayer when IsApplierPlayer(applierPlayer):
                    return new HookFactValue(HookFact.ApplierIsPlayer);
                // A death the game prevented (AfterDeath's wasRemovalPrevented): the simulator's deaths are never prevented (it tells AfterDeath
                // only of the deaths it follows through: SimEngine.AllyDeaths).
                case ParameterValue { Name: "wasRemovalPrevented" } or NamedValue { Name: "wasRemovalPrevented" } when _hook == "AfterDeath":
                    return new HookConstant(0m);
                // Its owner is dead (base.Owner.IsDead).
                case QueryValue { Name: "IsDead", Of: { } deadOf } when IsOwner(deadOf):
                    return new HookFactValue(HookFact.OwnerDead);
                // Its owner is alive (base.Owner.IsAlive: Creature.IsDead is !IsAlive; PoisonPower.Trigger's Decrement after each tick).
                case QueryValue { Name: "IsAlive", Of: { } aliveOf } when IsOwner(aliveOf):
                    return Not(new HookFactValue(HookFact.OwnerDead));
                case QueryValue { Type: "CombatManager", Name: "IsInProgress" }:
                    return new HookConstant(1m);
                // Its owner's or its own fight is there (base.Owner.Creature.CombatState != null): in a simulation, always.
                case QueryValue { Name: "CombatState", Of: var stateOf } when stateOf is NamedValue { Name: "this" } || stateOf != null && IsOwner(stateOf):
                    return new HookConstant(1m);
                case QueryValue { Type: "Creature", Name: "IsEnemy", Of: QueryValue { Name: "Owner", Of: NamedValue { Name: "power" } } }:
                    return new HookFactValue(HookFact.PowerOnEnemy);
                case QueryValue query when IsTakingPart(query):
                    return new HookFactValue(HookFact.TakesPart);
                case QueryValue { Name: "Contains", Arguments: [QueryValue { Name: "Pets" } pets, var dealer] } when pets.Of is { } petsOf && IsOwner(petsOf) && IsDealer(dealer):
                    return new HookFactValue(HookFact.DealerIsOwnersPet);
                case QueryValue { Name: "IsPoweredAttack", Arguments: [var props] } when IsProps(props):
                    return new HookFactValue(HookFact.PoweredAttack);
                case QueryValue { Name: "IsPoweredCardOrMonsterMoveBlock", Arguments: [var props] } when IsProps(props):
                    return new HookFactValue(HookFact.PoweredBlock);
                case QueryValue { Name: "IsCardOrMonsterMove", Arguments: [var props] } when IsProps(props):
                    return new HookFactValue(HookFact.CardOrMonsterMove);
                case QueryValue { Name: "Contains", Of: QueryValue { Name: "Keywords", Of: { } keywordsOf }, Arguments: [ConstantValue keyword] } when IsPlayCard(keywordsOf) || IsEventCard(keywordsOf):
                    return EnumName(_type.Assembly, "CardKeyword", keyword.Value) == "Ethereal" ? new HookFactValue(HookFact.CardEthereal) : throw new Gap(ReaderCapability.CardFacts, $"gap: tests the card's keyword {keyword.Value}");
                case QueryValue { Name: "is", Of: NamedValue { Name: "power" }, Type: var powerClass }:
                    return PowerIs(powerClass);
                case QueryValue { Name: "is", Of: { } cardOf, Type: var cardClass } when IsEventCard(cardOf):
                    return new HookFactValue(HookFact.CardIs) { Of = cardClass };
                // The card a damage or block comes from is a class (cardSource is SovereignBlade).
                case QueryValue { Name: "is", Of: { } sourceOf, Type: var sourceClass } when IsSourceCard(sourceOf):
                    return new HookFactValue(HookFact.SourceCardIs) { Of = sourceClass };
                case QueryValue { Type: "Decimal", Name: var operatorName, Arguments: [var left, var right] } when OperatorOf(operatorName) is { } operation:
                    return new HookMath(operation, Value(left), Value(right));
                // One of its own true-or-false properties (MiniRegent's UsedThisTurn): its state, 1 when set.
                case QueryValue { Of: NamedValue { Name: "this" }, IsProperty: true, Name: var property, Type: var declaring } when declaring == _type.Name && IsOwnMember(property):
                    return new HookMath(HookOperation.NotEqual, OwnProperty(property), new HookConstant(0m));
                // One of its own plain fields (Conductor's Baton's _triggeredThisTurn): its state, 1 when set.
                case var ownField when OwnPlainField(ownField) is { } fieldName:
                    return new HookMath(HookOperation.NotEqual, new HookField(fieldName), new HookConstant(0m));
                case ArithmeticValue:
                    // An int tested as true or false (not (count rem Cards): its remainder is 0).
                    return new HookMath(HookOperation.NotEqual, Value(test), new HookConstant(0m));
                case var creatureTest when CreatureTest(creatureTest) is { } creatureFact:
                    return creatureFact;
                default:
                    throw new Gap(ReaderCapabilities.OfValue(test), $"gap: tests {test}");
            }
        }

        /// <summary>
        /// A test of a creature the hook's code names (CreatureOf): it's there (dealer != null), a player (creature.Player, IsPlayer), a monster
        /// (IsMonster), an enemy (IsEnemy), has a power (HasPower&lt;T&gt;(), GetPower&lt;T&gt;() != null) or a relic (Player.GetRelic&lt;T&gt;()); null for
        /// any other test.
        /// </summary>
        private HookValue? CreatureTest(ProgramValue test)
        {
            if (test is StoredValue stored) return CreatureTest(stored.Value);
            if (CreatureOf(test) is { } there)
                return test is QueryValue { Name: "Player" } ? CreatureFact(HookFact.CreatureIsPlayer, there) : CreatureFact(HookFact.CreatureThere, there);
            if (test is not QueryValue { Of: { } of } query || CreatureOf(of) is not { } creature) return null;
            switch (query.Name)
            {
                case "IsPlayer": return CreatureFact(HookFact.CreatureIsPlayer, creature);
                case "IsMonster": return CreatureFact(HookFact.CreatureIsMonster, creature);
                case "IsEnemy": return Not(CreatureFact(HookFact.CreatureOnPlayersSide, creature));
                case "HasPower" or "GetPower" when query.GenericArguments is [var power]:
                    return CreatureFact(HookFact.CreatureHasPower, creature) with { Of = PowerClass(power) };
                case "GetRelic" when query.GenericArguments is [var relic]:
                    return CreatureFact(HookFact.CreatureHasRelic, creature) with { Of = RelicClass(relic) };
                default: return null;
            }
        }

        private static HookFactValue CreatureFact(HookFact fact, HookCreature creature) => new(fact) { Creature = creature };

        /// <summary>A power class of this branch, by name (a gap for anything else: an interface, a base class).</summary>
        private string PowerClass(string name) =>
            _type.Assembly.GetType("MegaCrit.Sts2.Core.Models.Powers." + name) is { IsInterface: false, IsAbstract: false } ? name : throw new Gap(ReaderCapability.OwnerPowers, $"gap: tests a power {name}");

        private string RelicClass(string name) =>
            _type.Assembly.GetType("MegaCrit.Sts2.Core.Models.Relics." + name) is { IsInterface: false, IsAbstract: false } ? name : throw new Gap(ReaderCapability.ValueNotFollowed, $"gap: tests a relic {name}");

        /// <summary>
        /// The creature a value names: the owner, the damage's dealer or target (a Modify, block or hit hook's), the creature that died (AfterDeath's),
        /// the one a power landed on, the power's applier; a creature's player or a player's creature is the same creature here (a test of a
        /// creature's player is whether it's a player); a pet's player (PetOwner) its own. Null for anything else.
        /// </summary>
        private HookCreature? CreatureOf(ProgramValue value)
        {
            if (value is StoredValue stored) return CreatureOf(stored.Value);
            if (IsOwner(value)) return new HookCreature(HookWho.Owner);
            if (IsDead(value)) return new HookCreature(HookWho.Dead);
            if (IsDealer(value) && DamageHooks.Contains(_hook)) return new HookCreature(HookWho.Dealer);
            if (value is ParameterValue { Name: "target" } or NamedValue { Name: "target" } && DamageHooks.Contains(_hook)) return new HookCreature(HookWho.Target);
            if (IsApplier(value)) return new HookCreature(HookWho.Applier);
            if (value is QueryValue { Name: "Owner", Of: NamedValue { Name: "power" } } && _hook == "AfterPowerAmountChanged") return new HookCreature(HookWho.PowerOwner);
            if (value is QueryValue { Name: "Player" or "Creature", Of: { } of }) return CreatureOf(of);
            if (value is QueryValue { Name: "PetOwner", Of: { } pet } && CreatureOf(pet) is { PetOwner: false } owned) return owned with { PetOwner = true };
            // The owner of a model got from a creature (a called model's base.Owner): that creature.
            if (value is QueryValue { Name: "Owner", Of: { } ownerOf } && Getter(ownerOf) is { } got) return got.Holder;
            return null;
        }

        /// <summary>The hooks given a damage's or block's dealer and target (the Modify hooks, a hit's reactions).</summary>
        private static readonly HashSet<string> DamageHooks = new(StringComparer.Ordinal)
        {
            "ModifyDamageAdditive", "ModifyDamageMultiplicative", "ModifyDamageMultiplicativeLate", "ModifyBlockAdditive", "ModifyBlockMultiplicative", "AfterDamageGiven", "AfterDamageReceived",
        };

        /// <summary>
        /// Two creatures the code names compared (creature == base.Owner of a model called on another, creature.Side == base.Owner.Side, a creature's
        /// side against the side whose turn ends or a side named), or a creature against null; null for anything else.
        /// </summary>
        private HookValue? CreatureCompare(CompareValue compare)
        {
            if (compare.Op is not (Comparison.Equal or Comparison.NotEqual)) return null;
            HookValue? holds = null;
            if (compare.Left is QueryValue { Name: "Side", Of: { } leftOf } && CreatureOf(leftOf) is { } leftSide)
            {
                if (compare.Right is QueryValue { Name: "Side", Of: { } rightOf } && CreatureOf(rightOf) is { } rightSide)
                    holds = CreatureFact(HookFact.SameSide, leftSide) with { Other = rightSide };
                // The side whose turn ends (AfterSideTurnEnd's side): the simulator ends the players'; the side whose turn starts, the enemies'.
                else if (compare.Right is ParameterValue { Name: "side" } or NamedValue { Name: "side" } && _hook.Contains("SideTurnEnd", StringComparison.Ordinal))
                    holds = CreatureFact(HookFact.CreatureOnPlayersSide, leftSide);
                else if (compare.Right is ParameterValue { Name: "side" } or NamedValue { Name: "side" } && StartsATurn(_hook))
                    holds = Not(CreatureFact(HookFact.CreatureOnPlayersSide, leftSide));
                else if (compare.Right is ConstantValue side && EnumName(_type.Assembly, "CombatSide", side.Value) is { } sideName)
                    holds = sideName == "Player" ? CreatureFact(HookFact.CreatureOnPlayersSide, leftSide) : Not(CreatureFact(HookFact.CreatureOnPlayersSide, leftSide));
            }
            else if (compare.Right is QueryValue { Name: "Side" } && compare.Left is not QueryValue { Name: "Side" })
                return CreatureCompare(compare with { Left = compare.Right, Right = compare.Left });
            // The dealer is its owner's pet (dealer == base.Owner.Osty: Mighty Wings').
            else if (IsDealer(compare.Left) && IsOwnersOsty(compare.Right) || IsDealer(compare.Right) && IsOwnersOsty(compare.Left))
                holds = new HookFactValue(HookFact.DealerIsOwnersPet);
            else if (CreatureOf(compare.Left) is { } left)
            {
                if (compare.Right is NullValue) holds = Not(CreatureFact(HookFact.CreatureThere, left));
                else if (CreatureOf(compare.Right) is { } right) holds = CreatureFact(HookFact.SameCreature, left) with { Other = right };
            }
            else if (compare.Left is NullValue && CreatureOf(compare.Right) is { } nulled) holds = Not(CreatureFact(HookFact.CreatureThere, nulled));
            return holds == null ? null : compare.Op == Comparison.Equal ? holds : Not(holds);
        }

        /// <summary>A comparison: of the owner with what the hook was given (a fact), of the card's type, of a power's type for its amount, or of values.</summary>
        private HookValue Compare(CompareValue compare)
        {
            // A value of several ways compared (a null-conditional chain: dealer.PetOwner?.Creature == base.Owner): the comparison of the first way
            // that holds, way by way.
            if (compare.Op is Comparison.Equal or Comparison.NotEqual && (Unstored(compare.Left) is WaysValue || Unstored(compare.Right) is WaysValue))
            {
                var (ways, other) = Unstored(compare.Left) is WaysValue leftWays ? (leftWays, compare.Right) : ((WaysValue)Unstored(compare.Right), compare.Left);
                // A way giving null against its owner (never null in a fight): not equal.
                HookValue Equal(ProgramValue value) => value is NullValue && IsOwner(other) ? new HookConstant(0m) : Compare(new CompareValue(Comparison.Equal, value, other));
                var each = new HookWays(ways.Ways.Select(way => new HookWay(Condition(way.When) ?? new HookConstant(1m), Equal(way.Value))).ToList());
                return compare.Op == Comparison.Equal ? each : Not(each);
            }
            if (compare.Op is Comparison.Equal or Comparison.NotEqual && OwnerFact(compare.Left, compare.Right) is { } fact)
                return compare.Op == Comparison.Equal ? fact : Not(fact);
            // The card played is its applier's (cardPlay.Card.Owner == base.Applier.Player).
            if (compare.Op is Comparison.Equal or Comparison.NotEqual
                && (CardOwnerOf(compare.Left) is { } leftCard && IsPlayCard(leftCard) && IsApplierPlayer(compare.Right) || CardOwnerOf(compare.Right) is { } rightCard && IsPlayCard(rightCard) && IsApplierPlayer(compare.Left)))
                return compare.Op == Comparison.Equal ? new HookFactValue(HookFact.PlayIsAppliers) : Not(new HookFactValue(HookFact.PlayIsAppliers));
            // The creature that died is the one who applied this power (creature == base.Applier).
            if (compare.Op is Comparison.Equal or Comparison.NotEqual && (IsDead(compare.Left) && IsApplier(compare.Right) || IsApplier(compare.Left) && IsDead(compare.Right)))
                return compare.Op == Comparison.Equal ? new HookFactValue(HookFact.DeadIsApplier) : Not(new HookFactValue(HookFact.DeadIsApplier));
            if (compare.Op is Comparison.Equal or Comparison.NotEqual && IsSideOfPlayers(compare.Left, compare.Right))
                return compare.Op == Comparison.Equal ? new HookFactValue(HookFact.OwnersTurn) : Not(new HookFactValue(HookFact.OwnersTurn));
            if (compare.Left is QueryValue { Type: "CardModel", Name: "Type", Of: { } typeOf } && (IsPlayCard(typeOf) || IsEventCard(typeOf)) && compare.Right is ConstantValue cardType)
            {
                var simType = EnumName(_type.Assembly, "CardType", cardType.Value) is { } typeName && Enum.TryParse<SimCardType>(typeName, out var parsed) ? parsed
                    : throw new Gap(ReaderCapability.CardFacts, $"gap: a card type {cardType.Value}");
                return new HookMath(OperationOf(compare.Op), new HookFactValue(HookFact.CardType), new HookConstant((int)simType));
            }
            if (compare.Left is QueryValue { Name: "GetTypeForAmount", Of: NamedValue { Name: "power" } } && compare.Right is ConstantValue powerType
                && compare.Op is Comparison.Equal or Comparison.NotEqual)
            {
                var debuff = EnumName(_type.Assembly, "PowerType", powerType.Value) switch
                {
                    "Debuff" => new HookFactValue(HookFact.PowerIsDebuff),
                    "Buff" => Not(new HookFactValue(HookFact.PowerIsDebuff)),
                    var other => throw new Gap(ReaderCapability.ValueNotFollowed, $"gap: a power type {other}"),
                };
                return compare.Op == Comparison.Equal ? debuff : Not(debuff);
            }
            // The side whose turn is ending or starting: the simulator ends the players' side's turn and starts the enemies'.
            if (compare.Left is NamedValue { Name: "side" } && compare.Right is ConstantValue side)
            {
                var isSide = StartsATurn(_hook) ? EnumName(_type.Assembly, "CombatSide", side.Value) == "Enemy" : EnumName(_type.Assembly, "CombatSide", side.Value) == "Player";
                return new HookConstant((compare.Op == Comparison.Equal) == isSide ? 1m : 0m);
            }
            if (CreatureCompare(compare) is { } creatures) return creatures;
            // The side whose turn it is against a side named (CombatState.CurrentSide == CombatSide.Enemy): the enemies' turn, or not.
            var sideConstant = compare.Left is QueryValue { Name: "CurrentSide" } ? compare.Right as ConstantValue : compare.Right is QueryValue { Name: "CurrentSide" } ? compare.Left as ConstantValue : null;
            if (compare.Op is Comparison.Equal or Comparison.NotEqual && sideConstant != null && EnumName(_type.Assembly, "CombatSide", sideConstant.Value) is { } sideNamed)
            {
                var enemies = new HookFactValue(HookFact.EnemiesTurn);
                var holds = sideNamed == "Enemy" ? (HookValue)enemies : Not(enemies);
                return compare.Op == Comparison.Equal ? holds : Not(holds);
            }
            return new HookMath(OperationOf(compare.Op), Value(compare.Left), Value(compare.Right));
        }

        /// <summary>The owner compared with what the hook was given: the card's owner, a made card's creator, the dealer, the target, the applier.</summary>
        private HookValue? OwnerFact(ProgramValue left, ProgramValue right)
        {
            if (!IsOwner(left) && !IsOwner(right)) return null;
            var other = IsOwner(left) ? right : left;
            if (CardOwnerOf(other) is { } card)
                return IsPlayCard(card) ? new HookFactValue(HookFact.PlayIsOwners)
                    : IsEventCard(card) ? new HookFactValue(HookFact.EventIsOwners)
                    : IsSourceCard(card) ? new HookFactValue(HookFact.CardIsOwners)
                    : throw new Gap(ReaderCapabilities.OfValue(other, otherwise: ReaderCapability.CreatureSide), $"gap: compares the owner with {other}");
            if (IsDead(other)) return new HookFactValue(HookFact.DeadIsOwner);
            if (IsCreator(other)) return new HookFactValue(HookFact.EventIsOwners);
            if (IsDealer(other)) return new HookFactValue(HookFact.DealerIsOwner);
            if (other is ParameterValue { Name: "target" } or NamedValue { Name: "target" }) return new HookFactValue(HookFact.TargetIsOwner);
            if (other is NamedValue { Name: "applier" } or ParameterValue { Name: "applier" }) return new HookFactValue(HookFact.ApplierIsOwner);
            // The player an event happened to (AfterOrbChanneled's player, AfterShuffle's): the event's player is its owner.
            if (other is NamedValue { Name: "player" or "shuffler" } or ParameterValue { Name: "player" or "shuffler" } && _hook is "AfterOrbChanneled" or "AfterShuffle")
                return new HookFactValue(HookFact.EventIsOwners);
            // Another creature the code names (a pet's player, a creature's player): the creatures' comparison (CreatureCompare).
            if (CreatureOf(other) != null) return null;
            throw new Gap(ReaderCapabilities.OfValue(other, otherwise: ReaderCapability.CreatureSide), $"gap: compares the owner with {other}");
        }

        private HookValue PowerIs(string powerClass)
        {
            var found = _type.Assembly.GetType("MegaCrit.Sts2.Core.Models.Powers." + powerClass);
            if (found != null && !found.IsInterface) return new HookFactValue(HookFact.PowerIs) { Of = powerClass };
            // An interface (ITemporaryPower): the power classes that are one, read from the game's types.
            var classes = HookReader.Powers(_type.Assembly.GetTypes()).Where(type => type.GetInterfaces().Any(implemented => implemented.Name == powerClass))
                .Select(type => type.Name).OrderBy(name => name, StringComparer.Ordinal).ToList();
            return classes.Count == 0 ? throw new Gap(ReaderCapability.ValueNotFollowed, $"gap: tests the power is {powerClass}") : new HookFactValue(HookFact.PowerIn) { Of = string.Join(",", classes) };
        }

        // ---- values

        private HookValue Value(ProgramValue value)
        {
            switch (value)
            {
                // A question to the fight's log (Count or Any of its entries): the history ledger's count (decision #191).
                case QueryValue { Name: "Count" or "Any" } logged when ProgramReader.ReadsHistory(logged):
                    return History(logged);
                case ConstantValue constant: return new HookConstant(constant.Value);
                // A null compared (an object against null: 0) and the owner's fight (there: 1).
                case NullValue: return new HookConstant(0m);
                case QueryValue { Name: "CombatState", Of: var fightOf } when fightOf is NamedValue { Name: "this" } || fightOf != null && IsOwner(fightOf):
                    return new HookConstant(1m);
                case NamedValue { Name: "Decimal.Zero" }: return new HookConstant(0m);
                case NamedValue { Name: "Decimal.One" }: return new HookConstant(1m);
                case NamedValue { Name: "Decimal.MinusOne" }: return new HookConstant(-1m);
                case NamedValue { Name: "amount" } when _hook == "AfterPowerAmountChanged": return new HookFactValue(HookFact.PowerAmount);
                // The energy a spend took (AfterEnergySpent's amount): the simulator raises one energy-spent event for each energy spent.
                case NamedValue { Name: "amount" } or ParameterValue { Name: "amount" } when _hook == "AfterEnergySpent": return new HookConstant(1m);
                // The stars a spend took (AfterStarsSpent's amount): the play's stars, as the simulator spends them (one event a play).
                case NamedValue { Name: "amount" } or ParameterValue { Name: "amount" } when _hook == "AfterStarsSpent": return new HookFactValue(HookFact.StarsSpent);
                // The floor of a quotient (Mathf.FloorToInt((float)a / (float)b): Galactic Dust's blocks per Stars spent): (a - a rem b) / b, for counts.
                case QueryValue { Type: "Mathf", Name: "FloorToInt", Arguments: [ArithmeticValue { Op: "div" } quotient] }:
                {
                    var (dividend, divisor) = (Value(quotient.Left), Value(quotient.Right));
                    return new HookMath(HookOperation.Divide, new HookMath(HookOperation.Subtract, dividend, new HookMath(HookOperation.Remainder, dividend, divisor)), divisor);
                }
                // The HP a hit took past block (result.UnblockedDamage).
                case QueryValue { Name: "UnblockedDamage", Of: ParameterValue { Name: "result" } or NamedValue { Name: "result" } } when _hook is "AfterDamageGiven" or "AfterDamageReceived":
                    return new HookFactValue(HookFact.HitUnblocked);
                // What a hit dealt, its block included (result.TotalDamage: DamageResult's blocked plus unblocked).
                case QueryValue { Name: "TotalDamage", Of: ParameterValue { Name: "result" } or NamedValue { Name: "result" } } when _hook is "AfterDamageGiven" or "AfterDamageReceived":
                    return new HookFactValue(HookFact.HitTotal);
                // The play count the listeners before it gave (ModifyCardPlayCount's playCount).
                case ParameterValue { Name: "playCount" } or NamedValue { Name: "playCount" } when _hook == "ModifyCardPlayCount": return new HookFactValue(HookFact.PlayCountSoFar);
                case QueryValue { Type: "Decimal", Name: ".ctor", Arguments: var parts } when parts.All(part => part is ConstantValue):
                {
                    var numbers = parts.Cast<ConstantValue>().Select(part => part.Value).ToList();
                    return numbers.Count switch
                    {
                        1 => new HookConstant(numbers[0]),
                        5 => new HookConstant(new decimal((int)numbers[0], (int)numbers[1], (int)numbers[2], numbers[3] != 0, (byte)numbers[4])),
                        _ => throw new Gap(ReaderCapability.Arithmetic, $"gap: a decimal {value}"),
                    };
                }
                case QueryValue { Name: "Amount", Of: NamedValue { Name: "this" }, Type: "PowerModel" }:
                    return new HookFactValue(HookFact.Amount);
                // One of its own properties (Kunai's AttacksPlayedThisTurn): simulator state, seeded from the game.
                case QueryValue { Of: NamedValue { Name: "this" }, IsProperty: true, Name: var property, Type: var declaring } when declaring == _type.Name && IsOwnMember(property):
                    return OwnProperty(property);
                // A base class's property its class overrides with a getter of its own (Insatiable Dominion's DisplayAmount): what its getter works out.
                case QueryValue { Of: NamedValue { Name: "this" }, IsProperty: true, Name: var overridden } when _type.GetProperty(overridden, Declared) is { GetMethod: not null, SetMethod: null }:
                    return OwnProperty(overridden);
                // One of its own plain fields (a count it keeps): simulator state, seeded from the game.
                case var ownField when OwnPlainField(ownField) is { } fieldName:
                    return new HookField(fieldName);
                case QueryValue { Name: "EnergyValue", Of: { } resources } when (resources is StoredValue stored ? stored.Value : resources) is QueryValue { Name: "Resources", Of: NamedValue { Name: "cardPlay" } or ParameterValue { Name: "cardPlay" } }:
                    return new HookFactValue(HookFact.CardEnergy);
                case QueryValue { Name: "GetResolved", Of: QueryValue { Name: "EnergyCost", Of: { } costOf } } when IsPlayCard(costOf):
                    return new HookFactValue(HookFact.CardEnergy);
                // Whole numbers divided (int energySpent / 4: Orbit's) leave the whole part, as the CPU's div does: (a - a rem b) / b.
                case ArithmeticValue { Op: "div" } whole when IsWhole(whole.Left) && IsWhole(whole.Right):
                {
                    var (dividend, divisor) = (Value(whole.Left), Value(whole.Right));
                    return new HookMath(HookOperation.Divide, new HookMath(HookOperation.Subtract, dividend, new HookMath(HookOperation.Remainder, dividend, divisor)), divisor);
                }
                case ArithmeticValue arithmetic:
                    return new HookMath(arithmetic.Op switch
                    {
                        "add" => HookOperation.Add, "sub" => HookOperation.Subtract, "mul" => HookOperation.Multiply, "div" => HookOperation.Divide, "rem" => HookOperation.Remainder,
                        _ => throw new Gap(ReaderCapability.Arithmetic, $"gap: {arithmetic.Op}"),
                    }, Value(arithmetic.Left), Value(arithmetic.Right));
                case QueryValue { Type: "Decimal", Name: var operatorName, Arguments: [var left, var right] } when OperatorOf(operatorName) is { } operation:
                    return new HookMath(operation, Value(left), Value(right));
                case QueryValue { Type: "Decimal", Name: "op_Implicit", Arguments: [var inner] }:
                    return Value(inner);
                // Math.Min and Math.Max of two values (PoisonPower.TriggerCount: Min(Amount, 1 + its opponents' Accelerant)).
                case QueryValue { Type: "Math", Name: "Min" or "Max", Arguments: [var left, var right] } bounded:
                    return new HookMath(bounded.Name == "Min" ? HookOperation.Min : HookOperation.Max, Value(left), Value(right));
                // A power summed over the owner's living opponents (Sum(Where(GetOpponentsOf(base.Owner), c => c.IsAlive), a => a.GetPowerAmount<T>())).
                case QueryValue { Type: "Enumerable", Name: "Sum", Arguments: [var opponents, LambdaValue each] } when LivingOpponents(opponents) && PowerAmountOf(each) is { } summed:
                    return new HookFactValue(HookFact.OpponentsPowerAmount) { Of = PowerClass(summed) };
                // x-- and x++ on a decimal (Panache's DynamicVars["CardsLeft"].BaseValue--).
                case QueryValue { Type: "Decimal", Name: "op_Decrement" or "op_Increment", Arguments: [var stepped] } step:
                    return new HookMath(step.Name == "op_Decrement" ? HookOperation.Subtract : HookOperation.Add, Value(stepped), new HookConstant(1m));
                // -x on a decimal (Monologue's -StrengthApplied).
                case QueryValue { Type: "Decimal", Name: "op_UnaryNegation", Arguments: [var negated] }:
                    return new HookMath(HookOperation.Multiply, Value(negated), new HookConstant(-1m));
                case StoredValue stored:
                    return Value(stored.Value);
                case CompareValue compare:
                    return Compare(compare);
                // The players' turn number (base.Owner.PlayerCombatState.TurnNumber).
                case QueryValue { Name: "TurnNumber", Of: QueryValue { Name: "PlayerCombatState", Of: { } turnOf } } when IsOwner(turnOf):
                    return new HookFactValue(HookFact.TurnNumber);
                // The owner's block (base.Owner.Creature.Block).
                case QueryValue { Name: "Block", Of: { } blockOf } when IsOwner(blockOf):
                    return new HookFactValue(HookFact.OwnerBlock);
                // The cards in the owner's hand (PileType.Hand.GetPile(base.Owner).Cards.Count, its Cards kept in a local or not).
                case QueryValue { Name: "Count", Of: { } cards } when IsOwnersHandCards(cards):
                    return new HookFactValue(HookFact.OwnerHandCount);
                // The owner's orbs of a kind (OrbQueue.Orbs.Count(o => o is FrostOrb)).
                case QueryValue { Type: "Enumerable", Name: "Count", Arguments: [var orbs, LambdaValue lambda] } when IsOwnersOrbs(orbs) && OrbKindTested(lambda) is { } orbKind:
                    return new HookFactValue(HookFact.OwnerOrbCount) { Of = orbKind.ToString() };
                // What its own data's Remove(cardPlay.Card, out value) took: the amount bound to the play.
                case OutValue { Call: var take, Parameter: 1 } when IsTakeBound(take):
                    return new HookFactValue(HookFact.BoundTaken);
                // A plain field of its own data (Dark Embrace's etherealCount): simulator state, seeded from the game.
                case QueryValue { Name: var dataField, Of: { } dataOf, Arguments.Count: 0, ValueType: "Int32" or "Boolean" or "Decimal" } when ProgramReader.IsOwnData(dataOf):
                    return new HookField(ProgramReader.OwnDataPrefix + dataField);
            }
            if (VarKey(value) is { } key) return Var(key);
            if (value is QueryValue { Name: "Props", Of: { } propsOf } && VarKey(propsOf) is { } propsKey) return Var(propsKey + ".Props");
            // A power's amount on a creature the code names (X.GetPower<T>().Amount: a called power's base.Amount, Cruelty's).
            if (value is QueryValue { Name: "Amount", Of: { } amountOf } && Getter(amountOf) is { Relic: false } amountGetter)
                return CreatureFact(HookFact.CreaturePowerAmount, amountGetter.Holder) with { Of = amountGetter.Model };
            // A value of several ways (a local set on each branch: Vulnerable's num after Paper Phrog, then Cruelty): the first way that holds.
            if (value is WaysValue ways)
                return new HookWays(ways.Ways.Select(way => new HookWay(Condition(way.When) ?? new HookConstant(1m), Value(way.Value))).ToList());
            if (value is QueryValue { Of: { } receiver } call && ModelCall(call, receiver) is { } called) return called;
            throw new Gap(ReaderCapabilities.OfValue(value), $"gap: the value {value}");
        }

        /// <summary>
        /// A question to the fight's log, as the history ledger counts it (HistoryQueryReader, the one translation every card, power and relic
        /// shares); a gap of the kind the reader names when the ledger doesn't count it.
        /// </summary>
        private HookValue History(QueryValue code)
        {
            var reading = HistoryQueryReader.Read(code, (enumType, number) => EnumName(_type.Assembly, enumType, number));
            if (reading.Query is not { } query) throw new Gap(ReaderCapability.FightLog, $"gap: the fight's log ({reading.Gap}: {reading.Why}): {code}");
            return new HookHistory(query) { Any = reading.AsksAny };
        }

        /// <summary>
        /// A model got from a creature the code names: X.GetPower&lt;T&gt;() (a power on it), X.Player.GetRelic&lt;T&gt;() (a relic of its player):
        /// the creature and the class.
        /// </summary>
        private (HookCreature Holder, string Model, bool Relic)? Getter(ProgramValue value)
        {
            if (value is StoredValue stored) return Getter(stored.Value);
            if (value is not QueryValue { Name: "GetPower" or "GetRelic", GenericArguments: [var model], Of: { } of } query || CreatureOf(of) is not { } holder) return null;
            return query.Name == "GetPower" ? (holder, PowerClass(model), false) : (holder, RelicClass(model), true);
        }

        /// <summary>
        /// A call into another model's own method on a model got from a creature (paperPhrog.ModifyVulnerableMultiplier(target, num, ...), on
        /// dealer.Player?.GetRelic&lt;PaperPhrog&gt;(); debilitate.ModifyWeakMultiplier(dealer, num, ...)): the method's returned value read from its
        /// code, its parameters the caller's arguments and its base.Owner the creature it was got from (its base.Amount that power's amount there),
        /// on each way the receiver is got (a ?? is one way each). Null for a call that isn't one; a gap where its code can't be read.
        /// </summary>
        private HookValue? ModelCall(QueryValue call, ProgramValue receiver)
        {
            var receivers = (receiver is StoredValue stored ? stored.Value : receiver) is WaysValue receiverWays
                ? receiverWays.Ways.Where(way => way.Value is not NullValue).Select(way => (When: (ProgramCondition?)way.When, Getter: way.Value)).ToList()
                : new List<(ProgramCondition? When, ProgramValue Getter)> { (null, receiver) };
            if (receivers.Count == 0 || receivers.Any(way => Getter(way.Getter) == null)) return null;
            var ways = new List<HookWay>();
            foreach (var (when, getter) in receivers)
            {
                var (_, model, relic) = Getter(getter)!.Value;
                var calleeType = _type.Assembly.GetType((relic ? "MegaCrit.Sts2.Core.Models.Relics." : "MegaCrit.Sts2.Core.Models.Powers.") + model) ?? throw new Gap(ReaderCapability.ModelCall, $"gap: no class {model}");
                var method = calleeType.GetMethods(Declared).FirstOrDefault(candidate => candidate.Name == call.Name && candidate.GetParameters().Length == call.Arguments.Count)
                    ?? throw new Gap(ReaderCapability.ModelCall, $"gap: {model}.{call.Name} not found");
                if (method.ReturnType.Name != "Decimal") throw new Gap(ReaderCapability.ModelCall, $"gap: {model}.{call.Name} gives a {method.ReturnType.Name}");
                var returned = ProgramReader.ReadReturnedWays(calleeType, method, _read);
                // Its parameters are the caller's arguments, its own model (this) the one got from the creature: one map, so neither is read as the other.
                var parameters = method.GetParameters().Select((parameter, index) => (parameter.Name!, call.Arguments[index])).ToDictionary(pair => pair.Item1, pair => pair.Item2, StringComparer.Ordinal);
                var mapped = (WaysValue)returned.Map(part => part switch
                {
                    NamedValue { Name: "this" } => getter,
                    ParameterValue { Name: var name } when parameters.TryGetValue(name, out var argument) => argument,
                    NamedValue { Name: var name } when parameters.TryGetValue(name, out var argument) => argument,
                    _ => null,
                });
                if (mapped.Parts().Any(part => part is QueryValue { Name: "DynamicVars", Of: var varsOf } && Equals(varsOf, getter))) throw new Gap(ReaderCapability.ModelCall, $"gap: {model}.{call.Name} reads its own values");
                ways.Add(new HookWay(when == null ? new HookConstant(1m) : Condition(when) ?? new HookConstant(1m), Ways(mapped)));
            }
            return ways.Count == 1 && ways[0].When is HookConstant { Value: 1m } ? ways[0].Value : new HookWays(ways);
        }

        private Dictionary<string, decimal>? _canonical;

        /// <summary>One of the model's values, with its canonical value (what the class's CanonicalVars make it) for a state that has none.</summary>
        private HookVar Var(string key)
        {
            _canonical ??= CanonicalVars();
            var canonical = _canonical.TryGetValue(key, out var value) ? value
                : key.EndsWith("Power", StringComparison.Ordinal) && _canonical.TryGetValue("~PowerVar", out var powerValue) ? powerValue : 0m;
            return new HookVar(key) { Canonical = canonical };
        }

        /// <summary>
        /// The class's values as its CanonicalVars getter makes them (new CardsVar(3), new BlockVar(4, Unpowered), new DynamicVar("DamageDecrease",
        /// 0.75)): each by its key (the var class's own key, read from its constructor; a PowerVar's is its power's, the one key the hooks read that
        /// no other value gives) and its props.
        /// </summary>
        private Dictionary<string, decimal> CanonicalVars()
        {
            var found = new Dictionary<string, decimal>(StringComparer.Ordinal);
            MethodInfo? getter = null;
            for (var type = _type; type != null && getter == null; type = type.BaseType)
                getter = type.GetProperty("CanonicalVars", Declared)?.GetMethod;
            if (getter == null) return found;
            ProgramValue? made;
            try { made = ProgramReader.ReadReturnedValue(getter.DeclaringType!, getter, _read); }
            catch { return found; }
            var values = made is QueryValue { Name: ".ctor", Arguments: [ArrayValue array] } ? array.Items
                : made is QueryValue { Name: ".ctor", Arguments: [QueryValue single] } ? new[] { (ProgramValue)single } : Array.Empty<ProgramValue>();
            var powerVars = new List<decimal>();
            foreach (var value in values)
            {
                if (value is not QueryValue { Name: ".ctor", Type: var varType, Arguments: var arguments } || arguments.Count == 0) continue;
                decimal? Number(ProgramValue argument) { try { return Value(argument) is HookConstant constant ? constant.Value : null; } catch (Gap) { return null; } }
                if (varType == "DynamicVar" && arguments is [NamedValue name, var named] && Number(named) is { } namedValue) { found[name.Name.Trim('"')] = namedValue; continue; }
                // A PowerVar<T> named in its constructor (new PowerVar<AimPower>("EndOfTurnAim", 3)): its key is that name.
                if (varType.StartsWith("PowerVar", StringComparison.Ordinal) && arguments is [NamedValue { Name: var powerVarName }, var powerVarValue] && powerVarName.StartsWith('"')
                    && Number(powerVarValue) is { } namedPower) { found[powerVarName.Trim('"')] = namedPower; continue; }
                if (Number(arguments[0]) is not { } number) continue;
                // A PowerVar<T>'s key is its power's class (its generic argument, as the var's own constructor names it).
                if (varType.StartsWith("PowerVar", StringComparison.Ordinal) && value is QueryValue { GenericArguments: [var power] }) { found[power] = number; continue; }
                if (varType.StartsWith("PowerVar", StringComparison.Ordinal)) { powerVars.Add(number); continue; }
                var key = VarClassKey(varType);
                found[key] = number;
                if (arguments.Count > 1 && Number(arguments[1]) is { } props) found[key + ".Props"] = props;
            }
            if (powerVars.Count == 1) found["~PowerVar"] = powerVars[0];
            return found;
        }

        /// <summary>The key a var class gives its value (its constructor's text: CardsVar's "Cards"), or its name less "Var".</summary>
        private string VarClassKey(string varType)
        {
            var type = _type.Assembly.GetType("MegaCrit.Sts2.Core.Localization.DynamicVars." + varType);
            foreach (var constructor in type?.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? Array.Empty<ConstructorInfo>())
            {
                try { if (_read(constructor).FirstOrDefault(instruction => instruction.Text != null)?.Text is { } text) return text; }
                catch { /* no code */ }
            }
            return varType.EndsWith("Var", StringComparison.Ordinal) ? varType[..^3] : varType;
        }

        /// <summary>A Modify hook's returns: each way's test and value, the first that holds.</summary>
        private HookValue Ways(WaysValue ways)
        {
            if (ways.Ways.Count == 0) throw new Gap(ReaderCapability.ValueNotFollowed, "gap: no return read");
            return new HookWays(ways.Ways.Select(way => new HookWay(Condition(way.When) ?? new HookConstant(1m), Value(way.Value))).ToList());
        }

        /// <summary>
        /// A returned value with what its ways already decide left out: inside a way, a test its way's test needs is known to hold; after a way,
        /// what its failing tells; the right side of an and knows its left side held, of an or that it failed; a way that can't hold goes, and
        /// one that always does ends the list. The code's own ways (Vulnerable's num after Paper Phrog, then Cruelty, then Debilitate: each a
        /// value of every way before it) nest every test again, so this is what keeps a read model's value near the size of its code. A test is
        /// known by what it tests, wherever the code made it: a Modify hook's value changes nothing, so one test has one answer in a run. It
        /// changes no answer.
        /// </summary>
        internal static HookValue Pruned(HookValue value) => Pruned(value, ImmutableHashSet<HookValue>.Empty, ImmutableHashSet<HookValue>.Empty);

        private static HookValue Pruned(HookValue value, ImmutableHashSet<HookValue> holds, ImmutableHashSet<HookValue> fails)
        {
            switch (value)
            {
                case HookTest test:
                {
                    var inner = Pruned(test.Test, holds, fails);
                    return inner is HookConstant ? inner : test with { Test = inner };
                }
                case HookFactValue when holds.Contains(value): return new HookConstant(1m);
                case HookFactValue when fails.Contains(value): return new HookConstant(0m);
                case HookNot not:
                    return Not(Pruned(not.Inner, holds, fails));
                case HookMath { Operation: HookOperation.And } both:
                {
                    var left = Pruned(both.Left, holds, fails);
                    var (leftHolds, leftFails) = Known(left, holds, fails, truth: true);
                    return Simplified(both with { Left = left, Right = Pruned(both.Right, leftHolds, leftFails) });
                }
                case HookMath { Operation: HookOperation.Or } either:
                {
                    var left = Pruned(either.Left, holds, fails);
                    var (leftHolds, leftFails) = Known(left, holds, fails, truth: false);
                    return Simplified(either with { Left = left, Right = Pruned(either.Right, leftHolds, leftFails) });
                }
                case HookMath math:
                    return math with { Left = Pruned(math.Left, holds, fails), Right = Pruned(math.Right, holds, fails) };
                case HookWays ways:
                {
                    var kept = new List<HookWay>();
                    foreach (var way in ways.Ways)
                    {
                        var when = Pruned(way.When, holds, fails);
                        if (when is HookConstant { Value: 0m }) continue;
                        var (wayHolds, wayFails) = Known(when, holds, fails, truth: true);
                        var wayValue = Pruned(way.Value, wayHolds, wayFails);
                        if (when is HookConstant)
                        {
                            kept.Add(new HookWay(new HookConstant(1m), wayValue));
                            break;
                        }
                        kept.Add(new HookWay(when, wayValue));
                        // A later way is taken only where this one wasn't.
                        (holds, fails) = Known(when, holds, fails, truth: false);
                    }
                    if (kept.Count == 0) return new HookConstant(0m);
                    return kept[0].When is HookConstant ? kept[0].Value : new HookWays(kept);
                }
                default:
                    return value;
            }
        }

        /// <summary>
        /// What a condition's answer tells of its facts: held, an and's every part held (a not's part failed); failed, a single fact (or an or's
        /// every part) failed. Added to those known.
        /// </summary>
        private static (ImmutableHashSet<HookValue> Holds, ImmutableHashSet<HookValue> Fails) Known(HookValue when, ImmutableHashSet<HookValue> holds, ImmutableHashSet<HookValue> fails, bool truth)
        {
            switch (when)
            {
                case HookTest test: return Known(test.Test, holds, fails, truth);
                case HookNot not: return Known(not.Inner, holds, fails, !truth);
                case HookFactValue: return truth ? (holds.Add(when), fails) : (holds, fails.Add(when));
                case HookMath { Operation: HookOperation.And } when truth:
                case HookMath { Operation: HookOperation.Or } when !truth:
                {
                    var math = (HookMath)when;
                    var (leftHolds, leftFails) = Known(math.Left, holds, fails, truth);
                    return Known(math.Right, leftHolds, leftFails, truth);
                }
                default: return (holds, fails);
            }
        }

        /// <summary>
        /// The DynamicVars key a value reads (V[Cards]; DynamicVars.Damage stored and read; its BaseValue or IntValue), the property's own key read
        /// from DynamicVarSet's getter (Dexterity is "DexterityPower"); null when it isn't one of the model's values.
        /// </summary>
        private string? VarKey(ProgramValue value)
        {
            switch (value)
            {
                case StoredValue stored: return VarKey(stored.Value);
                case CardValue card: return KeyOfProperty(card.Key);
                case QueryValue { Name: "BaseValue" or "IntValue", Of: { } of }: return VarKey(of);
                case QueryValue { Type: "DynamicVarSet", Name: var property, Of: QueryValue { Name: "DynamicVars", Of: NamedValue { Name: "this" } } }: return KeyOfProperty(property);
                case QueryValue { Type: "DynamicVarSet", Name: "Item", Arguments: [NamedValue named] }: return named.Name.Trim('"');
                default: return null;
            }
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Assembly, string), string> PropertyKeys = new();

        /// <summary>The key DynamicVarSet's property reads (its getter's text: Dexterity's "DexterityPower"), or the name itself (an indexed key).</summary>
        private string KeyOfProperty(string property) => PropertyKeys.GetOrAdd((_type.Assembly, property), key =>
        {
            var set = key.Item1.GetType("MegaCrit.Sts2.Core.Localization.DynamicVars.DynamicVarSet");
            if (set?.GetProperty(key.Item2, BindingFlags.Instance | BindingFlags.Public)?.GetMethod is not { } getter) return key.Item2;
            try { return _read(getter).FirstOrDefault(instruction => instruction.Text != null)?.Text ?? key.Item2; }
            catch { return key.Item2; }
        });

        // ---- what values are

        /// <summary>
        /// The model's owner: base.Owner (a power's creature, a relic's, a potion's or an orb's player), an enchantment's or affliction's card's
        /// owner (base.Card.Owner), its player or creature.
        /// </summary>
        private static bool IsOwner(ProgramValue value) => value switch
        {
            QueryValue { Name: "Owner", Of: NamedValue { Name: "this" }, Type: "PowerModel" or "RelicModel" or "AbstractModel" or "PotionModel" or "OrbModel" } => true,
            QueryValue { Name: "Owner", Of: QueryValue { Name: "Card", Of: NamedValue { Name: "this" }, Type: "EnchantmentModel" or "AfflictionModel" } } => true,
            QueryValue { Name: "Player" or "Creature", Of: { } of } => IsOwner(of),
            _ => false,
        };

        /// <summary>The owner's hand: PileType.Hand.GetPile(base.Owner) (the pile type read by its name on this branch).</summary>
        private bool IsOwnersHand(ProgramValue value) =>
            (value is StoredValue stored ? stored.Value : value) is QueryValue { Name: "GetPile", Arguments: [ConstantValue pileType, var whose] }
            && EnumName(_type.Assembly, "PileType", pileType.Value) == "Hand" && IsOwner(whose);

        /// <summary>The cards in the owner's hand: GetPile(Hand, base.Owner).Cards, kept in a local or not.</summary>
        private bool IsOwnersHandCards(ProgramValue value) =>
            (value is StoredValue stored ? stored.Value : value) is QueryValue { Name: "Cards", Of: { } pile } && IsOwnersHand(pile);

        /// <summary>The owner's orbs: base.Owner.Player.PlayerCombatState.OrbQueue.Orbs.</summary>
        private static bool IsOwnersOrbs(ProgramValue value) =>
            (value is StoredValue stored ? stored.Value : value) is QueryValue { Name: "Orbs", Of: QueryValue { Name: "OrbQueue", Of: QueryValue { Name: "PlayerCombatState", Of: { } player } } }
            && IsOwner(player);

        /// <summary>A value as kept in a local or not (a StoredValue's value).</summary>
        private static ProgramValue Unstored(ProgramValue value) => value is StoredValue stored ? Unstored(stored.Value) : value;

        /// <summary>Its owner's Osty (base.Owner.Osty: its player's pet).</summary>
        private static bool IsOwnersOsty(ProgramValue value) => (value is StoredValue stored ? stored.Value : value) is QueryValue { Name: "Osty", Of: { } ostyOf } && IsOwner(ostyOf);

        /// <summary>The owner's living opponents (Where(CombatState.GetOpponentsOf(base.Owner), c => c.IsAlive)), kept in a local or not.</summary>
        private static bool LivingOpponents(ProgramValue value) =>
            (value is StoredValue stored ? stored.Value : value) is QueryValue { Type: "Enumerable", Name: "Where", Arguments: [QueryValue { Name: "GetOpponentsOf", Arguments: [var whose] }, LambdaValue alive] }
            && IsOwner(whose) && alive.Body.Terms is [[{ Holds: true, Test: QueryValue { Name: "IsAlive", Of: ParameterValue { Name: var tested } } }]] && tested == alive.Parameter;

        /// <summary>The power whose amount a lambda gives for its creature (a => a.GetPowerAmount&lt;T&gt;()), or null for any other lambda.</summary>
        private static string? PowerAmountOf(LambdaValue lambda) =>
            lambda.Body.Terms is [[{ Holds: true, Test: QueryValue { Name: "GetPowerAmount", GenericArguments: [var power], Of: ParameterValue { Name: var asked } } }]] && asked == lambda.Parameter ? power : null;

        /// <summary>The kind of orb a lambda tests its orb for (o => o is FrostOrb), or null for any other lambda.</summary>
        private static OrbKind? OrbKindTested(LambdaValue lambda)
        {
            if (lambda.Body.Terms is not [[{ Holds: true, Test: var test }]]) return null;
            if (test is CompareValue { Op: Comparison.NotEqual, Left: var isOrb, Right: NullValue }) test = isOrb;
            if (test is not QueryValue { Name: "is", Of: ParameterValue or NamedValue, Type: var orbClass } isQuery || isQuery.Of is ParameterValue { Name: var name } && name != lambda.Parameter) return null;
            return Enum.TryParse<OrbKind>(orbClass.EndsWith("Orb", StringComparison.Ordinal) ? orbClass[..^3] : orbClass, out var kind) && kind is not (OrbKind.Unknown or OrbKind.Random) ? kind : null;
        }

        /// <summary>The card whose owner a value is (card.Owner, card.Owner.Creature); null otherwise.</summary>
        private static ProgramValue? CardOwnerOf(ProgramValue value) => value switch
        {
            QueryValue { Type: "CardModel", Name: "Owner", Of: { } card } => card,
            QueryValue { Type: "Player", Name: "Creature", Of: QueryValue { Type: "CardModel", Name: "Owner", Of: { } card } } => card,
            _ => null,
        };

        /// <summary>The card played: cardPlay.Card, or a play-shape hook's card (ModifyCardPlayCount's card: the card whose play it decides).</summary>
        private bool IsPlayCard(ProgramValue value) =>
            value is QueryValue { Type: "CardPlay", Name: "Card", Of: NamedValue { Name: "cardPlay" } or ParameterValue { Name: "cardPlay" } }
            || value is NamedValue { Name: "card" } or ParameterValue { Name: "card" } && PlayShapeHooks.Contains(_hook);

        /// <summary>The hooks that decide a card play's shape before its plays, their card the one played (decision #192).</summary>
        private static readonly HashSet<string> PlayShapeHooks = new(StringComparer.Ordinal) { "ModifyCardPlayCount", "AfterModifyingCardPlayCount" };

        private bool IsEventCard(ProgramValue value) => value is NamedValue { Name: "card" } or ParameterValue { Name: "card" } && _hook != "AfterCardPlayed" && !PlayShapeHooks.Contains(_hook);

        private static bool IsSourceCard(ProgramValue value) => value is ParameterValue { Name: "cardSource" } or NamedValue { Name: "cardSource" };

        /// <summary>The player a card event happened to, as its hook names it: a made card's creator, the player who shuffled or spent stars.</summary>
        private static bool IsCreator(ProgramValue value) =>
            value is NamedValue { Name: "creator" or "shuffler" or "spender" } or ParameterValue { Name: "creator" or "shuffler" or "spender" }
            || value is QueryValue { Type: "Player", Name: "Creature", Of: { } player } && IsCreator(player);

        /// <summary>AfterDeath's creature that died (its first Creature parameter, by the hook's own name for it).</summary>
        private bool IsDead(ProgramValue value) => _hook == "AfterDeath" && (value is ParameterValue { Name: var name } ? name : value is NamedValue { Name: var named } ? named : null) is { } creature
            && _type.GetMethods(Declared).FirstOrDefault(method => method.Name == "AfterDeath")?.GetParameters().FirstOrDefault(parameter => parameter.ParameterType.Name == "Creature")?.Name == creature;

        /// <summary>The creature that applied this power (base.Applier).</summary>
        private static bool IsApplier(ProgramValue value) => (value is StoredValue stored ? stored.Value : value) is QueryValue { Name: "Applier", Of: NamedValue { Name: "this" }, Type: "PowerModel" };

        /// <summary>The player who applied this power (base.Applier.Player).</summary>
        private static bool IsApplierPlayer(ProgramValue value) => (value is StoredValue stored ? stored.Value : value) is QueryValue { Name: "Player", Of: { } applier } && IsApplier(applier);

        private static bool IsDealer(ProgramValue value) => value is ParameterValue { Name: "dealer" } or NamedValue { Name: "dealer" };

        private static bool IsProps(ProgramValue value) => value is ParameterValue { Name: "props" } or NamedValue { Name: "props" };

        /// <summary>participants.Contains(base.Owner): the owner takes part in the side's turn ending or starting.</summary>
        private static bool IsTakingPart(ProgramValue value) =>
            value is QueryValue { Name: "Contains", Arguments: [NamedValue { Name: "participants" } or ParameterValue { Name: "participants" }, var who] } && IsOwner(who);

        /// <summary>CurrentSide == Side of the owner's (or the card owner's) creature: the side whose turn it is is a player's.</summary>
        private static bool IsSideOfPlayers(ProgramValue left, ProgramValue right)
        {
            static bool IsSide(ProgramValue value) => value is QueryValue { Name: "Side", Of: { } of } && (IsOwner(of) || CardOwnerOf(of) != null);
            static bool IsCurrent(ProgramValue value) => value is QueryValue { Name: "CurrentSide" };
            return IsSide(left) && IsCurrent(right) || IsCurrent(left) && IsSide(right);
        }

        private static HookOperation? OperatorOf(string name) => name switch
        {
            "op_Equality" => HookOperation.Equal,
            "op_Inequality" => HookOperation.NotEqual,
            "op_LessThan" => HookOperation.Less,
            "op_LessThanOrEqual" => HookOperation.LessOrEqual,
            "op_GreaterThan" => HookOperation.Greater,
            "op_GreaterThanOrEqual" => HookOperation.GreaterOrEqual,
            "op_Addition" => HookOperation.Add,
            "op_Subtraction" => HookOperation.Subtract,
            "op_Multiply" => HookOperation.Multiply,
            "op_Division" => HookOperation.Divide,
            _ => null,
        };

        private static HookOperation OperationOf(Comparison comparison) => comparison switch
        {
            Comparison.Equal => HookOperation.Equal,
            Comparison.NotEqual => HookOperation.NotEqual,
            Comparison.Less => HookOperation.Less,
            Comparison.LessOrEqual => HookOperation.LessOrEqual,
            Comparison.Greater => HookOperation.Greater,
            _ => HookOperation.GreaterOrEqual,
        };
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Assembly, string), Type?> Enums = new();

    /// <summary>An enum member's name by its number, on this branch's types (main's numbers can differ).</summary>
    private static string? EnumName(Assembly assembly, string enumType, long value)
    {
        var type = Enums.GetOrAdd((assembly, enumType), key => key.Item1.GetTypes()
            .FirstOrDefault(candidate => candidate.IsEnum && candidate.Name == key.Item2 && candidate.Namespace?.StartsWith("MegaCrit", StringComparison.Ordinal) == true));
        return type?.GetFields(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(field => field.IsLiteral && Convert.ToInt64(field.GetRawConstantValue(), System.Globalization.CultureInfo.InvariantCulture) == value)?.Name;
    }
}
