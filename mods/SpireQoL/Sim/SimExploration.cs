using System.Threading;

namespace SpireQoL.Sim;

/// <summary>
/// Every outcome of a simulation, explored in slices (decision #87). A single run keeps at most SimEngine.MaxBranches branches
/// per step; past that, instead of dropping the rest, each run follows one slice of every overflowing step (SliceCursor), and
/// the exploration runs every combination of slices in turn, adding up what each finds. Nothing is explored twice: a run only
/// redoes the steps before its overflows, which are small by definition.
///
/// Explore works for a budget and can be called again to go on (the frame first, a worker thread after: SimJob). Snapshot is
/// what's known so far: complete once every slice has run; until then Truncated, with the safe run's damage as the upper bound
/// on yours, like a single run that ran out of branches. Published snapshots are never changed (branches merged later are
/// copied first), so another thread can read them while exploration goes on.
/// </summary>
public sealed class SimExploration
{
    /// <summary>Most branches kept over the whole exploration (merged across slices); past it, exploration stops (Capped).</summary>
    public const int MaxStoredBranches = 50_000;
    /// <summary>Most state copies over the whole exploration; past it, exploration stops (Capped).</summary>
    public const long MaxTotalCopies = 20_000_000;

    private readonly SimState _start;
    private readonly IReadOnlyList<SimScript>? _leadIn;
    private readonly SimScript? _play;
    private readonly int _sliceSize;
    private List<int>? _next = new();

    private readonly Dictionary<long, int> _index = new();
    private readonly List<SimState> _branches = new();
    private bool _first = true;
    private HashSet<int> _killedAny = new(), _killedAll = new(), _reacted = new();
    private bool _killsEvery = true, _breaksEvery = true, _outOfBranches;
    private int _gainsMax, _biggestHitMin, _hpReachedMin, _hpLossesMin, _hpLostMin;
    private PowerSet _firedEvery = PowerSet.Empty, _firedAny = PowerSet.Empty, _firstHits = PowerSet.Empty;
    private SimState? _safe;

    public SimExploration(SimState start, IReadOnlyList<SimScript>? leadIn, SimScript? play, int sliceSize = SimEngine.MaxBranches)
    {
        _start = start;
        _leadIn = leadIn;
        _play = play;
        _sliceSize = sliceSize;
    }

    public static SimExploration Of(SimState start, SimPlay? play = null, int sliceSize = SimEngine.MaxBranches)
        => new(start, null, play == null ? null : SimScript.Of(play), sliceSize);

    /// <summary>Every slice has run: the outcome is complete (unless a step ran over its own copy budget: OutOfBranches).</summary>
    public bool Done => _next == null && !Capped;
    /// <summary>Stopped at MaxStoredBranches or MaxTotalCopies: what's known stays partial.</summary>
    public bool Capped { get; private set; }
    /// <summary>Which cap stopped it ("50,000 stored branches"); null when none did.</summary>
    public string? CapHit { get; private set; }
    /// <summary>A step ran over its own copy budget (SimEngine.StepCopyBudget) in some slice: that step followed one target only.</summary>
    public bool StepBudgetHit => _outOfBranches;
    /// <summary>Nothing more will be explored (Done or Capped).</summary>
    public bool Finished => _next == null || Capped;

    /// <summary>Runs (slices) so far, and the work they did (SimOutcome's counters, summed; the most one step made).</summary>
    public int Runs { get; private set; }
    public long Copies { get; private set; }
    public long Steps { get; private set; }
    public long Stepped { get; private set; }
    public int MaxStepCopies { get; private set; }
    /// <summary>The most overflows one run met (how deep the slicing went).</summary>
    public int MaxDepth { get; private set; }
    /// <summary>The share of the rolls explored so far (1 when complete).</summary>
    public double ExploredWeight { get; private set; }

    /// <summary>Explores slices until done, about <paramref name="copyBudget"/> copies are spent, or cancelled. Returns Finished.</summary>
    public bool Explore(long copyBudget, CancellationToken cancel = default)
    {
        long spent = 0;
        while (_next != null && !Capped && spent < copyBudget && !cancel.IsCancellationRequested)
        {
            var cursor = new SliceCursor(_next, _sliceSize);
            var o = SimEngine.Run(_start, _leadIn, _play, cursor);
            Runs++;
            spent += o.Copies;
            Copies += o.Copies;
            Steps += o.Steps;
            Stepped += o.Stepped;
            MaxStepCopies = Math.Max(MaxStepCopies, o.MaxStepCopies);
            MaxDepth = Math.Max(MaxDepth, cursor.Counts.Count);
            Add(o);
            _next = cursor.Next();
            if (_branches.Count > MaxStoredBranches) (Capped, CapHit) = (true, $"{MaxStoredBranches:N0} stored branches");
            else if (Copies > MaxTotalCopies) (Capped, CapHit) = (true, $"{MaxTotalCopies:N0} copies");
        }
        return Finished;
    }

