using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Menu;

/// <summary>
/// The first-run setup (decision #147): on the first launch (QolSettings.SetupDone), over the main menu, a short tour in steps (tabs
/// across the top): a starting point among three presets, then each page's core switches with what they do, then the hotkeys and
/// error reports. Every change applies at once, as in the settings menu, and closing it in any way marks it done; the settings
/// menu's General page runs it again. Only the features and options a player meets first: per-relic switches, tab kinds and widget
/// places stay in the settings menu. In dev mode, one button sets up the defaults and closes.
///
/// Plain Godot controls on our own CanvasLayer above the game (and above the settings menu), the game's hotkeys blocked while it's
/// open (NHotkeyManager.AddBlockingScreen) and Esc closing it. Nothing is patched.
/// </summary>
public sealed class SetupScreen
{
    /// <summary>The options a preset sets; the card tab kinds stay on in every preset (the settings menu turns them off).</summary>
    internal sealed record PresetOptions(bool Chances, bool DeathChance, bool IntentOdds, bool ProcNumbers, bool ProcIndirect, bool EventOdds, bool EventInspect);

    /// <summary>A starting point: its name, what it's for, the features it turns on (the rest of PresetFeatures off), its options.</summary>
    internal sealed record Preset(string Name, string Summary, IReadOnlySet<string> Features, PresetOptions Options);

    /// <summary>The features a preset decides: what shows in fights and outside them (the checks and the update notice aren't display).</summary>
    internal static IReadOnlyList<string> PresetFeatures { get; } =
        SettingsMenu.FeatureRows.Where(featureRow => featureRow.Where != SettingsMenu.FeaturePage.General).Select(featureRow => featureRow.Id).ToList();

    private static readonly HashSet<string> LightFeatures = new()
    {
        "incoming-damage", "teammate-incoming-damage", "death-skull", "card-indicators", "relic-counters", "exploration-progress", "rest-preview",
    };

    private static readonly QolSettings Defaults = new();

    internal static readonly Preset[] Presets =
    {
        new("Light", "What your turn leads to, and little else: the damage you'll take, which enemies die before they act, your counters. Only what's certain: no chances.",
            LightFeatures,
            new PresetOptions(Chances: false, DeathChance: true, IntentOdds: false, ProcNumbers: true, ProcIndirect: false, EventOdds: false, EventInspect: false)),
        new("Standard", "Light, plus a tab on each card for what playing it sets off, enemies' intents in full, and reminders on rewards, events and shops. Death chances, not every chance.",
            new HashSet<string>(LightFeatures) { "proc-indicators", "intent-details", "intent-forecast", "shuffle-warning", "potion-tabs", "run-reminders" },
            new PresetOptions(Chances: true, DeathChance: true, IntentOdds: false, ProcNumbers: true, ProcIndirect: false, EventOdds: false, EventInspect: true)),
        new("Everything", "Every feature and every chance: what each card sets off, even through what it draws or exhausts, and the odds of each enemy move. The mod's defaults.",
            PresetFeatures.ToHashSet(),
            new PresetOptions(Defaults.Chances, Defaults.DeathChance, Defaults.IntentOdds, Defaults.ProcNumbers, Defaults.ProcIndirect, Defaults.EventOdds, Defaults.EventInspect)),
    };

    private enum Step { Start, Fights, CardTabs, Outside, Finish }

    private static readonly (Step Step, string Title)[] Steps =
    {
        (Step.Start, "1  Start"), (Step.Fights, "2  In fights"), (Step.CardTabs, "3  Card tabs"), (Step.Outside, "4  Outside fights"), (Step.Finish, "5  Finish"),
    };

    private readonly QolContext _ctx;
    private readonly FeatureHost _features;
    private readonly OverlayHost _host;

    private CanvasLayer? _layer;
    private Control? _root;
    private PanelContainer? _panel;
    private VBoxContainer? _body;
    private ScrollContainer? _scroll;
    private Button? _back, _next;
    private readonly Dictionary<Step, Button> _stepButtons = new();
    private Step _step = Step.Start;
    private Preset? _chosen;
    private bool _shownThisLaunch;
    private bool _blocking;
    private Action? _closeAction;

    public SetupScreen(QolContext ctx, FeatureHost features, OverlayHost host)
    {
        _ctx = ctx;
        _features = features;
        _host = host;
    }

    /// <summary>Whether the settings menu is open (the setup waits for it to close before showing itself).</summary>
    internal Func<bool>? SettingsOpen { get; set; }

