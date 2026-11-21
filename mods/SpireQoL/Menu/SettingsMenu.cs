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
using SpireQoL.Data;
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
public sealed partial class SettingsMenu
{
    internal static readonly Color Accent = new(1f, 0.55f, 0.1f);
    internal static readonly Color Text = new(0.92f, 0.9f, 0.85f);
    internal static readonly Color Muted = new(0.68f, 0.66f, 0.62f);
    private static readonly Color RowA = new(1, 1, 1, 0.035f);
    private static readonly Color RowB = new(1, 1, 1, 0f);

    // Pages (decision #66): what shows in fights (its features and options, then the tab kinds and every relic, power and card's
    // switches), what shows outside fights (its features and options, then every reminder's switches), and the rest.
    private enum Page { Setup, Fights, CardTabs, Relics, YourPowers, EnemyPowers, Cards, Outside, General, Guide, About }

    private static readonly (Page Page, string Title)[] Pages =
    {
        (Page.Setup, "Quick setup"),
        (Page.Fights, "In fights"),
        (Page.CardTabs, "Card tabs"),
        (Page.Relics, "Relics"),
        (Page.YourPowers, "Your powers"),
        (Page.EnemyPowers, "Enemy powers"),
        (Page.Cards, "Cards"),
        (Page.Outside, "Outside fights"),
        (Page.General, "General"),
        (Page.Guide, "Guide"),
        (Page.About, "About"),
    };

    /// <summary>The page a feature's switch is on.</summary>
    internal enum FeaturePage { Fights, Outside, General }

    internal static readonly (string Id, string Name, string Description, FeaturePage Where)[] FeatureRows =
    {
        ("card-indicators", "Card Indicators", "Retain, Sly, replay and double-play tabs; Ethereal cards fade.", FeaturePage.Fights),
        ("proc-indicators", "Proc Indicators", "A tab for every relic or power a card would set off (the Relics, powers and Cards pages).", FeaturePage.Fights),
        ("relic-counters", "Relic Counters", "The tray of counter relics and countdown powers next to your HP bar.", FeaturePage.Fights),
        ("incoming-damage", "Incoming Damage", "What you'll take at end of turn, after block (and what Osty takes).", FeaturePage.Fights),
        ("teammate-incoming-damage", "Teammate Incoming Damage", "Multiplayer: hovering a teammate shows what they'll take, after their block (from what you can see, not their hand).", FeaturePage.Fights),
        ("shuffle-warning", "Shuffle Warning", "Discard pile tab on cards whose draw reshuffles your discard pile.", FeaturePage.Fights),
        ("death-skull", "Death Skull", "Skull beside an enemy's HP when it dies before acting if you end your turn now (with the chance when a roll decides it).", FeaturePage.Fights),
        ("exploration-progress", "Exploration Progress", "A clock and progress bar under the End Turn button while predictions are still working through every outcome (rare: most fights finish at once). Until then, what's uncertain carries a ?.", FeaturePage.Fights),
        ("intent-details", "Intent Details", "On each enemy's intent during your turn: the block its move gains, as a number on its defend intent, and each buff or debuff it applies (icon and amount), in the corner of its buff or debuff intent.", FeaturePage.Fights),
        ("intent-forecast", "Intent Forecast", "Hovering an enemy shows its next moves after the intent on screen: every turn its rules make certain, then each move a random pick can make, with its chance.", FeaturePage.Fights),
        ("potion-tabs", "Potion Tabs", "Tabs under your potions for the relics that drinking one sets off (the Relics page).", FeaturePage.Fights),
        ("run-reminders", "Run Reminders", "Your relics that change a choice, on the choice: tabs on cards you're offered (card rewards, event choices, the shop), icons beside event options and shop prices, and on the map beside the legend's rows.", FeaturePage.Outside),
        ("rest-preview", "Rest Preview", "At rest sites, a sum beside your character: your HP, the rest heal and each relic that changes it, and the HP you end with (and how much is wasted). Before the boss, Pantograph and Bath Robes are lines too. In multiplayer, hover a teammate while Mend is on offer for theirs.", FeaturePage.Outside),
        ("update-notice", "Update Notice", "On the main menu, how many Spire QoL versions behind you are (checks GitHub once per visit), with a gear beside it that opens this menu.", FeaturePage.General),
        ("forecast-audit", "Forecast check", "Compares Incoming Damage with what really happened (logs, and error reports).", FeaturePage.General),
        ("proc-audit", "Proc check", "Compares proc tabs with what really happened (logs, and error reports).", FeaturePage.General),
        ("coverage-watch", "Coverage check", "Reports game mechanics the mod doesn't cover yet when they show up (logs, and error reports).", FeaturePage.General),
        ("session-summary", "Session summary", "Counts each play session's turns, checks and calculation times, and sends the totals with error reports when the session ends.", FeaturePage.General),
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
    private Page _page = Page.Fights;
    private readonly List<Cards.TinyCardIcon> _tinyIcons = new();
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


    private QolSettings Settings => _ctx.Settings;

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
        BlockGameInput(block: true);
    }

    public void Close()
    {
        if (_layer != null && GodotObject.IsInstanceValid(_layer)) _layer.Visible = false;
        BlockGameInput(block: false);
        // Quick setup shows itself at the first launch only (FirstLaunch); once the menu has been open, it's done.
        Settings.SetupDone = true;
        _ctx.SettingsFile.Save();
    }

