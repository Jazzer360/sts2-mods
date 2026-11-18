namespace SpireQoL.Sim;

/// <summary>
/// Which slice of each overflowing step one run follows (SimExploration, decision #87). A run is deterministic: the same start
/// gives the same branches in the same order. So when a step makes more than Size branches, the n-th overflow of the run keeps
/// slice Path[n] of them (Size each, in order) and follows it to the end. Every combination of slices together covers every
/// branch exactly once, each with its share of the rolls (SimState.Weight). The run records how many slices each overflow had,
/// so the next combination (Next) is known without exploring ahead.
/// </summary>
public sealed class SliceCursor
{
    private readonly IReadOnlyList<int> _path;
    private readonly List<int> _counts = new();

    public SliceCursor(IReadOnlyList<int> path, int size)
    {
        _path = path;
        Size = Math.Max(1, size);
    }

    /// <summary>Branches a step keeps before it's sliced.</summary>
    public int Size { get; }

    /// <summary>The slices this run's overflows had, in order (one per overflow it met).</summary>
    public IReadOnlyList<int> Counts => _counts;

    /// <summary>The slice this overflow follows.</summary>
    internal List<SimState> Take(List<SimState> all)
    {
        var n = _counts.Count;
        var slices = (all.Count + Size - 1) / Size;
        _counts.Add(slices);
        var k = n < _path.Count ? _path[n] : 0;
        if (k >= slices) throw new InvalidOperationException($"slice {k} of {slices} at overflow {n}: the run isn't deterministic");
        return all.GetRange(k * Size, Math.Min(Size, all.Count - k * Size));
    }

    /// <summary>The next combination of slices to run after this one (depth first), or null when every one has been run.</summary>
    public List<int>? Next()
    {
        var p = Enumerable.Range(0, _counts.Count).Select(d => d < _path.Count ? _path[d] : 0).ToList();
        for (var d = p.Count - 1; d >= 0; d--)
        {
            if (p[d] + 1 >= _counts[d]) continue;
            p[d]++;
            return p.Take(d + 1).ToList();
        }
        return null;
    }
}