    public bool IsOpen => _layer != null && GodotObject.IsInstanceValid(_layer) && _layer.Visible;

    private QolSettings Settings => _ctx.Settings;

    /// <summary>Every frame: shows itself once on the main menu until it's done; keeps its panel fitted while open.</summary>
    public void OnFrame()
    {
        if (IsOpen)
        {
            FitPanel();
            return;
        }
        if (_shownThisLaunch || Settings.SetupDone || SettingsOpen?.Invoke() == true || !OnMainMenu()) return;
        _shownThisLaunch = true;
        Open();
    }

    private static bool OnMainMenu() => NGame.Instance?.MainMenu is { } mainMenu && GodotObject.IsInstanceValid(mainMenu) && mainMenu.IsVisibleInTree();

    public void Open()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        if (_layer == null || !GodotObject.IsInstanceValid(_layer)) Build(tree);
        _step = Step.Start;
        _layer!.Visible = true;
        Rebuild();
        BlockGameInput(block: true);
    }

    /// <summary>Done (Finish, Skip, Esc): the settings stay as they are now, and it won't show itself again.</summary>
    private void Done()
    {
        Settings.SetupDone = true;
        Saved();
        if (_layer != null && GodotObject.IsInstanceValid(_layer)) _layer.Visible = false;
        BlockGameInput(block: false);
    }

    // ------------------------------------------------------------------ frame

    private void Build(SceneTree tree)
    {
        _layer = new CanvasLayer { Name = "SpireQoL_Setup", Layer = 101, Visible = false };
        tree.Root.AddChild(_layer);
        _root = new Control { Name = "Root", MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_root);
        var backdrop = new ColorRect { Color = new Color(0, 0, 0, 0.7f), MouseFilter = Control.MouseFilterEnum.Stop };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(backdrop);

        _panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        var style = UiKit.PanelStyle(new Color(0.06f, 0.06f, 0.085f, 0.97f), radius: 12, margin: 26);
        style.SetBorderWidthAll(2);
        style.BorderColor = SettingsMenu.Accent;
        _panel.AddThemeStyleboxOverride("panel", style);
        _root.AddChild(_panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        _panel.AddChild(box);

        var header = UiKit.Row(separation: 14);
        header.AddChild(SettingsMenu.Label("Spire QoL setup", size: 30, SettingsMenu.Accent));
        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        if (ModEntry.DevMode)
        {
            // Dev mode: the setup the mod is developed with (every default), and done.
            var dev = SettingsMenu.MakeButton("Dev setup", size: 20);
            dev.TooltipText = "Dev mode: every default (Everything), and close.";
            dev.Pressed += () => { Apply(Presets[^1]); Done(); };
            header.AddChild(dev);
        }
        var skip = SettingsMenu.MakeButton("Skip", size: 20);
        skip.TooltipText = "Keep the settings as they are now. Run this again from the settings menu's General page.";
        skip.Pressed += Done;
        header.AddChild(skip);
        box.AddChild(header);

        var steps = UiKit.Row(separation: 6);
        var group = new ButtonGroup();
        foreach (var (step, title) in Steps)
        {
            var button = SettingsMenu.MakeButton(title, size: 21);
            button.ToggleMode = true;
            button.ButtonGroup = group;
            button.Pressed += () => Go(step);
            _stepButtons[step] = button;
            steps.AddChild(button);
        }
        box.AddChild(steps);

        _scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 6);
        _scroll.AddChild(_body);
        box.AddChild(_scroll);

        var footer = UiKit.Row(separation: 14);
        _back = SettingsMenu.MakeButton("Back", size: 22);
        _back.Pressed += () => Go(_step - 1);
        footer.AddChild(_back);
        footer.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        footer.AddChild(SettingsMenu.Label("Changes apply right away.", size: 16, SettingsMenu.Muted));
        _next = SettingsMenu.MakeButton("Next", size: 22);
        _next.Pressed += () => { if (_step == Step.Finish) Done(); else Go(_step + 1); };
        footer.AddChild(_next);
        box.AddChild(footer);
        FitPanel();
    }

    private void FitPanel()
    {
        if (_panel == null) return;
        var viewport = _host.ViewportSize;
        var size = new Vector2(Math.Min(1280, viewport.X - 80), Math.Min(860, viewport.Y - 80));
        _panel.CustomMinimumSize = size;
        _panel.Size = size;
        _panel.Position = (viewport - size) / 2;
    }

    private void Go(Step step)
    {
        _step = (Step)Math.Clamp((int)step, (int)Step.Start, (int)Step.Finish);
        if (_scroll != null) _scroll.ScrollVertical = 0;
        Rebuild();
    }

    /// <summary>The page again (deferred when a switch just changed: it's mid-signal).</summary>
    private void Refresh() => Callable.From(Rebuild).CallDeferred();

    private void Rebuild()
    {
        if (_body == null) return;
        UiKit.Clear(_body);
        foreach (var (step, button) in _stepButtons) button.ButtonPressed = step == _step;
        _back!.Disabled = _step == Step.Start;
        _next!.Text = _step == Step.Finish ? "Finish" : "Next";
        try
        {
            switch (_step)
            {
                case Step.Start: BuildStart(); break;
                case Step.Fights: BuildFights(); break;
                case Step.CardTabs: BuildCardTabs(); break;
                case Step.Outside: BuildOutside(); break;
                case Step.Finish: BuildFinish(); break;
            }
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"setup: {_step} page: {ex}");
            _body.AddChild(SettingsMenu.Label("This page couldn't be built; see the game log.", size: 20, Colors.OrangeRed));
        }
    }

    // ------------------------------------------------------------------ pages

    private void BuildStart()
    {
        Heading("Welcome to Spire QoL");
        Paragraph("Spire QoL works out what the game already knows, so you don't have to: the damage you'll take when you end your turn, "
            + "which enemies die before they act, and what each card would set off if you played it now. Everything comes from the game's own rules "
            + "and what's on screen; it never looks ahead at the draw pile's order or a random roll. When a roll decides something, you see its chance or its range.");
        Paragraph("Pick a starting point. The next pages show what each part does, and you can switch any of it there or later in the settings menu "
            + $"({Settings.SettingsKey}).");
        var cards = UiKit.Row(separation: 16);
        foreach (var preset in Presets) cards.AddChild(PresetCard(preset));
        _body!.AddChild(cards);
    }

    private Control PresetCard(Preset preset)
    {
        var chosen = _chosen == preset;
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var style = UiKit.PanelStyle(new Color(0.1f, 0.1f, 0.13f, 0.95f), radius: 10, margin: 16);
        style.SetBorderWidthAll(chosen ? 3 : 1);
        style.BorderColor = chosen ? SettingsMenu.Accent : new Color(0.35f, 0.35f, 0.4f);
        panel.AddThemeStyleboxOverride("panel", style);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        panel.AddChild(box);
        box.AddChild(SettingsMenu.Label(preset.Name, size: 26, SettingsMenu.Accent));
        box.AddChild(Wrapped(preset.Summary, size: 18, SettingsMenu.Text, width: 330));
        var on = SettingsMenu.FeatureRows.Where(featureRow => preset.Features.Contains(featureRow.Id)).Select(featureRow => featureRow.Name);
        box.AddChild(Wrapped("On: " + string.Join(", ", on) + ".", size: 16, SettingsMenu.Muted, width: 330));
        box.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        var use = SettingsMenu.MakeButton(chosen ? "Chosen" : "Use this", size: 20);
        use.Pressed += () => { _chosen = preset; Apply(preset); Refresh(); };
        box.AddChild(use);
        return panel;
    }

    private void BuildFights()
    {
        Heading("In fights");
        Paragraph("During your turn, Spire QoL works out where each choice leads: the cards in your hand, your potions, ending your turn. "
            + "Hover a card and its tabs say what playing it right now sets off; Incoming Damage says what ending your turn now costs you.");
        foreach (var (id, _, _, where) in SettingsMenu.FeatureRows)
        {
            if (where != SettingsMenu.FeaturePage.Fights) continue;
            FeatureRow(id);
            switch (id)
            {
                case "proc-indicators":
                    Option("Amounts on proc tabs", "The block, damage, draws and energy a tab's source gives, on the tab. Off: icon only.",
                        Settings.ProcNumbers, enabled => Settings.ProcNumbers = enabled, Needs(id));
                    Option("Indirect procs", "Also what a card sets off through its other effects: the cards it exhausts, discards or draws.",
                        Settings.ProcIndirect, enabled => Settings.ProcIndirect = enabled, Needs(id));
                    break;
                case "death-skull":
                    Option("Death chances", "When a roll decides a death (a random Lightning target), its chance beside the skull.",
                        Settings.DeathChance, enabled => Settings.DeathChance = enabled, Settings.Chances ? Needs(id) : "Show chances");
                    break;
                case "intent-forecast":
                    Option("Random moves", "After the turns an enemy's rules make certain, each move a random pick can make, with its chance.",
                        Settings.IntentOdds, enabled => Settings.IntentOdds = enabled, Settings.Chances ? Needs(id) : "Show chances");
                    break;
            }
        }
        Heading("Chances");
        Option("Show chances", "Every chance the mod shows, when a roll decides an outcome. Off: only what's certain. A ? or a range stays: it marks what isn't settled.",
            Settings.Chances, enabled => { Settings.Chances = enabled; Refresh(); }, needs: null, indent: false);
    }

    private void BuildCardTabs()
    {
        Heading("Card tabs");
        Paragraph("A tab hangs off the top of a card, with the icon of what it's about: a relic, a power, a pile. Every tab answers one question: "
            + "does something happen the moment you play this card, right now?");
        Paragraph("A plain tab: playing it does something extra (a relic fires, it's played twice, your discard pile is shuffled in). "
            + "A crossed tab, with a red slash: playing it might be bad for you (the enemy gains block, you take Thorns damage, you give up a bonus). "
            + "It's a warning, not a verdict. An arrow head in a tab's corner: it happens only at some targets.");
        Paragraph("The settings menu's Guide page shows each kind, and its Relics, powers and Cards pages switch each source on its own.");
        Option("Card tabs", "Every tab on cards. Off hides them all.", Settings.CardTabs, enabled => { Settings.CardTabs = enabled; Refresh(); }, needs: null, indent: false);
        var needsTabs = Settings.CardTabs ? null : "Card tabs";
        Option("Plain tabs", "Something extra happens when you play it.", Settings.PlainTabs, enabled => Settings.PlainTabs = enabled, needsTabs);
        Option("Crossed tabs", "Warnings: playing it might be bad for you.", Settings.CrossedTabs, enabled => Settings.CrossedTabs = enabled, needsTabs);
        Option("Aim marker", "The arrow head on a tab that happens only at some targets.", Settings.AimMarker, enabled => Settings.AimMarker = enabled, needsTabs);
    }

    private void BuildOutside()
    {
        Heading("Outside fights");
        Paragraph("Between fights, your relics that change a choice show up on the choice: a card reward, an event option, a shop price, the map. "
            + "At rest sites, what resting would heal you to.");
        foreach (var (id, _, _, where) in SettingsMenu.FeatureRows)
        {
            if (where != SettingsMenu.FeaturePage.Outside) continue;
            FeatureRow(id);
            if (id != "run-reminders") continue;
            Option("Inspect event cards", "A magnifier beside an event option that gives a random card from a known pool: every card it can give, and your chance of one you'd like.",
                Settings.EventInspect, enabled => Settings.EventInspect = enabled, Needs(id));
            Option("Chance under event icons", "Under a relic's icon beside an event option, the chance one of the cards it cares about comes up.",
                Settings.EventOdds, enabled => Settings.EventOdds = enabled, Settings.Chances ? Needs(id) : "Show chances (In fights)");
        }
    }

    private void BuildFinish()
    {
        Heading("You're set");
        var on = PresetFeatures.Count(FeatureOn);
        Paragraph($"{on} of {PresetFeatures.Count} features are on. Change any of this in the settings menu ({Settings.SettingsKey}); "
            + "its General page runs this setup again.");
        Paragraph($"{Settings.UnlockLayoutKey} unlocks the layout: drag a widget to move it, scroll over it to resize it, right-click it to reset it. "
            + $"{Settings.HideAllKey} hides everything Spire QoL shows, and again shows it.");
        Heading("Error reports");
        Option("Send error reports", "When a prediction doesn't match what the game did, or something breaks, send it with the game state at the time "
            + "(nothing personal) so it can be fixed. Each play session's counts go with them.",
            Settings.ReportErrors, enabled =>
            {
                Settings.ReportErrors = enabled;
                if (Sts2Mods.Core.Diagnostics.ErrorReporter.Instance is { } errorReporter) errorReporter.Active = enabled;
            }, needs: null, indent: false);
    }

    // ------------------------------------------------------------------ changes

    private bool FeatureOn(string id) => !Settings.Features.TryGetValue(id, out var on) || on;

    private void SetFeature(string id, bool on)
    {
        Settings.Features[id] = on;
        if (on) _features.EnableLate(id);
    }

    /// <summary>A preset: its features on and the rest of PresetFeatures off, its options, every card tab kind on.</summary>
    private void Apply(Preset preset)
    {
        foreach (var id in PresetFeatures) SetFeature(id, preset.Features.Contains(id));
        var options = preset.Options;
        Settings.Chances = options.Chances;
        Settings.DeathChance = options.DeathChance;
        Settings.IntentOdds = options.IntentOdds;
        Settings.ProcNumbers = options.ProcNumbers;
        Settings.ProcIndirect = options.ProcIndirect;
        Settings.EventOdds = options.EventOdds;
        Settings.EventInspect = options.EventInspect;
        Settings.CardTabs = Settings.PlainTabs = Settings.CrossedTabs = Settings.AimMarker = true;
        Saved();
    }

    private void Saved()
    {
        QolSettings.Revision++;
        _ctx.SettingsFile.Save();
    }

    /// <summary>What a feature's option needs: null when the feature's on, else its name (the row is greyed out until it is).</summary>
    private string? Needs(string id) => FeatureOn(id) ? null : SettingsMenu.FeatureRows.First(featureRow => featureRow.Id == id).Name;

    // ------------------------------------------------------------------ rows

    private void Heading(string text)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top", _body!.GetChildCount() == 0 ? 0 : 14);
        margin.AddChild(SettingsMenu.Label(text, size: 26, SettingsMenu.Accent));
        _body.AddChild(margin);
    }

    private void Paragraph(string text) => _body!.AddChild(Wrapped(text, size: 19, SettingsMenu.Text, width: 0));

    private static Label Wrapped(string text, int size, Color color, float width)
    {
        var label = SettingsMenu.Label(text, size, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (width > 0) label.CustomMinimumSize = new Vector2(width, 0);
        return label;
    }

    private void FeatureRow(string id)
    {
        var (_, name, desc, _) = SettingsMenu.FeatureRows.First(featureRow => featureRow.Id == id);
        Row(name, desc, FeatureOn(id), on => { SetFeature(id, on); Saved(); Refresh(); }, enabled: true, indent: false);
    }

    /// <summary>An option, indented under what it changes; greyed out, saying what it needs, while that's off.</summary>
    private void Option(string name, string desc, bool value, Action<bool> set, string? needs, bool indent = true) =>
        Row(name, needs == null ? desc : $"{desc} Needs {needs} on.", value, on => { set(on); Saved(); }, needs == null, indent);

    private void Row(string name, string desc, bool value, Action<bool> set, bool enabled, bool indent)
    {
        var row = UiKit.Row(separation: 12);
        if (indent) row.AddChild(new Control { CustomMinimumSize = new Vector2(28, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        var (button, check) = SettingsMenu.Check(value, set, text: null, tooltip: null);
        button.Disabled = !enabled;
        if (!enabled) row.Modulate = new Color(1, 1, 1, 0.5f);
        row.AddChild(check);
        row.AddChild(SettingsMenu.Label(name, size: 20, SettingsMenu.Text, width: 250));
        row.AddChild(Wrapped(desc, size: 18, SettingsMenu.Muted, width: 0));
        _body!.AddChild(row);
    }

    // ------------------------------------------------------------------ input

    /// <summary>While open, the game's hotkeys are swallowed and Esc / back closes it (done), as in the settings menu.</summary>
    private void BlockGameInput(bool block)
    {
        var nHotkeyManager = NHotkeyManager.Instance;
        if (nHotkeyManager == null || _root == null || block == _blocking) return;
        try
        {
            _closeAction ??= Done;
            var closeKeys = new[] { MegaInput.cancel.ToString(), MegaInput.pauseAndBack.ToString(), MegaInput.back.ToString() };
            if (block)
            {
                nHotkeyManager.AddBlockingScreen(_root);
                foreach (var closeKey in closeKeys) SettingsMenu.PushHotkey(nHotkeyManager, closeKey, _closeAction);
            }
            else
            {
                foreach (var closeKey in closeKeys) nHotkeyManager.RemoveHotkeyPressedBinding(closeKey, _closeAction);
                nHotkeyManager.RemoveBlockingScreen(_root);
            }
            _blocking = block;
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"setup: couldn't {(block ? "block" : "unblock")} game hotkeys: {ex.Message}");
        }
    }
}
