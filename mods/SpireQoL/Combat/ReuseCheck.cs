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
/// probe, a key), except that dev mode works it out anyway on such a frame once every <see cref="BoardProbe.CheckSeconds"/> and compares. A
/// difference means the probe or the key missed a change: logged once (<see cref="BoardProbe.Missed"/>), so a stale result shows up
/// in the logs before it shows on screen. An optimization that can be wrong carries the check that says when it is.
/// Every kept result checks on the same frame (decision #177: one check frame a second, not one per result at its own moment), and
/// tells the frame loop it did (OverlayHost.CountDevWork), so its time and memory are dev mode's, not the feature's, and the steady
/// watch leaves out one moment a second instead of several.
/// </summary>
public sealed class ReuseCheck
{
    private readonly string _what;
    private ulong _checkedFrame = ulong.MaxValue;
    /// <summary>The frame every result checks on, and when it was picked.</summary>
    private static ulong _checkFrame = ulong.MaxValue;
    private static long _checkFrameAt;

    /// <summary>Dev checks run since launch, every result's together (the dev stream marks the moments that had one: decision #176).</summary>
    public static int ChecksRun { get; private set; }

    /// <param name="what">What's kept, as the log names it ("the turn forecast", "Card Indicators").</param>
    public ReuseCheck(string what) => _what = what;

    /// <param name="unchanged">Nothing the result reads changed since it was worked out (and there is one).</param>
    public FrameReuse Decide(bool unchanged)
    {
        if (!unchanged) return FrameReuse.WorkOut;
        if (!ModEntry.DevMode) return FrameReuse.Reuse;
        var frame = Godot.Engine.GetProcessFrames();
        if (frame != _checkFrame)
        {
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_checkFrameAt != 0 && System.Diagnostics.Stopwatch.GetElapsedTime(_checkFrameAt, now).TotalSeconds < BoardProbe.CheckSeconds) return FrameReuse.Reuse;
            _checkFrame = frame;
            _checkFrameAt = now;
        }
        // Once on the check frame (a result asked for twice in it checks the first time).
        if (_checkedFrame == frame) return FrameReuse.Reuse;
        _checkedFrame = frame;
        ChecksRun++;
        Sts2Mods.Core.Ui.OverlayHost.CountDevWork();
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
