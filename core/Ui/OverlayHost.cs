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

        if (Layer != null)
        {
            Layer.Visible = !HideAll;
        }

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

        foreach (var w in _widgets)
        {
            w.UpdateLayout(this);
        }

        if (_toast != null)
        {
            _toast.Visible = Godot.Time.GetTicksMsec() < _toastUntilMs;
        }
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
        if (Pressed(HideAllKey))
        {
            HideAll = !HideAll;
            Toast(HideAll ? $"{ModId}: overlays hidden ({HideAllKey} to show)" : $"{ModId}: overlays shown", 2.5f);
        }
    }

    private bool Pressed(Key key)
    {
        var down = Input.IsPhysicalKeyPressed(key);
        _keyWasDown.TryGetValue(key, out var was);
        _keyWasDown[key] = down;
        return down && !was;
    }
}
