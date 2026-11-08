using Godot;

namespace Sts2Mods.Core.Ui;

/// <summary>
/// A floating panel the player can place.
///
/// Locked (default): click-through, shown only when it has content.
/// Unlocked (hotkey): outlined and always shown; drag to move, mouse wheel to scale,
/// right-click to reset. On drop it re-anchors to the nearest of nine screen anchors,
/// so the saved position survives resolution changes.
/// </summary>
public sealed class MovableWidget
{
    private readonly string _title;
    private readonly WidgetLayout _default;
    private readonly WidgetLayout _layout;
    private readonly StyleBoxFlat _lockedStyle;
    private readonly StyleBoxFlat _unlockedStyle;
    private readonly Label _placeholder;

    private bool _dragging;
    private Vector2 _dragGrab;

    public MovableWidget(OverlayHost host, string id, string title, WidgetLayout layout, WidgetLayout defaultLayout)
    {
        Id = id;
        _title = title;
        _layout = layout;
        _default = defaultLayout.Clone();

        _lockedStyle = UiKit.PanelStyle(new Color(0.05f, 0.05f, 0.08f, 0.55f));
        _unlockedStyle = UiKit.PanelStyle(new Color(0.1f, 0.1f, 0.18f, 0.8f));
        _unlockedStyle.SetBorderWidthAll(2);
        _unlockedStyle.BorderColor = new Color(1f, 0.55f, 0.1f);

        Root = new PanelContainer { Name = "Widget_" + id, MouseFilter = Control.MouseFilterEnum.Ignore };
        Root.AddThemeStyleboxOverride("panel", _lockedStyle);

        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 2);
        Root.AddChild(box);

        _placeholder = UiKit.Label(title, 14, new Color(1f, 0.7f, 0.3f));
        _placeholder.Visible = false;
        box.AddChild(_placeholder);

        Content = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        Content.AddThemeConstantOverride("separation", 3);
        box.AddChild(Content);

        Root.GuiInput += OnGuiInput;
        host.Layer!.AddChild(Root);
        host.RegisterWidget(this);
        _host = host;
    }

    private readonly OverlayHost _host;

    public string Id { get; }
    public PanelContainer Root { get; }

    /// <summary>Put the widget's content here.</summary>
    public VBoxContainer Content { get; }

    /// <summary>Set by the owning feature each frame: does the widget have anything to show?</summary>
    public bool HasContent { get; set; }

    internal void UpdateLayout(OverlayHost host)
    {
        var unlocked = host.LayoutUnlocked;
        Root.Visible = !host.HideAll && (HasContent || unlocked);
        Root.MouseFilter = unlocked ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
        Root.AddThemeStyleboxOverride("panel", unlocked ? _unlockedStyle : _lockedStyle);
        _placeholder.Visible = unlocked;
        _placeholder.Text = unlocked ? $"{_title}  ({_layout.Anchor})" : _title;
        Root.Modulate = new Color(1, 1, 1, Mathf.Clamp(_layout.Opacity, 0.1f, 1f));

        Root.ResetSize();
        var scale = Mathf.Clamp(_layout.Scale, 0.4f, 3f);
        Root.Scale = new Vector2(scale, scale);

        if (_dragging) return;

        var vp = host.ViewportSize;
        var size = Root.Size * scale;
        var (fx, fy) = WidgetLayout.Fraction(_layout.Anchor);
        var pos = new Vector2(vp.X * fx - size.X * fx + _layout.OffsetX, vp.Y * fy - size.Y * fy + _layout.OffsetY);
        Root.Position = Clamp(pos, size, vp);
    }

    private static Vector2 Clamp(Vector2 pos, Vector2 size, Vector2 vp)
    {
        return new Vector2(
            Mathf.Clamp(pos.X, 0, Mathf.Max(0, vp.X - size.X)),
            Mathf.Clamp(pos.Y, 0, Mathf.Max(0, vp.Y - size.Y)));
    }

    private void OnGuiInput(InputEvent ev)
    {
        if (!_host.LayoutUnlocked) return;

        if (ev is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed)
                {
                    _dragging = true;
                    _dragGrab = mb.GlobalPosition - Root.Position;
                }
                else if (_dragging)
                {
                    _dragging = false;
                    Reanchor();
                }
                Root.AcceptEvent();
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.Right)
            {
                CopyFrom(_default);
                _host.RaiseLayoutChanged();
                Root.AcceptEvent();
            }
            else if (mb.Pressed && (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown))
            {
                var step = mb.ButtonIndex == MouseButton.WheelUp ? 0.1f : -0.1f;
                _layout.Scale = Mathf.Clamp(_layout.Scale + step, 0.4f, 3f);
                _host.RaiseLayoutChanged();
                Root.AcceptEvent();
            }
        }
        else if (ev is InputEventMouseMotion mm && _dragging)
        {
            var size = Root.Size * Root.Scale;
            Root.Position = Clamp(mm.GlobalPosition - _dragGrab, size, _host.ViewportSize);
            Root.AcceptEvent();
        }
    }

    /// <summary>Pick the nearest of the nine anchors to the widget's center and store an offset from it.</summary>
    private void Reanchor()
    {
        var vp = _host.ViewportSize;
        var size = Root.Size * Root.Scale;
        var center = Root.Position + size / 2;

        var best = ScreenAnchor.Center;
        var bestDist = float.MaxValue;
        foreach (ScreenAnchor a in Enum.GetValues(typeof(ScreenAnchor)))
        {
            var (fx, fy) = WidgetLayout.Fraction(a);
            var d = center.DistanceSquaredTo(new Vector2(vp.X * fx, vp.Y * fy));
            if (d < bestDist)
            {
                bestDist = d;
                best = a;
            }
        }

        var (bx, by) = WidgetLayout.Fraction(best);
        _layout.Anchor = best;
        _layout.OffsetX = Root.Position.X - (vp.X * bx - size.X * bx);
        _layout.OffsetY = Root.Position.Y - (vp.Y * by - size.Y * by);
        _host.RaiseLayoutChanged();
    }

    private void CopyFrom(WidgetLayout src)
    {
        _layout.Anchor = src.Anchor;
        _layout.OffsetX = src.OffsetX;
        _layout.OffsetY = src.OffsetY;
        _layout.Scale = src.Scale;
        _layout.Opacity = src.Opacity;
    }
}
