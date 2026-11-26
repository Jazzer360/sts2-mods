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

    /// <summary>
    /// The gap of a body read as unknown where it runs (a power's AfterRemoved that doesn't read: UnknownStep), so the class's other hooks stay
    /// read; null for any other.
    /// </summary>
    public string? Unknown { get; init; }

    /// <summary>Read: translated, never run in a simulation for a reason given, or unknown where it runs.</summary>
    public bool Read => Gap == null;

    /// <summary>Translated into the simulator's program (not only never run, nor unknown).</summary>
    public bool Translated => Gap == null && NotRun == null && Unknown == null;

    public override string ToString() => Hook + (Gap != null ? ": " + Gap : NotRun != null ? ": not run: " + NotRun : Unknown != null ? ": unknown where it runs: " + Unknown : ": read");
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
        ["CreatureCmd.LoseMaxHp"] = "LoseMaxHpStep: a player the hook's code names (a hit's target, the owner) loses max HP, HP above the new max lost first as damage",
        ["CreatureCmd.LoseBlock"] = "LoseBlockStep: the owner loses up to that much block, AfterBlockBroken told when it breaks, its remover the owner or no one",
        ["OstyCmd.Summon"] = "SummonStep: a summon for its owner, the player who played the card or the hit's dealer's player, as OstyCmd.Summon runs (SimEngine.Summon)",
        ["CreatureCmd.Stun"] = "StunStep: the owner (an enemy) stunned, when the stunned move it's given calls no command",
        ["CreatureCmd.Kill"] = "KillStep: the owner killed: an enemy marked killed by its power, a player losing all their HP unless a death prevention holds it back; KillEachStep: each of a list the code works out (a foreach killing its item: DoomPower.DoomKill)",
        ["Hook.AfterDiedToDoom"] = "KillEachStep.Tells: the listeners told the list a kill went over (DoomPower.DoomKill, after its foreach)",
        ["CardPileCmd.Draw"] = "DrawStep: cards drawn by the owner, each a card event",
        ["PlayerCmd.GainEnergy"] = "GainEnergyStep: energy for the owner, each a card event",
        ["PlayerCmd.GainStars"] = "GainStarsStep: stars for the owner, one stars-gained event (as a card's)",
        ["PowerCmd.Apply"] = "ApplyPowerStep: the power on the owner, every hittable enemy or the creature a power landed on, applied by the owner",
        ["PowerCmd.Remove"] = "RemovePowerStep: the power itself goes (PowerCmd.Remove(this))",
        ["PowerCmd.Decrement"] = "DecrementPowerStep: the power itself goes down by one (PowerCmd.Decrement(this))",
        ["PowerCmd.TickDownDuration"] = "DecrementPowerStep: the power itself goes down by one, unless its SkipNextDurationTick spends the tick (PowerCmd.TickDownDuration(this))",
        ["OrbCmd.Channel"] = "ChannelStep: an orb of the kind named channeled for the owner",
        ["OrbCmd.EvokeLast"] = "EvokeLastStep: the owner's last orb evoked and dequeued",
        ["CardCmd.Upgrade"] = "UpgradeCardStep: the card played upgraded, its later plays its upgraded copy's",
        ["CardCmd.ApplyKeyword"] = "ApplyKeywordStep: a keyword added to the card played, for its later plays",
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
        // Decision #313: a potion procured or discarded is no decision the simulator follows; a card's or a potion's PotionCmd.TryToProcure is unknown
        // where a listener's class overrides AfterPotionProcured (SimAdapter.HookOnlyCommands).
        ["AfterPotionProcured"] = "a potion procured: no simulated decision procures one without its outcome being unknown where this hook is overridden",
        ["AfterPotionDiscarded"] = "a potion discarded: no simulated decision discards one",
        // CombatManager.SwitchFromPlayerToEnemySide tells the source as the extra turn it gave begins: past every simulation's horizon, whose players'
        // turn ends where Hook.ShouldTakeExtraTurn is asked (SimEngine.ExtraTurns).
        ["AfterTakingExtraTurn"] = "as the extra turn it gave begins: past the simulation's horizon, which ends where the extra turn is decided",
        // PlayerCombatState.MaxEnergy is read only as a player's turn starts (ResetEnergy, AddMaxEnergyToCurrent in CombatManager.SetupPlayerTurn), and a
        // projection of a later turn doesn't follow energy (decision #308).
        ["ModifyMaxEnergy"] = "a player's max energy, read only to set their energy as a turn starts: no simulation follows a later turn's energy",
    };

    /// <summary>
    /// The game's values a model's code sets that only show (on a card, in a tooltip): no code a fight runs reads them (AfflictionModel.Amount:
    /// Vital Spark sets its cards' Tainted amount; only the affliction's tooltip and the multiplayer checksum read it). A catalog scan holds every
    /// one to that on both branches (ShownOnlyValueTests): a game update whose code reads one fails it.
    /// </summary>
    internal static readonly IReadOnlySet<string> ShownOnly = new HashSet<string>(StringComparer.Ordinal)
    {
        "AfflictionModel.set_Amount",
        // How a creature's HP bar shows it (Steam Eruption's infinite HP as its Waterfall Giant is about to blow). Hellraiser's cap on endless auto-plays
        // reads it too (9 a turn once every enemy you could hit shows infinite HP), as the adapter reads the board, a cap no simulation reaches.
        "Creature.set_HpDisplay",
        // Whether Lagavulin Matriarch is awake, and its sleeping effect: its animator's states and its effect's Stop read them (decision #307); its
        // WakeUpMove reads IsAwake only to set it (if (!_isAwake) IsAwake = true). What it does in a fight is its Asleep power's and its move rules'.
        "LagavulinMatriarch.set_IsAwake",
        "LagavulinMatriarch.set_SleepingVfx",
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

    /// <summary>The hook runs as a side's turn starts: the enemies', or the players' next turn in a projection (decision #306).</summary>
    internal static bool StartsATurn(string? hook) => hook != null && SideTurnStartHooks.Contains(hook);

    /// <summary>The hook runs only as a player's next turn starts (CombatManager.SetupPlayerTurn's hooks), which only a projection runs (decision #306).</summary>
    internal static bool StartsAPlayersTurn(string? hook) => hook != null && PlayersTurnStartHooks.Contains(hook);

    /// <summary>
    /// A class's programs as the turn a play is in runs them (decision #306): the players' next turn's start left out (its SetupPlayerTurn hooks, and
    /// each side turn's-start step that can't run as the enemies' turn starts), which only a projection runs. What its tab, its payouts and its
    /// flags are read from: what a play and a turn's end make it do.
    /// </summary>
    private static List<HookProgram> InTheTurn(IEnumerable<HookProgram> programs, ModelKind kind) =>
        programs.Where(program => !StartsAPlayersTurn(program.Hook.ToString()))
            .Select(program => StartsATurn(program.Hook.ToString()) ? program with { Steps = program.Steps.Where(step => AsTheEnemiesTurnStarts(step.When, kind) != 0m).ToList() } : program)
            .Where(program => program.Returns != null || program.Steps.Count > 0).ToList();

    /// <summary>
    /// A condition's value as the enemies' side's turn starts, as far as it's known before the fight (null: not known): their side's turn
    /// (HookFact.EnemiesTurn); a relic's owner, a player, isn't among their participants (HookFact.TakesPart, OwnersTurn).
    /// </summary>
    private static decimal? AsTheEnemiesTurnStarts(HookValue? value, ModelKind kind)
    {
        static decimal Truth(bool holds) => holds ? 1m : 0m;
        switch (value)
        {
            case null: return 1m;
            case HookConstant constant: return constant.Value;
            case HookTest test: return AsTheEnemiesTurnStarts(test.Test, kind);
            case HookNot not: return AsTheEnemiesTurnStarts(not.Inner, kind) is { } inner ? Truth(inner == 0m) : null;
            case HookFactValue { Fact: HookFact.EnemiesTurn }: return 1m;
            case HookFactValue { Fact: HookFact.TakesPart or HookFact.OwnersTurn } when kind == ModelKind.Relic: return 0m;
            case HookMath { Operation: HookOperation.And } both:
            {
                var (left, right) = (AsTheEnemiesTurnStarts(both.Left, kind), AsTheEnemiesTurnStarts(both.Right, kind));
                return left == 0m || right == 0m ? 0m : left != null && right != null ? 1m : null;
            }
            case HookMath { Operation: HookOperation.Or } either:
            {
                var (left, right) = (AsTheEnemiesTurnStarts(either.Left, kind), AsTheEnemiesTurnStarts(either.Right, kind));
                return left is { } leftValue && leftValue != 0m || right is { } rightValue && rightValue != 0m ? 1m : left == 0m && right == 0m ? 0m : null;
            }
            default: return null;
        }
    }

    /// <summary>
    /// The hooks that run as either side's turn ends (Hook.BeforeSideTurnEnd: VeryEarly, Early, then its own; Hook.AfterSideTurnEnd, then Late): a
    /// simulation reaches both, the players' as your turn ends and the enemies' after their turn (CombatManager.EndEnemyTurnInternal).
    /// </summary>
    private static readonly HashSet<string> SideTurnEndHooks = new(StringComparer.Ordinal)
    {
        "BeforeSideTurnEndVeryEarly", "BeforeSideTurnEndEarly", "BeforeSideTurnEnd", "AfterSideTurnEnd", "AfterSideTurnEndLate",
    };

    /// <summary>The hook runs as a side's turn ends (the players' or the enemies': the side ending is a fact of the turn's end it runs at).</summary>
    internal static bool EndsATurn(string? hook) => hook != null && SideTurnEndHooks.Contains(hook);

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

    /// <summary>
    /// A class's own commands others can call: its public methods that aren't hooks, return a Task and take nothing (PoisonPower.Trigger, which
    /// Outbreak calls on each enemy's poison).
    /// </summary>
    public static IEnumerable<MethodInfo> OwnCommands(Type type) => type.GetMethods(Declared)
        .Where(method => method.IsPublic && !method.IsStatic && !method.IsVirtual && !method.IsSpecialName && method.GetParameters().Length == 0 && method.ReturnType.Name == nameof(Task))
        .OrderBy(method => method.Name, StringComparer.Ordinal);

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
            // A power's AfterRemoved runs only as it goes (PowerCmd.Remove): one that doesn't read is unknown there (UnknownStep: the simulation
            // carries why, decision #288), and its class's other hooks stay read, since what a simulation does may never remove it.
            if (gap != null && kind == ModelKind.Power && hook.Name == nameof(HookName.AfterRemoved))
            {
                readings.Add(new HookReading(hook.Name, null) { Unknown = gap.Value.Text });
                programs.Add(new HookProgram(HookName.AfterRemoved, new HookStep[] { new UnknownStep($"{type.Name}'s AfterRemoved isn't read ({gap.Value.Text})") }));
                continue;
            }
            // A hook that runs only as the players' next turn starts (decision #306: a projection's), whose body doesn't read: unknown there, the
            // class's other hooks read as they were.
            if (reader.UnknownWhereItRuns is { } unknown && program != null)
            {
                readings.Add(new HookReading(hook.Name, null) { Unknown = unknown });
                programs.Add(program);
                continue;
            }
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
        // What it pays out after the play, on what (decision #239): read from its turn-end hooks and its next turn's start, which never run.
        var nextTurn = Hooks(type).Where(hook => PlayersTurnStartHooks.Contains(hook.Name)).OrderBy(hook => hook.Name, StringComparer.Ordinal)
            .Select(hook => (hook.Name, Steps: reader.NextTurnSteps(hook))).ToList();
        // Its tab, payouts and flags are what it does through the turn a play is in: the players' next turn's start, which only a projection runs
        // (decision #306), left out of them.
        var inTheTurn = InTheTurn(programs, kind);
        // A model whose commands act during the play too (Joss Paper's draw as a card exhausts) keeps its play tab: its payouts give none.
        var actsDuringThePlay = TabOf(inTheTurn.Where(program => !TurnEndHooks.Contains(program.Hook) && !StartsATurn(program.Hook.ToString())).ToList()) != null;
        var payouts = actsDuringThePlay ? new List<Payout>() : PayoutsOf(inTheTurn, nextTurn);
        // A play hook that sets the relic's Status to Normal (RelicStatus.Normal, 0): it goes dark, the game's sign of a bonus given up.
        var darkens = kind == ModelKind.Relic && programs.Where(program => program.Hook is HookName.BeforeCardPlayed or HookName.AfterCardPlayed)
            .SelectMany(program => new ModelSpec(type.Name, kind, new[] { program }).Steps()).Any(step => step is StoreFieldStep { Field: StatusField, Value: HookConstant { Value: 0m } });
        // A store to a field no program reads changes only what the game shows (Kunai's IsActivating): left out.
        var fieldsRead = new ModelSpec(type.Name, kind, programs) { Payouts = payouts }.Values().OfType<HookField>().Select(field => field.Field).ToHashSet(StringComparer.Ordinal);
        programs = programs.Select(program => program with { Steps = program.Steps.Where(step => step is not StoreFieldStep store || fieldsRead.Contains(store.Field)).ToList() })
            .Where(program => program.Returns != null || program.Steps.Count > 0).ToList();
        inTheTurn = InTheTurn(programs, kind);
        var helpsOwner = HelpsOwnerOf(inTheTurn);
        var hurtsOwner = HurtsOwnerOf(inTheTurn);
        var hurtsPlayOwner = HurtsPlayOwnerOf(inTheTurn);
        var outcome = OutcomeOf(inTheTurn);
        // Its own commands another class calls (PoisonPower.Trigger, which Outbreak calls on each enemy's poison): each read as a program; one that
        // doesn't read whole is left out, and a call to it stays what the caller's translation makes of it.
        var commands = OwnCommands(type).Select(method => (method.Name, Steps: reader.OwnCommand(method))).Where(command => command.Steps != null)
            .Select(command => new CommandProgram(command.Name, command.Steps!)).ToList();
        // A crossed tab carries no amount (only the four core resources' plain tabs do: decision #132): a trigger, as it is for every owner of a
        // class one owner's is crossed for (a teammate's Sneaky keeps the trigger an enemy's would have). A tab judged on the outcome is its only one.
        // A payout decided where it's paid is a tab judged on the outcome, its only one; one the play decides is a crossed trigger on the play
        // that gives it up (decision #239).
        var spec = new ModelSpec(type.Name, kind, programs)
        {
            Tab = outcome != null || payouts.Any(payout => payout.DuringTheTurn == null) ? null : payouts.Count > 0 ? TabKind.Trigger
                : (helpsOwner || hurtsOwner) && TabOf(inTheTurn) != null ? TabKind.Trigger : TabOf(inTheTurn),
            // Going dark labels a bonus given up only with a payout the play decides: with none, it's a bonus spent on the play (Pael's Legion's doubled
            // block), shown by its flash beside it.
            Payouts = payouts.Count > 0 ? payouts : null, DarkensOnPlay = darkens && payouts.Any(payout => payout.DuringTheTurn != null),
            HelpsOwner = helpsOwner, HurtsOwner = hurtsOwner, HurtsPlayOwner = hurtsPlayOwner, Outcome = outcome,
            Copies = kind == ModelKind.Power ? reader.Copies() : PowerCopies.One,
            Commands = commands.Count > 0 ? commands : null,
            Tests = kind == ModelKind.Power ? reader.OwnQuestions() : null,
        };
        // Nothing else gives it a tab, and its damage term changing a damage is it acting (decision #247: Conqueror on a Sovereign Blade's hit).
        if (spec.Tab == null && outcome == null && payouts.Count == 0 && TermActsOf(inTheTurn)) spec = spec with { Tab = TabKind.Trigger, TermActs = true };
        return new ModelReading(spec, readings);
    }

    /// <summary>
    /// A Modify damage hook of its code returns a term (ModifyDamageAdditive, Multiplicative, MultiplicativeLate, Cap) and it answers nothing where
    /// the game tells it the term changed a damage (no AfterModifyingDamageAmount of its own): the term changing a damage is what it does there.
    /// </summary>
    internal static bool TermActsOf(IReadOnlyList<HookProgram> programs) =>
        programs.Any(program => program.Returns != null && program.Hook is HookName.ModifyDamageAdditive or HookName.ModifyDamageMultiplicative or HookName.ModifyDamageMultiplicativeLate or HookName.ModifyDamageCap)
        && programs.All(program => program.Hook != HookName.AfterModifyingDamageAmount)
        // A Modify block hook's term (Pael's Legion's doubled block): the game tells it changed a block (Hook.AfterModifyingBlockAmount), whatever its code
        // does there (decision #269).
        || programs.Any(program => program.Returns != null && program.Hook is HookName.ModifyBlockAdditive or HookName.ModifyBlockMultiplicative)
        // Where a played card goes (Nostalgia's, on top of the draw pile): the game tells it changed the location (Hook.AfterModifyingCardPlayResultLocation),
        // whatever its code does there (decision #290).
        || programs.Any(program => program.Returns != null && program.Hook is HookName.ModifyCardPlayResultLocation);

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

    /// <summary>The game's affliction classes among <paramref name="types"/>, but the game's own test mocks (IsMock: never on a card in a run).</summary>
    public static IEnumerable<Type> Afflictions(IEnumerable<Type> types) =>
        types.Where(type => type.Namespace == "MegaCrit.Sts2.Core.Models.Afflictions" && !type.IsNested && !type.IsAbstract && Derives(type, "AfflictionModel"));

    /// <summary>
    /// An affliction's hooks that act as it's put on or taken off its card, or decide which cards it can afflict (AfflictionModel's): the card the
    /// adapter reads shows it afflicted or not (SimCard.Affliction), and a card afflicted in a fight is its command's (CardCmd.Afflict).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AfflictionNotRun = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["CanAfflict"] = "it decides which cards it can afflict, as a card is afflicted",
        ["CanAfflictCardType"] = "it decides which cards it can afflict, as a card is afflicted",
        ["AfterApplied"] = "it acts as it's put on its card: the card the adapter reads shows it",
        ["BeforeRemoved"] = "it acts as it's taken off its card: the card the adapter reads shows it",
    };

    /// <summary>
    /// An affliction's code read (decision #225): what it does to its card is a fact of the card (SimCard.Affliction, which the powers' code tests:
    /// Galvanic's cardPlay.Card.Affliction is Galvanized); its hooks that act as it's put on or taken off (AfflictionNotRun) and its
    /// AfterCardEnteredCombat on its own card entering (Hexed's: no simulated play plays a card that entered during the simulation) aren't run. Any
    /// other hook (an OnPlay: CardModel.OnPlayWrapper runs it after the enchantment's) is a gap: the engine runs no affliction's program yet, so an
    /// affliction a game update gives one doesn't read whole, and a card with it plays unknown.
    /// </summary>
    public static ModelReading ReadAffliction(Type type, Func<MethodBase, List<CodeOp>> read)
    {
        var reader = new Translation(type, ModelKind.Affliction, read);
        var readings = new List<HookReading>();
        foreach (var hook in Hooks(type).OrderBy(method => method.Name, StringComparer.Ordinal))
        {
            if (AfflictionNotRun.TryGetValue(hook.Name, out var notRun)) readings.Add(new HookReading(hook.Name, null) { NotRun = notRun });
            else if (hook.Name == "AfterCardEnteredCombat" && reader.ActsOnItsOwnCardOnly(hook))
                readings.Add(new HookReading(hook.Name, null) { NotRun = "it acts on its own card as it enters the fight, which no simulated play plays" });
            else readings.Add(new HookReading(hook.Name, $"gap: the engine runs no read AfflictionModel's {hook.Name}") { Capability = ReaderCapability.EffectNotRun });
        }
        return new ModelReading(new ModelSpec(type.Name, ModelKind.Affliction, Array.Empty<HookProgram>()), readings);
    }

    /// <summary>The game's orb classes among <paramref name="types"/>.</summary>
    public static IEnumerable<Type> Orbs(IEnumerable<Type> types) => types.Where(type => type.Namespace == "MegaCrit.Sts2.Core.Models.Orbs" && !type.IsNested && !type.IsAbstract && Derives(type, "OrbModel"));

    /// <summary>The engine's kind for an orb class (its name less "Orb", as ChannelStep names it), or null for a class the engine has no kind for.</summary>
    public static OrbKind? OrbKindOf(string orbClass) =>
        Enum.TryParse<OrbKind>(orbClass.EndsWith("Orb", StringComparison.Ordinal) ? orbClass[..^3] : orbClass, out var kind) && kind is not (OrbKind.Unknown or OrbKind.Random) ? kind : null;

    /// <summary>
    /// What PowerCmd does with a power class's amounts, read from its code (PowerRule, decision #234): its AllowNegative, and, when its
    /// ShouldScaleInMultiplayer is true, the value its GetScaledAmountForMultiplayer returns (its own or PowerModel's), or why it can't be read.
    /// </summary>
    public static PowerRule RuleOf(Type type, Func<MethodBase, List<CodeOp>> read)
    {
        var reader = new Translation(type, ModelKind.Power, read);
        try
        {
            var (debuff, counter) = reader.Kind();
            var (allowNegative, scaled) = reader.Rule();
            return new PowerRule(type.Name) { Debuff = debuff, Counter = counter, AllowNegative = allowNegative, ScaledForPlayers = scaled, Visibility = reader.Visibility() };
        }
        catch (Exception exception) when (exception is Gap or InvalidOperationException or NotSupportedException)
        {
            var (unreadDebuff, unreadCounter) = reader.Kind();
            return new PowerRule(type.Name)
            {
                Debuff = unreadDebuff, Counter = unreadCounter, Visibility = reader.Visibility(),
                Unread = $"{type.Name}'s scaling for the players couldn't be read from the game's code (a game update?): {exception.Message}",
            };
        }
    }

    /// <summary>
    /// An orb's code read whole (decision #214): its Passive and its Evoke as programs, when its passive goes off (the trigger hook that calls
    /// OrbModel.TriggerPassive), its values' getters (PassiveVal, EvokeVal: OrbModel's abstract values) and its own fields as its constructor starts
    /// them. Anything else it overrides, or a body the reader stops in, is a gap.
    /// </summary>
    public static ModelReading ReadOrb(Type type, Func<MethodBase, List<CodeOp>> read)
    {
        var reader = new Translation(type, ModelKind.Orb, read);
        var programs = new List<HookProgram>();
        var readings = new List<HookReading>();
        OrbTrigger? passiveAt = null;
        var kind = OrbKindOf(type.Name);
        if (kind == null) readings.Add(new HookReading("(the class)", $"gap: the engine has no orb kind for {type.Name}") { Capability = ReaderCapability.Orbs });
        foreach (var hook in Hooks(type).OrderBy(method => method.Name, StringComparer.Ordinal))
        {
            // A trigger hook (OrbQueue calls it): the passive goes off then, when its body is OrbModel.TriggerPassive with no target.
            if (OrbTriggers.TryGetValue(hook.Name, out var trigger))
            {
                if (reader.CallsTriggerPassive(hook)) { passiveAt = trigger; readings.Add(new HookReading(hook.Name, null)); }
                else readings.Add(new HookReading(hook.Name, $"gap: {hook.Name} does more than OrbModel.TriggerPassive") { Capability = ReaderCapability.OwnMethod });
                continue;
            }
            var (program, gap, notRun) = reader.Hook(hook);
            readings.Add(new HookReading(hook.Name, gap?.Text) { NotRun = notRun, Capability = gap?.Capability });
            if (program != null) programs.Add(program);
        }
        var (values, aliases, starts, valueGap) = reader.OrbValuesOf();
        if (valueGap != null) readings.Add(new HookReading("(its values)", valueGap.Value.Text) { Capability = valueGap.Value.Capability });
        // An own field no program reads, but a value's getter (Glass's _passiveVal in its PassiveVal) is still its state: every store to it is kept.
        var spec = new ModelSpec(type.Name, ModelKind.Orb, programs) { Orb = kind, PassiveAt = passiveAt, OrbValues = values, OrbAliases = aliases, Starts = starts };
        return new ModelReading(spec, readings);
    }

    /// <summary>The hooks OrbQueue calls an orb's passive from (OrbModel's virtual trigger methods), and when each runs.</summary>
    private static readonly IReadOnlyDictionary<string, OrbTrigger> OrbTriggers = new Dictionary<string, OrbTrigger>(StringComparer.Ordinal)
    {
        ["BeforeTurnEndOrbTrigger"] = OrbTrigger.TurnEnd,
        ["AfterTurnStartOrbTrigger"] = OrbTrigger.TurnStart,
    };

    /// <summary>The game's enchantment classes among <paramref name="types"/>.</summary>
    public static IEnumerable<Type> Enchantments(IEnumerable<Type> types) => types.Where(type => type.Namespace == "MegaCrit.Sts2.Core.Models.Enchantments" && !type.IsNested && !type.IsAbstract && Derives(type, "EnchantmentModel"));

    /// <summary>
    /// The hooks of an enchantment the engine runs from its code (decision #217): its OnPlay after each play's own effects (CardModel.OnPlayWrapper),
    /// its AfterCardPlayed after the play's listeners, for its own card's play.
    /// </summary>
    private static readonly HashSet<string> EnchantmentRuns = new(StringComparer.Ordinal) { "OnPlay", "AfterCardPlayed" };

    /// <summary>An enchantment's hooks that act where the simulator never runs a read program, each with where it acts instead.</summary>
    private static readonly IReadOnlyDictionary<string, string> EnchantmentNotRun = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["EnchantDamageAdditive"] = "its card's damage math: the adapter runs the game's own code (CapturedTerms.EnchantAdd)",
        ["EnchantDamageMultiplicative"] = "its card's damage math: the adapter runs the game's own code (CapturedTerms.EnchantMul)",
        ["EnchantBlockAdditive"] = "its card's block math: the adapter runs the game's own code (CapturedTerms.EnchantAdd)",
        ["EnchantBlockMultiplicative"] = "its card's block math: the adapter runs the game's own code (CapturedTerms.EnchantMul)",
        ["CanEnchant"] = "it decides which cards it can enchant, as a card is enchanted",
        ["CanEnchantCardType"] = "it decides which cards it can enchant, as a card is enchanted",
        ["OnEnchant"] = "it changes its card as it's enchanted (EnchantmentModel.ModifyCard): the card the adapter reads already shows it; a card enchanted in a fight is its command's (CardCmd.Enchant)",
    };

    /// <summary>
    /// An enchantment's code read (decision #217): its OnPlay and AfterCardPlayed as programs, its values its RecalculateValues sets from its amount
    /// (Adroit's Block is its Amount) read as what they're set to; the hooks that act where no read program runs (its card's damage and block math,
    /// which the adapter runs live; what it can enchant; its OnEnchant, which the card already shows) not run. Any other hook, or a body the reader
    /// stops in, is a gap.
    /// </summary>
    public static ModelReading ReadEnchantment(Type type, Func<MethodBase, List<CodeOp>> read)
    {
        var reader = new Translation(type, ModelKind.Enchantment, read);
        var programs = new List<HookProgram>();
        var readings = new List<HookReading>();
        var setVars = new Dictionary<string, HookValue>(StringComparer.Ordinal);
        var onTop = false;
        foreach (var hook in Hooks(type).OrderBy(method => method.Name, StringComparer.Ordinal))
        {
            if (EnchantmentNotRun.TryGetValue(hook.Name, out var notRun)) { readings.Add(new HookReading(hook.Name, null) { NotRun = notRun }); continue; }
            if (hook.Name == "RecalculateValues")
            {
                // Each of its own values set from what it reads (base.DynamicVars.Block.BaseValue = base.Amount): every read of it is that value.
                var steps = reader.OwnCommand(hook);
                if (steps != null && steps.All(step => step is StoreFieldStep { When: null, Field: var field } && field.StartsWith(VarFieldPrefix, StringComparison.Ordinal)))
                {
                    foreach (var store in steps.Cast<StoreFieldStep>()) setVars[store.Field[VarFieldPrefix.Length..]] = store.Value;
                    readings.Add(new HookReading(hook.Name, null));
                }
                else readings.Add(new HookReading(hook.Name, "gap: RecalculateValues does more than set its own values") { Capability = ReaderCapability.OtherModelValueSet });
                continue;
            }
            if (reader.ChangesCostsAtFlushOnly(hook)) { readings.Add(new HookReading(hook.Name, null) { NotRun = CostsAtFlush }); continue; }
            // Its card moved to the front of what a shuffle puts into the draw pile (Perfect Fit's): a fact of its card the engine's shuffle reads.
            if (hook.Name == "ModifyShuffleOrder" && reader.PutsItsCardOnTop(hook))
            {
                onTop = true;
                readings.Add(new HookReading(hook.Name, null));
                continue;
            }
            if (!EnchantmentRuns.Contains(hook.Name))
            {
                readings.Add(new HookReading(hook.Name, $"gap: the engine runs no read EnchantmentModel's {hook.Name}") { Capability = ReaderCapability.EffectNotRun });
                continue;
            }
            var (program, gap, notRunHere) = reader.Hook(hook);
            readings.Add(new HookReading(hook.Name, gap?.Text) { NotRun = notRunHere, Capability = gap?.Capability });
            if (program != null) programs.Add(program);
        }
        if (setVars.Count > 0)
            programs = programs.Select(program => Mapped(program, value => value switch
            {
                HookVar variable when setVars.TryGetValue(variable.Key, out var set) => set,
                HookField { Field: var field } when field.StartsWith(VarFieldPrefix, StringComparison.Ordinal) && setVars.TryGetValue(field[VarFieldPrefix.Length..], out var set) => set,
                _ => null,
            })).ToList();
        // A store to its own state none of its read programs reads is left out: what the game shows, or what its own card math reads, which the
        // adapter runs live (Momentum's ExtraDamage, Vigorous' Status).
        // A store that only feeds itself (Momentum's ExtraDamage += Amount) is read by nothing here.
        var fieldsRead = new ModelSpec(type.Name, ModelKind.Enchantment, programs.Select(program => program with
            {
                Steps = program.Steps.Select(step => step is StoreFieldStep store ? store with { Value = Mapped(new HookProgram(program.Hook, Array.Empty<HookStep>()) { Returns = store.Value },
                    value => value is HookField { Field: var read } && read == store.Field ? new HookConstant(0m) : null).Returns! } : step).ToList(),
            }).ToList())
            .Values().OfType<HookField>().Select(field => field.Field).ToHashSet(StringComparer.Ordinal);
        programs = programs.Select(program => program with { Steps = program.Steps.Where(step => step is not StoreFieldStep store || fieldsRead.Contains(store.Field)).ToList() })
            .Where(program => program.Steps.Count > 0).ToList();
        return new ModelReading(new ModelSpec(type.Name, ModelKind.Enchantment, programs) { OnTopWhenShuffled = onTop }, readings);
    }

    /// <summary>Why a hook that only changes card costs as the hand is flushed isn't run (Translation.ChangesCostsAtFlushOnly).</summary>
    private const string CostsAtFlush = "it only changes card costs as the hand is flushed (Hook.BeforeFlush: after the turn's auto-plays and its end steps), which nothing a simulation reads after it";

    /// <summary>The prefix of one of a model's own values kept as its state ("Var.CardsLeft"), where its code stores it.</summary>
    public const string VarFieldPrefix = HookField.VarPrefix;

    /// <summary>A program with each read of a value its model stores (HookVar) read as that state (HookField "Var.key", from its canonical value).</summary>
    private static HookProgram VarsAsState(HookProgram program, IReadOnlySet<string> stored) =>
        Mapped(program, value => value is HookVar variable && stored.Contains(VarFieldPrefix + variable.Key) ? new HookField(VarFieldPrefix + variable.Key) { Canonical = variable.Canonical } : null);

    /// <summary>A program with each value <paramref name="replace"/> gives another for (null: kept, looked inside) replaced, wherever it's worked out.</summary>
    private static HookProgram Mapped(HookProgram program, Func<HookValue, HookValue?> replace)
    {
        HookValue? Map(HookValue? value) => value == null ? null : replace(value) ?? value switch
        {
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
            LoseBlockStep loss => loss with { Amount = Map(loss.Amount)! },
            SummonStep summon => summon with { Amount = Map(summon.Amount)! },
            SetHpStep setHp => setHp with { Amount = Map(setHp.Amount)! },
            LoseMaxHpStep maxHpLoss => maxHpLoss with { Amount = Map(maxHpLoss.Amount)! },
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
        // A stun of its owner, or its monster's state set, on a damage its owner deals (AfterDamageGiven, waiting on dealer == base.Owner): its own
        // attack's, after the play (Imbalanced's).
        static bool DealerIsOwner(HookFactValue fact) => fact.Fact == HookFact.DealerIsOwner
            || fact.Fact == HookFact.SameCreature && fact.Creature is { } one && fact.Other is { } other && !one.PetOwner && !other.PetOwner
               && new[] { one.Who, other.Who }.OrderBy(who => who).SequenceEqual(new[] { HookWho.Owner, HookWho.Dealer }.OrderBy(who => who));
        if (programs.Where(program => program.Hook == HookName.AfterDamageGiven).SelectMany(program => new ModelSpec("", ModelKind.Power, new[] { program }).Steps())
                .Where(step => step is StunStep or MonsterStateStep).ToList() is { Count: > 0 } ownHit && ownHit.All(step => Requires(step.When, DealerIsOwner, holds: true)))
            return HookOutcome.StunsOnItsOwnHit;
        // A stun of its owner (an enemy's) in a hook a play sets off: the play that stuns it.
        if (programs.Where(program => program.Hook != HookName.AfterDeath).Any(program => new ModelSpec("", ModelKind.Power, new[] { program }).Steps().Any(step => step is StunStep)))
            return HookOutcome.Stuns;
        // A kill of its owner (an enemy's) in a hook a play sets off: the play that kills it so.
        if (programs.Where(program => program.Hook != HookName.AfterDeath).Any(program => new ModelSpec("", ModelKind.Power, new[] { program }).Steps().Any(step => step is KillStep)))
            return HookOutcome.KillsItself;
        // A block its owner (or its pet) breaks, on another creature: every AfterBlockBroken step waits on the breaker (Hand Drill's Vulnerable).
        static bool NamesBreaker(HookFactValue fact) => fact.Creature?.Who == HookWho.Breaker || fact.Other?.Who == HookWho.Breaker;
        if (programs.Where(program => program.Hook == HookName.AfterBlockBroken).SelectMany(program => new ModelSpec("", ModelKind.Power, new[] { program }).Steps()).ToList() is { Count: > 0 } broken
            && broken.All(step => EveryWayNeeds(step.When, NamesBreaker)))
            return HookOutcome.BreaksBlock;
        return ReactsToHpLoss(programs) ? HookOutcome.ReactsToHpLoss : null;
    }

    /// <summary>The condition holds only through a fact that <paramref name="matches"/> holding: one of its ands, on each of its ors' ways.</summary>
    private static bool EveryWayNeeds(HookValue? when, Func<HookFactValue, bool> matches) => when switch
    {
        HookFactValue fact => matches(fact),
        HookTest test => EveryWayNeeds(test.Test, matches),
        HookMath { Operation: HookOperation.And } both => EveryWayNeeds(both.Left, matches) || EveryWayNeeds(both.Right, matches),
        HookMath { Operation: HookOperation.Or } either => EveryWayNeeds(either.Left, matches) && EveryWayNeeds(either.Right, matches),
        // A value of several ways tested (breaker?.PetOwner == base.Owner: null where there's no breaker): each way that can hold needs it.
        HookWays ways => ways.Ways.All(way => way.Value is HookConstant { Value: 0m } || EveryWayNeeds(way.When, matches) || EveryWayNeeds(way.Value, matches)),
        _ => false,
    };

    /// <summary>The relic's Status (RelicModel.Status: Normal 0, Active 1), as a store of its code names it.</summary>
    private const string StatusField = "Status";

    /// <summary>The hooks of the players' turn's end, in the game's order (decision #51).</summary>
    private static readonly HookName[] TurnEndHooks =
        { HookName.BeforeSideTurnEndVeryEarly, HookName.BeforeSideTurnEndEarly, HookName.BeforeSideTurnEnd, HookName.AfterSideTurnEnd, HookName.AfterSideTurnEndLate };

    /// <summary>
    /// What a model pays out after the play (decision #239): each command that gives (block for its owner, damage to the enemies, energy, cards, an
    /// orb, a heal, stars, a buff on its owner or a debuff on the enemies) in a hook of the players' turn's end or of their next turn's start
    /// (<paramref name="nextTurn"/>: those hooks' commands, null where the reader stops), on a test of its code beyond its owner taking part:
    /// that test is the payout's condition. A payout whose condition only a play changes (DuringTheTurn) is decided by the play; one in the
    /// next turn's start that isn't can't be judged from a simulation (what it reads then isn't what the simulation ends with): left out.
    /// </summary>
    internal static List<Payout> PayoutsOf(IReadOnlyList<HookProgram> programs, IEnumerable<(string Hook, IReadOnlyList<HookStep>? Steps)> nextTurn)
    {
        var payouts = new List<Payout>();
        void Add(string hook, IEnumerable<HookStep> steps, bool next, int turnEndHooksBefore)
        {
            foreach (var step in steps)
            {
                if (!Gives(step) || step.When is not { } when || OnlyTakesPart(when) || payouts.Any(payout => payout.Hook == hook && Equals(payout.Condition, when))) continue;
                // A step its code runs only as the enemies' side's turn ends (side == CombatSide.Enemy) pays nothing as yours does.
                if (Requires(when, fact => fact.Fact == HookFact.EnemiesTurn, holds: true)) continue;
                var during = DuringTheTurn(when, programs, turnEndHooksBefore, next);
                // Decided during the turn by nothing a play changes (Stone Calendar's turn number): no play gives it up or sets it up.
                if (during != null && !PlayChanges(during, programs)) continue;
                payouts.Add(new Payout(hook, when) { NextTurn = next, DuringTheTurn = during });
            }
        }
        for (var i = 0; i < TurnEndHooks.Length; i++)
            if (programs.FirstOrDefault(program => program.Hook == TurnEndHooks[i]) is { } program) Add(program.Hook.ToString(), program.Steps, next: false, i);
        foreach (var (hook, steps) in nextTurn)
            if (steps != null) Add(hook, steps, next: true, TurnEndHooks.Length);
        return payouts.Where(payout => !payout.NextTurn || payout.DuringTheTurn != null).ToList();
    }

    /// <summary>A command that gives its owner's side something (a payout's command).</summary>
    private static bool Gives(HookStep step) => step switch
    {
        GainBlockStep { To: null } or GainEnergyStep or DrawStep or ChannelStep or HealStep or GainStarsStep or SummonStep => true,
        DamageStep damage => damage.Target is not (HookTarget.Owner or HookTarget.PlayOwner or HookTarget.LivingPlayers),
        ApplyPowerStep apply => apply.Target == HookTarget.Owner ? !apply.Debuff : apply.Target is HookTarget.AllEnemies or HookTarget.LivingOpponents && apply.Debuff,
        _ => false,
    };

    /// <summary>
    /// A condition that only asks its owner takes part (or that the hook's player is its owner, or that the side ending is the players':
    /// side == CombatSide.Player, the players' turn's end the payouts are about): no condition of the payout's own.
    /// </summary>
    private static bool OnlyTakesPart(HookValue when) => when switch
    {
        HookTest test => OnlyTakesPart(test.Test),
        HookFactValue { Fact: HookFact.TakesPart or HookFact.EventIsOwners } => true,
        HookNot { Inner: HookFactValue { Fact: HookFact.EnemiesTurn } } => true,
        HookMath { Operation: HookOperation.And } both => OnlyTakesPart(both.Left) && OnlyTakesPart(both.Right),
        _ => false,
    };

    /// <summary>
    /// A payout's condition as it stands during the turn: the turn's end's stores before the payout (its first <paramref name="turnEndHooksBefore"/>
    /// hooks), each on its owner taking part, put in place of the fields they store (backwards, as the code runs them forwards), when what it then
    /// reads only a play changes: its own fields that only its play hooks (and the turn's end) store, the fight's log of this turn's card plays
    /// (not for a next turn's payout), its owner taking part, the hook's player, the turn number, constants and its values. Null when it reads
    /// anything else (block, the hand, energy, a hit) or a store it waits on can't be put in place.
    /// </summary>
    private static HookValue? DuringTheTurn(HookValue condition, IReadOnlyList<HookProgram> programs, int turnEndHooksBefore, bool nextTurn)
    {
        for (var i = turnEndHooksBefore - 1; i >= 0; i--)
        {
            if (programs.FirstOrDefault(program => program.Hook == TurnEndHooks[i]) is not { } program) continue;
            foreach (var store in program.Steps.OfType<StoreFieldStep>().Reverse())
            {
                if (!Reads(condition, store.Field)) continue;
                if (store.When != null && !OnlyTakesPart(store.When)) return null;
                condition = Substituted(condition, store.Field, store.Value);
            }
        }
        var storedBy = programs.SelectMany(program => new ModelSpec("", ModelKind.Power, new[] { program }).Steps().OfType<StoreFieldStep>().Select(store => (program.Hook, store.Field))).ToList();
        bool Allowed(HookValue value) => value switch
        {
            HookConstant or HookVar => true,
            HookFactValue fact => fact.Fact is HookFact.TakesPart or HookFact.EventIsOwners or HookFact.TurnNumber or HookFact.EnemiesTurn,
            HookField field => storedBy.Where(stored => stored.Field == field.Field)
                .All(stored => stored.Hook is HookName.BeforeCardPlayed or HookName.AfterCardPlayed || TurnEndHooks.Contains(stored.Hook)),
            HookHistory history => !nextTurn && history.Query.Kind is HistoryKind.CardPlayStarted or HistoryKind.CardPlayFinished,
            HookTest test => Allowed(test.Test),
            HookNot not => Allowed(not.Inner),
            HookMath math => Allowed(math.Left) && Allowed(math.Right),
            _ => false,
        };
        return Allowed(condition) ? condition : null;
    }

    /// <summary>A condition during the turn reads what a play changes: a field a play hook of its own stores, or the fight's log of card plays.</summary>
    private static bool PlayChanges(HookValue during, IReadOnlyList<HookProgram> programs)
    {
        var values = new ModelSpec("", ModelKind.Power, Array.Empty<HookProgram>()) { Payouts = new[] { new Payout("", during) } }.Values().ToList();
        var storedOnAPlay = programs.Where(program => program.Hook is HookName.BeforeCardPlayed or HookName.AfterCardPlayed)
            .SelectMany(program => new ModelSpec("", ModelKind.Power, new[] { program }).Steps().OfType<StoreFieldStep>()).Select(store => store.Field).ToHashSet(StringComparer.Ordinal);
        return values.Any(value => value is HookHistory || value is HookField field && storedOnAPlay.Contains(field.Field));
    }

    /// <summary>The value reads the field.</summary>
    private static bool Reads(HookValue value, string field) => new ModelSpec("", ModelKind.Power, Array.Empty<HookProgram>()) { Payouts = new[] { new Payout("", value) } }
        .Values().OfType<HookField>().Any(read => read.Field == field);

    /// <summary>The value with what a store put in a field in place of the field.</summary>
    private static HookValue Substituted(HookValue value, string field, HookValue stored) => value switch
    {
        HookField read when read.Field == field => stored,
        HookTest test => test with { Test = Substituted(test.Test, field, stored) },
        HookNot not => new HookNot(Substituted(not.Inner, field, stored)),
        HookMath math => math with { Left = Substituted(math.Left, field, stored), Right = Substituted(math.Right, field, stored) },
        HookWays ways => new HookWays(ways.Ways.Select(way => new HookWay(Substituted(way.When, field, stored), Substituted(way.Value, field, stored))).ToList()),
        _ => value,
    };

    /// <summary>
    /// Its AfterDamageReceived acts (a step that isn't a store) on its owner losing HP, every step waiting on target == base.Owner and
    /// result.UnblockedDamage &gt; 0 (or &gt;= 1), and nothing it does is a tab's amount (block, damage, cards drawn, energy: those are the play
    /// log's tabs, Centennial Puzzle's draw): its tab is judged on how the turn ends (HookOutcome.ReactsToHpLoss).
    /// </summary>
    internal static bool ReactsToHpLoss(IReadOnlyList<HookProgram> programs)
    {
        if (programs.FirstOrDefault(program => program.Hook == HookName.AfterDamageReceived) is not { } received) return false;
        var steps = new ModelSpec("", ModelKind.Power, new[] { received }).Steps().ToList();
        static bool LostHp(HookValue value) => value is HookMath { Left: HookFactValue { Fact: HookFact.HitUnblocked } } math
            && (math.Operation == HookOperation.Greater && math.Right is HookConstant { Value: 0m } || math.Operation == HookOperation.GreaterOrEqual && math.Right is HookConstant { Value: 1m });
        static bool Needs(HookValue? when, Func<HookValue, bool> matches) => when switch
        {
            HookTest test => matches(test.Test) || Needs(test.Test, matches),
            HookMath { Operation: HookOperation.And } both => Needs(both.Left, matches) || Needs(both.Right, matches),
            // Each way of an or needs it (Rupture's apply on its two ways, a card's or none).
            HookMath { Operation: HookOperation.Or } either => Needs(either.Left, matches) && Needs(either.Right, matches),
            _ => when != null && matches(when),
        };
        return steps.Any(step => step is not StoreFieldStep)
            && steps.All(step => Needs(step.When, value => value is HookFactValue { Fact: HookFact.TargetIsOwner }) && Needs(step.When, LostHp))
            && TabOf(programs) is null or TabKind.Trigger;
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
                case SummonStep: return TabKind.Summon;
                // Cards made into a player's draw pile: how many (decision #161), unless its crossing makes it a trigger.
                case MakeIntoDrawStep: return TabKind.CardsMade;
            }
        // A pass of the HP-loss pipeline changes the HP a hit takes: it acts (the hand-written models' trigger: Tungsten Rod, The Boot). A power an
        // attack spends (its AfterAttack takes its own stacks: Gigantification's Decrement) acts on that play (decision #228).
        return steps.Any(step => step is ApplyPowerStep or ChannelStep or GainStarsStep or UpgradeCardStep or UpgradeRandomInHandStep or ApplyKeywordStep or FreeInHandStep or MakeCopyStep or MakeRandomInHandStep)
            || programs.Any(program => program.Returns != null && program.Hook is HookName.ModifyHpLostBeforeOsty or HookName.ModifyHpLostBeforeOstyLate or HookName.ModifyHpLostAfterOsty or HookName.ModifyHpLostAfterOstyLate)
            || programs.Any(SpentByAnAttack)
            // A term that changed a damage, told so (Hook.AfterModifyingDamageAmount: Slow's, Hard to Kill's flash): it acts there (decision #229).
            || programs.Any(program => program.Hook == HookName.AfterModifyingDamageAmount)
            // A play that changes the state its damage term reads (Slow's count of the cards played) acts on that play.
            || ChangesItsTermOnAPlay(programs)
            ? TabKind.Trigger : null;
    }

    /// <summary>A play hook stores to state a damage term of its own reads (Slow's SlowAmount, which its ModifyDamageMultiplicative reads).</summary>
    private static bool ChangesItsTermOnAPlay(IReadOnlyList<HookProgram> programs)
    {
        var termFields = ModelSpec.TermFieldsOfAPlay(programs);
        return programs.Where(program => program.Hook is HookName.BeforeCardPlayed or HookName.AfterCardPlayed)
            .SelectMany(program => new ModelSpec("", ModelKind.Power, new[] { program }).Steps()).Any(step => step is StoreFieldStep store && termFields.Contains(store.Field));
    }

    /// <summary>An AfterAttack that takes the power's own stacks (Decrement, Remove: Gigantification's, Quietus'): the attack spent it, the power acting.</summary>
    internal static bool SpentByAnAttack(HookProgram program) =>
        program.Hook == HookName.AfterAttack && new ModelSpec("", ModelKind.Power, new[] { program }).Steps().Any(step => step is DecrementPowerStep or RemovePowerStep);

    /// <summary>
    /// A power an attack spends: its AfterAttack takes its own stacks (Decrement, Remove: Gigantification's) or changes its own amount
    /// (PowerCmd.ModifyAmount(this, ...): Vigor's, by what it started the attack with).
    /// </summary>
    internal static bool SpentByAnAttack(ModelSpec spec) =>
        spec.Kind == ModelKind.Power && spec.Of(HookName.AfterAttack) is { } afterAttack
        && (SpentByAnAttack(afterAttack) || new ModelSpec(spec.Name, spec.Kind, new[] { afterAttack }).Steps().Any(step => step is ApplyPowerStep { Target: HookTarget.Owner } own && own.Power == spec.Name));

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
            .Any(step => step is ApplyPowerStep { Target: HookTarget.Owner, Debuff: true })
        // A Status or a Curse made into its owner's own draw pile (Biiig Hug's Soot on a shuffle a play sets off).
        || new ModelSpec("", ModelKind.Power, programs).Steps().Any(step => step is MakeIntoDrawStep { Harms: true } make && MadeFor(make).All(creature => creature is { Who: HookWho.Owner, PetOwner: false }));

    /// <summary>
    /// A play sets off something that hurts the player who made it: a play hook's damage or debuff on the card's owner (Galvanic's damage to
    /// whoever plays a Galvanized card). That's the play hurting you, whoever owns the model: a crossed tab (CLAUDE.md "Plain vs crossed",
    /// ReadModel.HarmfulFor), its own kind of tab kept (a damage tab's amount).
    /// </summary>
    internal static bool HurtsPlayOwnerOf(IReadOnlyList<HookProgram> programs) =>
        programs.Where(program => program.Hook is HookName.BeforeCardPlayed or HookName.AfterCardPlayed)
            .SelectMany(program => new ModelSpec("", ModelKind.Power, new[] { program }).Steps())
            .Any(step => step is DamageStep { Target: HookTarget.PlayOwner } or ApplyPowerStep { Target: HookTarget.PlayOwner, Debuff: true })
        // A hit that sets off damage back to its dealer (Thorns' BeforeDamageReceived, Flame Barrier's and Reflect's AfterDamageReceived): the play's
        // attack hurts the player who made it (or their pet).
        || programs.Where(program => program.Hook is HookName.BeforeDamageReceived or HookName.AfterDamageReceived)
            .SelectMany(program => new ModelSpec("", ModelKind.Power, new[] { program }).Steps())
            .Any(step => step is DamageStep { Target: HookTarget.Dealer }
                // A Status or a Curse made into the draw pile of the hit's dealer (or its pet's player: Personal Hive's Dazed).
                || step is MakeIntoDrawStep { Harms: true } make && MadeFor(make).All(creature => creature is { Who: HookWho.Dealer }));

    /// <summary>The creatures a made card's player is, on each way its code sets it (MakeIntoDrawStep.For).</summary>
    private static IEnumerable<HookCreature?> MadeFor(MakeIntoDrawStep make) => make.For switch
    {
        HookWays ways => ways.Ways.Select(way => (way.Value as HookFactValue)?.Creature),
        HookFactValue fact => new[] { fact.Creature },
        _ => new HookCreature?[] { null },
    };

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

        /// <summary>The stores of the body being read (a local kept before one of them, used after it, is the value as it was then).</summary>
        private List<StoreStep> _stores = new();

        /// <summary>The place of the step being read (its offset in its method).</summary>
        private int _at;

        /// <summary>The locals a hook body keeps before a store (each a LetStep at its place); null where none can be kept (a value's getter).</summary>
        private List<(string Name, int Offset, HookValue Value)>? _lets;

        /// <summary>An orb's body is being read: its values (this.PassiveVal) are the values as shown (HookOrbProperty), not their getters' code.</summary>
        private bool _shownValues;

        /// <summary>OrbModel's own values (its abstract decimal properties: PassiveVal, EvokeVal), read from the base class.</summary>
        private IReadOnlySet<string> OrbValueProperties()
        {
            for (var type = _type.BaseType; type != null; type = type.BaseType)
                if (type.Name == "OrbModel")
                    return type.GetProperties(Declared).Where(property => property.GetMethod is { IsAbstract: true } && property.PropertyType.Name == "Decimal")
                        .Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
            return new HashSet<string>();
        }

        /// <summary>A method of the model's base class whose code returns Hook.<paramref name="hook"/>'s value (OrbModel.ModifyOrbValue: Hook.ModifyOrbValue).</summary>
        private bool CallsHook(string method, string hook)
        {
            for (var type = _type.BaseType; type != null; type = type.BaseType)
                if (type.GetMethods(Declared).FirstOrDefault(candidate => candidate.Name == method && !candidate.IsAbstract) is { } found)
                {
                    try { return ProgramReader.ReadReturnedValue(type, found, _read) is QueryValue { Type: "Hook", Name: var called } && called == hook; }
                    catch (InvalidOperationException) { return false; }
                }
            return false;
        }

        /// <summary>
        /// An orb's trigger hook whose code only calls OrbModel.TriggerPassive with no target (TriggerPassive(choiceContext, null)): its passive goes
        /// off then, Hook.ModifyOrbPassiveTriggerCount's count of times (the engine's rule for OrbModel.TriggerPassive).
        /// </summary>
        public bool CallsTriggerPassive(MethodInfo hook)
        {
            List<CodeOp> ops;
            try { ops = _read(ProgramReader.BodyOf(hook)); }
            catch { return false; }
            var call = ops.FindIndex(instruction => instruction.IsCall && instruction.Member is { Name: "TriggerPassive" });
            if (call < 1 || ops[call - 1].OpCode != System.Reflection.Emit.OpCodes.Ldnull) return false;
            var program = ProgramReader.Read(_type, _read, hook.Name);
            return !program.All().Any() && !program.Unknowns.Any()
                && !ops.Any(instruction => instruction.IsCall && instruction.Member is { } member && member.Name != "TriggerPassive" && ChangesAGameObject(member));
        }

        /// <summary>
        /// An orb's values (OrbModel's PassiveVal and EvokeVal), each its getter's code; one that's just one of its own fields (Dark's EvokeVal is
        /// _evokeVal) is that field (an alias); and its own fields as its constructor starts them. A getter the reader can't follow is a gap.
        /// </summary>
        public (IReadOnlyDictionary<string, HookValue>? Values, IReadOnlyDictionary<string, string>? Aliases, IReadOnlyDictionary<string, decimal>? Starts, GapOf? Gap) OrbValuesOf()
        {
            var values = new Dictionary<string, HookValue>(StringComparer.Ordinal);
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                foreach (var property in OrbValueProperties().Order(StringComparer.Ordinal))
                {
                    _hook = "get_" + property;
                    var getter = FindGetter(property) ?? throw new Gap(ReaderCapability.ValueNotFollowed, $"gap: no {property} getter");
                    var value = Value(ProgramReader.ReadReturnedValue(_type, getter, _read) ?? throw new Gap(ReaderCapability.ValueNotFollowed, $"gap: the value {property}"));
                    if (value is HookField { Field: var field } && _type.GetField(field, Declared) != null) aliases[field] = property;
                    else values[property] = value;
                }
                var starts = new Dictionary<string, decimal>(StringComparer.Ordinal);
                foreach (var store in ProgramReader.ReadConstructor(_type, _read).All().OfType<StoreStep>())
                    if (_type.GetField(store.Field, Declared) != null && Value(store.Value) is HookConstant { Value: var start }) starts[store.Field] = start;
                    else throw new Gap(ReaderCapability.ValueNotFollowed, $"gap: its constructor stores {store.Field} = {store.Value}");
                // Every own field its code reads starts where its constructor puts it (or at 0, a field's default).
                return (values.Count > 0 ? values : null, aliases.Count > 0 ? aliases : null, starts.Count > 0 ? starts : null, null);
            }
            catch (Gap gap)
            {
                return (null, null, null, new GapOf(gap.Capability, gap.Message));
            }
        }

        private MethodInfo? FindGetter(string property)
        {
            for (var type = _type; type != null; type = type.BaseType)
                if (type.GetProperty(property, Declared)?.GetMethod is { IsAbstract: false } getter) return getter;
            return null;
        }

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

        /// <summary>
        /// Its own questions (Questions: DoomPower.IsOwnerDoomed), each read from its code as a value for its owner; one the reader stops in is left out
        /// (a caller asking it is then unknown where it asks).
        /// </summary>
        public IReadOnlyDictionary<string, HookValue>? OwnQuestions()
        {
            var read = new SortedDictionary<string, HookValue>(StringComparer.Ordinal);
            foreach (var method in Questions(_type))
            {
                _hook = method.Name;
                try { read[method.Name] = Pruned(TestWays(ProgramReader.ReadReturnedWays(_type, method, _read))); }
                catch (Exception exception) when (exception is Gap or InvalidOperationException or NotSupportedException) { }
            }
            return read.Count > 0 ? read : null;
        }

        /// <summary>One of its own commands another class calls, read whole into steps; null where the reader stops (it's then not read).</summary>
        public IReadOnlyList<HookStep>? OwnCommand(MethodInfo method)
        {
            _hook = method.Name;
            try
            {
                var program = ProgramReader.Read(_type, _read, method.Name);
                _stores = program.All().OfType<StoreStep>().ToList();
                _body = program;
                if (program.Unknowns.Any() || OwnData(method, program) != null) return null;
                var steps = program.Steps.SelectMany(Step).ToList();
                return steps.Count == 0 ? null : steps;
            }
            catch (Gap)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>
        /// The stores a Modify hook's code makes as it works out its value (Stretch's gaveBonusBlock = true on the way that gives its bonus), each on its
        /// way's condition: StoreFieldSteps run once the value is worked out, so a store to a field the value reads is a gap; any other step is one too.
        /// </summary>
        private List<HookStep> StoresWorkingOut(MethodInfo hook, HookValue returns)
        {
            ModelProgram program;
            try { program = ProgramReader.Read(_type, _read, hook.Name); }
            catch (InvalidOperationException) { return new List<HookStep>(); }
            var stores = program.All().Where(step => step is StoreStep or DataStep).ToList();
            if (stores.Count == 0) return new List<HookStep>();
            _body = program;
            var steps = stores.SelectMany(Step).ToList();
            if (steps.FirstOrDefault(step => step is not StoreFieldStep) is { } other) throw new Gap(ReaderCapability.OwnData, $"gap: {hook.Name} changes its own state ({other.GetType().Name}) as it works out its value");
            var read = new ModelSpec("", ModelKind.Power, new[] { new HookProgram(HookName.ModifyBlockAdditive, Array.Empty<HookStep>()) { Returns = returns } }).Values().OfType<HookField>().Select(field => field.Field).ToHashSet(StringComparer.Ordinal);
            if (steps.OfType<StoreFieldStep>().FirstOrDefault(store => read.Contains(store.Field)) is { } readBack) throw new Gap(ReaderCapability.OwnData, $"gap: {hook.Name} stores {readBack.Field}, which its value reads");
            return steps;
        }

        /// <summary>
        /// A hook that acts only outside fights (Diagnostics/OutOfCombatHooks.txt), for this model's kind: a power's AfterRemoved isn't one, since
        /// PowerCmd.Remove runs it in the fight (RemoveInternal, then AfterRemoved(power.Owner)), as CreatureCmd.Kill does for the powers a death takes
        /// off; a relic's (RelicCmd.Remove: events, the console) is.
        /// </summary>
        private bool OutsideFights(string hook) =>
            Diagnostics.CoverageLedger.OutOfCombatHooks.Contains(hook) && !(hook == nameof(HookName.AfterRemoved) && _kind == ModelKind.Power);

        /// <summary>One hook body of a kind the engine runs no read program for, read to its first gap (ReadEffects).</summary>
        public HookReading Effect(MethodInfo hook)
        {
            _hook = hook.Name;
            if (NotRun.TryGetValue(hook.Name, out var why)) return new HookReading(hook.Name, null) { NotRun = why };
            if (OutsideFights(hook.Name)) return new HookReading(hook.Name, null) { NotRun = "it acts outside fights (Diagnostics/OutOfCombatHooks.txt)" };
            static HookReading Gapped(string hook, GapOf gap) => new(hook, gap.Text) { Capability = gap.Capability };
            try
            {
                if (hook.ReturnType.FullName != typeof(void).FullName && !hook.ReturnType.Name.StartsWith(nameof(Task), StringComparison.Ordinal)) _ = Pruned(Ways(ProgramReader.ReadReturnedWays(_type, hook, _read)));
                else
                {
                    var program = ProgramReader.Read(_type, _read, hook.Name);
                    _stores = program.All().OfType<StoreStep>().ToList();
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

        /// <summary>
        /// Why the last hook read (Hook) is unknown where it runs: a body that acts only as the players' next turn starts (decision #306) and doesn't
        /// read, its program an UnknownStep there; null for any other.
        /// </summary>
        public string? UnknownWhereItRuns { get; private set; }

        public (HookProgram? Program, GapOf? Gap, string? NotRun) Hook(MethodInfo hook)
        {
            _hook = hook.Name;
            UnknownWhereItRuns = null;
            if (NotRun.TryGetValue(hook.Name, out var why)) return (null, null, why);
            if (OutsideFights(hook.Name)) return (null, null, "it acts outside fights (Diagnostics/OutOfCombatHooks.txt)");
            try
            {
                // A player's next turn's start (CombatManager.SetupPlayerTurn's hooks, decision #306): run in a projection, the hook's player that player.
                if (PlayersTurnStartHooks.Contains(hook.Name)) return OnlyAsThePlayersTurnStarts(hook, Enum.Parse<HookName>(hook.Name), when: null);
                // PowerCmd.Apply calls it only for a power whose ShouldScaleInMultiplayer is true (read from its code: PowerModel's is false); then
                // it's PowerCmd.Apply's own rule, read for every power class into its PowerRule (decision #234) and run by the engine as it lands one.
                if (hook.Name == "GetScaledAmountForMultiplayer")
                    return Returned(_type, "get_ShouldScaleInMultiplayer") is not ConstantValue { Value: not 0 }
                        ? (null, null, "PowerCmd.Apply never calls it: the power's ShouldScaleInMultiplayer is false")
                        : (null, null, "PowerCmd.Apply's scaling for the players: read into the class's PowerRule and run by the engine as it lands one (decision #234)");
                // A card entering the fight afflicted (Galvanic's Galvanized, Vital Spark's Tainted on a Power or Skill made or added): every command an
                // affliction on the entering card. An affliction is a fact of its card the adapter reads; no simulated play plays a card that
                // entered the fight during the simulation. Any other change to the entering card (a keyword: Ghost Seed) stays a gap.
                if (hook.Name == "AfterCardEnteredCombat" && AfflictsTheEnteringCardOnly(hook))
                    return (null, null, "it afflicts a card as it enters the fight: the cards the adapter reads show their afflictions, and no simulated play plays a card that entered the fight during the simulation");
                if (ChangesCostsAtFlushOnly(hook)) return (null, null, CostsAtFlush);
                if (SetsShownOnly(hook)) return (null, null, "it only sets what its cards show (an affliction's amount): no code a fight runs reads it");
                if (StartsATurn(hook.Name) && PastTheHorizon(hook) is { } horizon)
                {
                    if (horizon.Length > 0) return (null, new GapOf(ReaderCapability.ValueNotFollowed, horizon), null);
                    // A relic's body whose every command waits on its owner, a player, taking part: it acts only as the players' next turn starts,
                    // which a projection runs (decision #306).
                    return OnlyAsThePlayersTurnStarts(hook, Enum.Parse<HookName>(hook.Name), when: new HookFactValue(HookFact.TakesPart));
                }
                if (!Enum.TryParse<HookName>(hook.Name, out var name)) return (null, new GapOf(ReaderCapabilities.OfHook(hook.Name), $"gap: the simulator runs no read model's {hook.Name}"), null);
                // A creature-valued Modify hook (a damage's redirect: Wuppy's Loyalty, Die For You): the creature of the first way that holds.
                if (name is HookName.ModifyPreBlockDamageTarget or HookName.ModifyUnblockedDamageTarget)
                    return (new HookProgram(name, Array.Empty<HookStep>()) { Returns = Pruned(CreatureWays(ProgramReader.ReadReturnedWays(_type, hook, _read))) }, null, null);
                // Where a played card goes (a location-valued return: the location given, or it with its pile and position set).
                if (name is HookName.ModifyCardPlayResultLocation)
                    return (new HookProgram(name, Array.Empty<HookStep>()) { Returns = Pruned(LocationWays(ProgramReader.ReadReturnedWays(_type, hook, _read))) }, null, null);
                // A Should hook the engine asks (a true-or-false: 1 or 0).
                if (name is HookName.ShouldAllowHitting or HookName.ShouldCreatureBeRemovedFromCombatAfterDeath or HookName.ShouldPowerBeRemovedAfterOwnerDeath or HookName.ShouldStopCombatFromEnding
                        or HookName.ShouldClearBlock or HookName.ShouldTakeExtraTurnEarly or HookName.ShouldTakeExtraTurn
                    && hook.ReturnType.Name == "Boolean")
                    return (new HookProgram(name, Array.Empty<HookStep>()) { Returns = Pruned(TestWays(ProgramReader.ReadReturnedWays(_type, hook, _read))) }, null, null);
                // A Try hook (TryModifyPowerAmountReceived): its answer each way (true: it changed the amount), and the amount its out parameter holds there.
                if (name is HookName.TryModifyPowerAmountReceived && hook.ReturnType.Name == "Boolean"
                    && hook.GetParameters().SingleOrDefault(parameter => parameter.IsOut) is { Name: { } outName })
                {
                    var answers = Pruned(TestWays(ProgramReader.ReadReturnedWays(_type, hook, _read)));
                    var amounts = ProgramReader.ReadOutWays(_type, hook, outName, _read) ?? throw new Gap(ReaderCapability.ValueNotFollowed, $"gap: no out parameter {outName}");
                    return (new HookProgram(name, Array.Empty<HookStep>()) { Returns = Pruned(Ways(amounts)), Modifies = answers }, null, null);
                }
                // A card's cost as a Try hook (decisions #332, #334): read the same way, its answer and its out cost each way, the card the one costed
                // (HookFact.CardPile its pile) and originalCost the cost so far (HookFact.CostSoFar); the engine runs the energy passes wherever a
                // simulation reads a hand card's cost (SimEngine.HandCost). No simulation reads a star cost, so a star cost hook that reads stays a gap.
                if (name is HookName.TryModifyEnergyCostInCombat or HookName.TryModifyEnergyCostInCombatLate or HookName.TryModifyStarCost && hook.ReturnType.Name == "Boolean"
                    && hook.GetParameters().SingleOrDefault(parameter => parameter.IsOut) is { Name: { } costName })
                {
                    var answers = Pruned(TestWays(ProgramReader.ReadReturnedWays(_type, hook, _read)));
                    var costs = Pruned(Ways(ProgramReader.ReadOutWays(_type, hook, costName, _read) ?? throw new Gap(ReaderCapability.ValueNotFollowed, $"gap: no out parameter {costName}")));
                    if (name == HookName.TryModifyStarCost)
                        return (null, new GapOf(ReaderCapability.CardCosts, $"gap: its body reads, but no simulation reads a hand card's star cost ({hook.Name})"), null);
                    return (new HookProgram(name, Array.Empty<HookStep>()) { Returns = costs, Modifies = answers }, null, null);
                }
                if (hook.ReturnType.Name is "Decimal" || hook.ReturnType.Name is "Int32" && name is HookName.ModifyCardPlayCount or HookName.ModifyOrbPassiveTriggerCounts)
                {
                    var returns = Pruned(Ways(ProgramReader.ReadReturnedWays(_type, hook, _read)));
                    return (new HookProgram(name, StoresWorkingOut(hook, returns)) { Returns = returns }, null, null);
                }
                var body = BodySteps(hook);
                if (body.Gap is { } bodyGap) return (null, bodyGap, null);
                var steps = body.Steps!;
                // A step whose condition never holds in a fight never runs: left out.
                var never = steps.RemoveAll(step => step.When is HookConstant { Value: 0m });
                if (steps.Count == 0) return (null, null, never == 0 ? null : "its commands' conditions never hold in a fight");
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
        /// A hook body that acts only as the players' next turn starts (a player's SetupPlayerTurn hooks; a relic's turn's-start body whose every
        /// command waits on its owner taking part), which only a projection runs (decision #306): its program, or, where it doesn't read, an
        /// UnknownStep there (on <paramref name="when"/>), the class's other hooks read as they were (UnknownWhereItRuns).
        /// </summary>
        private (HookProgram? Program, GapOf? Gap, string? NotRun) OnlyAsThePlayersTurnStarts(MethodInfo hook, HookName name, HookValue? when)
        {
            (List<HookStep>? Steps, GapOf? Gap) body;
            try { body = BodySteps(hook); }
            catch (Gap gap) { body = (null, new GapOf(gap.Capability, gap.Message)); }
            catch (Exception exception) { body = (null, new GapOf(ReaderCapability.NotRead, $"gap: not read ({exception.GetType().Name}: {exception.Message})")); }
            if (body.Gap is { } bodyGap)
            {
                UnknownWhereItRuns = bodyGap.Text;
                // Only a projection runs it; where what its code does there changes nothing a projection follows (decision #308: a player's energy,
                // cards, HP, read as TurnStartGap.ForecastIgnores reads it), the projection goes on past it, as it does past a class with no model.
                var ignored = ReadModels.ForecastIgnores(_type, new[] { hook.Name }, _read);
                return (new HookProgram(name, new HookStep[] { new UnknownStep($"{_type.Name}'s {hook.Name} isn't read ({bodyGap.Text})") { When = when, ForecastIgnores = ignored } }), null, null);
            }
            var steps = body.Steps!;
            var never = steps.RemoveAll(step => step.When is HookConstant { Value: 0m });
            if (steps.Count == 0) return (null, null, never == 0 ? "nothing in its body acts" : "its commands' conditions never hold in a fight");
            return (new HookProgram(name, steps), null, null);
        }

        /// <summary>A hook body's commands, each translated at its place (a let where the code keeps a local), or the gap where the reader stops.</summary>
        private (List<HookStep>? Steps, GapOf? Gap) BodySteps(MethodInfo hook)
        {
            var program = ProgramReader.Read(_type, _read, hook.Name);
            _stores = program.All().OfType<StoreStep>().ToList();
            _body = program;
            if (program.Unknowns.FirstOrDefault() is { } unknown) return (null, new GapOf(ReaderCapability.ValueNotFollowed, "gap: the reader can't follow it: " + unknown));
            if (OwnData(hook, program) is { } data) return (null, data);
            var steps = new List<HookStep>();
            var placed = new List<(int Offset, int Index)>();
            _lets = new List<(string Name, int Offset, HookValue Value)>();
            _shownValues = true;
            try
            {
                foreach (var step in program.Steps)
                {
                    placed.Add((step.Offset, steps.Count));
                    steps.AddRange(Step(step));
                }
                // Each local kept (a let) worked out where the code keeps it: before the first step after its place.
                foreach (var (local, offset, value) in _lets.OrderByDescending(let => let.Offset))
                {
                    var at = placed.Where(place => place.Offset > offset).Select(place => place.Index).DefaultIfEmpty(steps.Count).Min();
                    steps.Insert(at, new LetStep(local, value));
                }
            }
            finally
            {
                _lets = null;
                _shownValues = false;
            }
            return (steps, null);
        }

        /// <summary>
        /// The commands of a hook that runs as the players' next turn starts (AfterEnergyReset, AfterPlayerTurnStart...): never run (past a simulation's
        /// horizon), read for what it pays out and on what (ModelSpec.Payouts, decision #239). Null where the reader stops.
        /// </summary>
        public IReadOnlyList<HookStep>? NextTurnSteps(MethodInfo hook)
        {
            _hook = hook.Name;
            try { return BodySteps(hook).Steps; }
            catch (Exception) { return null; }
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
            var stepsAt = program.All().Where(step => step is DataStep or StoreStep or CardActionStep).Select(step => step.Offset).ToHashSet();
            var usesData = ops.Any(instruction => instruction.IsCall && instruction.Member is { Name: "GetInternalData" });
            var nested = Procs.CardCode.NestedTypes(_type).Where(type => !type.Name.StartsWith('<')).Select(type => type.Name).ToHashSet(StringComparer.Ordinal);
            for (var index = 0; index < ops.Count; index++)
            {
                var instruction = ops[index];
                if (instruction.OpCode == System.Reflection.Emit.OpCodes.Stfld && instruction.Member is { } stored && nested.Contains(stored.DeclaringType) && !stepsAt.Contains(instruction.Offset))
                    return new GapOf(ReaderCapability.OwnData, $"gap: changes its own data ({stored.Name}) where the reader reads no store");
                // A field set to its default through its address (ldflda, then initobj: a decimal's = default).
                if (instruction.OpCode == System.Reflection.Emit.OpCodes.Ldflda && index + 1 < ops.Count && ops[index + 1].OpCode == System.Reflection.Emit.OpCodes.Initobj
                    && instruction.Member is { } cleared && !cleared.DeclaringType.StartsWith('<') && !cleared.Name.StartsWith('<') && !stepsAt.Contains(instruction.Offset))
                    return new GapOf(nested.Contains(cleared.DeclaringType) ? ReaderCapability.OwnData : SetOn(cleared.DeclaringType, call: false), $"gap: clears {cleared.DeclaringType}.{cleared.Name} where the reader reads no store");
                if (instruction.IsCall && instruction.Member is { Kind: CodeMemberKind.Method, HasThis: true } call && usesData && call.Namespace.StartsWith("System.Collections", StringComparison.Ordinal)
                    && !CollectionQueries.Contains(call.Name) && !stepsAt.Contains(instruction.Offset)
                    // Going over a list (a foreach's enumerator: Calamity's made cards) changes nothing.
                    && call.DeclaringType != "Enumerator"
                    && !(call.Name == "Remove" && call.ParameterCount == 2 && index + 1 < ops.Count && ops[index + 1].OpCode != System.Reflection.Emit.OpCodes.Pop))
                    return new GapOf(ReaderCapability.OwnData, $"gap: changes a collection ({call.FullName}) where the reader reads no step");
                if (instruction.IsCall && !stepsAt.Contains(instruction.Offset) && ChangesAGameObject(instruction.Member)) return new GapOf(SetOn(instruction.Member!.DeclaringType, call: true), $"gap: changes a game object outside a command ({instruction.Member!.FullName})");
                // A property set on one of the game's objects the reader reads no store for (Panache's DynamicVars["CardsLeft"].BaseValue--).
                // A text set (a StringVar's StringValue: words its tooltip shows) is what's shown, never a number anything reads.
                if (instruction.IsCall && instruction.Member is { Kind: CodeMemberKind.Method, HasThis: true } setter && setter.Name.StartsWith("set_", StringComparison.Ordinal)
                    && setter.Namespace.StartsWith("MegaCrit", StringComparison.Ordinal) && !setter.Namespace.Contains(".Nodes", StringComparison.Ordinal) && !stepsAt.Contains(instruction.Offset)
                    && setter.ParameterTypes is not ["String"] && !ShownOnly.Contains(setter.DeclaringType + "." + setter.Name))
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
        /// A BeforeFlush whose code only changes card costs (CardEnergyCost's methods: Slumbering Essence's AddUntilPlayed(-1) on its card in hand):
        /// CombatManager.EndPlayerTurnPhaseOneInternal runs Hook.BeforeFlush last, after the auto-plays (the only simulated pick that reads a cost,
        /// Simpleton's) and the turn's end steps; the cards then leave the hand. No command, store or other change; read whole to tell.
        /// </summary>
        internal bool ChangesCostsAtFlushOnly(MethodInfo hook)
        {
            if (hook.Name != "BeforeFlush") return false;
            ModelProgram program;
            List<CodeOp> ops;
            try { program = ProgramReader.Read(_type, _read, hook.Name); ops = _read(ProgramReader.BodyOf(hook)); }
            catch (InvalidOperationException) { return false; }
            if (program.Unknowns.Any() || program.All().Any(step => step is not (LoopStep or FlashCall))) return false;
            var costs = 0;
            foreach (var instruction in ops)
            {
                if (instruction.OpCode == System.Reflection.Emit.OpCodes.Stfld && instruction.Member is { } stored && !stored.DeclaringType.StartsWith('<') && !stored.Name.StartsWith('<')) return false;
                if (!instruction.IsCall || instruction.Member is not { Kind: CodeMemberKind.Method } call) continue;
                if (call.HasThis && call.Name.StartsWith("set_", StringComparison.Ordinal) && call.Namespace.StartsWith("MegaCrit", StringComparison.Ordinal)) return false;
                if (!ChangesAGameObject(call)) continue;
                if (call.DeclaringType != "CardEnergyCost") return false;
                costs++;
            }
            return costs > 0;
        }

        /// <summary>
        /// A ModifyShuffleOrder whose code only moves its own card to the front of the shuffled cards: Remove(base.Card), then Insert(0, base.Card),
        /// on the list it's given, when the shuffle isn't the fight's first (!isInitialShuffle: a simulation's never is) and the card is among them
        /// (cards.Contains(base.Card): the engine moves it only when it's shuffled). Read whole to tell.
        /// </summary>
        internal bool PutsItsCardOnTop(MethodInfo hook)
        {
            ModelProgram program;
            try { program = ProgramReader.Read(_type, _read, hook.Name); }
            catch (InvalidOperationException) { return false; }
            if (program.Unknowns.Any() || OwnData(hook, program) != null) return false;
            var steps = program.All().ToList();
            if (steps is not [ParameterListStep { Operation: "Remove", Values: [var removed] } remove, ParameterListStep { Operation: "Insert", Values: [ConstantValue { Value: 0 }, var inserted] } insert]
                || remove.Parameter != insert.Parameter || !IsModelCard(removed) || !IsModelCard(inserted)) return false;
            // Each way to them: the shuffle not the first, the card among the shuffled.
            bool Expected(ConditionLiteral literal) => literal.Test switch
            {
                ParameterValue { Name: "isInitialShuffle" } or NamedValue { Name: "isInitialShuffle" } => !literal.Holds,
                QueryValue { Name: "Contains", Of: ParameterValue { Name: var list }, Arguments: [var card] } => literal.Holds && list == remove.Parameter && IsModelCard(card),
                _ => false,
            };
            return new[] { remove, insert }.All(step => step.Condition is not { } condition || condition.Terms.All(term => term.All(Expected)));
        }

        /// <summary>
        /// Every step of the hook's code runs only for its own card (AfterCardEnteredCombat's card == base.Card: Hexed's
        /// if (card != base.Card) return), and it changes nothing but by commands (no store, no change to its data). Read whole to tell.
        /// </summary>
        internal bool ActsOnItsOwnCardOnly(MethodInfo hook)
        {
            ModelProgram program;
            try { program = ProgramReader.Read(_type, _read, hook.Name); }
            catch (InvalidOperationException) { return false; }
            if (program.Unknowns.Any() || OwnData(hook, program) != null) return false;
            static bool OwnCard(ConditionLiteral literal) => literal.Test is CompareValue { Op: Comparison.Equal or Comparison.NotEqual } compare
                && (compare.Op == Comparison.Equal) == literal.Holds
                && (compare.Left is ParameterValue { Name: "card" } && IsModelCard(compare.Right) || compare.Right is ParameterValue { Name: "card" } && IsModelCard(compare.Left));
            var steps = program.All().ToList();
            return steps.Count > 0 && steps.All(step => step is not (StoreStep or DataStep) && step.Condition is { } condition && condition.Terms.All(term => term.Any(OwnCard)));
        }

        /// <summary>The keywords its code put on each copy of the card played (by the copy's value), for the card made of it (MakeCopyStep).</summary>
        private readonly Dictionary<string, List<string>> _copyKeywords = new(StringComparer.Ordinal);

        /// <summary>A copy of the card played (cardPlay.Card.CreateClone(), kept in a local or not).</summary>
        private bool IsCopyOfPlayCard(ProgramValue value) => Unstored(value) is QueryValue { Name: "CreateClone", Of: { } cloned } && IsPlayCard(cloned);

        /// <summary>
        /// A card its code keeps in a field of its own (MusicBox's CardBeingPlayed, Pen Nib's AttackToDouble, Vambrace's TriggeringCard) or of its data
        /// (Juggling's GetInternalData&lt;Data&gt;().cardToClone): the field's name, whose state is the identity of the card play the card was kept from
        /// (decision #272: SimCardPlay.Serial, 0 for none), as a card play kept is (decision #269); else null.
        /// </summary>
        private string? KeptCard(ProgramValue value) => Unstored(value) switch
        {
            QueryValue { Of: NamedValue { Name: "this" }, Name: var member } when KeepsACard(member) => member,
            NamedValue { Name: var named } when KeepsACard(named) => named,
            QueryValue { Name: var dataField, Of: { } dataOf, Arguments.Count: 0 } when ProgramReader.IsOwnData(dataOf) && KeepsACard(ProgramReader.OwnDataPrefix + dataField) => ProgramReader.OwnDataPrefix + dataField,
            _ => null,
        };

        /// <summary>
        /// The card a card kept is held against or kept from, in a hook given its card play: the card played (a play hook's cardPlay.Card) or the card a
        /// damage or block comes from (a Modify hook's or AfterModifyingBlockAmount's cardSource, its cardPlay's card): that card play's identity
        /// (HookFact.PlaySerial). A damage's or block's card outside a card play has none (0).
        /// </summary>
        private bool IsCardOfThePlay(ProgramValue value) => PlayOfTheCard(value) != null;

        /// <summary>
        /// The fact that is the identity of the card play the card comes from (IsCardOfThePlay): the hook's card play (HookFact.PlaySerial) where it's given
        /// one, or, in a damage hook given the hit's card and no card play (AfterDamageReceived's cardSource: Curl Up's), the card play under way when
        /// that card is the one played (HookFact.HitCardPlaySerial, decision #274); null for any other card.
        /// </summary>
        private HookFact? PlayOfTheCard(ProgramValue value) =>
            IsPlayCard(Unstored(value)) && GivenAPlay.Contains(_hook!) || IsSourceCard(Unstored(value)) && GivenACardSource.Contains(_hook!) ? HookFact.PlaySerial
            : IsSourceCard(Unstored(value)) && GivenAHitsCard.Contains(_hook!) ? HookFact.HitCardPlaySerial : null;

        /// <summary>The hooks given a damage's or block's card and its card play (cardSource, cardPlay).</summary>
        private static readonly HashSet<string> GivenACardSource = new(StringComparer.Ordinal)
        {
            "ModifyBlockAdditive", "ModifyBlockMultiplicative", "AfterModifyingBlockAmount", "ModifyDamageAdditive", "ModifyDamageMultiplicative", "ModifyDamageMultiplicativeLate",
        };

        /// <summary>The hooks given a hit's card and no card play (CreatureCmd.Damage's AfterDamageGiven and AfterDamageReceived: cardSource).</summary>
        private static readonly HashSet<string> GivenAHitsCard = new(StringComparer.Ordinal) { "AfterDamageGiven", "AfterDamageReceived" };

        /// <summary>
        /// A card play its code keeps (Stretch's data's cardPlay, Pael's Legion's AffectedCardPlay): the field's name ("Data.cardPlay", "AffectedCardPlay"),
        /// whose state is the play's identity (HookFact.PlaySerial, 0 for none) and, beside it, its player ("….Player"); else null.
        /// </summary>
        private string? KeptPlay(ProgramValue value) => Unstored(value) switch
        {
            QueryValue { Of: NamedValue { Name: "this" }, Name: var member, Arguments.Count: 0 } when KeepsAPlay(member) => member,
            QueryValue { Name: var dataField, Of: { } dataOf, Arguments.Count: 0 } when ProgramReader.IsOwnData(dataOf) && KeepsAPlay(ProgramReader.OwnDataPrefix + dataField) => ProgramReader.OwnDataPrefix + dataField,
            _ => null,
        };

        /// <summary>Which card a card fact names: "" the card played, "Source" the card a damage or block comes from (cardSource); null for any other.</summary>
        private string? CardNamed(ProgramValue value) => IsPlayCard(Unstored(value)) ? "" : IsSourceCard(Unstored(value)) ? "Source" : null;

        /// <summary>A card fact's Of for the card <see cref="CardNamed"/> names: null for the card played.</summary>
        private static string? CardOf(string named) => named.Length == 0 ? null : named;

        /// <summary>
        /// The true-or-false a static method of its own class returns (Stretch's GainsBlock(card): card.GainsBlock || card.Enchantment?.GainsBlock ?? false),
        /// read from its body, its parameters the call's arguments; null for a method that isn't one (or calls itself).
        /// </summary>
        private ProgramValue? OwnStaticTest(string method, IReadOnlyList<ProgramValue> arguments, bool isStatic = true)
        {
            // Or one of its own (this.ShouldDoomTrigger(participants): Doom's), read the same way, its this its own.
            var found = _type.GetMethods(Declared | BindingFlags.Static).FirstOrDefault(candidate => candidate.IsStatic == isStatic && candidate.Name == method && candidate.ReturnType.Name == "Boolean"
                && candidate.GetParameters().Length == arguments.Count && !candidate.IsVirtual);
            if (found == null || !_inlining.Add("static:" + method)) return null;
            try
            {
                var parameters = found.GetParameters().Select((parameter, index) => (parameter.Name!, arguments[index])).ToDictionary(pair => pair.Item1, pair => pair.Item2, StringComparer.Ordinal);
                return ProgramReader.ReadReturnedWays(_type, found, _read).Map(part => part switch
                {
                    ParameterValue { Name: var name } when parameters.TryGetValue(name, out var argument) => argument,
                    NamedValue { Name: var name } when parameters.TryGetValue(name, out var argument) => argument,
                    _ => null,
                });
            }
            finally { _inlining.Remove("static:" + method); }
        }

        /// <summary>The player of a card play its code keeps (internalData.cardPlay.Player): that field's player ("Data.cardPlay.Player"), else null.</summary>
        private string? KeptPlayPlayer(ProgramValue value) =>
            Unstored(value) is QueryValue { Name: "Player", Of: { } playOf } && KeptPlay(playOf) is { } field ? field + ".Player" : null;

        /// <summary>A field or property of its own class, or a field of its own data ("Data.cardPlay"), that holds a card play.</summary>
        private bool KeepsAPlay(string member) => member.StartsWith(ProgramReader.OwnDataPrefix, StringComparison.Ordinal)
            ? Procs.CardCode.NestedTypes(_type).Any(nested => nested.GetField(member[ProgramReader.OwnDataPrefix.Length..], Declared)?.FieldType.Name == "CardPlay")
            : (_type.GetProperty(member, Declared)?.PropertyType ?? _type.GetField(member, Declared)?.FieldType) is { Name: "CardPlay" };

        /// <summary>The card play the hook is given (its cardPlay: a play hook's, a Modify or AfterModifying hook's block's or damage's).</summary>
        private static bool IsHookPlay(ProgramValue value) => Unstored(value) is ParameterValue { Name: "cardPlay" } or NamedValue { Name: "cardPlay" };

        /// <summary>The hooks given a card play of their own beside the play hooks (a block's or damage's: cardPlay).</summary>
        private static readonly HashSet<string> GivenAPlay = new(StringComparer.Ordinal) { "ModifyBlockAdditive", "ModifyBlockMultiplicative", "AfterModifyingBlockAmount", "BeforeCardPlayed", "AfterCardPlayed" };

        /// <summary>
        /// A field or property of its own class (not a base class's: an enchantment's base.Card is its card), or a field of its own data ("Data.cardToClone"),
        /// that holds a card.
        /// </summary>
        private bool KeepsACard(string member) => member.StartsWith(ProgramReader.OwnDataPrefix, StringComparison.Ordinal)
            ? Procs.CardCode.NestedTypes(_type).Any(nested => nested.GetField(member[ProgramReader.OwnDataPrefix.Length..], Declared)?.FieldType.Name == "CardModel")
            : (_type.GetProperty(member, Declared)?.PropertyType ?? _type.GetField(member, Declared)?.FieldType) is { Name: "CardModel" };

        /// <summary>The hooks given an attack (Hook.BeforeAttack, AfterAttack: its AttackCommand): its attacker, props and card are the hook's.</summary>
        private static readonly HashSet<string> AttackHooks = new(StringComparer.Ordinal) { "BeforeAttack", "AfterAttack" };

        /// <summary>The attack a BeforeAttack or AfterAttack is given (its command).</summary>
        private bool IsAttack(ProgramValue value) => AttackHooks.Contains(_hook) && Unstored(value) is ParameterValue { Name: "command" } or NamedValue { Name: "command" };

        /// <summary>
        /// An attack its own data keeps (a field of its data holding an AttackCommand: Vigor's commandToModify): its field's name ("Data.commandToModify"),
        /// else null. The simulator keeps the attack's id there, and beside it what the code asks of it later: its card ("…ModelSource", the card's
        /// HandId) and its attacker ("…Attacker", the creature's id).
        /// </summary>
        private static string? KeptAttack(ProgramValue value) =>
            Unstored(value) is QueryValue { Name: var field, Of: { } dataOf, Arguments.Count: 0, ValueType: "AttackCommand" } && ProgramReader.IsOwnData(dataOf) ? ProgramReader.OwnDataPrefix + field : null;

        /// <summary>
        /// The class of the pet the simulator keeps for a player (Who.YourOsty): what Player.Osty gets (PlayerCombatState.GetPet&lt;T&gt;()), read from
        /// its code; null when it can't be read.
        /// </summary>
        private string? OstyClass()
        {
            var getter = _type.Assembly.GetType("MegaCrit.Sts2.Core.Entities.Players.Player")?.GetProperty("Osty", BindingFlags.Instance | BindingFlags.Public)?.GetMethod;
            if (getter == null) return null;
            try { return _read(getter).FirstOrDefault(instruction => instruction.IsCall && instruction.Member is { Name: "GetPet", GenericArguments.Count: 1 })?.Member!.GenericArguments[0]; }
            catch (InvalidOperationException) { return null; }
        }

        /// <summary>
        /// What a property of its own keeps when its code sets it to <paramref name="given"/> (decision #272): its setter's store to its backing field, read
        /// from the setter's code, `value` the value given (Pen Nib's AttacksPlayed: `_attacksPlayed = value % 10`); the value given for a plain setter
        /// or a member that isn't such a property. A setter that keeps it on a condition, or stores more than one of its own fields from it, is a gap.
        /// </summary>
        private ProgramValue SetterStores(string property, ProgramValue given)
        {
            if (_type.GetProperty(property, Declared) is not { SetMethod: { } setter }) return given;
            ModelProgram program;
            try { program = ProgramReader.Read(_type, _read, setter.Name); }
            catch (InvalidOperationException) { return given; }
            static bool IsGiven(ProgramValue part) => part is ParameterValue { Name: "value" } or NamedValue { Name: "value" };
            var kept = program.All().OfType<StoreStep>().Where(store => store.Value.Parts().Any(IsGiven)).ToList();
            if (kept.Count == 0) return given;
            // Kept only when it changes (if (_skillsPlayed != value) _skillsPlayed = value: Tuning Fork's, Centennial Puzzle's): the value given either way.
            static bool OnlyWhenItChanges(ProgramCondition? condition) => condition?.Terms.All(term => term.All(literal =>
                literal.Test is CompareValue { Op: Comparison.Equal or Comparison.NotEqual } compare && (IsGiven(compare.Left) || IsGiven(compare.Right)))) == true;
            if (kept.Count == 1 && IsGiven(kept[0].Value) && OnlyWhenItChanges(kept[0].Condition)) return given;
            if (kept.Count > 1 || kept[0].Condition != null) throw new Gap(ReaderCapability.OwnData, $"gap: {property}'s setter keeps what it's given on a condition or in several fields");
            return IsGiven(kept[0].Value) ? given : kept[0].Value.Map(part => IsGiven(part) ? given : null);
        }

        /// <summary>A monster class of this branch, by name (MegaCrit.Sts2.Core.Models.Monsters: BowlbugRock), not abstract.</summary>
        private bool IsMonsterClass(string name) => _type.Assembly.GetType("MegaCrit.Sts2.Core.Models.Monsters." + name) is { IsAbstract: false };

        /// <summary>
        /// A monster's state its owner's power sets ("Monster.LagavulinMatriarch.IsAwake") whose setter is one of the values only shown (ShownOnly,
        /// decision #307: its sleeping effect, its awake animation): nothing a simulation follows.
        /// </summary>
        private static bool MonsterStateShownOnly(string field) =>
            field[ProgramReader.OwnersMonsterPrefix.Length..].Split('.') is [var monsterClass, var property] && ShownOnly.Contains(monsterClass + ".set_" + property);

        /// <summary>A field of its own data that holds an attack ("Data.commandToModify", its Data class's AttackCommand field).</summary>
        private bool HoldsAnAttack(string field) => field.StartsWith(ProgramReader.OwnDataPrefix, StringComparison.Ordinal)
            && Procs.CardCode.NestedTypes(_type).Any(nested => nested.GetField(field[ProgramReader.OwnDataPrefix.Length..], Declared)?.FieldType.Name == "AttackCommand");

        /// <summary>The attack's card (command.ModelSource, or it as a card: command.ModelSource is CardModel cardModel).</summary>
        private bool IsAttackCard(ProgramValue value) => Unstored(value) switch
        {
            QueryValue { Name: "ModelSource", Of: { } of } => IsAttack(of),
            QueryValue { Name: "is", Type: "CardModel", Of: { } of } => IsAttackCard(of),
            _ => false,
        };

        /// <summary>
        /// The attack given or the one its data keeps, compared (decision #228): the same attack (command == commandToModify: their ids), the kept one
        /// there (!= null), the damage's card the kept attack's (cardSource == commandToModify.ModelSource: the damage is that attack's, or its card is
        /// the same card), the kept attack's attacker a creature the code names (commandToModify.Attacker == dealer). Null for anything else.
        /// </summary>
        private HookValue? AttackCompare(CompareValue compare)
        {
            if (compare.Op is not (Comparison.Equal or Comparison.NotEqual)) return null;
            HookValue? holds = null;
            static HookValue Equal(HookValue left, HookValue right) => new HookMath(HookOperation.Equal, left, right);
            static HookValue There(string field) => new HookMath(HookOperation.NotEqual, new HookField(field), new HookConstant(0m));
            foreach (var (one, other) in new[] { (compare.Left, compare.Right), (compare.Right, compare.Left) })
            {
                if (KeptAttack(one) is { } kept)
                {
                    if (IsAttack(other)) holds = Equal(new HookField(kept), new HookFactValue(HookFact.AttackId));
                    else if (other is NullValue) holds = Equal(new HookField(kept), new HookConstant(0m));
                }
                else if (Unstored(one) is QueryValue { Name: "ModelSource", Of: { } sourceOf } && KeptAttack(sourceOf) is { } keptSource && IsSourceCard(other))
                {
                    var card = keptSource + ".ModelSource";
                    holds = new HookMath(HookOperation.Or,
                        new HookMath(HookOperation.And, There(keptSource), Equal(new HookField(keptSource), new HookFactValue(HookFact.CurrentAttack))),
                        new HookMath(HookOperation.And, There(card), Equal(new HookField(card), new HookFactValue(HookFact.SourceCardId))));
                }
                else if (Unstored(one) is QueryValue { Name: "Attacker", Of: { } attackerOf } && KeptAttack(attackerOf) is { } keptAttacker && CreatureOf(other) is { } creature)
                    holds = Equal(new HookField(keptAttacker + ".Attacker"), CreatureFact(HookFact.CreatureId, creature));
                if (holds != null) break;
            }
            return holds == null ? null : compare.Op == Comparison.Equal ? holds : Not(holds);
        }

        /// <summary>The pile of the card a damage comes from (cardSource.Pile, kept in a local or not).</summary>
        private static bool IsSourcePile(ProgramValue value) => Unstored(value) switch
        {
            QueryValue { Name: "Pile", Of: { } pileOf } => IsSourceCard(Unstored(pileOf)),
            // A local set on each branch to the same pile (Lethality's pile), merged: that pile.
            WaysValue ways => ways.Ways.Count > 0 && ways.Ways.All(way => IsSourcePile(way.Value)),
            _ => false,
        };

        /// <summary>
        /// The card a damage comes from, where it is (decision #231): in a pile (cardSource.Pile != null: every card a simulation's damage comes from
        /// is), in the play pile (Pile.Type == PileType.Play: being played, the damage's card play there; any other pile a gap), and the Modify hook's
        /// card play there (cardPlay != null). Null for anything else.
        /// </summary>
        private HookValue? SourceCompare(CompareValue compare)
        {
            if (compare.Op is not (Comparison.Equal or Comparison.NotEqual)) return null;
            HookValue? holds = null;
            foreach (var (one, other) in new[] { (compare.Left, compare.Right), (compare.Right, compare.Left) })
            {
                if (IsSourcePile(one) && other is NullValue) holds = Not(new HookFactValue(HookFact.FromCard));
                else if (Unstored(one) is QueryValue { Name: "Type", Of: { } typeOf } && IsSourcePile(typeOf) && other is ConstantValue pileType)
                    holds = EnumName(_type.Assembly, "PileType", pileType.Value) == "Play" ? new HookFactValue(HookFact.SourceInPlay)
                        : throw new Gap(ReaderCapability.CardFacts, $"gap: tests the card's pile is {pileType.Value}");
                else if (Unstored(one) is ParameterValue { Name: "cardPlay" } or NamedValue { Name: "cardPlay" } && DamageHooks.Contains(_hook) && other is NullValue)
                    holds = Not(new HookFactValue(HookFact.FromPlay));
                if (holds != null) break;
            }
            return holds == null ? null : compare.Op == Comparison.Equal ? holds : Not(holds);
        }

        /// <summary>
        /// A shot's card against null (AfterArrowShot's source: a card made the shot, HookFact.HookCardThere). Null for anything else (its arrow
        /// against null: no game class asks since v0.114.0 took out Quietus' AfterArrowShot, a gap).
        /// </summary>
        private HookValue? ArrowCompare(CompareValue compare)
        {
            if (compare.Op is not (Comparison.Equal or Comparison.NotEqual) || _hook != "AfterArrowShot") return null;
            HookValue? there = null;
            foreach (var (one, other) in new[] { (compare.Left, compare.Right), (compare.Right, compare.Left) })
            {
                if (other is not NullValue) continue;
                if (Unstored(one) is ParameterValue { Name: "source" } or NamedValue { Name: "source" }) there = new HookFactValue(HookFact.HookCardThere);
                if (there != null) break;
            }
            return there == null ? null : compare.Op == Comparison.NotEqual ? there : Not(there);
        }

        /// <summary>
        /// The first arrow of the Impaled AfterArrowImpaled is given (power.Arrows.FirstOrDefault()): ArrowCmd.ImpaleTarget adds the shot's arrow to it
        /// before the hook, and every arrow of an instance is its applier's, the shooter (ImpaleTarget finds or applies the instance whose Applier
        /// is the arrow's owner: ImpaledPower is InstancedPerApplier, ArrowFactsTests holds it to the code).
        /// </summary>
        private bool IsImpaledsFirstArrow(ProgramValue value) =>
            _hook == "AfterArrowImpaled" && Unstored(value) is QueryValue { Name: "FirstOrDefault", Arguments: [QueryValue { Name: "Arrows", Of: ParameterValue { Name: "power" } or NamedValue { Name: "power" } }] };

        /// <summary>While a hand pick's step is read: its condition's tests of the pick hold (Test).</summary>
        private bool _handPickHolds;

        /// <summary>The hook is the card played's own (BeforeCardPlayed, AfterCardPlayed): its card's facts are the play's.</summary>
        private static bool IsPlayHook(string? hook) => hook is "BeforeCardPlayed" or "AfterCardPlayed";

        /// <summary>
        /// The card is the game's random pick from the owner's hand (every way a NextItem over GetPile(PileType.Hand, base.Owner).Cards, filtered
        /// or not: a fallback chain), whose first pick is among the cards that cost (a filter asking CostsEnergyOrStars): a card that costs, when
        /// one does (Mummified Hand's).
        /// </summary>
        private bool PicksAHandCardThatCosts(ProgramValue card)
        {
            var leaves = new List<ProgramValue>();
            void Collect(ProgramValue value)
            {
                if (value is StoredValue stored) value = stored.Value;
                if (value is WaysValue ways) foreach (var (_, way) in ways.Ways) Collect(way);
                else leaves.Add(value);
            }
            Collect(card);
            return leaves.Count > 0 && leaves.All(leaf => leaf is QueryValue { Name: "NextItem", Arguments: [var items] } && FromOwnersHand(items))
                && leaves[0] is QueryValue { Arguments: [var first] } && Filters(first).Any(filter => filter.Body.Terms.Any(term => term.Any(literal =>
                    literal.Holds && literal.Test is QueryValue { Name: "CostsEnergyOrStars" })));
        }

        /// <summary>The cards of the owner's hand, filtered (Where) or listed (ToList) or not: GetPile(PileType.Hand, base.Owner).Cards.</summary>
        private bool FromOwnersHand(ProgramValue items)
        {
            while (true)
            {
                if (items is StoredValue stored) items = stored.Value;
                if (items is QueryValue { Name: "Where" or "ToList", Arguments: [var source, ..] }) { items = source; continue; }
                return items is QueryValue { Name: "Cards", Of: QueryValue { Name: "GetPile", Arguments: [ConstantValue pile, var owner] } }
                    && EnumName(_type.Assembly, "PileType", pile.Value) == "Hand" && IsOwner(owner);
            }
        }

        /// <summary>The filters (Where's lambdas) on a list of items, outermost first.</summary>
        private static IEnumerable<LambdaValue> Filters(ProgramValue items)
        {
            while (true)
            {
                if (items is StoredValue stored) items = stored.Value;
                if (items is QueryValue { Name: "Where", Arguments: [var source, LambdaValue filter] }) { yield return filter; items = source; continue; }
                if (items is QueryValue { Name: "ToList", Arguments: [var listed] }) { items = listed; continue; }
                yield break;
            }
        }

        /// <summary>
        /// The hook's code only sets values that show (ShownOnly), over loops of its own: nothing it does reaches what a fight runs. Read whole to
        /// tell: no command, store, flash or change to its data, no unknown, and at least one such value set.
        /// </summary>
        private bool SetsShownOnly(MethodInfo hook)
        {
            ModelProgram program;
            List<CodeOp> ops;
            try { program = ProgramReader.Read(_type, _read, hook.Name); ops = _read(ProgramReader.BodyOf(hook)); }
            catch (InvalidOperationException) { return false; }
            if (program.Unknowns.Any() || OwnData(hook, program) != null) return false;
            return program.All().All(step => step is LoopStep)
                && ops.Any(instruction => instruction.IsCall && instruction.Member is { } setter && ShownOnly.Contains(setter.DeclaringType + "." + setter.Name));
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
            && !member.Namespace.Contains(".Nodes", StringComparison.Ordinal) && member.Name is not ("Flash" or "InvokeDisplayAmountChanged" or "AssertMutable" or "SetResult" or "Start" or "AwaitUnsafeOnCompleted" or "SetStateMachine" or "SetException")
            && !ShowsOnly(member, depth: 0);

        /// <summary>
        /// A method of the model's own base class, or of a creature (Creature.SetNodeVisible: Infested's Wrigglers hidden until its death's animation
        /// ends), whose code only shows something (OrbModel.ActivatePassive: an event the orb's node hears; PlayEvokeSfx: a sound): it stores nothing,
        /// and every call in it asks (a getter), shows (a show-only command, a node, a sound) or invokes a delegate, or is such a method itself.
        /// </summary>
        private bool ShowsOnly(CodeMember member, int depth)
        {
            if (depth > 2) return false;
            MethodInfo? method = null;
            if (Derives(_type, member.DeclaringType))
            {
                for (var type = _type.BaseType; type != null && method == null; type = type.BaseType)
                    if (type.Name == member.DeclaringType) method = type.GetMethods(Declared).FirstOrDefault(candidate => candidate.Name == member.Name && !candidate.IsAbstract);
            }
            else if (member.Namespace == "MegaCrit.Sts2.Core.Entities.Creatures" && member.DeclaringType == nameof(MegaCrit.Sts2.Core.Entities.Creatures.Creature))
                method = _type.Assembly.GetType(member.Namespace + "." + member.DeclaringType)?.GetMethods(Declared)
                    .FirstOrDefault(candidate => candidate.Name == member.Name && candidate.GetParameters().Length == member.ParameterCount && !candidate.IsAbstract);
            if (method == null) return false;
            List<CodeOp> ops;
            try { ops = _read(ProgramReader.BodyOf(method)); }
            catch { return false; }
            foreach (var instruction in ops)
            {
                if (instruction.OpCode == System.Reflection.Emit.OpCodes.Stfld || instruction.OpCode == System.Reflection.Emit.OpCodes.Stsfld) return false;
                if (!instruction.IsCall || instruction.Member is not { } called) continue;
                if (called.Name.StartsWith("get_", StringComparison.Ordinal) || called.Name == nameof(Action.Invoke) || called.Namespace.StartsWith("System", StringComparison.Ordinal)
                    || called.Namespace.Contains(".Nodes", StringComparison.Ordinal) || called.Namespace.Contains(".Audio", StringComparison.Ordinal)
                    || called.Namespace.StartsWith("Godot", StringComparison.Ordinal) || called.DeclaringType is "SfxCmd" or "VfxCmd") continue;
                if (called.DeclaringType != member.DeclaringType || !ShowsOnly(called, depth + 1)) return false;
            }
            return true;
        }

        /// <summary>
        /// A side's turn-start body of a relic whose every step waits on its owner, a player, taking part: it acts only as the players' next turn
        /// starts, past a simulation's horizon (""). Null: read it as the enemies' turn start runs it; a gap when the reader can't follow it.
        /// </summary>
        private string? PastTheHorizon(MethodInfo hook)
        {
            var program = ProgramReader.Read(_type, _read, hook.Name);
            if (program.Unknowns.Any()) return "gap: the reader can't follow it: " + program.Unknowns.First();
            if (!program.All().Any()) return "";
            // Its every step at the top waits on it (a loop's body under the loop's condition: Infused Core's channels on its first turn).
            if (_kind == ModelKind.Relic && program.Steps.All(step => step.Condition?.Terms is { Count: > 0 } terms && terms.All(term => term.Any(literal => literal.Holds && IsTakingPart(literal.Test)))))
                return "";
            return null;
        }

        private IEnumerable<HookStep> Step(ProgramStep step)
        {
            _at = step.Offset;
            switch (step)
            {
                // A command whose creatures are a local of several ways naming different ones (Inky's targets: the play's target, or every enemy for a
                // card aimed at them all): a step for each way, on its test and none of the earlier ways' (the first way that holds is the value).
                case CommandStep command when _kind == ModelKind.Enchantment && CreatureWays(command) is { } split:
                {
                    var steps = new List<HookStep>();
                    HookValue? earlier = null;
                    foreach (var (when, at, creatures) in split)
                    {
                        var values = command.Values.ToList();
                        values[at] = creatures;
                        var way = Condition(when) ?? new HookConstant(1m);
                        var guard = earlier == null ? way : new HookMath(HookOperation.And, way, Not(earlier));
                        earlier = earlier == null ? way : new HookMath(HookOperation.Or, earlier, way);
                        if (Command(command with { Values = values }) is { } wayStep)
                            steps.Add(wayStep with { When = Condition(step.Condition) is { } outer ? new HookMath(HookOperation.And, outer, guard) : guard });
                    }
                    return steps;
                }
                case CommandStep command:
                    var translated = Command(command);
                    return translated == null ? Array.Empty<HookStep>() : new[] { translated with { When = Condition(step.Condition) } };
                // A card its code keeps in a field of its own (MusicBox's CardBeingPlayed): the card played kept binds the play in progress (BindStep),
                // null lets it go (UnbindStep), as a power's own data's Add(cardPlay.Card, ...) and Remove do; any other card is a gap.
                // A card play its code keeps (Stretch's data's cardPlay = cardPlay, Pael's Legion's AffectedCardPlay): the play's identity and its player,
                // each its state (KeptPlay); null lets it go. Any other play kept is a gap.
                case StoreStep playStore when KeepsAPlay(playStore.Field):
                {
                    var keptWhen = Condition(step.Condition);
                    if (IsHookPlay(playStore.Value) && GivenAPlay.Contains(_hook!))
                        return new HookStep[]
                        {
                            new StoreFieldStep(playStore.Field, new HookFactValue(HookFact.PlaySerial)) { When = keptWhen },
                            new StoreFieldStep(playStore.Field + ".Player", CreatureFact(HookFact.CreatureId, new HookCreature(HookWho.PlayOwner))) { When = keptWhen },
                        };
                    if (playStore.Value is NullValue)
                        return new HookStep[] { new StoreFieldStep(playStore.Field, new HookConstant(0m)) { When = keptWhen }, new StoreFieldStep(playStore.Field + ".Player", new HookConstant(Who.None)) { When = keptWhen } };
                    throw new Gap(ReaderCapability.OwnData, $"gap: keeps {playStore.Value} in {playStore.Field}");
                }
                // A card its code keeps (Music Box's CardBeingPlayed = cardPlay.Card, Vambrace's TriggeringCard = cardSource): the identity of the card play it
                // comes from (decision #272); null lets it go. Any other card kept is a gap.
                case StoreStep cardStore when KeepsACard(cardStore.Field):
                    if (PlayOfTheCard(cardStore.Value) is { } keptFrom) return new HookStep[] { new StoreFieldStep(cardStore.Field, new HookFactValue(keptFrom)) { When = Condition(step.Condition) } };
                    if (cardStore.Value is NullValue) return new HookStep[] { new StoreFieldStep(cardStore.Field, new HookConstant(0m)) { When = Condition(step.Condition) } };
                    throw new Gap(ReaderCapability.OwnData, $"gap: keeps {cardStore.Value} in {cardStore.Field}");
                // An attack its data keeps (Vigor's commandToModify = command): the attack's id, its card and its attacker, each its state (KeptAttack);
                // null lets it go. Any other attack kept is a gap.
                case StoreStep attackStore when HoldsAnAttack(attackStore.Field):
                {
                    var keptWhen = Condition(step.Condition);
                    HookStep[] Kept(HookValue id, HookValue card, HookValue attacker) => new HookStep[]
                    {
                        new StoreFieldStep(attackStore.Field, id) { When = keptWhen }, new StoreFieldStep(attackStore.Field + ".ModelSource", card) { When = keptWhen },
                        new StoreFieldStep(attackStore.Field + ".Attacker", attacker) { When = keptWhen },
                    };
                    if (IsAttack(attackStore.Value))
                        return Kept(new HookFactValue(HookFact.AttackId), new HookFactValue(HookFact.AttackCardId), CreatureFact(HookFact.CreatureId, new HookCreature(HookWho.Attacker)));
                    if (attackStore.Value is NullValue) return Kept(new HookConstant(0m), new HookConstant(0m), new HookConstant(0m));
                    throw new Gap(ReaderCapability.AttackHooks, $"gap: keeps {attackStore.Value} in {attackStore.Field}");
                }
                // A property set on an object the hook is given (an arrow, a card): nothing the simulator keeps, a gap.
                case StoreStep given when given.Field.StartsWith(ProgramReader.GivenPrefix, StringComparison.Ordinal):
                    throw new Gap(ReaderCapability.OtherModelValueSet, $"gap: sets {given.Field[ProgramReader.GivenPrefix.Length..]}");
                // A property set on its owner's monster (((CorpseSlug)base.Owner.Monster).IsRavenous = true): its monster's own state, which its moves and
                // its next move's choice read. Read where the hook stuns its owner on the same test (StoresWithAStun): the monster's move this turn is then
                // the stunned move, so what reads it is a later turn, past every simulation (the simulator runs no monster hook: MonsterHookTests).
                // Any other is its monster's state set (MonsterStateStep: Imbalanced's IsOffBalance), kept on its owner where its moves' code reads it.
                case StoreStep monsterStore when monsterStore.Field.StartsWith(ProgramReader.OwnersMonsterPrefix, StringComparison.Ordinal):
                    if (StoredWithAStun(monsterStore)) return Array.Empty<HookStep>();
                    // One no fight's code reads but to set it again (Lagavulin Matriarch's IsAwake, its sleeping effect): what the game shows.
                    if (MonsterStateShownOnly(monsterStore.Field)) return Array.Empty<HookStep>();
                    return new HookStep[] { new MonsterStateStep(monsterStore.Field, Value(monsterStore.Value)) { When = Condition(step.Condition) } };
                // A property set on a monster its death adds, before it joins (Infested's wriggler.StartStunned, Stock's axebot.StockAmount): how that
                // monster starts, which the adapter reads with the creature (SimAdapter.SpawnsOf: its HP, its starting powers), so no step here. One set
                // on a monster no CreatureCmd.Add of this hook adds is a gap.
                case StoreStep madeStore when madeStore.Field.StartsWith(ProgramReader.MadeMonsterPrefix, StringComparison.Ordinal):
                    if (_hook != "AfterDeath" || !AddsMadeMonster(madeStore.Field)) throw new Gap(ReaderCapability.MonsterStateSet, $"gap: sets {madeStore.Field[ProgramReader.MadeMonsterPrefix.Length..]} on a monster it doesn't add as it dies");
                    return Array.Empty<HookStep>();
                // A property its owner's monster's method sets on its own creature (Steam Eruption's base.Creature.HpDisplay), or a power on its owner
                // (Hardened Shell's base.Owner.HpDisplay): one that only shows (ShownOnly) is no step; any other is a gap.
                case StoreStep creatureStore when creatureStore.Field.StartsWith(ProgramReader.OwnCreaturePrefix, StringComparison.Ordinal):
                    if (!ShownOnly.Contains(nameof(MegaCrit.Sts2.Core.Entities.Creatures.Creature) + ".set_" + creatureStore.Field[ProgramReader.OwnCreaturePrefix.Length..]))
                        throw new Gap(ReaderCapability.OtherModelValueSet, $"gap: sets its owner's {creatureStore.Field[ProgramReader.OwnCreaturePrefix.Length..]}");
                    return Array.Empty<HookStep>();
                // A change to a collection the code makes itself (Infested's wrigglers.Add(creature)): nothing of the fight's, and anything that reads it
                // (a loop over it) is a gap of its own.
                case DataStep local when local.Data.StartsWith(ProgramReader.LocalPrefix, StringComparison.Ordinal):
                    return Array.Empty<HookStep>();
                case StoreStep store:
                    // A store to its own state (a field or property of its class, a plain field of its data): simulator state its programs read (HookField).
                    // One of its own values set (DynamicVars["CardsLeft"].BaseValue = ...): its state too, under the value's key.
                    var field = store.Field.StartsWith(ProgramReader.OwnVarPrefix, StringComparison.Ordinal) ? VarFieldPrefix + OwnVarKey(store.Field[ProgramReader.OwnVarPrefix.Length..]) : store.Field;
                    // One of its own fields or properties that holds a node (Echo Form's Vfx: how it looks) is what's shown, never read by a hook.
                    if (OwnMemberType(store.Field) is { } memberType && (memberType.Namespace ?? "").Contains(".Nodes", StringComparison.Ordinal)) return Array.Empty<HookStep>();
                    return new[] { new StoreFieldStep(field, Value(SetterStores(store.Field, store.Value))) { When = Condition(step.Condition) } };
                case DataStep data:
                    return new[] { Data(data) with { When = Condition(step.Condition) } };
                case LoopStep loop:
                {
                    // A kill of each creature of a list the code works out (DoomPower.DoomKill's foreach over GetDoomedCreatures: CreatureCmd.Kill of each
                    // one not dead yet, beside what only shows), then the hook its code tells that list (Hook.AfterDiedToDoom): KillEachStep.
                    if (loop.Items is { } killedItems && KillsEach(loop) && CreatureList(killedItems) is { } killed)
                        return new HookStep[] { new KillEachStep(killed.Over) { Where = killed.Where, Tells = TellsOf(killedItems), When = Condition(step.Condition) } };
                    // A loop removing every power its owner has of a class (foreach over base.Owner.GetPowerInstances<TemporaryStrengthPower>().ToList():
                    // PowerCmd.Remove(item): Plow's): each power class of the game's that is one off its owner (decision #275).
                    if (loop.Items is { } instances && OwnersPowersOf(instances) is { } powerClass && loop.Body is [CommandStep { Command.FullName: "PowerCmd.Remove" } removeEach]
                        && removeEach.Values is [var removed] && StepValues.IsEach(Unstored(removed)))
                        return new HookStep[] { new RemovePowerStep { Powers = Removable(new[] { powerClass }), When = Condition(step.Condition) } };
                    // A loop over the fight's players or its hittable enemies (foreach): its body once for each, the item Each inside it.
                    if (loop.Items is { } items && ItemsOf(items) is { } over)
                    {
                        var each = loop.Body.SelectMany(Step).ToList();
                        return each.Count == 0 ? Array.Empty<HookStep>() : new[] { new ForEachStep(over, each) { When = Condition(step.Condition) } };
                    }
                    // A loop over the owner's side filtered by a lambda (Where(GetTeammatesOf(base.Owner), c => ...): Covering Fire's other living
                    // players): the filter asked of each item, its parameter the item.
                    if (loop.Items is { } filteredItems && TeammatesWhere(filteredItems) is { } filter)
                    {
                        var each = loop.Body.SelectMany(Step).ToList();
                        var where = Condition(filter.Body.Map(part => part is ParameterValue { Name: var name } && name == filter.Parameter ? new NamedValue("each") : null));
                        return each.Count == 0 ? Array.Empty<HookStep>() : new[] { new ForEachStep(HookItems.Teammates, each) { When = Condition(step.Condition), Where = where } };
                    }
                    // A loop upgrading random cards of its owner's hand (foreach over TakeRandom(hand.Cards.Where(c => c.IsUpgradable), n): TunePower's,
                    // v0.114.0): UpgradeRandomInHandStep, n its count. Which cards is a roll, never read.
                    if (loop.Items is { } rolled && UpgradesRandomHandCards(rolled, loop.Body) is { } upgradeCount)
                        return new HookStep[] { new UpgradeRandomInHandStep(Value(upgradeCount)) { When = Condition(step.Condition) } };
                    // A loop making each of a few random cards of a type into its owner's hand (foreach over CardFactory.GetForCombat(owner, the
                    // character's pool of that type, n, rng): Calamity's): MakeRandomInHandStep, n its count. Which cards is a roll, never read.
                    if (loop.Items is { } made && RandomCardsMadeInHand(made, loop.Body) is { } madeRandom)
                        return new HookStep[] { new MakeRandomInHandStep(Value(madeRandom.Count), madeRandom.Type) { When = Condition(step.Condition) } };
                    // A counter loop (for i < n): its body n times, n worked out as the loop starts (the card programs read it the same way:
                    // ProgramEvaluator.Rounds). Any other loop is a gap.
                    // Its bound compared as an int (i < n) or as a decimal ((decimal)i < DynamicVars.Cards.BaseValue: Centennial Puzzle's).
                    var (bounded, inclusive) = loop.Items == null && loop.While is { Terms: [[{ Holds: true, Test: var whileTest }]] } ? CounterBound(whileTest) : (null, false);
                    if (bounded == null) throw new Gap(ReaderCapability.LoopOverItems, "gap: a loop");
                    var times = Value(bounded);
                    if (inclusive) times = new HookMath(HookOperation.Add, times, new HookConstant(1m));
                    var body = loop.Body.SelectMany(Step).ToList();
                    return body.Count == 0 ? Array.Empty<HookStep>() : new[] { new RepeatStep(times, body) { When = Condition(step.Condition) } };
                }
                // A card in the owner's hand made free this turn (Mummified Hand's SetToFreeThisTurn on its pick from the hand): on the card played's
                // own hook, the pick the game's random one among the hand's cards that cost. Whether the pick found a card is the step's own (it acts
                // only on a card that costs): the pick's tests in its condition hold.
                case CardActionStep { Action.Name: "SetToFreeThisTurn", Values.Count: 0 } free when IsPlayHook(_hook) && PicksAHandCardThatCosts(free.Card):
                {
                    _handPickHolds = true;
                    try { return new[] { new FreeInHandStep { When = Condition(step.Condition) } }; }
                    finally { _handPickHolds = false; }
                }
                case CardActionStep action:
                    throw new Gap(ReaderCapability.CardChanged, $"gap: changes {action.Card} ({action.Action.FullName})");
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
                // A card its code makes (CombatState.CreateCard<T>) that a command of the body adds to a pile: that command's step makes it (MakeIntoDrawStep).
                case CreateStep create when MadeByACommand(create):
                    return Array.Empty<HookStep>();
                case CreateStep create:
                    throw new Gap(ReaderCapability.CardMade, $"gap: makes a {create.Card}");
                default:
                    throw new Gap(ReaderCapability.ValueNotFollowed, $"gap: {step}");
            }
        }

        /// <summary>
        /// A loop whose body kills its item and does nothing else (CreatureCmd.Kill(each), on the item not being dead and being in the fight, beside
        /// commands that change nothing a simulation follows: MonsterModel.OnDieToDoom's looks).
        /// </summary>
        private static bool KillsEach(LoopStep loop)
        {
            var acting = loop.Body.Where(step => step is not CommandStep { Command.FullName: var name } || !SimAdapter.CommandsOutsideTheFight.ContainsKey(name)).ToList();
            if (acting is not [CommandStep { Command.FullName: "CreatureCmd.Kill", Values: [var killedValue, ..] } kill] || !StepValues.IsEach(Unstored(killedValue))) return false;
            return kill.Condition == null || kill.Condition.Terms is [var only] && only.All(literal => Unstored(literal.Test) is QueryValue { Of: { } of } tested && StepValues.IsEach(Unstored(of))
                && (tested.Name == "IsDead" && !literal.Holds || tested.Name is "IsAlive" or "CombatState" && literal.Holds));
        }

        /// <summary>The steps of this hook's body that tell a hook a list a kill went over, taken by that kill's step (TellsOf).</summary>
        private readonly HashSet<ProgramStep> _told = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// The hook the body tells the list a kill went over (Hook.AfterDiedToDoom(combatState, creatures), its list the loop's items), taken by that
        /// kill's step; null where it tells none.
        /// </summary>
        private HookName? TellsOf(ProgramValue items)
        {
            if (_body == null) return null;
            foreach (var told in _body.Steps.OfType<CommandStep>())
            {
                if (!ProgramReader.IsHookTold(told.Command) || !told.Values.Any(value => Equals(Unstored(value), Unstored(items)))) continue;
                if (!Enum.TryParse<HookName>(told.Command.Name, out var hook)) throw new Gap(ReaderCapability.CommandNotTranslated, $"gap: tells {told.Command.FullName}, which the simulator doesn't run");
                _told.Add(told);
                return hook;
            }
            return null;
        }

        /// <summary>A CreateStep whose card a command of the body adds to a pile (CardPileCmd.AddGeneratedCardToCombat of CreateCard&lt;T&gt;: MadeOfAClass).</summary>
        private bool MadeByACommand(CreateStep create) => _body != null && _body.All().OfType<CommandStep>().Any(command => command.Command.FullName == "CardPileCmd.AddGeneratedCardToCombat"
            && command.Values.Count > 0 && MadeOfAClass(command.Values[0]) is { } made && made.GenericArguments[0] == create.Card);

        /// <summary>The card a command adds, when the code makes it there of a class it names (CombatState.CreateCard&lt;T&gt;(player)); null otherwise.</summary>
        private static QueryValue? MadeOfAClass(ProgramValue value) =>
            Unstored(value) is QueryValue { Name: "CreateCard", GenericArguments.Count: 1, Arguments.Count: 1 } create ? create : null;

        /// <summary>
        /// CardPileCmd.AddGeneratedCardToCombat(CreateCard&lt;T&gt;(player), PileType.Draw, creator, CardPilePosition.Random): MakeIntoDrawStep, its card
        /// the class's canonical one (its type and tags: what the created-card hooks test), for the player the code names (a creature of the hook's: the
        /// owner, the dealer or its pet's player, on each way the code sets it), its creator the owner or none. Any other pile, position or creator is a gap.
        /// </summary>
        private HookStep MakeIntoDraw(QueryValue created, IReadOnlyList<ProgramValue> values, IReadOnlyList<string> types)
        {
            var card = created.GenericArguments[0];
            var pileAt = IndexOf(types, "PileType");
            var creatorAt = IndexOf(types, "Player");
            var positionAt = IndexOf(types, "CardPilePosition");
            if (pileAt < 0 || values[pileAt] is not ConstantValue pile || EnumName(_type.Assembly, "PileType", pile.Value) != "Draw")
                throw new Gap(ReaderCapability.CardMade, $"gap: a {card} made into another pile");
            if (positionAt < 0 || values[positionAt] is not ConstantValue position || EnumName(_type.Assembly, "CardPilePosition", position.Value) != "Random")
                throw new Gap(ReaderCapability.CardMade, $"gap: a {card} made into a draw pile not at a random spot");
            var byOwner = creatorAt >= 0 && IsOwner(values[creatorAt]);
            if (!byOwner && (creatorAt < 0 || values[creatorAt] is not NullValue)) throw new Gap(ReaderCapability.CommandOnAnotherCreature, $"gap: a {card} made by {(creatorAt >= 0 ? values[creatorAt] : "none")}");
            var (madeType, madeTags) = MadeCardFacts(card);
            return new MakeIntoDrawStep(card, madeType, madeTags, PlayerIdOf(created.Arguments[0]), byOwner);
        }

        /// <summary>
        /// A made card's type and tags, read from its class's code as the created-card hooks test them: its type the constant its constructor passes
        /// CardModel's (base(cost, CardType.Status, ...)), its tags none unless the class overrides CanonicalTags (then a gap: not read yet).
        /// </summary>
        private (SimCardType Type, IReadOnlyList<SimCardTag> Tags) MadeCardFacts(string card)
        {
            var cardType = _type.Assembly.GetTypes().FirstOrDefault(candidate => candidate.Name == card && Derives(candidate, "CardModel"))
                ?? throw new Gap(ReaderCapability.CardMade, $"gap: a {card} whose class isn't found");
            if (cardType.GetMethods(Declared).Any(method => method.Name == "get_CanonicalTags")) throw new Gap(ReaderCapability.CardMade, $"gap: the tags of a made {card}");
            foreach (var constructor in cardType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var ops = _read(constructor);
                for (var i = 0; i < ops.Count; i++)
                {
                    if (ops[i] is not { OpCode.Name: "call", Member: { Name: ".ctor", DeclaringType: "CardModel" } baseConstructor }) continue;
                    var at = baseConstructor.ParameterTypes.ToList().IndexOf("CardType");
                    var count = baseConstructor.ParameterTypes.Count;
                    // The arguments pushed as constants just before the call, in order.
                    if (at < 0 || i < count || ops.Skip(i - count).Take(count).Any(argument => argument.Integer == null)) break;
                    var name = EnumName(_type.Assembly, "CardType", ops[i - count + at].Integer!.Value);
                    return (name switch
                    {
                        "Attack" => SimCardType.Attack, "Skill" => SimCardType.Skill, "Power" => SimCardType.Power, "Status" => SimCardType.Status, "Curse" => SimCardType.Curse,
                        _ => throw new Gap(ReaderCapability.CardMade, $"gap: a made {card} of type {name}"),
                    }, Array.Empty<SimCardTag>());
                }
            }
            throw new Gap(ReaderCapability.CardMade, $"gap: a made {card} whose type its constructor doesn't pass as a constant");
        }

        /// <summary>
        /// The player a creature value of the hook names, as a creature id (HookFact.CreatureId): one creature of the hook's, or each way of a value
        /// the code sets on several (PersonalHivePower's dealer, or its pet's player). Anything else is a gap.
        /// </summary>
        private HookValue PlayerIdOf(ProgramValue value)
        {
            var unstored = Unstored(value);
            if (unstored is QueryValue { Name: "Player", Of: { } of } && Unstored(of) is WaysValue ways)
                return new HookWays(ways.Ways.Select(way => new HookWay(Condition(way.When) ?? new HookConstant(1m), PlayerIdOf(new QueryValue("Creature", "Player", way.Value, Array.Empty<ProgramValue>(), "Player") { IsProperty = true }))).ToList());
            if (CreatureOf(unstored) is { } creature) return CreatureFact(HookFact.CreatureId, creature);
            throw new Gap(ReaderCapability.CommandOnAnotherCreature, $"gap: a card made for {value}");
        }
        /// <summary>The hook body's program being read (BodySteps), for what a step asks of the steps around it.</summary>
        private ModelProgram? _body;

        /// <summary>
        /// A store to its owner's monster's state made on the test a CreatureCmd.Stun of its owner in the same body is made on (Flutter's IsHovering =
        /// false and Ravenous' IsRavenous = true, each beside the stun): the monster's move this turn is the stunned move.
        /// </summary>
        private bool StoredWithAStun(StoreStep store) => _body != null && _body.All().OfType<CommandStep>().Any(command => command.Command.FullName == "CreatureCmd.Stun"
            && command.Values.Count > 0 && IsOwner(command.Values[0]) && string.Equals(command.Condition?.ToString(), store.Condition?.ToString(), StringComparison.Ordinal));

        /// <summary>
        /// A CreatureCmd.Add in the same body adds the monster a store sets up ("Made.Wriggler.StartStunned": a CreatureCmd.Add(monster, ...) of a
        /// (Wriggler)ModelDb.Monster&lt;Wriggler&gt;().ToMutable()).
        /// </summary>
        private bool AddsMadeMonster(string field)
        {
            var monster = field[ProgramReader.MadeMonsterPrefix.Length..field.LastIndexOf('.')];
            return _body != null && _body.All().OfType<CommandStep>().Any(command => command.Command.FullName == "CreatureCmd.Add" && command.Values.Count > 0
                && ProgramReader.MadeMonster(command.Values[0]) == monster);
        }

        /// <summary>
        /// A counter loop's bound: what its test compares the counter with (i &lt; n, (decimal)i &lt; n through Decimal's operator), and whether it
        /// includes it (&lt;=); null for any other test.
        /// </summary>
        private static (ProgramValue? Bound, bool Inclusive) CounterBound(ProgramValue test) => test switch
        {
            CompareValue { Op: Comparison.Less or Comparison.LessOrEqual } compare => (compare.Right, compare.Op == Comparison.LessOrEqual),
            QueryValue { Type: "Decimal", Name: "op_LessThan" or "op_LessThanOrEqual", Arguments: [_, var bound] } decimalCompare => (bound, decimalCompare.Name == "op_LessThanOrEqual"),
            _ => (null, false),
        };

        /// <summary>The key of one of its own values a store names (a DynamicVarSet property's own key, or an indexed key as written).</summary>
        private string OwnVarKey(string named) => named.StartsWith('"') ? named.Trim('"') : KeyOfProperty(named);

        /// <summary>
        /// A change to its own data: a value bound to the play in progress (Add(cardPlay.Card, value)), or taken back for its out value
        /// (Remove(cardPlay.Card, out value)); any other is a gap.
        /// </summary>
        private HookStep Data(DataStep data)
        {
            // More on what it bound to the play in progress, under the damage's card (Rupture's playedCards[cardSource] += base.Amount): its condition holds the
            // damage's card to the bound one (ContainsKey(cardSource): HookFact.SourceBound), the only key its data keeps.
            if (data.Operation == "set_Item" && data.Values is [var sourceKey, ArithmeticValue { Op: "add", Left: QueryValue { Name: "Item" or "get_Item", Arguments: [var readKey] }, Right: var added }]
                && IsSourceCard(Unstored(sourceKey)) && IsSourceCard(Unstored(readKey)))
                return new BindAddStep(Value(added));
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
            // A hook the code tells itself: the list a kill went over, its kill's step's (KillEachStep.Tells); any other is a gap.
            if (ProgramReader.IsHookTold(member))
                return _told.Contains(command) ? null : throw new Gap(ReaderCapability.CommandNotTranslated, $"gap: tells {member.FullName} itself");
            // A command that changes nothing a simulation follows (MonsterModel.OnDieToDoom: a monster's looks as Doom kills it).
            if (SimAdapter.CommandsOutsideTheFight.ContainsKey(member.FullName)) return null;
            switch (member.FullName)
            {
                case "CreatureCmd.GainBlock":
                {
                    // Block for a loop's player (each.Creature: Frost's for each other player with Hibernate).
                    if (CreatureOf(values[0]) is { Who: HookWho.Each, PetOwner: false } && types[1] != "BlockVar")
                        return new GainBlockStep(Value(values[1]), Value(values[2])) { To = HookTarget.Each };
                    if (!IsOwner(values[0])) throw new Gap(Other(values[0]), $"gap: block for {values[0]}");
                    if (types[1] == "BlockVar")
                    {
                        var key = VarKey(values[1]) ?? throw new Gap(ReaderCapabilities.OfValue(values[1]), $"gap: block of {values[1]}");
                        return new GainBlockStep(Var(key), Var(key + ".Props"));
                    }
                    return new GainBlockStep(Value(values[1]), Value(values[2]));
                }
                // CreatureCmd.LoseBlock(choiceContext, target, amount, remover) of its owner (Burrowed's as it goes), by its owner or no one.
                case "CreatureCmd.LoseBlock":
                {
                    var targetAt = IndexOf(types, "Creature");
                    var amountAt = IndexOf(types, "Decimal");
                    var removerAt = IndexOf(types, from: amountAt + 1, "Creature");
                    if (targetAt < 0 || amountAt < 0 || removerAt < 0) throw new Gap(ReaderCapability.CommandNotTranslated, $"gap: block loss overload {string.Join(",", types)}");
                    if (!IsOwner(values[targetAt])) throw new Gap(Other(values[targetAt]), $"gap: a block loss for {values[targetAt]}");
                    var byOwner = IsOwner(values[removerAt]);
                    if (!byOwner && values[removerAt] is not NullValue) throw new Gap(ReaderCapability.CommandOnAnotherCreature, $"gap: a block loss by {values[removerAt]}");
                    return new LoseBlockStep(Value(values[amountAt])) { ByOwner = byOwner };
                }
                // A player's max HP lost (Paper Cuts' on the hit's target): CreatureCmd.LoseMaxHp(choiceContext, creature, amount, cardSource), no card.
                case "CreatureCmd.LoseMaxHp":
                {
                    var targetAt = IndexOf(types, "Creature");
                    var amountAt = IndexOf(types, "Decimal");
                    if (targetAt < 0 || amountAt < 0) throw new Gap(ReaderCapability.CommandNotTranslated, $"gap: max HP loss overload {string.Join(",", types)}");
                    if (IndexOf(types, "CardModel") is var cardAt and >= 0 && values[cardAt] is not NullValue) throw new Gap(ReaderCapability.CommandNotTranslated, "gap: a max HP loss from a card");
                    var lost = Targets(values[targetAt]);
                    if (lost is not (HookTarget.HitTarget or HookTarget.Owner)) throw new Gap(ReaderCapability.CommandOnAnotherCreature, $"gap: max HP lost by {values[targetAt]}");
                    return new LoseMaxHpStep(lost, Value(values[amountAt]));
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
                    if (damaged is not (HookTarget.AllEnemies or HookTarget.RandomEnemy or HookTarget.PowerOwner or HookTarget.Owner or HookTarget.WeakestEnemy or HookTarget.EvokeTargets or HookTarget.Each or HookTarget.LivingPlayers or HookTarget.PlayOwner or HookTarget.Dealer))
                        throw new Gap(ReaderCapability.CommandOnAnotherCreature, $"gap: damage to {values[targetAt]}");
                    return new DamageStep(damaged, amount, props) { NoDealer = noDealer };
                }
                // A copy of the card played made into its owner's hand (MusicBox's cardPlay.Card.CreateClone(), its keywords applied to the copy first):
                // a card made, its facts the card played's with those keywords (MakeCopyStep). Any other card made is a gap.
                case "CardPileCmd.AddGeneratedCardToCombat":
                {
                    var pileAt = IndexOf(types, "PileType");
                    var creatorAt = IndexOf(types, "Player");
                    // A copy of a card event's card (AfterCardExhausted's card.CreateClone(): Burning Sticks'), the keywords it has kept.
                    var ofEventCard = values.Count > 0 && Unstored(values[0]) is QueryValue { Name: "CreateClone", Of: { } eventCloned } && IsEventCard(eventCloned) && _hook != "AfterCardPlayed";
                    // A card of a class its code names (CombatState.CreateCard<T>(player)) into a draw pile at a random spot: Biiig Hug's Soot, Personal Hive's Dazed.
                    if (values.Count > 0 && MadeOfAClass(values[0]) is { } created) return MakeIntoDraw(created, values, types);
                    if (values.Count == 0 || !IsCopyOfPlayCard(values[0]) && !ofEventCard) throw new Gap(ReaderCapability.CardMade, $"gap: makes {(values.Count > 0 ? values[0] : "nothing")}");
                    if (pileAt < 0 || values[pileAt] is not ConstantValue pile || EnumName(_type.Assembly, "PileType", pile.Value) != "Hand") throw new Gap(ReaderCapability.CardMade, "gap: a card made into another pile");
                    if (creatorAt < 0 || !IsOwner(values[creatorAt])) throw new Gap(ReaderCapability.CommandOnAnotherCreature, "gap: a card made for someone else");
                    var keywords = _copyKeywords.TryGetValue(values[0].ToString(), out var applied) ? applied : new List<string>();
                    if (keywords.Any(keyword => keyword != "Ethereal")) throw new Gap(ReaderCapability.CardChanged, $"gap: the keywords {string.Join(", ", keywords)} on a card made");
                    return new MakeCopyStep(Ethereal: keywords.Contains("Ethereal"), OfEventCard: ofEventCard);
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
                // OstyCmd.Summon(choiceContext, summoner, amount, source) for its owner, the player who played the card, or the hit's dealer's player.
                case "OstyCmd.Summon":
                {
                    var summonerAt = IndexOf(types, "Player");
                    var amountAt = IndexOf(types, "Decimal");
                    if (summonerAt < 0 || amountAt < 0) throw new Gap(ReaderCapability.Osty, $"gap: summon overload {string.Join(",", types)}");
                    var summoner = Unstored(values[summonerAt]);
                    var amount = Value(values[amountAt]);
                    if (IsOwner(summoner)) return new SummonStep(HookTarget.Owner, amount);
                    if (summoner is QueryValue { Name: "Owner", Of: { } card } && IsPlayCard(card)) return new SummonStep(HookTarget.PlayOwner, amount);
                    if (summoner is QueryValue { Name: "PetOwner", Of: { } pet } && IsDealer(pet) && DamageHooks.Contains(_hook)) return new SummonStep(HookTarget.Dealer, amount) { PetOwner = true };
                    throw new Gap(ReaderCapability.Osty, $"gap: a summon for {summoner}");
                }
                // Its owner stunned (CreatureCmd.Stun(base.Owner, stunMove, nextMoveId)): the stunned move's code is the delegate it's given (the reader
                // reads a delegate's code: its commands are the command's Inner), run as its owner acts in the enemy turn: its own monster's method
                // (Slumbering Beetle's WakeUpMove: its base.Creature its owner) translated on its owner (StunStep.Move). The next move's id it's given (a
                // constant: Asleep's "SLASH_MOVE") is the move it rolls after the stunned one (decision #307); one the code works out (Flutter's, Plow's)
                // is left out (null: its last move's, which the comparison doesn't ask).
                case "CreatureCmd.Stun":
                {
                    if (!IsOwner(values[0])) throw new Gap(Other(values[0]), $"gap: stuns {values[0]}");
                    // The stunned move it's given is code the reader read (a lambda or a method: one that calls a command is the command's Inner).
                    if (IndexOf(types, "Func`2") is var moveAt and >= 0 && (moveAt >= values.Count || values[moveAt] is not LambdaValue))
                        throw new Gap(ReaderCapability.MonsterStateSet, $"gap: a stunned move the reader doesn't read ({(moveAt < values.Count ? values[moveAt] : "none")})");
                    var nextAt = IndexOf(types, "String");
                    var next = nextAt >= 0 && nextAt < values.Count && Unstored(values[nextAt]) is NamedValue { Name: ['"', .., '"'] named } ? named.Trim('"') : null;
                    List<HookStep>? move = null;
                    if (command.Inner.Count > 0)
                    {
                        // Its code's this, a method of its owner's monster: that monster (base.Creature is its owner).
                        var monster = new QueryValue("Creature", "Monster", Unstored(values[0]), Array.Empty<ProgramValue>(), "MonsterModel") { IsProperty = true };
                        move = command.Inner.Select(inner => ProgramReader.BoundStep(inner, part => part is NamedValue { Name: "this" } ? monster : null)).SelectMany(Step).ToList();
                        if (move.Any(step => step is not (RemovePowerStep or ApplyPowerStep or GainBlockStep or MonsterStateStep)))
                            throw new Gap(ReaderCapability.StunOrKill, "gap: a stunned move that does more than change its own powers, block or state");
                    }
                    return new StunStep { NextMove = next, Move = move is { Count: > 0 } ? move : null };
                }
                // Its owner killed (CreatureCmd.Kill(base.Owner, force): The Gambit's). Force a constant (false: a death prevention may hold it back).
                case "CreatureCmd.Kill":
                {
                    if (values.Count == 0 || !IsOwner(values[0])) throw new Gap(Other(values.Count > 0 ? values[0] : NullValue.Instance), $"gap: kills {(values.Count > 0 ? values[0] : "nothing")}");
                    var forceAt = IndexOf(types, "Boolean");
                    if (forceAt >= 0 && (forceAt >= values.Count || values[forceAt] is not ConstantValue)) throw new Gap(ReaderCapability.StunOrKill, $"gap: a kill's force the reader can't follow ({(forceAt < values.Count ? values[forceAt] : "none")})");
                    if (IndexOf(types, "CardModel") >= 0) throw new Gap(ReaderCapability.StunOrKill, "gap: a kill by a card");
                    return new KillStep(Forced: forceAt >= 0 && values[forceAt] is ConstantValue { Value: not 0 });
                }
                case "PlayerCmd.GainEnergy":
                    // Energy for a loop's player (item.Player over the owner's living players: Compost's), each in turn.
                    if (CreatureOf(values[1]) is { Who: HookWho.Each, PetOwner: false }) return new GainEnergyStep(Value(values[0])) { To = HookTarget.Each };
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
                    if (Targets(values[targetAt]) is not (HookTarget.Owner or HookTarget.PowerOwner or HookTarget.AllEnemies or HookTarget.HitTarget or HookTarget.PlayTarget or HookTarget.PlayOwner or HookTarget.LivingOpponents or HookTarget.Added or HookTarget.Each)) throw new Gap(Other(values[targetAt]), $"gap: {power} on {values[targetAt]}");
                    // A temporary Strength or Dexterity power (Helical Dart's) lands as the Strength or Dexterity its BeforeApplied applies, by its sign
                    // (TemporaryStrengthPower, TemporaryDexterityPower: IsPositive), as the card programs land it (SimAdapter.PowersFrom).
                    if (TemporaryOf(power) is { } underlying)
                    {
                        // Its sign is its own class's IsPositive (TemporaryStrengthPower.Type asks it: MonarchsGazeStrengthDownPower's is false).
                        var negative = !IsPositive(power) || LandsAsDebuff(power, amount);
                        return new ApplyPowerStep(underlying, Targets(values[targetAt]), negative ? new HookMath(HookOperation.Multiply, amount, new HookConstant(-1m)) : amount) { Debuff = negative, By = by };
                    }
                    return new ApplyPowerStep(power, Targets(values[targetAt]), amount)
                    {
                        Debuff = LandsAsDebuff(power, amount), By = by,
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
                // OrbCmd.EvokeLast(choiceContext, player, dequeue): its owner's last orb evoked and dequeued (Consuming Shadow's); one kept in its slot is a gap.
                case "OrbCmd.EvokeLast":
                {
                    var playerAt = IndexOf(types, "Player");
                    if (playerAt < 0 || !IsOwner(values[playerAt])) throw new Gap(ReaderCapability.CommandOnAnotherCreature, "gap: an orb evoked for someone else");
                    var dequeueAt = IndexOf(types, "Boolean");
                    if (dequeueAt >= 0 && values[dequeueAt] is not ConstantValue { Value: not 0 }) throw new Gap(ReaderCapability.Orbs, "gap: an orb evoked and kept in its slot");
                    return new EvokeLastStep();
                }
                // PowerCmd.ModifyAmount(this, offset, applier, cardSource): its own amount changed (Vigor's spent), as PowerCmd.Apply of itself on its owner
                // changes an instance there (the same pipeline: the amount received, the log, AfterPowerAmountChanged; gone at 0 unless it can go below).
                // Another power's, or one copy of a power kept in copies, is a gap.
                case "PowerCmd.ModifyAmount":
                {
                    var powerAt = IndexOf(types, "PowerModel");
                    if (powerAt < 0 || values[powerAt] is not NamedValue { Name: "this" }) throw new Gap(ReaderCapability.PowerAmountChanged, "gap: PowerCmd.ModifyAmount of another power");
                    if (_kind != ModelKind.Power || Copies() != PowerCopies.One) throw new Gap(ReaderCapability.PowerAmountChanged, "gap: PowerCmd.ModifyAmount of one copy");
                    var offsetAt = IndexOf(types, "Decimal");
                    var applierAt = IndexOf(types, from: offsetAt + 1, "Creature");
                    if (offsetAt < 0 || applierAt < 0) throw new Gap(ReaderCapability.PowerAmountChanged, "gap: PowerCmd.ModifyAmount with no offset or applier");
                    HookCreature? by = IsOwner(values[applierAt]) ? null : values[applierAt] is NullValue ? new HookCreature(HookWho.NoOne)
                        : IsApplier(values[applierAt]) ? new HookCreature(HookWho.Applier) : throw new Gap(ReaderCapability.CommandOnAnotherCreature, "gap: its amount changed by someone else");
                    var offset = Value(values[offsetAt]);
                    return new ApplyPowerStep(_type.Name, HookTarget.Owner, offset)
                    {
                        Debuff = LandsAsDebuff(_type.Name, offset), By = by, GoesAtZero = !AllowsNegative(_type.Name),
                    };
                }
                // Another power of its owner's removed by its class (PowerCmd.Remove<StrengthPower>(base.Owner): Plow's): that power off its owner, when
                // nothing of its own runs as it goes (no AfterRemoved of its class's).
                case "PowerCmd.Remove" when member.GenericArguments is [var removedClass] && values.Count == 1 && IsOwner(Unstored(values[0])):
                    return new RemovePowerStep { Powers = Removable(new[] { removedClass }) };
                // Its owner's instance of another power removed (PowerCmd.Remove(base.Owner.GetPower<PlatingPower>()): Asleep's, Slumbering Beetle's
                // WakeUpMove): that power off its owner (GetPower gives the one instance, or null, which PowerCmd.Remove does nothing with).
                case "PowerCmd.Remove" when values.Count >= 1 && Unstored(values[0]) is QueryValue { Name: "GetPower", GenericArguments: [var instanceClass], Of: { } instanceOf }
                    && IsOwner(Unstored(instanceOf)):
                    return new RemovePowerStep { Powers = Removable(new[] { instanceClass }) };
                case "PowerCmd.Remove" or "PowerCmd.Decrement" or "PowerCmd.TickDownDuration":
                    if (values[0] is not NamedValue { Name: "this" }) throw new Gap(ReaderCapability.CommandOnAnotherPower, $"gap: {member.FullName} of another power");
                    return member.Name == "Remove" ? new RemovePowerStep() : new DecrementPowerStep { TicksDuration = member.Name == "TickDownDuration" };
                // A change to the card played (decision #218): an upgrade, or a keyword added. Another card (one in the hand, a pick) is a gap.
                case "CardCmd.Upgrade":
                    if (values.Count == 0 || !IsPlayCard(values[0])) throw new Gap(ReaderCapability.CardChanged, $"gap: upgrades {(values.Count > 0 ? values[0] : "nothing")}, not the card played");
                    return new UpgradeCardStep();
                case "CardCmd.ApplyKeyword":
                {
                    // A keyword on a copy of the card played that its code then makes (MusicBox's): the made card's (MakeCopyStep), no step of its own.
                    if (values.Count >= 2 && IsCopyOfPlayCard(values[0]) && KeywordsOf(values[1]) is { } copyKeywords)
                    {
                        var key = values[0].ToString();
                        _copyKeywords[key] = (_copyKeywords.TryGetValue(key, out var earlier) ? earlier : new List<string>()).Concat(copyKeywords).ToList();
                        return null;
                    }
                    if (values.Count < 2 || !IsPlayCard(values[0])) throw new Gap(ReaderCapability.CardChanged, $"gap: a keyword on {(values.Count > 0 ? values[0] : "nothing")}, not the card played");
                    var keywords = KeywordsOf(values[1]);
                    if (keywords is not [var keyword]) throw new Gap(ReaderCapability.CardChanged, $"gap: the keywords {values[1]}");
                    return new ApplyKeywordStep(keyword);
                }
                // A creature its owner's death adds to the fight (CreatureCmd.Add<T>, or of a monster its code makes and sets up first: Infested's Wrigglers,
                // Stock's next Axebot): the creature the adapter reads from this same code with every HP it can start with (SimAdapter.SpawnsOf), added
                // once the hit is over (SpawnStep). Added in any other hook, or as another creature dies, it's a gap.
                case "CreatureCmd.Add":
                    if (_hook != "AfterDeath") throw new Gap(ReaderCapability.CommandOnAnotherCreature, "gap: a creature added other than as a creature dies");
                    return new SpawnStep();
                // Its owner's max and current HP set (CreatureCmd.SetMaxAndCurrentHp(base.Creature, amount), in its monster's method: Steam Eruption's
                // TriggerAboutToBlowState): on a dead enemy, a revive.
                case "CreatureCmd.SetMaxAndCurrentHp":
                    if (values.Count < 2 || !IsOwner(Unstored(values[0]))) throw new Gap(Other(values.Count > 0 ? values[0] : NullValue.Instance), $"gap: sets the HP of {(values.Count > 0 ? values[0] : "nothing")}");
                    return new SetHpStep(Value(values[1]));
                // Its owner's monster's next move set to one of that monster's own (SetMoveImmediate(AboutToBlowState, forceTransition: true): Steam
                // Eruption's), forced: the move the adapter reads from that monster (SimEnemy.SetMoves). Not forced, the monster's current move decides
                // whether it changes (MoveState.CanTransitionAway): a gap.
                case "MonsterModel.SetMoveImmediate":
                    if (values.Count < 2 || Unstored(values[0]) is not QueryValue { IsProperty: true, Name: var moveProperty, Of: QueryValue { Name: "Monster", Of: { } monsterOf } } || !IsOwner(Unstored(monsterOf)))
                        throw new Gap(ReaderCapability.MonsterStateSet, $"gap: a move set from {(values.Count > 0 ? values[0] : "nothing")}");
                    if (values[1] is not ConstantValue { Value: not 0 }) throw new Gap(ReaderCapability.MonsterStateSet, "gap: a move set unless the current one holds it");
                    return new SetMoveStep(moveProperty);
                default:
                    throw new Gap(ReaderCapabilities.OfCommand(member.FullName), $"gap: {member} isn't translated");
            }
        }

        /// <summary>
        /// The hook's every command afflicts the card it's given (CardCmd.Afflict&lt;T&gt;(card, ...)) and nothing else changes: no store, no
        /// other command, no call that changes a game object; read whole to tell.
        /// </summary>
        private bool AfflictsTheEnteringCardOnly(MethodInfo hook)
        {
            ModelProgram program;
            try { program = ProgramReader.Read(_type, _read, hook.Name); }
            catch (InvalidOperationException) { return false; }
            if (program.Unknowns.Any() || OwnData(hook, program) != null) return false;
            var steps = program.All().ToList();
            return steps.Count > 0 && steps.All(step => step is CommandStep { Command: { FullName: "CardCmd.Afflict" } } command
                && command.Values.Count > 0 && command.Values[0] is ParameterValue { Name: "card" } or NamedValue { Name: "card" });
        }

        /// <summary>The keywords a params array of the game's CardKeyword names (ApplyKeyword(card, CardKeyword.Sly)), each by its name; null for any other value.</summary>
        private List<string>? KeywordsOf(ProgramValue value)
        {
            if (value is not ArrayValue { Items: var items }) return null;
            var names = new List<string>();
            foreach (var item in items)
                if (item is ConstantValue constant && EnumName(_type.Assembly, "CardKeyword", constant.Value) is { } name) names.Add(name);
                else return null;
            return names;
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
            // A list of one (new ReadOnlySingleElementList(x)): x.
            if (value is QueryValue { Name: ".ctor", Arguments: [var single] } listOfOne && listOfOne.Type.StartsWith("<>z__ReadOnlySingleElementList", StringComparison.Ordinal)) return Targets(single);
            // A value of several ways (Lightning's target ?? a roll): the ways that can hold, all naming the same creatures.
            if (value is WaysValue ways)
            {
                var named = ways.Ways.Where(way => Simplified(Condition(way.When) ?? new HookConstant(1m)) is not HookConstant { Value: 0m }).Select(way => Targets(way.Value)).Distinct().ToList();
                return named.Count == 1 ? named[0] : throw new Gap(ReaderCapability.CommandOnAnotherCreature, $"gap: acts on {value}");
            }
            // The loop's item (each, each.Creature).
            if (CreatureOf(value) is { Who: HookWho.Each, PetOwner: false }) return HookTarget.Each;
            if (IsHittableEnemies(value)) return HookTarget.AllEnemies;
            if (value is QueryValue { Name: "NextItem", Arguments: [var rolled] } && IsHittableEnemies(rolled)) return HookTarget.RandomEnemy;
            // The hittable enemy with the least HP (MinBy(HittableEnemies, c => c.CurrentHp)).
            if (value is QueryValue { Type: "Enumerable" or "MoreEnumerable", Name: "MinBy", Arguments: [var among, LambdaValue byHp] } && IsHittableEnemies(among) && LambdaReads(byHp, "CurrentHp")) return HookTarget.WeakestEnemy;
            // The living creatures an evoke acted on (AfterOrbEvoked's targets.Where(c => c.IsAlive)).
            if (_hook == "AfterOrbEvoked" && Unstored(value) is QueryValue { Name: "ToList", Arguments: [QueryValue { Type: "Enumerable", Name: "Where", Arguments: [ParameterValue { Name: "targets" } or NamedValue { Name: "targets" }, LambdaValue alive] }] }
                && LambdaReads(alive, "IsAlive")) return HookTarget.EvokeTargets;
            // The creature a hit landed on (AfterDamageGiven's, AfterDamageReceived's target), or whose block broke (AfterBlockBroken's: Hand Drill's Vulnerable).
            if (value is ParameterValue { Name: "target" } or NamedValue { Name: "target" } && _hook is "AfterDamageGiven" or "AfterDamageReceived" or "BeforeDamageReceived" or "AfterBlockBroken") return HookTarget.HitTarget;
            // The damage's dealer (BeforeDamageReceived's, a hit's: Thorns, Flame Barrier and Reflect hit it back).
            if (IsDealer(value) && _hook is "AfterDamageGiven" or "AfterDamageReceived" or "BeforeDamageReceived") return HookTarget.Dealer;
            // The card play's target (an enchantment's OnPlay: Inky's Weak on cardPlay.Target).
            if (IsPlayTarget(value)) return HookTarget.PlayTarget;
            if (IsOwner(value)) return HookTarget.Owner;
            // Every living player creature (CombatState.PlayerCreatures.Where(c => c.IsAlive), listed or not: Explosive's).
            if (LivingPlayerCreatures(value)) return HookTarget.LivingPlayers;
            // A player's model's living opponents (Where(combatState.GetOpponentsOf(base.Owner.Creature), c => c.IsAlive): Brimstone's), hittable or not:
            // the living enemies.
            if (_kind is ModelKind.Relic or ModelKind.Orb && LivingOpponents(value)) return HookTarget.LivingOpponents;
            // The player who played the card (cardPlay.Card.Owner.Creature).
            if (Unstored(value) is QueryValue { Name: "Creature", Of: QueryValue { Name: "Owner", Of: { } ownedCard } } && IsPlayCard(ownedCard)) return HookTarget.PlayOwner;
            if (value is QueryValue { Name: "HittableEnemies" }) return HookTarget.AllEnemies;
            if (value is QueryValue { Name: "NextItem", Arguments: [QueryValue { Name: "HittableEnemies" }] }) return HookTarget.RandomEnemy;
            if (value is QueryValue { Name: "Owner", Of: NamedValue { Name: "power" } }) return HookTarget.PowerOwner;
            // The creature added to the fight (AfterCreatureAddedToCombat's creature).
            if (value is ParameterValue { Name: "creature" } or NamedValue { Name: "creature" } && _hook == nameof(HookName.AfterCreatureAddedToCombat)) return HookTarget.Added;
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
        /// A power lands as a debuff at the amount read (PowerModel.GetTypeForAmount, its Type, StackType and AllowNegative read from the power's own
        /// code): a counter that can go below 0 given a constant below 0 is one, a debuff that can't is a buff then, and any other amount its Type.
        /// What a step is told as it runs is the type at the amount given then (SimModels.IsDebuffAt).
        /// </summary>
        private bool LandsAsDebuff(string power, HookValue amount)
        {
            var powerType = _type.Assembly.GetType("MegaCrit.Sts2.Core.Models.Powers." + power) ?? throw new Gap(ReaderCapability.ValueNotFollowed, $"gap: no power class {power}");
            var negative = amount is HookConstant { Value: < 0m };
            var allowNegative = Returned(powerType, "get_AllowNegative") is ConstantValue { Value: 1 };
            var counter = Returned(powerType, "get_StackType") is ConstantValue stack && EnumName(powerType.Assembly, "PowerStackType", stack.Value) == "Counter";
            var debuff = Returned(powerType, "get_Type") is ConstantValue type && EnumName(powerType.Assembly, "PowerType", type.Value) == "Debuff";
            if (negative && counter && allowNegative) return true;
            if (negative && !allowNegative && debuff) return false;
            return debuff;
        }

        /// <summary>The class's Type is PowerType.Debuff, and its StackType PowerStackType.Counter, read from their getters (its own or PowerModel's).</summary>
        public (bool Debuff, bool Counter) Kind() =>
            (Returned(_type, "get_Type") is ConstantValue type && EnumName(_type.Assembly, "PowerType", type.Value) == "Debuff",
             Returned(_type, "get_StackType") is ConstantValue stack && EnumName(_type.Assembly, "PowerStackType", stack.Value) == "Counter");

        /// <summary>
        /// The class's own PowerCmd rules (PowerRule): its AllowNegative, and its GetScaledAmountForMultiplayer's returned value (its own or
        /// PowerModel's) when its ShouldScaleInMultiplayer is true, else null. A value the reader can't follow throws (a Gap).
        /// </summary>
        public (bool AllowNegative, HookValue? Scaled) Rule()
        {
            var allowNegative = Returned(_type, "get_AllowNegative") is ConstantValue { Value: 1 };
            if (Returned(_type, "get_ShouldScaleInMultiplayer") is not ConstantValue { Value: not 0 }) return (allowNegative, null);
            var method = _type.GetMethod("GetScaledAmountForMultiplayer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new Gap(ReaderCapability.MultiplayerScaling, "gap: no GetScaledAmountForMultiplayer");
            _hook = method.Name;
            return (allowNegative, Pruned(Ways(ProgramReader.ReadReturnedWays(_type, method, _read))));
        }

        /// <summary>
        /// The class's IsVisibleInternal (PowerModel.IsVisible's own part, decision #331), read from its getter (its own or PowerModel's true):
        /// shown, hidden, or shown when you applied it (LocalContext.IsMe(base.Applier)); any other value isn't read.
        /// </summary>
        public PowerVisibility Visibility() => Returned(_type, "get_IsVisibleInternal") switch
        {
            ConstantValue { Value: 0 } => PowerVisibility.Hidden,
            ConstantValue => PowerVisibility.Shown,
            QueryValue { Name: "IsMe", Type: "LocalContext", Arguments: [QueryValue { Name: "Applier", Of: NamedValue { Name: "this" } }] } => PowerVisibility.ShownWhenYouApplied,
            _ => PowerVisibility.NotRead,
        };

        /// <summary>A temporary Strength or Dexterity power's IsPositive, read from its own class (an override, or its base class's true).</summary>
        private bool IsPositive(string power) =>
            _type.Assembly.GetType("MegaCrit.Sts2.Core.Models.Powers." + power) is not { } powerType || Returned(powerType, "get_IsPositive") is not ConstantValue { Value: 0 };

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
                // The run on the Dread ascension, tested (Ceremonial Beast's RitualAmount: IsDread() ? 2 : 0): decision #275.
                case QueryValue { Type: "AscensionHelper", Name: "IsDread", Arguments.Count: 0 }:
                    return new HookFactValue(HookFact.Dread);
                // A nullable tested for a value (switch (card.Pile?.Type) tests HasValue first): none where a way is null, one where it's made, way
                // by way.
                case QueryValue { Name: "HasValue", Of: { } nullable } when HasValueOf(Unstored(nullable)) is { } hasValue:
                    return Test(hasValue);
                // The costed card's pile tested as there (card.Pile?.Type: Free Attack's, Veilpiercer's): every card whose cost a simulation asks is in
                // one (your hand, or the play pile as it's played: SimEngine.HandCost).
                case QueryValue { Name: "Pile", Of: { } costedPileOf } when IsCostHook(_hook) && IsEventCard(Unstored(costedPileOf)):
                    return new HookConstant(1m);
                // A pick from the owner's hand tested as there, in the condition of the step it guards (FreeInHandStep: the step acts on what the
                // hand holds).
                case QueryValue { Name: "NextItem", Arguments: [var picked] } when _handPickHolds && FromOwnersHand(picked):
                    return new HookConstant(1m);
                // Its kept card tested as there (MusicBox's CardBeingPlayed): one is kept, whichever card play's.
                case var kept when KeptCard(kept) is { } keptField:
                    return new HookMath(HookOperation.NotEqual, new HookField(keptField), new HookConstant(0m));
                // A card play its code keeps, tested as there (Stretch's data's cardPlay != null): one is kept, whichever.
                case var keptPlay when KeptPlay(keptPlay) is { } playField:
                    return new HookMath(HookOperation.NotEqual, new HookField(playField), new HookConstant(0m));
                // The card play the hook is given, tested as there (AfterModifyingBlockAmount's cardPlay: a block from a card play).
                case ParameterValue { Name: "cardPlay" } or NamedValue { Name: "cardPlay" } when _hook == nameof(HookName.AfterModifyingBlockAmount):
                    return new HookMath(HookOperation.NotEqual, new HookFactValue(HookFact.PlaySerial), new HookConstant(0m));
                // A card's own block (card.GainsBlock), its enchantment there or its enchantment's block (card.Enchantment?.GainsBlock: Stretch's GainsBlock), for
                // the card played or the damage's or block's card.
                case QueryValue { Name: "GainsBlock", Of: { } gainsOf } when CardNamed(gainsOf) is { } gainer:
                    return new HookFactValue(HookFact.CardGainsBlock) { Of = CardOf(gainer) };
                case QueryValue { Name: "GainsBlock", Of: { } enchantmentGains } when Unstored(enchantmentGains) is QueryValue { Name: "Enchantment", Of: { } enchantedOf } && CardNamed(enchantedOf) is { } enchanted:
                    return new HookFactValue(HookFact.EnchantmentGainsBlock) { Of = CardOf(enchanted) };
                case QueryValue { Name: "Enchantment", Of: { } enchantmentOf } when CardNamed(enchantmentOf) is { } hasEnchantment:
                    return new HookFactValue(HookFact.CardEnchanted) { Of = CardOf(hasEnchantment) };
                // A static method of its own class giving a true-or-false (Stretch's GainsBlock(card)): its body's returned value, its parameters the arguments.
                case QueryValue { Of: null, Name: var ownStatic, Arguments: var ownArguments, Type: var ownClass } when ownClass == _type.Name && OwnStaticTest(ownStatic, ownArguments) is { } returned:
                    return Test(returned);
                // One of its own true-or-false methods (Doom's ShouldDoomTrigger(participants)): its body's returned value, its parameters the arguments.
                case QueryValue { Of: NamedValue { Name: "this" }, IsProperty: false, Name: var ownMethod, Arguments: var methodArguments, Type: var methodClass } when methodClass == _type.Name
                    && OwnStaticTest(ownMethod, methodArguments, isStatic: false) is { } ownReturned:
                    return Test(ownReturned);
                // The fight is over or ending (CombatManager.Instance.IsOverOrEnding: Doom's turn-end kill waits on it not being).
                case QueryValue { Type: "CombatManager", Name: "IsOverOrEnding" }:
                    return new HookFactValue(HookFact.FightEnding);
                // A list of creatures the code works out has any, or any that pass a filter (GetDoomedCreatures(side).Count, Count(creatures, c => ...),
                // tested as true or false).
                case var countedList when CountOfList(countedList) is { } counted:
                    return new HookMath(HookOperation.NotEqual, counted, new HookConstant(0m));
                // A power's own question asked of it on a creature (c.GetPower<DoomPower>().IsOwnerDoomed(): its owner's HP at most its amount), read from
                // that power's code for that creature (ModelSpec.Tests).
                case QueryValue { IsProperty: false, Name: var question, Arguments.Count: 0, Of: { } asked } when Getter(asked) is { Relic: false } got && IsPowerQuestion(got.Model, question):
                    return CreatureFact(HookFact.CreaturePowerTest, got.Holder) with { Of = got.Model + "." + question };
                // Every power on a creature answers that its death sets off Fatal (c.Powers.All(p => p.ShouldOwnerDeathTriggerFatal()): Book Repair Knife's).
                case QueryValue { Type: "Enumerable", Name: "All", Arguments: [QueryValue { Name: "Powers", Of: { } powersOf }, LambdaValue fatal] }
                    when CreatureOf(powersOf) is { } dying && LambdaReads(fatal, "ShouldOwnerDeathTriggerFatal"):
                    return CreatureFact(HookFact.CreatureDeathSetsOffFatal, dying);
                // A creature's monster is a class (command.Attacker?.Monster is Osty, way by way for a null-conditional chain): the pet the simulator
                // keeps for a player when it's the class Player.Osty gets (read from its code); any other class is a gap.
                case QueryValue { Name: "is", Of: { } monsterOf, Type: var monsterClass } when Unstored(monsterOf) is WaysValue or QueryValue { Name: "Monster" }:
                {
                    // Any other monster class of a creature the code names (base.Owner.Monster is BowlbugRock: Imbalanced's; AfterBlockBroken's target.Monster
                    // is Tunneler: Burrowed's): the enemy's monster (SimEnemy.Monster).
                    HookValue MonsterIs(ProgramValue monster) => monster is NullValue ? new HookConstant(0m)
                        : Unstored(monster) is QueryValue { Name: "Monster", Of: { } creatureOf } && CreatureOf(creatureOf) is { } creature && monsterClass == OstyClass()
                            ? CreatureFact(HookFact.CreatureIsPet, creature)
                            : Unstored(monster) is QueryValue { Name: "Monster", Of: { } namedOf } && CreatureOf(namedOf) is { PetOwner: false } named && IsMonsterClass(monsterClass)
                                ? CreatureFact(HookFact.CreatureMonsterIs, named) with { Of = monsterClass }
                                : throw new Gap(ReaderCapability.Osty, $"gap: tests {monster} is {monsterClass}");
                    return Unstored(monsterOf) is WaysValue monsterWays
                        ? new HookWays(monsterWays.Ways.Select(way => new HookWay(Condition(way.When) ?? new HookConstant(1m), MonsterIs(way.Value))).ToList())
                        : MonsterIs(monsterOf);
                }
                // A creature's pet owner tested as there (osty.Creature.PetOwner != null: Sic 'Em's): the creature is a player's pet.
                case QueryValue { Name: "PetOwner" } petOwner when CreatureOf(petOwner) is { PetOwner: true } owner:
                    return CreatureFact(HookFact.CreatureThere, owner);
                // An attack its data keeps, tested as there (commandToModify as a truth): one is bound to it (decision #228).
                case var keptAttack when KeptAttack(keptAttack) is { } keptField:
                    return new HookMath(HookOperation.NotEqual, new HookField(keptField), new HookConstant(0m));
                // The attack is a card's (command.ModelSource is CardModel), or has a model as its source at all (command.ModelSource): the simulator's
                // attacks are its plays', each a card's.
                case var attackSource when IsAttackCard(attackSource):
                    return new HookFactValue(HookFact.AttackFromCard);
                case CompareValue compare:
                    return Compare(compare);
                // An enchantment's status tested as true or false (Normal is 0: Swift's and Sown's status == Normal): its state, not 0.
                case QueryValue { Name: "Status", Of: NamedValue { Name: "this" }, Type: "EnchantmentModel" } when _kind == ModelKind.Enchantment:
                    return new HookMath(HookOperation.NotEqual, new HookField("Status"), new HookConstant(0m));
                // A question to the fight's log tested: there's any (Any), or its count isn't 0.
                case QueryValue { Name: "Count" or "Any" } logged when ProgramReader.ReadsHistory(logged):
                    return logged.Name == "Any" ? History(logged) : new HookMath(HookOperation.NotEqual, History(logged), new HookConstant(0m));
                // The owner's hand is empty (GetPile(Hand, owner).IsEmpty), or has cards (its Count tested as true or false).
                case QueryValue { Name: "IsEmpty", Of: { } emptyPile } when IsOwnersHand(emptyPile):
                    return new HookMath(HookOperation.Equal, new HookFactValue(HookFact.OwnerHandCount), new HookConstant(0m));
                case QueryValue { Name: "Count", Of: { } countedCards } when IsOwnersHandCards(countedCards):
                    return new HookMath(HookOperation.NotEqual, new HookFactValue(HookFact.OwnerHandCount), new HookConstant(0m));
                // The owner holds potions (base.Owner.Potions.Any(): Belt Buckle's, decision #313).
                case QueryValue { Type: "Enumerable", Name: "Any", Arguments: [var potions] } when IsOwnersPotions(potions):
                    return new HookMath(HookOperation.NotEqual, new HookFactValue(HookFact.OwnerPotionCount), new HookConstant(0m));
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
                case StoredValue { Value: QueryValue { Name: "NextItem", Arguments: [var rolledAmong] } } when IsHittableEnemies(rolledAmong):
                    return new HookConstant(1m);
                case QueryValue { Name: "NextItem", Arguments: [var rolledAmongThem] } when IsHittableEnemies(rolledAmongThem):
                    return new HookConstant(1m);
                // A value the code kept in a local and tests (int count = ...; if (count != 0)): the value it kept.
                case StoredValue storedTest:
                    return Test(storedTest.Value);
                case QueryValue { Name: "Contains", Arguments: [QueryValue { Name: "Tags", Of: { } tagsOf }, ConstantValue tag] } when IsPlayCard(tagsOf):
                    return EnumName(_type.Assembly, "CardTag", tag.Value) is { } tagName && Enum.TryParse<SimCardTag>(tagName, out _)
                        ? new HookFactValue(HookFact.CardTag) { Of = tagName } : throw new Gap(ReaderCapability.CardFacts, $"gap: tests the card's tag {tag.Value}");
                // A card drawn in a simulation is never the turn's hand draw.
                // The card exhausted is an Ethereal one its owner's turn end exhausts (AfterCardExhausted's causedByEthereal).
                case ParameterValue { Name: "causedByEthereal" } or NamedValue { Name: "causedByEthereal" } when _hook == "AfterCardExhausted":
                    return new HookFactValue(HookFact.CausedByEthereal);
                case NamedValue { Name: "fromHandDraw" }:
                    return new HookConstant(0m);
                case ParameterValue { Name: "cardSource" } or NamedValue { Name: "cardSource" }:
                    return new HookFactValue(HookFact.FromCard);
                // The card a damage comes from is in a pile (cardSource.Pile, tested as there): every card a simulation's damage comes from is (decision #231).
                case var sourcePile when IsSourcePile(sourcePile):
                    return new HookFactValue(HookFact.FromCard);
                // The card play a Modify hook is given (cardPlay, tested as there): the damage's or block's play.
                case ParameterValue { Name: "cardPlay" } or NamedValue { Name: "cardPlay" } when DamageHooks.Contains(_hook):
                    return new HookFactValue(HookFact.FromPlay);
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
                // A constant tested as true or false (a ?? false's false: Stretch's GainsBlock).
                case ConstantValue constantTest:
                    return new HookConstant(constantTest.Value != 0 ? 1m : 0m);
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
                // Its owner's or its own fight is there (base.Owner.Creature.CombatState != null), or a creature's it names (DoomKill's creature.CombatState
                // != null): in a simulation, always.
                case QueryValue { Name: "CombatState", Of: var stateOf } when stateOf is NamedValue { Name: "this" } || stateOf != null && (IsOwner(stateOf) || CreatureOf(stateOf) != null):
                    return new HookConstant(1m);
                // It's on its owner (base.Owner.Powers.Contains(this): Intercept's, Covered's): a power runs its hooks only while it is.
                case QueryValue { Name: "Contains", Arguments: [QueryValue { Name: "Powers", Of: { } powersOf }, NamedValue { Name: "this" }] } when _kind == ModelKind.Power && IsOwner(powersOf):
                    return new HookConstant(1m);
                // The hittable enemies tested as there (a ?? on CombatState?.HittableEnemies: Serpent Form's): a list, in a fight always.
                case QueryValue { Name: "HittableEnemies" }:
                    return new HookConstant(1m);
                case QueryValue { Type: "Creature", Name: "IsEnemy", Of: QueryValue { Name: "Owner", Of: NamedValue { Name: "power" } } }:
                    return new HookFactValue(HookFact.PowerOnEnemy);
                case QueryValue query when IsTakingPart(query):
                    return new HookFactValue(HookFact.TakesPart);
                case QueryValue { Name: "Contains", Arguments: [QueryValue { Name: "Pets" } pets, var dealer] } when pets.Of is { } petsOf && IsOwner(petsOf) && IsDealer(dealer):
                    return new HookFactValue(HookFact.DealerIsOwnersPet);
                case QueryValue { Name: "IsPoweredAttack", Arguments: [var props] } when IsProps(props):
                    return new HookFactValue(HookFact.PoweredAttack);
                // A flag of the props (props.HasFlag(ValueProp.Unpowered)): the simulator's props keep the game's flags, read by the flag's name.
                case QueryValue { Name: "HasFlag", Of: { } flagsOf, Arguments: [ConstantValue flag] } when IsProps(flagsOf):
                    return EnumName(_type.Assembly, "ValueProp", flag.Value) is { } flagName && Enum.TryParse<SimProps>(flagName, out var simFlag) && simFlag != SimProps.None
                        && (int)simFlag == (int)flag.Value
                        ? new HookFactValue(HookFact.PropsHave) { Of = flagName } : throw new Gap(ReaderCapability.DamageProps, $"gap: tests the props' flag {flag.Value}");
                case QueryValue { Name: "IsPoweredCardOrMonsterMoveBlock", Arguments: [var props] } when IsProps(props):
                    return new HookFactValue(HookFact.PoweredBlock);
                case QueryValue { Name: "IsCardOrMonsterMove", Arguments: [var props] } when IsProps(props):
                    return new HookFactValue(HookFact.CardOrMonsterMove);
                // The HP a hit took past block tested as true or false (result.UnblockedDamage != 0: Flutter's, Slumber's).
                case QueryValue { Name: "UnblockedDamage", Of: ParameterValue { Name: "result" } or NamedValue { Name: "result" } } when _hook is "AfterDamageGiven" or "AfterDamageReceived":
                    return new HookMath(HookOperation.NotEqual, new HookFactValue(HookFact.HitUnblocked), new HookConstant(0m));
                // The hit was fully blocked (result.WasFullyBlocked: Imbalanced's).
                case QueryValue { Name: "WasFullyBlocked", Of: ParameterValue { Name: "result" } or NamedValue { Name: "result" } } when _hook is "AfterDamageGiven" or "AfterDamageReceived":
                    return new HookFactValue(HookFact.HitFullyBlocked);
                // The play is its card play's last (cardPlay.IsLastInSeries: Black Hole's).
                case QueryValue { Name: "IsLastInSeries", Of: NamedValue { Name: "cardPlay" } or ParameterValue { Name: "cardPlay" } } when IsPlayHook(_hook):
                    return new HookFactValue(HookFact.PlayIsLast);
                // The card played can still be upgraded (Razor Tooth's cardPlay.Card.IsUpgradable).
                case QueryValue { Name: "IsUpgradable", Of: { } upgradableOf } when IsPlayCard(upgradableOf):
                    return new HookFactValue(HookFact.CardUpgradable);
                // The card played's affliction is a class (Galvanic's cardPlay.Card.Affliction is Galvanized), or the costed card's (Tangled's card.Affliction is
                // Entangled), by the simulator's name for it.
                case QueryValue { Name: "is", Of: QueryValue { Name: "Affliction", Of: { } afflictedOf }, Type: var afflictionClass } when IsPlayCard(afflictedOf) || IsCostHook(_hook) && IsEventCard(afflictedOf):
                    return Enum.TryParse<SimAffliction>(afflictionClass, out var affliction) && affliction is not (SimAffliction.None or SimAffliction.Other)
                        ? new HookFactValue(HookFact.CardAffliction) { Of = afflictionClass } : throw new Gap(ReaderCapability.CardFacts, $"gap: tests the card's affliction {afflictionClass}");
                case QueryValue { Name: "Contains", Of: QueryValue { Name: "Keywords", Of: { } keywordsOf }, Arguments: [ConstantValue keyword] } when IsPlayCard(keywordsOf) || IsEventCard(keywordsOf):
                    return EnumName(_type.Assembly, "CardKeyword", keyword.Value) == "Ethereal" ? new HookFactValue(HookFact.CardEthereal) : throw new Gap(ReaderCapability.CardFacts, $"gap: tests the card's keyword {keyword.Value}");
                case QueryValue { Name: "is", Of: NamedValue { Name: "power" or "canonicalPower" } or ParameterValue { Name: "power" or "canonicalPower" }, Type: var powerClass }:
                    return PowerIs(powerClass);
                // The power landing is shown (PowerModel.IsVisible: Artifact's canonicalPower.IsVisible): its class's IsVisibleInternal, read into its
                // PowerRule (decision #331), unless its Target is a player other than you.
                case QueryValue { Name: "IsVisible", IsProperty: true, Of: { } visibleOf } when IsPowerTold(visibleOf) && _hook == nameof(HookName.TryModifyPowerAmountReceived):
                    return new HookFactValue(HookFact.PowerVisible);
                case QueryValue { Name: "is", Of: { } cardOf, Type: var cardClass } when IsEventCard(cardOf) || IsPlayCard(cardOf):
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
                // The Impaled AfterArrowImpaled is given has an arrow: the shot's, added before the hook (ArrowCmd.ImpaleTarget).
                // A shot's card tested as there (AfterArrowShot's source: a card made the shot).
                case ParameterValue { Name: "source" } or NamedValue { Name: "source" } when _hook == "AfterArrowShot":
                    return new HookFactValue(HookFact.HookCardThere);
                // The damage's card is the play its model bound (its own data's ContainsKey(cardSource): Rupture's, the card in play its BeforeCardPlayed added).
                case QueryValue { Name: "ContainsKey", Of: { } boundOf, Arguments: [var boundKey] } when Unstored(boundOf) is QueryValue { Of: { } dataOf } && ProgramReader.IsOwnData(dataOf)
                    && IsSourceCard(Unstored(boundKey)) && DamageHooks.Contains(_hook):
                    return new HookFactValue(HookFact.SourceBound);
                case var firstArrow when IsImpaledsFirstArrow(firstArrow):
                    return new HookConstant(1m);
                case var creatureTest when CreatureTest(creatureTest) is { } creatureFact:
                    return creatureFact;
                // An orb's Passive's target: TriggerPassive passes none (its trigger hook's code: CallsTriggerPassive).
                case ParameterValue { Name: "target" } or NamedValue { Name: "target" } when _kind == ModelKind.Orb && _hook == "Passive":
                    return new HookConstant(0m);
                // The enemies that can be hit are there (HittableEnemies.Count, a list of them kept: its Count).
                case QueryValue { Name: "Count", Of: { } hittable } when IsHittableEnemies(hittable):
                    return new HookMath(HookOperation.NotEqual, new HookFactValue(HookFact.HittableEnemyCount), new HookConstant(0m));
                // The owner has orbs (OrbQueue.Orbs.Count tested as a true-or-false: Consuming Shadow's).
                case QueryValue { Name: "Count", Of: { } ownersOrbs } when IsOwnersOrbs(ownersOrbs):
                    return new HookMath(HookOperation.NotEqual, new HookFactValue(HookFact.OwnerOrbCount), new HookConstant(0m));
                // The orb a hook is given is of a class (orb is LightningOrb).
                case QueryValue { Name: "is", Of: { } orbOf, Type: var orbClass } when IsHookOrb(orbOf):
                    return OrbKindOf(orbClass) is { } orbKind ? new HookFactValue(HookFact.OrbIs) { Of = orbKind.ToString() } : throw new Gap(ReaderCapability.Orbs, $"gap: tests the orb is {orbClass}");
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
            // A player's combat state is there (x.Player?.PlayerCombatState: Insatiable Dominion's turn number): every player has one in a fight
            // (Player.PlayerCombatState, made as the fight starts), so it's the creature being a player.
            if (test is QueryValue { Name: "PlayerCombatState", Of: QueryValue { Name: "Player" } player } && CreatureOf(player) is { } fighter)
                return CreatureFact(HookFact.CreatureIsPlayer, fighter);
            if (test is not QueryValue { Of: { } of } query || CreatureOf(of) is not { } creature) return null;
            switch (query.Name)
            {
                case "IsPlayer": return CreatureFact(HookFact.CreatureIsPlayer, creature);
                case "IsMonster": return CreatureFact(HookFact.CreatureIsMonster, creature);
                case "IsAlive": return CreatureFact(HookFact.CreatureAlive, creature);
                case "IsDead": return Not(CreatureFact(HookFact.CreatureAlive, creature));
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
            // A foreach's item (each: a player, an enemy).
            if (value is NamedValue { Name: "each" }) return new HookCreature(HookWho.Each);
            if (IsDead(value)) return new HookCreature(HookWho.Dead);
            if (IsDealer(value) && DamageHooks.Contains(_hook)) return new HookCreature(HookWho.Dealer);
            // ModifyPreBlockDamageTarget(originalTarget, target, ...): the damage's target, and the redirect so far.
            if (_hook == nameof(HookName.ModifyPreBlockDamageTarget) && value is ParameterValue { Name: "originalTarget" } or NamedValue { Name: "originalTarget" }) return new HookCreature(HookWho.Target);
            if (_hook == nameof(HookName.ModifyPreBlockDamageTarget) && value is ParameterValue { Name: "target" } or NamedValue { Name: "target" }) return new HookCreature(HookWho.RedirectSoFar);
            if (value is ParameterValue { Name: "target" } or NamedValue { Name: "target" } && DamageHooks.Contains(_hook)) return new HookCreature(HookWho.Target);
            // AfterBlockBroken(target, breaker): the creature whose block broke, and who broke it.
            if (_hook == nameof(HookName.AfterBlockBroken) && value is ParameterValue { Name: "target" } or NamedValue { Name: "target" }) return new HookCreature(HookWho.Target);
            if (_hook == nameof(HookName.AfterBlockBroken) && value is ParameterValue { Name: "breaker" } or NamedValue { Name: "breaker" }) return new HookCreature(HookWho.Breaker);
            // The creature a Should hook asks about.
            if (value is ParameterValue { Name: "creature" } or NamedValue { Name: "creature" }
                && _hook is nameof(HookName.ShouldAllowHitting) or nameof(HookName.ShouldCreatureBeRemovedFromCombatAfterDeath) or nameof(HookName.ShouldClearBlock) or nameof(HookName.AfterCreatureAddedToCombat))
                return new HookCreature(HookWho.Asked);
            if (IsApplier(value)) return new HookCreature(HookWho.Applier);
            if (value is QueryValue { Name: "Owner", Of: NamedValue { Name: "power" } } && _hook == "AfterPowerAmountChanged") return new HookCreature(HookWho.PowerOwner);
            // A creature's monster cast to its class, then that monster's creature (dealer?.Monster is Osty osty, then osty.Creature: Sic 'Em's): the
            // creature itself, where the cast's own test holds.
            if (value is QueryValue { Name: "Creature", Of: { } castValue } && Unstored(castValue) is QueryValue { Name: "is", Of: { } castOf } && CastCreature(castOf) is { } cast) return cast;
            if (value is QueryValue { Name: "Player" or "Creature", Of: { } of }) return CreatureOf(of);
            if (value is QueryValue { Name: "PetOwner", Of: { } pet } && CreatureOf(pet) is { PetOwner: false } owned) return owned with { PetOwner = true };
            // The attack's attacker (command.Attacker), and its card's owner (cardModel.Owner, the player whose card it is).
            if (value is QueryValue { Name: "Attacker", Of: { } attackOf } && IsAttack(attackOf)) return new HookCreature(HookWho.Attacker);
            if (value is QueryValue { Name: "Owner", Of: { } attackCardOf } && IsAttackCard(attackCardOf)) return new HookCreature(HookWho.AttackCardOwner);
            // The owner of a model got from a creature (a called model's base.Owner): that creature.
            if (value is QueryValue { Name: "Owner", Of: { } ownerOf } && Getter(ownerOf) is { } got) return got.Holder;
            return null;
        }

        /// <summary>
        /// The creature whose monster a cast is of (creature.Monster, or a null-conditional's ways: null where the creature isn't there, its Monster
        /// where it is); null for anything else.
        /// </summary>
        private HookCreature? CastCreature(ProgramValue castOf)
        {
            var unstored = Unstored(castOf);
            if (unstored is WaysValue ways)
            {
                var named = ways.Ways.Where(way => way.Value is not NullValue).Select(way => Unstored(way.Value)).ToList();
                return named is [QueryValue { Name: "Monster", Of: { } wayOf }] ? CreatureOf(wayOf) : null;
            }
            return unstored is QueryValue { Name: "Monster", Of: { } creatureOf } ? CreatureOf(creatureOf) : null;
        }

        /// <summary>The hooks given a damage's or block's dealer and target (the Modify hooks, a hit's reactions).</summary>
        private static readonly HashSet<string> DamageHooks = new(StringComparer.Ordinal)
        {
            "ModifyDamageAdditive", "ModifyDamageMultiplicative", "ModifyDamageMultiplicativeLate", "ModifyBlockAdditive", "ModifyBlockMultiplicative", "AfterDamageGiven", "AfterDamageReceived",
            "BeforeDamageReceived", "ModifyPreBlockDamageTarget", "ModifyUnblockedDamageTarget",
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
                // The side whose turn ends or starts (AfterSideTurnEnd's side, AfterSideTurnStart's): the players' as your turn ends or starts, the
                // enemies' as theirs does, a fact of the turn's end or start it runs at (decision #306: the side whose turn it is now).
                else if (compare.Right is ParameterValue { Name: "side" } or NamedValue { Name: "side" } && (EndsATurn(_hook) || StartsATurn(_hook)))
                    holds = CreatureFact(HookFact.CreatureOnEndingSide, leftSide);
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
            // The first creature of a list the code works out against a creature it names (GetDoomedCreatures(base.Owner.Side).First() != base.Owner:
            // Doom's first doomed of its side), as creature ids.
            if (compare.Op is Comparison.Equal or Comparison.NotEqual)
            {
                if (FirstOfList(compare.Left) is { } firstLeft && CreatureOf(compare.Right) is { } rightCreature)
                    return new HookMath(OperationOf(compare.Op), firstLeft, CreatureFact(HookFact.CreatureId, rightCreature));
                if (FirstOfList(compare.Right) is { } firstRight && CreatureOf(compare.Left) is { } leftCreature)
                    return new HookMath(OperationOf(compare.Op), CreatureFact(HookFact.CreatureId, leftCreature), firstRight);
            }
            if (AttackCompare(compare) is { } attacks) return attacks;
            if (SourceCompare(compare) is { } sources) return sources;
            if (CostedPileCompare(compare) is { } costedPile) return costedPile;
            if (FirstPlayCompare(compare) is { } firstPlay) return firstPlay;
            // The stars a gain gave against none (AfterStarsGained's amount > 0: Black Hole's): the simulator raises a stars-gained event for a
            // PlayerCmd.GainStars alone, and every one in the game gains a Stars value or a power's amount, at least 1 (StarsGainTests holds it to the
            // code). Any other comparison of it is a gap: the simulator keeps no count of stars.
            if (_hook == "AfterStarsGained" && (compare.Left, compare.Right) is (NamedValue { Name: "amount" } or ParameterValue { Name: "amount" }, ConstantValue { Value: 0 }))
                return new HookMath(OperationOf(compare.Op), new HookConstant(1m), new HookConstant(0m));
            if (_hook == "AfterStarsGained" && (compare.Left is NamedValue { Name: "amount" } or ParameterValue { Name: "amount" } || compare.Right is NamedValue { Name: "amount" } or ParameterValue { Name: "amount" }))
                throw new Gap(ReaderCapability.HookAmount, $"gap: compares the stars gained {compare}");
            if (ArrowCompare(compare) is { } arrows) return arrows;
            if (compare.Op is Comparison.Equal or Comparison.NotEqual)
            {
                // A card play its code keeps against none, or against the card play the hook is given (Stretch's cardPlay != internalData.cardPlay): its
                // identity against 0, or against that play's (decision #269).
                var (keptPlay, otherPlay) = KeptPlay(compare.Left) is { } leftPlay ? (leftPlay, compare.Right) : KeptPlay(compare.Right) is { } rightPlay ? (rightPlay, compare.Left) : (null, compare.Left);
                if (keptPlay != null && (otherPlay is NullValue || IsHookPlay(otherPlay) && GivenAPlay.Contains(_hook!)))
                    return new HookMath(OperationOf(compare.Op), new HookField(keptPlay), otherPlay is NullValue ? new HookConstant(0m) : new HookFactValue(HookFact.PlaySerial));
                // The player of a card play it keeps against the player whose card a damage or block comes from (Stretch's cardPlay.Player != cardSource.Owner).
                var (keptPlayer, otherPlayer) = KeptPlayPlayer(compare.Left) is { } leftPlayer ? (leftPlayer, compare.Right) : KeptPlayPlayer(compare.Right) is { } rightPlayer ? (rightPlayer, compare.Left) : (null, compare.Left);
                if (keptPlayer != null && CardOwnerOf(Unstored(otherPlayer)) is { } ownedCard && IsSourceCard(Unstored(ownedCard)))
                    return new HookMath(OperationOf(compare.Op), new HookField(keptPlayer), CreatureFact(HookFact.CreatureId, new HookCreature(HookWho.SourceCardOwner)));
                // Its kept card against none (CardBeingPlayed != null: any card play's), or against the card played or a damage's or block's card
                // (cardPlay.Card == CardBeingPlayed, cardSource == AttackToDouble): one is kept and it's that card play's (decision #272; a card played
                // inside it is another card play: decision #267).
                var (keptCard, otherCard) = KeptCard(compare.Left) is { } keptOnTheLeft ? (keptOnTheLeft, compare.Right) : KeptCard(compare.Right) is { } keptOnTheRight ? (keptOnTheRight, compare.Left) : (null, compare.Left);
                if (keptCard != null && (otherCard is NullValue || IsCardOfThePlay(otherCard)))
                {
                    HookValue same = otherCard is NullValue ? Not(new HookMath(HookOperation.NotEqual, new HookField(keptCard), new HookConstant(0m)))
                        : new HookMath(HookOperation.And, new HookMath(HookOperation.NotEqual, new HookField(keptCard), new HookConstant(0m)),
                            new HookMath(HookOperation.Equal, new HookField(keptCard), new HookFactValue(PlayOfTheCard(otherCard)!.Value)));
                    return compare.Op == Comparison.Equal ? same : Not(same);
                }
                // The orb a hook is given is its owner's (orb.Owner == base.Owner, base.Owner.Player).
                var orbOwner = IsOrbOwner(compare.Left) && IsOwner(compare.Right) || IsOrbOwner(compare.Right) && IsOwner(compare.Left);
                // It's its owner's front orb (orb == base.Owner.PlayerCombatState.OrbQueue.Orbs[0]).
                var front = IsHookOrb(compare.Left) && IsOwnersFrontOrb(compare.Right) || IsHookOrb(compare.Right) && IsOwnersFrontOrb(compare.Left);
                if (orbOwner || front)
                {
                    var orbFact = new HookFactValue(orbOwner ? HookFact.OrbIsOwners : HookFact.OrbIsFront);
                    return compare.Op == Comparison.Equal ? orbFact : Not(orbFact);
                }
            }
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
            // The card played is the enchantment's own card (cardPlay.Card == base.Card).
            if (compare.Op is Comparison.Equal or Comparison.NotEqual && _kind == ModelKind.Enchantment
                && (IsPlayCard(compare.Left) && IsModelCard(compare.Right) || IsPlayCard(compare.Right) && IsModelCard(compare.Left)))
                return compare.Op == Comparison.Equal ? new HookFactValue(HookFact.PlayIsModelCard) : Not(new HookFactValue(HookFact.PlayIsModelCard));
            // Whom the enchantment's card is aimed at against a target type named (base.Card.TargetType != TargetType.AllEnemies): SimPlay.Target's
            // number for the game's, a type the simulator doesn't tell apart a gap.
            if (_kind == ModelKind.Enchantment && IsModelCardTargets(compare.Left) && compare.Right is ConstantValue targetType)
            {
                var simTarget = EnumName(_type.Assembly, "TargetType", targetType.Value) switch
                {
                    "AnyEnemy" => SimPlay.Target.Single,
                    "AllEnemies" => SimPlay.Target.All,
                    "RandomEnemy" => SimPlay.Target.Random,
                    var other => throw new Gap(ReaderCapability.CardFacts, $"gap: a target type {other}"),
                };
                return new HookMath(OperationOf(compare.Op), new HookFactValue(HookFact.ModelCardTargets), new HookConstant((int)simTarget));
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
            // The compiler's range test of a card's type ((uint)(type - 1) > 1u: neither an Attack nor a Skill): the type is one of the game's values
            // from the one subtracted to it plus the bound (<=), or none of them (>).
            if (compare is { Unsigned: true, Op: Comparison.Greater or Comparison.LessOrEqual, Left: ArithmeticValue { Op: "sub", Left: var rangedType, Right: ConstantValue { Value: var low } }, Right: ConstantValue { Value: var span } }
                && Unstored(rangedType) is QueryValue { Type: "CardModel", Name: "Type", Of: { } rangedOf } && (IsPlayCard(rangedOf) || IsEventCard(rangedOf)) && span is >= 0 and < 8)
            {
                HookValue? any = null;
                for (var value = low; value <= low + span; value++)
                {
                    var simType = EnumName(_type.Assembly, "CardType", value) is { } typeName && Enum.TryParse<SimCardType>(typeName, out var parsed) ? parsed
                        : throw new Gap(ReaderCapability.CardFacts, $"gap: a card type {value}");
                    var isIt = new HookMath(HookOperation.Equal, new HookFactValue(HookFact.CardType), new HookConstant((int)simType));
                    any = any == null ? isIt : new HookMath(HookOperation.Or, any, isIt);
                }
                return compare.Op == Comparison.LessOrEqual ? any! : Not(any!);
            }
            if (compare.Left is QueryValue { Type: "CardModel", Name: "Type", Of: { } typeOf } && (IsPlayCard(typeOf) || IsEventCard(typeOf) || IsAttackCard(typeOf)) && compare.Right is ConstantValue cardType)
            {
                var simType = EnumName(_type.Assembly, "CardType", cardType.Value) is { } typeName && Enum.TryParse<SimCardType>(typeName, out var parsed) ? parsed
                    : throw new Gap(ReaderCapability.CardFacts, $"gap: a card type {cardType.Value}");
                return new HookMath(OperationOf(compare.Op), new HookFactValue(HookFact.CardType), new HookConstant((int)simType));
            }
            if (compare.Left is QueryValue { Name: "GetTypeForAmount", Of: { } typedPower, Arguments: var typeArguments } && IsPowerTold(typedPower) && compare.Right is ConstantValue powerType
                && compare.Op is Comparison.Equal or Comparison.NotEqual)
            {
                // In the received pass, at the amount so far (Artifact's canonicalPower.GetTypeForAmount(amount)): the power's type at that amount,
                // from its class's Type, StackType and AllowNegative (SimModels.IsDebuffAt). Elsewhere, the power as it landed (power: Sleight of Flesh's).
                HookValue isDebuff = _hook == nameof(HookName.TryModifyPowerAmountReceived)
                    ? typeArguments is [var amountArgument] && Value(amountArgument) is HookFactValue { Fact: HookFact.PowerAmount }
                        ? new HookFactValue(HookFact.PowerDebuffAtAmount)
                        : throw new Gap(ReaderCapability.ValueNotFollowed, "gap: the power's type at an amount other than the one landing")
                    : typedPower is NamedValue { Name: "power" } ? new HookFactValue(HookFact.PowerIsDebuff)
                    : throw new Gap(ReaderCapability.ValueNotFollowed, "gap: the type of a power other than the one landing");
                var debuff = EnumName(_type.Assembly, "PowerType", powerType.Value) switch
                {
                    "Debuff" => isDebuff,
                    "Buff" => Not(isDebuff),
                    var other => throw new Gap(ReaderCapability.ValueNotFollowed, $"gap: a power type {other}"),
                };
                return compare.Op == Comparison.Equal ? debuff : Not(debuff);
            }
            // The side whose turn is ending against a side named (Weak's side == CombatSide.Enemy): the side whose turn it is (CombatManager passes
            // CurrentSide), the enemies' as their turn ends, a fact of the turn's end it runs at; the side whose turn is starting, a fact of the turn's
            // start it runs at (the enemies', or the players' next turn in a projection: decision #306).
            if (compare.Left is NamedValue { Name: "side" } or ParameterValue { Name: "side" } && compare.Right is ConstantValue side)
            {
                var named = EnumName(_type.Assembly, "CombatSide", side.Value);
                if ((EndsATurn(_hook) || StartsATurn(_hook)) && named is "Player" or "Enemy")
                {
                    var enemies = new HookFactValue(HookFact.EnemiesTurn);
                    var isSide = named == "Enemy" ? (HookValue)enemies : Not(enemies);
                    return compare.Op == Comparison.Equal ? isSide : Not(isSide);
                }
                var players = named == "Player"; // a side named on another hook: the players' side whose turn it is during the play
                return new HookConstant((compare.Op == Comparison.Equal) == players ? 1m : 0m);
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
            if (CardOwnerOf(other) is { } card && !IsAttackCard(card))
                return IsPlayCard(card) ? new HookFactValue(HookFact.PlayIsOwners)
                    : IsEventCard(card) ? new HookFactValue(HookFact.EventIsOwners)
                    : IsSourceCard(card) ? new HookFactValue(HookFact.CardIsOwners)
                    : throw new Gap(ReaderCapabilities.OfValue(other, otherwise: ReaderCapability.CreatureSide), $"gap: compares the owner with {other}");
            if (IsDead(other)) return new HookFactValue(HookFact.DeadIsOwner);
            if (IsCreator(other)) return new HookFactValue(HookFact.EventIsOwners);
            // The owner of the arrow knocked loose (AfterArrowDislodged's arrow.owner, a player, or its Creature): the event's player, the one the
            // engine raises it for (the arrow comes back to its owner: ArrowCmd.DislodgeArrow).
            if (_hook is "AfterArrowDislodged" or "AfterArrowImpaled" && IsArrowOwner(other)) return new HookFactValue(HookFact.EventIsOwners);
            // The player who tried a shot (AfterArrowShot's owner, or its Creature): the event's player, the shooter the engine raises it for.
            if (_hook == "AfterArrowShot" && Unstored(other) is ParameterValue { Name: "owner" } or NamedValue { Name: "owner" } or QueryValue { Name: "Creature", Of: ParameterValue { Name: "owner" } or NamedValue { Name: "owner" } })
                return new HookFactValue(HookFact.EventIsOwners);
            if (IsDealer(other)) return new HookFactValue(HookFact.DealerIsOwner);
            if (other is ParameterValue { Name: "target" } or NamedValue { Name: "target" }) return new HookFactValue(HookFact.TargetIsOwner);
            if (other is NamedValue { Name: "applier" } or ParameterValue { Name: "applier" }) return new HookFactValue(HookFact.ApplierIsOwner);
            // A power's giver (ModifyPowerAmountGiven's passes: the creature applying it).
            if (other is NamedValue { Name: "giver" } or ParameterValue { Name: "giver" } && _hook.StartsWith("ModifyPowerAmountGiven", StringComparison.Ordinal)) return new HookFactValue(HookFact.ApplierIsOwner);
            // The player an event happened to (AfterOrbChanneled's player, AfterShuffle's, AfterStarsGained's gainer): the event's player is its owner.
            if (other is NamedValue { Name: "player" or "shuffler" } or ParameterValue { Name: "player" or "shuffler" } && _hook is "AfterOrbChanneled" or "AfterShuffle"
                || other is NamedValue { Name: "gainer" } or ParameterValue { Name: "gainer" } && _hook == "AfterStarsGained")
                return new HookFactValue(HookFact.EventIsOwners);
            // The potion's owner (BeforePotionUsed's and AfterPotionUsed's potion.Owner, a player, or its Creature): the hook's player (decision #313).
            if (_hook is "BeforePotionUsed" or "AfterPotionUsed" && IsPotionOwner(other)) return new HookFactValue(HookFact.EventIsOwners);
            // The player whose next turn starts (AfterEnergyReset's, AfterPlayerTurnStart's player: a payout's hook, decision #239): asked of its owner's.
            if (other is NamedValue { Name: "player" } or ParameterValue { Name: "player" } && PlayersTurnStartHooks.Contains(_hook))
                return new HookFactValue(HookFact.EventIsOwners);
            // The player asked whether they take an extra turn (ShouldTakeExtraTurn's player): the hook's player, the one the engine asks it for.
            if (other is NamedValue { Name: "player" } or ParameterValue { Name: "player" } && _hook is nameof(HookName.ShouldTakeExtraTurnEarly) or nameof(HookName.ShouldTakeExtraTurn))
                return new HookFactValue(HookFact.EventIsOwners);
            // Another creature the code names (a pet's player, a creature's player): the creatures' comparison (CreatureCompare).
            if (CreatureOf(other) != null) return null;
            throw new Gap(ReaderCapabilities.OfValue(other, otherwise: ReaderCapability.CreatureSide), $"gap: compares the owner with {other}");
        }

        /// <summary>A card's cost hook (decision #332): TryModifyEnergyCostInCombat, its Late pass, TryModifyStarCost.</summary>
        private static bool IsCostHook(string? hook) =>
            hook is nameof(HookName.TryModifyEnergyCostInCombat) or nameof(HookName.TryModifyEnergyCostInCombatLate) or nameof(HookName.TryModifyStarCost);

        /// <summary>The power a hook is told of (its power or canonicalPower parameter).</summary>
        private static bool IsPowerTold(ProgramValue value) => value is NamedValue { Name: "power" or "canonicalPower" } or ParameterValue { Name: "power" or "canonicalPower" };

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
                // The run on the Dread ascension (AscensionHelper.IsDread(): Ceremonial Beast's RitualAmount), which a player knows (decision #275).
                case QueryValue { Type: "AscensionHelper", Name: "IsDread", Arguments.Count: 0 }:
                    return new HookFactValue(HookFact.Dread);
                // A property of its owner's monster's class with no setter (Ceremonial Beast's RitualAmount, read in a method of that class its power
                // calls on it: decision #275): what its getter's code returns, its this that monster.
                case QueryValue { IsProperty: true, Type: var monsterClass, Name: var monsterProperty, Of: QueryValue { Name: "Monster", Of: { } monsterOf } monster } when IsOwner(Unstored(monsterOf))
                    && OwnersMonsterGetter(monsterClass, monsterProperty) is (Type monsterType, MethodBase getter):
                    return Ways((WaysValue)ProgramReader.ReadReturnedWays(monsterType, getter, _read).Map(part => part is NamedValue { Name: "this" } ? monster : null));
                // An orb's value read in its own programs (this.PassiveVal): the value as the game shows it, which its getter's code works out.
                case QueryValue { Of: NamedValue { Name: "this" }, IsProperty: true, Name: var shown } when _kind == ModelKind.Orb && _shownValues && OrbValueProperties().Contains(shown):
                    return new HookOrbProperty(shown);
                // OrbModel.ModifyOrbValue(x): the base class's own code, Hook.ModifyOrbValue on this orb's x (the pipeline: Focus, Infused Core).
                case QueryValue { Of: NamedValue { Name: "this" }, Arguments: [var orbBase] } ownCall when _kind == ModelKind.Orb && CallsHook(ownCall.Name, "ModifyOrbValue"):
                    return new HookOrbValue(Value(orbBase));
                // ModifyOrbValue's value and ModifyOrbPassiveTriggerCounts' count: what the listeners before this one left.
                case NamedValue { Name: "value" } or ParameterValue { Name: "value" } when _hook == "ModifyOrbValue":
                    return new HookFactValue(HookFact.OrbValueSoFar);
                case NamedValue { Name: "triggerCount" } or ParameterValue { Name: "triggerCount" } when _hook == "ModifyOrbPassiveTriggerCounts":
                    return new HookFactValue(HookFact.TriggerCountSoFar);
                // How many enemies can be hit (HittableEnemies.Count, kept in a local or not: Haunt's != 0).
                case QueryValue { Name: "Count", Of: { } hittableCounted } when IsHittableEnemies(hittableCounted):
                    return new HookFactValue(HookFact.HittableEnemyCount);
                // How many of a list of creatures the code works out there are, or pass a filter (Book Repair Knife's Count(creatures, c => ...)).
                case var listCount when CountOfList(listCount) is { } counted:
                    return counted;
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
                // A card's own value in its own method's code, read for the damage's card (ModelCall: Heavy Shot's AimMultiplier).
                case CardValue sourceVar when _inSourceCardCall:
                    return new HookFactValue(HookFact.SourceCardVar) { Of = OwnVarKey(sourceVar.Key) };
                // One of the damage's card's own values (heavyShot.DynamicVars["AimMultiplier"].BaseValue, on cardSource is HeavyShot, or cardSource's
                // own): the card's (SimCard.Vars), never the model's.
                case QueryValue { Name: "BaseValue" or "IntValue", Of: QueryValue { Type: "DynamicVarSet", Name: "Item", Arguments: [NamedValue cardVar], Of: QueryValue { Name: "DynamicVars", Of: { } cardVarsOf } } }
                    when IsSourceCard(Unstored(cardVarsOf)) || Unstored(cardVarsOf) is QueryValue { Name: "is", Of: { } isOf } && IsSourceCard(Unstored(isOf)):
                    return new HookFactValue(HookFact.SourceCardVar) { Of = cardVar.Name.Trim('"') };
                // A true-or-false the code worked out and kept as a number (int num2 = (a && b) ? 1 : 0: Lethality's): its condition, 1 or 0.
                case ConditionValue condition:
                    return Condition(condition.Condition) ?? new HookConstant(1m);
                // Which play of its card play the damage's card is in (cardSource.CurrentPlayIndex: 0 the first; the damage's card play's).
                case QueryValue { Name: "CurrentPlayIndex", Of: { } indexOf } when IsSourceCard(Unstored(indexOf)):
                    return new HookFactValue(HookFact.SourcePlayIndex);
                // No cap (ModifyDamageCap's return for a damage it doesn't cap).
                case NamedValue { Name: "Decimal.MaxValue" }: return new HookConstant(decimal.MaxValue);
                case NamedValue { Name: "amount" } when _hook == "AfterPowerAmountChanged": return new HookFactValue(HookFact.PowerAmount);
                // The amount a power lands with so far (TryModifyPowerAmountReceived's amount: after the amount given and the listeners before it).
                case NamedValue { Name: "amount" } or ParameterValue { Name: "amount" } when _hook == nameof(HookName.TryModifyPowerAmountReceived):
                    return new HookFactValue(HookFact.PowerAmount);
                // The cost so far a card's cost hook is asked about (originalCost: after the card's local modifiers and the listeners before it).
                case NamedValue { Name: "originalCost" } or ParameterValue { Name: "originalCost" } when IsCostHook(_hook):
                    return new HookFactValue(HookFact.CostSoFar);
                // The block a gain came to (AfterModifyingBlockAmount's modifiedAmount).
                case NamedValue { Name: "modifiedAmount" } or ParameterValue { Name: "modifiedAmount" } when _hook == nameof(HookName.AfterModifyingBlockAmount):
                    return new HookFactValue(HookFact.BlockModified);
                // The player of a card play its code keeps (Data.cardPlay.Player).
                case var keptPlayer when KeptPlayPlayer(keptPlayer) is { } playerField:
                    return new HookField(playerField);
                // How many a collection of its own data holds (Intercept's coveredCreatures.Count): its state ("Data.coveredCreatures.Count"), which only its
                // own methods another class calls change (InterceptPower.AddCoveredCreature, from Covered's AfterApplied: decision #270).
                case QueryValue { Name: "Count" or "get_Count", Arguments.Count: 0, Of: QueryValue { Name: var collection, Of: { } collectionOf, Arguments.Count: 0 } } when ProgramReader.IsOwnData(collectionOf)
                    && Procs.CardCode.NestedTypes(_type).Any(nested => nested.GetField(collection, Declared)?.FieldType is { IsGenericType: true } listType && listType.Name.StartsWith("List", StringComparison.Ordinal)):
                    return new HookField(ProgramReader.OwnDataPrefix + collection + ".Count");
                // The energy a spend took (AfterEnergySpent's amount): the simulator raises one energy-spent event for each energy spent.
                case NamedValue { Name: "amount" } or ParameterValue { Name: "amount" } when _hook == "AfterEnergySpent": return new HookConstant(1m);
                // The stars a spend took (AfterStarsSpent's amount): the play's stars, as the simulator spends them (one event a play).
                case NamedValue { Name: "amount" } or ParameterValue { Name: "amount" } when _hook == "AfterStarsSpent": return new HookFactValue(HookFact.StarsSpent);
                // An HP-loss pass's amount: the HP the hit would take as the passes before it left it (Hook.ModifyHpLost*).
                case NamedValue { Name: "amount" } or ParameterValue { Name: "amount" } when _hook.StartsWith("ModifyHpLost", StringComparison.Ordinal): return new HookFactValue(HookFact.HpLostSoFar);
                // The floor of a quotient (Mathf.FloorToInt((float)a / (float)b): Galactic Dust's blocks per Stars spent): (a - a rem b) / b, for counts.
                case QueryValue { Type: "Mathf", Name: "FloorToInt", Arguments: [ArithmeticValue { Op: "div" } quotient] }:
                {
                    var (dividend, divisor) = (Value(quotient.Left), Value(quotient.Right));
                    return new HookMath(HookOperation.Divide, new HookMath(HookOperation.Subtract, dividend, new HookMath(HookOperation.Remainder, dividend, divisor)), divisor);
                }
                // The HP a hit took past block (result.UnblockedDamage).
                case QueryValue { Name: "UnblockedDamage", Of: ParameterValue { Name: "result" } or NamedValue { Name: "result" } } when _hook is "AfterDamageGiven" or "AfterDamageReceived":
                    return new HookFactValue(HookFact.HitUnblocked);
                // A creature's HP (target.CurrentHp: Shriek's threshold), the creature one the hook's code names.
                case QueryValue { Name: "CurrentHp", Of: { } hpOf } when CreatureOf(hpOf) is { } withHp:
                    return CreatureFact(HookFact.CreatureHp, withHp);
                // What a hit's block took (result.BlockedDamage: Reflect's damage back).
                case QueryValue { Name: "BlockedDamage", Of: ParameterValue { Name: "result" } or NamedValue { Name: "result" } } when _hook is "AfterDamageGiven" or "AfterDamageReceived":
                    return new HookFactValue(HookFact.HitBlocked);
                // What a hit dealt, its block included (result.TotalDamage: DamageResult's blocked plus unblocked).
                case QueryValue { Name: "TotalDamage", Of: ParameterValue { Name: "result" } or NamedValue { Name: "result" } } when _hook is "AfterDamageGiven" or "AfterDamageReceived":
                    return new HookFactValue(HookFact.HitTotal);
                // PowerCmd.Apply's scaling for the players (GetScaledAmountForMultiplayer: decision #234): the amount it's given, the players, the
                // fight's scaling (MultiplayerScalingModel.GetMultiplayerScaling: a fight constant the adapter reads from the game's own math).
                case NamedValue { Name: "amount" } or ParameterValue { Name: "amount" } when _hook == "GetScaledAmountForMultiplayer": return new HookFactValue(HookFact.ScaledAmount);
                case QueryValue { Name: "Count", Of: QueryValue { Name: "Players", Of: NamedValue { Name: "combatState" } or ParameterValue { Name: "combatState" } } } when _hook == "GetScaledAmountForMultiplayer":
                    return new HookFactValue(HookFact.PlayerCount);
                case QueryValue { Type: "MultiplayerScalingModel", Name: "GetMultiplayerScaling" } when _hook == "GetScaledAmountForMultiplayer":
                    return new HookFactValue(HookFact.EncounterScaling);
                // The play count the listeners before it gave (ModifyCardPlayCount's playCount).
                case ParameterValue { Name: "playCount" } or NamedValue { Name: "playCount" } when _hook == "ModifyCardPlayCount": return new HookFactValue(HookFact.PlayCountSoFar);
                // Where the played card goes so far (ModifyCardPlayResultLocation's and AfterModifyingCardPlayResultLocation's location), and its pile.
                case ParameterValue { Name: "location" } or NamedValue { Name: "location" } when _hook is "ModifyCardPlayResultLocation" or "AfterModifyingCardPlayResultLocation":
                    return new HookFactValue(HookFact.ResultLocationSoFar);
                case QueryValue { Name: "pileType", Of: ParameterValue { Name: "location" } or NamedValue { Name: "location" } } when _hook is "ModifyCardPlayResultLocation" or "AfterModifyingCardPlayResultLocation":
                    return PileSoFar();
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
                // An enchantment's own amount (EnchantmentModel.Amount, its card's: the engine reads it from SimCard.Enchantment), its status (Normal 0,
                // Disabled 1: its own state, stored by its code), and whom its card is aimed at (base.Card.TargetType).
                case QueryValue { Name: "Amount", Of: NamedValue { Name: "this" }, Type: "EnchantmentModel" } when _kind == ModelKind.Enchantment:
                    return new HookFactValue(HookFact.Amount);
                case QueryValue { Name: "Status", Of: NamedValue { Name: "this" }, Type: "EnchantmentModel" } when _kind == ModelKind.Enchantment:
                    return new HookField("Status");
                case var targetType when _kind == ModelKind.Enchantment && IsModelCardTargets(targetType):
                    return new HookFactValue(HookFact.ModelCardTargets);
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
                // The stars the card played spent (cardPlay.Resources.StarsSpent: Black Hole's), each play of its card play the same.
                case QueryValue { Name: "StarsSpent", Of: { } starsOf } when Unstored(starsOf) is QueryValue { Name: "Resources", Of: NamedValue { Name: "cardPlay" } or ParameterValue { Name: "cardPlay" } } && IsPlayHook(_hook):
                    return new HookFactValue(HookFact.PlayStars);
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
                // A power of a value (Shadowmeld's (decimal)Math.Pow(2.0, base.Amount)): worked out in doubles, as Math.Pow does, then made a decimal.
                case QueryValue { Type: "Math", Name: "Pow", Arguments: [var basis, var exponent] }:
                    return new HookMath(HookOperation.Power, Value(basis), Value(exponent));
                // A power summed over the owner's living opponents (Sum(Where(GetOpponentsOf(base.Owner), c => c.IsAlive), a => a.GetPowerAmount<T>())).
                case QueryValue { Type: "Enumerable", Name: "Sum", Arguments: [var opponents, LambdaValue each] } when LivingOpponents(opponents) && PowerAmountOf(each) is { } summed:
                    return new HookFactValue(HookFact.OpponentsPowerAmount) { Of = PowerClass(summed) };
                // x-- and x++ on a decimal (Panache's DynamicVars["CardsLeft"].BaseValue--).
                case QueryValue { Type: "Decimal", Name: "op_Decrement" or "op_Increment", Arguments: [var stepped] } step:
                    return new HookMath(step.Name == "op_Decrement" ? HookOperation.Subtract : HookOperation.Add, Value(stepped), new HookConstant(1m));
                // -x on a decimal (Monologue's -StrengthApplied).
                case QueryValue { Type: "Decimal", Name: "op_UnaryNegation", Arguments: [var negated] }:
                    return new HookMath(HookOperation.Multiply, Value(negated), new HookConstant(-1m));
                // A local kept before a store to its own state it read, used after that store (Joss Paper's int cardsExhausted = CardsExhausted, then
                // CardsExhausted %= ExhaustAmount, then Draw(cardsExhausted / ExhaustAmount)): the value as it was, which the reader doesn't keep.
                case StoredValue stored when _stores.Any(store => store.Offset > stored.Offset && store.Offset < _at && ReadsOwn(stored.Value, store.Field)):
                {
                    // A let: the local worked out where the code keeps it (a LetStep there, before the store), read here as it was then.
                    if (_lets == null) throw new Gap(ReaderCapability.LocalKeptBeforeAStore, $"gap: a local keeps {stored.Value} from before it's stored, and is used after");
                    var name = "local" + stored.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (!_lets.Any(let => let.Name == name))
                    {
                        var at = _at;
                        _at = stored.Offset;
                        try { _lets.Add((name, stored.Offset, Value(stored.Value))); }
                        finally { _at = at; }
                    }
                    return new HookLocal(name);
                }
                case StoredValue stored:
                    return Value(stored.Value);
                case CompareValue compare:
                    return Compare(compare);
                // The players' turn number (base.Owner.PlayerCombatState.TurnNumber).
                case QueryValue { Name: "TurnNumber", Of: QueryValue { Name: "PlayerCombatState", Of: { } turnOf } } when IsOwner(turnOf):
                    return new HookFactValue(HookFact.TurnNumber);
                // The fight's round (combatState.RoundNumber, base.Owner.CombatState.RoundNumber).
                case QueryValue { Name: "RoundNumber", Of: { } roundOf } when IsTheFight(roundOf):
                    return new HookFactValue(HookFact.RoundNumber);
                // The players in the fight (base.Owner.CombatState.RunState.Players.Count, combatState.Players.Count).
                case QueryValue { Name: "Count", Of: QueryValue { Name: "Players", Of: { } playersOf } } when IsTheFight(playersOf) || playersOf is QueryValue { Name: "RunState", Of: { } runOf } && IsTheFight(runOf):
                    return new HookFactValue(HookFact.PlayerCount);
                // The owner's block (base.Owner.Creature.Block).
                case QueryValue { Name: "Block", Of: { } blockOf } when IsOwner(blockOf):
                    return new HookFactValue(HookFact.OwnerBlock);
                // The cards in the owner's hand (PileType.Hand.GetPile(base.Owner).Cards.Count, its Cards kept in a local or not).
                case QueryValue { Name: "Count", Of: { } cards } when IsOwnersHandCards(cards):
                    return new HookFactValue(HookFact.OwnerHandCount);
                // The potions the owner holds (base.Owner.Potions.Count(), decision #313).
                case QueryValue { Type: "Enumerable", Name: "Count", Arguments: [var potions] } when IsOwnersPotions(potions):
                    return new HookFactValue(HookFact.OwnerPotionCount);
                // The owner's orbs of a kind (OrbQueue.Orbs.Count(o => o is FrostOrb)).
                case QueryValue { Type: "Enumerable", Name: "Count", Arguments: [var orbs, LambdaValue lambda] } when IsOwnersOrbs(orbs) && OrbKindTested(lambda) is { } orbKind:
                    return new HookFactValue(HookFact.OwnerOrbCount) { Of = orbKind.ToString() };
                // All the owner's orbs (OrbQueue.Orbs.Count: Consuming Shadow's).
                case QueryValue { Name: "Count", Of: { } allOrbs } when IsOwnersOrbs(allOrbs):
                    return new HookFactValue(HookFact.OwnerOrbCount);
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
            // A nullable's value or its default ((x?.y?.Z).GetValueOrDefault(): Insatiable Dominion's turn number): each way's value, 0 where it's null.
            if (value is QueryValue { Name: "GetValueOrDefault", Arguments.Count: 0, Of: { } nullable }) return Value(OrDefault(Unstored(nullable)));
            // A value of several ways (a local set on each branch: Vulnerable's num after Paper Phrog, then Cruelty): the first way that holds.
            if (value is WaysValue ways)
                return new HookWays(ways.Ways.Select(way => new HookWay(Condition(way.When) ?? new HookConstant(1m), Value(way.Value))).ToList());
            if (value is QueryValue { Of: { } receiver } call && ModelCall(call, receiver) is { } called) return called;
            throw new Gap(ReaderCapabilities.OfValue(value), $"gap: the value {value}");
        }

        /// <summary>
        /// The card played compared with the play of the first entry of the fight's log a FirstOrDefault finds (MelodyCmd.IsFirstPlayThisTurn's
        /// FirstOrDefault(CardPlaysStarted, e =&gt; this turn, a tag, its player's)?.CardPlay == cardPlay, read where its helper is called): the play is
        /// that first one (HookFact.PlayIsFirst). Every way of the value is null (none found: never the play) or that entry's play. Null for any other
        /// comparison.
        /// </summary>
        private HookValue? FirstPlayCompare(CompareValue compare)
        {
            if (compare.Op is not (Comparison.Equal or Comparison.NotEqual) || !IsPlayHook(_hook)) return null;
            foreach (var (one, other) in new[] { (compare.Left, compare.Right), (compare.Right, compare.Left) })
            {
                if (Unstored(other) is not (ParameterValue { Name: "cardPlay" } or NamedValue { Name: "cardPlay" })) continue;
                var values = Unstored(one) is WaysValue ways ? ways.Ways.Select(way => Unstored(way.Value)).ToList() : new List<ProgramValue> { Unstored(one) };
                QueryValue? first = null;
                foreach (var value in values)
                {
                    if (value is NullValue) continue;
                    if (value is not QueryValue { Name: "CardPlay", Of: { } foundOf } || Unstored(foundOf) is not QueryValue { Name: "FirstOrDefault" } found || !ProgramReader.ReadsHistory(found)) return null;
                    if (first != null && found.ToString() != first.ToString()) return null;
                    first = found;
                }
                if (first == null) return null;
                var isFirst = FirstPlay(first);
                return compare.Op == Comparison.Equal ? isFirst : Not(isFirst);
            }
            return null;
        }

        /// <summary>
        /// The first entry a FirstOrDefault of the fight's log finds is the card played (HookFact.PlayIsFirst): an entry of the plays started this turn,
        /// its player's (e.CardPlay.Player == cardPlay.Player: the turn counts the play's FirstThisTurn is read from are its player's), of every play,
        /// of a type or with a tag (the ledger's own reading of the rest of its tests). Anything else is a gap.
        /// </summary>
        private HookValue FirstPlay(QueryValue first)
        {
            if (first.Arguments is not [var source, LambdaValue lambda] || lambda.Body.Terms is not [var tests]) throw new Gap(ReaderCapability.FightLog, $"gap: the fight's log's first entry of {first}");
            bool IsEntryPlayer(ProgramValue value) => Unstored(value) is QueryValue { Name: "Player", Of: QueryValue { Name: "CardPlay", Of: ParameterValue { Name: var entry } } } && entry == lambda.Parameter;
            static bool IsPlayPlayer(ProgramValue value) => value is QueryValue { Name: "Player", Of: ParameterValue { Name: "cardPlay" } or NamedValue { Name: "cardPlay" } };
            bool PlaysPlayer(ConditionLiteral literal) => literal.Holds && literal.Test is CompareValue { Op: Comparison.Equal } player
                && (IsEntryPlayer(player.Left) && IsPlayPlayer(player.Right) || IsEntryPlayer(player.Right) && IsPlayPlayer(player.Left));
            if (tests.Count(PlaysPlayer) != 1) throw new Gap(ReaderCapability.FightLog, $"gap: the fight's log's first entry, not of the play's player: {first}");
            var rest = tests.Where(literal => !PlaysPlayer(literal)).Aggregate(ProgramCondition.Always, (condition, literal) => condition.And(literal));
            var reading = HistoryQueryReader.Read(first with { Name = "Count", Arguments = new ProgramValue[] { source, lambda with { Body = rest } } }, (enumType, number) => EnumName(_type.Assembly, enumType, number));
            if (reading.Query is not { Kind: HistoryKind.CardPlayStarted, Scope: HistoryScope.ThisTurn, Whose: HistoryWhose.Anyone, Details: HistoryDetail.None, Power: null, Card: null, Orb: null } query
                || query.CardType != SimCardType.None && query.CardTag != null)
                throw new Gap(ReaderCapability.FightLog, $"gap: the fight's log's first entry ({reading.Gap ?? "not a first play"}: {reading.Why}): {first}");
            return new HookFactValue(HookFact.PlayIsFirst) { Of = query.CardTag is { } tag ? "Tag:" + tag : query.CardType != SimCardType.None ? "Type:" + query.CardType : "" };
        }

        /// <summary>
        /// A question to the fight's log, as the history ledger counts it (HistoryQueryReader, the one translation every card, power and relic
        /// shares); a gap of the kind the reader names when the ledger doesn't count it.
        /// </summary>
        private HookValue History(QueryValue code)
        {
            var reading = HistoryQueryReader.Read(code, (enumType, number) => EnumName(_type.Assembly, enumType, number));
            // A sum of a count per card type (an Attack or a Skill: Nostalgia's): the counts added, or any of them there.
            if (reading.Sum is { Count: > 0 } parts)
                return parts.Select(part => (HookValue)new HookHistory(part) { Any = reading.AsksAny })
                    .Aggregate((sum, part) => new HookMath(reading.AsksAny ? HookOperation.Or : HookOperation.Add, sum, part));
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

        /// <summary>While the damage's card's own method is read (ModelCall): its own values are that card's.</summary>
        private bool _inSourceCardCall;

        /// <summary>
        /// A call into another model's own method on a model got from a creature (paperPhrog.ModifyVulnerableMultiplier(target, num, ...), on
        /// dealer.Player?.GetRelic&lt;PaperPhrog&gt;(); debilitate.ModifyWeakMultiplier(dealer, num, ...)): the method's returned value read from its
        /// code, its parameters the caller's arguments and its base.Owner the creature it was got from (its base.Amount that power's amount there),
        /// on each way the receiver is got (a ?? is one way each). Null for a call that isn't one; a gap where its code can't be read.
        /// </summary>
        private HookValue? ModelCall(QueryValue call, ProgramValue receiver)
        {
            // The damage's card's own method, on it as its class (heavyShot.ModifyAimAmount(num) on cardSource is HeavyShot: decision #232): its
            // returned value read from its code, its parameters the caller's arguments, its own values the card's (HookFact.SourceCardVar).
            if (Unstored(receiver) is QueryValue { Name: "is", Of: { } cardOf, Type: var cardClass } && IsSourceCard(Unstored(cardOf)))
            {
                var cardType = _type.Assembly.GetType("MegaCrit.Sts2.Core.Models.Cards." + cardClass) ?? throw new Gap(ReaderCapability.ModelCall, $"gap: no card class {cardClass}");
                var cardMethod = cardType.GetMethods(Declared).FirstOrDefault(candidate => candidate.Name == call.Name && candidate.GetParameters().Length == call.Arguments.Count)
                    ?? throw new Gap(ReaderCapability.ModelCall, $"gap: {cardClass}.{call.Name} not found");
                if (cardMethod.ReturnType.Name != "Decimal") throw new Gap(ReaderCapability.ModelCall, $"gap: {cardClass}.{call.Name} gives a {cardMethod.ReturnType.Name}");
                var cardParameters = cardMethod.GetParameters().Select((parameter, index) => (parameter.Name!, call.Arguments[index])).ToDictionary(pair => pair.Item1, pair => pair.Item2, StringComparer.Ordinal);
                var cardReturned = (WaysValue)ProgramReader.ReadReturnedWays(cardType, cardMethod, _read).Map(part => part switch
                {
                    NamedValue { Name: "this" } => receiver,
                    ParameterValue { Name: var name } when cardParameters.TryGetValue(name, out var argument) => argument,
                    NamedValue { Name: var name } when cardParameters.TryGetValue(name, out var argument) => argument,
                    _ => null,
                });
                // The card's own values in its code (CardValue: base.DynamicVars["AimMultiplier"]) are the damage's card's, read as they run.
                _inSourceCardCall = true;
                try { return Ways(cardReturned); }
                finally { _inSourceCardCall = false; }
            }
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

        /// <summary>
        /// A nullable value as its GetValueOrDefault gives it: a null is 0, a value made a nullable (new Nullable&lt;T&gt;(x)) is x, each way of
        /// several so; anything else is a gap.
        /// </summary>
        private static ProgramValue OrDefault(ProgramValue nullable) => nullable switch
        {
            NullValue => new ConstantValue(0),
            QueryValue { Type: "Nullable`1", Name: ".ctor", Arguments: [var made] } => made,
            WaysValue ways => new WaysValue(ways.Ways.Select(way => (way.When, OrDefault(Unstored(way.Value)))).ToList()),
            _ => throw new Gap(ReaderCapability.ValueNotFollowed, $"gap: the nullable {nullable}"),
        };

        /// <summary>
        /// A nullable value's HasValue: false for a null, true for a value made a nullable (new Nullable&lt;T&gt;(x)), each way of several so; null
        /// for anything else.
        /// </summary>
        private static ProgramValue? HasValueOf(ProgramValue nullable) => nullable switch
        {
            NullValue => new ConstantValue(0),
            QueryValue { Type: "Nullable`1", Name: ".ctor", Arguments: [_] } => new ConstantValue(1),
            WaysValue ways when ways.Ways.Select(way => HasValueOf(Unstored(way.Value))).ToList() is var values && values.All(value => value != null) =>
                new WaysValue(ways.Ways.Select((way, at) => (way.When, values[at]!)).ToList()),
            _ => null,
        };

        /// <summary>
        /// The costed card's pile (card.Pile.Type, or the nullable a switch on card.Pile?.Type makes of it) in a card's cost hook (decision #334).
        /// </summary>
        private bool IsCostedPileType(ProgramValue value) => Unstored(value) switch
        {
            QueryValue { Name: "Type", Of: QueryValue { Name: "Pile", Of: { } pileOf } } => IsCostHook(_hook) && IsEventCard(Unstored(pileOf)),
            QueryValue { Name: "GetValueOrDefault", Arguments.Count: 0, Of: { } nullable } => IsCostedPileType(OrDefault(Unstored(nullable))),
            // The switch's nullable, its null way the pile not there (no card a simulation costs is in no pile): the way that's made.
            WaysValue ways => ways.Ways.Any(way => IsCostedPileType(way.Value)) && ways.Ways.All(way => way.Value is ConstantValue { Value: 0 } || IsCostedPileType(way.Value)),
            _ => false,
        };

        /// <summary>
        /// The costed card's pile against one of the game's piles (case PileType.Hand: case PileType.Play: Free Attack's): the simulator's
        /// SimCardPile, any pile other than the hand or the play pile its Other. Null for any other compare.
        /// </summary>
        private HookValue? CostedPileCompare(CompareValue compare)
        {
            if (compare.Op is not (Comparison.Equal or Comparison.NotEqual)) return null;
            foreach (var (one, other) in new[] { (compare.Left, compare.Right), (compare.Right, compare.Left) })
            {
                if (!IsCostedPileType(one) || other is not ConstantValue pile) continue;
                var simPile = EnumName(_type.Assembly, "PileType", pile.Value) switch
                {
                    "Hand" => SimCardPile.Hand,
                    "Play" => SimCardPile.Play,
                    null => throw new Gap(ReaderCapability.CardFacts, $"gap: tests the card's pile is {pile.Value}"),
                    _ => SimCardPile.Other,
                };
                return new HookMath(OperationOf(compare.Op), new HookFactValue(HookFact.CardPile), new HookConstant((int)simPile));
            }
            return null;
        }

        /// <summary>A Modify hook's returns: each way's test and value, the first that holds.</summary>
        private HookValue Ways(WaysValue ways)
        {
            if (ways.Ways.Count == 0) throw new Gap(ReaderCapability.ValueNotFollowed, "gap: no return read");
            return new HookWays(ways.Ways.Select(way => new HookWay(Condition(way.When) ?? new HookConstant(1m), Value(way.Value))).ToList());
        }

        /// <summary>A true-or-false hook's returns (a Should hook's): each way's test and what it returns as a test (1 or 0), the first that holds.</summary>
        private HookValue TestWays(WaysValue ways)
        {
            if (ways.Ways.Count == 0) throw new Gap(ReaderCapability.ValueNotFollowed, "gap: no return read");
            return new HookWays(ways.Ways.Select(way => new HookWay(Condition(way.When) ?? new HookConstant(1m),
                Unstored(way.Value) is ConstantValue ? Value(way.Value) : Test(way.Value))).ToList());
        }

        /// <summary>
        /// A creature-valued hook's returns (a redirect's: ModifyPreBlockDamageTarget, ModifyUnblockedDamageTarget): each way's test and the creature it
        /// names (its id: HookFact.CreatureId; a null: no one, Who.None). A creature the reader can't name is a gap.
        /// </summary>
        private HookValue CreatureWays(WaysValue ways)
        {
            if (ways.Ways.Count == 0) throw new Gap(ReaderCapability.ValueNotFollowed, "gap: no return read");
            HookValue CreatureValue(ProgramValue value) => CreatureOf(Unstored(value)) is { } creature ? CreatureFact(HookFact.CreatureId, creature)
                : Unstored(value) is NullValue ? new HookConstant(Who.None)
                : throw new Gap(ReaderCapability.ValueNotFollowed, $"gap: returns a creature the reader can't name ({value})");
            return new HookWays(ways.Ways.Select(way => new HookWay(Condition(way.When) ?? new HookConstant(1m), CreatureValue(way.Value))).ToList());
        }

        /// <summary>
        /// A played card's result location's ways (ModifyCardPlayResultLocation's returns: the location it's given, or that location with its pile and
        /// position set), each as the simulator's SimResultLocation (HookFact.ResultLocationSoFar for the one given). A pile and position the
        /// simulator has no location for (the bottom of the draw pile, a random place) is a gap.
        /// </summary>
        private HookValue LocationWays(WaysValue ways)
        {
            if (ways.Ways.Count == 0) throw new Gap(ReaderCapability.ResultLocation, "gap: no return read");
            HookValue LocationValue(ProgramValue value) => Unstored(value) switch
            {
                ParameterValue { Name: "location" } or NamedValue { Name: "location" } => new HookFactValue(HookFact.ResultLocationSoFar),
                FieldsSetValue { Of: ParameterValue { Name: "location" } or NamedValue { Name: "location" }, Fields: var fields } => new HookConstant((int)LocationOf(fields)),
                var other => throw new Gap(ReaderCapability.ResultLocation, $"gap: returns a location the reader can't follow ({other})"),
            };
            return new HookWays(ways.Ways.Select(way => new HookWay(Condition(way.When) ?? new HookConstant(1m), LocationValue(way.Value))).ToList());
        }

        /// <summary>The location a CardLocation's pile and position set name (PileType, CardPilePosition: the game's enums, read by name).</summary>
        private SimResultLocation LocationOf(IReadOnlyList<(string Field, ProgramValue Value)> fields)
        {
            string? Named(string field, string enumType) =>
                fields.FirstOrDefault(set => set.Field == field).Value is ConstantValue { Value: var number } ? (EnumMembers(_type.Assembly, enumType) ?? throw new Gap(ReaderCapability.ResultLocation, $"gap: no enum {enumType}")).FirstOrDefault(member => member.Value == number).Name : null;
            var pile = Named("pileType", "PileType") ?? throw new Gap(ReaderCapability.ResultLocation, "gap: a location whose pile the reader can't follow");
            var position = Named("position", "CardPilePosition");
            return (pile, position) switch
            {
                ("Draw", "Top") => SimResultLocation.DrawTop,
                ("Hand", _) => SimResultLocation.HandTop,
                ("Exhaust", _) => SimResultLocation.Exhaust,
                ("Discard", _) => SimResultLocation.Default,
                ("None", _) => SimResultLocation.Nowhere,
                _ => throw new Gap(ReaderCapability.ResultLocation, $"gap: a location the simulator has none for ({pile}, {position ?? "its position as given"})"),
            };
        }

        /// <summary>
        /// The pile a location so far is in (ModifyCardPlayResultLocation's location.pileType), as the game's PileType number: the simulator's
        /// location so far, each way its pile.
        /// </summary>
        private HookValue PileSoFar()
        {
            var piles = EnumMembers(_type.Assembly, "PileType") ?? throw new Gap(ReaderCapability.ResultLocation, "gap: no enum PileType");
            decimal Number(string pile) => piles.FirstOrDefault(member => member.Name == pile) is { Name: not null } found ? found.Value : throw new Gap(ReaderCapability.ResultLocation, $"gap: no PileType.{pile}");
            HookWay Way(SimResultLocation location, string pile) =>
                new(new HookMath(HookOperation.Equal, new HookFactValue(HookFact.ResultLocationSoFar), new HookConstant((int)location)), new HookConstant(Number(pile)));
            return new HookWays(new List<HookWay>
            {
                Way(SimResultLocation.Default, "Discard"), Way(SimResultLocation.DrawTop, "Draw"), Way(SimResultLocation.HandTop, "Hand"),
                Way(SimResultLocation.Exhaust, "Exhaust"), Way(SimResultLocation.Nowhere, "None"),
            });
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
                // An indexed key (base.DynamicVars["SelfDamage"], cast to its kind: Inferno's DamageVar) before a property's.
                case QueryValue { Type: "DynamicVarSet", Name: "Item", Arguments: [NamedValue named] }: return named.Name.Trim('"');
                case QueryValue { Type: "DynamicVarSet", Name: var property, Of: QueryValue { Name: "DynamicVars", Of: NamedValue { Name: "this" } } }: return KeyOfProperty(property);
                default: return null;
            }
        }

        /// <summary>The key DynamicVarSet's property reads (its getter's text: Dexterity's "DexterityPower"), or the name itself (an indexed key).</summary>
        private string KeyOfProperty(string property) => ProgramReader.DynamicVarKey(_type.Assembly, property, _read);

        // ---- what values are

        /// <summary>
        /// The power class a value lists its owner's powers of (base.Owner.GetPowerInstances&lt;T&gt;(), made a list or not), or null.
        /// </summary>
        private static string? OwnersPowersOf(ProgramValue value) => Unstored(value) switch
        {
            QueryValue { Name: "ToList", Arguments: [var listed] } => OwnersPowersOf(listed),
            QueryValue { Name: "GetPowerInstances", GenericArguments: [var power], Of: { } of } when IsOwner(Unstored(of)) => power,
            _ => null,
        };

        /// <summary>
        /// A property of a monster class of the game's with a getter and no setter (Ceremonial Beast's RitualAmount): its class and getter, whose code
        /// works out its value; null for any other (a property with a setter is that monster's own state, MonsterStateStep's).
        /// </summary>
        private (Type Monster, MethodBase Getter)? OwnersMonsterGetter(string monsterClass, string property)
        {
            if (_type.Assembly.GetType("MegaCrit.Sts2.Core.Models.Monsters." + monsterClass) is not { } monster || !IsMonsterClass(monsterClass)) return null;
            var declared = monster.GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            return declared is { SetMethod: null, GetMethod: { } getter } ? (monster, getter) : null;
        }

        /// <summary>
        /// The power classes of the game's a removal by class takes off (a class, or every class of an abstract one: TemporaryStrengthPower's Dark
        /// Shackles, Flex Potion...), by name; a gap where one runs code of its own as it goes (PowerCmd.Remove calls its AfterRemoved).
        /// </summary>
        private IReadOnlyList<string> Removable(IEnumerable<string> classes)
        {
            var powers = new List<string>();
            foreach (var name in classes)
            {
                var type = _type.Assembly.GetType("MegaCrit.Sts2.Core.Models.Powers." + name) ?? throw new Gap(ReaderCapability.CommandOnAnotherPower, $"gap: no power class {name}");
                var each = type.IsAbstract ? _type.Assembly.GetTypes().Where(candidate => !candidate.IsAbstract && type.IsAssignableFrom(candidate)).ToList() : new List<Type> { type };
                foreach (var power in each)
                {
                    // Its class or a base class below PowerModel declares an AfterRemoved (an override of PowerModel's, which does nothing). Its own class
                    // (Burrowed's PowerCmd.Remove<BurrowedPower>(base.Owner)): its AfterRemoved is one of the hooks read with it, as PowerCmd.Remove(this)'s is.
                    for (var declaring = power; declaring != null && declaring != _type && declaring.Name != nameof(MegaCrit.Sts2.Core.Models.PowerModel); declaring = declaring.BaseType)
                        if (declaring.GetMethod("AfterRemoved", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null)
                            throw new Gap(ReaderCapability.CommandOnAnotherPower, $"gap: removes {power.Name}, whose AfterRemoved runs as it goes");
                    powers.Add(power.Name);
                }
            }
            return powers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// The model's owner: base.Owner (a power's creature, a relic's, a potion's or an orb's player), an enchantment's or affliction's card's
        /// owner (base.Card.Owner), its player or creature.
        /// </summary>
        private static bool IsOwner(ProgramValue value) => value switch
        {
            QueryValue { Name: "Owner", Of: NamedValue { Name: "this" }, Type: "PowerModel" or "RelicModel" or "AbstractModel" or "PotionModel" or "OrbModel" } => true,
            QueryValue { Name: "Owner", Of: QueryValue { Name: "Card", Of: NamedValue { Name: "this" }, Type: "EnchantmentModel" or "AfflictionModel" } } => true,
            // AfterRemoved's oldOwner: the creature it came off (PowerCmd.Remove passes power.Owner; the one parameter of the game's by that name).
            ParameterValue { Name: "oldOwner" } or NamedValue { Name: "oldOwner" } => true,
            // Its owner's monster's creature (base.Creature in a method of that monster's class its power calls on it: decision #275).
            QueryValue { Name: "Creature", Of: QueryValue { Name: "Monster", Of: { } monsterOf } } => IsOwner(monsterOf),
            QueryValue { Name: "Player" or "Creature", Of: { } of } => IsOwner(of),
            _ => false,
        };

        /// <summary>The fight's combat state: a hook's combatState parameter, the model's own (base.CombatState) or its owner's (base.Owner.CombatState).</summary>
        private static bool IsTheFight(ProgramValue value) => (value is StoredValue stored ? stored.Value : value) switch
        {
            ParameterValue { Name: "combatState" } or NamedValue { Name: "combatState" } => true,
            QueryValue { Name: "CombatState", Of: NamedValue { Name: "this" } } => true,
            QueryValue { Name: "CombatState", Of: { } of } => IsOwner(of),
            _ => false,
        };

        /// <summary>The owner's hand: PileType.Hand.GetPile(base.Owner) (the pile type read by its name on this branch).</summary>
        private bool IsOwnersHand(ProgramValue value) =>
            (value is StoredValue stored ? stored.Value : value) is QueryValue { Name: "GetPile", Arguments: [ConstantValue pileType, var whose] }
            && EnumName(_type.Assembly, "PileType", pileType.Value) == "Hand" && IsOwner(whose);

        /// <summary>The cards in the owner's hand: GetPile(Hand, base.Owner).Cards, kept in a local or not.</summary>
        private bool IsOwnersHandCards(ProgramValue value) =>
            (value is StoredValue stored ? stored.Value : value) is QueryValue { Name: "Cards", Of: { } pile } && IsOwnersHand(pile);

        /// <summary>The owner's potions: base.Owner.Potions (decision #313).</summary>
        private static bool IsOwnersPotions(ProgramValue value) =>
            (value is StoredValue stored ? stored.Value : value) is QueryValue { Name: "Potions", Of: { } player } && IsOwner(player);

        /// <summary>The potion a potion hook is given's owner (potion.Owner, a player, or its Creature: decision #313).</summary>
        private static bool IsPotionOwner(ProgramValue value) =>
            (value is StoredValue stored ? stored.Value : value) is QueryValue { Name: "Owner", Of: ParameterValue { Name: "potion" } or NamedValue { Name: "potion" } }
            || value is QueryValue { Type: "Player", Name: "Creature", Of: { } player } && IsPotionOwner(player);

        /// <summary>The owner's orbs: base.Owner.Player.PlayerCombatState.OrbQueue.Orbs.</summary>
        private static bool IsOwnersOrbs(ProgramValue value) =>
            (value is StoredValue stored ? stored.Value : value) is QueryValue { Name: "Orbs", Of: QueryValue { Name: "OrbQueue", Of: QueryValue { Name: "PlayerCombatState", Of: { } player } } }
            && IsOwner(player);

        /// <summary>
        /// The value reads its model's own field or property by this name (this.CardsExhausted; its data's field, Data.name), or one of its own
        /// properties whose getter does (Glass's PassiveVal reads _passiveVal).
        /// </summary>
        private bool ReadsOwn(ProgramValue value, string field) => value.Parts().Any(part => part is QueryValue { Name: var name, Of: var receiver } query
            && (receiver is NamedValue { Name: "this" } && name == field || receiver != null && ProgramReader.IsOwnData(receiver) && ProgramReader.OwnDataPrefix + name == field
                || receiver is NamedValue { Name: "this" } && query.IsProperty && GetterReadsOwn(name, field)));

        private bool GetterReadsOwn(string property, string field)
        {
            if (_type.GetProperty(property, Declared)?.GetMethod is not { } getter || !_inlining.Add("reads:" + property)) return false;
            try { return ProgramReader.ReadReturnedValue(_type, getter, _read) is { } returned && ReadsOwn(returned, field); }
            catch (InvalidOperationException) { return false; }
            finally { _inlining.Remove("reads:" + property); }
        }

        /// <summary>
        /// The enemies that can be hit: CombatState.HittableEnemies, filtered on IsHittable or not, or (for a player's model: an orb's, a relic's) its
        /// owner's opponents that can be hit (ToList(Where(GetOpponentsOf(base.Owner), e => e.IsHittable))), kept in a local or not.
        /// </summary>
        private bool IsHittableEnemies(ProgramValue value)
        {
            value = Unstored(value);
            // A value of several ways (base.Owner.CombatState?.HittableEnemies ?? Array.Empty<Creature>(): Serpent Form's): the ways that can hold, each
            // the hittable enemies, but for a fallback of no creatures (Array.Empty), reached only where the fight isn't there, never in a simulation.
            if (value is WaysValue ways)
            {
                var holding = ways.Ways.Where(way => Simplified(Condition(way.When) ?? new HookConstant(1m)) is not HookConstant { Value: 0m })
                    .Where(way => Unstored(way.Value) is not QueryValue { Type: "Array" or "Enumerable", Name: "Empty", Arguments.Count: 0 }).ToList();
                return holding.Count > 0 && holding.All(way => IsHittableEnemies(way.Value));
            }
            if (value is QueryValue { Name: "ToList", Arguments: [var listed] }) value = Unstored(listed);
            if (value is QueryValue { Type: "Enumerable", Name: "Where", Arguments: [var filtered, LambdaValue hittable] } && LambdaReads(hittable, "IsHittable")) value = Unstored(filtered);
            return value is QueryValue { Name: "HittableEnemies" }
                || _kind is ModelKind.Orb or ModelKind.Relic && value is QueryValue { Name: "GetOpponentsOf", Arguments: [var whose] } && IsOwner(whose);
        }

        /// <summary>A lambda that gives its parameter's property by this name (e => e.IsHittable, c => c.CurrentHp).</summary>
        private static bool LambdaReads(LambdaValue lambda, string property) =>
            lambda.Body.Terms is [[{ Holds: true, Test: QueryValue { Name: var name, Of: ParameterValue { Name: var given } } }]] && name == property && given == lambda.Parameter;

        /// <summary>The hooks given an orb (its parameter orb).</summary>
        private static readonly HashSet<string> OrbHooks = new(StringComparer.Ordinal) { "ModifyOrbValue", "ModifyOrbPassiveTriggerCounts", "AfterModifyingOrbPassiveTriggerCount", "AfterOrbEvoked", "AfterOrbChanneled" };

        /// <summary>The orb a hook is given (its orb parameter).</summary>
        private bool IsHookOrb(ProgramValue value) => Unstored(value) is ParameterValue { Name: "orb" } or NamedValue { Name: "orb" } && OrbHooks.Contains(_hook);

        /// <summary>The player whose the hook's orb is (orb.Owner).</summary>
        private bool IsOrbOwner(ProgramValue value) => value is QueryValue { Name: "Owner", Of: { } orb } && IsHookOrb(orb);

        /// <summary>The owner's front orb (base.Owner.PlayerCombatState.OrbQueue.Orbs[0]).</summary>
        private static bool IsOwnersFrontOrb(ProgramValue value) =>
            Unstored(value) is QueryValue { Name: "Item" or "get_Item", Of: { } orbs, Arguments: [ConstantValue { Value: 0 }] } && IsOwnersOrbs(orbs);

        /// <summary>
        /// The count of a loop that upgrades each of a random few of its owner's hand's upgradable cards and does nothing else: its items a
        /// TakeRandom (of the hand's cards filtered on IsUpgradable, then how many), its body one CardCmd.Upgrade of the item. Null otherwise.
        /// </summary>
        private static ProgramValue? UpgradesRandomHandCards(ProgramValue items, IReadOnlyList<ProgramStep> body)
        {
            if (Unstored(items) is not QueryValue { Name: "TakeRandom", Arguments: [var from, var count, ..] }) return null;
            if (body is not [CommandStep { Command.FullName: "CardCmd.Upgrade", Values: [var upgraded, ..] }] || Unstored(upgraded) is not NamedValue { Name: "each" }) return null;
            var source = Unstored(from);
            if (source is QueryValue { Name: "ToList", Arguments: [var listed] }) source = Unstored(listed);
            if (source is not QueryValue { Name: "Where", Arguments: [var cards, LambdaValue filter] }) return null;
            var handPile = ((int)MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var ofTheHand = cards.Parts().Any(part => part is QueryValue { Name: "GetPile" } pile && pile.Parts().Any(inner => inner is ConstantValue constant && constant.ToString() == handPile))
                            && cards.Parts().Any(part => part is QueryValue { Name: "Owner" });
            var upgradable = filter.Body.Literals.Any(literal => literal.Holds && literal.Test.Parts().Any(part => part is QueryValue { Name: "IsUpgradable" }));
            return ofTheHand && upgradable ? count : null;
        }

        /// <summary>
        /// The count and type of a loop that makes each of a few random cards into its owner's hand and does nothing else: its items a
        /// CardFactory.GetForCombat(base.Owner.Player, Where(the owner's character's card pool, c => c.Type == T), count, rng), listed or not; its body
        /// one CardPileCmd.AddGeneratedCardToCombat of the item into the owner's hand, for the owner. Null otherwise.
        /// </summary>
        private (ProgramValue Count, SimCardType Type)? RandomCardsMadeInHand(ProgramValue items, IReadOnlyList<ProgramStep> body)
        {
            var source = Unstored(items);
            if (source is QueryValue { Name: "ToList", Arguments: [var listed] }) source = Unstored(listed);
            if (source is not QueryValue { Type: "CardFactory", Name: "GetForCombat", Arguments: [var player, var options, var count, _] } || !IsOwner(Unstored(player))) return null;
            if (body is not [CommandStep { Command.FullName: "CardPileCmd.AddGeneratedCardToCombat" } add] || add.Values.Count < 3 || Unstored(add.Values[0]) is not NamedValue { Name: "each" }) return null;
            var types = add.Command.ParameterTypes;
            var pileAt = IndexOf(types, "PileType");
            var creatorAt = IndexOf(types, "Player");
            if (pileAt < 0 || add.Values[pileAt] is not ConstantValue pile || EnumName(_type.Assembly, "PileType", pile.Value) != "Hand" || creatorAt < 0 || !IsOwner(add.Values[creatorAt])) return null;
            if (Unstored(options) is not QueryValue { Type: "Enumerable", Name: "Where", Arguments: [var pool, LambdaValue ofType] }) return null;
            if (!pool.Parts().Any(part => part is QueryValue { Name: "CardPool" }) || !pool.Parts().Any(IsOwner)) return null;
            if (ofType.Body.Terms is not [[{ Holds: true, Test: CompareValue { Op: Comparison.Equal, Left: QueryValue { Name: "Type", Of: ParameterValue { Name: var tested } }, Right: ConstantValue type } }]]
                || tested != ofType.Parameter) return null;
            return EnumName(_type.Assembly, "CardType", type.Value) is { } typeName && Enum.TryParse<SimCardType>(typeName, out var simType) ? (count, simType) : null;
        }

        /// <summary>What a foreach of the code goes over: the fight's players (CombatState.Players), its hittable enemies; null for anything else.</summary>
        private HookItems? ItemsOf(ProgramValue items) =>
            Unstored(items) is QueryValue { Name: "Players", Of: QueryValue { Name: "CombatState" } } ? HookItems.Players
            // The owner's side, unfiltered (foreach over CombatState.GetTeammatesOf(base.Owner): Tank's teammates given Guarded).
            : Unstored(items) is QueryValue { Name: "GetTeammatesOf", Arguments: [var whose] } && IsOwner(whose) ? HookItems.Teammates
            : IsHittableEnemies(items) ? HookItems.HittableEnemies : null;

        /// <summary>
        /// The owner's side filtered by a lambda (Where(CombatState.GetTeammatesOf(base.Owner), c => ...), a ToList of it or not, kept in a local or
        /// not): the lambda, else null.
        /// </summary>
        private static LambdaValue? TeammatesWhere(ProgramValue items)
        {
            var unstored = Unstored(items);
            if (unstored is QueryValue { Type: "Enumerable", Name: "ToList", Arguments: [var listed] }) unstored = Unstored(listed);
            return unstored is QueryValue { Type: "Enumerable", Name: "Where", Arguments: [QueryValue { Name: "GetTeammatesOf", Arguments: [var whose] }, LambdaValue filter] } && IsOwner(whose)
                ? filter : null;
        }

        /// <summary>A value as kept in a local or not (a StoredValue's value).</summary>
        private static ProgramValue Unstored(ProgramValue value) => value is StoredValue stored ? Unstored(stored.Value) : value;

        /// <summary>Its owner's Osty (base.Owner.Osty: its player's pet).</summary>
        private static bool IsOwnersOsty(ProgramValue value) => (value is StoredValue stored ? stored.Value : value) is QueryValue { Name: "Osty", Of: { } ostyOf } && IsOwner(ostyOf);

        /// <summary>
        /// Every living player creature of the fight (Where(CombatState.PlayerCreatures, c => c.IsAlive), a ToList of it or not, kept in a local or not):
        /// the fight's players, whoever's code asks.
        /// </summary>
        private static bool LivingPlayerCreatures(ProgramValue value)
        {
            var unstored = Unstored(value);
            if (unstored is QueryValue { Type: "Enumerable", Name: "ToList", Arguments: [var listed] }) unstored = Unstored(listed);
            return unstored is QueryValue { Type: "Enumerable", Name: "Where", Arguments: [QueryValue { Name: "PlayerCreatures", Of: QueryValue { Name: "CombatState" } }, LambdaValue alive] }
                && alive.Body.Terms is [[{ Holds: true, Test: QueryValue { Name: "IsAlive", Of: ParameterValue { Name: var tested } } }]] && tested == alive.Parameter;
        }

        /// <summary>
        /// A list of creatures the code works out, as what it goes over and its filter (each item HookWho.Each): a side's creatures (the side whose turn
        /// is ending, CombatState.GetCreaturesOnSide(side) in a turn's-end hook; its owner's, GetCreaturesOnSide(base.Owner.Side) or GetTeammatesOf),
        /// the hittable enemies, the creatures the hook is told (AfterDiedToDoom's creatures), a Where of one, ToList'd or not, kept in a local or not,
        /// or what a method of a model's class returns for the call's arguments (DoomPower.GetDoomedCreatures(creatures): a Where of them on each one's
        /// own Doom). Null for anything else.
        /// </summary>
        private (HookItems Over, HookValue? Where)? CreatureList(ProgramValue value)
        {
            var unstored = Unstored(value);
            // The hittable enemies, filtered on IsHittable or not (an orb's Where(GetOpponentsOf(base.Owner), e => e.IsHittable)).
            if (IsHittableEnemies(unstored)) return (HookItems.HittableEnemies, null);
            if (unstored is QueryValue { Type: "Enumerable", Name: "ToList", Arguments: [var listed] }) return CreatureList(listed);
            if (unstored is QueryValue { Type: "Enumerable", Name: "Where", Arguments: [var filtered, LambdaValue filter] })
            {
                if (CreatureList(filtered) is not { } inner) return null;
                var where = Condition(filter.Body.Map(part => part is ParameterValue { Name: var name } && name == filter.Parameter ? new NamedValue("each") : null)) ?? new HookConstant(1m);
                return (inner.Over, inner.Where == null ? where : Simplified(new HookMath(HookOperation.And, inner.Where, where)));
            }
            if (unstored is QueryValue { Name: "GetTeammatesOf", Arguments: [var whose] } && IsOwner(whose)) return (HookItems.Teammates, null);
            if (unstored is QueryValue { Name: "GetCreaturesOnSide", Arguments: [var side] })
            {
                if (Unstored(side) is QueryValue { Name: "Side", Of: { } sideOf } && IsOwner(sideOf)) return (HookItems.Teammates, null);
                if (Unstored(side) is ParameterValue { Name: "side" } or NamedValue { Name: "side" } && TurnEndHooks.Contains(_hook ?? "")) return (HookItems.SideEnding, null);
                return null;
            }
            if (unstored is ParameterValue { Name: "creatures" } or NamedValue { Name: "creatures" } && _hook == nameof(HookName.AfterDiedToDoom)) return (HookItems.Told, null);
            // A method of a model's class that gives a list (DoomPower.GetDoomedCreatures, static or its own): what its code returns, its parameters the
            // call's arguments.
            if (unstored is QueryValue { Name: var method, Arguments: var arguments, Type: var declaring } call && !call.IsProperty
                && (call.Of == null || call.Of is NamedValue { Name: "this" } && declaring == _type.Name) && _inlining.Add("list:" + declaring + "." + method))
            {
                try { return ListMethodReturned(declaring, method, arguments, call.Of == null) is { } returned ? CreatureList(returned) : null; }
                finally { _inlining.Remove("list:" + declaring + "." + method); }
            }
            return null;
        }

        /// <summary>The first creature of a list the code works out (First(list): HookFirst), or null for any other value.</summary>
        private HookFirst? FirstOfList(ProgramValue value) =>
            Unstored(value) is QueryValue { Type: "Enumerable", Name: "First", Arguments: [var list] } && CreatureList(list) is { } listed ? new HookFirst(listed.Over, listed.Where) : null;

        /// <summary>
        /// How many creatures of a list the code works out there are (list.Count), or pass a filter (Count(list, c => ...): Book Repair Knife's), as
        /// HookCount; null for any other value.
        /// </summary>
        private HookCount? CountOfList(ProgramValue value)
        {
            var unstored = Unstored(value);
            // The hittable enemies' count is HookFact.HittableEnemyCount's (the cases above it).
            if (unstored is QueryValue { Name: "Count", Of: { } countedList, Arguments.Count: 0 } && !IsHittableEnemies(countedList) && CreatureList(countedList) is { } counted)
                return new HookCount(counted.Over, counted.Where);
            if (unstored is QueryValue { Type: "Enumerable", Name: "Count", Arguments: [var list, LambdaValue filter] } && CreatureList(list) is { } listed)
            {
                var where = Condition(filter.Body.Map(part => part is ParameterValue { Name: var name } && name == filter.Parameter ? new NamedValue("each") : null)) ?? new HookConstant(1m);
                return new HookCount(listed.Over, listed.Where == null ? where : Simplified(new HookMath(HookOperation.And, listed.Where, where)));
            }
            return null;
        }

        /// <summary>A question of a power class's own (one of its <see cref="Questions"/>: DoomPower.IsOwnerDoomed).</summary>
        private bool IsPowerQuestion(string power, string question) =>
            _type.Assembly.GetType("MegaCrit.Sts2.Core.Models.Powers." + power) is { } powerType && Questions(powerType).Any(method => method.Name == question);

        /// <summary>
        /// A class's own questions another class's code may ask of it: its instance methods that take nothing and give a true-or-false, not a hook (not
        /// virtual) nor a property's getter (DoomPower.IsOwnerDoomed). Read into ModelSpec.Tests.
        /// </summary>
        internal static IEnumerable<MethodInfo> Questions(Type type) => type.GetMethods(Declared)
            .Where(method => !method.IsStatic && !method.IsVirtual && !method.IsSpecialName && !method.Name.StartsWith('<') && method.GetParameters().Length == 0 && method.ReturnType.Name == "Boolean");

        /// <summary>The turn's-end hooks, told the side whose turn is ending (their side parameter).</summary>
        private static readonly HashSet<string> TurnEndHooks = new(StringComparer.Ordinal) { "BeforeSideTurnEndVeryEarly", "BeforeSideTurnEndEarly", "BeforeSideTurnEnd", "AfterSideTurnEnd", "AfterSideTurnEndLate" };

        /// <summary>
        /// What a method of its own class or of a power class returns, read from its code, its parameters the call's arguments (its this its own), for a
        /// method that gives a list of creatures; null otherwise.
        /// </summary>
        private ProgramValue? ListMethodReturned(string declaring, string method, IReadOnlyList<ProgramValue> arguments, bool isStatic)
        {
            var owner = declaring == _type.Name ? _type : _type.Assembly.GetType("MegaCrit.Sts2.Core.Models.Powers." + declaring);
            if (owner == null) return null;
            var found = owner.GetMethods(Declared | BindingFlags.Static).FirstOrDefault(candidate => candidate.Name == method && candidate.IsStatic == isStatic
                && candidate.GetParameters().Length == arguments.Count && candidate.ReturnType.Name.StartsWith("IReadOnlyList", StringComparison.Ordinal));
            if (found == null) return null;
            try
            {
                var parameters = found.GetParameters().Select((parameter, index) => (parameter.Name!, arguments[index])).ToDictionary(pair => pair.Item1, pair => pair.Item2, StringComparer.Ordinal);
                return ProgramReader.ReadReturnedValue(owner, found, _read)?.Map(part => part switch
                {
                    ParameterValue { Name: var name } when parameters.TryGetValue(name, out var argument) => argument,
                    NamedValue { Name: var name } when parameters.TryGetValue(name, out var argument) => argument,
                    _ => null,
                });
            }
            catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException) { return null; }
        }

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
            // A card play's player (cardPlay.Player, its Creature: Stretch's): its card's owner, the player who played it.
            QueryValue { Type: "CardPlay", Name: "Player", Of: { } play } when IsHookPlay(play) => PlayedCard(play),
            QueryValue { Type: "Player", Name: "Creature", Of: QueryValue { Type: "CardPlay", Name: "Player", Of: { } play } } when IsHookPlay(play) => PlayedCard(play),
            _ => null,
        };

        /// <summary>The card of the card play the hook is given (cardPlay.Card).</summary>
        private static QueryValue PlayedCard(ProgramValue play) => new("CardPlay", "Card", Unstored(play), Array.Empty<ProgramValue>(), "CardModel") { IsProperty = true };

        /// <summary>
        /// The ways of a command's creatures when they're a local of several ways naming different creatures (each way's test, the argument's place,
        /// the creatures it names), or null: one creature, or ways the reader can't name.
        /// </summary>
        private List<(ProgramCondition When, int At, ProgramValue Creatures)>? CreatureWays(CommandStep command)
        {
            var types = command.Command.ParameterTypes;
            var at = IndexOf(types, "Creature", "IEnumerable`1");
            if (at < 0 || at >= command.Values.Count || Unstored(command.Values[at]) is not WaysValue ways || ways.Ways.Count < 2) return null;
            try
            {
                if (ways.Ways.Select(way => Targets(way.Value)).Distinct().Count() < 2) return null;
            }
            catch (Gap)
            {
                return null;
            }
            return ways.Ways.Select(way => (way.When, at, way.Value)).ToList();
        }

        /// <summary>An enchantment's own card (base.Card).</summary>
        private static bool IsModelCard(ProgramValue value) => Unstored(value) is QueryValue { Name: "Card", Of: NamedValue { Name: "this" }, Type: "EnchantmentModel" or "AfflictionModel" };

        /// <summary>Whom an enchantment's own card is aimed at (base.Card.TargetType).</summary>
        private static bool IsModelCardTargets(ProgramValue value) => Unstored(value) is QueryValue { Name: "TargetType", Of: { } card } && IsModelCard(card);

        /// <summary>The card play's target (cardPlay.Target), as an enchantment's OnPlay is given it.</summary>
        private bool IsPlayTarget(ProgramValue value) =>
            _kind == ModelKind.Enchantment && Unstored(value) is QueryValue { Name: "Target", Of: NamedValue { Name: "cardPlay" } or ParameterValue { Name: "cardPlay" } };

        /// <summary>The card played: cardPlay.Card, or a play-shape hook's card (ModifyCardPlayCount's card: the card whose play it decides).</summary>
        private bool IsPlayCard(ProgramValue value) =>
            value is QueryValue { Type: "CardPlay", Name: "Card", Of: NamedValue { Name: "cardPlay" } or ParameterValue { Name: "cardPlay" } }
            || value is NamedValue { Name: "card" } or ParameterValue { Name: "card" } && PlayShapeHooks.Contains(_hook);

        /// <summary>The hooks that decide a card play's shape before its plays, their card the one played (decision #192).</summary>
        private static readonly HashSet<string> PlayShapeHooks = new(StringComparer.Ordinal) { "ModifyCardPlayCount", "AfterModifyingCardPlayCount", "ModifyCardPlayResultLocation", "AfterModifyingCardPlayResultLocation" };

        private bool IsEventCard(ProgramValue value) => value is NamedValue { Name: "card" } or ParameterValue { Name: "card" } && _hook != "AfterCardPlayed" && !PlayShapeHooks.Contains(_hook);

        private static bool IsSourceCard(ProgramValue value) => value is ParameterValue { Name: "cardSource" } or NamedValue { Name: "cardSource" };

        /// <summary>An arrow's owner (arrow.owner: its player), or that player's creature (arrow.owner.Creature).</summary>
        private bool IsArrowOwner(ProgramValue value) =>
            value is QueryValue { Name: "owner", Of: { } first } && IsImpaledsFirstArrow(first) ||
            value is QueryValue { Name: "owner", Of: ParameterValue { Name: "arrow" } or NamedValue { Name: "arrow" } }
            || value is QueryValue { Type: "Player", Name: "Creature", Of: { } player } && IsArrowOwner(player);

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

        private static bool IsProps(ProgramValue value) => value is ParameterValue { Name: "props" } or NamedValue { Name: "props" }
            // An attack's props (BeforeAttack's and AfterAttack's command.DamageProps).
            || value is QueryValue { Name: "DamageProps", Of: ParameterValue { Name: "command" } or NamedValue { Name: "command" } };

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

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Assembly, string), IReadOnlyList<(string Name, long Value)>?> Enums = new();

    /// <summary>
    /// An enum of the game's on this branch's types, by name: its members' names and numbers, read from its fields' constants (the assembly may be
    /// read apart from the runtime; main's numbers can differ), read once; null where it has none by that name.
    /// </summary>
    private static IReadOnlyList<(string Name, long Value)>? EnumMembers(Assembly assembly, string enumType) =>
        Enums.GetOrAdd((assembly, enumType), key => key.Item1.GetTypes()
            .FirstOrDefault(candidate => candidate.IsEnum && candidate.Name == key.Item2 && candidate.Namespace?.StartsWith("MegaCrit", StringComparison.Ordinal) == true)
            ?.GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.IsLiteral)
            .Select(field => (field.Name, Convert.ToInt64(field.GetRawConstantValue(), System.Globalization.CultureInfo.InvariantCulture))).ToList());

    /// <summary>An enum member's name by its number (EnumMembers'); null where the enum or the number isn't there.</summary>
    private static string? EnumName(Assembly assembly, string enumType, long value) =>
        EnumMembers(assembly, enumType)?.FirstOrDefault(member => member.Value == value).Name;
}
