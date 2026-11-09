using Godot;

namespace Sts2Mods.Core.Ui;

/// <summary>
/// A floating panel the player can place.
///
/// Placement model:
///  - The **pin** (WidgetLayout.Anchor) is the widget's own corner/edge that stays fixed. When
///    the widget grows or shrinks (e.g. more counter rows), the pinned point doesn't move.
///  - **Screen mode:** the pin is measured from the same point of the screen (bottom-left pin →
///    from the screen's bottom-left corner), plus an offset.
///  - **Attached mode** (FollowGame): the pin is measured from a reference point beside a game
///    element (e.g. just right of the HP bar's bottom-right corner), plus an offset. Each attach
///    point has a natural pin (bottom-left for "right of, bottom-aligned"); choosing a pin in the
///    picker overrides it.
///
/// Locked (default): click-through, shown only when it has content.
/// Unlocked (hotkey): outlined and always shown, with a header holding a 3×3 pin picker and an
/// Attach toggle. Drag to move, mouse wheel to scale, right-click to reset.
/// </summary>
public sealed class MovableWidget
{
    private static readonly Color PinIdle = new(1f, 1f, 1f, 0.25f);
    private static readonly Color PinActive = new(1f, 0.6f, 0.15f, 1f);

    private readonly string _title;
    private readonly WidgetLayout _default;
    private readonly WidgetLayout _layout;
    private readonly StyleBoxFlat _lockedStyle;
    private readonly StyleBoxFlat _unlockedStyle;
    private readonly HBoxContainer _header;
    private readonly Label _headerLabel;
    private readonly Button[] _pinButtons = new Button[9];
    private readonly Button _attachButton;
    private readonly OverlayHost _host;
    private readonly List<(Func<Rect2?> Target, AttachSide Side)> _attach = new();

    private bool _dragging;
    private Vector2 _dragGrab;

    public MovableWidget(OverlayHost host, string id, string title, WidgetLayout layout, WidgetLayout defaultLayout)
    {
        Id = id;
        _title = title;
        _layout = layout;
        _default = defaultLayout.Clone();
        _host = host;

        _lockedStyle = UiKit.PanelStyle(new Color(0.05f, 0.05f, 0.08f, 0.55f));
        _unlockedStyle = UiKit.PanelStyle(new Color(0.1f, 0.1f, 0.18f, 0.85f));
        _unlockedStyle.SetBorderWidthAll(2);
        _unlockedStyle.BorderColor = new Color(1f, 0.55f, 0.1f);

        Root = new PanelContainer { Name = "Widget_" + id, MouseFilter = Control.MouseFilterEnum.Ignore };
        Root.AddThemeStyleboxOverride("panel", _lockedStyle);

        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 4);
        Root.AddChild(box);

        // Header, shown only while the layout is unlocked.
        _header = UiKit.Row(8);
        _header.Visible = false;
        _headerLabel = UiKit.Label(title, 14, new Color(1f, 0.7f, 0.3f));
        _header.AddChild(_headerLabel);

        var pinGrid = new GridContainer { Columns = 3, MouseFilter = Control.MouseFilterEnum.Ignore, TooltipText = "Pin: which corner stays put" };
        pinGrid.AddThemeConstantOverride("h_separation", 2);
        pinGrid.AddThemeConstantOverride("v_separation", 2);
        var anchors = (ScreenAnchor[])Enum.GetValues(typeof(ScreenAnchor));
        for (var i = 0; i < 9; i++)
        {
            var anchor = anchors[i];
            var b = new Button
            {
                CustomMinimumSize = new Vector2(12, 12),
                FocusMode = Control.FocusModeEnum.None,
                MouseFilter = Control.MouseFilterEnum.Stop,
                TooltipText = $"Pin {anchor}",
            };
            b.AddThemeStyleboxOverride("normal", UiKit.PanelStyle(Colors.White, 2, 0));
            b.AddThemeStyleboxOverride("hover", UiKit.PanelStyle(Colors.White, 2, 0));
            b.AddThemeStyleboxOverride("pressed", UiKit.PanelStyle(Colors.White, 2, 0));
            b.Pressed += () => SetPin(anchor);
            _pinButtons[i] = b;
            pinGrid.AddChild(b);
        }
        _header.AddChild(pinGrid);

