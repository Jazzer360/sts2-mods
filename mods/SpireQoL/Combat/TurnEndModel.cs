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
    public bool OstyDies => OstyHp > 0 && OstyTaken >= OstyHp;

    public int BlockGain => EndOfTurnBlock.Sum(s => s.Amount);
    public int BlockableTotal => Blockable.Sum(s => s.Amount);
    public int UnblockableTotal => Unblockable.Sum(s => s.Amount);
    public int ExpectedBlock => CurrentBlock + BlockGain;

    /// <summary>The simulation behind these numbers (start state, enemy ids, outcome), for card tabs.</summary>
    public SimState State { get; private set; } = new();
    public IReadOnlyDictionary<Creature, int> Ids { get; private set; } = new Dictionary<Creature, int>();
    public SimOutcome Outcome { get; private set; } = new();
    /// <summary>
    /// A teammate's Incoming Damage as shown (decision #25: from what you can see, so without the cards in
    /// their hand): damage landing on them, their block by the enemy turn, what their Osty absorbs (OstyHp 0:
    /// not out), and HP lost (worst and best branch).
    /// </summary>
    public sealed record TeammateForecast(Player Player, int Incoming, int Block, int OstyHp, int OstyTaken, int TakenMin, int TakenMax)
    {
        public bool OstyDies => OstyHp > 0 && OstyTaken >= OstyHp;
    }

    /// <summary>Each teammate's shown forecast, in SimState.Teammates order (a second run, built on first use).</summary>
    public IReadOnlyList<TeammateForecast> TeammateView => _teammateView ??= BuildTeammateView();
    private IReadOnlyList<TeammateForecast>? _teammateView;
    private static long _lastViewKey;
    private static IReadOnlyList<TeammateForecast>? _lastView;

    private IReadOnlyList<TeammateForecast> BuildTeammateView()
    {
        if (State.Teammates.Count == 0 || Teammates.Count != State.Teammates.Count) return Array.Empty<TeammateForecast>();
        if (_lastView != null && _lastViewKey == StateKey && _lastView.Count == Teammates.Count
            && _lastView.Select(v => v.Player).SequenceEqual(Teammates)) return _lastView;
        // Their hand isn't known to you: run again with the cards in it left out (what they hold only
        // ever changes their own HP in the simulator).
        var o = SimEngine.Run(State.WithoutTeammateHands());
        var view = new List<TeammateForecast>();
        for (var k = 0; k < Teammates.Count; k++)
        {
            var worst = o.TeammateWorst(k);
            view.Add(new TeammateForecast(Teammates[k], worst.Incoming, State.Teammates[k].Block + worst.BlockGained,
                State.Teammates[k].OstyHp, worst.OstyTaken, o.TeammateTakenMin(k), o.TeammateTakenMax(k)));
        }
        _lastViewKey = StateKey;
        _lastView = view;
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
        var state = SimAdapter.Build(me, cs, out var ids);
        // The game state rarely changes between frames: rerun only when it did.
        var key = state.FullKey();
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
        var w = outcome.Worst;
        var m = new TurnEndModel
        {
            State = state,
            StateKey = key,
            Ids = ids,
            Outcome = outcome,
            Teammates = SimAdapter.Teammates(me, cs),
            CurrentBlock = state.P.Block,
            SkippedPoisoned = w.SkippedPoisoned,
            SkippedEndOfTurn = w.SkippedEndOfTurn,
            CancelledHits = w.CancelledHits,
            DamageThrough = w.DamageThrough,
            Taken = outcome.TakenMax,
            TakenMin = outcome.TakenMin,
            OstyHp = state.P.OstyHp,
            OstyTaken = outcome.OstyTakenMax,
            OstyTakenMin = outcome.OstyTakenMin,
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
