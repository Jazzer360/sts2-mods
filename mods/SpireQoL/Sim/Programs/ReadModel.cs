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
    /// <summary>The damage or block being worked out (the Modify hooks).</summary>
    public DamageCtx? Damage { get; init; }
    public BlockCtx? Block { get; init; }

    /// <summary>The same hook on another branch.</summary>
    public HookArgs On(SimState branch) => this with { State = branch };
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
    private readonly Evaluate?[] _returns = new Evaluate?[Enum.GetValues<HookName>().Length];

    public ReadModel(ModelSpec spec) : base(spec.Name, spec.Hooks.Select(program => SimHookOf(program.Hook)).Distinct())
    {
        Spec = spec;
        foreach (var program in spec.Hooks)
        {
            if (program.Returns != null) _returns[(int)program.Hook] = Compile(program.Returns);
            var firing = new List<HookStep>();
            var compiled = new List<CompiledStep>();
            foreach (var step in program.Steps)
            {
                // A hook that acts logs one firing (the hand-written models' Fire): a draw, energy or a power, once for the steps on one condition.
                var fires = step is DrawStep or GainEnergyStep or ApplyPowerStep && !firing.Any(earlier => Equals(earlier.When, step.When));
                if (step is DrawStep or GainEnergyStep or ApplyPowerStep) firing.Add(step);
                compiled.Add(new CompiledStep(step.When == null ? null : Compile(step.When), Runner(step, fires)));
            }
            _steps[(int)program.Hook] = compiled.ToArray();
        }
    }

    /// <summary>The SimModel method a game hook is answered in (the card events' in AfterCardEvent, a power landing in AfterPowerApplied).</summary>
    public static string SimHookOf(HookName hook) => hook switch
    {
        HookName.BeforeCardPlayed => nameof(BeforeCardPlayed),
        HookName.AfterCardPlayed => nameof(AfterCardPlayed),
        HookName.AfterPowerAmountChanged => nameof(AfterPowerApplied),
        HookName.AfterSideTurnEnd => nameof(AfterSideTurnEnd),
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

    public override IEnumerable<SimState> AfterSideTurnEnd(SimState state, int owner) =>
        Run(HookName.AfterSideTurnEnd, new HookArgs { State = state, Owner = owner, Play = state.CurrentPlay });

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
        args = args with { Hook = hook };
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

    private Func<HookArgs, IEnumerable<SimState>> Runner(HookStep step, bool fires)
    {
        switch (step)
        {
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
            default: throw new InvalidOperationException($"{Name}: no fact {fact.Fact}");
        }
    }

    /// <summary>The type of the hook's card: the card played, or the event's card (a made Status or Sadness by its event; one drawn as its profile says).</summary>
    private static SimCardType CardTypeOf(in HookArgs args)
    {
        if (args.Hook is HookName.BeforeCardPlayed or HookName.AfterCardPlayed) return args.Play?.Card.Type ?? SimCardType.None;
        return args.Event switch
        {
            CardEvent.GeneratedStatus => SimCardType.Status,
            CardEvent.GeneratedSadness => SimCardType.Curse,
            CardEvent.Drawn => args.State.DrawnCard is { Status: true } ? SimCardType.Status : args.State.EventCard?.Type ?? SimCardType.None,
            _ => args.State.EventCard?.Type ?? args.Play?.Card.Type ?? SimCardType.None,
        };
    }
}
