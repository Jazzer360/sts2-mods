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
/// releases are on GitHub, or "Spire QoL up to date!" when GitHub says there are none. A test build (its
/// version isn't a release, or it was built with uncommitted changes) shows its version, commit and the
/// newest release instead. Checked once each time the main menu appears (off the main thread), never
/// during a run. Clicking it opens the releases page. To its right, the game's settings gear opens the Spire QoL settings menu
/// (shown even before GitHub answers). Spec: docs/FEATURES.md#update-notice.
/// </summary>
public sealed class UpdateNoticeFeature : IFeature, IFrameFeature
{
    internal const string Repo = "Jazzer360/sts2-mods";
    private const string TagPrefix = "spireqol-v";

    private static readonly Color Behind = new(0.62f, 0.85f, 0.62f);
    private static readonly Color UpToDate = new(0.75f, 0.75f, 0.72f, 0.8f);
    private static readonly Color TestBuild = new(0.95f, 0.7f, 0.45f);
    private static readonly Color Checking = new(0.75f, 0.75f, 0.72f, 0.55f);

    private bool _onMenu;
    private Task<GitHubReleases.Status?>? _check;
    private GitHubReleases.Status? _status; // null: GitHub hasn't answered (nothing shown)
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
            _check = Task.Run(() => GitHubReleases.Check(Repo, TagPrefix, current, ModEntry.Id));
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
                _status = n;
                ModEntry.Log.Info($"update notice: {Text(n)}");
            }
        }
        Show(menu!);
    }

    /// <summary>Opens the settings menu (set by ModEntry once the menu exists): the gear beside the notice.</summary>
    public Action? OpenSettings { get; set; }

    private TextureRect? _gear;
    private int _fontSize = 18;
    private const string GearPath = "res://images/packed/common_ui/top_bar/top_bar_settings.png";

    private void Show(Node menu)
    {
        if (!EnsureNodes(menu)) return;
        var label = _label!;
        var gear = _gear!;
        var warning = _warning!;

        // While GitHub hasn't answered (the first visit to the menu), say so, so the notice doesn't seem to be missing.
        label.Visible = _status != null || _check != null;
        if (_status is { } status)
        {
            label.Text = Text(status);
            label.AddThemeColorOverride("font_color", IsTestBuild(status) ? TestBuild : status.Newer > 0 ? Behind : UpToDate);
        }
        else if (_check != null)
        {
            label.Text = "Checking for Spire QoL updates…";
            label.AddThemeColorOverride("font_color", Checking);
        }
        gear.Visible = OpenSettings != null && Cards.GameIcons.Valid(gear.Texture);
        gear.Modulate = gear.GetGlobalRect().HasPoint(gear.GetGlobalMousePosition()) ? Colors.White : new Color(0.85f, 0.85f, 0.85f, 0.85f);

        // Just above the game's label, on the same side of the screen (it sits in a corner; beside it could run off the
        // edge), with the gear to the right of the notice (or alone while GitHub hasn't answered).
        var min = label.Visible ? label.GetMinimumSize() : Vector2.Zero;
        label.Size = min;
        var gearSize = gear.Visible ? Math.Max(24f, _fontSize * 1.6f) : 0f;
        gear.Size = new Vector2(gearSize, gearSize);
        const float gap = 8;
        var height = Math.Max(min.Y, gearSize);
        var width = min.X + (label.Visible && gear.Visible ? gap : 0) + gearSize;
        var screen = warning.GetViewportRect().Size;
        var rect = warning.GetGlobalRect();
        var rightSide = rect.GetCenter().X > screen.X / 2;
        var x0 = rightSide ? warning.Size.X - width : 0;
        var top = -height - 2;
        label.Position = new Vector2(x0, top + (height - min.Y) / 2);
        gear.Position = new Vector2(x0 + width - gearSize, top + (height - gearSize) / 2);
        if (!_logged)
        {
            _logged = true;
            ModEntry.Log.Info($"update notice: shown at {label.GetGlobalRect()}, gear at {gear.GetGlobalRect()}, above {warning.GetPath()} at {rect} (screen {screen})");
        }
    }

    /// <summary>Finds the game's MODDED label and adds the notice and the gear to it (it can appear a few frames after the menu).</summary>
    private bool EnsureNodes(Node menu)
    {
        if (_label != null && GodotObject.IsInstanceValid(_label) && _gear != null && GodotObject.IsInstanceValid(_gear)
            && _warning != null && GodotObject.IsInstanceValid(_warning)) return true;
        if (_searchCooldown-- > 0) return false;
        _searchCooldown = 30;
        _warning = ModdedWarning(menu);
        if (_warning == null)
        {
            if (!_loggedMissing) ModEntry.Log.Warn("update notice: the main menu's MODDED label wasn't found yet");
            _loggedMissing = true;
            return false;
        }
        var size = _warning.GetThemeFontSize("font_size");
        _fontSize = size > 0 ? size : 18;
        _label = UiKit.Label("", _fontSize, Behind);
        _label.HorizontalAlignment = HorizontalAlignment.Right;
        _label.MouseFilter = Control.MouseFilterEnum.Stop;
        _label.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        _label.TooltipText = "Open Spire QoL's releases on GitHub (the newest one when you're behind)";
        _label.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) OS.ShellOpen(ReleasesLink(_status));
        };
        _warning.AddChild(_label);

        // The game's own gear (the in-run top bar's settings button art).
        _gear = UiKit.Icon(ResourceLoader.Exists(GearPath) ? ResourceLoader.Load<Texture2D>(GearPath) : null, 24);
        _gear.MouseFilter = Control.MouseFilterEnum.Stop;
        _gear.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        _gear.TooltipText = "Spire QoL settings";
        _gear.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) OpenSettings?.Invoke();
        };
        _warning.AddChild(_gear);
        _logged = false;
        return true;
    }

    private static readonly System.Reflection.Assembly Me = typeof(ModEntry).Assembly;

    /// <summary>Built with uncommitted changes (Directory.Build.targets stamps GitDirty).</summary>
    private static bool Dirty => Me.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
        .OfType<System.Reflection.AssemblyMetadataAttribute>().Any(a => a.Key == "GitDirty" && a.Value == "true");

    /// <summary>The newest Spire QoL release when you're behind (its download), else Spire QoL's releases (not the repo's other mods').</summary>
    internal static string ReleasesLink(GitHubReleases.Status? s) =>
        s is { Newer: > 0, Latest: { } latest } ? GitHubReleases.ReleaseUrl(Repo, TagPrefix, latest) : GitHubReleases.PageUrl(Repo, TagPrefix);

    private static bool IsTestBuild(GitHubReleases.Status s) => !s.Released || Dirty;

    private static string Text(GitHubReleases.Status s)
    {
        if (IsTestBuild(s))
        {
            // "0.31.16+a70db85…": the version, then the commit SourceLink stamps.
            var info = Me.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "?";
            var plus = info.IndexOf('+');
            var version = plus >= 0 ? info[..plus] : info;
            var commit = plus >= 0 ? info[(plus + 1)..] : "";
            if (commit.Length > 7) commit = commit[..7];
            var built = File.GetLastWriteTime(Me.Location).ToString("MMM d HH:mm");
            var lines = new List<string>
            {
                $"Spire QoL {version} test build ({(commit == "" ? "no commit" : commit)}{(Dirty ? ", uncommitted changes" : "")}, built {built})",
                $"version {version} {(s.Released ? "is already released" : "isn't released yet")}; newest release {s.Latest?.ToString() ?? "none"}{(s.Newer > 0 ? $", {s.Newer} newer" : "")}",
            };
            return string.Join('\n', lines);
        }
        return s.Newer > 0 ? $"Spire QoL: {s.Newer} version{(s.Newer == 1 ? "" : "s")} behind (new version on GitHub)" : "Spire QoL up to date!";
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