    /// <summary>Called every frame: keeps the entry buttons on the Mods screen and pause menu.</summary>
    public void OnFrame()
    {
        if (IsOpen && _root != null) FitPanel();
        _tinyIcons.RemoveAll(tinyCardIcon => !GodotObject.IsInstanceValid(tinyCardIcon.Root));
        foreach (var tinyCardIcon in _tinyIcons) tinyCardIcon.Fit();
        if (IsOpen && _page is Page.Guide or Page.Setup) foreach (var tick in _guideTicks) tick(OverlayHost.Time, Settings.FlashHz);
        if (++_frame % 10 != 0) return;
        FirstLaunch();
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
            button = MakeButton("Spire QoL settings", size: 22);
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
        var nHotkeyManager = NHotkeyManager.Instance;
        if (nHotkeyManager == null || _root == null || block == _blocking) return;
        try
        {
            _closeAction ??= Close;
            var closeKeys = new[] { MegaInput.cancel.ToString(), MegaInput.pauseAndBack.ToString(), MegaInput.back.ToString() };
            if (block)
            {
                nHotkeyManager.AddBlockingScreen(_root);
                foreach (var closeKey in closeKeys) PushHotkey(nHotkeyManager, closeKey, _closeAction);
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
            ModEntry.Log.Warn($"settings menu: couldn't {(block ? "block" : "unblock")} game hotkeys: {ex.Message}");
        }
    }

    /// <summary>
    /// NHotkeyManager.PushHotkeyPressedBinding by reflection: v0.113.0 added an optional sequence parameter,
    /// and a direct call compiled against one game version is a missing method on the other (decision #32).
    /// </summary>
    internal static void PushHotkey(NHotkeyManager nHotkeyManager, string key, Action action)
    {
        var method = typeof(NHotkeyManager).GetMethods().FirstOrDefault(otherMethod => otherMethod.Name == nameof(NHotkeyManager.PushHotkeyPressedBinding)
            && otherMethod.GetParameters() is { Length: >= 2 } parameterInfos && parameterInfos[0].ParameterType == typeof(string) && parameterInfos[1].ParameterType == typeof(Action))
            ?? throw new MissingMethodException(nameof(NHotkeyManager), nameof(NHotkeyManager.PushHotkeyPressedBinding));
        var args = method.GetParameters().Select((parameterInfo, index) => index == 0 ? key : index == 1 ? action : parameterInfo.HasDefaultValue ? parameterInfo.DefaultValue : null).ToArray();
        method.Invoke(nHotkeyManager, args);
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
        var style = UiKit.PanelStyle(new Color(0.06f, 0.06f, 0.085f, 0.97f), radius: 12, margin: 22);
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
        var header = UiKit.Row(separation: 14);
        header.MouseFilter = Control.MouseFilterEnum.Pass;
        var title = Label("Spire QoL settings", size: 30, Accent);
        header.AddChild(title);
        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        _search = new LineEdit { PlaceholderText = "Search", CustomMinimumSize = new Vector2(260, 0), ClearButtonEnabled = true };
        _search.AddThemeFontSizeOverride("font_size", 20);
        _search.TextChanged += _ => Rebuild();
        header.AddChild(_search);
        var (only, onlyBox) = Check(value: false, _ => Rebuild(), "Only what I have", "Show only what you have right now: your relics, the cards in your deck and, in a fight, the powers on you and the enemies.");
        _onlyHere = only;
        header.AddChild(onlyBox);
        var reset = MakeButton("Reset defaults", size: 20);
        reset.TooltipText = "Every feature, option, card tab and tracker row back to its default. Keeps hotkeys, widget positions and error reporting.";
        reset.CustomMinimumSize = new Vector2(230, 0); // fits "Click again to reset" without moving the header
        reset.Pressed += () => ConfirmReset(reset);
        header.AddChild(reset);
        var close = MakeButton("Close", size: 20);
        close.Pressed += Close;
        header.AddChild(close);
        box.AddChild(header);

        // Page tabs.
        var tabs = UiKit.Row(separation: 6);
        tabs.MouseFilter = Control.MouseFilterEnum.Pass;
        var group = new ButtonGroup();
        foreach (var (page, name) in Pages)
        {
            var button = MakeButton(name, size: 21);
            button.ToggleMode = true;
            button.ButtonGroup = group;
            button.ButtonPressed = page == _page;
            button.Pressed += () => { _page = page; _scroll!.ScrollVertical = 0; Rebuild(); };
            _pageButtons[page] = button;
            tabs.AddChild(button);
        }
        box.AddChild(tabs);

        _columnHeader = UiKit.Row(separation: 12);
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

        var keys = $"{Settings.SettingsKey}: this menu · {Settings.UnlockLayoutKey}: move widgets · {Settings.HideAllKey}: hide everything · Esc: close. Changes apply right away.";
        box.AddChild(Label(keys, size: 16, Muted));
        FitPanel();
    }

    private void FitPanel()
    {
        if (_panel == null) return;
        var position = _host.ViewportSize;
        var size = new Vector2(Math.Min(1500, position.X - 80), position.Y - 80);
        _panel.CustomMinimumSize = size;
        _panel.Size = size;
        _panel.Position = (position - size) / 2;
    }

    // ------------------------------------------------------------------ pages

    private void Rebuild()
    {
        if (_rows == null || _columnHeader == null) return;
        UiKit.Clear(_rows);
        UiKit.Clear(_columnHeader);
        _guideTicks.Clear();
        foreach (var (page, button) in _pageButtons) button.ButtonPressed = page == _page;
        _onlyHere!.Disabled = Sts2State.Me == null;
        _row = 0;
        try
        {
            switch (_page)
            {
                case Page.Setup: BuildSetup(); break;
                case Page.Fights: BuildFights(); break;
                case Page.Outside: BuildOutside(); break;
                case Page.General: BuildGeneral(); break;
                case Page.CardTabs: BuildCardTabs(); break;
                case Page.Relics: BuildSources(SourceGroup.Relic); break;
                case Page.YourPowers: BuildSources(SourceGroup.YourPower); break;
                case Page.EnemyPowers: BuildSources(SourceGroup.EnemyPower); break;
                case Page.Cards: BuildSources(SourceGroup.Card); break;
                case Page.Guide: BuildGuide(); break;
                case Page.About: BuildAbout(); break;
            }
        }
        catch (Exception ex)
        {
            ModEntry.Log.Error($"settings menu page {_page} failed: {ex}");
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "settings-menu");
            _rows.AddChild(Label("This page couldn't be built; see the game log.", size: 20, Colors.OrangeRed));
        }
    }

    /// <summary>
    /// The feature switches on one page, each followed by its own options, indented under it (decision #122): every indented row
    /// sits under the switch it depends on.
    /// </summary>
    private void FeatureSwitches(FeaturePage where, Dictionary<string, Action>? options = null)
    {
        Section("Features");
        foreach (var (id, name, desc, page) in FeatureRows)
        {
            if (page != where) continue;
            if (Matches(name, desc))
            {
                var failed = _features.States.TryGetValue(id, out var featureState) && featureState == FeatureState.Failed;
                SimpleRow(name, failed ? desc + " (turned off after an error; restart the game)" : desc, FeatureOn(id), on => SetFeature(id, on), FeatureIcon(id));
            }
            if (options != null && options.TryGetValue(id, out var add)) add();
        }
    }

