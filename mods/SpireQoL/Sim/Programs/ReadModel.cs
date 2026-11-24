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
    /// <summary>A card event and who it happened to (AfterCardEvent).</summary>
    public CardEvent Event { get; init; }
    public int Player { get; init; }
    /// <summary>A power landing (AfterPowerApplied): which, how much, who applied it, on whom, as a debuff.</summary>
    public PowerId Power { get; init; }
    public int PowerAmount { get; init; }
    public int Applier { get; init; }
    public int Target { get; init; }
    public bool Debuff { get; init; }
    /// <summary>The card play's count so far (ModifyCardPlayCount).</summary>
    public int PlayCount { get; init; }
    /// <summary>The damage or block being worked out (the Modify hooks).</summary>
    public DamageCtx? Damage { get; init; }
    public BlockCtx? Block { get; init; }

    /// <summary>
    /// The answers of the code's tests this hook run has asked (HookTest, by its slot: 0 not yet, 1 held, 2 didn't), for a program that stores to
    /// its own state; null: every test is asked where it's needed.
    /// </summary>
    public int[]? Asked { get; init; }

    /// <summary>What this hook run took back of the amount its model bound to the play (HookFact.TakeBound, UnbindStep); null: it takes none.</summary>
    public int[]? Taken { get; init; }

    /// <summary>The same hook on another branch (its answers so far its own).</summary>
    public HookArgs On(SimState branch) => this with { State = branch, Asked = (int[]?)Asked?.Clone(), Taken = (int[]?)Taken?.Clone() };
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

    private readonly CompiledStep[]?[] _steps = new CompiledStep[]?[Enum.GetValues<HookName>().Length];
    /// <summary>The hooks whose program stores to the model's own state: their tests are asked once a run (HookTest).</summary>
    private readonly bool[] _asksOnce = new bool[Enum.GetValues<HookName>().Length];
    /// <summary>Each test place's slot in HookArgs.Asked.</summary>
    private readonly Dictionary<string, int> _testSlots = new(StringComparer.Ordinal);
    private readonly Evaluate?[] _returns = new Evaluate?[Enum.GetValues<HookName>().Length];

    /// <summary>The hooks whose program takes back what the model bound to the play: each run gets a slot for it (HookArgs.Taken).</summary>
    private readonly bool[] _takes = new bool[Enum.GetValues<HookName>().Length];

    public ReadModel(ModelSpec spec) : base(spec.Name, spec.Hooks.Select(program => SimHookOf(program.Hook)).Distinct())
    {
        Spec = spec;
        foreach (var test in spec.Values().OfType<HookTest>()) _testSlots.TryAdd(test.At, _testSlots.Count);
        foreach (var program in spec.Hooks)
        {
            var one = new ModelSpec(spec.Name, spec.Kind, new[] { program });
            // A take of what it bound is a test with an effect: asked once a run, as a store's tests are.
            _takes[(int)program.Hook] = one.Steps().Any(step => step is UnbindStep) || one.Values().Any(value => value is HookFactValue { Fact: HookFact.TakeBound or HookFact.BoundTaken });
            _asksOnce[(int)program.Hook] = one.Steps().Any(step => step is StoreFieldStep) || _takes[(int)program.Hook];
            if (program.Returns != null) _returns[(int)program.Hook] = Compile(program.Returns);
            _steps[(int)program.Hook] = CompileSteps(program.Steps, new List<HookStep>(), firstRound: true);
        }
    }

    /// <summary>
    /// Steps compiled in order. A hook that acts logs one firing (the hand-written models' Fire): a draw, energy, a power or an orb, once for the
    /// steps on one condition (<paramref name="firing"/>: the ones before); a counter loop's later rounds log none (<paramref name="firstRound"/>).
    /// </summary>
    private CompiledStep[] CompileSteps(IReadOnlyList<HookStep> steps, List<HookStep> firing, bool firstRound)
    {
        var compiled = new List<CompiledStep>();
        foreach (var step in steps)
        {
            var acts = step is DrawStep or GainEnergyStep or ApplyPowerStep or ChannelStep;
            var fires = firstRound && acts && !firing.Any(earlier => Equals(earlier.When, step.When));
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
        HookName.ModifyDamageAdditive => nameof(ModifyDamageAdditive),
        HookName.ModifyDamageMultiplicative => nameof(ModifyDamageMultiplicative),
        HookName.ModifyDamageMultiplicativeLate => nameof(ModifyDamageMultiplicativeLate),
        HookName.ModifyBlockAdditive => nameof(ModifyBlockAdditive),
        HookName.ModifyBlockMultiplicative => nameof(ModifyBlockMultiplicative),
        _ => nameof(AfterCardEvent),
    };

    /// <summary>The game hook a card event is heard in (its After... hook); null for one no game hook hears (energy gained).</summary>
    public static HookName? HookOf(CardEvent cardEvent) => cardEvent switch
    {
        CardEvent.Drawn => HookName.AfterCardDrawn,
        CardEvent.Exhausted => HookName.AfterCardExhausted,
        CardEvent.Discarded => HookName.AfterCardDiscarded,
        CardEvent.Generated or CardEvent.GeneratedStatus or CardEvent.GeneratedSadness => HookName.AfterCardGeneratedForCombat,
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

    /// <summary>Hook.ModifyCardPlayCount: the value of its code's first way that holds (playCount, playCount + 1...), decided once before the plays.</summary>
    public override int ModifyCardPlayCount(SimState state, int owner, SimCardPlay play, int count)
    {
        if (_returns[(int)HookName.ModifyCardPlayCount] is not { } returns) return count;
        var args = new HookArgs { State = state, Owner = owner, Hook = HookName.ModifyCardPlayCount, Play = play, PlayCount = count };
        return (int)returns(in args);
    }

    /// <summary>After it changed a card play's count (a stack used: PowerCmd.Decrement; Throwing Axe's UsedThisCombat): deterministic, on the state given.</summary>
    public override void AfterModifyingCardPlayCount(SimState state, int owner, SimCardPlay play)
    {
        foreach (var branch in Run(HookName.AfterModifyingCardPlayCount, new HookArgs { State = state, Owner = owner, Play = play }))
            if (!ReferenceEquals(branch, state)) throw new InvalidOperationException($"{Name}: AfterModifyingCardPlayCount branched");
    }

    /// <summary>A hook of the players' side's turn ending (decision #51), in its place among them.</summary>
    private IEnumerable<SimState> TurnEnd(HookName hook, SimState state, int owner) => Run(hook, new HookArgs { State = state, Owner = owner, Play = state.CurrentPlay });

    public override decimal ModifyDamageAdditive(SimState state, int owner, DamageCtx damage) => Returned(HookName.ModifyDamageAdditive, state, owner, damage, otherwise: 0m);
    public override decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage) => Returned(HookName.ModifyDamageMultiplicative, state, owner, damage, otherwise: 1m);
    public override decimal ModifyDamageMultiplicativeLate(SimState state, int owner, DamageCtx damage) => Returned(HookName.ModifyDamageMultiplicativeLate, state, owner, damage, otherwise: 1m);
    public override decimal ModifyBlockAdditive(SimState state, int owner, BlockCtx block) => Returned(HookName.ModifyBlockAdditive, state, owner, block, otherwise: 0m);
    public override decimal ModifyBlockMultiplicative(SimState state, int owner, BlockCtx block) => Returned(HookName.ModifyBlockMultiplicative, state, owner, block, otherwise: 1m);

    private decimal Returned(HookName hook, SimState state, int owner, DamageCtx damage, decimal otherwise)
    {
        if (_returns[(int)hook] is not { } returns) return otherwise;
        var args = new HookArgs { State = state, Owner = owner, Damage = damage, Play = damage.Play };
        return returns(in args);
    }

    private decimal Returned(HookName hook, SimState state, int owner, BlockCtx block, decimal otherwise)
    {
        if (_returns[(int)hook] is not { } returns) return otherwise;
        var args = new HookArgs { State = state, Owner = owner, Block = block, Play = block.Play };
        return returns(in args);
    }

    /// <summary>
    /// A hook's commands in order, each on its own condition asked as it runs (a store before it is seen), each on every branch the ones before
    /// it left: a random target branches, and identical branches merge (SimEngine.Then).
    /// </summary>
    private IEnumerable<SimState> Run(HookName hook, HookArgs args)
    {
        if (_steps[(int)hook] is not { } steps) return One(args.State);
        args = args with { Hook = hook, Asked = _asksOnce[(int)hook] ? new int[_testSlots.Count] : null, Taken = _takes[(int)hook] ? new int[1] : null };
        return RunSteps(steps, args);
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
                    args.State.Bound = args.State.Bound.With(SimKeys.Owned(Name, args.Owner), (int)value(in args));
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
            case GainBlockStep gain:
            {
                var amount = Compile(gain.Amount);
                var props = Compile(gain.Props);
                return args => SimEngine.ProcBlock(args.State, args.Owner, Name, amount(in args), (SimProps)(int)props(in args));
            }
            case DamageStep damage:
            {
                var amount = Compile(damage.Amount);
                var props = Compile(damage.Props);
                var noDealer = damage.NoDealer;
                return damage.Target switch
                {
                    HookTarget.AllEnemies => args => SimEngine.ProcDamageAll(args.State, args.Owner, Name, amount(in args), (SimProps)(int)props(in args), noDealer: noDealer),
                    HookTarget.RandomEnemy => args => SimEngine.ProcDamageRandom(args.State, args.Owner, Name, amount(in args), (SimProps)(int)props(in args), noDealer: noDealer),
                    HookTarget.PowerOwner => args => SimEngine.ProcDamageOn(args.State, args.Owner, Name, args.Target, amount(in args), (SimProps)(int)props(in args), noDealer: noDealer),
                    _ => throw new InvalidOperationException($"{Name}: no damage to {damage.Target}"),
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
            case ApplyPowerStep apply:
            {
                var amount = Compile(apply.Amount);
                var power = new PowerId(apply.Power);
                var debuff = apply.Debuff;
                var target = apply.Target;
                return args =>
                {
                    var state = args.State;
                    var given = (int)amount(in args);
                    switch (target)
                    {
                        case HookTarget.Owner: SimEngine.ApplyPower(state, args.Owner, args.Owner, power, given, debuff); break;
                        case HookTarget.PowerOwner: SimEngine.ApplyPower(state, args.Owner, args.Target, power, given, debuff); break;
                        case HookTarget.AllEnemies:
                            foreach (var enemy in state.Targetable.ToList()) SimEngine.ApplyPower(state, args.Owner, enemy.Id, power, given, debuff);
                            break;
                        default: throw new InvalidOperationException($"{Name}: no power on {target}");
                    }
                    if (fires) state.LogFired(Name, amount: 0, args.Owner);
                    return One(state);
                };
            }
            case StoreFieldStep store:
            {
                var value = Compile(store.Value);
                var key = FieldKey(store.Field);
                return args =>
                {
                    SimEngine.SetField(args.State, args.Owner, key, (int)value(in args));
                    return One(args.State);
                };
            }
            case RemovePowerStep:
                return args =>
                {
                    SimEngine.SetPowers(args.State, args.Owner, SimEngine.PowersOf(args.State, args.Owner).Without(ThisPower));
                    return One(args.State);
                };
            case DecrementPowerStep:
                return args =>
                {
                    SimEngine.DecrementPower(args.State, args.Owner, ThisPower);
                    return One(args.State);
                };
            default:
                throw new InvalidOperationException($"{Name}: no command for {step.GetType().Name}");
        }
    }

    /// <summary>
    /// What it bound to the play in progress (its own data's Remove(cardPlay.Card, out amount)): taken out of the state into this run's slot;
    /// false (and 0) when nothing was bound.
    /// </summary>
    private bool Take(in HookArgs args)
    {
        var key = SimKeys.Owned(Name, args.Owner);
        var state = args.State;
        var bound = state.Bound.Has(key);
        if (args.Taken is { } taken) taken[0] = bound ? state.Bound[key] : 0;
        if (bound) state.Bound = state.Bound.Without(key);
        return bound;
    }

    /// <summary>One of its own fields' key among its owner's (SimPlayer.Fields, SimEnemy.Fields): "Model.Field".</summary>
    public string FieldKey(string field) => Name + "." + field;

    /// <summary>Card events it causes (draws, energy), each through every listener; a draw or energy tab counts what they gave (decision #132).</summary>
    private IEnumerable<SimState> Cause(HookArgs args, CardEvent cardEvent, int count, bool fires)
    {
        if (fires) args.State.LogFired(Name, amount: 0, args.Owner);
        return SimEngine.ProcCardEvent(args.State, cardEvent, count, args.Owner, onEach: SimEngine.TallyFor(Name, Tab, cardEvent, args.Owner));
    }

    // ---- values

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
                var canonical = (int)variable.Canonical;
                return (in HookArgs args) => SimEngine.Var(args.State, args.Owner, Name, key, canonical);
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
            case HookField field:
            {
                var key = FieldKey(field.Field);
                return (in HookArgs args) => SimEngine.FieldsOf(args.State, args.Owner)[key];
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
        _ => (in HookArgs args) => left(in args) != 0m || right(in args) != 0m ? 1m : 0m,
    };

    private Evaluate CompileFact(HookFactValue fact)
    {
        var named = fact.Of;
        switch (fact.Fact)
        {
            case HookFact.Amount:
                return Spec.Kind == ModelKind.Power
                    ? (in HookArgs args) => SimEngine.PowerOn(args.State, args.Owner, ThisPower)
                    : (in HookArgs args) => SimEngine.RelicsOf(args.State, args.Owner)[ThisRelic];
            case HookFact.PlayIsOwners: return (in HookArgs args) => args.Play?.Owner == args.Owner ? 1m : 0m;
            case HookFact.CardType: return (in HookArgs args) => (int)CardTypeOf(in args);
            case HookFact.CardEthereal: return (in HookArgs args) => (args.Hook == HookName.AfterCardDrawn ? args.State.DrawnCard?.Ethereal : args.Play?.Card.Ethereal) == true ? 1m : 0m;
            case HookFact.CardEnergy: return (in HookArgs args) => args.Play?.Card.Energy ?? 0;
            case HookFact.CardTag:
            {
                var tag = Enum.Parse<SimCardTag>(named!);
                return (in HookArgs args) => args.Play?.Card.Is(tag) == true ? 1m : 0m;
            }
            case HookFact.EventIsOwners: return (in HookArgs args) => args.Player == args.Owner ? 1m : 0m;
            case HookFact.CardIs:
                return (in HookArgs args) => named == CardId.Sadness.Name && args.Event == CardEvent.GeneratedSadness
                    || args.State.EventCard?.Name == named ? 1m : 0m;
            case HookFact.OwnersTurn: return (in HookArgs args) => Who.IsPlayer(args.Owner) != args.State.EnemyTurnNow ? 1m : 0m;
            case HookFact.ApplierIsOwner: return (in HookArgs args) => args.Applier == args.Owner ? 1m : 0m;
            case HookFact.PowerIs: return (in HookArgs args) => args.Power.Name == named ? 1m : 0m;
            case HookFact.PowerIn:
            {
                var names = (named ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
                return (in HookArgs args) => names.Contains(args.Power.Name) ? 1m : 0m;
            }
            case HookFact.PowerAmount: return (in HookArgs args) => args.PowerAmount;
            case HookFact.PowerIsDebuff: return (in HookArgs args) => args.Debuff ? 1m : 0m;
            case HookFact.PowerOnEnemy: return (in HookArgs args) => Who.IsEnemy(args.Target) ? 1m : 0m;
            case HookFact.DealerIsOwner: return (in HookArgs args) => args.Damage?.Dealer == args.Owner ? 1m : 0m;
            case HookFact.DealerIsOwnersPet: return (in HookArgs args) => args.Owner == Who.You && args.Damage?.Dealer == Who.YourOsty ? 1m : 0m;
            case HookFact.TargetIsOwner: return (in HookArgs args) => (args.Damage?.Target ?? args.Block?.Target) == args.Owner ? 1m : 0m;
            case HookFact.PoweredAttack: return (in HookArgs args) => args.Damage?.Props.IsPoweredAttack() == true ? 1m : 0m;
            case HookFact.PoweredBlock: return (in HookArgs args) => args.Block?.Props.IsPoweredBlock() == true ? 1m : 0m;
            case HookFact.FromCard: return (in HookArgs args) => (args.Damage?.Card ?? args.Block?.Card) != null ? 1m : 0m;
            case HookFact.CardIsOwners: return (in HookArgs args) => (args.Damage?.Play ?? args.Block?.Play)?.Owner == args.Owner ? 1m : 0m;
            case HookFact.TakesPart: return (in HookArgs args) => args.State.TakesPart(args.Owner) ? 1m : 0m;
            case HookFact.TakeBound: return (in HookArgs args) => Take(in args) ? 1m : 0m;
            case HookFact.BoundTaken: return (in HookArgs args) => args.Taken?[0] ?? 0;
            case HookFact.PlayCountSoFar: return (in HookArgs args) => args.PlayCount;
            case HookFact.OwnerBlock: return (in HookArgs args) => args.State.Player(args.Owner)?.Block ?? args.State.Enemy(args.Owner)?.Block ?? 0;
            case HookFact.OwnerHandCount: return (in HookArgs args) => args.State.Player(args.Owner)?.HandCount ?? 0;
            default: throw new InvalidOperationException($"{Name}: no fact {fact.Fact}");
        }
    }

    /// <summary>The type of the hook's card: the card played, or the event's card (a made Status or Sadness by its event; one drawn as its profile says).</summary>
    private static SimCardType CardTypeOf(in HookArgs args)
    {
        if (args.Hook is HookName.BeforeCardPlayed or HookName.AfterCardPlayed or HookName.ModifyCardPlayCount or HookName.AfterModifyingCardPlayCount) return args.Play?.Card.Type ?? SimCardType.None;
        return args.Event switch
        {
            CardEvent.GeneratedStatus => SimCardType.Status,
            CardEvent.GeneratedSadness => SimCardType.Curse,
            CardEvent.Drawn => args.State.DrawnCard is { Status: true } ? SimCardType.Status : args.State.EventCard?.Type ?? SimCardType.None,
            _ => args.State.EventCard?.Type ?? args.Play?.Card.Type ?? SimCardType.None,
        };
    }
}
