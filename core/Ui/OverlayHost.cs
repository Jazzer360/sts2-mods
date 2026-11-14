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

    /// <summary>
    /// Settings lookup: is this feature switched on right now? A feature switched off is skipped
    /// each frame and its widgets are hidden, so features can be toggled while the game runs.
    /// Null = all on.
    /// </summary>
    public Func<string, bool>? FeatureEnabled { get; set; }

    private readonly Dictionary<MovableWidget, string> _widgetOwner = new();
    private readonly List<(Key Key, Action Action)> _hotkeys = new();
    private string? _running; // feature whose Attach/OnFrame is running (owns widgets created now)

    /// <summary>Runs <paramref name="action"/> when <paramref name="key"/> is pressed (polled each frame).</summary>
    public void AddHotkey(Key key, Action action) => _hotkeys.Add((key, action));

    private bool IsOn(string id)
    {
        try { return FeatureEnabled?.Invoke(id) ?? true; }
        catch { return true; }
    }

    /// <summary>Runs after all features each frame (e.g. a shared renderer).</summary>
    private readonly Dictionary<string, int> _lateErrors = new();

    public void AddLateFrame(Action action) => _lateFrame.Add(action);

    internal void RegisterWidget(MovableWidget widget)
    {
        _widgets.Add(widget);
        if (_running != null) _widgetOwner[widget] = _running;
    }

    /// <summary>False when the feature that made this widget is switched off.</summary>
    internal bool OwnerOn(MovableWidget widget) => !_widgetOwner.TryGetValue(widget, out var id) || IsOn(id);

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
            _running = id;
            try
            {
                feature.Attach(this);
            }
            catch (Exception ex)
            {
                _errors[id] = MaxErrorsBeforeDisable;
                _log.Error($"[{id}] failed to attach; feature turned off. {ex}");
                Diagnostics.ErrorReporter.Instance?.Exception(ex, id + ".Attach");
            }
        }

        _running = null;
        tree.ProcessFrame += OnFrame;
        _log.Info($"overlay ready ({_features.Count} frame features). {UnlockLayoutKey}: unlock layout, {HideAllKey}: hide all");
    }

    // ---- slow frames: the mod's own time per frame (logged, and reported when it's ours), and frames the game
    // itself stalled on (logged only: a slow PC or the game's own work is nothing for us to fix).
    /// <summary>The mod's work in one frame above this is logged, every time (about 30 fps).</summary>
    private const double SlowModFrameMs = 30;
    /// <summary>
    /// The mod's work in one frame above this is reported (a stutter on its own, 20 fps): grouped by the slowest
    /// feature, from that feature's second such frame this session (the first can be its code's warm-up).
    /// </summary>
    private const double ReportModFrameMs = 50;
    /// <summary>A whole frame (the gap between two frames) above this, with the mod's share small, is logged (the
    /// game caps its frame rate at 30 in the background, so lower would log every frame there).</summary>
    private const double SlowGameFrameMs = 50;
    /// <summary>The game's own slow frames are logged at most this often, with a count of the ones in between.</summary>
    private const ulong GameStallLogIntervalMs = 2000;
    private readonly List<(string Id, double Ms)> _featureMs = new();
    private readonly Dictionary<string, int> _slowByFeature = new();
    private ulong _lastFrameStartUs, _lastStallLogMs;
    private double _lastModMs;
    private int _stallsSkipped;

    /// <summary>A slow frame of the mod's own: logged, and reported from a feature's second one this session.</summary>
    private void ModFrameSlow(double modMs, double gapMs)
    {
        var times = FeatureTimes();
        _log.Warn($"[perf] slow frame: the mod took {modMs:0} ms ({times})");
        if (modMs <= ReportModFrameMs) return;
        var top = _featureMs.OrderByDescending(x => x.Ms).FirstOrDefault().Id ?? "?";
        var n = _slowByFeature[top] = (_slowByFeature.TryGetValue(top, out var prev) ? prev : 0) + 1;
        if (n < 2) return;
        Diagnostics.ErrorReporter.Instance?.Mismatch("perf", $"Slow frame: {top}", new[] { "slow-frame", top },
            $"The mod's own work took {modMs:0} ms in one frame ({times}); {gapMs:0} ms since the frame before. Slow frame {n} from {top} this session.",
            tags: new Dictionary<string, string> { ["source"] = top, ["check"] = "slow-frame" });
    }

    private void OnFrame()
    {
        var start = Godot.Time.GetTicksUsec();
        var gapMs = _lastFrameStartUs == 0 ? 0 : (start - _lastFrameStartUs) / 1000.0;
        var prevModMs = _lastModMs;
        _lastFrameStartUs = start;
        _featureMs.Clear();
        RunFrame();
        var modMs = (Godot.Time.GetTicksUsec() - start) / 1000.0;
        _lastModMs = modMs;
        try
        {
            if (modMs > SlowModFrameMs)
            {
                ModFrameSlow(modMs, gapMs);
                return;
            }
            if (gapMs <= SlowGameFrameMs || prevModMs >= SlowModFrameMs) return;
            var now = Godot.Time.GetTicksMsec();
            if (now - _lastStallLogMs < GameStallLogIntervalMs) { _stallsSkipped++; return; }
            var line = $"[perf] slow frame: {gapMs:0} ms since the last frame, the mod's share {prevModMs:0.0} ms (the game's own work)";
            if (_stallsSkipped > 0) line += $"; {_stallsSkipped} more since the last line";
            _stallsSkipped = 0;
            _lastStallLogMs = now;
            _log.Info(line);
        }
        catch
        {
            // diagnostics only
        }
    }

    private string FeatureTimes()
        => string.Join(", ", _featureMs.Where(x => x.Ms >= 1).OrderByDescending(x => x.Ms).Select(x => $"{x.Id} {x.Ms:0} ms")) is { Length: > 0 } s ? s : "spread thin";

    private void RunFrame()
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
            if (!IsOn(id)) continue;
            _running = id;
            var featureStart = Godot.Time.GetTicksUsec();
            try
            {
                feature.OnFrame(this);
            }
            catch (Exception ex)
            {
                n = _errors[id] = (_errors.TryGetValue(id, out var prev) ? prev : 0) + 1;
                _log.Error($"[{id}] frame error {n}/{MaxErrorsBeforeDisable}: {ex}");
                Diagnostics.ErrorReporter.Instance?.Exception(ex, id + ".OnFrame");
                if (n >= MaxErrorsBeforeDisable)
                {
                    _log.Error($"[{id}] turned off after repeated errors");
                }
            }
            _featureMs.Add((id, (Godot.Time.GetTicksUsec() - featureStart) / 1000.0));
        }

        _running = null;
        var lateStart = Godot.Time.GetTicksUsec();

        foreach (var late in _lateFrame)
        {
            try { late(); }
            catch (Exception ex)
            {
                // Log the first occurrence in full, then only a count every 600 repeats (~10s), so a
                // per-frame failure can't flood the game log.
                var key = ex.GetType().Name + ex.TargetSite;
                var n = _lateErrors[key] = (_lateErrors.TryGetValue(key, out var prev) ? prev : 0) + 1;
                if (n == 1)
                {
                    _log.Error($"late frame error: {ex}");
                    Diagnostics.ErrorReporter.Instance?.Exception(ex, "late-frame");
                }
                else if (n % 600 == 0) _log.Error($"late frame error repeated {n} times: {ex.GetType().Name}: {ex.Message}");
            }
        }

        _featureMs.Add(("late frame", (Godot.Time.GetTicksUsec() - lateStart) / 1000.0));
        var layoutStart = Godot.Time.GetTicksUsec();
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
        _featureMs.Add(("widgets", (Godot.Time.GetTicksUsec() - layoutStart) / 1000.0));
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
        // Ctrl+1–9 over a widget (layout unlocked) is a shortcut for its pin picker, laid out like
        // a numpad: 7 8 9 = top row, 4 5 6 = middle, 1 2 3 = bottom. Ctrl avoids the game's
        // number-key card shortcuts.
        if (LayoutUnlocked && Input.IsPhysicalKeyPressed(Key.Ctrl))
        {
            foreach (var (key, anchor) in AnchorKeys)
            {
                if (!Pressed(key)) continue;
                var hovered = _widgets.FirstOrDefault(w => w.Hovered);
                if (hovered != null) hovered.SetPin(anchor);
                else Toast("Hover a widget, then Ctrl+1–9 to pick its anchor", 2.5f);
            }
        }
        foreach (var (key, action) in _hotkeys)
        {
            if (!Pressed(key)) continue;
            try { action(); }
            catch (Exception ex)
            {
                _log.Error($"hotkey {key} failed: {ex}");
                Diagnostics.ErrorReporter.Instance?.Exception(ex, "hotkey");
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