    /// <summary>In fights: its features, then each one's options under its name (the tabs and rows themselves are on the next pages).</summary>
    private void BuildFights()
    {
        FeatureSwitches(FeaturePage.Fights, new()
        {
            ["card-indicators"] = () => Slider("Ethereal opacity", "How see-through Ethereal cards are (1 = not at all).", Settings.EtherealOpacity, min: 0.25, max: 1, step: 0.05,
                value => Settings.EtherealOpacity = (float)value, value => $"{value:0.00}", Needs("card-indicators")),
            ["proc-indicators"] = () =>
            {
                Option("Amounts on proc tabs", "Show the amount on proc tabs: block gained, damage dealt, the amount summoned, cards drawn and energy gained. Off: icon only.",
                    Settings.ProcNumbers, enabled => Settings.ProcNumbers = enabled, Needs("proc-indicators"), icon: TextIcon("4"));
                Option("Indirect procs", "Also count procs set off through the card's other effects (exhausting or discarding other cards, drawing, creating cards) and everything those set off in turn.",
                    Settings.ProcIndirect, enabled => Settings.ProcIndirect = enabled, Needs("proc-indicators"));
            },
            ["relic-counters"] = () =>
            {
                Option("Tracker flashes early", "Relic Counters rows also flash two steps before firing (not just one).",
                    Settings.PulseOneStepEarly, enabled => Settings.PulseOneStepEarly = enabled, Needs("relic-counters"));
                Slider("Tracker flash speed", "Flashes per second on Relic Counters rows about to fire.", Settings.FlashHz, min: 0.5, max: 6, step: 0.5,
                    value => Settings.FlashHz = (float)value, value => $"{value:0.0}/s", Needs("relic-counters"));
            },
        });

        ChancesSection(fights: true);

        Section("Multiplayer");
        Option("Multiplayer predictions (experimental)", "Count teammates in predictions: their orbs and end-of-turn effects, their block and Beacon of Hope, and each enemy hit on every player. Still being checked against real co-op games.",
            Settings.MultiplayerPredictions, enabled => { Settings.MultiplayerPredictions = enabled; Sim.SimAdapter.IncludeTeammates = enabled; Refresh(); }, indent: false);
        Option("Count teammates' hands", "Multiplayer: predictions also use the cards in teammates' hands, which you can't see unless they tell you: Ethereal cards exhausting at end of turn (Feel No Pain block, Charon's Ashes and Forgotten Soul damage), Burn, Decay and Regret. Off: only their hand size counts.",
            Settings.TeammateHands, enabled => { Settings.TeammateHands = enabled; Combat.TurnEndModel.CountTeammateHands = enabled; },
            Settings.MultiplayerPredictions ? null : "Multiplayer predictions");
    }

    /// <summary>
    /// What an option needs on (decision #122): null when it's on, else its name, for the greyed-out row. An option shown under
    /// a feature needs that feature (any one of several, for an option several features show).
    /// </summary>
    private string? Needs(params string[] features)
        => features.Any(FeatureOn) ? null : string.Join(" or ", features.Select(id => FeatureRows.First(featureRow => featureRow.Id == id).Name));

    /// <summary>
    /// Chances (decision #122): one switch for every chance the mod shows, then each one under it, greyed out while it's off or
    /// while the feature that shows it is. The same switch on both pages that show chances.
    /// </summary>
    private void ChancesSection(bool fights)
    {
        Section("Chances");
        Option("Show chances", "Every chance the mod shows, when a roll decides an outcome. Off: only what's certain. A ? or a range on what's shown stays: it marks what isn't settled yet.",
            Settings.Chances, enabled => { Settings.Chances = enabled; Refresh(); }, indent: false, icon: TextIcon("50%"));
        string? Under(string? feature) => !Settings.Chances ? "Show chances" : feature;
        if (fights)
        {
            Option("Death chances", "On the Death Skull, on your skull on Incoming Damage, and on lethal tabs, when a roll decides a death (a random Lightning target). Off: certain deaths only (your skull still shows whenever you might die).",
                Settings.DeathChance, enabled => Settings.DeathChance = enabled, Under(Needs("death-skull", "incoming-damage", "proc-indicators")), icon: ArtIcon(Cards.GameIcons.Skull()));
            Option("Incoming Damage: most likely outcome", "When a roll decides how the enemy turn goes (a draw at the end of your turn, a random target), the sum shows the most likely way, with its chance; the skull still shows whenever you might die. Hover the sum for every outcome. Off: the worst case.",
                Settings.MostLikely, enabled => Settings.MostLikely = enabled, Under(Needs("incoming-damage")), icon: ArtIcon(Cards.GameIcons.Heart()));
            Option("Draw chance tab", "On Escape Plan, Pillage and Scrape: the chance of what their draw gives (the Card tabs page has the same switch).",
                Settings.TabOwn(QolSettings.DrawChanceKey), enabled => SetTab(QolSettings.DrawChanceKey, enabled), Under(Needs("proc-indicators")), icon: TextIcon("40%"));
            Option("Intent Forecast's random moves", "After the turns an enemy's rules make certain, the move a random pick can make, each with its chance. Off: the certain turns only.",
                Settings.IntentOdds, enabled => Settings.IntentOdds = enabled, Under(Needs("intent-forecast")), icon: ArtIcon(Cards.GameIcons.Intent("unknown")));
        }
        else
            Option("Chance under event icons", "Under the icon of a relic that cares about certain cards (an egg, Fresnel Lens, Strike Dummy...) beside an event option, the chance one of its cards comes up. Off: icon only.",
                Settings.EventOdds, enabled => Settings.EventOdds = enabled, Under(Needs("run-reminders")), icon: TextIcon("25%"));
    }

    /// <summary>The page again, for rows that depend on a switch just changed (deferred: the switch is mid-signal).</summary>
    private void Refresh() => Callable.From(Rebuild).CallDeferred();

