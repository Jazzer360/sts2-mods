namespace SpireQoL.Sim;

/// <summary>
/// What a hook is given (the game's hook parameters, as the simulator has them) and whose model answers it: the values a read model's
/// facts (HookFact) are read from. A struct, passed by reference: a hook call allocates nothing for it.
/// </summary>
internal readonly struct HookArgs
{
    public SimState State { get; init; }
    public int Owner { get; init; }
    /// <summary>The game hook being answered.</summary>
    public HookName Hook { get; init; }
    /// <summary>The card play (BeforeCardPlayed, AfterCardPlayed), else the play in progress.</summary>
    public SimCardPlay? Play { get; init; }
    /// <summary>The card whose enchantment runs (ModelKind.Enchantment: its base.Card), or null.</summary>
    public SimCard? Card { get; init; }
    /// <summary>A card event and who it happened to (AfterCardEvent).</summary>
    public CardEvent Event { get; init; }
    public int Player { get; init; }
    /// <summary>A power landing (AfterPowerApplied): which, how much, who applied it, on whom, as a debuff.</summary>
    public PowerId Power { get; init; }
    public int PowerAmount { get; init; }
    public int Applier { get; init; }
    public int Target { get; init; }
    public bool Debuff { get; init; }
    /// <summary>The hit a damage-time hook is told of (AfterDamageGiven, AfterDamageReceived).</summary>
    public DamageEvent? Hit { get; init; }
    /// <summary>The card play's count so far (ModifyCardPlayCount).</summary>
    public int PlayCount { get; init; }
    /// <summary>The HP the hit would take as an HP-loss pass starts (Hook.ModifyHpLost*'s amount).</summary>
    public decimal HpLost { get; init; }
    /// <summary>The damage or block being worked out (the Modify hooks).</summary>
    public DamageCtx? Damage { get; init; }
    public BlockCtx? Block { get; init; }
    /// <summary>The attack a BeforeAttack or AfterAttack is told of (its AttackCommand: id, attacker, props, card).</summary>
    public AttackInfo? Attack { get; init; }

    /// <summary>
    /// The answers of the code's tests this hook run has asked (HookTest, by its slot: 0 not yet, 1 held, 2 didn't), for a program that stores to
    /// its own state; null: every test is asked where it's needed.
    /// </summary>
    public int[]? Asked { get; init; }

    /// <summary>What this hook run took back of the amount its model bound to the play (HookFact.TakeBound, UnbindStep); null: it takes none.</summary>
    public int[]? Taken { get; init; }

    /// <summary>The copy of an instanced power this run is (ReadModel: its id among its owner's copies, from 1); 0 for a model kept as one.</summary>
    public int Copy { get; init; }

    /// <summary>The orb a hook is told of (ModifyOrbValue, the trigger count, AfterOrbEvoked), or the orb whose own program runs (Passive, Evoke).</summary>
    public OrbCtx Orb { get; init; }
    /// <summary>The orb whose own program runs, out of its slots (an evoke: OrbCmd removes it first); null: it's at Orb.Index.</summary>
    public SimOrb? Evoked { get; init; }
    /// <summary>The orb value so far (ModifyOrbValue's value), the trigger count so far (ModifyOrbPassiveTriggerCounts').</summary>
    public decimal OrbValue { get; init; }
    public int TriggerCount { get; init; }
    /// <summary>The item of the loop the step is in (HookWho.Each): a player's or an enemy's id.</summary>
    public int Each { get; init; }
    /// <summary>The locals this run kept (LetStep, by its slot); null: it keeps none.</summary>
    public decimal[]? Locals { get; init; }
    /// <summary>Asked of the players' next turn (a payout there: decision #239): the turn number is the next one.</summary>
    public bool NextTurn { get; init; }

    /// <summary>The same hook on another branch (its answers so far its own).</summary>
    public HookArgs On(SimState branch) => this with { State = branch, Asked = (int[]?)Asked?.Clone(), Taken = (int[]?)Taken?.Clone(), Locals = (decimal[]?)Locals?.Clone() };
}

/// <summary>
/// A power or a relic modelled from its own code (decision #195): its hooks' programs (ModelSpec, read from the game's code once per class by
/// the adapter) compiled once into delegates, and run where the simulator runs every model's hooks, in the game's listener order. Its
/// commands go through the same engine functions the hand-written models call (SimEngine.ProcBlock, ProcDamageAll, ProcCardEvent,
/// ApplyPower), so a read model logs, branches and merges as they did: what's shown from it is the same.
/// </summary>
public sealed class ReadModel : SimModel
{
    private delegate decimal Evaluate(in HookArgs args);

    /// <summary>One compiled command: the condition its code runs it on (null: always), and what it does on one branch.</summary>
    private sealed record CompiledStep(Evaluate? When, Func<HookArgs, IEnumerable<SimState>> Run);

    public ModelSpec Spec { get; }

    public override TabKind? Tab => Spec.Tab;

    /// <summary>Its code upgrades the card played (an UpgradeCardStep): the adapter reads that card's upgraded copy for a replay's later plays.</summary>
    public override bool UpgradesCardInPlay => _upgradesCardInPlay;
    private readonly bool _upgradesCardInPlay;

    /// <summary>The state its damage terms read (its Modify hooks' returns' fields: Slow's SlowAmount): a play that changes it acts on that play.</summary>
    private readonly HashSet<string> _termFields;

    /// <summary>
    /// Crossed by whose side its effect helps (decision #198): a play anyone makes setting off something that helps its owner (ModelSpec.HelpsOwner)
    /// is a warning only on an enemy, your play helping the other side; a teammate's (Sneaky's block on your Attack) helps yours. A play setting
    /// off something that hurts its owner (ModelSpec.HurtsOwner: Tender's debuffs on you) is a warning on a player, your side (decision #204).
    /// </summary>
    public override bool HarmfulFor(int owner) => GivesUpDuringTheTurn || Spec.HurtsPlayOwner || (Who.IsEnemy(owner) ? Spec.HelpsOwner : Spec.HurtsOwner);

    /// <summary>Its play tab says a play gives a bonus up (a payout the play decides: decision #239): crossed for every owner.</summary>
    public override bool Harmful => GivesUpDuringTheTurn;

    /// <summary>A play hook of its code turns it dark (ModelSpec.DarkensOnPlay): the play check holds its crossed tab to that.</summary>
    public override ActedSeen Seen => Spec.DarkensOnPlay ? ActedSeen.StatusCleared : ActedSeen.Flash;

    // ---- payouts (decision #239)

    /// <summary>Its payouts a play decides (Payout.DuringTheTurn), each compiled once: its condition during the turn, asked of its hook's turn.</summary>
    private readonly (Evaluate Condition, HookName? Hook, bool NextTurn)[] _duringTheTurn = Array.Empty<(Evaluate, HookName?, bool)>();

    /// <summary>
    /// Its payouts decided where they're paid, by their hook: each condition, asked as the engine runs the hook, and what it pays there (its giving
    /// command's amount, worked out as the hook starts: Cloak Clasp's block per card in hand; 1 for a command with no amount: decision #244).
    /// </summary>
    private readonly Dictionary<HookName, (Evaluate Condition, Evaluate Amount)[]> _paidWhere = new();

    /// <summary>The fields its payouts a play decides read: a play hook's store to one can give the bonus up.</summary>
    private readonly HashSet<string> _payoutFields = new(StringComparer.Ordinal);

    /// <summary>
    /// It has a payout decided where it's paid (Parrying Shield's block as your turn ends): its tab is about the end of your turn, never set off
    /// during the play (the play check leaves it to the forecast check).
    /// </summary>
    public bool PaysWhereItsDecided => _paidWhere.Count > 0;

    /// <summary>It has a payout a play decides: its tab is a crossed trigger on the play that gives it up.</summary>
    public bool GivesUpDuringTheTurn => _duringTheTurn.Length > 0;

    /// <summary>A payout a play decides reads the fight's log of card plays (Ripple Basin's): the engine asks it as it logs each play (SimEngine.RecordPlay).</summary>
    public bool PayoutReadsThePlayLog { get; }

    /// <summary>
    /// Some payout a play decides is still on for its owner (its condition during the turn holds): the bonus isn't given up yet. Asked before and
    /// after what can turn it off during the play (its own store, the fight's log of plays), so the play that does logs its crossed tab's firing.
    /// </summary>
    public bool PayoutOn(SimState state, int owner)
    {
        foreach (var (condition, hook, nextTurn) in _duringTheTurn)
        {
            var args = new HookArgs { State = state, Owner = owner, Player = owner, Hook = hook ?? default, NextTurn = nextTurn };
            if (condition(in args) != 0m) return true;
        }
        return false;
    }

    /// <summary>A payout that was on is off now: the play gave the bonus up (the crossed tab's firing, decision #239).</summary>
    public void GaveUp(SimState state, int owner, bool wasOn)
    {
        if (wasOn && !PayoutOn(state, owner)) state.LogFired(Name, amount: 0, owner);
    }

    /// <summary>
    /// A payout that was off is on now: the play set the bonus up (Parrying Shield's block reaching its threshold: its plain tab's firing, decision #244),
    /// logged under its set-up name (SimKeys.SetUp), apart from its own firings.
    /// </summary>
    public void SetUp(SimState state, int owner, bool wasOn)
    {
        if (!wasOn && PayoutOn(state, owner)) state.LogFired(SimKeys.SetUp(Name), amount: 0, owner);
    }

    /// <summary>What the play did to its payouts it decides, from whether one was on before (<paramref name="wasOn"/>): gave them up, or set one up.</summary>
    public void Decided(SimState state, int owner, bool wasOn)
    {
        if (wasOn) GaveUp(state, owner, wasOn);
        else SetUp(state, owner, wasOn);
    }

    /// <summary>
    /// The amount its payout waits for, a countdown's last tick (decision #248: The Bomb's BeforeSideTurnEnd deals its Damage at Amount &lt;= 1, and
    /// decrements before): the constant its code's first giving command's condition holds its amount at or under; null when it has none.
    /// </summary>
    public int? CountdownAt { get; }

    private static int? CountdownOf(ModelSpec spec)
    {
        static int? AtMost(HookValue? value) => value switch
        {
            HookMath { Operation: HookOperation.LessOrEqual, Left: HookFactValue { Fact: HookFact.Amount }, Right: HookConstant constant } => (int)constant.Value,
            HookMath math => AtMost(math.Left) ?? AtMost(math.Right),
            HookTest test => AtMost(test.Test),
            _ => null,
        };
        foreach (var step in spec.Steps())
            if (step is DamageStep or GainBlockStep or GainEnergyStep or DrawStep && AtMost(step.When) is { } at) return at;
        return null;
    }

    /// <summary>The key its payouts are kept under in a branch (SimState.Payouts): its source's log key.</summary>
    private string PaidKey(int owner) => SimKeys.Logged(Name, Who.KindOf(owner) == CreatureKind.You ? null : owner);

    private readonly CompiledStep[]?[] _steps = new CompiledStep[]?[Enum.GetValues<HookName>().Length];
    /// <summary>The hooks whose program stores to the model's own state: their tests are asked once a run (HookTest).</summary>
    private readonly bool[] _asksOnce = new bool[Enum.GetValues<HookName>().Length];
    /// <summary>Each test place's slot in HookArgs.Asked.</summary>
    private readonly Dictionary<string, int> _testSlots = new(StringComparer.Ordinal);
    private readonly Evaluate?[] _returns = new Evaluate?[Enum.GetValues<HookName>().Length];

    /// <summary>The hooks whose program takes back what the model bound to the play: each run gets a slot for it (HookArgs.Taken).</summary>
    private readonly bool[] _takes = new bool[Enum.GetValues<HookName>().Length];

    /// <summary>Its applications are copies, each with its own amount, state and applier (ModelSpec.Copies): each runs its hooks on its own.</summary>
    public bool HasCopies { get; }

    public override bool Instanced => Spec.Copies == PowerCopies.EachApplication;

