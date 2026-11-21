namespace SpireQoL.Combat;

/// <summary>What a result kept between frames does this frame (<see cref="ReuseCheck"/>).</summary>
public enum FrameReuse
{
    /// <summary>Something it reads may have changed (or there's nothing kept): work it out.</summary>
    WorkOut,
    /// <summary>Nothing it reads changed: use the kept result.</summary>
    Reuse,
    /// <summary>Nothing changed, but this is dev mode's check: work it out anyway and <see cref="ReuseCheck.Compare"/> it with the kept one.</summary>
    WorkOutAndCheck,
}

/// <summary>
/// The rule every result kept between frames follows (decisions #114, #130): reused while nothing it reads has changed (the board
/// probe, a key), except that dev mode works it out anyway every <see cref="BoardProbe.CheckEvery"/>th such frame and compares. A
/// difference means the probe or the key missed a change: logged once (<see cref="BoardProbe.Missed"/>), so a stale result shows up
/// in the logs before it shows on screen. An optimization that can be wrong carries the check that says when it is.
/// </summary>
public sealed class ReuseCheck
{
    private readonly string _what;
    private int _reusedSinceCheck;

    /// <param name="what">What's kept, as the log names it ("the turn forecast", "Card Indicators").</param>
    public ReuseCheck(string what) => _what = what;

    /// <param name="unchanged">Nothing the result reads changed since it was worked out (and there is one).</param>
    public FrameReuse Decide(bool unchanged)
    {
        if (!unchanged) return FrameReuse.WorkOut;
        if (!ModEntry.DevMode || ++_reusedSinceCheck < BoardProbe.CheckEvery) return FrameReuse.Reuse;
        _reusedSinceCheck = 0;
        return FrameReuse.WorkOutAndCheck;
    }

    /// <summary>After <see cref="FrameReuse.WorkOutAndCheck"/>: whether the result worked out is the kept one; logs it when not.</summary>
    /// <param name="why">How it differed, free of names and numbers (each one is logged once).</param>
    /// <param name="detail">What differed, worked out only when it did.</param>
    public void Compare(bool same, string why, Func<string?>? detail = null)
    {
        if (!same) BoardProbe.Missed(_what, why, detail?.Invoke());
    }
}