    /// <summary>Outside fights: its features and options, then a switch per relic or card for each place its reminder shows.</summary>
    private void BuildOutside()
    {
        FeatureSwitches(FeaturePage.Outside, new()
        {
            ["run-reminders"] = () =>
            {
                // Each place: one switch for every relic's reminder there (the per-relic columns below are under it).
                foreach (var (place, name, desc) in ReminderPlaceRows)
                {
                    var key = SpireQoL.Data.ChoiceReminders.Key(place, "*");
                    Option(name, desc, Settings.TabOwn(key), enabled => SetTab(key, enabled), Needs("run-reminders"), icon: ArtIcon(ReminderPlaceArt(place)));
                }
                Option("Inspect event cards", "A magnifier beside an event option that gives cards at random from a known pool (a reward, a transform). Click it to see every card it can give, mark the ones you'd like, and get your chance of being offered one.",
                    Settings.EventInspect, enabled => Settings.EventInspect = enabled, Needs("run-reminders"), icon: ArtIcon(Features.RunRemindersFeature.InspectIcon()));
            },
        });

        ChancesSection(fights: false);

        var here = _onlyHere!.ButtonPressed ? PresentTypes() : null;
        var shown = SettingsCatalog.Sources.Where(sourceEntry => sourceEntry.OutsideFights)
            .Where(sourceEntry => here == null || here.Contains(sourceEntry.Type))
            .Select(sourceEntry => (Entry: sourceEntry, Name: DisplayName(sourceEntry.Type)))
            .Where(entryName => Matches(entryName.Name, entryName.Entry.Type, entryName.Entry.Reward, entryName.Entry.Event, entryName.Entry.Shop, entryName.Entry.Map))
            .OrderBy(entryName => entryName.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        Section("Reminders");
        if (shown.Count == 0)
        {
            _rows!.AddChild(Label(here != null ? "You have none of these." : "No matches.", size: 20, Muted));
            return;
        }
        ReminderRows(shown);
    }

    /// <summary>The rest: the main menu notice, the checks and error reports.</summary>
    private void BuildGeneral()
    {
        FeatureSwitches(FeaturePage.General);

        Section("Error reports");
        Option("Send error reports", "Send crashes and prediction mismatches, with the game state at the time, and a summary of counts per play session (nothing personal), so they can be fixed.",
            Settings.ReportErrors, enabled =>
            {
                Settings.ReportErrors = enabled;
                if (Sts2Mods.Core.Diagnostics.ErrorReporter.Instance is { } errorReporter) errorReporter.Active = enabled;
            }, indent: false);
    }

    /// <summary>Run Reminders' places: one switch for every relic's reminder there (decision #122; the group key of their switches).</summary>
    private static readonly (SpireQoL.Data.ReminderPlace Place, string Name, string Description)[] ReminderPlaceRows =
    {
        (SpireQoL.Data.ReminderPlace.Reward, "On card rewards", "Tabs on the cards you're offered after a fight."),
        (SpireQoL.Data.ReminderPlace.Event, "At events", "Icons beside event options, and tabs on the cards an event offers."),
        (SpireQoL.Data.ReminderPlace.Shop, "In shops", "Icons beside shop prices, and tabs on the cards for sale."),
        (SpireQoL.Data.ReminderPlace.Map, "On the map", "Icons beside the map legend's rows."),
    };

    private void BuildCardTabs()
    {
        // One switch for every tab on cards, then everything under it (decision #122): the kinds, each tab, the families.
        Section("Tabs on cards");
        Option("Tabs on cards", "Every tab the mod hangs on cards: in your hand, and on the cards you're offered outside fights. Off: none (the features' other parts stay).",
            Settings.CardTabs, enabled => { Settings.CardTabs = enabled; Refresh(); }, indent: false, icon: FeatureIcon("proc-indicators"));
        var tabs = Settings.CardTabs ? Needs("card-indicators", "proc-indicators", "shuffle-warning", "run-reminders") : "Tabs on cards";
        Option("Plain tabs", "Tabs that mean playing the card does something extra (a proc, a replay, a reshuffle, a chance). Off: warnings only. Potion tabs too.",
            Settings.PlainTabs, enabled => Settings.PlainTabs = enabled, tabs, icon: FeatureIcon("proc-indicators"));
        Option("Crossed tabs", "Tabs with a red slash: playing the card might be bad for you (it helps the enemy, hurts you, or gives up a relic's bonus). Potion tabs too.",
            Settings.CrossedTabs, enabled => Settings.CrossedTabs = enabled, tabs, icon: ArtIcon(Cards.GameIcons.Power<MegaCrit.Sts2.Core.Models.Powers.ThornsPower>(), slashed: true));
        Option("Aim marker", "The targeting arrow head in a tab's corner when it happens only at some targets. Off: the tab shows without it.",
            Settings.AimMarker, enabled => Settings.AimMarker = enabled, tabs, icon: ArtIcon(Cards.GameIcons.TargetArrow()));
        foreach (var tabEntry in SettingsCatalog.CardTabs)
        {
            if (!Matches(tabEntry.Name, tabEntry.Description, tabEntry.Key)) continue;
            SimpleRow(tabEntry.Name, NeedsText(tabEntry.Description, tabs), Settings.TabOwn(tabEntry.Key), on => SetTab(tabEntry.Key, on), TabPreview(tabEntry.Key), tabs == null, indent: true);
        }
        foreach (var tabEntry in SettingsCatalog.TabGroups)
        {
            if (!Matches(tabEntry.Name, tabEntry.Description, tabEntry.Key)) continue;
            var spacer = new Control { CustomMinimumSize = new Vector2(IconSize, IconSize), MouseFilter = Control.MouseFilterEnum.Ignore };
            SimpleRow(tabEntry.Name, NeedsText(tabEntry.Description, tabs), Settings.TabOwn(tabEntry.Key), on => SetTab(tabEntry.Key, on), spacer, tabs == null, indent: true);
        }

        // Tab kinds seen in play that the catalog doesn't know (a newer rule): still switchable.
        var other = Settings.Tabs.Keys.Where(key => !key.EndsWith(":*", StringComparison.Ordinal) && !key.StartsWith("reminder-", StringComparison.Ordinal) && !SettingsCatalog.IsKnownTabKey(key)).OrderBy(key => key).ToList();
        foreach (var key in other)
        {
            if (!Matches(key)) continue;
            SimpleRow(key, NeedsText("A tab kind without a description yet.", tabs), Settings.TabOwn(key), on => SetTab(key, on), IconSpacer(), tabs == null, indent: true);
        }
    }

    private void BuildSources(SourceGroup group)
    {
        var entries = SettingsCatalog.Sources.Where(sourceEntry => sourceEntry.Group == group && sourceEntry.InFights).ToList();
        var here = _onlyHere!.ButtonPressed ? PresentTypes() : null;
        var shown = entries
            .Where(sourceEntry => here == null || here.Contains(sourceEntry.Type))
            .Select(sourceEntry => (Entry: sourceEntry, Name: DisplayName(sourceEntry.Type)))
            .Where(entryName => Matches(entryName.Name, entryName.Entry.Type, entryName.Entry.Tag, entryName.Entry.Tracker, entryName.Entry.Reward, entryName.Entry.Event, entryName.Entry.Shop, entryName.Entry.Map))
            .OrderBy(entryName => entryName.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (shown.Count == 0)
        {
            _rows!.AddChild(Label(here != null ? "You have none of these right now." : "No matches.", size: 20, Muted));
            return;
        }
        // What they do outside fights (Run Reminders) is on the Outside fights page.
        FightRows(entries, shown, _columnHeader!);
    }

    /// <summary>On-card tabs and tracker rows, with their column headers in <paramref name="header"/>.</summary>
    private void FightRows(List<SourceEntry> entries, List<(SourceEntry Entry, string Name)> shown, HBoxContainer header)
    {
        var anyTracker = entries.Any(sourceEntry => sourceEntry.Tracker != null);

        // Column headers with all-on / all-off for what's listed.
        header.AddChild(new Control { CustomMinimumSize = new Vector2(IconSize + NameWidth + 12, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        var tagHead = UiKit.Row(separation: 8);
        tagHead.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        tagHead.AddChild(Label("On cards", size: 20, Accent));
        AllButtons(tagHead, on =>
        {
            foreach (var (sourceEntry, _) in shown) if (sourceEntry.TagKey != null) Settings.Tabs[sourceEntry.TagKey] = on;
        });
        var groupKeys = shown.Where(entryName => entryName.Entry.TagKey != null).Select(entryName => entryName.Entry.TagPrefix + "*").Distinct();
        var groupOff = groupKeys.Where(groupKey => !Settings.TabOwn(groupKey)).ToList();
        if (groupOff.Count > 0) tagHead.AddChild(Label("(a whole family is off on the Card tabs page)", size: 16, Colors.OrangeRed));
        header.AddChild(tagHead);
        if (anyTracker)
        {
            var trackHead = UiKit.Row(separation: 8);
            trackHead.CustomMinimumSize = new Vector2(TrackerWidth, 0);
            trackHead.AddChild(Label("Tracker", size: 20, Accent));
            AllButtons(trackHead, on =>
            {
                foreach (var (sourceEntry, _) in shown) if (sourceEntry.Tracker != null) Settings.Relics[sourceEntry.Type] = on;
            });
            header.AddChild(trackHead);
        }

        foreach (var (otherSourceEntry, name) in shown)
        {
            // The icon as the tab shows it: crossed tabs get the same red slash as on cards.
            var crossed = otherSourceEntry.Tag?.StartsWith(SettingsCatalog.CrossedPrefix, StringComparison.Ordinal) == true;
            var row = SourceRow(otherSourceEntry, name, crossed);

            var tagCell = UiKit.Row(separation: 10);
            tagCell.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            if (otherSourceEntry.TagKey is { } key)
            {
                var text = crossed ? "Crossed. " + Capitalize(otherSourceEntry.Tag![SettingsCatalog.CrossedPrefix.Length..]) : otherSourceEntry.Tag!;
                var (_, control) = Check(Settings.TabOwn(key), on => SetTab(key, on), text, tooltip: null, expand: true);
                tagCell.AddChild(control);
            }
            else
            {
                tagCell.AddChild(Label("no card tab", size: 17, Muted));
            }
            row.AddChild(tagCell);

            if (anyTracker)
            {
                var trackCell = UiKit.Row(separation: 10);
                trackCell.CustomMinimumSize = new Vector2(TrackerWidth, 0);
                if (otherSourceEntry.Tracker != null)
                {
                    var (_, control) = Check(Settings.TrackerEnabled(otherSourceEntry.Type, otherSourceEntry.TrackerDefault), on => SetTracker(otherSourceEntry.Type, on), otherSourceEntry.Tracker, tooltip: null, expand: true);
                    trackCell.AddChild(control);
                }
                row.AddChild(trackCell);
            }
            AddRow(row);
        }
    }

    private static readonly (ReminderPlace Place, string Title)[] ReminderColumns =
    {
        (ReminderPlace.Reward, "Card rewards"),
        (ReminderPlace.Event, "Events"),
        (ReminderPlace.Shop, "Shops"),
        (ReminderPlace.Map, "Map"),
    };

    /// <summary>Run Reminders: a column per place they show (card reward tabs, event options, shop prices), each its own switch.</summary>
    private void ReminderRows(List<(SourceEntry Entry, string Name)> shown)
    {
        if (!FeatureOn("run-reminders"))
            _rows!.AddChild(Label("Run Reminders is off (above): none of these show.", size: 16, Colors.OrangeRed));
        var head = UiKit.Row(separation: 12);
        head.MouseFilter = Control.MouseFilterEnum.Pass;
        head.AddChild(new Control { CustomMinimumSize = new Vector2(IconSize + NameWidth + 12, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        foreach (var (place, title) in ReminderColumns)
        {
            var cell = UiKit.Row(separation: 8);
            cell.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            cell.SizeFlagsStretchRatio = 1;
            cell.AddChild(Label(title, size: 20, Accent));
            AllButtons(cell, on =>
            {
                foreach (var (sourceEntry, _) in shown)
                    if (sourceEntry.Reminder(place) != null) Settings.Tabs[ChoiceReminders.Key(place, sourceEntry.Type)] = on;
            });
            head.AddChild(cell);
        }
        _rows!.AddChild(head);

        foreach (var (otherSourceEntry, name) in shown)
        {
            var row = SourceRow(otherSourceEntry, name, crossed: false);
            foreach (var (place, _) in ReminderColumns)
            {
                var cell = UiKit.Row(separation: 10);
                cell.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                cell.SizeFlagsStretchRatio = 1;
                if (otherSourceEntry.Reminder(place) is { } text)
                {
                    var key = ChoiceReminders.Key(place, otherSourceEntry.Type);
                    var (_, control) = Check(Settings.TabOwn(key), on => SetTab(key, on), text, tooltip: null, expand: true);
                    cell.AddChild(control);
                }
                else
                {
                    cell.AddChild(Label("—", size: 17, Muted));
                }
                row.AddChild(cell);
            }
            AddRow(row);
        }
    }

    /// <summary>A row with the source's icon (a tiny card for cards, decision #42) and name.</summary>
    private HBoxContainer SourceRow(SourceEntry sourceEntry, string name, bool crossed)
    {
        var row = RowBox();
        var icon = UiKit.Icon(Icon(sourceEntry.Type), IconSize);
        icon.MouseFilter = Control.MouseFilterEnum.Pass;
        icon.TooltipText = GameDescription(sourceEntry.Type);
        if (crossed) AddSlash(icon);
        row.AddChild(icon);
        if (Model(sourceEntry.Type) is CardModel card)
        {
            var tiny = new Cards.TinyCardIcon(card, IconSize);
            icon.AddChild(tiny.Root);
            _tinyIcons.Add(tiny);
        }
        var nameLabel = Label(name, size: 20, Text, NameWidth);
        nameLabel.MouseFilter = Control.MouseFilterEnum.Pass;
        nameLabel.TooltipText = GameDescription(sourceEntry.Type);
        row.AddChild(nameLabel);
        return row;
    }

    // ------------------------------------------------------------------ changes

    private bool FeatureOn(string id)
    {
        if (Settings.Features.TryGetValue(id, out var on)) return on;
        return true;
    }

    private void SetFeature(string id, bool on)
    {
        Settings.Features[id] = on;
        if (on) _features.EnableLate(id);
        Saved();
        Refresh(); // the options that need it
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
        var settings = new QolSettings();
        Settings.Features.Clear();
        Settings.Tabs.Clear();
        Settings.Relics.Clear();
        Settings.ProcNumbers = settings.ProcNumbers;
        Settings.ProcIndirect = settings.ProcIndirect;
        Settings.DeathChance = settings.DeathChance;
        Settings.MultiplayerPredictions = settings.MultiplayerPredictions;
        Sim.SimAdapter.IncludeTeammates = settings.MultiplayerPredictions;
        Settings.TeammateHands = settings.TeammateHands;
        Combat.TurnEndModel.CountTeammateHands = settings.TeammateHands;
        Settings.PulseOneStepEarly = settings.PulseOneStepEarly;
        Settings.EventOdds = settings.EventOdds;
        Settings.EventInspect = settings.EventInspect;
        Settings.EtherealOpacity = settings.EtherealOpacity;
        Settings.FlashHz = settings.FlashHz;
        foreach (var (id, _, _, _) in FeatureRows)
            if (FeatureOn(id)) _features.EnableLate(id);
        Saved();
        Rebuild();
    }

    private void SetTab(string key, bool on)
    {
        Settings.Tabs[key] = on;
        Saved();
    }

    private void SetTracker(string type, bool on)
    {
        Settings.Relics[type] = on;
        Saved();
    }

    private static string NeedsText(string desc, string? needs) => needs == null ? desc : $"{desc} Needs {needs} on.";

    /// <summary>
    /// An option, indented under what it changes (decision #122). <paramref name="needs"/>: what has to be on first (a feature, or
    /// Show chances), null when it is; the row is greyed out and says so until it is.
    /// </summary>
    private void Option(string name, string desc, bool value, Action<bool> set, string? needs = null, bool indent = true, Control? icon = null)
    {
        if (!Matches(name, desc)) return;
        SimpleRow(name, needs == null ? desc : $"{desc} Needs {needs} on.", value, enabled => { set(enabled); Saved(); }, icon ?? IconSpacer(), needs == null, indent);
    }

    private void Saved()
    {
        QolSettings.Revision++;
        _ctx.SettingsFile.Save();
    }

    // ------------------------------------------------------------------ rows

    private const float IconSize = 44;
    private const float NameWidth = 230;
    private const float TrackerWidth = 420;
    private int _row;

    private void Section(string title)
    {
        var label = Label(title, size: 24, Accent);
        var marginContainer = new MarginContainer();
        marginContainer.AddThemeConstantOverride("margin_top", _rows!.GetChildCount() == 0 ? 2 : 18);
        marginContainer.AddThemeConstantOverride("margin_bottom", 6);
        marginContainer.AddChild(label);
        _rows.AddChild(marginContainer);
        _row = 0;
    }

    private static HBoxContainer RowBox()
    {
        var row = UiKit.Row(separation: 12);
        row.MouseFilter = Control.MouseFilterEnum.Pass;
        row.CustomMinimumSize = new Vector2(0, IconSize + 8);
        return row;
    }

    private void AddRow(HBoxContainer row)
    {
        var panelContainer = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        panelContainer.AddThemeStyleboxOverride("panel", UiKit.PanelStyle(_row++ % 2 == 0 ? RowA : RowB, radius: 4, margin: 8));
        panelContainer.AddChild(row);
        _rows!.AddChild(panelContainer);
    }

    /// <summary>The icon a Card tabs row shows on cards (null = none to show here).</summary>
    private static Control? TabPreview(string key)
    {
        Texture2D? tex = null;
        try
        {
            switch (key)
            {
                case "retain": tex = Cards.GameIcons.Enchantment<MegaCrit.Sts2.Core.Models.Enchantments.Steady>(); break;
                case "sly": tex = Cards.GameIcons.Power<MegaCrit.Sts2.Core.Models.Powers.MasterPlannerPower>(); break;
                case "shuffle": tex = Cards.GameIcons.DiscardPile(); break;
                case "lethal": tex = Cards.GameIcons.Skull(); break;
                case "drawn":
                    var label = Label("40%", size: 18, Text);
                    label.CustomMinimumSize = new Vector2(IconSize, IconSize);
                    label.HorizontalAlignment = HorizontalAlignment.Center;
                    return label;
                case "replay":
                    var replayLabel = Label("2×", size: 22, Text);
                    replayLabel.CustomMinimumSize = new Vector2(IconSize, IconSize);
                    replayLabel.HorizontalAlignment = HorizontalAlignment.Center;
                    return replayLabel;
            }
        }
        catch
        {
            tex = null;
        }
        var icon = UiKit.Icon(Cards.GameIcons.Valid(tex) ? tex : null, IconSize);
        return icon;
    }

    /// <summary>The game's art for a feature, beside its switch here and on Quick setup (null: none that reads as it).</summary>
    internal static Texture2D? FeatureArt(string id)
    {
        try
        {
            return id switch
            {
                "incoming-damage" or "teammate-incoming-damage" => Cards.GameIcons.Heart(),
                "death-skull" => Cards.GameIcons.Skull(),
                "intent-details" => Cards.GameIcons.Intent("buff"),
                "intent-forecast" => Cards.GameIcons.Intent("unknown"),
                "card-indicators" => Cards.GameIcons.Enchantment<MegaCrit.Sts2.Core.Models.Enchantments.Steady>(),
                "proc-indicators" => ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.Kunai>().Icon,
                "relic-counters" => ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.PenNib>().Icon,
                "potion-tabs" => ModelDb.AllPotions.Select(potion => Cards.GameIcons.Potion(potion)).FirstOrDefault(Cards.GameIcons.Valid),
                "shuffle-warning" => Cards.GameIcons.DiscardPile(),
                "exploration-progress" => Cards.GameIcons.Clock(),
                "run-reminders" => Cards.GameIcons.CardReward(),
                "rest-preview" => Cards.GameIcons.Heal(),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Run Reminders' art for a place: the card reward icon, the event and shop room icons, the top bar's map button.</summary>
    internal static Texture2D? ReminderPlaceArt(ReminderPlace place) => place switch
    {
        ReminderPlace.Reward => Cards.GameIcons.CardReward(),
        ReminderPlace.Event => Cards.GameIcons.Room(MegaCrit.Sts2.Core.Map.MapPointType.Unknown, MegaCrit.Sts2.Core.Rooms.RoomType.Event),
        ReminderPlace.Shop => Cards.GameIcons.Room(MegaCrit.Sts2.Core.Map.MapPointType.Shop, MegaCrit.Sts2.Core.Rooms.RoomType.Shop),
        ReminderPlace.Map => Cards.GameIcons.MapButton(),
        _ => null,
    };

    /// <summary>The icon beside a feature's switch (a blank of the same size when it has none).</summary>
    private static Control FeatureIcon(string id) => ArtIcon(FeatureArt(id));

    /// <summary>A row's icon from the game's art; a blank of the same size when there's none.</summary>
    private static Control ArtIcon(Texture2D? tex, bool slashed = false)
    {
        if (!Cards.GameIcons.Valid(tex)) return IconSpacer();
        var icon = UiKit.Icon(tex, IconSize);
        if (slashed) AddSlash(icon);
        return icon;
    }

    /// <summary>A row's icon that's a number or a chance as a tab shows it ("4", "50%").</summary>
    private static Control TextIcon(string text)
    {
        var label = Label(text, size: 18, Text);
        label.CustomMinimumSize = new Vector2(IconSize, IconSize);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        return label;
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

    private const float Indent = 28;

    private void SimpleRow(string name, string desc, bool value, Action<bool> set, Control? icon = null, bool enabled = true, bool indent = false)
    {
        var row = RowBox();
        if (indent) row.AddChild(new Control { CustomMinimumSize = new Vector2(Indent, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        var (button, check) = Check(value, set, text: null, tooltip: null);
        button.Disabled = !enabled;
        if (!enabled) row.Modulate = new Color(1, 1, 1, 0.5f);
        row.AddChild(check);
        if (icon != null) row.AddChild(icon);
        row.AddChild(Label(name, size: 20, Text, NameWidth));
        var label = Label(desc, size: 18, Muted);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(label);
        AddRow(row);
    }

    private void Slider(string name, string desc, double value, double min, double max, double step, Action<double> set, Func<double, string> fmt,
        string? needs = null)
    {
        if (!Matches(name, desc)) return;
        if (needs != null) desc = $"{desc} Needs {needs} on.";
        var row = RowBox();
        row.AddChild(new Control { CustomMinimumSize = new Vector2(Indent + CheckSize, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        if (needs != null) row.Modulate = new Color(1, 1, 1, 0.5f);
        row.AddChild(IconSpacer());
        row.AddChild(Label(name, size: 20, Text, NameWidth));
        var valueLabel = Label(fmt(value), size: 20, Accent, width: 70);
        var slider = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, Value = value,
            CustomMinimumSize = new Vector2(260, 30),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            FocusMode = Control.FocusModeEnum.None,
        };
        slider.Editable = needs == null;
        slider.ValueChanged += newValue => { set(newValue); valueLabel.Text = fmt(newValue); };
        slider.DragEnded += _ => Saved();
        row.AddChild(slider);
        row.AddChild(valueLabel);
        var label = Label(desc, size: 18, Muted);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(label);
        AddRow(row);
    }

    private void AllButtons(HBoxContainer parent, Action<bool> setAll)
    {
        foreach (var (text, on) in new[] { ("All on", true), ("All off", false) })
        {
            var button = MakeButton(text, size: 15);
            button.Pressed += () => { setAll(on); Saved(); Rebuild(); };
            parent.AddChild(button);
        }
    }

    // ------------------------------------------------------------------ controls

    private const float CheckSize = 30;

    /// <summary>
    /// A checkbox drawn with our own boxes (filled orange = on), so it doesn't depend on the
    /// game theme's checkbox art. With text, the text is part of the click target.
    /// </summary>
    internal static (Button Button, Control Box) Check(bool value, Action<bool> changed, string? text, string? tooltip, bool expand = false)
    {
        var button = new Button
        {
            ToggleMode = true,
            ButtonPressed = value,
            CustomMinimumSize = new Vector2(CheckSize, CheckSize),
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            TooltipText = tooltip ?? "",
        };
        StyleBoxFlat Box(Color color, Color border)
        {
            var styleBoxFlat = UiKit.PanelStyle(color, radius: 5, margin: 0);
            styleBoxFlat.SetBorderWidthAll(2);
            styleBoxFlat.BorderColor = border;
            return styleBoxFlat;
        }
        var off = Box(new Color(0.1f, 0.1f, 0.13f), new Color(0.55f, 0.55f, 0.6f));
        var offHover = Box(new Color(0.16f, 0.16f, 0.2f), Accent);
        var on = Box(Accent, Accent.Lightened(0.3f));
        var onHover = Box(Accent.Lightened(0.15f), Colors.White);
        button.AddThemeStyleboxOverride("normal", off);
        button.AddThemeStyleboxOverride("hover", offHover);
        button.AddThemeStyleboxOverride("pressed", on);
        button.AddThemeStyleboxOverride("hover_pressed", onHover);
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.AddThemeStyleboxOverride("disabled", Box(new Color(0.1f, 0.1f, 0.1f, 0.5f), new Color(0.3f, 0.3f, 0.3f)));
        button.Toggled += on2 => changed(on2);

        if (text == null) return (button, button);

        var row = UiKit.Row(separation: 10);
        row.MouseFilter = Control.MouseFilterEnum.Pass;
        if (expand) row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(button);
        var label = Label(text, size: 18, Muted);
        label.MouseFilter = Control.MouseFilterEnum.Stop;
        label.TooltipText = tooltip ?? "";
        if (expand)
        {
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }
        label.GuiInput += inputEvent =>
        {
            if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } && !button.Disabled) button.ButtonPressed = !button.ButtonPressed;
        };
        row.AddChild(label);
        return (button, row);
    }

    internal static Button MakeButton(string text, int size)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        button.AddThemeFontSizeOverride("font_size", size);
        var normal = UiKit.PanelStyle(new Color(0.13f, 0.13f, 0.17f), radius: 6, margin: 14);
        normal.SetBorderWidthAll(1);
        normal.BorderColor = new Color(0.4f, 0.4f, 0.45f);
        var hover = UiKit.PanelStyle(new Color(0.2f, 0.2f, 0.26f), radius: 6, margin: 14);
        hover.SetBorderWidthAll(1);
        hover.BorderColor = Accent;
        var pressed = UiKit.PanelStyle(new Color(0.32f, 0.18f, 0.05f), radius: 6, margin: 14);
        pressed.SetBorderWidthAll(2);
        pressed.BorderColor = Accent;
        foreach (var styleBoxFlat in new[] { normal, hover, pressed }) { styleBoxFlat.ContentMarginTop = 6; styleBoxFlat.ContentMarginBottom = 6; }
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", pressed);
        button.AddThemeStyleboxOverride("hover_pressed", pressed);
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.AddThemeColorOverride("font_color", Text);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Accent.Lightened(0.4f));
        button.AddThemeColorOverride("font_hover_pressed_color", Colors.White);
        return button;
    }

    internal static Label Label(string text, int size, Color color, float width = 0)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        if (width > 0)
        {
            label.CustomMinimumSize = new Vector2(width, 0);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }
        return label;
    }

    // ------------------------------------------------------------------ data

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private bool Matches(params string?[] fields)
    {
        var query = _search?.Text?.Trim();
        if (string.IsNullOrEmpty(query)) return true;
        return fields.Any(searchable => searchable != null && searchable.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }

    private static HashSet<string> PresentTypes()
    {
        var set = new HashSet<string>();
        var me = Sts2State.Me;
        var combat = Sts2State.Combat;
        if (me == null) return set;
        foreach (var relic in me.Relics) set.Add(relic.GetType().Name);
        foreach (var card in me.Deck.Cards) set.Add(card.GetType().Name);
        if (combat == null) return set;
        foreach (var power in me.Creature.Powers) set.Add(power.GetType().Name);
        foreach (var creature in combat.Enemies) foreach (var power in creature.Powers) set.Add(power.GetType().Name);
        foreach (var name in Procs.ReturnToHand.Modelled)
            if (Procs.ReturnToHand.Copies(me, name).Any()) set.Add(name);
        return set;
    }

    private AbstractModel? Model(string type)
    {
        if (_models == null)
        {
            _models = new Dictionary<string, AbstractModel>();
            try { foreach (var relic in ModelDb.AllRelics) _models.TryAdd(relic.GetType().Name, relic); } catch (Exception ex) { ModEntry.Log.Warn($"settings menu: relic list: {ex.Message}"); }
            try { foreach (var power in ModelDb.AllPowers) _models.TryAdd(power.GetType().Name, power); } catch (Exception ex) { ModEntry.Log.Warn($"settings menu: power list: {ex.Message}"); }
            try { foreach (var card in ModelDb.AllCards) _models.TryAdd(card.GetType().Name, card); } catch (Exception ex) { ModEntry.Log.Warn($"settings menu: card list: {ex.Message}"); }
        }
        return _models.TryGetValue(type, out var model) ? model : null;
    }

    private static readonly Regex Tags = new(@"\[/?[^\]]*\]", RegexOptions.Compiled);

    private string DisplayName(string type)
    {
        try
        {
            var title = Model(type) switch
            {
                RelicModel relic => relic.Title.GetFormattedText(),
                PowerModel power => power.Title.GetFormattedText(),
                CardModel card => card.Title,
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(title)) return Tags.Replace(title, "");
        }
        catch
        {
            // fall back to the class name
        }
        var name = type.EndsWith("Power", StringComparison.Ordinal) ? type[..^5] : type;
        return Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");
    }

    private string GameDescription(string type)
    {
        try
        {
            var text = Model(type) switch
            {
                RelicModel relic => relic.DynamicDescription.GetFormattedText(),
                // Raw text: formatting a power's description without its owner's amount makes the
                // game log a localization error. Fill the placeholders by hand instead.
                PowerModel power => Placeholders(power.Description.GetRawText()),
                CardModel card => Placeholders(card.Description.GetRawText()),
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
        var pattern = raw.Replace("{}", "X");
        pattern = Plural.Replace(pattern, "$1");
        return Placeholder.Replace(pattern, "X");
    }

    private Texture2D? Icon(string type)
    {
        try
        {
            var tex = Model(type) switch
            {
                RelicModel relic => relic.Icon,
                PowerModel power => power.Icon,
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
