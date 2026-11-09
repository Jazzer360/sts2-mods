namespace Sts2Mods.Core.Ui;

/// <summary>Where an attached widget sits relative to its game element.</summary>
public enum AttachSide
{
    Below,
    LeftOf,
    /// <summary>To the right, with the widget's bottom edge level with the target's bottom.</summary>
    RightOfBottom,
}

/// <summary>
/// A point on a 3×3 grid: used both for the widget's pin (which of its corners/edges stays
/// fixed) and, in screen mode, the matching point of the screen it's measured from.
/// </summary>
public enum ScreenAnchor
{
    TopLeft, Top, TopRight,
    Left, Center, Right,
    BottomLeft, Bottom, BottomRight,
}

/// <summary>
/// Saved placement of one widget: an anchor point on screen plus an offset from it.
/// Storing anchor + offset (not absolute pixels) keeps widgets in sensible places when
/// the resolution or window size changes.
/// </summary>
public sealed class WidgetLayout
{
    public ScreenAnchor Anchor { get; set; } = ScreenAnchor.Center;
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float Scale { get; set; } = 1f;
    public float Opacity { get; set; } = 1f;

    /// <summary>
    /// Attached to a game element (e.g. below the player's health bar) instead of a screen
    /// anchor. Offsets are then relative to that element's bottom-center. Middle-click toggles.
    /// </summary>
    public bool FollowGame { get; set; }

    /// <summary>
    /// The player chose the pin (Anchor) themselves. In attached mode this overrides the attach
    /// point's natural pin; in screen mode Anchor is always the pin.
    /// </summary>
    public bool CustomPin { get; set; }

    public WidgetLayout Clone() => (WidgetLayout)MemberwiseClone();

    public static (float X, float Y) Fraction(ScreenAnchor a) => a switch
    {
        ScreenAnchor.TopLeft => (0f, 0f),
        ScreenAnchor.Top => (0.5f, 0f),
        ScreenAnchor.TopRight => (1f, 0f),
        ScreenAnchor.Left => (0f, 0.5f),
        ScreenAnchor.Center => (0.5f, 0.5f),
        ScreenAnchor.Right => (1f, 0.5f),
        ScreenAnchor.BottomLeft => (0f, 1f),
        ScreenAnchor.Bottom => (0.5f, 1f),
        _ => (1f, 1f),
    };
}
