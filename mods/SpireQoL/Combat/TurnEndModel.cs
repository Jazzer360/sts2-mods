using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Sim;
using Sts2Mods.Core.Game;

namespace SpireQoL.Combat;

public sealed record Source(string Name, int Amount);

/// <summary>
/// What happens between now and the start of your next turn, for Incoming Damage and the forecast
/// audit: a view of the outcome simulator (Sim/SimEngine) run on the current state. Random targets
/// are explored as branches; the numbers shown are the worst branch for you (most HP lost), and
/// TakenMin is the best branch (equal unless a random target changes the result).
/// </summary>
public sealed class TurnEndModel
{
    public List<Source> EndOfTurnBlock { get; } = new();

    /// <summary>Blockable damage that will actually land (self damage + enemy hits that happen).</summary>
    public List<Source> Blockable { get; } = new();
    public List<Source> Unblockable { get; } = new();

    /// <summary>Your end-of-turn damage to enemies (Hailstorm, The Bomb, Lightning...), for the audit log.</summary>
    public List<Source> EndOfTurnDamage { get; } = new();

    public int CurrentBlock { get; private set; }
    public int SkippedPoisoned { get; private set; }
    public int SkippedEndOfTurn { get; private set; }
    public int CancelledHits { get; private set; }
    public int DamageThrough { get; private set; }

    /// <summary>HP you lose by the start of your next turn, worst case. This is the widget's result.</summary>
    public int Taken { get; private set; }

    /// <summary>HP you lose in the best case (lower than Taken only when a random target matters).</summary>
    public int TakenMin { get; private set; }

    public int OstyHp { get; private set; }
    public int OstyTaken { get; private set; }
    public int OstyTakenMin { get; private set; }
    /// <summary>The worst branch kills you: nothing left to prevent death (Fairy in a Bottle, Lizard Tail).</summary>
    public bool Dies { get; private set; }
    /// <summary>In the worst branch you'd die, but Fairy in a Bottle (potions used) or Lizard Tail heals you instead.</summary>
    public int FairiesUsed { get; private set; }
    public bool LizardTailUsed { get; private set; }
    /// <summary>Your HP once the enemy turn is over (worst branch).</summary>
    public int HpAfter { get; private set; }
    public bool OstyDies => OstyHp > 0 && OstyTaken >= OstyHp;

    public int BlockGain => EndOfTurnBlock.Sum(s => s.Amount);
    public int BlockableTotal => Blockable.Sum(s => s.Amount);
    public int UnblockableTotal => Unblockable.Sum(s => s.Amount);
    public int ExpectedBlock => CurrentBlock + BlockGain;

    /// <summary>
    /// Settings "Count teammates' hands" (decision #38): what's shown also uses the cards in teammates' hands.
    /// Off (the default, decision #25): what's shown leaves them out; the checks use them either way.
    /// </summary>
    public static volatile bool CountTeammateHands;

    /// <summary>
    /// The simulation behind what's shown (start state, enemy ids, outcome): Incoming Damage, the Death Skull, card
    /// tabs. Without the cards in teammates' hands unless CountTeammateHands (decision #25); in single-player it's the
    /// whole state.
    /// </summary>
    public SimState State { get; private set; } = new();
    public IReadOnlyDictionary<Creature, int> Ids { get; private set; } = new Dictionary<Creature, int>();
    public SimOutcome Outcome { get; private set; } = new();

    /// <summary>The whole state, teammates' hands included (decision #25: the checks use everything that isn't a roll).</summary>
    public SimState FullState { get; private set; } = new();

    /// <summary>
    /// The same forecast from FullState, for the enemy-turn check (this one when they're the same: single-player, or
    /// the setting on). Its TeammateView stays the shown one, so the check can say when what's shown differs.
    /// </summary>
    public TurnEndModel Full => _full ??= BuildFull();
    private TurnEndModel? _full;
    private long _fullKey;
    private int _myHp;
    private static long _lastFullKey;
    private static SimOutcome? _lastFullOutcome;

