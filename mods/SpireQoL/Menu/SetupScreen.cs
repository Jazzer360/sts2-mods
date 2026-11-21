using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Menu;

/// <summary>
/// The first-run setup (decision #147): on the first launch (QolSettings.SetupDone), over the main menu, a short tour in steps (tabs
/// across the top): two choices to start from (how much on screen, and chances), then each page's core switches with what they do,
/// then the hotkeys and error reports. Every change applies at once, as in the settings menu, and closing it in any way marks it done; the settings
/// menu's General page runs it again. Only the features and options a player meets first: per-relic switches, tab kinds and widget
/// places stay in the settings menu. In dev mode, one button sets up the defaults and closes.
///
/// Plain Godot controls on our own CanvasLayer above the game (and above the settings menu), the game's hotkeys blocked while it's
/// open (NHotkeyManager.AddBlockingScreen) and Esc closing it. Nothing is patched.
/// </summary>
public sealed class SetupScreen
{
    /// <summary>
    /// How much the mod puts on screen: the features it turns on (the rest of PresetFeatures off) and the options that add to what
    /// they show. Chances are the other choice (ChanceLevel), so each changes only its own settings.
    /// </summary>
    internal sealed record Density(string Name, string Summary, IReadOnlySet<string> Features, bool ProcIndirect, bool EventInspect);

    /// <summary>Whether the mod shows odds: every chance, death chances only, or none (only what's certain; ranges and ? stay).</summary>
    internal sealed record ChanceLevel(string Name, string Summary, bool Chances, bool DeathChance, bool IntentOdds, bool EventOdds, bool DrawChance);

    /// <summary>The features a density decides: what shows in fights and outside them (the checks and the update notice aren't display).</summary>
    internal static IReadOnlyList<string> PresetFeatures { get; } =
        SettingsMenu.FeatureRows.Where(featureRow => featureRow.Where != SettingsMenu.FeaturePage.General).Select(featureRow => featureRow.Id).ToList();

    private static readonly HashSet<string> MinimalFeatures = new() { "incoming-damage", "teammate-incoming-damage", "death-skull", "exploration-progress", "rest-preview" };

    private static readonly HashSet<string> ModerateFeatures = new(MinimalFeatures) { "card-indicators", "relic-counters", "proc-indicators", "intent-details", "run-reminders" };

    private static readonly QolSettings Defaults = new();

    internal static readonly Density[] Densities =
    {
        new("Minimal", "The numbers that matter most: what you'll take when you end your turn, which enemies die before they act, what resting heals. Nothing on your cards.",
            MinimalFeatures, ProcIndirect: false, EventInspect: false),
        new("Moderate", "Plus a tab on each card for what playing it sets off, your counter relics' tray, what each enemy's move does, and your relics' reminders on rewards, events and shops.",
            ModerateFeatures, ProcIndirect: false, EventInspect: true),
        new("Full", "Everything: also each enemy's next moves, a warning before a draw reshuffles your discard pile, tabs under your potions, and what a card sets off through the cards it draws or exhausts.",
            PresetFeatures.ToHashSet(), Defaults.ProcIndirect, Defaults.EventInspect),
    };

