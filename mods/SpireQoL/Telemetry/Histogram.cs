namespace SpireQoL.Telemetry;

/// <summary>
/// Millisecond timings in fixed buckets, for percentiles without keeping every value (a frame's time, every frame of a session):
/// 0.1 ms steps up to 20 ms, 1 ms up to 200 ms, 50 ms up to 5 s, then one bucket for the rest. A percentile is its bucket's upper
/// edge (never below the true value; within 0.1 ms where frames live). Not thread-safe: its owner locks.
/// </summary>
public sealed class Histogram
{
    private const int Fine = 200, Mid = 180, Coarse = 96;
    private readonly int[] _buckets = new int[Fine + Mid + Coarse + 1];

    public int Count { get; private set; }
    public double Max { get; private set; }
    public double Sum { get; private set; }

    public void Add(double ms)
    {
        if (double.IsNaN(ms) || ms < 0) ms = 0;
        _buckets[Index(ms)]++;
        Count++;
        Sum += ms;
        if (ms > Max) Max = ms;
    }

    private static int Index(double ms)
        => ms < 20 ? (int)(ms * 10)
            : ms < 200 ? Fine + (int)(ms - 20)
            : ms < 5000 ? Fine + Mid + (int)((ms - 200) / 50)
            : Fine + Mid + Coarse;

    private double UpperEdge(int i)
        => i < Fine ? (i + 1) / 10.0
            : i < Fine + Mid ? 20 + (i - Fine + 1)
            : i < Fine + Mid + Coarse ? 200 + (i - Fine - Mid + 1) * 50.0
            : Max;

    /// <summary>The value <paramref name="p"/> (0 to 1) of them are at or below (0 when empty), never above the largest seen.</summary>
    public double Percentile(double p)
    {
        if (Count == 0) return 0;
        var rank = Math.Max(1, (int)Math.Ceiling(p * Count));
        var seen = 0;
        for (var i = 0; i < _buckets.Length; i++)
        {
            seen += _buckets[i];
            if (seen >= rank) return Math.Min(UpperEdge(i), Max);
        }
        return Max;
    }

    public TimingSummary Summary() => new(Count, Round(Percentile(0.5)), Round(Percentile(0.95)), Round(Percentile(0.99)), Round(Max));

    private static double Round(double v) => Math.Round(v, 1);
}

/// <summary>A histogram's numbers, as sent: how many, the median, 95th and 99th percentiles and the largest (ms).</summary>
public sealed record TimingSummary(int Count, double P50, double P95, double P99, double Max);