    /// <summary>Explores everything there is (up to the caps). For tests and checks that need the whole outcome at once.</summary>
    public SimOutcome ExploreAll(CancellationToken cancel = default)
    {
        Explore(long.MaxValue, cancel);
        return Snapshot();
    }

    private void Add(SimOutcome o)
    {
        foreach (var b in o.Branches)
        {
            ExploredWeight += b.Weight;
            var key = b.Key();
            if (_index.TryGetValue(key, out var i))
            {
                // The same branch from another slice: merged into a copy, so a snapshot already published never changes.
                var merged = _branches[i].Clone();
                merged.Weight += b.Weight;
                merged.MergeResults(b);
                merged.OutOfBranches |= b.OutOfBranches;
                _branches[i] = merged;
            }
            else
            {
                _index[key] = _branches.Count;
                _branches.Add(b);
            }
        }
        _outOfBranches |= o.Truncated;
        if (_first)
        {
            _first = false;
            _killedAll = new HashSet<int>(o.KilledByPlayInAll);
            _killsEvery = o.PlayKillsInEveryBranch;
            _breaksEvery = o.PlayBreaksBlockInEveryBranch;
            _gainsMax = o.PlayBlockGainsMax;
            _biggestHitMin = o.PlayBiggestHitMin;
            _hpReachedMin = o.PlayHpReachedMin;
            _hpLossesMin = o.PlayHpLossesMin;
            _hpLostMin = o.PlayHpLostMin;
            _firedEvery = o.PlayFiredEvery;
            _firedAny = o.PlayFiredAny;
            _firstHits = o.PlayFirstHits;
        }
        else
        {
            // Each slice covers different rolls: "in every branch" holds over all of them, "in some branch" over any.
            _killedAll.IntersectWith(o.KilledByPlayInAll);
            _killsEvery &= o.PlayKillsInEveryBranch;
            _breaksEvery &= o.PlayBreaksBlockInEveryBranch;
            _gainsMax = Math.Max(_gainsMax, o.PlayBlockGainsMax);
            _biggestHitMin = Math.Min(_biggestHitMin, o.PlayBiggestHitMin);
            _hpReachedMin = Math.Min(_hpReachedMin, o.PlayHpReachedMin);
            _hpLossesMin = Math.Min(_hpLossesMin, o.PlayHpLossesMin);
            _hpLostMin = Math.Min(_hpLostMin, o.PlayHpLostMin);
            _firedEvery = SimState.MergeLog(_firedEvery, o.PlayFiredEvery, every: true);
            _firedAny = SimState.MergeLog(_firedAny, o.PlayFiredAny, every: false);
            if (!ReferenceEquals(_firstHits, o.PlayFirstHits) && !_firstHits.Items.AsSpan().SequenceEqual(o.PlayFirstHits.Items)) _firstHits = PowerSet.Empty;
        }
        _killedAny.UnionWith(o.KilledByPlayInAny);
        _reacted.UnionWith(o.BlockReactionInAny);
    }

    /// <summary>
    /// What's known so far, as one outcome (a new object each call, never changed afterwards). Truncated until every slice
    /// has run, with the safe run as the bound on damage to you; "in every branch" results stay empty until then.
    /// </summary>
    public SimOutcome Snapshot()
    {
        var complete = Done && !_outOfBranches;
        if (!complete) _safe ??= SimEngine.SafeRun(_start, _leadIn, _play);
        return new SimOutcome
        {
            Branches = new List<SimState>(_branches),
            Truncated = !complete,
            Explored = complete ? 1 : _outOfBranches ? double.NaN : ExploredWeight,
            SafeWorst = complete ? null : _safe,
            KilledByPlayInAny = new HashSet<int>(_killedAny),
            KilledByPlayInAll = complete ? new HashSet<int>(_killedAll) : new HashSet<int>(),
            PlayKillsInEveryBranch = complete && _killsEvery,
            BlockReactionInAny = new HashSet<int>(_reacted),
            PlayBreaksBlockInEveryBranch = complete && _breaksEvery,
            PlayBlockGainsMax = _gainsMax,
            PlayBiggestHitMin = complete ? _biggestHitMin : 0,
            PlayHpReachedMin = complete ? _hpReachedMin : 0,
            PlayHpLossesMin = complete ? _hpLossesMin : 0,
            PlayHpLostMin = complete ? _hpLostMin : 0,
            PlayFiredEvery = complete ? _firedEvery : PowerSet.Empty,
            PlayFiredAny = _firedAny,
            PlayFirstHits = complete ? _firstHits : PowerSet.Empty,
            Steps = (int)Math.Min(int.MaxValue, Steps),
            Stepped = (int)Math.Min(int.MaxValue, Stepped),
            Copies = (int)Math.Min(int.MaxValue, Copies),
            MaxStepCopies = MaxStepCopies,
        };
    }
}