    /// <summary>
    /// The same forecast over only the branches <paramref name="keep"/> allows: the check, once the game's history shows how
    /// some of the rolls went (Stampede's picks: decision #86). Null when none is left.
    /// </summary>
    internal TurnEndModel? Narrowed(Func<SimState, bool> keep)
    {
        var branches = Outcome.Branches.Where(keep).ToList();
        if (branches.Count == 0) return null;
        if (branches.Count == Outcome.Branches.Count) return this;
        var o = new SimOutcome { Branches = branches, Truncated = Outcome.Truncated, SafeWorst = Outcome.SafeWorst };
        var m = From(State, StateKey, o, Ids, Teammates, _myHp);
        m.FullState = FullState;
        m._fullKey = _fullKey;
        m._full = m;
        m._teammateView = _teammateView;
        return m;
    }

    private TurnEndModel BuildFull()
    {
        SimOutcome o;
        if (_lastFullOutcome != null && _lastFullKey == _fullKey)
        {
            o = _lastFullOutcome;
        }
        else
        {
            var logged = FullState.Clone();
            logged.Log = true;
            o = SimEngine.Run(logged);
            _lastFullKey = _fullKey;
            _lastFullOutcome = o;
        }
        var m = From(FullState, _fullKey, o, Ids, Teammates, _myHp);
        m.FullState = FullState;
        m._fullKey = _fullKey;
        m._full = m;
        m._teammateView = TeammateView;
        return m;
    }

    /// <summary>
    /// A teammate's Incoming Damage as shown (from the shown outcome: without the cards in their hand unless
    /// CountTeammateHands): damage landing on them, their block by the enemy turn, what their Osty absorbs (OstyHp 0:
    /// not out), and HP lost (worst and best branch).
    /// </summary>
    public sealed record TeammateForecast(Player Player, int Incoming, int Block, int OstyHp, int OstyTaken, int TakenMin, int TakenMax)
    {
        public bool OstyDies => OstyHp > 0 && OstyTaken >= OstyHp;
    }

    /// <summary>Each teammate's shown forecast, in SimState.Teammates order.</summary>
    public IReadOnlyList<TeammateForecast> TeammateView => _teammateView ??= BuildTeammateView();
    private IReadOnlyList<TeammateForecast>? _teammateView;

    private IReadOnlyList<TeammateForecast> BuildTeammateView()
    {
        if (State.Teammates.Count == 0 || Teammates.Count != State.Teammates.Count) return Array.Empty<TeammateForecast>();
        var o = Outcome;
        var view = new List<TeammateForecast>();
        for (var k = 0; k < Teammates.Count; k++)
        {
            var worst = o.TeammateWorst(k);
            view.Add(new TeammateForecast(Teammates[k], worst.Incoming, State.Teammates[k].Block + worst.BlockGained,
                State.Teammates[k].OstyShields ? State.Teammates[k].OstyHp : 0, worst.OstyTaken, o.TeammateTakenMin(k), o.TeammateTakenMax(k)));
        }
        return view;
    }

    /// <summary>The players behind SimState.Teammates (multiplayer), in the same order, as of this forecast.</summary>
    public IReadOnlyList<Player> Teammates { get; private set; } = Array.Empty<Player>();

    /// <summary>The start state's full fingerprint (SimState.FullKey): card simulations are cached by it.</summary>
    public long StateKey { get; private set; }

    // Incoming Damage, the forecast audit and the proc rules all ask every frame: simulate once per frame.
    private static ulong _frame = ulong.MaxValue;
    private static TurnEndModel? _cached;
    private static long _lastKey;
    private static SimOutcome? _lastOutcome;

    public static TurnEndModel? Compute()
    {
        var frame = Godot.Engine.GetProcessFrames();
        if (frame == _frame) return _cached;
        _frame = frame;
        _cached = ComputeNow();
        return _cached;
    }

