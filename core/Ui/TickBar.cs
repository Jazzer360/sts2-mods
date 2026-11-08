using Godot;

namespace Sts2Mods.Core.Ui;

/// <summary>
/// A condensed progress bar split by tick marks: one segment per step.
/// Built from ColorRects (mods can't override _Draw).
/// </summary>
public sealed class TickBar
{
    private readonly List<ColorRect> _segments = new();
    private readonly float _segWidth;
    private readonly float _height;

    public TickBar(float segWidth = 9, float height = 12)
    {
        _segWidth = segWidth;
        _height = height;
        Root = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        Root.AddThemeConstantOverride("separation", 2);
        Root.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
    }

    public HBoxContainer Root { get; }

    public void Set(int filled, int total, Color fill, Color empty)
    {
        total = Math.Clamp(total, 1, 40);
        while (_segments.Count < total)
        {
            var seg = new ColorRect
            {
                CustomMinimumSize = new Vector2(_segWidth, _height),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _segments.Add(seg);
            Root.AddChild(seg);
        }
        for (var i = 0; i < _segments.Count; i++)
        {
            _segments[i].Visible = i < total;
            _segments[i].Color = i < filled ? fill : empty;
        }
    }
}
