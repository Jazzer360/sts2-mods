using Godot;
using Sts2Mods.Core.Features;

namespace Sts2Mods.Core.Ui;

/// <summary>
/// Owns the mod's overlay CanvasLayer and the per-frame loop.
///  - Creates a CanvasLayer under the scene tree root (survives scene changes).
///  - Calls every IFrameFeature each frame; a feature that keeps throwing is turned off.
///  - Handles hotkeys: toggle layout lock, hide all overlays.
/// </summary>
public sealed class OverlayHost
{
    private const int MaxErrorsBeforeDisable = 5;

    private readonly ModLog _log;
    private readonly List<(string Id, IFrameFeature Feature)> _features = new();
    private readonly Dictionary<string, int> _errors = new();
    private readonly List<Action> _lateFrame = new();
    private readonly List<MovableWidget> _widgets = new();
    private readonly Dictionary<Key, bool> _keyWasDown = new();

    private Label? _toast;
    private ulong _toastUntilMs;

    public OverlayHost(string modId, ModLog log)
    {
        ModId = modId;
        _log = log;
    }

    public string ModId { get; }
    public ModLog Log => _log;
    public CanvasLayer? Layer { get; private set; }

    /// <summary>When unlocked, widgets can be dragged, scaled and reset.</summary>
    public bool LayoutUnlocked { get; private set; }

    /// <summary>When true, every overlay is hidden (hotkey).</summary>
    public bool HideAll { get; private set; }

    /// <summary>
    /// When this returns true the floating widgets hide, so game screens opened on top of combat
    /// (pause menu, pile views, rewards, map) aren't covered. Card decorations live on the card
    /// nodes themselves, so they already sit underneath those screens.
    /// </summary>
    public Func<bool>? SuppressWhen { get; set; }

    public bool Suppressed { get; private set; }

    public Key UnlockLayoutKey { get; set; } = Key.F8;
    public Key HideAllKey { get; set; } = Key.F9;

    /// <summary>Seconds since start, for pulse/flash animations.</summary>
    public float Time => (float)(Godot.Time.GetTicksMsec() / 1000.0);