    /// <summary>The board right now, uncached (the audit's prediction as a play begins to resolve).</summary>
    public static TurnEndModel? ComputeLive() => ComputeNow();

    private static TurnEndModel? ComputeNow()
    {
        var me = Sts2State.Me;
        var cs = Sts2State.Combat;
        if (me?.PlayerCombatState == null || cs == null) return null;

        // The board as it is, even mid-resolution (decision #28): the tabs don't run plays still resolving.
        var full = SimAdapter.Build(me, cs, out var ids);
        var fullKey = full.FullKey();
        // What's shown: without the cards in teammates' hands (decision #25), unless the setting counts them.
        var state = CountTeammateHands || full.Teammates.Count == 0 ? full : full.WithoutTeammateHands();
        // The game state rarely changes between frames: rerun only when it did.
        var key = ReferenceEquals(state, full) ? fullKey : state.FullKey();
        SimOutcome outcome;
        if (_lastOutcome != null && key == _lastKey)
        {
            outcome = _lastOutcome;
        }
        else
        {
            var logged = state.Clone();
            logged.Log = true; // the display needs the breakdown; card simulations run without it
            outcome = SimEngine.Run(logged);
            _lastKey = key;
            _lastOutcome = outcome;
        }
        var m = From(state, key, outcome, ids, SimAdapter.Teammates(me, cs), me.Creature.CurrentHp);
        m.FullState = full;
        m._fullKey = fullKey;
        if (ReferenceEquals(state, full)) m._full = m;
        return m;
    }

    private static TurnEndModel From(SimState state, long key, SimOutcome outcome, IReadOnlyDictionary<Creature, int> ids,
        IReadOnlyList<Player> teammates, int myHp)
    {
        var w = outcome.Worst;
        var m = new TurnEndModel
        {
            State = state,
            StateKey = key,
            Ids = ids,
            Outcome = outcome,
            Teammates = teammates,
            _myHp = myHp,
            CurrentBlock = state.P.Block,
            SkippedPoisoned = w.SkippedPoisoned,
            SkippedEndOfTurn = w.SkippedEndOfTurn,
            CancelledHits = w.CancelledHits,
            DamageThrough = w.DamageThrough,
            Taken = outcome.TakenMax,
            TakenMin = outcome.TakenMin,
            OstyHp = state.P.OstyShields ? state.P.OstyHp : 0, // shown only while he takes your hits
            OstyTaken = outcome.OstyTakenMax,
            OstyTakenMin = outcome.OstyTakenMin,
            Dies = w.Died,
            FairiesUsed = w.FairiesUsed,
            LizardTailUsed = w.LizardTailUsed,
            HpAfter = w.P.Hp == int.MaxValue ? myHp : w.P.Hp,
        };
        // Same-name entries (one per Frost trigger, per Feel No Pain exhaust...) are summed for display.
        foreach (var g in w.EndOfTurnBlock.GroupBy(x => x.Name)) m.EndOfTurnBlock.Add(new Source(g.Key, g.Sum(x => x.Amount)));
        foreach (var (name, amount) in w.Blockable) m.Blockable.Add(new Source(name, amount));
        foreach (var (name, amount) in w.Unblockable) m.Unblockable.Add(new Source(name, amount));
        foreach (var g in w.EndOfTurnDamage.GroupBy(x => x.Name)) m.EndOfTurnDamage.Add(new Source(g.Key, g.Sum(x => x.Amount)));
        return m;
    }

    public static string SafeTitle(AbstractModel model)
    {
        try
        {
            var title = model switch
            {
                RelicModel r => r.Title.GetFormattedText(),
                CardModel c => c.Title,
                PowerModel p => p.Title.GetFormattedText(),
                MonsterModel mo => mo.Title.GetFormattedText(),
                PotionModel po => po.Title.GetFormattedText(),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(title)) return title;
        }
        catch
        {
            // fall through to the class name
        }
        var name = model.GetType().Name;
        return name.EndsWith("Power") ? name[..^5] : name;
    }
}