    internal static readonly ChanceLevel[] ChanceLevels =
    {
        new("None", "Only what's certain. When a roll decides something, you see a range or a ?, never a percentage.",
            Chances: false, DeathChance: true, IntentOdds: false, EventOdds: false, DrawChance: false),
        new("Deaths only", "The chance an enemy dies before it acts, or that you die, when a roll decides it. No other odds.",
            Chances: true, DeathChance: true, IntentOdds: false, EventOdds: false, DrawChance: false),
        new("All", "Every chance: deaths, each move an enemy's random pick can make, what a card's draw gives, the cards an event can offer.",
            Defaults.Chances, Defaults.DeathChance, Defaults.IntentOdds, Defaults.EventOdds, DrawChance: true),
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
            dev.TooltipText = "Dev mode: every default (Full, all chances), and close.";
            dev.Pressed += () => { Apply(Densities[^1]); Apply(ChanceLevels[^1]); Done(); };
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
            + "and what's on screen; it never looks ahead at the draw pile's order or a random roll.");
        Paragraph($"Two choices to start from. The next pages show what each part does, and you can switch any of it there or later in the settings menu ({Settings.SettingsKey}).");
        Heading("How much on screen?");
        var densities = UiKit.Row(separation: 16);
        foreach (var density in Densities)
        {
            var on = SettingsMenu.FeatureRows.Where(featureRow => density.Features.Contains(featureRow.Id)).Select(featureRow => featureRow.Name);
            densities.AddChild(Choice(density.Name, density.Summary, "On: " + string.Join(", ", on) + ".", Matches(density), () => Apply(density)));
        }
        _body!.AddChild(densities);
        Heading("Chances?");
        var chances = UiKit.Row(separation: 16);
        foreach (var level in ChanceLevels) chances.AddChild(Choice(level.Name, level.Summary, detail: null, Matches(level), () => Apply(level)));
        _body.AddChild(chances);
    }

    /// <summary>One answer to a choice: marked when the settings are what it sets; choosing it applies it at once.</summary>
    private Control Choice(string name, string summary, string? detail, bool chosen, Action choose)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var style = UiKit.PanelStyle(new Color(0.1f, 0.1f, 0.13f, 0.95f), radius: 10, margin: 16);
        style.SetBorderWidthAll(chosen ? 3 : 1);
        style.BorderColor = chosen ? SettingsMenu.Accent : new Color(0.35f, 0.35f, 0.4f);
        panel.AddThemeStyleboxOverride("panel", style);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        panel.AddChild(box);
        box.AddChild(SettingsMenu.Label(name, size: 26, SettingsMenu.Accent));
        box.AddChild(Wrapped(summary, size: 18, SettingsMenu.Text, width: 330));
        if (detail != null) box.AddChild(Wrapped(detail, size: 16, SettingsMenu.Muted, width: 330));
        box.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        var use = SettingsMenu.MakeButton(chosen ? "Chosen" : "Choose", size: 20);
        use.Pressed += () => { choose(); Refresh(); };
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
        var density = Densities.FirstOrDefault(Matches)?.Name ?? "your own mix";
        var chances = ChanceLevels.FirstOrDefault(Matches)?.Name.ToLowerInvariant() ?? "your own mix";
        Paragraph($"On screen: {density}. Chances: {chances}. {on} of {PresetFeatures.Count} features are on. Change any of this in the settings menu ({Settings.SettingsKey}); "
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

    /// <summary>A density: its features on and the rest of PresetFeatures off, and its options.</summary>
    private void Apply(Density density)
    {
        foreach (var id in PresetFeatures) SetFeature(id, density.Features.Contains(id));
        Settings.ProcIndirect = density.ProcIndirect;
        Settings.EventInspect = density.EventInspect;
        Saved();
    }

    private void Apply(ChanceLevel level)
    {
        Settings.Chances = level.Chances;
        Settings.DeathChance = level.DeathChance;
        Settings.IntentOdds = level.IntentOdds;
        Settings.EventOdds = level.EventOdds;
        Settings.Tabs[QolSettings.DrawChanceKey] = level.DrawChance;
        Saved();
    }

    private bool Matches(Density density) =>
        PresetFeatures.All(id => FeatureOn(id) == density.Features.Contains(id)) && Settings.ProcIndirect == density.ProcIndirect && Settings.EventInspect == density.EventInspect;

    private bool Matches(ChanceLevel level) =>
        Settings.Chances == level.Chances && (!level.Chances || (Settings.DeathChance == level.DeathChance && Settings.IntentOdds == level.IntentOdds
            && Settings.EventOdds == level.EventOdds && Settings.TabOwn(QolSettings.DrawChanceKey) == level.DrawChance));

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
