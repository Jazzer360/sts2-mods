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
    /// <summary>The chance the enemy turn kills you (decision #87): the skull shows whenever it's possible, with this.</summary>
    public SimChance DeathChance { get; private set; }
    /// <summary>
    /// The search stopped at its memory cap (decision #107): your damage is unknown, so Incoming Damage shows ? for it; enemy deaths
    /// already settled still count (Chance's lasting).
    /// </summary>
    public bool Unknown => Outcome.Truncated;
    /// <summary>The mark for a forecast that isn't certain (something not modelled, or the search stopped at its cap): "?"; null otherwise.</summary>
    public string? ProgressMark => Outcome.Truncated || Outcome.AnyUnmodelled ? "?" : null;
    /// <summary>ProgressMark, or "?" while the shown forecast is a held one waiting on a newer result past ShownWaitMs (Shown).</summary>
    public string? ShownMark => ProgressMark ?? (Waiting ? "?" : null);
    /// <summary>In the worst branch you'd die, but Fairy in a Bottle (potions used) or Lizard Tail heals you instead.</summary>
    public int FairiesUsed { get; private set; }
    public bool LizardTailUsed { get; private set; }
    /// <summary>Your HP once the enemy turn is over (worst branch).</summary>
    public int HpAfter { get; private set; }
    public bool OstyDies => OstyHp > 0 && OstyTaken >= OstyHp;

    public int BlockGain => EndOfTurnBlock.Sum(source => source.Amount);
    public int BlockableTotal => Blockable.Sum(source => source.Amount);
    /// <summary>
    /// What you lose in the worst outcome that no block could stop (Regret, Beckon, Bad Luck), after the HP-loss modifiers:
    /// Incoming Damage shows it on its own line, under what gets through your block (Taken − this).
    /// </summary>
    public int UnblockableTaken { get; private set; }
    public int UnblockableTotal => Unblockable.Sum(source => source.Amount);
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
    /// The forecast with teammates' hands (the forecast check's), or null while its first result is still on its way past the
    /// audit's wait: PendingFull is that exploration then, and FullFrom builds the model once it has one.
    /// </summary>
    public TurnEndModel? Full => _full ??= BuildFull();

    /// <summary>The exploration behind Full (set once Full was asked for).</summary>
    internal SimJob? PendingFull { get; private set; }
    private TurnEndModel? _full;
    private long _fullKey;
    private int _myHp;
    private static long _lastFullKey;
    private static SimJob? _lastFullJob;

    /// <summary>The search behind Outcome (decision #107): it may still be going; Refreshed takes its result once it's over.</summary>
    internal SimJob? Job { get; private set; }

    /// <summary>
    /// The search for this board, kept while the board stays the same: started on a worker, waited for a few milliseconds at
    /// most (SimWait), then read once it's over. A new board cancels the old one, unless the forecast check pinned it (it compares
    /// against the finished result).
    /// </summary>
    private static SimJob Explore(ref long lastKey, ref SimJob? job, long key, SimState state, string label)
    {
        if (job == null || key != lastKey)
        {
            if (job is { Pinned: false }) job.Cancel();
            var logged = state.Clone();
            logged.Log = true; // the display needs the breakdown; card simulations run without it
            job = SimJob.Start(SimSearch.Of(logged), label);
            lastKey = key;
        }
        return job;
    }

    /// <summary>
    /// Waits for the job's result within the frame's budget (SimWait, decision #104), or a budget of its own. False while it has
    /// none; a search that failed throws here.
    /// </summary>
    private static bool Wait(SimJob job, bool ownBudget)
    {
        if (!SimWait.Ready(job, ownBudget)) return false;
        job.Ready.GetAwaiter().GetResult();
        return true;
    }

    /// <summary>The same forecast from its search's result (the forecast check, once the enemy turn is over).</summary>
    internal TurnEndModel Refreshed()
    {
        if (Job?.Latest is not { } latest || ReferenceEquals(latest, Outcome)) return this;
        var forecast = From(State, StateKey, latest, Ids, Teammates, _myHp);
        forecast.Job = Job;
        forecast.FullState = FullState;
        forecast._fullKey = _fullKey;
        if (ReferenceEquals(_full, this)) forecast._full = forecast;
        forecast._teammateView = _teammateView;
        return forecast;
    }

    /// <summary>
    /// The same forecast over only the branches <paramref name="keep"/> allows: the check, once the game's history shows how
    /// some of the rolls went (Stampede's picks: decision #86). Null when none is left.
    /// </summary>
    internal TurnEndModel? Narrowed(Func<SimState, bool> keep)
    {
        var branches = Outcome.Branches.Where(keep).ToList();
        if (branches.Count == 0) return null;
        if (branches.Count == Outcome.Branches.Count) return this;
        var outcome = new SimOutcome { Branches = branches, Truncated = Outcome.Truncated };
        var forecast = From(State, StateKey, outcome, Ids, Teammates, _myHp);
        forecast.FullState = FullState;
        forecast._fullKey = _fullKey;
        forecast._full = forecast;
        forecast._teammateView = _teammateView;
        return forecast;
    }

    private TurnEndModel? BuildFull()
    {
        var job = Explore(ref _lastFullKey, ref _lastFullJob, _fullKey, FullState, "the turn forecast with teammates' hands");
        PendingFull = job;
        return Wait(job, ownBudget: true) ? FullFrom(job) : null;
    }

    /// <summary>The forecast with teammates' hands from this exploration's latest result; null while it has none.</summary>
    internal TurnEndModel? FullFrom(SimJob job)
    {
        if (job.Latest is not { } latest) return null;
        var forecast = From(FullState, _fullKey, latest, Ids, Teammates, _myHp);
        forecast.Job = job;
        forecast.FullState = FullState;
        forecast._fullKey = _fullKey;
        forecast._full = forecast;
        forecast._teammateView = TeammateView;
        return forecast;
    }

    /// <summary>
    /// A teammate's Incoming Damage as shown (from the shown outcome: without the cards in their hand unless
    /// CountTeammateHands): damage landing on them, their block by the enemy turn, what their Osty absorbs (OstyHp 0:
    /// not out), HP lost (worst and best branch), and the part of it no block stops (on its own line, as yours: decision #93).
    /// </summary>
    public sealed record TeammateForecast(Player Player, int Incoming, int Block, int OstyHp, int OstyTaken, int TakenMin, int TakenMax, int Unblockable = 0)
    {
        public bool OstyDies => OstyHp > 0 && OstyTaken >= OstyHp;
    }

    /// <summary>Each teammate's shown forecast, in SimState.Teammates order.</summary>
    public IReadOnlyList<TeammateForecast> TeammateView => _teammateView ??= BuildTeammateView();
    private IReadOnlyList<TeammateForecast>? _teammateView;

    private IReadOnlyList<TeammateForecast> BuildTeammateView()
    {
        if (State.Teammates.Count == 0 || Teammates.Count != State.Teammates.Count) return Array.Empty<TeammateForecast>();
        var outcome = Outcome;
        var view = new List<TeammateForecast>();
        for (var teammateIndex = 0; teammateIndex < Teammates.Count; teammateIndex++)
        {
            var worst = outcome.TeammateWorst(teammateIndex);
            view.Add(new TeammateForecast(Teammates[teammateIndex], worst.Incoming, State.Teammates[teammateIndex].Block + worst.BlockGained,
                State.Teammates[teammateIndex].OstyShields ? State.Teammates[teammateIndex].OstyHp : 0, worst.OstyTaken, outcome.TeammateTakenMin(teammateIndex), outcome.TeammateTakenMax(teammateIndex),
                Math.Min(worst.UnblockableTaken, outcome.TeammateTakenMax(teammateIndex))));
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
    private static SimJob? _lastJob;

    public static TurnEndModel? Compute()
    {
        var frame = Godot.Engine.GetProcessFrames();
        if (frame == _frame) return _cached;
        _frame = frame;
        SimJob.FlushLog(line => ModEntry.Log.Info(line), ex => Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "sim:background"), ReportCap);
        var start = Godot.Time.GetTicksUsec();
        var alloc = GC.GetAllocatedBytesForCurrentThread();
        _cached = ComputeNow();
        // Once a frame, by whichever feature asks first: timed on its own for the dev dashboard, so that feature isn't charged for it.
        PartTime?.Invoke("turn-forecast", (Godot.Time.GetTicksUsec() - start) / 1000.0, (GC.GetAllocatedBytesForCurrentThread() - alloc) / 1024.0);
        return _cached;
    }

    /// <summary>Where the frame's shared forecast work is timed (OverlayHost.AddPartTime), set at startup.</summary>
    internal static Action<string, double, double>? PartTime;

    /// <summary>
    /// What Incoming Damage, the Death Skull and Teammate Incoming Damage show (decision #105): the forecast for the board now, or,
    /// while its first result is on its way, the last one this turn of this fight had (held), so they don't blink out for a few frames
    /// on every hit of a busy play. The audits and the card tabs use Compute: never a held forecast.
    /// </summary>
    public static TurnEndModel? Shown()
    {
        var combat = Sts2State.Combat;
        if (Compute() is { } forecast)
        {
            _shown = (forecast, combat, Sts2State.TurnNumber);
            _waitingSince = 0;
            forecast.Waiting = false;
            return forecast;
        }
        if (_shown is { } modelCombat && combat != null && ReferenceEquals(modelCombat.Combat, combat) && modelCombat.Turn == Sts2State.TurnNumber)
        {
            var frame = Godot.Engine.GetProcessFrames();
            if (frame != _heldFrame)
            {
                _heldFrame = frame;
                _heldFrames++;
            }
            // Held past ShownWaitMs: the ? beside it says a newer result is on its way (shorter waits go unseen, so it doesn't blink).
            if (_waitingSince == 0) _waitingSince = System.Diagnostics.Stopwatch.GetTimestamp();
            modelCombat.Model.Waiting = System.Diagnostics.Stopwatch.GetElapsedTime(_waitingSince).TotalMilliseconds >= ShownWaitMs;
            return modelCombat.Model;
        }
        return null;
    }

    /// <summary>How long a held forecast shows before it carries the ? (decision #107).</summary>
    public const int ShownWaitMs = 100;
    private static long _waitingSince;
    /// <summary>This is a held forecast (Shown) waiting on a newer result past ShownWaitMs.</summary>
    public bool Waiting { get; private set; }

    private static (TurnEndModel Model, MegaCrit.Sts2.Core.Combat.ICombatState? Combat, int Turn)? _shown;
    private static ulong _heldFrame = ulong.MaxValue;
    private static int _heldFrames;

    /// <summary>Frames that showed a held forecast since the last call (the combat summary).</summary>
    internal static int TakeHeldFrames()
    {
        var heldFrames = _heldFrames;
        _heldFrames = 0;
        return heldFrames;
    }

    /// <summary>
    /// A search the memory cap stopped (decision #107), to Sentry (decision #105: how often the caps bind, from players' fights), grouped by
    /// the cap and what was explored (the forecast, or any card's play: the card is a tag), with its start state to replay.
    /// </summary>
    private static void ReportCap(SimJob.CapHit capHit)
    {
        var card = capHit.Label.Contains(" (target") ? capHit.Label[..capHit.Label.IndexOf(" (target", StringComparison.Ordinal)] : null;
        var what = card == null ? capHit.Label : "a card's play";
        var tags = new Dictionary<string, string> { ["sim_cap"] = capHit.Caps };
        if (card != null) tags["card"] = card;
        Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("sim-cap", $"Simulation cap: {capHit.Caps} exploring {what}",
            new[] { "sim-cap", capHit.Caps, what }, capHit.Line, new Dictionary<string, string> { ["sim_state"] = capHit.Start.ToJson() }, tags);
    }

    /// <summary>The board right now, uncached (the audit's prediction as a play begins to resolve), on the audit's own wait.</summary>
    public static TurnEndModel? ComputeLive() => ComputeNow(ownBudget: true);

    /// <summary>
    /// Null outside combat, and while the forecast's first result is still on its way past the frame's wait (decision #104): what
    /// it feeds shows as pending until it comes, on a later frame.
    /// </summary>
    private static TurnEndModel? ComputeNow(bool ownBudget = false)
    {
        var me = Sts2State.Me;
        var combat = Sts2State.Combat;
        if (me?.PlayerCombatState == null || combat == null) return null;

        // Nothing a prediction reads changed since last frame, and its search is over (decision #130): last frame's model,
        // without reading the board again. Dev mode works it out anyway every 30th such frame and logs it if the key differs.
        var probe = BoardProbe.Hash(me, combat);
        var reuse = ForecastReuse.Decide(!ownBudget && _lastModel is { Job.Ready.IsCompleted: true } && _lastModelProbe == probe);
        if (reuse == FrameReuse.Reuse) return _lastModel;
        var probeCheck = reuse == FrameReuse.WorkOutAndCheck ? _lastModel : null;

        // The board as it is, even mid-resolution (decision #28): the tabs don't run plays still resolving.
        DrawPileMemory.Update(me, combat);
        var full = SimAdapter.Build(me, combat, out var ids);
        var fullKey = full.FullKey();
        // What's shown: without the cards in teammates' hands (decision #25), unless the setting counts them.
        var state = CountTeammateHands || full.Teammates.Count == 0 ? full : full.WithoutTeammateHands();
        // The game state rarely changes between frames: rerun only when it did.
        var key = ReferenceEquals(state, full) ? fullKey : state.FullKey();
        if (probeCheck != null)
            ForecastReuse.Compare(probeCheck.StateKey == key && probeCheck._fullKey == fullKey, "its start state changed while the board probe didn't",
                () => BoardProbe.Diff(probeCheck.FullState, full));
        var job = Explore(ref _lastKey, ref _lastJob, key, state, "the turn forecast");
        if (!Wait(job, ownBudget)) return null;
        // The same board and the same search result as the last frame: its model again, not rebuilt from the outcome every frame.
        if (_lastModel is { } last && last.StateKey == key && last._fullKey == fullKey && ReferenceEquals(last.Outcome, job.Latest)
            && last.Teammates.Count == full.Teammates.Count)
        {
            _lastModelProbe = probe; // the board probe moved (a setting, say) but not what the forecast reads
            return last;
        }
        var forecast = From(state, key, job.Latest!, ids, SimAdapter.Teammates(me, combat), me.Creature.CurrentHp);
        forecast.Job = job;
        forecast.FullState = full;
        forecast._fullKey = fullKey;
        if (ReferenceEquals(state, full)) forecast._full = forecast;
        _lastModel = forecast;
        _lastModelProbe = probe;
        return forecast;
    }

    /// <summary>The last model built from a search result (ComputeNow), handed back while the board and the result stay the same.</summary>
    private static TurnEndModel? _lastModel;
    /// <summary>The board probe <see cref="_lastModel"/> was worked out from (decision #130).</summary>
    private static int _lastModelProbe;
    private static readonly ReuseCheck ForecastReuse = new("the turn forecast");

    private static TurnEndModel From(SimState state, long key, SimOutcome outcome, IReadOnlyDictionary<Creature, int> ids,
        IReadOnlyList<Player> teammates, int myHp)
    {
        var branch = outcome.Worst;
        var forecast = new TurnEndModel
        {
            State = state,
            StateKey = key,
            Ids = ids,
            Outcome = outcome,
            Teammates = teammates,
            _myHp = myHp,
            CurrentBlock = state.You.Block,
            SkippedPoisoned = branch.SkippedPoisoned,
            SkippedEndOfTurn = branch.SkippedEndOfTurn,
            CancelledHits = branch.CancelledHits,
            DamageThrough = branch.DamageThrough,
            Taken = outcome.TakenMax,
            TakenMin = outcome.TakenMin,
            OstyHp = state.You.OstyShields ? state.You.OstyHp : 0, // shown only while he takes your hits
            OstyTaken = outcome.OstyTakenMax,
            OstyTakenMin = outcome.OstyTakenMin,
            Dies = branch.Died,
            UnblockableTaken = Math.Min(branch.UnblockableTaken, outcome.TakenMax),
            DeathChance = outcome.YouDieChance(),
            FairiesUsed = branch.FairiesUsed,
            LizardTailUsed = branch.LizardTailUsed,
            HpAfter = branch.You.Hp == int.MaxValue ? myHp : branch.You.Hp,
        };
        // Same-name entries (one per Frost trigger, per Feel No Pain exhaust...) are summed for display.
        foreach (var bySource in branch.EndOfTurnBlock.GroupBy(nameAmount => nameAmount.Name)) forecast.EndOfTurnBlock.Add(new Source(bySource.Key, bySource.Sum(nameAmount => nameAmount.Amount)));
        foreach (var (name, amount) in branch.Blockable) forecast.Blockable.Add(new Source(name, amount));
        foreach (var (name, amount) in branch.Unblockable) forecast.Unblockable.Add(new Source(name, amount));
        foreach (var bySource in branch.EndOfTurnDamage.GroupBy(nameAmount => nameAmount.Name)) forecast.EndOfTurnDamage.Add(new Source(bySource.Key, bySource.Sum(nameAmount => nameAmount.Amount)));
        return forecast;
    }

    public static string SafeTitle(AbstractModel model)
    {
        try
        {
            var title = model switch
            {
                RelicModel relic => relic.Title.GetFormattedText(),
                CardModel card => card.Title,
                PowerModel power => power.Title.GetFormattedText(),
                MonsterModel monster => monster.Title.GetFormattedText(),
                PotionModel potion => potion.Title.GetFormattedText(),
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
