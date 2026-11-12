using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Debug;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// "Spire QoL: N versions behind" just above the game's "MODDED (n)" note on the main menu, when newer
/// releases are on GitHub, or "Spire QoL up to date!" when GitHub says there are none. Checked once each time the main menu appears (one request, off the main
/// thread), never during a run. Clicking it opens the releases page. Spec: docs/FEATURES.md#update-notice.
/// </summary>
public sealed class UpdateNoticeFeature : IFeature, IFrameFeature
{
    private const string Repo = "Jazzer360/sts2-mods";
    private const string TagPrefix = "spireqol-v";

    private static readonly Color Behind = new(0.62f, 0.85f, 0.62f);
    private static readonly Color UpToDate = new(0.75f, 0.75f, 0.72f, 0.8f);

    private bool _onMenu;
    private Task<int?>? _check;
    private int? _behind; // null: GitHub hasn't answered (nothing shown)
    private Label? _label;
    private Control? _warning;
    private int _searchCooldown;
    private bool _loggedMissing, _logged;

    public string Id => "update-notice";
    public string DisplayName => "Update Notice";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Frame-driven; no patches.
    }

    public void Attach(OverlayHost host)
    {
    }

    public void OnFrame(OverlayHost host)
    {
        var menu = NGame.Instance?.MainMenu;
        var onMenu = menu != null && GodotObject.IsInstanceValid(menu) && menu.IsVisibleInTree();
        if (onMenu && !_onMenu)
        {
            // Arrived at the main menu: ask once for this visit.
            var current = typeof(ModEntry).Assembly.GetName().Version ?? new Version(0, 0, 0);
            _check = Task.Run(() => GitHubReleases.CountNewer(Repo, TagPrefix, current, ModEntry.Id));
            _searchCooldown = 0;
            _loggedMissing = false;
        }
        _onMenu = onMenu;
        if (!onMenu) return;

        if (_check is { IsCompleted: true } done)
        {
            _check = null;
            var n = done.Status == TaskStatus.RanToCompletion ? done.Result : null;
            if (n != null)
            {
                _behind = n.Value;
                ModEntry.Log.Info(n > 0 ? $"update notice: {_behind} newer release(s) on GitHub" : "update notice: up to date");
            }
        }
        Show(menu!);
    }

    private void Show(Node menu)
    {
        if (_behind is not { } behind)
        {
            if (_label != null && GodotObject.IsInstanceValid(_label)) _label.Visible = false;
            return;
        }
        if (_label == null || !GodotObject.IsInstanceValid(_label) || _warning == null || !GodotObject.IsInstanceValid(_warning))
        {
            // The game's label can appear a few frames after the menu: look again every half second.
            if (_searchCooldown-- > 0) return;
            _searchCooldown = 30;
            _warning = ModdedWarning(menu);
            if (_warning == null)
            {
                if (!_loggedMissing) ModEntry.Log.Warn("update notice: the main menu's MODDED label wasn't found yet");
                _loggedMissing = true;
                return;
            }
            var size = _warning.GetThemeFontSize("font_size");
            _label = UiKit.Label("", size > 0 ? size : 18, Behind);
            _label.MouseFilter = Control.MouseFilterEnum.Stop;
            _label.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            _label.TooltipText = "Open the Spire QoL releases on GitHub";
            _label.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) OS.ShellOpen(GitHubReleases.PageUrl(Repo));
            };
            _warning.AddChild(_label);
            _logged = false;
        }
        _label.Visible = true;
        _label.Text = behind > 0 ? $"Spire QoL: {behind} version{(behind == 1 ? "" : "s")} behind (new version on GitHub)" : "Spire QoL up to date!";
        _label.AddThemeColorOverride("font_color", behind > 0 ? Behind : UpToDate);

        // Just above the game's label, on the same side of the screen (it sits in a corner; beside it
        // could run off the edge).
        var min = _label.GetMinimumSize();
        _label.Size = min;
        var screen = _warning.GetViewportRect().Size;
        var rect = _warning.GetGlobalRect();
        var rightSide = rect.GetCenter().X > screen.X / 2;
        _label.Position = new Vector2(rightSide ? _warning.Size.X - min.X : 0, -min.Y - 2);
        if (!_logged)
        {
            _logged = true;
            ModEntry.Log.Info($"update notice: shown at {_label.GetGlobalRect()} above {_warning.GetPath()} at {rect} (screen {screen})");
        }
    }

    /// <summary>The game's "MODDED (n)" label in the main menu's info corner (NDebugInfoLabelManager).</summary>
    private static Control? ModdedWarning(Node menu) => Find(menu) ?? (NGame.Instance is { } g ? Find(g) : null);

    // The visible one (the game keeps another for in-run screens).
    private static Control? Find(Node n)
    {
        if (n is NDebugInfoLabelManager && n.GetNodeOrNull<Control>("%ModdedWarning") is { } w && w.IsVisibleInTree()) return w;
        foreach (var c in n.GetChildren())
            if (Find(c) is { } found) return found;
        return null;
    }
}