    public ReadModel(ModelSpec spec) : base(spec.Name, spec.Hooks.Select(program => SimHookOf(program.Hook))
        .Append(spec.Outcome != null || spec.Payouts?.Any(payout => payout.DuringTheTurn == null) == true ? nameof(OutcomeTab) : "")
        .Concat(spec.Payouts?.Where(payout => payout.DuringTheTurn == null && Enum.TryParse<HookName>(payout.Hook, out _)).Select(payout => SimHookOf(Enum.Parse<HookName>(payout.Hook))) ?? Array.Empty<string>())
        .Where(hook => hook.Length > 0).Distinct())
    {
        Spec = spec;
        HasCopies = spec.Kind == ModelKind.Power && spec.Copies != PowerCopies.One;
        _copyPrefix = spec.Name + "@";
        _upgradesCardInPlay = spec.Steps().Any(step => step is UpgradeCardStep);
        CountdownAt = CountdownOf(spec);
        _termFields = spec.Hooks.Where(program => program.Returns != null && program.Hook is HookName.ModifyDamageAdditive or HookName.ModifyDamageMultiplicative or HookName.ModifyDamageMultiplicativeLate or HookName.ModifyDamageCap)
            .SelectMany(program => new ModelSpec(spec.Name, spec.Kind, new[] { program }).Values()).OfType<HookField>().Select(field => field.Field).ToHashSet(StringComparer.Ordinal);
        foreach (var test in spec.Values().OfType<HookTest>()) _testSlots.TryAdd(test.At, _testSlots.Count);
        foreach (var let in spec.Steps().OfType<LetStep>()) _localSlots.TryAdd(let.Name, _localSlots.Count);
        foreach (var payout in spec.Payouts ?? Array.Empty<Payout>())
        {
            HookName? hook = Enum.TryParse<HookName>(payout.Hook, out var parsed) ? parsed : null;
            if (payout.DuringTheTurn is { } during)
            {
                _duringTheTurn = _duringTheTurn.Append((Compile(during), hook, payout.NextTurn)).ToArray();
                _payoutFields.UnionWith(new ModelSpec(Name, spec.Kind, Array.Empty<HookProgram>()) { Payouts = new[] { new Payout(payout.Hook, during) } }.Values().OfType<HookField>().Select(field => field.Field));
                PayoutReadsThePlayLog |= new ModelSpec(Name, spec.Kind, Array.Empty<HookProgram>()) { Payouts = new[] { new Payout(payout.Hook, during) } }.Values().OfType<HookHistory>().Any();
            }
            else if (hook is { } paidIn && !payout.NextTurn)
                _paidWhere[paidIn] = (_paidWhere.TryGetValue(paidIn, out var those) ? those : Array.Empty<(Evaluate, Evaluate)>())
                    .Append((Compile(payout.Condition), Compile(PaidAmount(spec, paidIn, payout.Condition)))).ToArray();
            else throw new InvalidOperationException($"{Name}: a payout in {payout.Hook} that can't be judged from a simulation");
        }
        if (spec.Kind == ModelKind.Orb)
        {
            if (spec.Orb is not { } orbKind) throw new InvalidOperationException($"{spec.Name}: an orb with no kind");
            OrbKind = orbKind;
            _orbAliases = spec.OrbAliases ?? new Dictionary<string, string>();
            _orbStarts = spec.Starts ?? new Dictionary<string, decimal>();
            // In the getters' order of dependence: an EvokeVal may read PassiveVal (Glass's PassiveVal * 2), so PassiveVal first.
            _orbValues = (spec.OrbValues ?? new Dictionary<string, HookValue>()).OrderBy(pair => pair.Key == "PassiveVal" ? 0 : 1).ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => (pair.Key, Compile(pair.Value))).ToArray();
            foreach (var (field, start) in _orbStarts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (_orbAliases.TryGetValue(field, out var property)) _startValues = _startValues.Append((property, (int)start)).ToArray();
                else _startFields = _startFields.With(field, (int)start);
            }
        }
        foreach (var program in spec.Hooks)
        {
            var one = new ModelSpec(spec.Name, spec.Kind, new[] { program });
            // A take of what it bound is a test with an effect: asked once a run, as a store's tests are.
            _takes[(int)program.Hook] = one.Steps().Any(step => step is UnbindStep) || one.Values().Any(value => value is HookFactValue { Fact: HookFact.TakeBound or HookFact.BoundTaken });
            // A test that guards more than one step is asked once too: the game asks it once at its branch, though the first step it guards
            // changes what it tests (if (Block == 0) { GainBlock; GainEnergy }). One that guards a single step can't tell.
            _asksOnce[(int)program.Hook] = one.Steps().Any(step => step is StoreFieldStep) || _takes[(int)program.Hook] || GuardsSeveral(one);
            if (program.Returns != null) _returns[(int)program.Hook] = Compile(program.Returns);
            _steps[(int)program.Hook] = CompileSteps(program.Steps, new List<HookStep>(), firstRound: true, program.Hook);
        }
        foreach (var command in spec.Commands ?? Array.Empty<CommandProgram>())
        {
            var one = new ModelSpec(spec.Name, spec.Kind, new[] { new HookProgram(HookName.OwnCommand, command.Steps) });
            _commands[command.Method] = (CompileSteps(command.Steps, new List<HookStep>(), firstRound: true), one.Steps().Any(step => step is StoreFieldStep) || GuardsSeveral(one));
        }
    }

    /// <summary>
    /// A power class's amount as PowerCmd.Apply scales a new one on an enemy for the players (PowerRule.ScaledForPlayers: decision #234), compiled
    /// once: the state and the amount given, the scaled amount (before PowerModel.ApplyInternal's whole-number cast).
    /// </summary>
    internal static Func<SimState, int, decimal> CompileScaling(string power, HookValue scaled)
    {
        var evaluate = new ReadModel(new ModelSpec(power, ModelKind.Power, Array.Empty<HookProgram>())).Compile(scaled);
        return (state, amount) =>
        {
            var args = new HookArgs { State = state, Owner = Who.None, PowerAmount = amount };
            return evaluate(in args);
        };
    }

    // ---- an orb's own programs (ModelKind.Orb: decision #214)

    /// <summary>The orb kind this is the program of (ModelKind.Orb).</summary>
    public OrbKind OrbKind { get; }

    private readonly IReadOnlyDictionary<string, string> _orbAliases = new Dictionary<string, string>();
    private readonly (string Property, Evaluate Value)[] _orbValues = Array.Empty<(string, Evaluate)>();
    private readonly IReadOnlyDictionary<string, decimal> _orbStarts = new Dictionary<string, decimal>();

    /// <summary>Its passive goes off as its owner's turn ends (OrbQueue.BeforeTurnEnd), the one turn end a simulation covers.</summary>
    public bool PassiveAtTurnEnd => Spec.PassiveAt == OrbTrigger.TurnEnd;

    /// <summary>A new orb of this kind for <paramref name="player"/> (OrbCmd.Channel's ToMutable): its fields as they start, its values worked out.</summary>
    public SimOrb NewOrb(SimState state, int player)
    {
        var orb = new SimOrb { Kind = OrbKind, Fields = _startFields };
        foreach (var (property, start) in _startValues) SetOrbProperty(orb, property, start);
        ValuesAgain(state, player, index: -1, orb);
        return orb;
    }

    /// <summary>A new orb's own fields (its constructor's), and the values that are fields (Dark's EvokeVal, _evokeVal at 6): made once.</summary>
    private readonly NamedCounts _startFields = NamedCounts.Empty;
    private readonly (string Property, int Start)[] _startValues = Array.Empty<(string, int)>();

    /// <summary>
    /// An orb's values worked out again from its getters' code (ModelSpec.OrbValues: Hook.ModifyOrbValue on its base), as the game's getters work
    /// them out each time they're read: after a store to its own fields, a change to what the pipeline reads (Focus), as it's channelled.
    /// </summary>
    public void ValuesAgain(SimState state, int player, int index, SimOrb orb)
    {
        if (_orbValues.Length == 0) return;
        var args = new HookArgs { State = state, Owner = player, Orb = new OrbCtx(player, index, OrbKind), Evoked = orb };
        foreach (var (property, value) in _orbValues) SetOrbProperty(orb, property, (int)value(in args));
    }

    private static void SetOrbProperty(SimOrb orb, string property, int value)
    {
        if (property == "PassiveVal") orb.Passive = value;
        else if (property == "EvokeVal") orb.Evoke = value;
    }

    private static int OrbProperty(SimOrb orb, string property) => property == "PassiveVal" ? orb.Passive : property == "EvokeVal" ? orb.Evoke : 0;

    /// <summary>
    /// One of the orb's own programs (its Passive, as TriggerPassive runs it once per trigger; its Evoke, as OrbCmd runs it on the orb taken out of
    /// its slots), on the state given: <paramref name="index"/> its slot, or -1 with <paramref name="evoked"/> the orb out of them.
    /// </summary>
    public IEnumerable<SimState> RunOrb(HookName hook, SimState state, int player, int index, SimOrb? evoked)
    {
        if (_steps[(int)hook] is not { } steps) return One(state);
        var args = new HookArgs
        {
            State = state, Owner = player, Hook = hook, Play = state.CurrentPlay, Orb = new OrbCtx(player, index, OrbKind), Evoked = evoked,
            Asked = _asksOnce[(int)hook] ? new int[_testSlots.Count] : null, Locals = _localSlots.Count > 0 ? new decimal[_localSlots.Count] : null,
        };
        return RunSteps(steps, args);
    }

    // ---- an enchantment's own programs (ModelKind.Enchantment: decision #217)

    /// <summary>It has a program for the hook (an enchantment's OnPlay, its AfterCardPlayed).</summary>
    public bool Runs(HookName hook) => _steps[(int)hook] != null;

    /// <summary>
    /// One of the enchantment's programs on its own card's play (its OnPlay after the play's own effects, its AfterCardPlayed after the play's
    /// listeners: SimEngine.RunCardPlay), on the state given; its owner the card's.
    /// </summary>
    public IEnumerable<SimState> RunEnchantment(HookName hook, SimState state, SimCardPlay play)
    {
        if (_steps[(int)hook] is not { } steps) return One(state);
        return RunSteps(steps, new HookArgs
        {
            State = state, Owner = play.Owner, Hook = hook, Play = play, Card = play.Card,
            Asked = _asksOnce[(int)hook] ? new int[_testSlots.Count] : null, Locals = _localSlots.Count > 0 ? new decimal[_localSlots.Count] : null,
        });
    }

    /// <summary>
    /// The cards an enchantment's OnPlay draws for its card as it is now (Swift's Amount while it's on), its draws whose conditions hold on the
    /// card's own state: for a count outside a simulation (the Return tags' hand size). Its program's top-level draws only; 0 for any other model.
    /// </summary>
    public int DrawsOnPlay(SimCard card)
    {
        if (Spec.Kind != ModelKind.Enchantment || card.Enchantment == null) return 0;
        var draws = _drawsOnPlay ??= (Spec.Of(HookName.OnPlay)?.Steps ?? Array.Empty<HookStep>()).OfType<DrawStep>()
            .Select(draw => (When: draw.When == null ? null : Compile(draw.When), Count: Compile(draw.Count))).ToArray();
        if (draws.Length == 0) return 0;
        var args = new HookArgs { State = Unchanged, Owner = Who.You, Hook = HookName.OnPlay, Card = card };
        var total = 0;
        foreach (var (when, count) in draws)
            if (when == null || when(in args) != 0m) total += Math.Max(0, (int)count(in args));
        return total;
    }

    /// <summary>An enchantment's OnPlay draws, compiled once (DrawsOnPlay).</summary>
    private (Evaluate? When, Evaluate Count)[]? _drawsOnPlay;

    /// <summary>A state nothing changes, for a program's values asked outside a simulation (DrawsOnPlay: its card's own state is the card's).</summary>
    private static readonly SimState Unchanged = new();

    /// <summary>The enchantment's own field's key among its owner's: "Model#card.Field", its card's own (two cards with one enchantment keep apart).</summary>
    private string EnchantmentFieldKey(in HookArgs args, string field) =>
        _enchantmentKeys.GetOrAdd((args.Card?.HandId ?? 0, field), static (key, name) => name + "#" + key.Card.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + key.Field, Name);

    /// <summary>The enchantment's field keys, each made once (a run reads them on every play; the model is shared by every worker thread).</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int Card, string Field), string> _enchantmentKeys = new();

    /// <summary>The orb a run of its own program is: out of its slots (an evoke's), or in them at its index.</summary>
    private static SimOrb? OrbOf(in HookArgs args) =>
        args.Evoked ?? (args.State.Player(args.Orb.Player) is { } player && args.Orb.Index >= 0 && args.Orb.Index < player.Orbs.Count ? player.Orbs[args.Orb.Index] : null);

    /// <summary>The slots of the locals its programs keep (LetStep), by name.</summary>
    private readonly Dictionary<string, int> _localSlots = new(StringComparer.Ordinal);

    /// <summary>Its own commands another class calls (ModelSpec.Commands), compiled, and whether a run asks each test once.</summary>
    private readonly Dictionary<string, (CompiledStep[] Steps, bool AsksOnce)> _commands = new(StringComparer.Ordinal);

    /// <summary>It has this command of its own read (PoisonPower's Trigger).</summary>
    public bool HasCommand(string method) => _commands.ContainsKey(method);

    /// <summary>
    /// One of its own commands, called by another class on its owner's instance of it (Outbreak: PoisonPower.Trigger on each enemy's poison), run
    /// as its code runs it: on the state given, its owner's first copy for a power kept in copies (the instance GetPower gives).
    /// </summary>
    public IEnumerable<SimState> RunCommand(string method, SimState state, int owner)
    {
        if (!_commands.TryGetValue(method, out var command)) return One(state);
        var copies = HasCopies ? CopiesOrOne(state, owner) : AsOne;
        var copy = copies.Count > 0 ? copies[0] : 0;
        if (HasCopies && copy == 0) return One(state);
        return RunSteps(command.Steps, new HookArgs
        {
            State = state, Owner = owner, Play = state.CurrentPlay, Hook = HookName.OwnCommand, Copy = copy, Asked = command.AsksOnce ? new int[_testSlots.Count] : null,
            Locals = _localSlots.Count > 0 ? new decimal[_localSlots.Count] : null,
        });
    }

    /// <summary>One of the code's tests (by its place) is in the condition of more than one step of the program.</summary>
    private static bool GuardsSeveral(ModelSpec program)
    {
        static IEnumerable<string> Tests(HookValue? value) => value switch
        {
            HookTest test => new[] { test.At }.Concat(Tests(test.Test)),
            HookMath math => Tests(math.Left).Concat(Tests(math.Right)),
            HookNot not => Tests(not.Inner),
            _ => Array.Empty<string>(),
        };
        var places = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in program.Steps())
            foreach (var place in Tests(step.When).Distinct(StringComparer.Ordinal))
                if (!places.Add(place)) return true;
        return false;
    }

    /// <summary>
    /// Steps compiled in order. A hook that acts logs one firing (the hand-written models' Fire): a draw, energy, a power or an orb, once for the
    /// steps on one condition (<paramref name="firing"/>: the ones before); a counter loop's later rounds log none (<paramref name="firstRound"/>).
    /// </summary>
    private CompiledStep[] CompileSteps(IReadOnlyList<HookStep> steps, List<HookStep> firing, bool firstRound, HookName? hook = null)
    {
        var compiled = new List<CompiledStep>();
        foreach (var step in steps)
        {
            // A power an attack spends (its AfterAttack takes its own stacks: Gigantification's Decrement) acts on that play too (decision #228).
            // A play that changes the state its damage term reads (Slow's SlowAmount++ on every card) acts on that play too (decision #229).
            var acts = step is DrawStep or GainEnergyStep or GainStarsStep or SummonStep or ApplyPowerStep or ChannelStep or UpgradeCardStep or UpgradeRandomInHandStep or ApplyKeywordStep or FreeInHandStep or MakeCopyStep or MakeRandomInHandStep or MakeIntoDrawStep or StunStep or KillStep
                || hook == HookName.AfterAttack && step is DecrementPowerStep or RemovePowerStep
                || hook is HookName.BeforeCardPlayed or HookName.AfterCardPlayed && step is StoreFieldStep { Field: var termField } && _termFields.Contains(termField);
            // An enchantment's effects are its card's (no tab of its own, nothing in the play log: its card's play shows them).
            var fires = firstRound && acts && Spec.Kind != ModelKind.Enchantment && !firing.Any(earlier => Equals(earlier.When, step.When));
            if (acts) firing.Add(step);
            compiled.Add(new CompiledStep(step.When == null ? null : Compile(step.When), Runner(step, fires, firing)));
        }
        return compiled.ToArray();
    }

    /// <summary>The SimModel method a game hook is answered in (the card events' in AfterCardEvent, a power landing in AfterPowerApplied).</summary>
    public static string SimHookOf(HookName hook) => hook switch
    {
        HookName.BeforeCardPlayed => nameof(BeforeCardPlayed),
        HookName.AfterCardPlayed => nameof(AfterCardPlayed),
        HookName.AfterPowerAmountChanged => nameof(AfterPowerApplied),
        HookName.BeforeSideTurnEndVeryEarly => nameof(BeforeSideTurnEndVeryEarly),
        HookName.BeforeSideTurnEndEarly => nameof(BeforeSideTurnEndEarly),
        HookName.BeforeSideTurnEnd => nameof(BeforeSideTurnEnd),
        HookName.AfterSideTurnEnd => nameof(AfterSideTurnEnd),
        HookName.AfterSideTurnEndLate => nameof(AfterSideTurnEndLate),
        HookName.ModifyCardPlayCount => nameof(ModifyCardPlayCount),
        HookName.AfterModifyingCardPlayCount => nameof(AfterModifyingCardPlayCount),
        HookName.AfterDamageGiven => nameof(AfterDamageGiven),
        HookName.AfterDamageReceived => nameof(AfterDamageReceived),
        HookName.AfterDeath => nameof(AfterDeath),
        HookName.ModifyDamageAdditive => nameof(ModifyDamageAdditive),
        HookName.ModifyDamageMultiplicative => nameof(ModifyDamageMultiplicative),
        HookName.ModifyDamageMultiplicativeLate => nameof(ModifyDamageMultiplicativeLate),
        HookName.ModifyBlockAdditive => nameof(ModifyBlockAdditive),
        HookName.ModifyBlockMultiplicative => nameof(ModifyBlockMultiplicative),
        HookName.AfterApplied => nameof(AfterApplied),
        HookName.BeforeSideTurnStart => nameof(BeforeSideTurnStart),
        HookName.AfterSideTurnStart => nameof(AfterSideTurnStart),
        HookName.AfterSideTurnStartLate => nameof(AfterSideTurnStartLate),
        HookName.AfterOrbChanneled => nameof(AfterOrbChanneled),
        HookName.ModifyHpLostBeforeOsty => nameof(ModifyHpLostBeforeOsty),
        HookName.ModifyHpLostBeforeOstyLate => nameof(ModifyHpLostBeforeOstyLate),
        HookName.ModifyHpLostAfterOsty => nameof(ModifyHpLostAfterOsty),
        HookName.ModifyHpLostAfterOstyLate => nameof(ModifyHpLostAfterOstyLate),
        HookName.AfterModifyingHpLostBeforeOsty or HookName.AfterModifyingHpLostAfterOsty => nameof(AfterModifyingHpLost),
        HookName.ModifyOrbValue => nameof(ModifyOrbValue),
        HookName.ModifyOrbPassiveTriggerCounts => nameof(ModifyOrbPassiveTriggerCounts),
        HookName.AfterModifyingOrbPassiveTriggerCount => nameof(AfterModifyingOrbPassiveTriggerCount),
        HookName.AfterOrbEvoked => nameof(AfterOrbEvoked),
        HookName.BeforeAttack => nameof(BeforeAttack),
        HookName.AfterAttack => nameof(AfterAttack),
        HookName.ModifyDamageCap => nameof(ModifyDamageCap),
        HookName.AfterModifyingDamageAmount => nameof(AfterModifyingDamageAmount),
        HookName.BeforeDamageReceived => nameof(BeforeDamageReceived),
        HookName.AfterArrowShot => nameof(AfterArrowShot),
        HookName.AfterArrowImpaled => nameof(AfterArrowImpaled),
        HookName.ModifyPreBlockDamageTarget => nameof(ModifyPreBlockDamageTarget),
        HookName.ModifyUnblockedDamageTarget => nameof(ModifyUnblockedDamageTarget),
        HookName.ShouldAllowHitting => nameof(ShouldAllowHitting),
        HookName.ShouldCreatureBeRemovedFromCombatAfterDeath => nameof(ShouldCreatureBeRemovedFromCombatAfterDeath),
        // Its power's own question as its owner dies (Creature.RemoveAllPowersAfterDeath): asked of it, never of every listener.
        HookName.ShouldPowerBeRemovedAfterOwnerDeath => nameof(StaysOnDeadOwner),
        // An orb's own programs: the engine runs them where OrbCmd and OrbQueue do (RunOrb), never as a listener's hook; an enchantment's OnPlay
        // where its card plays (RunEnchantment).
        HookName.Passive or HookName.Evoke or HookName.OnPlay => "",
        _ => nameof(AfterCardEvent),
    };

    /// <summary>The game hook a card event is heard in (its After... hook); null for one no game hook hears (energy gained).</summary>
    public static HookName? HookOf(CardEvent cardEvent) => cardEvent switch
    {
        CardEvent.Drawn => HookName.AfterCardDrawn,
        CardEvent.Exhausted => HookName.AfterCardExhausted,
        CardEvent.Discarded => HookName.AfterCardDiscarded,
        CardEvent.Generated => HookName.AfterCardGeneratedForCombat,
        CardEvent.Shuffled => HookName.AfterShuffle,
        CardEvent.StarsSpent => HookName.AfterStarsSpent,
        CardEvent.StarsGained => HookName.AfterStarsGained,
        CardEvent.EnergySpent => HookName.AfterEnergySpent,
        CardEvent.ArrowDislodged => HookName.AfterArrowDislodged,
        _ => null,
    };

    // ---- the hooks

    public override IEnumerable<SimState> BeforeCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Run(HookName.BeforeCardPlayed, new HookArgs { State = state, Owner = owner, Play = play });

    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Run(HookName.AfterCardPlayed, new HookArgs { State = state, Owner = owner, Play = play });

    /// <summary>An attack about to make its hits, and one that made them (SimEngine.RunAttack: Hook.BeforeAttack, Hook.AfterAttack), in the play in progress.</summary>
    public override IEnumerable<SimState> BeforeAttack(SimState state, int owner, AttackInfo attack) =>
        Run(HookName.BeforeAttack, new HookArgs { State = state, Owner = owner, Play = state.CurrentPlay, Attack = attack });

    public override IEnumerable<SimState> AfterAttack(SimState state, int owner, AttackInfo attack) =>
        Run(HookName.AfterAttack, new HookArgs { State = state, Owner = owner, Play = state.CurrentPlay, Attack = attack });

    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player) =>
        HookOf(cardEvent) is { } hook ? Run(hook, new HookArgs { State = state, Owner = owner, Play = state.CurrentPlay, Event = cardEvent, Player = player }) : One(state);

    public override IEnumerable<SimState> AfterPowerApplied(SimState state, int owner, PowerId power, int amount, int applier, int target, bool debuff) =>
        Run(HookName.AfterPowerAmountChanged, new HookArgs
        {
            State = state, Owner = owner, Play = state.CurrentPlay, Power = power, PowerAmount = amount, Applier = applier, Target = target, Debuff = debuff,
        });

    public override IEnumerable<SimState> BeforeSideTurnEndVeryEarly(SimState state, int owner) => TurnEnd(HookName.BeforeSideTurnEndVeryEarly, state, owner);
    public override IEnumerable<SimState> BeforeSideTurnEndEarly(SimState state, int owner) => TurnEnd(HookName.BeforeSideTurnEndEarly, state, owner);
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState state, int owner) => TurnEnd(HookName.BeforeSideTurnEnd, state, owner);
    public override IEnumerable<SimState> AfterSideTurnEnd(SimState state, int owner) => TurnEnd(HookName.AfterSideTurnEnd, state, owner);
    public override IEnumerable<SimState> AfterSideTurnEndLate(SimState state, int owner) => TurnEnd(HookName.AfterSideTurnEndLate, state, owner);
    public override IEnumerable<SimState> BeforeSideTurnStart(SimState state, int owner) => TurnEnd(HookName.BeforeSideTurnStart, state, owner);
    public override IEnumerable<SimState> AfterSideTurnStart(SimState state, int owner) => TurnEnd(HookName.AfterSideTurnStart, state, owner);
    public override IEnumerable<SimState> AfterSideTurnStartLate(SimState state, int owner) => TurnEnd(HookName.AfterSideTurnStartLate, state, owner);

    /// <summary>Hook.AfterOrbChanneled: an orb was channeled for <paramref name="player"/> (the hook's player, EventIsOwners).</summary>
    public override IEnumerable<SimState> AfterOrbChanneled(SimState state, int owner, int player) =>
        Run(HookName.AfterOrbChanneled, new HookArgs { State = state, Owner = owner, Play = state.CurrentPlay, Player = player });

    /// <summary>Hook.ModifyOrbValue: the value of its code's first way that holds (Focus: max(value + Amount, 0) on its owner's orbs).</summary>
    public override decimal ModifyOrbValue(SimState state, int owner, OrbCtx orb, decimal value)
    {
        if (_returns[(int)HookName.ModifyOrbValue] is not { } returns) return value;
        foreach (var copy in HasCopies ? CopiesOrOne(state, owner) : AsOne)
        {
            var args = new HookArgs { State = state, Owner = owner, Hook = HookName.ModifyOrbValue, Orb = orb, OrbValue = value, Copy = copy };
            value = returns(in args);
        }
        return value;
    }

    /// <summary>Hook.ModifyOrbPassiveTriggerCount: the value of its code's first way that holds (Gold-Plated Cables: one more on its owner's front orb).</summary>
    public override int ModifyOrbPassiveTriggerCounts(SimState state, int owner, OrbCtx orb, int count)
    {
        if (_returns[(int)HookName.ModifyOrbPassiveTriggerCounts] is not { } returns) return count;
        foreach (var copy in HasCopies ? CopiesOrOne(state, owner) : AsOne)
        {
            var args = new HookArgs { State = state, Owner = owner, Hook = HookName.ModifyOrbPassiveTriggerCounts, Orb = orb, TriggerCount = count, Copy = copy };
            count = (int)returns(in args);
        }
        return count;
    }

    /// <summary>Hook.AfterModifyingOrbPassiveTriggerCount: it changed the count (it acted), then its code's steps (a flash), on the state given.</summary>
    public override void AfterModifyingOrbPassiveTriggerCount(SimState state, int owner, OrbCtx orb)
    {
        state.LogFired(Name, amount: 0, owner);
        foreach (var branch in Run(HookName.AfterModifyingOrbPassiveTriggerCount, new HookArgs { State = state, Owner = owner, Play = state.CurrentPlay, Orb = orb }))
            if (!ReferenceEquals(branch, state)) throw new InvalidOperationException($"{Name}: AfterModifyingOrbPassiveTriggerCount branched");
    }

    /// <summary>Hook.AfterOrbEvoked: an orb was evoked, its targets SimState.OrbTargets (Thunder's damage on the living ones).</summary>
    public override IEnumerable<SimState> AfterOrbEvoked(SimState state, int owner, OrbCtx orb) =>
        Run(HookName.AfterOrbEvoked, new HookArgs { State = state, Owner = owner, Play = state.CurrentPlay, Orb = orb, Player = orb.Player });

    /// <summary>Hook.ModifyCardPlayCount: the value of its code's first way that holds (playCount, playCount + 1...), decided once before the plays.</summary>
    public override int ModifyCardPlayCount(SimState state, int owner, SimCardPlay play, int count)
    {
        if (_returns[(int)HookName.ModifyCardPlayCount] is not { } returns) return count;
        // Each copy in turn on what the one before it gave (each is a listener of its own in the game).
        foreach (var copy in CopiesOrOne(state, owner))
        {
            var args = new HookArgs { State = state, Owner = owner, Hook = HookName.ModifyCardPlayCount, Play = play, PlayCount = count, Copy = copy };
            count = (int)returns(in args);
        }
        return count;
    }

    /// <summary>
    /// A new instance of it landed on its owner (PowerModel.AfterApplied, once it's on: SimEngine.ApplyPower): deterministic, on the state
    /// given; for a power kept in copies, the copy that landed (<see cref="Land"/>).
    /// </summary>
    public override void AfterApplied(SimState state, int owner, int applier, int amount) => AfterApplied(state, owner, applier, amount, copy: 0);

    public void AfterApplied(SimState state, int owner, int applier, int amount, int copy)
    {
        if (_steps[(int)HookName.AfterApplied] is not { } steps) return;
        var args = new HookArgs { State = state, Owner = owner, Hook = HookName.AfterApplied, Play = state.CurrentPlay, Applier = applier, PowerAmount = amount, Copy = copy };
        args = args with { Asked = _asksOnce[(int)HookName.AfterApplied] ? new int[_testSlots.Count] : null, Taken = _takes[(int)HookName.AfterApplied] ? new int[1] : null };
        foreach (var branch in RunSteps(steps, args))
            if (!ReferenceEquals(branch, state)) throw new InvalidOperationException($"{Name}: AfterApplied branched");
    }

    /// <summary>After it changed a card play's count (a stack used: PowerCmd.Decrement; Throwing Axe's UsedThisCombat): deterministic, on the state given.</summary>
    public override void AfterModifyingCardPlayCount(SimState state, int owner, SimCardPlay play)
    {
        foreach (var branch in Run(HookName.AfterModifyingCardPlayCount, new HookArgs { State = state, Owner = owner, Play = play }))
            if (!ReferenceEquals(branch, state)) throw new InvalidOperationException($"{Name}: AfterModifyingCardPlayCount branched");
    }

    /// <summary>A hit it dealt or took (inside the damage: SimEngine.Damage): deterministic, on the state given, as the hand-written models' are.</summary>
    public override void AfterDamageGiven(SimState state, int owner, DamageEvent hit) => InDamage(HookName.AfterDamageGiven, state, owner, hit);
    public override void AfterDamageReceived(SimState state, int owner, DamageEvent hit) => InDamage(HookName.AfterDamageReceived, state, owner, hit);

    /// <summary>A player tried a shot (Hook.AfterArrowShot, SimEngine.RunShoot): its shooter the hook's player, the shot's arrow SimState.ArrowAwayAt.</summary>
    public override IEnumerable<SimState> AfterArrowShot(SimState state, int owner, int shooter) =>
        Run(HookName.AfterArrowShot, new HookArgs { State = state, Owner = owner, Play = state.CurrentPlay, Player = shooter });

    /// <summary>A player's arrow impaled <paramref name="target"/> (Hook.AfterArrowImpaled, SimEngine.RunImpale): its shooter the hook's player.</summary>
    public override IEnumerable<SimState> AfterArrowImpaled(SimState state, int owner, int target, int shooter) =>
        Run(HookName.AfterArrowImpaled, new HookArgs { State = state, Owner = owner, Play = state.CurrentPlay, Player = shooter, Target = target });

    /// <summary>
    /// A damage worked out, about to meet its target's block (Hook.BeforeDamageReceived: Thorns hits the dealer back), on any creature, inside the
    /// damage: deterministic, on the state given.
    /// </summary>
    public override void BeforeDamageReceived(SimState state, int owner, DamageCtx damage, int amount)
    {
        foreach (var branch in Run(HookName.BeforeDamageReceived, new HookArgs { State = state, Owner = owner, Play = state.CurrentPlay, Damage = damage, Target = damage.Target }))
            if (!ReferenceEquals(branch, state)) throw new InvalidOperationException($"{Name}: BeforeDamageReceived branched inside a hit");
    }

    /// <summary>A hook told inside a hit or a death (on the state given: its run can't branch).</summary>
    private static bool InHit(HookName hook) => hook is HookName.AfterDamageGiven or HookName.AfterDamageReceived or HookName.AfterDeath or HookName.BeforeDamageReceived;

    private void InDamage(HookName hook, SimState state, int owner, DamageEvent hit)
    {
        foreach (var branch in Run(hook, new HookArgs { State = state, Owner = owner, Play = state.CurrentPlay, Hit = hit, Target = hit.Target }))
            if (!ReferenceEquals(branch, state)) throw new InvalidOperationException($"{Name}: {hook} branched inside a hit");
    }

    /// <summary>A creature died (AfterDeath, once per death, as the engine tells it: SimEngine.AllyDeaths): deterministic, on the state given.</summary>
    public override void AfterDeath(SimState state, int owner, int dead)
    {
        foreach (var branch in Run(HookName.AfterDeath, new HookArgs { State = state, Owner = owner, Play = state.CurrentPlay, Target = dead }))
            if (!ReferenceEquals(branch, state)) throw new InvalidOperationException($"{Name}: AfterDeath branched");
    }

    /// <summary>
    /// Its tab judged on how the turn ends, by what its AfterDeath waits on (ModelSpec.Outcome): an enemy's own death or an ally's, a warning on a
    /// play that kills it or another enemy; a death on your side's other side, your bonus on a play that kills an enemy in every outcome.
    /// </summary>
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) => Spec.Outcome switch
    {
        HookOutcome.KillsIt => Who.IsEnemy(owner) ? Models.OutcomeRules.KillsIt(owner) : null,
        HookOutcome.KillsAnother => Who.IsEnemy(owner) ? Models.OutcomeRules.KillsAnother(owner) : null,
        HookOutcome.KillsOne => owner == Who.You ? Models.OutcomeRules.KillsOne : null,
        HookOutcome.ReactsToHpLoss => ReactsToHpLoss(now, owner),
        HookOutcome.Stuns => Models.OutcomeRules.Stuns(now, owner),
        HookOutcome.KillsItself => Who.IsEnemy(owner) && !now.KilledByItsPowerInAll(owner) ? new(Harmful: false, outcome => outcome.KilledByItsPowerInAll(owner)) : null,
        _ => _paidWhere.Count > 0 ? PaidWhere(now, owner) : null,
    };

    /// <summary>
    /// Its payout decided where it's paid (decision #239), yours: ending the turn now, does it pay in every outcome? If not, a bonus where the play
    /// makes it pay in every outcome (it sets it up); if so, a warning where the play stops it in some outcome (it gives it up), with the chance of
    /// those. Set off after the play (as your turn ends), so the play check can't see it. Not a teammate's: the play's bonus is its own player's.
    /// Each branch's change against ending the turn now is told too (OutcomeRule.Change, decision #244): on the bonus's side, it pays where ending
    /// now doesn't in every outcome (sets it up); on the warning's, it pays nothing (gives it up) or less than ending now pays in every outcome
    /// (makes it smaller: Cloak Clasp's block for a card fewer in hand). The source's settings row shows the changes it names.
    /// </summary>
    private OutcomeRule? PaidWhere(SimOutcome now, int owner)
    {
        if (owner != Who.You) return null;
        var key = PaidKey(owner);
        bool Paid(SimState branch) => branch.Payouts.Has(key);
        if (!now.InEvery(Paid))
            return new OutcomeRule(Harmful: false, outcome => outcome.InEvery(Paid), InPlay: false, Change: branch => Paid(branch) ? TabShows.SetsUp : TabShows.None);
        var least = now.Branches.Count == 0 ? 0 : now.Branches.Min(branch => branch.Payouts.DataOf(key));
        return new OutcomeRule(Harmful: true, outcome => !outcome.InEvery(Paid), branch => !Paid(branch), InPlay: false,
            Change: branch => !Paid(branch) ? TabShows.GivesUp | TabShows.Reduces : branch.Payouts.DataOf(key) < least ? TabShows.Reduces : TabShows.None);
    }

    /// <summary>
    /// What a payout decided where it's paid pays (decision #244): the amount of its hook's first giving command on its condition (block, damage,
    /// energy, cards drawn, a heal, stars, a power's amount), as its code works it out; 1 for one with no amount (a channel). Kept with the payout in
    /// each branch, so a play that makes it smaller shows.
    /// </summary>
    private static HookValue PaidAmount(ModelSpec spec, HookName hook, HookValue condition)
    {
        foreach (var step in spec.Of(hook)?.Steps ?? (IReadOnlyList<HookStep>)Array.Empty<HookStep>())
        {
            if (!Equals(step.When, condition)) continue;
            switch (step)
            {
                case GainBlockStep gain: return gain.Amount;
                case DamageStep damage: return damage.Amount;
                case GainEnergyStep energy: return energy.Amount;
                case DrawStep draw: return draw.Count;
                case HealStep heal: return heal.Amount;
                case SummonStep summon: return summon.Amount;
                case GainStarsStep stars: return stars.Amount;
                case ApplyPowerStep apply: return apply.Amount;
            }
        }
        return new HookConstant(1m);
    }

    /// <summary>
    /// Its reaction to its owner losing HP (HookOutcome.ReactsToHpLoss, decision #239), as its acting or flash log says it acted: on you, a bonus when
    /// the play sets it off in every outcome (you lose HP during the play); on an enemy, a warning when the play sets it off in some outcome of the turn
    /// and ending the turn now sets it off in none, with the chance of the outcomes that do. Not a teammate's: the play's HP loss is its own player's.
    /// </summary>
    private OutcomeRule? ReactsToHpLoss(SimOutcome now, int owner)
    {
        // On you, a reaction that spends itself (Slippery's charge: its AfterDamageReceived's Decrement) gives you nothing for the HP: no bonus.
        if (owner == Who.You)
            return Spec.Of(HookName.AfterDamageReceived)?.Steps.Any(step => step is DecrementPowerStep or RemovePowerStep) == true ? null
                : new OutcomeRule(Harmful: false, outcome => outcome.PlayFiredEvery.Has(Name) || outcome.PlayFlashedEvery.Has(Name));
        if (!Who.IsEnemy(owner)) return null;
        bool Acted(SimState branch) => SimOutcome.Acted(branch, Name, owner);
        return now.ActedInAny(Name, owner) ? null : new OutcomeRule(Harmful: true, outcome => outcome.ActedInAny(Name, owner), Acted);
    }

    /// <summary>It keeps its applier as its Data (its code asks base.Applier): the engine lands it so (SimEngine.ApplyPower). Worked out once.</summary>
    public bool UsesApplier => _usesApplier ??= Spec.UsesApplier();
    private bool? _usesApplier;

    /// <summary>The classes of the cards its code makes into a draw pile (MakeIntoDrawStep: Biiig Hug's Soot): the adapter reads their profiles for a later draw. Worked out once.</summary>
    public IReadOnlyList<string> CardsMadeIntoDraw => _cardsMadeIntoDraw ??= Spec.Steps().OfType<MakeIntoDrawStep>().Select(make => make.Card).Distinct(StringComparer.Ordinal).ToArray();
    private string[]? _cardsMadeIntoDraw;

    /// <summary>
    /// Its own fields its programs read or store (ModelSpec.Fields), and each one's key among its owner's (FieldKey), worked out once: the
    /// adapter seeds them every time it reads the board or a card, and walking the spec each time made garbage on the game's thread.
    /// </summary>
    public IReadOnlyList<(string Field, string Key)> OwnFields => _ownFields ??= Spec.Fields().Select(field => (field, FieldKey(field))).ToArray();
    private (string Field, string Key)[]? _ownFields;

    /// <summary>A hook of the players' side's turn ending (decision #51), in its place among them.</summary>
    private IEnumerable<SimState> TurnEnd(HookName hook, SimState state, int owner)
    {
        var args = new HookArgs { State = state, Owner = owner, Play = state.CurrentPlay };
        // A payout decided where it's paid (Parrying Shield's block as your turn ends): whether its condition holds here, kept in the branch for the
        // tab judged on how the turn ends (decision #239). Its tests are asked as the hook starts, before any of its steps.
        if (_paidWhere.TryGetValue(hook, out var payouts))
        {
            var asked = args with { Hook = hook };
            foreach (var (condition, amount) in payouts)
                if (condition(in asked) != 0m)
                {
                    // What it pays there too, so a play that makes it smaller shows (decision #244).
                    state.Payouts = state.Payouts.With(PaidKey(owner), 1, data: (int)amount(in asked));
                    break;
                }
        }
        return Run(hook, args);
    }

    public override decimal ModifyDamageAdditive(SimState state, int owner, DamageCtx damage) => Returned(HookName.ModifyDamageAdditive, state, owner, damage, otherwise: 0m);
    public override decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage) => Returned(HookName.ModifyDamageMultiplicative, state, owner, damage, otherwise: 1m);
    public override decimal ModifyDamageMultiplicativeLate(SimState state, int owner, DamageCtx damage) => Returned(HookName.ModifyDamageMultiplicativeLate, state, owner, damage, otherwise: 1m);
    public override decimal ModifyDamageCap(SimState state, int owner, DamageCtx damage) => Returned(HookName.ModifyDamageCap, state, owner, damage, otherwise: decimal.MaxValue);

    /// <summary>
    /// Its term changed a damage (the game calls Hook.AfterModifyingDamageAmount on such listeners): it acts there, logged as the hand-written
    /// models' FlashesOnDamageModified did, then its code's steps (a flash), inside the damage. A model whose code doesn't answer it does nothing.
    /// </summary>
    public override void AfterModifyingDamageAmount(SimState state, int owner, SimCard? card)
    {
        if (_steps[(int)HookName.AfterModifyingDamageAmount] == null)
        {
            // Its code answers nothing here; its term changing the damage is what it does (ModelSpec.TermActs, decision #247: Conqueror).
            if (Spec.TermActs) state.LogFired(Name, amount: 0, owner);
            return;
        }
        if (Spec.Outcome == null) state.LogFired(Name, amount: 0, owner); // its acting log is its play tab's: one judged on the outcome has none
        foreach (var branch in Run(HookName.AfterModifyingDamageAmount, new HookArgs { State = state, Owner = owner, Play = state.CurrentPlay }))
            if (!ReferenceEquals(branch, state)) throw new InvalidOperationException($"{Name}: AfterModifyingDamageAmount branched inside a damage");
    }
    public override decimal ModifyBlockAdditive(SimState state, int owner, BlockCtx block) => Returned(HookName.ModifyBlockAdditive, state, owner, block, otherwise: 0m);

    /// <summary>Hook.ModifyPreBlockDamageTarget: its code's returned creature (Wuppy's Loyalty: its own Wuppy for a powered attack on its player while it lives).</summary>
    public override int ModifyPreBlockDamageTarget(SimState state, int owner, DamageCtx damage, int targetSoFar, decimal amount) =>
        _returns[(int)HookName.ModifyPreBlockDamageTarget] is { } returns
            ? (int)returns(new HookArgs { State = state, Owner = owner, Hook = HookName.ModifyPreBlockDamageTarget, Damage = damage, Play = damage.Play, Target = targetSoFar, HpLost = amount })
            : targetSoFar;

    /// <summary>Hook.ModifyUnblockedDamageTarget: its code's returned creature (Die For You: its own Osty for a powered attack on his player while he lives).</summary>
    public override int ModifyUnblockedDamageTarget(SimState state, int owner, DamageCtx damage, decimal amount) =>
        _returns[(int)HookName.ModifyUnblockedDamageTarget] is { } returns
            ? (int)returns(new HookArgs { State = state, Owner = owner, Hook = HookName.ModifyUnblockedDamageTarget, Damage = damage, Play = damage.Play, Target = damage.Target, HpLost = amount })
            : damage.Target;

    /// <summary>Hook.ShouldAllowHitting of <paramref name="creature"/>: its code's answer (Die For You's and Wuppy's Loyalty's: whether it lives).</summary>
    public override bool ShouldAllowHitting(SimState state, int owner, int creature) =>
        _returns[(int)HookName.ShouldAllowHitting] is not { } returns || returns(new HookArgs { State = state, Owner = owner, Hook = HookName.ShouldAllowHitting, Target = creature }) != 0m;

    /// <summary>Hook.ShouldCreatureBeRemovedFromCombatAfterDeath of <paramref name="creature"/>: its code's answer (Die For You keeps its own Osty).</summary>
    public override bool ShouldCreatureBeRemovedFromCombatAfterDeath(SimState state, int owner, int creature) =>
        _returns[(int)HookName.ShouldCreatureBeRemovedFromCombatAfterDeath] is not { } returns
        || returns(new HookArgs { State = state, Owner = owner, Hook = HookName.ShouldCreatureBeRemovedFromCombatAfterDeath, Target = creature }) != 0m;

    /// <summary>Its ShouldPowerBeRemovedAfterOwnerDeath says no: it stays on its dead owner (Die For You, Wuppy's Loyalty).</summary>
    public override bool StaysOnDeadOwner(SimState state, int owner) =>
        _returns[(int)HookName.ShouldPowerBeRemovedAfterOwnerDeath] is { } returns && returns(new HookArgs { State = state, Owner = owner, Hook = HookName.ShouldPowerBeRemovedAfterOwnerDeath }) == 0m;
    public override decimal ModifyBlockMultiplicative(SimState state, int owner, BlockCtx block) => Returned(HookName.ModifyBlockMultiplicative, state, owner, block, otherwise: 1m);

    public override decimal ModifyHpLostBeforeOsty(SimState state, int owner, DamageCtx damage, decimal amount) => HpLost(HookName.ModifyHpLostBeforeOsty, state, owner, damage, amount);
    public override decimal ModifyHpLostBeforeOstyLate(SimState state, int owner, DamageCtx damage, decimal amount) => HpLost(HookName.ModifyHpLostBeforeOstyLate, state, owner, damage, amount);
    public override decimal ModifyHpLostAfterOsty(SimState state, int owner, DamageCtx damage, decimal amount) => HpLost(HookName.ModifyHpLostAfterOsty, state, owner, damage, amount);
    public override decimal ModifyHpLostAfterOstyLate(SimState state, int owner, DamageCtx damage, decimal amount) => HpLost(HookName.ModifyHpLostAfterOstyLate, state, owner, damage, amount);

    /// <summary>
    /// An HP-loss pass (Hook.ModifyHpLost*): its code's return on the HP the hit would take, each copy a listener of its own, the amount passed from
    /// one to the next as the game's pipeline passes it.
    /// </summary>
    private decimal HpLost(HookName hook, SimState state, int owner, DamageCtx damage, decimal amount)
    {
        if (_returns[(int)hook] is not { } returns) return amount;
        foreach (var copy in HasCopies ? CopiesOrOne(state, owner) : AsOne)
        {
            var args = new HookArgs { State = state, Owner = owner, Hook = hook, Damage = damage, Play = damage.Play, Target = damage.Target, HpLost = amount, Copy = copy };
            amount = returns(in args);
        }
        return amount;
    }

    /// <summary>
    /// Its pass changed the HP lost (Hook.AfterModifyingHpLostBeforeOsty or AfterOsty, told to each model that did): it acted (its acting log, as every
    /// model that changes an HP loss), then its code's after hook (a flash), on the state given.
    /// </summary>
    public override void AfterModifyingHpLost(SimState state, int owner, bool afterOsty)
    {
        if (Spec.Outcome == null) state.LogFired(Name, amount: 0, owner); // its acting log is its play tab's: one judged on the outcome has none
        var hook = afterOsty ? HookName.AfterModifyingHpLostAfterOsty : HookName.AfterModifyingHpLostBeforeOsty;
        foreach (var branch in Run(hook, new HookArgs { State = state, Owner = owner, Play = state.CurrentPlay }))
            if (!ReferenceEquals(branch, state)) throw new InvalidOperationException($"{Name}: {hook} branched inside a hit");
    }

    private decimal Returned(HookName hook, SimState state, int owner, DamageCtx damage, decimal otherwise)
    {
        if (_returns[(int)hook] is not { } returns) return otherwise;
        if (!HasCopies)
        {
            var args = new HookArgs { State = state, Owner = owner, Damage = damage, Play = damage.Play };
            return returns(in args);
        }
        // Each copy a listener of its own: their terms added (an additive hook) or multiplied (a multiplicative one), as the game's pipeline does.
        var total = otherwise;
        foreach (var copy in CopiesOrOne(state, owner))
        {
            var args = new HookArgs { State = state, Owner = owner, Damage = damage, Play = damage.Play, Copy = copy };
            total = hook == HookName.ModifyDamageAdditive ? total + returns(in args) : hook == HookName.ModifyDamageCap ? Math.Min(total, returns(in args)) : total * returns(in args);
        }
        return total;
    }

    private decimal Returned(HookName hook, SimState state, int owner, BlockCtx block, decimal otherwise)
    {
        if (_returns[(int)hook] is not { } returns) return otherwise;
        if (!HasCopies)
        {
            var args = new HookArgs { State = state, Owner = owner, Block = block, Play = block.Play };
            return returns(in args);
        }
        var total = otherwise;
        foreach (var copy in CopiesOrOne(state, owner))
        {
            var args = new HookArgs { State = state, Owner = owner, Block = block, Play = block.Play, Copy = copy };
            total = hook == HookName.ModifyBlockAdditive ? total + returns(in args) : total * returns(in args);
        }
        return total;
    }

    /// <summary>
    /// A hook's commands in order, each on its own condition asked as it runs (a store before it is seen), each on every branch the ones before
    /// it left: a random target branches, and identical branches merge (SimEngine.Then).
    /// </summary>
    private IEnumerable<SimState> Run(HookName hook, HookArgs args)
    {
        if (_steps[(int)hook] is not { } steps) return One(args.State);
        args = args with
        {
            Hook = hook, Asked = _asksOnce[(int)hook] ? new int[_testSlots.Count] : null, Taken = _takes[(int)hook] ? new int[1] : null,
            Locals = _localSlots.Count > 0 ? new decimal[_localSlots.Count] : null,
        };
        if (!HasCopies) return RunSteps(steps, args);
        // Each copy runs the hook on its own, in the order they landed (each a listener of its own in the game), on every branch the one before
        // it left; a copy a branch no longer has (it went) runs nothing there.
        var copies = CopiesOrOne(args.State, args.Owner);
        IEnumerable<SimState> states = One(args.State);
        foreach (var copy in copies)
        {
            var start = args;
            states = SimEngine.Then(states, branch => HasCopy(branch, start.Owner, copy)
                ? RunSteps(steps, start with
                {
                    State = branch, Copy = copy, Asked = start.Asked == null ? null : new int[start.Asked.Length], Taken = start.Taken == null ? null : new int[1],
                    Locals = start.Locals == null ? null : new decimal[start.Locals.Length],
                })
                : One(branch));
        }
        return states;
    }

    // ---- copies (an instanced power: SimPlayer.Fields / SimEnemy.Fields hold each one as "Name@id", its amount and its applier as Data, and its own
    // state as "Name@id.field"; none listed: the power's entry is the one copy, id 1)

    private readonly string _copyPrefix;

    /// <summary>The ids of its owner's copies, in the order they landed (the power's entry alone: one, id 1); none when the owner has no such power.</summary>
    public IReadOnlyList<int> CopiesOrOne(SimState state, int owner)
    {
        if (!HasCopies) return AsOne;
        var listed = ListedCopies(SimEngine.FieldsOf(state, owner));
        return listed.Count > 0 ? listed : SimEngine.PowersOf(state, owner).Has(ThisPower) ? OneCopy : Array.Empty<int>();
    }

    private static readonly int[] OneCopy = { 1 };

    /// <summary>A model kept as one: its single run, copy 0.</summary>
    private static readonly int[] AsOne = { 0 };

    private List<int> ListedCopies(NamedCounts fields)
    {
        var ids = new List<int>();
        foreach (var entry in fields.Items)
            if (entry.Name.StartsWith(_copyPrefix, StringComparison.Ordinal) && entry.Name.IndexOf('.', _copyPrefix.Length) < 0
                && int.TryParse(entry.Name.AsSpan(_copyPrefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id))
                ids.Add(id);
        return ids;
    }

    private string CopyKey(int copy) => _copyPrefix + copy.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private bool HasCopy(SimState state, int owner, int copy) =>
        SimEngine.FieldsOf(state, owner).Has(CopyKey(copy)) || copy == 1 && ListedCopies(SimEngine.FieldsOf(state, owner)).Count == 0 && SimEngine.PowersOf(state, owner).Has(ThisPower);

    /// <summary>A copy's amount and applier (its entry; the power's own entry when none is listed).</summary>
    private (int Amount, int Data) CopyOf(SimState state, int owner, int copy)
    {
        var fields = SimEngine.FieldsOf(state, owner);
        var key = CopyKey(copy);
        if (fields.Has(key)) return (fields[key], fields.DataOf(key));
        var powers = SimEngine.PowersOf(state, owner);
        return copy == 1 && powers.Has(ThisPower) ? (powers[ThisPower], powers.DataOf(ThisPower)) : (0, 0);
    }

    /// <summary>Its copies listed in its owner's fields (the power's entry as copy 1 when none were), so one can change on its own.</summary>
    private void ListCopies(SimState state, int owner)
    {
        var fields = SimEngine.FieldsOf(state, owner);
        if (ListedCopies(fields).Count > 0) return;
        var powers = SimEngine.PowersOf(state, owner);
        if (powers.Has(ThisPower)) SimEngine.SetFields(state, owner, fields.With(CopyKey(1), powers[ThisPower], powers.DataOf(ThisPower)));
    }

    /// <summary>A copy's amount changed by <paramref name="delta"/>: gone at 0, with its state; the power's entry holds their total, gone with the last.</summary>
    private void ChangeCopy(SimState state, int owner, int copy, int? delta)
    {
        ListCopies(state, owner);
        var fields = SimEngine.FieldsOf(state, owner);
        var key = CopyKey(copy);
        if (!fields.Has(key)) return;
        var amount = fields[key];
        var left = delta is { } change ? amount + change : 0;
        fields = left > 0 ? fields.With(key, left) : WithoutCopy(fields, copy);
        SimEngine.SetFields(state, owner, fields);
        var powers = SimEngine.PowersOf(state, owner);
        var total = powers[ThisPower] - (amount - Math.Max(0, left));
        SimEngine.SetPowers(state, owner, total > 0 && ListedCopies(fields).Count > 0 ? powers.With(ThisPower, total, powers.DataOf(ThisPower)) : powers.Without(ThisPower));
    }

    private NamedCounts WithoutCopy(NamedCounts fields, int copy)
    {
        var key = CopyKey(copy);
        foreach (var entry in fields.Items)
            if (entry.Name == key || entry.Name.StartsWith(key + ".", StringComparison.Ordinal)) fields = fields.Without(entry.Name);
        return fields;
    }

    /// <summary>
    /// An application landed on its owner (SimEngine.ApplyPower, its amount already in the power's entry): a new copy of its own (Instanced), or
    /// more of its applier's copy (InstancedPerApplier: PowerCmd.FindExistingInstanceForStacking); a new instance's own state starts afresh. The
    /// new copy's id, for its AfterApplied; 0 when it stacked onto one.
    /// </summary>
    public int Land(SimState state, int owner, int applier, int amount, bool had)
    {
        var fields = SimEngine.FieldsOf(state, owner);
        if (!had)
        {
            // A new instance: what an earlier one kept is gone with it (its fields, its copies).
            foreach (var entry in fields.Items)
                if (entry.Name.StartsWith(_copyPrefix, StringComparison.Ordinal) || entry.Name.StartsWith(Name + ".", StringComparison.Ordinal)) fields = fields.Without(entry.Name);
            SimEngine.SetFields(state, owner, fields);
            if (!HasCopies) return 0;
            SimEngine.SetFields(state, owner, fields.With(CopyKey(1), amount, DataCreature.Of(applier)));
            return 1;
        }
        if (!HasCopies) return 0;
        // The copies before this one: listed, or the entry (less what just landed) as copy 1.
        if (ListedCopies(fields).Count == 0)
        {
            var powers = SimEngine.PowersOf(state, owner);
            fields = fields.With(CopyKey(1), powers[ThisPower] - amount, powers.DataOf(ThisPower));
        }
        if (Spec.Copies == PowerCopies.PerApplier)
            foreach (var copy in ListedCopies(fields))
                if (fields.DataOf(CopyKey(copy)) == DataCreature.Of(applier))
                {
                    SimEngine.SetFields(state, owner, fields.With(CopyKey(copy), fields[CopyKey(copy)] + amount));
                    return 0;
                }
        var next = ListedCopies(fields).DefaultIfEmpty(0).Max() + 1;
        SimEngine.SetFields(state, owner, fields.With(CopyKey(next), amount, DataCreature.Of(applier)));
        return next;
    }

    /// <summary>Compiled steps in order on one branch (see Run).</summary>
    private static IEnumerable<SimState> RunSteps(CompiledStep[] steps, HookArgs args)
    {
        IEnumerable<SimState>? states = null;
        foreach (var step in steps)
        {
            if (states == null)
            {
                if (step.When != null && step.When(in args) == 0m) continue;
                var result = step.Run(args);
                var after = result as IReadOnlyList<SimState> ?? result.ToList();
                if (after.Count == 1) { args = args.On(after[0]); states = null; }
                else states = after;
                if (after.Count == 1 && step == steps[^1]) return after;
                continue;
            }
            var current = args;
            var compiled = step;
            states = SimEngine.Then(states, branch =>
            {
                var branchArgs = current.On(branch);
                return compiled.When != null && compiled.When(in branchArgs) == 0m ? new[] { branch } : compiled.Run(branchArgs);
            });
        }
        return states ?? One(args.State);
    }

    // ---- commands

    private Func<HookArgs, IEnumerable<SimState>> Runner(HookStep step, bool fires, List<HookStep> firing)
    {
        switch (step)
        {
            case BindStep bind:
            {
                var value = Compile(bind.Value);
                return args =>
                {
                    args.State.Bound = args.State.Bound.With(BoundKey(in args), (int)value(in args));
                    return One(args.State);
                };
            }
            case BindAddStep bindAdd:
            {
                // More on what it bound to the play in progress (inside a hit: no branching), taken back with it.
                var value = Compile(bindAdd.Value);
                return args =>
                {
                    var key = BoundKey(in args);
                    if (args.State.Bound.Has(key)) args.State.Bound = args.State.Bound.With(key, args.State.Bound[key] + (int)value(in args));
                    return One(args.State);
                };
            }
            case UnbindStep:
                return args =>
                {
                    Take(in args);
                    return One(args.State);
                };
            case ChannelStep channel:
            {
                var orb = channel.Orb;
                return args =>
                {
                    if (fires) args.State.LogFired(Name, amount: 0, args.Owner);
                    return SimEngine.ChannelKind(args.State, orb, args.Owner);
                };
            }
            case RepeatStep repeat:
            {
                var times = Compile(repeat.Times);
                var first = CompileSteps(repeat.Steps, new List<HookStep>(), firstRound: true);
                var later = CompileSteps(repeat.Steps, new List<HookStep>(), firstRound: false);
                return args =>
                {
                    var rounds = (int)times(in args);
                    IEnumerable<SimState> states = One(args.State);
                    for (var round = 0; round < rounds; round++)
                    {
                        var body = round == 0 ? first : later;
                        var current = args;
                        states = SimEngine.Then(states, branch => RunSteps(body, current.On(branch)));
                    }
                    return states;
                };
            }
            case LetStep let:
            {
                // A local kept: worked out here, read later as it was (HookLocal).
                var value = Compile(let.Value);
                var slot = _localSlots[let.Name];
                return args =>
                {
                    if (args.Locals is { } locals) locals[slot] = value(in args);
                    return One(args.State);
                };
            }
            case ForEachStep each:
            {
                // foreach over the items as the loop starts (the players in their order, the hittable enemies, its owner's side): its body once for
                // each the loop's filter lets through (asked of each item as the loop reaches it, on each branch), the item Each.
                var body = CompileSteps(each.Steps, new List<HookStep>(), firstRound: true);
                var over = each.Over;
                var where = each.Where == null ? null : Compile(each.Where);
                return args =>
                {
                    var items = over switch
                    {
                        HookItems.Players => args.State.PlayerIds.ToList(),
                        HookItems.Teammates => Teammates(args.State, args.Owner),
                        _ => args.State.Targetable.Select(enemy => enemy.Id).ToList(),
                    };
                    IEnumerable<SimState> states = One(args.State);
                    foreach (var item in items)
                    {
                        var current = args with { Each = item };
                        states = SimEngine.Then(states, branch =>
                        {
                            var branchArgs = current.On(branch);
                            return where != null && where(in branchArgs) == 0m ? One(branch) : RunSteps(body, branchArgs);
                        });
                    }
                    return states;
                };
            }
            case GainBlockStep orbBlock when Spec.Kind == ModelKind.Orb:
            {
                // An orb's block (CreatureCmd.GainBlock of its value, Unpowered: to its owner, or a loop's player), logged as the orb's.
                var amount = Compile(orbBlock.Amount);
                var toEach = orbBlock.To == HookTarget.Each;
                return args => SimEngine.OrbBlock(args.State, args.Orb, evoke: args.Hook == HookName.Evoke, toEach ? args.Each : args.Orb.Player, (int)amount(in args));
            }
            case DamageStep orbDamage when Spec.Kind == ModelKind.Orb:
            {
                // An orb's damage (CreatureCmd.Damage of its value from its owner, Unpowered): a roll's, every enemy's, the weakest's; its targets kept
                // for AfterOrbEvoked.
                var amount = Compile(orbDamage.Amount);
                var props = Compile(orbDamage.Props);
                var target = orbDamage.Target;
                if (target is not (HookTarget.RandomEnemy or HookTarget.AllEnemies or HookTarget.WeakestEnemy)) throw new InvalidOperationException($"{Name}: no orb damage to {target}");
                return args => SimEngine.OrbDamage(args.State, args.Orb, evoke: args.Hook == HookName.Evoke, target, (int)amount(in args), (SimProps)(int)props(in args));
            }
            case GainEnergyStep orbEnergy when Spec.Kind == ModelKind.Orb:
            {
                // An orb's energy (PlayerCmd.GainEnergy of its value: Plasma's), for its owner.
                var amount = Compile(orbEnergy.Amount);
                return args => SimEngine.ProcCardEvent(args.State, CardEvent.EnergyGained, (int)amount(in args), args.Orb.Player, indirect: false);
            }
            case StoreFieldStep orbStore when Spec.Kind == ModelKind.Orb:
            {
                // A store to the orb's own field (Glass's _passiveVal, Dark's _evokeVal: its EvokeVal), in its slot; its values then worked out again.
                var value = Compile(orbStore.Value);
                var field = orbStore.Field;
                var aliased = _orbAliases.GetValueOrDefault(field);
                return args =>
                {
                    var stored = (int)value(in args);
                    var index = args.Orb.Index;
                    if (index < 0 || args.State.Player(args.Orb.Player) is not { } owner || index >= owner.Orbs.Count) return One(args.State);
                    var orb = args.State.MutablePlayer(args.Orb.Player).OwnOrbs()[index];
                    if (aliased != null) SetOrbProperty(orb, aliased, stored);
                    else orb.Fields = orb.Fields.With(field, stored);
                    ValuesAgain(args.State, args.Orb.Player, index, orb);
                    return One(args.State);
                };
            }
            // An enchantment's commands are its card's (CardModel.OnPlayWrapper runs them as part of the play): each the simulator's card action for
            // the same game command, as a card's program builds it (SimAdapter.Steps), on its card's owner.
            case GainBlockStep enchantmentBlock when Spec.Kind == ModelKind.Enchantment:
            {
                if (enchantmentBlock.To != null) throw new InvalidOperationException($"{Name}: no enchantment block for {enchantmentBlock.To}");
                var amount = Compile(enchantmentBlock.Amount);
                var props = Compile(enchantmentBlock.Props);
                return args => SimEngine.BlockOnce(args.State, new GainBlockAction(Times: 1, amount(in args), (SimProps)(int)props(in args))
                    { To = args.Owner, Captured = args.Card?.Enchantment?.BlockTerms });
            }
            case DamageStep enchantmentDamage when Spec.Kind == ModelKind.Enchantment:
            {
                // CreatureCmd.Damage of its card's owner, unblockable: the HP its card costs (a card program's LoseHpAction), named by its card
                // (the damage's cardSource is base.Card).
                if (enchantmentDamage.Target != HookTarget.Owner || enchantmentDamage.Props is not HookConstant { Value: var hpProps } || !((SimProps)(int)hpProps).HasFlag(SimProps.Unblockable))
                    throw new InvalidOperationException($"{Name}: no enchantment damage to {enchantmentDamage.Target}");
                var amount = Compile(enchantmentDamage.Amount);
                return args =>
                {
                    SimEngine.LoseHp(args.State, args.Card?.Name ?? Name, (int)amount(in args), args.Owner, args.Card, (SimProps)(int)hpProps);
                    return One(args.State);
                };
            }
            case DrawStep enchantmentDraw when Spec.Kind == ModelKind.Enchantment:
            {
                // Its card's draws join the hand as they're drawn (decision #189), set off as a card event of the play's (indirect).
                var count = Compile(enchantmentDraw.Count);
                return args => (int)count(in args) is > 0 and var drawn
                    ? SimEngine.CardEventsOf(args.State, new CardEventAction(CardEvent.Drawn, drawn) { Indirect = true, JoinsHand = true, Player = args.Owner })
                    : One(args.State);
            }
            case GainEnergyStep enchantmentEnergy when Spec.Kind == ModelKind.Enchantment:
            {
                var amount = Compile(enchantmentEnergy.Amount);
                return args => (int)amount(in args) is > 0 and var gained
                    ? SimEngine.CardEventsOf(args.State, new CardEventAction(CardEvent.EnergyGained, gained) { Player = args.Owner })
                    : One(args.State);
            }
            case ApplyPowerStep enchantmentPower when Spec.Kind == ModelKind.Enchantment:
            {
                // PowerCmd.Apply from its card (its cardSource): a card's application, on the play's target or every hittable enemy.
                var amount = Compile(enchantmentPower.Amount);
                var power = new PowerId(enchantmentPower.Power);
                var debuff = enchantmentPower.Debuff;
                var to = enchantmentPower.Target switch
                {
                    HookTarget.PlayTarget => ApplyPowerAction.Where.Target,
                    HookTarget.AllEnemies => ApplyPowerAction.Where.AllEnemies,
                    HookTarget.Owner => ApplyPowerAction.Where.You,
                    _ => throw new InvalidOperationException($"{Name}: no enchantment power on {enchantmentPower.Target}"),
                };
                if (enchantmentPower.By != null || enchantmentPower.DebuffWhenNegative) throw new InvalidOperationException($"{Name}: no enchantment power applied by another");
                return args => SimEngine.ApplyPowerOf(args.State, new ApplyPowerAction(power, (int)amount(in args)) { To = to, Debuff = debuff });
            }
            case StoreFieldStep enchantmentStore when Spec.Kind == ModelKind.Enchantment:
            {
                // Its own state (its Status), its card's own, in its owner's fields.
                var value = Compile(enchantmentStore.Value);
                var field = enchantmentStore.Field;
                return args =>
                {
                    SimEngine.SetField(args.State, args.Owner, EnchantmentFieldKey(in args, field), (int)value(in args));
                    return One(args.State);
                };
            }
            case FlashStep or GainStarsStep or HealStep or SummonStep or ChannelStep or RepeatStep or ForEachStep or LetStep or BindStep or BindAddStep or UnbindStep or RemovePowerStep or DecrementPowerStep or UpgradeCardStep or ApplyKeywordStep or FreeInHandStep or MakeCopyStep or MakeRandomInHandStep or MakeIntoDrawStep or StunStep or KillStep
                when Spec.Kind == ModelKind.Enchantment:
                throw new InvalidOperationException($"{Name}: no enchantment command for {step.GetType().Name}");
            case GainBlockStep gain when gain.To == HookTarget.Each:
            {
                // Block for the loop's player (a player's model's: the item each round), through the block pipeline on them, logged under its owner.
                var amount = Compile(gain.Amount);
                var props = Compile(gain.Props);
                return args => SimEngine.ProcBlockFor(args.State, args.Owner, Name, args.Each, amount(in args), (SimProps)(int)props(in args));
            }
            case GainBlockStep gain:
            {
                var amount = Compile(gain.Amount);
                var props = Compile(gain.Props);
                var perCard = PerHandCard(gain.Amount);
                // An enemy's own block (Sneaky's) is its reaction to your play; a player's goes through the proc block, its tab counting it.
                return args => Who.IsEnemy(args.Owner) ? SimEngine.EnemyBlock(args.State, args.Owner, Name, amount(in args), (SimProps)(int)props(in args))
                    : SimEngine.ProcBlock(args.State, args.Owner, Name, amount(in args), (SimProps)(int)props(in args), label: perCard ? PerCardLabel(in args) : null);
            }
            case DamageStep damage:
            {
                var amount = Compile(damage.Amount);
                var props = Compile(damage.Props);
                var noDealer = damage.NoDealer;
                var perCard = PerHandCard(damage.Amount);
                return damage.Target switch
                {
                    HookTarget.AllEnemies => args => SimEngine.ProcDamageAll(args.State, args.Owner, Name, amount(in args), (SimProps)(int)props(in args), noDealer: noDealer, label: perCard ? PerCardLabel(in args) : null),
                    HookTarget.RandomEnemy => args => SimEngine.ProcDamageRandom(args.State, args.Owner, Name, amount(in args), (SimProps)(int)props(in args), noDealer: noDealer, label: perCard ? PerCardLabel(in args) : null),
                    HookTarget.PowerOwner => args => SimEngine.ProcDamageOn(args.State, args.Owner, Name, args.Target, amount(in args), (SimProps)(int)props(in args), noDealer: noDealer),
                    // Every living player, in player order (Explosive's: its owner's death hits each), each through the damage on a player (the one
                    // path: SimEngine.DamagePlayer), named in Incoming Damage's breakdown by its owner and its title, as the game shows them.
                    HookTarget.LivingPlayers => args =>
                    {
                        var label = OwnerLabel(in args);
                        var dealt = amount(in args);
                        var dealtProps = (SimProps)(int)props(in args);
                        foreach (var player in args.State.PlayerIds)
                            if (args.State.Player(player) is { Hp: > 0 })
                                SimEngine.DamagePlayer(args.State, label, player, dealt, dealtProps, noDealer ? null : args.Owner, card: null);
                        return One(args.State);
                    },
                    // The player who played the card (cardPlay.Card.Owner.Creature: Galvanic's), through the damage on a player, while they live.
                    HookTarget.PlayOwner => args => args.Play is { } played && args.State.Player(played.Owner) is { Hp: > 0 }
                        ? SimEngine.ProcDamagePlayer(args.State, args.Owner, Name, played.Owner, amount(in args), (SimProps)(int)props(in args), noDealer: noDealer)
                        : One(args.State),
                    // The damage's dealer (Thorns', Flame Barrier's and Reflect's damage back), while it lives: a player through the damage on a player
                    // (Incoming Damage's breakdown names it by its owner and its title), your Osty through the damage on him, an enemy as any.
                    HookTarget.Dealer => args =>
                    {
                        if ((args.Damage?.Dealer ?? args.Hit?.Dealer) is not { } dealer) return One(args.State);
                        if (Who.IsPet(dealer))
                        {
                            if (Who.PetKindOf(dealer) == PetKind.Osty)
                                return SimEngine.ProcDamageOsty(args.State, args.Owner, Name, Who.PetOwnerOf(dealer), amount(in args), (SimProps)(int)props(in args), noDealer: noDealer);
                            args.State.Unmodelled ??= $"{Name}'s damage to a pet that deals none in the simulator";
                            return One(args.State);
                        }
                        if (Who.IsPlayer(dealer))
                            return args.State.Player(dealer) is { Hp: > 0, Died: false }
                                ? SimEngine.ProcDamagePlayer(args.State, args.Owner, Name, dealer, amount(in args), (SimProps)(int)props(in args), noDealer: noDealer, label: OwnerLabel(in args))
                                : One(args.State);
                        return SimEngine.ProcDamageOn(args.State, args.Owner, Name, dealer, amount(in args), (SimProps)(int)props(in args), noDealer: noDealer);
                    },
                    // A loop's enemy (the item each round).
                    HookTarget.Each => args => Who.IsEnemy(args.Each) ? SimEngine.ProcDamageOn(args.State, args.Owner, Name, args.Each, amount(in args), (SimProps)(int)props(in args), noDealer: noDealer) : One(args.State),
                    // The living creatures an evoke acted on (AfterOrbEvoked's targets.Where(c => c.IsAlive): Thunder), each in turn.
                    HookTarget.EvokeTargets => args =>
                    {
                        IEnumerable<SimState> states = One(args.State);
                        foreach (var id in args.State.OrbTargets ?? Array.Empty<int>())
                        {
                            var hit = args;
                            states = SimEngine.Then(states, branch => branch.Enemy(id) is { Alive: true }
                                ? SimEngine.ProcDamageOn(branch, hit.Owner, Name, id, amount(hit.On(branch)), (SimProps)(int)props(hit.On(branch)), noDealer: noDealer) : One(branch));
                        }
                        return states;
                    },
                    // Its owner: a player through the damage on a player (Incoming Damage's breakdown names it as the game does), an enemy as any.
                    HookTarget.Owner => args => Who.IsPlayer(args.Owner)
                        ? SimEngine.ProcDamagePlayer(args.State, args.Owner, Name, args.Owner, amount(in args), (SimProps)(int)props(in args), noDealer: noDealer, label: SimEngine.Title(args.State, args.Owner, Name))
                        : SimEngine.ProcDamageOn(args.State, args.Owner, Name, args.Owner, amount(in args), (SimProps)(int)props(in args), noDealer: noDealer),
                    _ => throw new InvalidOperationException($"{Name}: no damage to {damage.Target}"),
                };
            }
            case SummonStep summon:
            {
                // OstyCmd.Summon as the engine runs it (SimEngine.Summon), for the player its code names; a summon tab counts what it summons.
                var amount = Compile(summon.Amount);
                var tallies = Tab == TabKind.Summon;
                return args =>
                {
                    var summoner = summon.Summoner switch
                    {
                        HookTarget.Owner => args.Owner,
                        HookTarget.PlayOwner => args.Play?.Owner ?? Who.None,
                        // The hit's dealer's player: a pet's (Osty's); no one else's dealer has one.
                        HookTarget.Dealer when summon.PetOwner => Who.PetOwnerOf(args.Damage?.Dealer ?? args.Hit?.Dealer ?? Who.None),
                        _ => Who.None,
                    };
                    if (!Who.IsPlayer(summoner) || args.State.Player(summoner) == null) return One(args.State);
                    if (fires && !tallies) args.State.LogFired(Name, amount: 0, args.Owner);
                    var states = SimEngine.Summon(args.State, summoner, amount(in args), tallies ? Name : null, args.Owner, firing: fires);
                    // Inside a hit no hook can branch: the summon's own hooks (Hook.AfterSummon, AfterOstyRevived) must leave the one state.
                    if (!InHit(args.Hook)) return states;
                    var list = states.ToList();
                    if (list.Count != 1 || !ReferenceEquals(list[0], args.State)) args.State.Unmodelled ??= $"{Name}'s summon inside a hit set off hooks that branch";
                    return One(args.State);
                };
            }
            case HealStep heal:
            {
                var amount = Compile(heal.Amount);
                return args =>
                {
                    SimEngine.Heal(args.State, args.Owner, (int)amount(in args));
                    return One(args.State);
                };
            }
            case DrawStep draw:
            {
                var count = Compile(draw.Count);
                return args => Cause(args, CardEvent.Drawn, (int)count(in args), fires);
            }
            case GainEnergyStep energy:
            {
                var amount = Compile(energy.Amount);
                return args => Cause(args, CardEvent.EnergyGained, (int)amount(in args), fires);
            }
            case FlashStep flash:
            {
                // Its own Flash(): the flash log the play check reads (decision #199); the tabs read the acting log. One on a condition the
                // reader doesn't read is logged as unknown: it may flash or not, which only the play check asks.
                var unknown = flash.Unknown != null;
                return args =>
                {
                    args.State.LogFlash(Name, args.Owner, unknown);
                    return One(args.State);
                };
            }
            case GainStarsStep:
                // One stars-gained event, as the game calls AfterStarsGained once (the simulator keeps no count of stars).
                return args => Cause(args, CardEvent.StarsGained, 1, fires);
            case ApplyPowerStep apply:
            {
                var amount = Compile(apply.Amount);
                var power = new PowerId(apply.Power);
                var alwaysDebuff = apply.Debuff;
                var debuffWhenNegative = apply.DebuffWhenNegative;
                var target = apply.Target;
                var appliedBy = apply.By;
                var goesAtZero = apply.GoesAtZero;
                return args =>
                {
                    var state = args.State;
                    var given = (int)amount(in args);
                    // PowerCmd.Apply of 0 does nothing (its first test: amount == 0m returns), so the model doesn't act (Rupture's Strength for a card
                    // that cost no HP). PowerCmd.ModifyAmount of itself (GoesAtZero) isn't Apply.
                    if (given == 0 && !goesAtZero) return One(state);
                    var debuff = alwaysDebuff || debuffWhenNegative && given < 0;
                    // Its applier: its owner, or the creature its code names (the applier it keeps; no one).
                    var applier = appliedBy == null ? args.Owner : CreatureOf(in args, appliedBy);
                    if (fires) state.LogFired(Name, amount: 0, args.Owner);
                    // Inside a hit or a death, where hooks can't branch: it lands now, heard once that's over (SimEngine.ApplyInHit).
                    if (InHit(args.Hook))
                    {
                        switch (target)
                        {
                            case HookTarget.Owner:
                                SimEngine.ApplyInHit(state, applier, args.Owner, power, given, debuff);
                                if (goesAtZero && SimEngine.PowersOf(state, args.Owner) is var inHitLeft && inHitLeft.Has(power) && inHitLeft[power] <= 0) SimEngine.SetPowers(state, args.Owner, inHitLeft.Without(power));
                                break;
                            case HookTarget.PowerOwner or HookTarget.HitTarget: SimEngine.ApplyInHit(state, applier, args.Target, power, given, debuff); break;
                            case HookTarget.AllEnemies:
                                foreach (var enemy in state.Targetable.ToList()) SimEngine.ApplyInHit(state, applier, enemy.Id, power, given, debuff);
                                break;
                            default: throw new InvalidOperationException($"{Name}: no power on {target}");
                        }
                        return One(state);
                    }
                    // Each landing heard by the listeners (PowerCmd.Apply: Hook.AfterPowerAmountChanged), on each one's own creature in turn.
                    switch (target)
                    {
                        case HookTarget.Owner:
                        {
                            if (!goesAtZero) return SimEngine.ApplyAndTell(state, applier, args.Owner, power, given, debuff);
                            // Its own amount changed (PowerCmd.ModifyAmount): gone once it's at 0 or less, after its listeners heard it (ShouldRemoveDueToAmount).
                            var owner = args.Owner;
                            var changed = SimEngine.ApplyAndTell(state, applier, owner, power, given, debuff).ToList();
                            foreach (var branch in changed)
                                if (SimEngine.PowersOf(branch, owner) is var left && left.Has(power) && left[power] <= 0) SimEngine.SetPowers(branch, owner, left.Without(power));
                            return changed;
                        }
                        case HookTarget.PowerOwner or HookTarget.HitTarget: return SimEngine.ApplyAndTell(state, applier, args.Target, power, given, debuff);
                        // The player who played the card (cardPlay.Card.Owner.Creature: Vital Spark's Tainted on whoever plays a Tainted card).
                        case HookTarget.PlayOwner: return args.Play is { } played ? SimEngine.ApplyAndTell(state, applier, played.Owner, power, given, debuff) : One(state);
                        case HookTarget.AllEnemies:
                        {
                            IEnumerable<SimState> states = One(state);
                            foreach (var enemy in state.Targetable.ToList())
                            {
                                var id = enemy.Id;
                                states = SimEngine.Then(states, branch => branch.Enemy(id) is { Alive: true } ? SimEngine.ApplyAndTell(branch, applier, id, power, given, debuff) : One(branch));
                            }
                            return states;
                        }
                        default: throw new InvalidOperationException($"{Name}: no power on {target}");
                    }
                };
            }
            case StoreFieldStep store:
            {
                var value = Compile(store.Value);
                var key = FieldKey(store.Field);
                var field = store.Field;
                // A store a payout the play decides reads (Art of War's AnyAttacksPlayedThisTurn): the play that turns it off gives the bonus up there.
                var decides = _payoutFields.Contains(field);
                return args =>
                {
                    if (fires) args.State.LogFired(Name, amount: 0, args.Owner);
                    var stored = (int)value(in args);
                    if (args.Copy > 0) ListCopies(args.State, args.Owner);
                    var onAPlay = decides && args.Hook is HookName.BeforeCardPlayed or HookName.AfterCardPlayed;
                    var wasOn = onAPlay && PayoutOn(args.State, args.Owner);
                    SimEngine.SetField(args.State, args.Owner, args.Copy > 0 ? CopyFieldKey(args.Copy, field) : key, stored);
                    // The play gives the bonus up there, or sets it up (decision #244).
                    if (onAPlay) Decided(args.State, args.Owner, wasOn);
                    return One(args.State);
                };
            }
            // PowerCmd.Remove(this), PowerCmd.Decrement(this): the power, or the copy running the hook.
            case RemovePowerStep:
                return args =>
                {
                    if (fires) args.State.LogFired(Name, amount: 0, args.Owner);
                    if (args.Copy > 0) ChangeCopy(args.State, args.Owner, args.Copy, delta: null);
                    else SimEngine.SetPowers(args.State, args.Owner, SimEngine.PowersOf(args.State, args.Owner).Without(ThisPower));
                    return One(args.State);
                };
            case DecrementPowerStep:
                return args =>
                {
                    if (fires) args.State.LogFired(Name, amount: 0, args.Owner);
                    if (args.Copy > 0) ChangeCopy(args.State, args.Owner, args.Copy, delta: -1);
                    else SimEngine.DecrementPower(args.State, args.Owner, ThisPower);
                    return One(args.State);
                };
            // A change to the card played (decision #218): its later plays in this card play see it.
            case UpgradeCardStep:
                return args =>
                {
                    SimEngine.UpgradeCardInPlay(args.State);
                    if (fires) args.State.LogFired(Name, amount: 0, args.Owner);
                    return One(args.State);
                };
            // Random cards in its owner's hand upgraded (Tune's, on the turn's first Ki): fired (its plain tab); followed only when none of that hand acts as
            // the turn ends and could be upgraded.
            case UpgradeRandomInHandStep upgradeRandom:
            {
                var count = Compile(upgradeRandom.Count);
                return args =>
                {
                    if ((int)count(in args) <= 0) return One(args.State);
                    if (fires) args.State.LogFired(Name, amount: 0, args.Owner);
                    if (args.State.Player(args.Owner) is { UpgradableAtTurnEnd: > 0 })
                        args.State.Unmodelled ??= $"{Name} upgrades random cards in hand, and one that acts as the turn ends could be one";
                    return One(args.State);
                };
            }
            // A card in its owner's hand made free this turn (Mummified Hand's pick): it acts when another card in that hand costs something (the
            // card played's fact, read as it was played), on a card the game picks at random (SimEngine.FreeInHand).
            // A copy of the card played made into its owner's hand (Music Box's): it joins the hand (SimEngine.AddToHand, Ethereal: it exhausts as the
            // turn ends), then what reacts to a card made hears it, the copy's facts the card played's with its keywords (CardPileCmd.AddGeneratedCardToCombat).
            case MakeCopyStep copy:
            {
                var ethereal = copy.Ethereal;
                if (copy.OfEventCard)
                    return args =>
                    {
                        // A copy of the event's card (the card exhausted), as it is: its facts when the event knows them.
                        if (fires) args.State.LogFired(Name, amount: 0, args.Owner);
                        SimEngine.AddToHand(args.State, args.Owner, count: 1, ethereal);
                        var copied = args.State.EventCard is { } eventCard && ethereal && !eventCard.Ethereal ? eventCard with { Ethereal = true } : args.State.EventCard;
                        return SimEngine.ProcCardEvent(args.State, CardEvent.Generated, count: 1, args.Owner, onEach: SimEngine.TallyFor(Name, Tab, CardEvent.Generated, args.Owner), card: copied);
                    };
                return args =>
                {
                    if (args.Play is not { } played) return One(args.State);
                    if (fires) args.State.LogFired(Name, amount: 0, args.Owner);
                    SimEngine.AddToHand(args.State, args.Owner, count: 1, ethereal);
                    var made = ethereal && !played.Card.Ethereal ? played.Card with { Ethereal = true } : played.Card;
                    return SimEngine.ProcCardEvent(args.State, CardEvent.Generated, count: 1, args.Owner, onEach: SimEngine.TallyFor(Name, Tab, CardEvent.Generated, args.Owner), card: made);
                };
            }
            // Cards of a type made at random into its owner's hand (Calamity's): each joins the hand, then what reacts to a card made hears it, a card of
            // that type whose class is the roll's (CardPileCmd.AddGeneratedCardToCombat for each, in turn).
            case MakeRandomInHandStep random:
            {
                var count = Compile(random.Count);
                var card = new SimCard("", random.Type) { RandomClass = true };
                return args =>
                {
                    var made = (int)count(in args);
                    if (made <= 0) return One(args.State);
                    if (fires) args.State.LogFired(Name, amount: 0, args.Owner);
                    IEnumerable<SimState> states = new[] { args.State };
                    var owner = args.Owner;
                    var tally = SimEngine.TallyFor(Name, Tab, CardEvent.Generated, owner);
                    for (var i = 0; i < made; i++)
                        states = SimEngine.Then(states, branch =>
                        {
                            SimEngine.AddToHand(branch, owner, count: 1, ethereal: false);
                            return SimEngine.ProcCardEvent(branch, CardEvent.Generated, count: 1, owner, onEach: tally, card: card);
                        });
                    return states;
                };
            }
            case StunStep:
                return args =>
                {
                    // Creature.StunInternal: nothing on a dead creature; the game throws on a player (no player is ever given one: an enemy's power).
                    if (args.State.Enemy(args.Owner) is not { Alive: true } enemy)
                    {
                        if (Who.IsPlayer(args.Owner)) args.State.Unmodelled ??= $"{Name} stuns a player, which the game's code doesn't do";
                        return One(args.State);
                    }
                    if (fires) args.State.LogFired(Name, amount: 0, args.Owner);
                    SimEngine.Stun(enemy);
                    return One(args.State);
                };
            case MakeIntoDrawStep make:
            {
                var into = Compile(make.For);
                // The card the created-card hooks hear (its class, type and tags), made once per class's step.
                var card = new SimCard(make.Card, make.Type) { Tags = make.Tags.ToHashSet() };
                var counts = Tab == TabKind.CardsMade;
                return args =>
                {
                    var player = (int)into(in args);
                    if (!Who.IsPlayer(player))
                    {
                        args.State.Unmodelled ??= $"{Name} makes a {make.Card} for a creature that isn't a player";
                        return One(args.State);
                    }
                    if (fires) args.State.LogFired(Name, amount: 0, args.Owner);
                    if (counts) args.State.LogAmount(Name, amount: 1, args.Owner);
                    var creator = make.ByOwner ? args.Owner : Who.None;
                    // Inside a hit, where hooks can't branch: it joins the pile, and its hook hears it, once the hit is over (SimEngine.DrainPending).
                    if (InHit(args.Hook))
                    {
                        args.State.PendingIntoDraw = args.State.PendingIntoDraw.Add(new PendingMade(player, make.Card, creator, card));
                        return One(args.State);
                    }
                    return SimEngine.MadeIntoDrawPile(args.State, new PendingMade(player, make.Card, creator, card));
                };
            }
            case KillStep kill:
                return args =>
                {
                    // CreatureCmd.Kill of a player: all their HP, then a death prevention unless forced (SimEngine.KillPlayer).
                    if (Who.IsPlayer(args.Owner))
                    {
                        if (fires) args.State.LogFired(Name, amount: 0, args.Owner);
                        SimEngine.KillPlayer(args.State, args.Owner, kill.Forced);
                        return One(args.State);
                    }
                    // Of an enemy: it dies (its death told once the hit is over, as every death the engine follows), marked killed by its power even on
                    // the hit that already killed it, as the hand-written model had it (its tab).
                    if (args.State.Enemy(args.Owner) is not { } enemy) return One(args.State);
                    if (fires) args.State.LogFired(Name, amount: 0, args.Owner);
                    enemy.KilledByItsPower = true;
                    enemy.Hp = 0;
                    return One(args.State);
                };
            case FreeInHandStep:
                return args =>
                {
                    if (args.Play is not { Card.OthersInHandCost: true } played) return One(args.State);
                    if (fires) args.State.LogFired(Name, amount: 0, args.Owner);
                    return SimEngine.FreeInHand(args.State, args.Owner, played.Card.HandId);
                };
            case ApplyKeywordStep keywordStep:
            {
                var keyword = keywordStep.Keyword;
                return args =>
                {
                    SimEngine.KeywordCardInPlay(args.State, keyword);
                    if (fires) args.State.LogFired(Name, amount: 0, args.Owner);
                    return One(args.State);
                };
            }
            default:
                throw new InvalidOperationException($"{Name}: no command for {step.GetType().Name}");
        }
    }

    /// <summary>
    /// Its owner's side (CombatState.GetTeammatesOf(base.Owner): Allies or Enemies, itself included): for a player's model every player in their
    /// order, then each one's pets while they live (Osty, Wuppy: the allies after the players), for an enemy's the living enemies.
    /// </summary>
    private static List<int> Teammates(SimState state, int owner)
    {
        if (Who.IsEnemy(owner)) return state.Enemies.Where(enemy => enemy.Alive).Select(enemy => enemy.Id).ToList();
        var side = state.PlayerIds.ToList();
        foreach (var player in state.PlayerIds)
            foreach (var kind in new[] { PetKind.Osty, PetKind.Wuppy })
                if (state.Player(player)!.PetHp(kind) > 0) side.Add(Who.PetOf(player, kind));
        return side;
    }

    /// <summary>
    /// An amount its code works out per card in its owner's hand (cards.Count * Block: Cloak Clasp's block, Obsidian Vice's damage): Incoming
    /// Damage's breakdown names it with the count, as "Cloak Clasp ×4" (the value's own factor, not the class: any model whose amount is so).
    /// </summary>
    private static bool PerHandCard(HookValue amount) => amount is HookMath { Operation: HookOperation.Multiply } product
        && (product.Left is HookFactValue { Fact: HookFact.OwnerHandCount } || product.Right is HookFactValue { Fact: HookFact.OwnerHandCount });

    /// <summary>Its name in Incoming Damage's breakdown with the cards its amount counts ("Cloak Clasp ×4").</summary>
    private string PerCardLabel(in HookArgs args) => $"{SimEngine.Title(args.State, args.Owner, Name)} ×{args.State.Player(args.Owner)?.HandCount ?? 0}";

    /// <summary>Its name in Incoming Damage's breakdown: an enemy's power by its enemy and its title ("Exploding Slime Explosive"), a player's by its title.</summary>
    private string OwnerLabel(in HookArgs args) =>
        args.State.Enemy(args.Owner) is { } enemy ? $"{enemy.Name} {SimEngine.Title(args.State, args.Owner, Name)}" : SimEngine.Title(args.State, args.Owner, Name);

    /// <summary>
    /// What it bound to the play in progress (its own data's Remove(cardPlay.Card, out amount)): taken out of the state into this run's slot;
    /// false (and 0) when nothing was bound.
    /// </summary>
    private bool Take(in HookArgs args)
    {
        var key = BoundKey(in args);
        var state = args.State;
        var bound = state.Bound.Has(key);
        if (args.Taken is { } taken) taken[0] = bound ? state.Bound[key] : 0;
        if (bound) state.Bound = state.Bound.Without(key);
        return bound;
    }

    /// <summary>One of its own fields' key among its owner's (SimPlayer.Fields, SimEnemy.Fields): "Model.Field".</summary>
    public string FieldKey(string field) => Name + "." + field;

    /// <summary>One copy's own field's key among its owner's: "Model@copy.Field".</summary>
    public string CopyFieldKey(int copy, string field) => CopyKey(copy) + "." + field;

    /// <summary>
    /// What it binds to the card play in progress (its own data keyed by cardPlay.Card), by its owner (and the copy binding it) and that card play
    /// (SimCardPlay.Serial: a card played inside another binds and takes its own, decision #267).
    /// </summary>
    private string BoundKey(in HookArgs args) => SimKeys.Bound(BoundOwnerKey(in args), (args.Play ?? args.State.CurrentPlay)?.Serial ?? 0);

    /// <summary>Its own key among what's bound, whatever card play: "Model@owner" (or the copy's).</summary>
    private string BoundOwnerKey(in HookArgs args) => args.Copy > 0 ? SimKeys.Owned(CopyKey(args.Copy), args.Owner) : SimKeys.Owned(Name, args.Owner);

    /// <summary>It has something bound to any card play (CardBeingPlayed != null: Music Box's, kept from an outer card play too).</summary>
    private bool HoldsAny(in HookArgs args)
    {
        var prefix = BoundOwnerKey(in args);
        foreach (var entry in args.State.Bound.Items) if (SimKeys.IsBoundOf(entry.Name, prefix)) return true;
        return false;
    }

    /// <summary>Card events it causes (draws, energy), each through every listener; a draw or energy tab counts what they gave (decision #132).</summary>
    private IEnumerable<SimState> Cause(HookArgs args, CardEvent cardEvent, int count, bool fires)
    {
        // None to draw or gain (Dark Embrace's amount × an Ethereal count of 0 as the turn ends): CardPileCmd.Draw and PlayerCmd.GainEnergy do nothing.
        if (count <= 0) return One(args.State);
        if (fires) args.State.LogFired(Name, amount: 0, args.Owner);
        // A death's or a hit's (AfterDamageReceived: Centennial Puzzle's draws): queued until the hit or action is over (SimEngine.Raise), as
        // the simulator can't branch inside a hit; a draw or energy tab
        // counts them as they come.
        if (InHit(args.Hook))
        {
            SimEngine.Raise(args.State, cardEvent, args.Owner, count, source: SimEngine.TallyFor(Name, Tab, cardEvent, args.Owner) != null ? Name : null);
            return One(args.State);
        }
        return SimEngine.ProcCardEvent(args.State, cardEvent, count, args.Owner, onEach: SimEngine.TallyFor(Name, Tab, cardEvent, args.Owner));
    }

    // ---- values

    /// <summary>The owners a value's key is made for once: the players (-1 to -4) and the enemies a fight can have.</summary>
    private const int FirstKeyedOwner = -4, KeyedOwners = 24;

    private Evaluate Compile(HookValue value)
    {
        switch (value)
        {
            case HookConstant constant:
            {
                var number = constant.Value;
                return (in HookArgs _) => number;
            }
            case HookVar variable:
            {
                var key = variable.Key;
                var canonical = variable.Canonical;
                // Its key for each owner made once (a damage's math asks it on every hit: no string made per ask).
                var keys = Enumerable.Range(FirstKeyedOwner, KeyedOwners).Select(owner => $"{owner}:{Name}:{key}").ToArray();
                return (in HookArgs args) => SimEngine.VarValue(args.State,
                    args.Owner - FirstKeyedOwner is var at && at >= 0 && at < keys.Length ? keys[at] : $"{args.Owner}:{Name}:{key}", canonical);
            }
            case HookNot not:
            {
                var inner = Compile(not.Inner);
                return (in HookArgs args) => inner(in args) == 0m ? 1m : 0m;
            }
            case HookMath math:
                return CompileMath(math.Operation, Compile(math.Left), Compile(math.Right));
            case HookWays ways:
            {
                var compiled = ways.Ways.Select(way => (When: Compile(way.When), Value: Compile(way.Value))).ToArray();
                return (in HookArgs args) =>
                {
                    foreach (var (when, wayValue) in compiled)
                        if (when(in args) != 0m) return wayValue(in args);
                    return 0m;
                };
            }
            case HookFactValue fact:
                return CompileFact(fact);
            case HookTest test:
            {
                var inner = Compile(test.Test);
                var slot = _testSlots[test.At];
                return (in HookArgs args) =>
                {
                    if (args.Asked is not { } asked) return inner(in args);
                    if (asked[slot] == 0) asked[slot] = inner(in args) != 0m ? 1 : 2;
                    return asked[slot] == 1 ? 1m : 0m;
                };
            }
            // An orb's own field (its fields, or the value it is: Dark's _evokeVal is its EvokeVal), and its values as the game shows them.
            case HookField orbField when Spec.Kind == ModelKind.Orb:
            {
                var name = orbField.Field;
                if (_orbAliases.TryGetValue(name, out var aliased)) return (in HookArgs args) => OrbOf(in args) is { } orb ? OrbProperty(orb, aliased) : 0m;
                var start = _orbStarts.GetValueOrDefault(name);
                return (in HookArgs args) => OrbOf(in args) is { } orb && orb.Fields.Has(name) ? orb.Fields[name] : start;
            }
            case HookOrbProperty property:
            {
                var name = property.Property;
                return (in HookArgs args) => OrbOf(in args) is { } orb ? OrbProperty(orb, name) : 0m;
            }
            case HookOrbValue orbValue:
            {
                // OrbModel.ModifyOrbValue: Hook.ModifyOrbValue on the value, every listener's term in turn.
                var inner = Compile(orbValue.Base);
                return (in HookArgs args) => SimEngine.OrbValue(args.State, args.Orb, inner(in args));
            }
            case HookLocal local:
            {
                var slot = _localSlots[local.Name];
                return (in HookArgs args) => args.Locals?[slot] ?? 0m;
            }
            case HookField enchantmentField when Spec.Kind == ModelKind.Enchantment:
            {
                // Its own state through its card's plays (its owner's fields, under its card), from where the game had it as the card was played.
                var name = enchantmentField.Field;
                var canonical = enchantmentField.Canonical;
                return (in HookArgs args) =>
                {
                    var fields = SimEngine.FieldsOf(args.State, args.Owner);
                    var at = EnchantmentFieldKey(in args, name);
                    if (fields.Has(at)) return fields[at];
                    return args.Card?.Enchantment is { } enchantment && enchantment.Fields.Has(name) ? enchantment.Fields[name] : canonical;
                };
            }
            case HookField field:
            {
                // Its state, or a copy's; one never stored is where a new instance starts (0, or a value's canonical one).
                var key = FieldKey(field.Field);
                var name = field.Field;
                var canonical = field.Canonical;
                return (in HookArgs args) =>
                {
                    var fields = SimEngine.FieldsOf(args.State, args.Owner);
                    var at = args.Copy > 0 ? CopyFieldKey(args.Copy, name) : key;
                    return fields.Has(at) ? fields[at] : canonical;
                };
            }
            case HookHistory history:
            {
                var query = history.Query;
                var any = history.Any;
                return (in HookArgs args) =>
                {
                    // The owner's player's ledger (a player's model); anyone's entries are in every player's.
                    var player = Who.IsPlayer(args.Owner) ? args.Owner : query.Whose == HistoryWhose.Anyone ? Who.You : Who.None;
                    if (player == Who.None || SimEngine.HistoryCount(args.State, player, query) is not { } count)
                    {
                        args.State.Unmodelled ??= SimEngine.HistoryUnknown;
                        return 0m;
                    }
                    return any ? count > 0 ? 1m : 0m : count;
                };
            }
            default:
                throw new InvalidOperationException($"{Name}: no value {value.GetType().Name}");
        }
    }

    private static Evaluate CompileMath(HookOperation operation, Evaluate left, Evaluate right) => operation switch
    {
        HookOperation.Add => (in HookArgs args) => left(in args) + right(in args),
        HookOperation.Subtract => (in HookArgs args) => left(in args) - right(in args),
        HookOperation.Multiply => (in HookArgs args) => left(in args) * right(in args),
        HookOperation.Divide => (in HookArgs args) => right(in args) is var divisor && divisor != 0m ? left(in args) / divisor : 0m,
        HookOperation.Remainder => (in HookArgs args) => right(in args) is var divisor && divisor != 0m ? left(in args) % divisor : 0m,
        HookOperation.Equal => (in HookArgs args) => left(in args) == right(in args) ? 1m : 0m,
        HookOperation.NotEqual => (in HookArgs args) => left(in args) != right(in args) ? 1m : 0m,
        HookOperation.Less => (in HookArgs args) => left(in args) < right(in args) ? 1m : 0m,
        HookOperation.LessOrEqual => (in HookArgs args) => left(in args) <= right(in args) ? 1m : 0m,
        HookOperation.Greater => (in HookArgs args) => left(in args) > right(in args) ? 1m : 0m,
        HookOperation.GreaterOrEqual => (in HookArgs args) => left(in args) >= right(in args) ? 1m : 0m,
        HookOperation.And => (in HookArgs args) => left(in args) != 0m && right(in args) != 0m ? 1m : 0m,
        HookOperation.Min => (in HookArgs args) => Math.Min(left(in args), right(in args)),
        HookOperation.Max => (in HookArgs args) => Math.Max(left(in args), right(in args)),
        _ => (in HookArgs args) => left(in args) != 0m || right(in args) != 0m ? 1m : 0m,
    };

    private Evaluate CompileFact(HookFactValue fact)
    {
        var named = fact.Of;
        switch (fact.Fact)
        {
            case HookFact.Amount when Spec.Kind == ModelKind.Enchantment:
                return (in HookArgs args) => args.Card?.Enchantment?.Amount ?? 0;
            case HookFact.PlayIsModelCard: return (in HookArgs args) => args.Card != null && ReferenceEquals(args.Play?.Card, args.Card) ? 1m : 0m;
            case HookFact.ModelCardTargets: return (in HookArgs args) => (int)(args.Card?.Targets ?? SimPlay.Target.None);
            case HookFact.Amount:
                return Spec.Kind == ModelKind.Power
                    ? (in HookArgs args) => args.Copy > 0 ? CopyOf(args.State, args.Owner, args.Copy).Amount : SimEngine.PowerOn(args.State, args.Owner, ThisPower)
                    : (in HookArgs args) => SimEngine.RelicsOf(args.State, args.Owner)[ThisRelic];
            case HookFact.PlayIsOwners: return (in HookArgs args) => args.Play?.Owner == args.Owner ? 1m : 0m;
            case HookFact.CardType: return (in HookArgs args) => (int)CardTypeOf(in args);
            // Ethereal: the card drawn as its profile has it, the card played, or another event's card (one exhausted, made).
            case HookFact.CardEthereal: return (in HookArgs args) => (args.Hook == HookName.AfterCardDrawn ? args.State.DrawnCard?.Ethereal
                : IsCardEventHook(args.Hook) ? args.State.EventCard?.Ethereal : args.Play?.Card.Ethereal) == true ? 1m : 0m;
            case HookFact.CardEnergy: return (in HookArgs args) => args.Play?.Card.Energy ?? 0;
            case HookFact.PlayStars: return (in HookArgs args) => args.Play?.Card.Stars ?? 0;
            case HookFact.PlayIsFirst:
            {
                // Its bit of the play's FirstThisTurn: the first play at all, of a type, with a tag (read once, never per call).
                var firstOf = fact.Of ?? "";
                var bit = firstOf.StartsWith("Type:", StringComparison.Ordinal) ? SimKeys.FirstOfType(Enum.Parse<SimCardType>(firstOf[5..]))
                    : firstOf.StartsWith("Tag:", StringComparison.Ordinal) ? SimKeys.FirstWithTag(Enum.Parse<SimCardTag>(firstOf[4..])) : 1;
                return (in HookArgs args) => args.Play is { } first && (first.FirstThisTurn & bit) != 0 ? 1m : 0m;
            }
            case HookFact.PlayIsLast: return (in HookArgs args) => args.Play is { } last && last.PlayIndex == last.PlayCount - 1 ? 1m : 0m;
            // As the plays before left it: once upgraded, the card in play is its upgraded copy (decision #218).
            case HookFact.CardUpgradable: return (in HookArgs args) => args.Play?.Card.Upgradable == true ? 1m : 0m;
            case HookFact.HpLostSoFar: return (in HookArgs args) => args.HpLost;
            // PowerCmd.Apply's scaling for the players (decision #234): the amount it's given, the players, the fight's scaling.
            case HookFact.ScaledAmount: return (in HookArgs args) => args.PowerAmount;
            case HookFact.PlayerCount: return (in HookArgs args) => 1 + args.State.Teammates.Count;
            case HookFact.EncounterScaling:
                return (in HookArgs args) =>
                {
                    if (args.State.EncounterScaling is { } scaling) return scaling;
                    args.State.Unmodelled ??= "the fight's multiplayer scaling isn't read: a power scaled for the players can't be worked out";
                    return 1m;
                };
            case HookFact.CausedByEthereal: return (in HookArgs args) => args.Hook == HookName.AfterCardExhausted && args.State.EtherealNow ? 1m : 0m;
            case HookFact.CardTag:
            {
                var tag = Enum.Parse<SimCardTag>(named!);
                return (in HookArgs args) => args.Play?.Card.Is(tag) == true ? 1m : 0m;
            }
            case HookFact.EventIsOwners: return (in HookArgs args) => args.Player == args.Owner ? 1m : 0m;
            case HookFact.CardAffliction:
            {
                // The card played's affliction, by the simulator's name for its class (SimCard.Affliction).
                var affliction = Enum.TryParse<SimAffliction>(named, out var parsed) ? parsed : throw new InvalidOperationException($"{Name}: no affliction {named}");
                return (in HookArgs args) => args.Play?.Card.Affliction == affliction ? 1m : 0m;
            }
            case HookFact.CardIs:
                // The card played (a play hook's: cardPlay.Card is Soul), or the event's card; a card made at random has a class only its roll knows.
                return (in HookArgs args) =>
                {
                    var card = IsPlayHook(args.Hook) ? args.Play?.Card : args.State.EventCard;
                    if (card is { RandomClass: true }) args.State.Unmodelled ??= $"{Name} tests the class of a card made at random";
                    return card?.Name == named ? 1m : 0m;
                };
            case HookFact.SourceCardIs:
                return (in HookArgs args) => (args.Damage?.Card ?? args.Block?.Card)?.Name == named ? 1m : 0m;
            case HookFact.OwnersTurn: return (in HookArgs args) => Who.IsPlayer(args.Owner) != args.State.EnemyTurnNow ? 1m : 0m;
            case HookFact.EnemiesTurn: return (in HookArgs args) => args.State.EnemyTurnNow ? 1m : 0m;
            case HookFact.OrbIsOwners: return (in HookArgs args) => args.Orb.Player == args.Owner ? 1m : 0m;
            case HookFact.OrbIs:
            {
                var kind = Enum.Parse<OrbKind>(named!);
                return (in HookArgs args) => args.Orb.Kind == kind ? 1m : 0m;
            }
            case HookFact.PropsHave:
            {
                // The game's flag by its name (ValueProp's and SimProps' numbers are the same, but the name is what the reader checked).
                var flag = Enum.TryParse<SimProps>(named, out var parsedFlag) && parsedFlag != SimProps.None ? parsedFlag : throw new InvalidOperationException($"{Name}: no props flag {named}");
                return (in HookArgs args) => (args.Damage?.Props ?? args.Block?.Props ?? args.Hit?.Props ?? args.Attack?.Props)?.HasFlag(flag) == true ? 1m : 0m;
            }
            // An attack (decision #228): its id, the one under way, its card's and a damage's card's identity, whether it's a card's.
            case HookFact.AttackId: return (in HookArgs args) => args.Attack?.Id ?? 0;
            case HookFact.CurrentAttack: return (in HookArgs args) => args.State.CurrentAttack;
            case HookFact.AttackCardId: return (in HookArgs args) => args.Attack?.Card?.HandId ?? 0;
            case HookFact.SourceCardId: return (in HookArgs args) => (args.Damage?.Card ?? args.Hit?.Card)?.HandId ?? 0;
            case HookFact.AttackFromCard: return (in HookArgs args) => args.Attack?.Card != null ? 1m : 0m;
            case HookFact.CreatureIsPet:
            {
                var creature = CreatureNamed(fact);
                return (in HookArgs args) => CreatureOf(in args, creature) is var id && Who.IsPet(id) && Who.PetKindOf(id) == PetKind.Osty ? 1m : 0m;
            }
            case HookFact.CreatureId:
            {
                var creature = CreatureNamed(fact);
                return (in HookArgs args) => CreatureOf(in args, creature);
            }
            case HookFact.OrbValueSoFar: return (in HookArgs args) => args.OrbValue;
            case HookFact.TriggerCountSoFar: return (in HookArgs args) => args.TriggerCount;
            case HookFact.OrbIsFront: return (in HookArgs args) => args.Orb.Index == 0 ? 1m : 0m;
            case HookFact.HittableEnemyCount:
                return (in HookArgs args) =>
                {
                    var count = 0;
                    foreach (var enemy in args.State.Enemies) if (enemy.Hittable && enemy.Alive) count++;
                    return count;
                };
            case HookFact.OpponentsPowerAmount:
            {
                // GetOpponentsOf(owner) where alive: the living players and their living pets for an enemy, the living enemies for a player.
                var power = new PowerId(named!);
                return (in HookArgs args) =>
                {
                    var state = args.State;
                    var total = 0;
                    if (Who.IsEnemy(args.Owner))
                    {
                        foreach (var id in state.PlayerIds)
                        {
                            var player = state.Player(id)!;
                            if (player is { Hp: > 0, Died: false }) total += player.Powers[power];
                            if (player.OstyHp > 0) total += player.OstyPowers[power];
                            if (player.WuppyHp > 0) total += player.WuppyPowers[power];
                        }
                    }
                    else
                        foreach (var enemy in state.Enemies)
                            if (enemy.Alive) total += enemy.Powers[power];
                    return total;
                };
            }
            case HookFact.ApplierIsOwner: return (in HookArgs args) => args.Applier == args.Owner ? 1m : 0m;
            case HookFact.PowerIs: return (in HookArgs args) => args.Power.Name == named ? 1m : 0m;
            case HookFact.PowerIn:
            {
                var names = (named ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
                return (in HookArgs args) => names.Contains(args.Power.Name) ? 1m : 0m;
            }
            case HookFact.PowerAmount: return (in HookArgs args) => args.PowerAmount;
            case HookFact.PowerIsDebuff: return (in HookArgs args) => args.Debuff ? 1m : 0m;
            case HookFact.StarsSpent: return (in HookArgs args) => args.Event == CardEvent.StarsSpent ? args.Play?.Card.Stars ?? 0 : 0;
            case HookFact.PowerOnEnemy: return (in HookArgs args) => Who.IsEnemy(args.Target) ? 1m : 0m;
            case HookFact.DealerIsOwner: return (in HookArgs args) => (args.Damage?.Dealer ?? args.Hit?.Dealer) == args.Owner ? 1m : 0m;
            case HookFact.DealerIsOwnersPet: return (in HookArgs args) => Who.IsPlayer(args.Owner) && Who.PetOwnerOf(args.Damage?.Dealer ?? args.Hit?.Dealer ?? Who.None) == args.Owner ? 1m : 0m;
            case HookFact.TargetIsOwner: return (in HookArgs args) => (args.Damage?.Target ?? args.Block?.Target ?? args.Hit?.Target) == args.Owner ? 1m : 0m;
            case HookFact.PoweredAttack: return (in HookArgs args) => (args.Damage?.Props ?? args.Hit?.Props ?? args.Attack?.Props)?.IsPoweredAttack() == true ? 1m : 0m;
            case HookFact.PoweredBlock: return (in HookArgs args) => args.Block?.Props.IsPoweredBlock() == true ? 1m : 0m;
            case HookFact.CardOrMonsterMove: return (in HookArgs args) => (args.Damage?.Props ?? args.Block?.Props ?? args.Hit?.Props)?.IsCardOrMonsterMove() == true ? 1m : 0m;
            case HookFact.FromCard: return (in HookArgs args) => (args.Damage?.Card ?? args.Block?.Card) != null ? 1m : 0m;
            // The damage's card where it is (decision #231): being played is having a card play (a card acting in your hand has none).
            case HookFact.SourceInPlay: return (in HookArgs args) => (args.Damage?.Card ?? args.Block?.Card) != null && (args.Damage?.Play ?? args.Block?.Play) != null ? 1m : 0m;
            case HookFact.SourcePlayIndex: return (in HookArgs args) => (args.Damage?.Play ?? args.Block?.Play)?.PlayIndex ?? 0;
            case HookFact.FromPlay: return (in HookArgs args) => (args.Damage?.Play ?? args.Block?.Play) != null ? 1m : 0m;
            case HookFact.SourceCardVar:
            {
                var key = named ?? throw new InvalidOperationException($"{Name}: a card value with no key");
                return (in HookArgs args) => (args.Damage?.Card ?? args.Block?.Card)?.Vars.TryGetValue(key, out var cardValue) == true ? cardValue : 0m;
            }
            case HookFact.CardIsOwners: return (in HookArgs args) => (args.Damage?.Play ?? args.Block?.Play)?.Owner == args.Owner ? 1m : 0m;
            // The side whose turn ends is the players' (a living player takes part); the side whose turn starts, the enemies' (a living enemy).
            case HookFact.TakesPart: return (in HookArgs args) => (args.Hook is HookName.BeforeSideTurnStart or HookName.AfterSideTurnStart or HookName.AfterSideTurnStartLate
                ? args.State.Enemy(args.Owner) is { Alive: true } : args.State.TakesPart(args.Owner)) ? 1m : 0m;
            case HookFact.TakeBound: return (in HookArgs args) => Take(in args) ? 1m : 0m;
            case HookFact.BoundTaken: return (in HookArgs args) => args.Taken?[0] ?? 0;
            case HookFact.BoundHeld: return (in HookArgs args) => HoldsAny(in args) ? 1m : 0m;
            case HookFact.BoundToPlay: return (in HookArgs args) => args.State.Bound.Has(BoundKey(in args)) ? 1m : 0m;
            case HookFact.HitUnblocked: return (in HookArgs args) => args.Hit?.Unblocked ?? 0;
            case HookFact.HitBlocked: return (in HookArgs args) => args.Hit?.Blocked ?? 0;
            // The damage's card is the card in play, bound (what its BeforeCardPlayed added for the play, still there).
            case HookFact.SourceBound: return (in HookArgs args) => (args.Hit?.Card ?? args.Damage?.Card) is { } source && args.State.CurrentPlay is { } inPlay && source == inPlay.Card
                && args.State.Bound.Has(BoundKey(in args)) ? 1m : 0m;
            case HookFact.HookCardThere: return (in HookArgs args) => args.Play != null ? 1m : 0m;
            case HookFact.CreatureAlive:
            {
                var creature = CreatureNamed(fact);
                return (in HookArgs args) => CreatureOf(in args, creature) switch
                {
                    var pet when Who.IsPet(pet) => SimEngine.PetHp(args.State, pet) > 0 ? 1m : 0m,
                    var player when Who.IsPlayer(player) => args.State.Player(player) is { Hp: > 0, Died: false } ? 1m : 0m,
                    var id => args.State.Enemy(id) is { Alive: true } ? 1m : 0m,
                };
            }
            case HookFact.CreatureHp:
            {
                var creature = CreatureNamed(fact);
                return (in HookArgs args) =>
                {
                    var id = CreatureOf(in args, creature);
                    if (Who.IsPet(id)) return Math.Max(0, SimEngine.PetHp(args.State, id));
                    if (!Who.IsPlayer(id)) return args.State.Enemy(id) is { Alive: true } enemy ? enemy.Hp : 0m;
                    if (args.State.Player(id) is not { } player || player.Died) return 0m;
                    if (player.Hp == int.MaxValue) args.State.Unmodelled ??= $"{Name} asks a player's HP, which isn't known";
                    return Math.Max(0, player.Hp);
                };
            }
            case HookFact.HitTotal: return (in HookArgs args) => args.Hit?.Total ?? 0;
            case HookFact.PlayCountSoFar: return (in HookArgs args) => args.PlayCount;
            case HookFact.OwnerBlock: return (in HookArgs args) => args.State.Player(args.Owner)?.Block ?? args.State.Enemy(args.Owner)?.Block ?? 0;
            case HookFact.OwnerHandCount: return (in HookArgs args) => args.State.Player(args.Owner)?.HandCount ?? 0;
            case HookFact.OwnerOrbCount:
            {
                var kind = Enum.Parse<OrbKind>(named!);
                return (in HookArgs args) =>
                {
                    var count = 0;
                    if (args.State.Player(args.Owner) is { } player) foreach (var orb in player.Orbs) if (orb.Kind == kind) count++;
                    return count;
                };
            }
            case HookFact.DeadIsApplier: return (in HookArgs args) => ApplierOf(in args) is var applier && applier != Who.None && applier == args.Target ? 1m : 0m;
            case HookFact.DeadIsOwner: return (in HookArgs args) => args.Target == args.Owner ? 1m : 0m;
            case HookFact.HasApplier: return (in HookArgs args) => ApplierOf(in args) != Who.None ? 1m : 0m;
            case HookFact.ApplierIsPlayer: return (in HookArgs args) => Who.IsPlayer(ApplierOf(in args)) ? 1m : 0m;
            case HookFact.PlayIsAppliers: return (in HookArgs args) => args.Play is { } play && ApplierOf(in args) is var applier && applier != Who.None && play.Owner == applier ? 1m : 0m;
            case HookFact.TurnNumber: return (in HookArgs args) => args.State.TurnNumber + (args.NextTurn ? 1 : 0);
            case HookFact.OwnerDead: return (in HookArgs args) => (Who.IsPet(args.Owner) ? SimEngine.PetHp(args.State, args.Owner) <= 0
                : args.State.Player(args.Owner) is { } player ? player.Died || player.Hp <= 0 : args.State.Enemy(args.Owner) is not { Alive: true }) ? 1m : 0m;
            case HookFact.CreatureThere:
            {
                var creature = CreatureNamed(fact);
                return (in HookArgs args) => CreatureOf(in args, creature) != Who.None ? 1m : 0m;
            }
            case HookFact.CreatureIsPlayer:
            {
                var creature = CreatureNamed(fact);
                return (in HookArgs args) => Who.IsPlayer(CreatureOf(in args, creature)) ? 1m : 0m;
            }
            case HookFact.CreatureIsMonster:
            {
                // An enemy, or a pet (Osty is a monster that fights for its player: Creature.IsMonster).
                var creature = CreatureNamed(fact);
                return (in HookArgs args) => CreatureOf(in args, creature) is var id && (Who.IsEnemy(id) || Who.IsPet(id)) ? 1m : 0m;
            }
            case HookFact.CreatureHasPower:
            {
                var creature = CreatureNamed(fact);
                var power = new PowerId(named!);
                return (in HookArgs args) => CreatureOf(in args, creature) is var id && id != Who.None && SimEngine.HasPower(args.State, id, power) ? 1m : 0m;
            }
            case HookFact.CreatureHasRelic:
            {
                var creature = CreatureNamed(fact);
                var relic = new RelicId(named!);
                return (in HookArgs args) => CreatureOf(in args, creature) is var id && Who.IsPlayer(id) && SimEngine.RelicsOf(args.State, id).Has(relic) ? 1m : 0m;
            }
            case HookFact.CreaturePowerAmount:
            {
                var creature = CreatureNamed(fact);
                var power = new PowerId(named!);
                return (in HookArgs args) => CreatureOf(in args, creature) is var id && id != Who.None ? SimEngine.PowerOn(args.State, id, power) : 0m;
            }
            case HookFact.SameCreature:
            {
                var (creature, other) = (CreatureNamed(fact), fact.Other ?? throw new InvalidOperationException($"{Name}: {fact.Fact} names one creature"));
                return (in HookArgs args) => CreatureOf(in args, creature) is var id && id != Who.None && id == CreatureOf(in args, other) ? 1m : 0m;
            }
            case HookFact.SameSide:
            {
                var (creature, other) = (CreatureNamed(fact), fact.Other ?? throw new InvalidOperationException($"{Name}: {fact.Fact} names one creature"));
                return (in HookArgs args) => CreatureOf(in args, creature) is var id && CreatureOf(in args, other) is var otherId && id != Who.None && otherId != Who.None
                    && Who.IsEnemy(id) == Who.IsEnemy(otherId) ? 1m : 0m;
            }
            case HookFact.CreatureOnPlayersSide:
            {
                var creature = CreatureNamed(fact);
                return (in HookArgs args) => CreatureOf(in args, creature) is var id && id != Who.None && !Who.IsEnemy(id) ? 1m : 0m;
            }
            default: throw new InvalidOperationException($"{Name}: no fact {fact.Fact}");
        }
    }

    private HookCreature CreatureNamed(HookFactValue fact) => fact.Creature ?? throw new InvalidOperationException($"{Name}: {fact.Fact} names no creature");

    /// <summary>
    /// The creature a hook's code names, as the simulator has it (Who.None: none, a null in the game's code): the owner, the damage's dealer
    /// and target (a Modify hook's, a block's, a hit's), the creature that died or a power landed on, the applier it keeps; a pet's player for
    /// its PetOwner (a pet's: its player), none for anyone else's.
    /// </summary>
    private int CreatureOf(in HookArgs args, HookCreature creature)
    {
        var id = creature.Who switch
        {
            HookWho.Owner => args.Owner,
            HookWho.Dealer => args.Damage is { } damage ? damage.Dealer ?? Who.None : args.Hit?.Dealer ?? Who.None,
            HookWho.Target => args.Damage?.Target ?? args.Block?.Target ?? args.Hit?.Target ?? Who.None,
            HookWho.Dead or HookWho.PowerOwner or HookWho.RedirectSoFar or HookWho.Asked => args.Target,
            HookWho.Applier => ApplierOf(in args),
            HookWho.Each => args.Each,
            HookWho.Attacker => args.Attack?.Dealer ?? Who.None,
            HookWho.AttackCardOwner => args.Attack?.Card != null ? args.Play?.Owner ?? Who.None : Who.None,
            _ => Who.None,
        };
        return creature.PetOwner ? Who.PetOwnerOf(id) : id;
    }

    /// <summary>Who applied this power: its Data (DataCreature), as a read power that asks keeps it (ModelSpec.UsesApplier); Who.None when no one.</summary>
    private int ApplierOf(in HookArgs args) =>
        DataCreature.In(args.Copy > 0 ? CopyOf(args.State, args.Owner, args.Copy).Data : SimEngine.PowersOf(args.State, args.Owner).DataOf(ThisPower));

    /// <summary>A card event's hook (the card drawn, exhausted, discarded, made...: SimState.EventCard, a draw's its profile).</summary>
    private static bool IsCardEventHook(HookName hook) => hook is HookName.AfterCardDrawn or HookName.AfterCardExhausted or HookName.AfterCardDiscarded
        or HookName.AfterCardGeneratedForCombat or HookName.AfterShuffle or HookName.AfterStarsSpent or HookName.AfterStarsGained or HookName.AfterEnergySpent
        or HookName.AfterArrowDislodged;

    /// <summary>A hook whose card is the card played (its play's or its count's), not an event's.</summary>
    private static bool IsPlayHook(HookName hook) =>
        hook is HookName.BeforeCardPlayed or HookName.AfterCardPlayed or HookName.ModifyCardPlayCount or HookName.AfterModifyingCardPlayCount;

    /// <summary>
    /// The type of the hook's card: the card played, or the event's card (a made card's, as the code that made it names it; one drawn as its
    /// profile says). A made card no code names (a roll) has none here: it isn't the card played.
    /// </summary>
    private static SimCardType CardTypeOf(in HookArgs args)
    {
        if (args.Hook is HookName.BeforeAttack or HookName.AfterAttack) return args.Attack?.Card?.Type ?? SimCardType.None;
        if (IsPlayHook(args.Hook)) return args.Play?.Card.Type ?? SimCardType.None;
        return args.Event switch
        {
            CardEvent.Drawn => args.State.DrawnCard is { Status: true } ? SimCardType.Status : args.State.DrawnCard?.Type ?? args.State.EventCard?.Type ?? SimCardType.None,
            CardEvent.Generated => args.State.EventCard?.Type ?? SimCardType.None,
            _ => args.State.EventCard?.Type ?? args.Play?.Card.Type ?? SimCardType.None,
        };
    }
}