    public Vector2 ViewportSize => Layer?.GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920, 1080);

    /// <summary>Raised when a widget is moved, scaled or reset, so the mod can save.</summary>
    public event Action? LayoutChanged;

    public void AddFeature(string id, IFrameFeature feature) => _features.Add((id, feature));

    /// <summary>Runs after all features each frame (e.g. a shared renderer).</summary>
    public void AddLateFrame(Action action) => _lateFrame.Add(action);

    internal void RegisterWidget(MovableWidget widget) => _widgets.Add(widget);

    internal void RaiseLayoutChanged() => LayoutChanged?.Invoke();

    /// <summary>Hooks the frame loop. Safe to call from the mod initializer.</summary>
    public void Start()
    {
        Callable.From(StartOnMainThread).CallDeferred();
    }

    private void StartOnMainThread()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            _log.Error("no SceneTree; overlays disabled");
            return;
        }

        Layer = new CanvasLayer { Name = ModId + "_Overlay", Layer = 10 };
        tree.Root.AddChild(Layer);

        WidgetRoot = new Control { Name = ModId + "_Widgets", MouseFilter = Control.MouseFilterEnum.Ignore };
        Layer.AddChild(WidgetRoot);

        _toast = UiKit.Label("", 20, Colors.White);
        _toast.Position = new Vector2(24, 24);
        _toast.Visible = false;
        Layer.AddChild(_toast);

        foreach (var (id, feature) in _features)
        {
            try
            {
                feature.Attach(this);
            }
            catch (Exception ex)
            {
                _errors[id] = MaxErrorsBeforeDisable;
                _log.Error($"[{id}] failed to attach; feature turned off. {ex}");
            }
        }

        tree.ProcessFrame += OnFrame;
        _log.Info($"overlay ready ({_features.Count} frame features). {UnlockLayoutKey}: unlock layout, {HideAllKey}: hide all");
    }

    private void OnFrame()
    {
        HandleHotkeys();

        try
        {
            Suppressed = SuppressWhen?.Invoke() ?? false;
        }
        catch
        {
            Suppressed = false;
        }
        if (Layer != null)
        {
            // The toast stays visible even when suppressed, so hotkey feedback still shows.
            Layer.Visible = !HideAll || (_toast?.Visible ?? false);
        }
        PlaceWidgetRoot();

        foreach (var (id, feature) in _features)
        {
            if (_errors.TryGetValue(id, out var n) && n >= MaxErrorsBeforeDisable)
            {
                continue;
            }
            try
            {
                feature.OnFrame(this);
            }
            catch (Exception ex)
            {
                n = _errors[id] = (_errors.TryGetValue(id, out var prev) ? prev : 0) + 1;
                _log.Error($"[{id}] frame error {n}/{MaxErrorsBeforeDisable}: {ex}");
                if (n >= MaxErrorsBeforeDisable)
                {
                    _log.Error($"[{id}] turned off after repeated errors");
                }
            }
        }

        foreach (var late in _lateFrame)
        {
            try { late(); }
            catch (Exception ex) { _log.Error($"late frame error: {ex}"); }
        }

        if (WidgetRoot != null)
        {
            foreach (var w in _widgets)
            {
                w.UpdateLayout(this);
            }
        }

        if (_toast != null)
        {
            _toast.Visible = Godot.Time.GetTicksMsec() < _toastUntilMs;
        }
    }

    /// <summary>
    /// Where widgets should draw, as (parent, index): e.g. just before the game's hand node, so
    /// cards draw over them. Null = on our own top layer. Ignored while the layout is unlocked,
    /// so the hand can't swallow drag clicks.
    /// </summary>
    public Func<(Node Parent, int Index)?>? WidgetUnderlay { get; set; }

    /// <summary>Container for all movable widgets. Positions inside it are screen coordinates.</summary>
    public Control? WidgetRoot { get; private set; }

    private void PlaceWidgetRoot()
    {
        if (WidgetRoot == null || Layer == null) return;
        if (!GodotObject.IsInstanceValid(WidgetRoot))
        {
            // Freed along with a game node it was parked under. Shouldn't happen (see
            // WidgetUnderlay), but if it does, say so once instead of erroring every frame.
            _log.Error("widget container was freed with a game node; floating widgets are gone until restart");
            WidgetRoot = null;
            return;
        }

        (Node Parent, int Index)? target = null;
        if (!LayoutUnlocked)
        {
            try { target = WidgetUnderlay?.Invoke(); }
            catch { target = null; }
        }

        if (target is { } t && GodotObject.IsInstanceValid(t.Parent))
        {
            if (WidgetRoot.GetParent() != t.Parent)
            {
                WidgetRoot.Reparent(t.Parent, keepGlobalTransform: false);
            }
            // Keep it directly before the target index (the hand), so the hand draws on top.
            var want = Math.Max(0, t.Index - (WidgetRoot.GetIndex() < t.Index ? 1 : 0));
            if (WidgetRoot.GetIndex() != want) t.Parent.MoveChild(WidgetRoot, want);
        }
        else if (WidgetRoot.GetParent() != Layer)
        {
            WidgetRoot.Reparent(Layer, keepGlobalTransform: false);
        }

        // Cancel the parent's offset so widget positions stay in screen coordinates.
        WidgetRoot.Scale = Vector2.One;
        WidgetRoot.GlobalPosition = Vector2.Zero;
    }

    public void Toast(string text, float seconds = 4f)
    {
        if (_toast == null) return;
        _toast.Text = text;
        _toastUntilMs = Godot.Time.GetTicksMsec() + (ulong)(seconds * 1000);
    }

    private void HandleHotkeys()
    {
        if (Pressed(UnlockLayoutKey))
        {
            LayoutUnlocked = !LayoutUnlocked;
            Toast(LayoutUnlocked
                ? $"{ModId}: layout UNLOCKED. Drag to move, scroll to scale, right-click to reset. {UnlockLayoutKey} to lock."
                : $"{ModId}: layout locked.");
            if (!LayoutUnlocked) RaiseLayoutChanged();
        }
        // Ctrl+1–9 over a widget (layout unlocked) pins it to that screen anchor, laid out like a
        // numpad: 7 8 9 = top row, 4 5 6 = middle, 1 2 3 = bottom. Ctrl avoids the game's
        // number-key card shortcuts.
        if (LayoutUnlocked && Input.IsPhysicalKeyPressed(Key.Ctrl))
        {
            foreach (var (key, anchor) in AnchorKeys)
            {
                if (!Pressed(key)) continue;
                var hovered = _widgets.FirstOrDefault(w => w.Hovered);
                if (hovered != null) hovered.SetAnchor(anchor);
                else Toast("Hover a widget, then Ctrl+1–9 to pick its anchor", 2.5f);
            }
        }
        if (Pressed(HideAllKey))
        {
            HideAll = !HideAll;
            Toast(HideAll ? $"{ModId}: overlays hidden ({HideAllKey} to show)" : $"{ModId}: overlays shown", 2.5f);
        }
    }

    private static readonly (Key Key, ScreenAnchor Anchor)[] AnchorKeys =
    {
        (Key.Key7, ScreenAnchor.TopLeft), (Key.Key8, ScreenAnchor.Top), (Key.Key9, ScreenAnchor.TopRight),
        (Key.Key4, ScreenAnchor.Left), (Key.Key5, ScreenAnchor.Center), (Key.Key6, ScreenAnchor.Right),
        (Key.Key1, ScreenAnchor.BottomLeft), (Key.Key2, ScreenAnchor.Bottom), (Key.Key3, ScreenAnchor.BottomRight),
    };

    private bool Pressed(Key key)
    {
        var down = Input.IsPhysicalKeyPressed(key);
        _keyWasDown.TryGetValue(key, out var was);
        _keyWasDown[key] = down;
        return down && !was;
    }
}