        _attachButton = new Button
        {
            Text = "Attach",
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Stop,
            TooltipText = "Follow your character, or stay fixed on screen",
            Visible = false,
        };
        _attachButton.AddThemeFontSizeOverride("font_size", 13);
        _attachButton.Pressed += ToggleAttach;
        _header.AddChild(_attachButton);
        box.AddChild(_header);

        Content = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        Content.AddThemeConstantOverride("separation", 3);
        box.AddChild(Content);

        Root.GuiInput += OnGuiInput;
        Root.MouseEntered += () => Hovered = true;
        Root.MouseExited += () => Hovered = false;
        host.WidgetRoot!.AddChild(Root);
        host.RegisterWidget(this);
    }

    public string Id { get; }
    public PanelContainer Root { get; }

    /// <summary>Put the widget's content here.</summary>
    public VBoxContainer Content { get; }

    /// <summary>Set by the owning feature each frame: does the widget have anything to show?</summary>
    public bool HasContent { get; set; }

    /// <summary>Mouse is over the widget (only meaningful while the layout is unlocked).</summary>
    public bool Hovered { get; private set; }

    /// <summary>
    /// Adds a game element this widget can attach to. Candidates are tried in order; the first
    /// whose placement fits on screen wins (e.g. "left of the block icon", else "below the HP bar").
    /// The target returns the element's screen rect, or null when it isn't on screen.
    /// </summary>
    public MovableWidget AttachTo(Func<Rect2?> target, AttachSide side)
    {
        _attach.Add((target, side));
        return this;
    }

    private bool Attached => _layout.FollowGame && _attach.Count > 0;

    // ---------- placement ----------

    /// <summary>Reference point and pin for the current mode. Null reference = not placeable right now.</summary>
    private (Vector2 Ref, ScreenAnchor Pin)? Placement(Vector2 size)
    {
        var vp = _host.ViewportSize;
        if (Attached)
        {
            var offset = new Vector2(_layout.OffsetX, _layout.OffsetY);
            (Vector2, ScreenAnchor)? fallback = null;
            foreach (var (target, side) in _attach)
            {
                if (target() is not { } rect) continue;
                var (reference, natural) = AttachReference(rect, side);
                var pin = _layout.CustomPin ? _layout.Anchor : natural;
                var pos = TopLeft(reference + offset, pin, size);
                if (pos.X >= 0 && pos.Y >= 0 && pos.X + size.X <= vp.X && pos.Y + size.Y <= vp.Y) return (reference, pin);
                fallback ??= (reference, pin);
            }
            if (fallback != null) return fallback;
            // Element not on screen: fall through to screen mode with the same pin.
        }
        var (fx, fy) = WidgetLayout.Fraction(_layout.Anchor);
        return (new Vector2(vp.X * fx, vp.Y * fy), _layout.Anchor);
    }

    /// <summary>Reference point beside the target, and the pin that suits it.</summary>
    private static (Vector2 Ref, ScreenAnchor Pin) AttachReference(Rect2 target, AttachSide side) => side switch
    {
        AttachSide.LeftOf => (new Vector2(target.Position.X - 6, target.Position.Y + target.Size.Y / 2), ScreenAnchor.Right),
        AttachSide.RightOfBottom => (new Vector2(target.End.X + 12, target.End.Y), ScreenAnchor.BottomLeft),
        _ => (new Vector2(target.Position.X + target.Size.X / 2, target.End.Y), ScreenAnchor.Top),
    };

    private static Vector2 TopLeft(Vector2 pinPoint, ScreenAnchor pin, Vector2 size)
    {
        var (fx, fy) = WidgetLayout.Fraction(pin);
        return pinPoint - new Vector2(size.X * fx, size.Y * fy);
    }

    private static Vector2 PinPoint(Vector2 topLeft, ScreenAnchor pin, Vector2 size)
    {
        var (fx, fy) = WidgetLayout.Fraction(pin);
        return topLeft + new Vector2(size.X * fx, size.Y * fy);
    }

    internal void UpdateLayout(OverlayHost host)
    {
        var unlocked = host.LayoutUnlocked;
        Root.Visible = !host.HideAll && !host.Suppressed && (HasContent || unlocked);
        Root.MouseFilter = unlocked ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
        Root.AddThemeStyleboxOverride("panel", unlocked ? _unlockedStyle : _lockedStyle);
        Root.Modulate = new Color(1, 1, 1, Mathf.Clamp(_layout.Opacity, 0.1f, 1f));

        _header.Visible = unlocked;
        if (unlocked) RefreshHeader();

        Root.ResetSize();
        var scale = Mathf.Clamp(_layout.Scale, 0.4f, 3f);
        Root.Scale = new Vector2(scale, scale);

        if (_dragging) return;

        var size = Root.Size * scale;
        if (Placement(size) is not { } p) return;
        var pos = TopLeft(p.Ref + new Vector2(_layout.OffsetX, _layout.OffsetY), p.Pin, size);
        Root.Position = Clamp(pos, size, host.ViewportSize);
    }

    private void RefreshHeader()
    {
        var size = Root.Size * Root.Scale;
        var pin = Placement(size)?.Pin ?? _layout.Anchor;
        for (var i = 0; i < 9; i++)
        {
            _pinButtons[i].SelfModulate = (ScreenAnchor)i == pin ? PinActive : PinIdle;
        }
        _attachButton.Visible = _attach.Count > 0;
        _attachButton.Text = Attached ? "Attached" : "Attach";
        _headerLabel.Text = _title;
    }

    private static Vector2 Clamp(Vector2 pos, Vector2 size, Vector2 vp)
    {
        return new Vector2(
            Mathf.Clamp(pos.X, 0, Mathf.Max(0, vp.X - size.X)),
            Mathf.Clamp(pos.Y, 0, Mathf.Max(0, vp.Y - size.Y)));
    }

    /// <summary>Recompute the stored offset so the widget stays exactly where it is now.</summary>
    private void StoreCurrentPosition()
    {
        var size = Root.Size * Root.Scale;
        if (Placement(size) is not { } p) return;
        var off = PinPoint(Root.Position, p.Pin, size) - p.Ref;
        _layout.OffsetX = off.X;
        _layout.OffsetY = off.Y;
        _host.RaiseLayoutChanged();
    }

    // ---------- player actions ----------

    /// <summary>Choose which corner/edge stays put. The widget doesn't move.</summary>
    internal void SetPin(ScreenAnchor pin)
    {
        _layout.Anchor = pin;
        _layout.CustomPin = true;
        StoreCurrentPosition();
        _host.Toast($"{_title}: pinned {pin}{(Attached ? " (attached)" : "")}", 2.5f);
    }

    private void ToggleAttach()
    {
        if (_attach.Count == 0) return;
        _layout.FollowGame = !_layout.FollowGame;
        StoreCurrentPosition();
        _host.Toast(_layout.FollowGame ? $"{_title}: follows your character" : $"{_title}: fixed on screen", 2.5f);
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
                    StoreCurrentPosition();
                }
                Root.AcceptEvent();
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.Right)
            {
                CopyFrom(_default);
                _host.RaiseLayoutChanged();
                _host.Toast($"{_title}: reset to default", 2f);
                Root.AcceptEvent();
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.Middle)
            {
                ToggleAttach();
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

    private void CopyFrom(WidgetLayout src)
    {
        _layout.Anchor = src.Anchor;
        _layout.OffsetX = src.OffsetX;
        _layout.OffsetY = src.OffsetY;
        _layout.Scale = src.Scale;
        _layout.Opacity = src.Opacity;
        _layout.FollowGame = src.FollowGame;
        _layout.CustomPin = src.CustomPin;
    }
}
