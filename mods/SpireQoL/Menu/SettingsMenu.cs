using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Menu;

/// <summary>
/// The in-game settings menu: every feature, option, on-card tab and tracker row, with the
/// game's own icon for each relic and power. Opens with the settings hotkey (F10), from a
/// button on the Mods screen (when Spire QoL is selected) and from a button on the pause menu.
/// Every change applies at once and is saved.
///
/// Plain Godot controls on our own CanvasLayer above the game. While it's open the game's
/// hotkeys are blocked (NHotkeyManager.AddBlockingScreen, the same thing the game does for its
/// own popups) and Esc closes it. Nothing is patched.
/// </summary>
public sealed class SettingsMenu
{
    private static readonly Color Accent = new(1f, 0.55f, 0.1f);
    private static readonly Color Text = new(0.92f, 0.9f, 0.85f);
    private static readonly Color Muted = new(0.68f, 0.66f, 0.62f);
    private static readonly Color RowA = new(1, 1, 1, 0.035f);
    private static readonly Color RowB = new(1, 1, 1, 0f);

    private enum Page { General, CardTabs, Relics, YourPowers, EnemyPowers }

    private static readonly (Page Page, string Title)[] Pages =
    {
        (Page.General, "General"),
        (Page.CardTabs, "Card tabs"),
        (Page.Relics, "Relics"),
        (Page.YourPowers, "Your powers"),
        (Page.EnemyPowers, "Enemy powers"),
    };

    private static readonly (string Id, string Name, string Description)[] FeatureRows =
    {
        ("card-indicators", "Card Indicators", "Retain, Sly, replay and double-play tabs; Ethereal cards fade."),
        ("proc-indicators", "Proc Indicators", "A tab for every relic or power a card would set off (the Relics and powers pages)."),
        ("relic-counters", "Relic Counters", "The tray of counter relics and countdown powers next to your HP bar."),
        ("incoming-damage", "Incoming Damage", "What you'll take at end of turn, after block (and what Osty takes)."),
        ("shuffle-warning", "Shuffle Warning", "Discard pile tab on cards whose draw reshuffles your discard pile."),
        ("forecast-audit", "Forecast check", "Compares Incoming Damage with what really happened (logs, and error reports)."),
        ("proc-audit", "Proc check", "Compares proc tabs with what really happened (logs, and error reports)."),
    };

    private readonly QolContext _ctx;
    private readonly FeatureHost _features;
    private readonly OverlayHost _host;

    private CanvasLayer? _layer;
    private Control? _root;
    private VBoxContainer? _rows;
    private HBoxContainer? _columnHeader;
    private ScrollContainer? _scroll;
    private LineEdit? _search;
    private Button? _onlyHere;
    private readonly Dictionary<Page, Button> _pageButtons = new();
    private Page _page = Page.General;
    private Action? _closeAction;
    private bool _blocking;

    private Button? _modsButton;
    private Button? _pauseButton;
    private int _frame;

    private Dictionary<string, AbstractModel>? _models;

    public SettingsMenu(QolContext ctx, FeatureHost features, OverlayHost host)
    {
        _ctx = ctx;
        _features = features;
        _host = host;
    }

    public bool IsOpen => _layer != null && GodotObject.IsInstanceValid(_layer) && _layer.Visible;

