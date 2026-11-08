namespace Sts2Mods.Core.Ui;

/// <summary>The nine screen anchors a widget can be pinned to.</summary>
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
