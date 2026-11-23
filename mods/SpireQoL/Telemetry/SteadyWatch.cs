namespace SpireQoL.Telemetry;

/// <summary>
/// Garbage while nothing changes (decision #177; dev mode only). The mod works things out when something it reads changes and only
/// draws them after that, so on a frame where nothing changed every feature should allocate nothing at all. Each frame gets a key
/// from what the mod reads (the dashboard's: the board probe, the screen, the control under the mouse, anything held, the settings,
/// searches running). Once a key has held for <see cref="SettleSeconds"/> the frame is steady, and any feature that allocated on it,
/// by even a byte, is counted: on how many steady frames, how much, and how often (the commonest gap between its allocating frames:
/// 15 is a poll every 15th frame). Dev mode's own work (the dashboard's writing, its reuse checks) is left out, and so are the frames
/// just after a reuse check, whose results can finish a frame late. Holds nothing of the game's: plain names and numbers, so it's
/// tested on its own, and allocates nothing once a feature has been seen.
/// </summary>
public sealed class SteadyWatch
{
    /// <summary>How long a key holds before its frames count as steady: what one change sets off has finished by then.</summary>
    public const double SettleSeconds = 1;
    /// <summary>Frames this long after a reuse check aren't steady: what the check set off can finish on the next frames.</summary>
    public const double DevCheckTailSeconds = 0.1;
    /// <summary>Gaps between allocating frames counted one by one up to this; longer ones together.</summary>
    public const int LongestGap = 120;

    private readonly HashSet<string> _devOnly;
    private int _key;
    private bool _haveKey;
    private double _keySince, _notSteadyUntil;
    private long _steadyIndex;
    private readonly Dictionary<(string Where, string Id), Seen> _seen = new();
    private readonly Dictionary<string, (int Frames, double Seconds)> _steady = new();
    private readonly HashSet<(string Where, string Id)> _logged = new();

    private sealed class Seen
    {
        public int Frames;
        public double Kb, MaxKb;
        public long LastIndex = -1;
        public readonly int[] Gaps = new int[LongestGap + 1];
    }

    /// <param name="devOnly">Entries that are dev mode's own (the dashboard, its reuse checks): never counted.</param>
    public SteadyWatch(IEnumerable<string> devOnly) => _devOnly = new HashSet<string>(devOnly, StringComparer.Ordinal);

    /// <summary>Whether the last frame was steady.</summary>
    public bool Steady { get; private set; }

    /// <summary>
    /// One frame: its key, the clock (seconds), the frame's length (seconds), where the game is (a fixed name: "fight", "run",
    /// "menu"), whether a reuse check ran in it, and each feature's memory in it (KB).
    /// </summary>
    public void Frame(int key, double now, double frameSeconds, string where, bool devCheck, IReadOnlyList<(string Id, double Kb)> featureKb)
    {
        if (!_haveKey || key != _key)
        {
            _haveKey = true;
            _key = key;
            _keySince = now;
            // A new stretch: gaps don't run across a change.
            foreach (var seen in _seen.Values) seen.LastIndex = -1;
        }
        if (devCheck) _notSteadyUntil = Math.Max(_notSteadyUntil, now + DevCheckTailSeconds);
        Steady = now - _keySince >= SettleSeconds && now >= _notSteadyUntil && !devCheck;
        if (!Steady) return;
        _steadyIndex++;
        var steady = _steady.GetValueOrDefault(where);
        _steady[where] = (steady.Frames + 1, steady.Seconds + frameSeconds);
        for (var i = 0; i < featureKb.Count; i++)
        {
            var (id, kilobytes) = featureKb[i];
            if (kilobytes <= 0 || _devOnly.Contains(id)) continue;
            if (!_seen.TryGetValue((where, id), out var seen)) _seen[(where, id)] = seen = new Seen();
            seen.Frames++;
            seen.Kb += kilobytes;
            seen.MaxKb = Math.Max(seen.MaxKb, kilobytes);
            if (seen.LastIndex >= 0) seen.Gaps[Math.Min(LongestGap, (int)(_steadyIndex - seen.LastIndex))]++;
            seen.LastIndex = _steadyIndex;
        }
    }

    /// <summary>What was seen since the last call (and starts the counts again), with the entries seen for the first time this launch.</summary>
    public (SteadySample Sample, List<SteadyGarbage> FirstSeen) Take()
    {
        var garbage = new List<SteadyGarbage>();
        var firstSeen = new List<SteadyGarbage>();
        foreach (var ((where, id), seen) in _seen)
        {
            if (seen.Frames == 0) continue;
            var period = 0;
            for (var gap = 1; gap <= LongestGap; gap++)
                if (seen.Gaps[gap] > 0 && (period == 0 || seen.Gaps[gap] > seen.Gaps[period])) period = gap;
            var entry = new SteadyGarbage(where, id, seen.Frames, Math.Round(seen.Kb, 3), Math.Round(seen.MaxKb, 3), period);
            garbage.Add(entry);
            if (_logged.Add((where, id))) firstSeen.Add(entry);
            seen.Frames = 0;
            seen.Kb = 0;
            seen.MaxKb = 0;
            Array.Clear(seen.Gaps);
        }
        var steady = _steady.ToDictionary(entry => entry.Key, entry => new SteadyTime(entry.Value.Frames, Math.Round(entry.Value.Seconds, 2)));
        _steady.Clear();
        garbage.Sort((one, other) => other.Kb.CompareTo(one.Kb));
        return (new SteadySample(steady, garbage), firstSeen);
    }

    /// <summary>The log's line for an entry seen for the first time (dev mode; once per feature and place a launch).</summary>
    public static string Describe(SteadyGarbage garbage, int steadyFrames) =>
        $"[steady] {garbage.Id} made garbage while nothing changed ({garbage.Where}): on {garbage.Frames} of {steadyFrames} steady frames" +
        (garbage.Period > 0 ? $" ({(garbage.Period >= LongestGap ? $"{LongestGap} or more" : garbage.Period.ToString())} apart most often)" : "") +
        $", {garbage.Kb / garbage.Frames:0.00} KB each on average, {garbage.MaxKb:0.00} KB at most";
}