    private QolSettings S => _ctx.Settings;

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        if (_layer == null || !GodotObject.IsInstanceValid(_layer)) Build(tree);
        _layer!.Visible = true;
        Rebuild();
        BlockGameInput(true);
    }

    public void Close()
    {
        if (_layer != null && GodotObject.IsInstanceValid(_layer)) _layer.Visible = false;
        BlockGameInput(false);
        _ctx.SettingsFile.Save();
    }

    /// <summary>Called every frame: keeps the entry buttons on the Mods screen and pause menu.</summary>
    public void OnFrame()
    {
        if (IsOpen && _root != null) FitPanel();
        if (++_frame % 10 != 0) return;
        _modsButton = KeepButton(_modsButton, ModsScreenAnchor());
        _pauseButton = KeepButton(_pauseButton, PauseMenuAnchor());
    }

    // ------------------------------------------------------------------ entry points

    /// <summary>The Mods screen's info panel, while Spire QoL is the selected mod.</summary>
    private static Control? ModsScreenAnchor()
    {
        if (NGame.Instance?.MainMenu?.SubmenuStack?.Peek() is not NModdingScreen screen || !screen.IsVisibleInTree()) return null;
        var info = screen.GetNodeOrNull<Control>("%ModInfoContainer");
        var title = info?.GetNodeOrNull<RichTextLabel>("ModTitle")?.Text ?? "";
        return title.Contains("Spire QoL") ? info : null;
    }

    private static Control? PauseMenuAnchor()
    {
        var capstone = NCapstoneContainer.Instance;
        if (capstone?.CurrentCapstoneScreen is not NCapstoneSubmenuStack stack) return null;
        return stack.Stack?.Peek() is NPauseMenu pause && pause.IsVisibleInTree() ? pause : null;
    }

    private Button? KeepButton(Button? button, Control? parent)
    {
        if (button != null && !GodotObject.IsInstanceValid(button)) button = null;
        if (parent == null)
        {
            if (button != null) button.Visible = false;
            return button;
        }
        if (button == null || button.GetParent() != parent)
        {
            button?.QueueFree();
            button = MakeButton("Spire QoL settings", 22);
            button.Pressed += Open;
            parent.AddChild(button);
            // Bottom-left corner of its parent, clear of the game's own buttons.
            button.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
            button.Position = new Vector2(24, parent.Size.Y - 70);
        }
        button.Visible = !IsOpen;
        button.Position = new Vector2(24, Math.Max(0, parent.Size.Y - button.Size.Y - 24));
        return button;
    }

    /// <summary>
    /// While open, every game hotkey is swallowed (the game's own blocking-screen mechanism) and
    /// Esc / back closes the menu.
    /// </summary>
    private void BlockGameInput(bool block)
    {
        var hk = NHotkeyManager.Instance;
        if (hk == null || _root == null || block == _blocking) return;
        try
        {
            _closeAction ??= Close;
            var closeKeys = new[] { MegaInput.cancel.ToString(), MegaInput.pauseAndBack.ToString(), MegaInput.back.ToString() };
            if (block)
            {
                hk.AddBlockingScreen(_root);
                foreach (var k in closeKeys) hk.PushHotkeyPressedBinding(k, _closeAction);
            }
            else
            {
                foreach (var k in closeKeys) hk.RemoveHotkeyPressedBinding(k, _closeAction);
                hk.RemoveBlockingScreen(_root);
            }
            _blocking = block;
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"settings menu: couldn't {(block ? "block" : "unblock")} game hotkeys: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ frame

    private PanelContainer? _panel;

    private void Build(SceneTree tree)
    {
        _layer = new CanvasLayer { Name = "SpireQoL_Settings", Layer = 100, Visible = false };
        tree.Root.AddChild(_layer);

        _root = new Control { Name = "Root", MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_root);

        var backdrop = new ColorRect { Color = new Color(0, 0, 0, 0.62f), MouseFilter = Control.MouseFilterEnum.Stop };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(backdrop);

        _panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        var style = UiKit.PanelStyle(new Color(0.06f, 0.06f, 0.085f, 0.97f), 12, 22);
        style.ContentMarginTop = 16;
        style.ContentMarginBottom = 14;
        style.SetBorderWidthAll(2);
        style.BorderColor = Accent;
        _panel.AddThemeStyleboxOverride("panel", style);
        _root.AddChild(_panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        _panel.AddChild(box);

        // Header: title, search, filter, close.
        var header = UiKit.Row(14);
        header.MouseFilter = Control.MouseFilterEnum.Pass;
        var title = Label("Spire QoL settings", 30, Accent);
        header.AddChild(title);
        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        _search = new LineEdit { PlaceholderText = "Search", CustomMinimumSize = new Vector2(260, 0), ClearButtonEnabled = true };
        _search.AddThemeFontSizeOverride("font_size", 20);
        _search.TextChanged += _ => Rebuild();
        header.AddChild(_search);
        var (only, onlyBox) = Check(false, _ => Rebuild(), "Only what's in this fight", "Show only relics and powers you or the enemies have right now (in combat).");
        _onlyHere = only;
        header.AddChild(onlyBox);
        var reset = MakeButton("Reset defaults", 20);
        reset.TooltipText = "Every feature, option, card tab and tracker row back to its default. Keeps hotkeys, widget positions and error reporting.";
        reset.CustomMinimumSize = new Vector2(230, 0); // fits "Click again to reset" without moving the header
        reset.Pressed += () => ConfirmReset(reset);
        header.AddChild(reset);
        var close = MakeButton("Close", 20);
        close.Pressed += Close;
        header.AddChild(close);
        box.AddChild(header);

        // Page tabs.
        var tabs = UiKit.Row(6);
        tabs.MouseFilter = Control.MouseFilterEnum.Pass;
        var group = new ButtonGroup();
        foreach (var (page, name) in Pages)
        {
            var b = MakeButton(name, 21);
            b.ToggleMode = true;
            b.ButtonGroup = group;
            b.ButtonPressed = page == _page;
            b.Pressed += () => { _page = page; _scroll!.ScrollVertical = 0; Rebuild(); };
            _pageButtons[page] = b;
            tabs.AddChild(b);
        }
        box.AddChild(tabs);

        _columnHeader = UiKit.Row(12);
        _columnHeader.MouseFilter = Control.MouseFilterEnum.Pass;
        box.AddChild(_columnHeader);

        _scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 0);
        _scroll.AddChild(_rows);
        box.AddChild(_scroll);

        var keys = $"{S.SettingsKey}: this menu · {S.UnlockLayoutKey}: move widgets · {S.HideAllKey}: hide everything · Esc: close. Changes apply right away.";
        box.AddChild(Label(keys, 16, Muted));
        FitPanel();
    }

    private void FitPanel()
    {
        if (_panel == null) return;
        var vp = _host.ViewportSize;
        var size = new Vector2(Math.Min(1500, vp.X - 80), vp.Y - 80);
        _panel.CustomMinimumSize = size;
        _panel.Size = size;
        _panel.Position = (vp - size) / 2;
    }

    // ------------------------------------------------------------------ pages

    private void Rebuild()
    {
        if (_rows == null || _columnHeader == null) return;
        UiKit.Clear(_rows);
        UiKit.Clear(_columnHeader);
        foreach (var (page, b) in _pageButtons) b.ButtonPressed = page == _page;
        _onlyHere!.Disabled = Sts2State.Me == null;
        _row = 0;
        try
        {
            switch (_page)
            {
                case Page.General: BuildGeneral(); break;
                case Page.CardTabs: BuildCardTabs(); break;
                case Page.Relics: BuildSources(SourceGroup.Relic); break;
                case Page.YourPowers: BuildSources(SourceGroup.YourPower); break;
                case Page.EnemyPowers: BuildSources(SourceGroup.EnemyPower); break;
            }
        }
        catch (Exception ex)
        {
            ModEntry.Log.Error($"settings menu page {_page} failed: {ex}");
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "settings-menu");
            _rows.AddChild(Label("This page couldn't be built; see the game log.", 20, Colors.OrangeRed));
        }
    }

    private void BuildGeneral()
    {
        Section("Features");
        foreach (var (id, name, desc) in FeatureRows)
        {
            if (!Matches(name, desc)) continue;
            var failed = _features.States.TryGetValue(id, out var st) && st == FeatureState.Failed;
            SimpleRow(name, failed ? desc + " (turned off after an error; restart the game)" : desc, FeatureOn(id), on => SetFeature(id, on), FeatureIcon(id));
        }

        Section("Options");
        Option("Amounts on proc tabs", "Show the block, damage or summon amount on proc tabs. Off: icon only.",
            S.ProcNumbers, v => S.ProcNumbers = v);
        Option("Indirect procs", "Count procs one step removed: cards that exhaust or discard other cards, draw, or create cards.",
            S.ProcIndirect, v => S.ProcIndirect = v);
        Option("Tracker flashes early", "Relic Counters rows also flash two steps before firing (not just one).",
            S.PulseOneStepEarly, v => S.PulseOneStepEarly = v);
        Option("Send error reports", "Send crashes and prediction mismatches, with the game state at the time (nothing personal), so they can be fixed.",
            S.ReportErrors, v =>
            {
                S.ReportErrors = v;
                if (Sts2Mods.Core.Diagnostics.ErrorReporter.Instance is { } r) r.Active = v;
            });
        Slider("Ethereal opacity", "How see-through Ethereal cards are (1 = not at all).", S.EtherealOpacity, 0.25, 1, 0.05,
            v => S.EtherealOpacity = (float)v, v => $"{v:0.00}");
        Slider("Tracker flash speed", "Flashes per second on Relic Counters rows about to fire.", S.FlashHz, 0.5, 6, 0.5,
            v => S.FlashHz = (float)v, v => $"{v:0.0}/s");
    }

    private void BuildCardTabs()
    {
        Section("Card tabs");
        foreach (var t in SettingsCatalog.CardTabs)
        {
            if (!Matches(t.Name, t.Description, t.Key)) continue;
            SimpleRow(t.Name, t.Description, S.TabOwn(t.Key), on => SetTab(t.Key, on), TabPreview(t.Key));
        }
        Section("Whole families");
        foreach (var t in SettingsCatalog.TabGroups)
        {
            if (!Matches(t.Name, t.Description, t.Key)) continue;
            var spacer = new Control { CustomMinimumSize = new Vector2(IconSize, IconSize), MouseFilter = Control.MouseFilterEnum.Ignore };
            SimpleRow(t.Name, t.Description, S.TabOwn(t.Key), on => SetTab(t.Key, on), spacer);
        }

        // Tab kinds seen in play that the catalog doesn't know (a newer rule): still switchable.
        var other = S.Tabs.Keys.Where(k => !k.EndsWith(":*") && !SettingsCatalog.IsKnownTabKey(k)).OrderBy(k => k).ToList();
        if (other.Count > 0)
        {
            Section("Other");
            foreach (var key in other)
            {
                if (!Matches(key)) continue;
                SimpleRow(key, "A tab kind without a description yet.", S.TabOwn(key), on => SetTab(key, on));
            }
        }
    }

    private void BuildSources(SourceGroup group)
    {
        var entries = SettingsCatalog.Sources.Where(s => s.Group == group).ToList();
        var here = _onlyHere!.ButtonPressed ? PresentTypes() : null;
        var shown = entries
            .Where(e => here == null || here.Contains(e.Type))
            .Select(e => (Entry: e, Name: DisplayName(e.Type)))
            .Where(x => Matches(x.Name, x.Entry.Type, x.Entry.Tag, x.Entry.Tracker))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var anyTracker = entries.Any(e => e.Tracker != null);

        // Column headers with all-on / all-off for what's listed.
        _columnHeader!.AddChild(new Control { CustomMinimumSize = new Vector2(IconSize + NameWidth + 12, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        var tagHead = UiKit.Row(8);
        tagHead.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        tagHead.AddChild(Label("On cards", 20, Accent));
        AllButtons(tagHead, on =>
        {
            foreach (var (e, _) in shown) if (e.TagKey != null) S.Tabs[e.TagKey] = on;
        });
        var groupKeys = shown.Where(x => x.Entry.TagKey != null).Select(x => x.Entry.TagPrefix + "*").Distinct();
        var groupOff = groupKeys.Where(k => !S.TabOwn(k)).ToList();
        if (groupOff.Count > 0) tagHead.AddChild(Label("(a whole family is off on the Card tabs page)", 16, Colors.OrangeRed));
        _columnHeader.AddChild(tagHead);
        if (anyTracker)
        {
            var trackHead = UiKit.Row(8);
            trackHead.CustomMinimumSize = new Vector2(TrackerWidth, 0);
            trackHead.AddChild(Label("Tracker", 20, Accent));
            AllButtons(trackHead, on =>
            {
                foreach (var (e, _) in shown) if (e.Tracker != null) S.Relics[e.Type] = on;
            });
            _columnHeader.AddChild(trackHead);
        }

        if (shown.Count == 0)
        {
            _rows!.AddChild(Label(here != null ? "Nothing from this page is in this fight." : "No matches.", 20, Muted));
            return;
        }

        foreach (var (e, name) in shown)
        {
            var row = RowBox();
            // The icon as the tab shows it: crossed tabs get the same red slash as on cards.
            var crossed = e.Tag?.StartsWith(SettingsCatalog.CrossedPrefix) == true;
            var icon = UiKit.Icon(Icon(e.Type), IconSize);
            icon.MouseFilter = Control.MouseFilterEnum.Pass;
            icon.TooltipText = GameDescription(e.Type);
            if (crossed) AddSlash(icon);
            row.AddChild(icon);
            var nameLabel = Label(name, 20, Text, NameWidth);
            nameLabel.MouseFilter = Control.MouseFilterEnum.Pass;
            nameLabel.TooltipText = GameDescription(e.Type);
            row.AddChild(nameLabel);

            var tagCell = UiKit.Row(10);
            tagCell.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            if (e.TagKey is { } key)
            {
                var text = crossed ? "Crossed. " + Capitalize(e.Tag![SettingsCatalog.CrossedPrefix.Length..]) : e.Tag!;
                var (_, c) = Check(S.TabOwn(key), on => SetTab(key, on), text, null, expand: true);
                tagCell.AddChild(c);
            }
            else
            {
                tagCell.AddChild(Label("no card tab", 17, Muted));
            }
            row.AddChild(tagCell);

            if (anyTracker)
            {
                var trackCell = UiKit.Row(10);
                trackCell.CustomMinimumSize = new Vector2(TrackerWidth, 0);
                if (e.Tracker != null)
                {
                    var (_, c) = Check(S.TrackerEnabled(e.Type, e.TrackerDefault), on => SetTracker(e.Type, on), e.Tracker, null, expand: true);
                    trackCell.AddChild(c);
                }
                row.AddChild(trackCell);
            }
            AddRow(row);
        }
    }

    // ------------------------------------------------------------------ changes

    private bool FeatureOn(string id)
    {
        if (S.Features.TryGetValue(id, out var on)) return on;
        return true;
    }

    private void SetFeature(string id, bool on)
    {
        S.Features[id] = on;
        if (on) _features.EnableLate(id);
        Saved();
    }

    /// <summary>The first click arms the button; a second click within 4 s resets.</summary>
    private void ConfirmReset(Button button)
    {
        if (button.Text != ResetConfirm)
        {
            button.Text = ResetConfirm;
            button.GetTree().CreateTimer(4).Timeout += () =>
            {
                if (GodotObject.IsInstanceValid(button)) button.Text = "Reset defaults";
            };
            return;
        }
        button.Text = "Reset defaults";
        ResetDefaults();
    }

    private const string ResetConfirm = "Click again to reset";

    /// <summary>
    /// Everything the menu shows back to its default. Hotkeys, widget positions and the error
    /// reporting choice are kept: they aren't display preferences, and reporting is the
    /// player's own consent.
    /// </summary>
    private void ResetDefaults()
    {
        var d = new QolSettings();
        S.Features.Clear();
        S.Tabs.Clear();
        S.Relics.Clear();
        S.ProcNumbers = d.ProcNumbers;
        S.ProcIndirect = d.ProcIndirect;
        S.PulseOneStepEarly = d.PulseOneStepEarly;
        S.EtherealOpacity = d.EtherealOpacity;
        S.FlashHz = d.FlashHz;
        foreach (var (id, _, _) in FeatureRows)
            if (FeatureOn(id)) _features.EnableLate(id);
        Saved();
        Rebuild();
    }

    private void SetTab(string key, bool on)
    {
        S.Tabs[key] = on;
        Saved();
    }

    private void SetTracker(string type, bool on)
    {
        S.Relics[type] = on;
        Saved();
    }

    private void Option(string name, string desc, bool value, Action<bool> set)
    {
        if (!Matches(name, desc)) return;
        SimpleRow(name, desc, value, v => { set(v); Saved(); }, IconSpacer());
    }

    private void Saved() => _ctx.SettingsFile.Save();

    // ------------------------------------------------------------------ rows

    private const float IconSize = 44;
    private const float NameWidth = 230;
    private const float TrackerWidth = 420;
    private int _row;

    private void Section(string title)
    {
        var l = Label(title, 24, Accent);
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_top", _rows!.GetChildCount() == 0 ? 2 : 18);
        m.AddThemeConstantOverride("margin_bottom", 6);
        m.AddChild(l);
        _rows.AddChild(m);
        _row = 0;
    }

    private HBoxContainer RowBox()
    {
        var row = UiKit.Row(12);
        row.MouseFilter = Control.MouseFilterEnum.Pass;
        row.CustomMinimumSize = new Vector2(0, IconSize + 8);
        return row;
    }

    private void AddRow(HBoxContainer row)
    {
        var bg = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        bg.AddThemeStyleboxOverride("panel", UiKit.PanelStyle(_row++ % 2 == 0 ? RowA : RowB, 4, 8));
        bg.AddChild(row);
        _rows!.AddChild(bg);
    }

    /// <summary>The icon a Card tabs row shows on cards (null = none to show here).</summary>
    private Control? TabPreview(string key)
    {
        Texture2D? tex = null;
        try
        {
            switch (key)
            {
                case "retain": tex = Cards.GameIcons.Enchantment<MegaCrit.Sts2.Core.Models.Enchantments.Steady>(); break;
                case "sly": tex = Cards.GameIcons.Power<MegaCrit.Sts2.Core.Models.Powers.MasterPlannerPower>(); break;
                case "shuffle": tex = Cards.GameIcons.DiscardPile(); break;
                case "replay":
                    var l = Label("2×", 22, Text);
                    l.CustomMinimumSize = new Vector2(IconSize, IconSize);
                    l.HorizontalAlignment = HorizontalAlignment.Center;
                    return l;
            }
        }
        catch
        {
            tex = null;
        }
        var icon = UiKit.Icon(Cards.GameIcons.Valid(tex) ? tex : null, IconSize);
        return icon;
    }

    /// <summary>The icon next to a feature on the General page (a spacer when it has none).</summary>
    private static Control FeatureIcon(string id)
    {
        Texture2D? tex = null;
        try
        {
            if (id == "shuffle-warning") tex = Cards.GameIcons.DiscardPile();
        }
        catch
        {
            tex = null;
        }
        return Cards.GameIcons.Valid(tex)
            ? UiKit.Icon(tex, IconSize)
            : IconSpacer();
    }

    private static Control IconSpacer() =>
        new Control { CustomMinimumSize = new Vector2(IconSize, IconSize), MouseFilter = Control.MouseFilterEnum.Ignore };

    private static void AddSlash(Control icon)
    {
        var slash = new Line2D { Width = 5, DefaultColor = new Color(0.9f, 0.12f, 0.1f, 0.95f) };
        slash.AddPoint(new Vector2(IconSize * 0.12f, IconSize * 0.88f));
        slash.AddPoint(new Vector2(IconSize * 0.88f, IconSize * 0.12f));
        icon.AddChild(slash);
    }

    private void SimpleRow(string name, string desc, bool value, Action<bool> set, Control? icon = null)
    {
        var row = RowBox();
        var (_, check) = Check(value, set, null, null);
        row.AddChild(check);
        if (icon != null) row.AddChild(icon);
        row.AddChild(Label(name, 20, Text, NameWidth));
        var d = Label(desc, 18, Muted);
        d.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        d.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(d);
        AddRow(row);
    }

    private void Slider(string name, string desc, double value, double min, double max, double step, Action<double> set, Func<double, string> fmt)
    {
        if (!Matches(name, desc)) return;
        var row = RowBox();
        row.AddChild(new Control { CustomMinimumSize = new Vector2(CheckSize, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        row.AddChild(IconSpacer());
        row.AddChild(Label(name, 20, Text, NameWidth));
        var valueLabel = Label(fmt(value), 20, Accent, 70);
        var slider = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, Value = value,
            CustomMinimumSize = new Vector2(260, 30),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            FocusMode = Control.FocusModeEnum.None,
        };
        slider.ValueChanged += v => { set(v); valueLabel.Text = fmt(v); };
        slider.DragEnded += _ => Saved();
        row.AddChild(slider);
        row.AddChild(valueLabel);
        var d = Label(desc, 18, Muted);
        d.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        d.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(d);
        AddRow(row);
    }

    private void AllButtons(HBoxContainer parent, Action<bool> setAll)
    {
        foreach (var (text, on) in new[] { ("All on", true), ("All off", false) })
        {
            var b = MakeButton(text, 15);
            b.Pressed += () => { setAll(on); Saved(); Rebuild(); };
            parent.AddChild(b);
        }
    }

    // ------------------------------------------------------------------ controls

    private const float CheckSize = 30;

    /// <summary>
    /// A checkbox drawn with our own boxes (filled orange = on), so it doesn't depend on the
    /// game theme's checkbox art. With text, the text is part of the click target.
    /// </summary>
    private static (Button Button, Control Box) Check(bool value, Action<bool> changed, string? text, string? tooltip, bool expand = false)
    {
        var b = new Button
        {
            ToggleMode = true,
            ButtonPressed = value,
            CustomMinimumSize = new Vector2(CheckSize, CheckSize),
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            TooltipText = tooltip ?? "",
        };
        StyleBoxFlat Box(Color bg, Color border)
        {
            var sb = UiKit.PanelStyle(bg, 5, 0);
            sb.SetBorderWidthAll(2);
            sb.BorderColor = border;
            return sb;
        }
        var off = Box(new Color(0.1f, 0.1f, 0.13f), new Color(0.55f, 0.55f, 0.6f));
        var offHover = Box(new Color(0.16f, 0.16f, 0.2f), Accent);
        var on = Box(Accent, Accent.Lightened(0.3f));
        var onHover = Box(Accent.Lightened(0.15f), Colors.White);
        b.AddThemeStyleboxOverride("normal", off);
        b.AddThemeStyleboxOverride("hover", offHover);
        b.AddThemeStyleboxOverride("pressed", on);
        b.AddThemeStyleboxOverride("hover_pressed", onHover);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.AddThemeStyleboxOverride("disabled", Box(new Color(0.1f, 0.1f, 0.1f, 0.5f), new Color(0.3f, 0.3f, 0.3f)));
        b.Toggled += on2 => changed(on2);

        if (text == null) return (b, b);

        var row = UiKit.Row(10);
        row.MouseFilter = Control.MouseFilterEnum.Pass;
        if (expand) row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(b);
        var l = Label(text, 18, Muted);
        l.MouseFilter = Control.MouseFilterEnum.Stop;
        l.TooltipText = tooltip ?? "";
        if (expand)
        {
            l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }
        l.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } && !b.Disabled) b.ButtonPressed = !b.ButtonPressed;
        };
        row.AddChild(l);
        return (b, row);
    }

    private static Button MakeButton(string text, int size)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        b.AddThemeFontSizeOverride("font_size", size);
        var normal = UiKit.PanelStyle(new Color(0.13f, 0.13f, 0.17f), 6, 14);
        normal.SetBorderWidthAll(1);
        normal.BorderColor = new Color(0.4f, 0.4f, 0.45f);
        var hover = UiKit.PanelStyle(new Color(0.2f, 0.2f, 0.26f), 6, 14);
        hover.SetBorderWidthAll(1);
        hover.BorderColor = Accent;
        var pressed = UiKit.PanelStyle(new Color(0.32f, 0.18f, 0.05f), 6, 14);
        pressed.SetBorderWidthAll(2);
        pressed.BorderColor = Accent;
        foreach (var sb in new[] { normal, hover, pressed }) { sb.ContentMarginTop = 6; sb.ContentMarginBottom = 6; }
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", pressed);
        b.AddThemeStyleboxOverride("hover_pressed", pressed);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.AddThemeColorOverride("font_color", Text);
        b.AddThemeColorOverride("font_hover_color", Colors.White);
        b.AddThemeColorOverride("font_pressed_color", Accent.Lightened(0.4f));
        b.AddThemeColorOverride("font_hover_pressed_color", Colors.White);
        return b;
    }

    private static Label Label(string text, int size, Color color, float width = 0)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        if (width > 0)
        {
            l.CustomMinimumSize = new Vector2(width, 0);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }
        return l;
    }

    // ------------------------------------------------------------------ data

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private bool Matches(params string?[] fields)
    {
        var q = _search?.Text?.Trim();
        if (string.IsNullOrEmpty(q)) return true;
        return fields.Any(f => f != null && f.Contains(q, StringComparison.CurrentCultureIgnoreCase));
    }

    private static HashSet<string> PresentTypes()
    {
        var set = new HashSet<string>();
        var me = Sts2State.Me;
        var cs = Sts2State.Combat;
        if (me == null || cs == null) return set;
        foreach (var r in me.Relics) set.Add(r.GetType().Name);
        foreach (var p in me.Creature.Powers) set.Add(p.GetType().Name);
        foreach (var e in cs.Enemies) foreach (var p in e.Powers) set.Add(p.GetType().Name);
        return set;
    }

    private AbstractModel? Model(string type)
    {
        if (_models == null)
        {
            _models = new Dictionary<string, AbstractModel>();
            try { foreach (var r in ModelDb.AllRelics) _models.TryAdd(r.GetType().Name, r); } catch (Exception ex) { ModEntry.Log.Warn($"settings menu: relic list: {ex.Message}"); }
            try { foreach (var p in ModelDb.AllPowers) _models.TryAdd(p.GetType().Name, p); } catch (Exception ex) { ModEntry.Log.Warn($"settings menu: power list: {ex.Message}"); }
        }
        return _models.TryGetValue(type, out var m) ? m : null;
    }

    private static readonly Regex Tags = new(@"\[/?[^\]]*\]", RegexOptions.Compiled);

    private string DisplayName(string type)
    {
        try
        {
            var title = Model(type) switch
            {
                RelicModel r => r.Title.GetFormattedText(),
                PowerModel p => p.Title.GetFormattedText(),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(title)) return Tags.Replace(title, "");
        }
        catch
        {
            // fall back to the class name
        }
        var name = type.EndsWith("Power") ? type[..^5] : type;
        return Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");
    }

    private string GameDescription(string type)
    {
        try
        {
            var text = Model(type) switch
            {
                RelicModel r => r.DynamicDescription.GetFormattedText(),
                // Raw text: formatting a power's description without its owner's amount makes the
                // game log a localization error. Fill the placeholders by hand instead.
                PowerModel p => Placeholders(p.Description.GetRawText()),
                _ => null,
            };
            return text == null ? "" : Tags.Replace(text, "");
        }
        catch
        {
            return "";
        }
    }

    private static readonly Regex Plural = new(@"\{\w+:plural:([^|}]*)\|[^}]*\}", RegexOptions.Compiled);
    private static readonly Regex Placeholder = new(@"\{[^{}]*\}", RegexOptions.Compiled);

    /// <summary>"{Amount:plural:an extra time|{} extra times}" → "an extra time"; "{Amount}" → "X".</summary>
    private static string Placeholders(string raw)
    {
        var s = raw.Replace("{}", "X");
        s = Plural.Replace(s, "$1");
        return Placeholder.Replace(s, "X");
    }

    private Texture2D? Icon(string type)
    {
        try
        {
            var tex = Model(type) switch
            {
                RelicModel r => r.Icon,
                PowerModel p => p.Icon,
                _ => null,
            };
            return Cards.GameIcons.Valid(tex) ? tex : null;
        }
        catch
        {
            return null;
        }
    }
}
