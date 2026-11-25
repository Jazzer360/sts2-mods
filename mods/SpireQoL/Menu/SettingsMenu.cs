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

    // Pages (decisions #66, #186): grouped by where each thing shows. In fights, in sections by where on screen; Between fights; the
    // tags by their source (relics, your powers, enemy powers, cards: one page, a filter for which); the rest; and the Guide and About.
    private enum Page { Setup, Fights, Between, Sources, General, Guide, About }

    private static readonly (Page Page, string Title)[] Pages =
    {
        (Page.Setup, "Quick setup"),
        (Page.Fights, "In fights"),
        (Page.Between, "Between fights"),
        (Page.Sources, "Tags by source"),
        (Page.General, "General"),
        (Page.Guide, "Guide"),
        (Page.About, "About"),
    };

    /// <summary>The page a feature's switch is on.</summary>
    internal enum FeaturePage { Fights, Between, General }

    /// <summary>Where a feature shows (decision #186): its section, in page order; each section is on one page (PageOf).</summary>
    internal enum FeatureSection { OnYourCards, OnEnemies, BesideYou, WhileHoldingACard, UnderEndTurn, BetweenFights, MainMenu, Audits, Sessions }

    /// <summary>Each section's title, on its page and in the Guide.</summary>
    internal static string SectionTitle(FeatureSection section) => section switch
    {
        FeatureSection.OnYourCards => "On your cards",
        FeatureSection.OnEnemies => "On enemies",
        FeatureSection.BesideYou => "Beside you",
        FeatureSection.WhileHoldingACard => "While you hold a card",
        FeatureSection.UnderEndTurn => "Under End Turn",
        FeatureSection.BetweenFights => "Between fights",
        FeatureSection.MainMenu => "On the main menu",
        FeatureSection.Audits => "Audits",
        _ => "Play sessions",
    };

    /// <summary>The page a section is on.</summary>
    internal static FeaturePage PageOf(FeatureSection section) => section switch
    {
        FeatureSection.BetweenFights => FeaturePage.Between,
        FeatureSection.MainMenu or FeatureSection.Audits or FeatureSection.Sessions => FeaturePage.General,
        _ => FeaturePage.Fights,
    };

    /// <summary>Every feature's switch, in the order its page shows them: a section's features together, the sections in page order.</summary>
    internal static readonly (string Id, string Name, string Description, FeatureSection Where)[] FeatureRows =
    {
        ("card-indicators", "Keyword Tags", "Retain, Sly, replay and double-play tags on the cards in your hand (each double-play source's switch, and All double-play tags, is on the Tags by source page).", FeatureSection.OnYourCards),
        ("proc-indicators", "Proc Tags", "A tag for every relic or power a card would set off (each one's switch is on the Tags by source page).", FeatureSection.OnYourCards),
        ("shuffle-warning", "Shuffle Tag", "Discard pile tag on cards whose draw reshuffles your discard pile.", FeatureSection.OnYourCards),
        ("ethereal-fade", "Ethereal Fade", "An Ethereal card in your hand is see-through, so you can tell it will exhaust at the end of your turn.", FeatureSection.OnYourCards),
        ("intent-details", "Intent Details", "On each enemy's intent during your turn: the block its move gains, as a number on its defend intent, and each buff or debuff it applies (icon and amount), in the corner of its buff or debuff intent.", FeatureSection.OnEnemies),
        ("intent-forecast", "Intent Forecast", "Hovering an enemy shows its next moves after the intent on screen: every turn its rules make certain, then each move a random pick can make, with its chance.", FeatureSection.OnEnemies),
        ("death-skull", "Death Skull", "Skull beside an enemy's HP when it dies before acting if you end your turn now (with the chance when a roll decides it).", FeatureSection.OnEnemies),
        ("incoming-damage", "Incoming Damage", "What you'll take at end of turn, after block (and what Osty takes).", FeatureSection.BesideYou),
        ("teammate-incoming-damage", "Teammate Incoming Damage", "Multiplayer: hovering a teammate shows what they'll take, after their block (from what you can see, not their hand).", FeatureSection.BesideYou),
        ("relic-counters", "Relic Counters", "The tray of counter relics and countdown powers next to your HP bar.", FeatureSection.BesideYou),
        ("potion-tabs", "Potion Tags", "Tags under your potions for the relics that drinking one sets off (each relic's switch is on the Tags by source page).", FeatureSection.BesideYou),
        ("play-preview", "Play Preview", "While you hold a card (aimed at its target, for one that needs one), or point at a potion's Drink or aim it: Incoming Damage, the enemies' intents and the Death Skull show the turn as it would go after you play or use it. The card tags stay as they are.", FeatureSection.WhileHoldingACard),
        ("exploration-progress", "Prediction Progress", "A clock and progress bar under the End Turn button while predictions are still working through every outcome (rare: most fights finish at once). Until then, what's uncertain carries a ?.", FeatureSection.UnderEndTurn),
        ("reward-tags", "Reward Tags", "Tags on the cards you're offered (card rewards, an event's choice of cards, the shop) for your relics that taking the card sets off or that make it stronger in fights.", FeatureSection.BetweenFights),
        ("event-reminders", "Event Reminders", "Beside each event option, your relics that change what it does, with the chance a card relic's cards come up, and Inspect for an option that gives random cards.", FeatureSection.BetweenFights),
        ("shop-reminders", "Shop Reminders", "Beside each shop price, your relics that buying it sets off, and with Lucky Fysh the price after its gold.", FeatureSection.BetweenFights),
        ("map-reminders", "Map Reminders", "Beside the map legend's rows, your relics and quest cards that make that kind of room unusually good or bad.", FeatureSection.BetweenFights),
        ("rest-preview", "Rest Preview", "At rest sites, a sum beside your character: your HP, the rest heal and each relic that changes it, and the HP you end with (and how much is wasted). Before the boss, Pantograph and Bath Robes are lines too. In multiplayer, hover a teammate while Mend is on offer for theirs.", FeatureSection.BetweenFights),
        ("update-notice", "Update Notice", "On the main menu, how many Spire QoL versions behind you are (checks GitHub once per visit), with a gear beside it that opens this menu.", FeatureSection.MainMenu),
        ("forecast-audit", "Forecast audit", "Compares Incoming Damage with what really happened (logs, and error reports).", FeatureSection.Audits),
        ("proc-audit", "Play audit", "Compares each card's tags with what playing it really did (logs, and error reports).", FeatureSection.Audits),
        ("coverage-watch", "Coverage audit", "Reports game mechanics the mod doesn't cover yet when they show up (logs, and error reports).", FeatureSection.Audits),
        ("session-summary", "Session summary", "Counts each play session's turns, checks and calculation times, and sends the totals with error reports when the session ends.", FeatureSection.Sessions),
    };

    private readonly QolContext _ctx;
    private readonly FeatureHost _features;
    private readonly OverlayHost _host;

    private CanvasLayer? _layer;
    private Control? _root;
    private VBoxContainer? _rows;
    private HBoxContainer? _columnHeader;
    /// <summary>A page's filter row above its list (Tags by source: which source), hidden on the other pages.</summary>
    private HBoxContainer? _filterRow;
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
        // Timed in parts (the dev dashboard's steady table names the one that makes garbage).
        using (_host.Part("settings-menu:tiny-cards"))
        {
            _tinyIcons.RemoveAll(tinyCardIcon => !GodotObject.IsInstanceValid(tinyCardIcon.Root));
            foreach (var tinyCardIcon in _tinyIcons) tinyCardIcon.Fit();
        }
        if (IsOpen && _page is Page.Guide or Page.Setup)
            using (_host.Part("settings-menu:examples"))
                foreach (var tick in _guideTicks) tick(OverlayHost.Time, Settings.FlashHz);
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
        var info = screen.GetNodeOrNull<Control>(GodotNames.Path("%ModInfoContainer"));
        var title = info?.GetNodeOrNull<RichTextLabel>(GodotNames.Path("ModTitle"))?.Text ?? "";
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
            button.RemoveAndFree();
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
                foreach (var closeKey in closeKeys) PushHotkey(nHotkeyManager, closeKey, _root, _closeAction);
            }
            else
            {
                foreach (var closeKey in closeKeys) RemoveHotkey(nHotkeyManager, closeKey, _root, _closeAction);
                nHotkeyManager.RemoveBlockingScreen(_root);
            }
            _blocking = block;
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"settings menu: couldn't {(block ? "block" : "unblock")} game hotkeys: {ex.Message}");
        }
    }

    /// <summary>NHotkeyManager.PushHotkeyPressedBinding, on any game version (<see cref="CallHotkey"/>).</summary>
    internal static void PushHotkey(NHotkeyManager nHotkeyManager, string key, Node owner, Action action) =>
        CallHotkey(nHotkeyManager, nameof(NHotkeyManager.PushHotkeyPressedBinding), key, owner, action);

    /// <summary>NHotkeyManager.RemoveHotkeyPressedBinding, on any game version (<see cref="CallHotkey"/>).</summary>
    internal static void RemoveHotkey(NHotkeyManager nHotkeyManager, string key, Node owner, Action action) =>
        CallHotkey(nHotkeyManager, nameof(NHotkeyManager.RemoveHotkeyPressedBinding), key, owner, action);

    /// <summary>
    /// A hotkey binding call by reflection: its signature changes between game versions, and a direct call compiled against one is a
    /// missing method on the others (decision #32). Main (v0.111) takes (key, action) and Remove returns nothing; v0.113.0 added an
    /// optional sequence and Remove returns it; v0.114.0 added the binding's owner (key, owner, action), a node the binding fires only
    /// while it's visible and inside the top blocking screen. Each parameter is filled by its type: the key, the owner, the action, and
    /// the defaults for the rest.
    /// </summary>
    private static void CallHotkey(NHotkeyManager nHotkeyManager, string name, string key, Node owner, Action action)
    {
        var method = typeof(NHotkeyManager).GetMethods().FirstOrDefault(otherMethod => otherMethod.Name == name
            && otherMethod.GetParameters() is { Length: >= 2 } parameterInfos && parameterInfos[0].ParameterType == typeof(string)
            && parameterInfos.Skip(1).All(parameterInfo => parameterInfo.ParameterType == typeof(Action) || typeof(Node).IsAssignableFrom(parameterInfo.ParameterType) || parameterInfo.HasDefaultValue))
            ?? throw new MissingMethodException(nameof(NHotkeyManager), name);
        var args = method.GetParameters().Select((parameterInfo, index) =>
            index == 0 ? key
            : parameterInfo.ParameterType == typeof(Action) ? action
            : typeof(Node).IsAssignableFrom(parameterInfo.ParameterType) ? owner
            : parameterInfo.DefaultValue).ToArray();
        method.Invoke(nHotkeyManager, args);
    }

    // ------------------------------------------------------------------ frame

    private PanelContainer? _panel;

    private void Build(SceneTree tree)
    {
        _layer = new CanvasLayer { Name = GodotNames.Name("SpireQoL_Settings"), Layer = 100, Visible = false };
        tree.Root.AddChild(_layer);

        _root = new Control { Name = GodotNames.Name("Root"), MouseFilter = Control.MouseFilterEnum.Stop };
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
        _panel.AddThemeStyleboxOverride(GodotNames.Name("panel"), style);
        _root.AddChild(_panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride(GodotNames.Name("separation"), 10);
        _panel.AddChild(box);

        // Header: title, search, filter, close.
        var header = UiKit.Row(separation: 14);
        header.MouseFilter = Control.MouseFilterEnum.Pass;
        var title = Label("Spire QoL settings", size: 30, Accent);
        header.AddChild(title);
        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        _search = new LineEdit { PlaceholderText = "Search", CustomMinimumSize = new Vector2(260, 0), ClearButtonEnabled = true };
        _search.AddThemeFontSizeOverride(GodotNames.Name("font_size"), 20);
        _search.TextChanged += _ => Rebuild();
        header.AddChild(_search);
        var (only, onlyBox) = Check(value: false, _ => Rebuild(), "Only what I have", "Show only what you have right now: your relics, the cards in your deck and, in a fight, the powers on you and the enemies.");
        _onlyHere = only;
        header.AddChild(onlyBox);
        var reset = MakeButton("Reset defaults", size: 20);
        reset.TooltipText = "Every feature, option, card tag and tracker row back to its default. Keeps hotkeys, widget positions and error reporting.";
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

        _filterRow = UiKit.Row(separation: 6);
        _filterRow.MouseFilter = Control.MouseFilterEnum.Pass;
        box.AddChild(_filterRow);

        _columnHeader = UiKit.Row(separation: 12);
        _columnHeader.MouseFilter = Control.MouseFilterEnum.Pass;
        box.AddChild(_columnHeader);

        _scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride(GodotNames.Name("separation"), 0);
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
        if (_rows == null || _columnHeader == null || _filterRow == null) return;
        UiKit.Clear(_rows);
        UiKit.Clear(_columnHeader);
        UiKit.Clear(_filterRow);
        _filterRow.Visible = false;
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
                case Page.Between: BuildBetween(); break;
                case Page.Sources: BuildSources(); break;
                case Page.General: BuildGeneral(); break;
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
    /// One section's feature switches, each followed by its own options, indented under it (decision #122): every indented row sits
    /// under the switch it depends on. <paramref name="before"/>: rows at the top of the section, before its first feature.
    /// </summary>
    private void FeatureSwitches(FeatureSection section, Dictionary<string, Action>? options = null, Action? before = null)
    {
        Section(SectionTitle(section));
        before?.Invoke();
        foreach (var (id, name, desc, where) in FeatureRows)
        {
            if (where != section) continue;
            if (Matches(name, desc))
            {
                var failed = _features.States.TryGetValue(id, out var featureState) && featureState == FeatureState.Failed;
                SimpleRow(name, failed ? desc + " (turned off after an error; restart the game)" : desc, FeatureOn(id), on => SetFeature(id, on), FeatureIcon(id));
            }
            if (options != null && options.TryGetValue(id, out var add)) add();
        }
    }

    /// <summary>
    /// In fights (decision #186), by where each thing shows: on your cards (every tag on cards and its options, each tag kind under
    /// its feature), on enemies, beside you, while you hold a card, under End Turn; then the chances and multiplayer, which span them.
    /// </summary>
    private void BuildFights()
    {
        // Tags on cards: one switch for all of them, which kinds show under it, then each feature with its own kinds of tag (decisions
        // #122, #187). Each source's tag, and each family's switch, is on Tags by source (the note under Tags on cards says so).
        FeatureSwitches(FeatureSection.OnYourCards, new()
        {
            ["card-indicators"] = () => TagRows("card-indicators"),
            ["proc-indicators"] = () =>
            {
                Option(AmountsOnProcTags, enabled => Settings.ProcNumbers = enabled, TextIcon("4"));
                Option(IndirectProcs, enabled => Settings.ProcIndirect = enabled);
                TagRows("proc-indicators");
            },
            ["shuffle-warning"] = () => TagRows("shuffle-warning"),
            [Features.EtherealFadeFeature.FeatureId] = () => Slider(EtherealOpacity, Settings.EtherealOpacity, min: 0.25, max: 1, step: 0.05,
                value => Settings.EtherealOpacity = (float)value, value => $"{value:0.00}"),
        }, before: () =>
        {
            Option(TagsOnCards, enabled => Settings.CardTabs = enabled, TagIcon(PlainBanner, CrossedBanner));
            Option(PlainTags, enabled => Settings.PlainTabs = enabled, TagIcon(PlainBanner));
            Option(CrossedTags, enabled => Settings.CrossedTabs = enabled, TagIcon(CrossedBanner));
            Option(TargetMarker, enabled => Settings.AimMarker = enabled, ArtIcon(Cards.GameIcons.TargetArrow()));
            // Tag kinds seen in play that the catalog doesn't know (a newer rule): still switchable, under the switch for every tag.
            var needs = NeedsOf(Any(TagsOnCards.Id));
            var other = Settings.Tabs.Keys.Where(key => !key.EndsWith(":*", StringComparison.Ordinal) && !key.StartsWith("reminder-", StringComparison.Ordinal) && !SettingsCatalog.IsKnownTabKey(key)).OrderBy(key => key).ToList();
            foreach (var key in other)
            {
                if (!Matches(key)) continue;
                SimpleRow(key, NeedsText("A tag kind without a description yet.", needs), Settings.TabOwn(key), on => SetTab(key, on), IconSpacer(), needs == null, indent: true);
            }
            GoToNote("Each relic's, power's and card's own tag is switched on Tags by source, under a switch for its whole family (All proc tags, All double-play tags...).",
                "Tags by source", () => _page = Page.Sources);
        });
        FeatureSwitches(FeatureSection.OnEnemies);
        FeatureSwitches(FeatureSection.BesideYou, new()
        {
            ["relic-counters"] = () =>
            {
                Option(TrackerFlashesEarly, enabled => Settings.PulseOneStepEarly = enabled);
                Slider(TrackerFlashSpeed, Settings.FlashHz, min: 0.5, max: 6, step: 0.5, value => Settings.FlashHz = (float)value, value => $"{value:0.0}/s");
            },
            ["potion-tabs"] = () => TagRows("potion-tabs"),
        });
        FeatureSwitches(FeatureSection.WhileHoldingACard);
        FeatureSwitches(FeatureSection.UnderEndTurn);

        ChancesSection();

        Section("Multiplayer");
        Option(MultiplayerPredictions, enabled => { Settings.MultiplayerPredictions = enabled; Sim.SimAdapter.IncludeTeammates = enabled; });
        Option(TeammateHands, enabled => { Settings.TeammateHands = enabled; Combat.TurnEndModel.CountTeammateHands = enabled; });
    }

    /// <summary>
    /// A feature's kinds of tag (SettingsCatalog.CardTabs by their Feature), indented under its switch: greyed out while Tags on cards
    /// (for tags on cards), the feature, or Show chances (for a chance) is off. The families' switches are on Tags by source.
    /// </summary>
    private void TagRows(string feature)
    {
        foreach (var tabEntry in SettingsCatalog.CardTabs)
        {
            if (tabEntry.Feature != feature || !Matches(tabEntry.Name, tabEntry.Description, tabEntry.Key)) continue;
            var needs = NeedsOf(TagNeeds(tabEntry));
            SimpleRow(tabEntry.Name, NeedsText(tabEntry.Description, needs), Settings.TabOwn(tabEntry.Key), on => SetTab(tabEntry.Key, on), TabPreview(tabEntry.Key), needs == null, indent: true);
        }
    }

    /// <summary>
    /// What a kind of tag, or a family's switch, needs on (decision #187): Tags on cards for a tag on cards, the feature that shows it,
    /// and Show chances for a chance.
    /// </summary>
    internal static string[][] TagNeeds(TabEntry tabEntry)
    {
        var needs = new List<string[]>();
        if (tabEntry.OnCards) needs.Add(Any(TagsOnCards.Id));
        needs.Add(Any(tabEntry.Feature));
        if (tabEntry.Chance) needs.Add(Any(ShowChances.Id));
        return needs.ToArray();
    }

    /// <summary>Whether a switch is on: a feature by its id, or an option by its id.</summary>
    private bool SwitchOn(string id) => Options.TryGetValue(id, out var option) ? option.Value?.Invoke(Settings) ?? true : FeatureOn(id);

    /// <summary>A switch's name, as its row shows it.</summary>
    internal static string SwitchName(string id) =>
        Options.TryGetValue(id, out var option) ? option.Name : FeatureRows.First(featureRow => featureRow.Id == id).Name;

    /// <summary>
    /// What a row needs on (decisions #122, #187): null when every group has a switch on, else the first group that hasn't, by name
    /// (any one of them will do), for the greyed-out row.
    /// </summary>
    private string? NeedsOf(params string[][] needs)
    {
        foreach (var group in needs)
            if (!group.Any(SwitchOn)) return string.Join(" or ", group.Select(SwitchName));
        return null;
    }

    /// <summary>Whether another row greys out with this option (it's named in some row's needs): its page is built again when it changes.</summary>
    private static bool IsParent(MenuOption option) =>
        Options.Values.Any(other => other.Needs.Any(group => group.Contains(option.Id)));

    /// <summary>
    /// Chances in fights (decision #122): one switch for every chance the mod shows, then each one under it, greyed out while it's off
    /// or while the feature that shows it is. The chance under event icons is under Event Reminders, on Between fights (decision #186).
    /// Each chance has the art of what it's on with a % in its corner (decision #187), apart from the draw chance, drawn as its tag.
    /// </summary>
    private void ChancesSection()
    {
        Section("Chances");
        Option(ShowChances, enabled => Settings.Chances = enabled, TextIcon("50%"));
        Option(DeathChances, enabled => Settings.DeathChance = enabled, ArtIcon(Cards.GameIcons.Skull(), corner: "%"));
        Option(MostLikely, enabled => Settings.MostLikely = enabled, ArtIcon(Cards.GameIcons.Heart(), corner: "%"));
        Option(IntentRandomMoves, enabled => Settings.IntentOdds = enabled, ArtIcon(Cards.GameIcons.Intent("unknown"), corner: "%"));
    }

    /// <summary>
    /// A note indented under the rows above it, with a button that opens where it points (another page, or a source on Tags by source).
    /// No setting of its own.
    /// </summary>
    private void GoToNote(string text, string button, Action openPage)
    {
        if (!Matches(text, button)) return;
        var row = RowBox();
        row.AddChild(new Control { CustomMinimumSize = new Vector2(Indent + CheckSize, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        row.AddChild(IconSpacer());
        var label = Label(text, size: 18, Muted);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(label);
        var open = MakeButton(button, size: 17);
        open.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        open.Pressed += () =>
        {
            openPage();
            _scroll!.ScrollVertical = 0;
            Refresh();
        };
        row.AddChild(open);
        AddRow(row);
    }

    /// <summary>The page again, for rows that depend on a switch just changed (deferred: the switch is mid-signal).</summary>
    private void Refresh() => Callable.From(Rebuild).CallDeferred();

    /// <summary>Between fights: its features and options, then a switch per relic or card for each place its reminder shows.</summary>
    private void BuildBetween()
    {
        FeatureSwitches(FeatureSection.BetweenFights, new()
        {
            [Features.EventRemindersFeature.FeatureId] = () =>
            {
                Option(InspectEventCards, enabled => Settings.EventInspect = enabled, ArtIcon(Features.RunReminders.InspectIcon()));
                Option(EventChance, enabled => Settings.EventOdds = enabled, TextIcon("25%"));
            },
        });

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

    /// <summary>The rest: the main menu notice, error reports, the audits, the session summary, and dev mode in a developer's game.</summary>
    private void BuildGeneral()
    {
        FeatureSwitches(FeatureSection.MainMenu);

        Section("Error reports");
        Option(SendErrorReports, enabled =>
        {
            Settings.ReportErrors = enabled;
            if (Sts2Mods.Core.Diagnostics.ErrorReporter.Instance is { } errorReporter) errorReporter.Active = enabled;
        });

        FeatureSwitches(FeatureSection.Audits);
        FeatureSwitches(FeatureSection.Sessions);

        // Dev mode (decision #108): only in a developer's game (on there, or set in the file). Off for players: their reports reach Sentry.
        if (!ModEntry.DevMode && Settings.DevMode == null) return;
        Section("Developer");
        Option(DevMode, enabled => Settings.DevMode = enabled);
        TraceLevelRow();
    }

    /// <summary>
    /// The performance trace's level (decision #258): Off, Memory or Full, applied at once (Telemetry.DevDashboard.Trace), with what the
    /// recorder says now (recording which file, or dotnet-trace isn't installed and how to install it).
    /// </summary>
    private void TraceLevelRow()
    {
        var option = PerformanceTrace;
        if (!Matches(option.Name, option.Description)) return;
        var needs = NeedsOf(option.Needs);
        var row = RowBox();
        row.AddChild(new Control { CustomMinimumSize = new Vector2(Indent + CheckSize, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        if (needs != null) row.Modulate = new Color(1, 1, 1, 0.5f);
        row.AddChild(IconSpacer());
        row.AddChild(Label(option.Name, size: 20, Text, NameWidth));
        var group = new ButtonGroup();
        foreach (var level in Enum.GetValues<Telemetry.TracingLevel>())
        {
            var button = MakeButton(level.ToString(), size: 18);
            button.ToggleMode = true;
            button.ButtonGroup = group;
            button.ButtonPressed = Settings.TraceLevel == level;
            button.Disabled = needs != null;
            button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            button.Pressed += () =>
            {
                Settings.TraceLevel = level;
                Saved();
                Telemetry.DevDashboard.Trace(level);
                Refresh(); // the status line: the recorder says what it does on its next pass
            };
            row.AddChild(button);
        }
        var status = Telemetry.DevDashboard.TraceStatus();
        var label = Label(NeedsText(status == null ? option.Description : $"{status} {option.Description}", needs), size: 18, Muted);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(label);
        AddRow(row);
    }

    /// <summary>Which source the Tags by source page lists (decision #186): relics until another is picked.</summary>
    private SourceGroup _sourceGroup = SourceGroup.Relic;

    private static readonly (SourceGroup Group, string Title)[] SourceGroups =
    {
        (SourceGroup.Relic, "Relics"),
        (SourceGroup.YourPower, "Your powers"),
        (SourceGroup.EnemyPower, "Enemy powers"),
        (SourceGroup.Card, "Cards"),
    };

    /// <summary>
    /// Tags by source (decision #186): every relic, power and card with a tag or a tracker row, one source at a time (the filter row
    /// above the list). A search lists its matches from every source, each under its own heading.
    /// </summary>
    private void BuildSources()
    {
        foreach (var (group, title) in SourceGroups)
        {
            var button = MakeButton(title, size: 19);
            button.ToggleMode = true;
            button.ButtonPressed = group == _sourceGroup;
            button.Pressed += () => { _sourceGroup = group; _scroll!.ScrollVertical = 0; Rebuild(); };
            _filterRow!.AddChild(button);
        }
        _filterRow!.Visible = true;

        var searching = !string.IsNullOrWhiteSpace(_search?.Text);
        var any = false;
        foreach (var (group, title) in SourceGroups)
        {
            if (!searching && group != _sourceGroup) continue;
            var entries = SettingsCatalog.Sources.Where(sourceEntry => sourceEntry.Group == group && sourceEntry.InFights).ToList();
            var here = _onlyHere!.ButtonPressed ? PresentTypes() : null;
            var shown = entries
                .Where(sourceEntry => here == null || here.Contains(sourceEntry.Type))
                .Select(sourceEntry => (Entry: sourceEntry, Name: DisplayName(sourceEntry.Type)))
                .Where(entryName => Matches(entryName.Name, entryName.Entry.Type, entryName.Entry.Tag, entryName.Entry.Tracker, entryName.Entry.Reward, entryName.Entry.Event, entryName.Entry.Shop, entryName.Entry.Map))
                .OrderBy(entryName => entryName.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var familyMatch = here == null && FamiliesIn(entries).Any(family => Matches(family.Name, family.Description, family.Key));
            if (shown.Count == 0 && !familyMatch) continue;
            any = true;
            // One source: its column headers stay above the list. Several (a search): each its own heading and headers.
            HBoxContainer header;
            if (searching)
            {
                Section(title);
                header = UiKit.Row(separation: 12);
                header.MouseFilter = Control.MouseFilterEnum.Pass;
                _rows!.AddChild(header);
            }
            else header = _columnHeader!;
            // What they do between fights (the reminders) is on the Between fights page.
            FightRows(entries, shown, header);
        }
        if (!any) _rows!.AddChild(Label(_onlyHere!.ButtonPressed ? "You have none of these right now." : "No matches.", size: 20, Muted));
    }

    /// <summary>The families (SettingsCatalog.TabGroups) with a source among <paramref name="entries"/>, in the catalog's order.</summary>
    private static IEnumerable<TabEntry> FamiliesIn(List<SourceEntry> entries) =>
        SettingsCatalog.TabGroups.Where(family => entries.Any(sourceEntry => SettingsCatalog.FamilyOf(sourceEntry) == family));

    /// <summary>
    /// On-card tabs and tracker rows, with their column headers in <paramref name="header"/> (decision #187): each family's switch, with
    /// its sources indented under it and greyed out while it's off; then the sources with only a Relic Counters row.
    /// </summary>
    private void FightRows(List<SourceEntry> entries, List<(SourceEntry Entry, string Name)> shown, HBoxContainer header)
    {
        var anyTracker = entries.Any(sourceEntry => sourceEntry.Tracker != null);

        // Column headers with all-on / all-off for what's listed. Every source row is indented under its family's switch.
        header.AddChild(new Control { CustomMinimumSize = new Vector2(Indent + IconSize + NameWidth + 24, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        var tagHead = UiKit.Row(separation: 8);
        tagHead.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        tagHead.AddChild(Label("On cards", size: 20, Accent));
        AllButtons(tagHead, on =>
        {
            foreach (var (sourceEntry, _) in shown) if (sourceEntry.TagKey != null) Settings.Tabs[sourceEntry.TagKey] = on;
        });
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

        // Each family's switch at the top of its sources (decision #187): a family over several sources heads each of their lists.
        foreach (var family in FamiliesIn(entries))
        {
            var members = shown.Where(entryName => SettingsCatalog.FamilyOf(entryName.Entry) == family).ToList();
            if (members.Count == 0 && (_onlyHere!.ButtonPressed || !Matches(family.Name, family.Description, family.Key))) continue;
            FamilyRow(family);
            foreach (var (sourceEntry, name) in members) SourceTableRow(sourceEntry, name, family, anyTracker);
        }
        var trackerOnly = shown.Where(entryName => entryName.Entry.TagKey == null).ToList();
        if (trackerOnly.Count == 0) return;
        var heading = Label("Only a Relic Counters row (no card tag)", size: 20, Text);
        var marginContainer = new MarginContainer();
        marginContainer.AddThemeConstantOverride(GodotNames.Name("margin_top"), 12);
        marginContainer.AddThemeConstantOverride(GodotNames.Name("margin_bottom"), 4);
        marginContainer.AddChild(heading);
        _rows!.AddChild(marginContainer);
        foreach (var (sourceEntry, name) in trackerOnly) SourceTableRow(sourceEntry, name, family: null, anyTracker);
    }

    /// <summary>
    /// A family's switch (SettingsCatalog.TabGroups: "proc:*" and the rest) at the top of its sources: off hides every tag in it,
    /// whatever each one's own switch. Greyed out while Tags on cards (for tags on cards) or the feature showing them is off.
    /// </summary>
    private void FamilyRow(TabEntry family)
    {
        var lists = SourceGroups.Where(sourceGroup => SettingsCatalog.Sources.Any(sourceEntry => sourceEntry.Group == sourceGroup.Group && SettingsCatalog.FamilyOf(sourceEntry) == family))
            .Select(sourceGroup => sourceGroup.Title).ToList();
        var desc = lists.Count > 1 ? $"{family.Description} One switch at the top of each list it covers: {string.Join(", ", lists)}." : family.Description;
        var needs = NeedsOf(TagNeeds(family));
        SimpleRow(family.Name, NeedsText(desc, needs), Settings.TabOwn(family.Key), on => { SetTab(family.Key, on); Refresh(); }, FamilyIcon(family.Key), needs == null);
    }

    /// <summary>
    /// A source's row: its tag (greyed out while its family, Tags on cards or the feature showing it is off) and its Relic Counters
    /// row (greyed out while Relic Counters is off), indented under its family's switch.
    /// </summary>
    private void SourceTableRow(SourceEntry sourceEntry, string name, TabEntry? family, bool anyTracker)
    {
        // The icon as the tab shows it: crossed tabs get the same red slash as on cards.
        var crossed = sourceEntry.Tag?.StartsWith(SettingsCatalog.CrossedPrefix, StringComparison.Ordinal) == true;
        var tagNeeds = family == null ? null : NeedsOf(TagNeeds(family)) ?? (Settings.TabOwn(family.Key) ? null : family.Name);
        var trackerNeeds = sourceEntry.Tracker == null ? null : NeedsOf(Any("relic-counters"));
        // Everything on the row waits on a switch that's off: the whole row greys out, its icon and name too.
        var blocked = (sourceEntry.TagKey == null || tagNeeds != null) && (sourceEntry.Tracker == null || trackerNeeds != null);
        var row = SourceRow(sourceEntry, name, crossed, indent: true);
        if (blocked) row.Modulate = new Color(1, 1, 1, 0.5f);

        var tagCell = UiKit.Row(separation: 10);
        tagCell.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (sourceEntry.TagKey is { } key)
        {
            var text = crossed ? "Crossed. " + Capitalize(sourceEntry.Tag![SettingsCatalog.CrossedPrefix.Length..]) : sourceEntry.Tag!;
            tagCell.AddChild(TableCheck(Settings.TabOwn(key), on => SetTab(key, on), text, tagNeeds, dim: !blocked));
        }
        else
        {
            tagCell.AddChild(Label("no card tag", size: 17, Muted));
        }
        row.AddChild(tagCell);

        if (anyTracker)
        {
            var trackCell = UiKit.Row(separation: 10);
            trackCell.CustomMinimumSize = new Vector2(TrackerWidth, 0);
            if (sourceEntry.Tracker != null)
                trackCell.AddChild(TableCheck(Settings.TrackerEnabled(sourceEntry.Type, sourceEntry.TrackerDefault), on => SetTracker(sourceEntry.Type, on), sourceEntry.Tracker, trackerNeeds, dim: !blocked));
            row.AddChild(trackCell);
        }
        AddRow(row);
    }

    /// <summary>A table cell's checkbox with its text: greyed out and saying what it needs while <paramref name="needs"/> is set.</summary>
    private static Control TableCheck(bool value, Action<bool> set, string text, string? needs, bool dim)
    {
        var (button, control) = Check(value, set, text, needs == null ? null : $"Needs {needs} on.", expand: true);
        if (needs == null) return control;
        button.Disabled = true;
        if (dim) control.Modulate = new Color(1, 1, 1, 0.5f);
        return control;
    }

    private static readonly (ReminderPlace Place, string Title)[] ReminderColumns =
    {
        (ReminderPlace.Reward, "Card rewards"),
        (ReminderPlace.Event, "Events"),
        (ReminderPlace.Shop, "Shops"),
        (ReminderPlace.Map, "Map"),
    };

    /// <summary>The four reminders outside fights (decision #185): the features whose work the Reminders columns switch, relic by relic.</summary>
    internal static readonly string[] ReminderFeatures =
    {
        Features.RewardTagsFeature.FeatureId, Features.EventRemindersFeature.FeatureId, Features.ShopRemindersFeature.FeatureId, Features.MapRemindersFeature.FeatureId,
    };

    /// <summary>The reminders: a column per place they show (card reward tags, event options, shop prices, the map), each its own switch.</summary>
    private void ReminderRows(List<(SourceEntry Entry, string Name)> shown)
    {
        var off = ReminderFeatures.Where(id => !FeatureOn(id)).Select(id => FeatureRows.First(featureRow => featureRow.Id == id).Name).ToList();
        if (off.Count > 0)
            _rows!.AddChild(Label($"Off (above): {string.Join(", ", off)}. What {(off.Count == 1 ? "it shows" : "they show")} doesn't show, whatever its switches here.", size: 16, Colors.OrangeRed));
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
            for (var i = 0; i < ReminderColumns.Length; i++)
            {
                var place = ReminderColumns[i].Place;
                // Each column waits on its feature (the columns and ReminderFeatures are in the same order).
                var needs = NeedsOf(Any(ReminderFeatures[i]));
                var cell = UiKit.Row(separation: 10);
                cell.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                cell.SizeFlagsStretchRatio = 1;
                if (otherSourceEntry.Reminder(place) is { } text)
                {
                    var key = ChoiceReminders.Key(place, otherSourceEntry.Type);
                    cell.AddChild(TableCheck(Settings.TabOwn(key), on => SetTab(key, on), text, needs, dim: true));
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
    private HBoxContainer SourceRow(SourceEntry sourceEntry, string name, bool crossed, bool indent = false)
    {
        var row = RowBox();
        if (indent) row.AddChild(new Control { CustomMinimumSize = new Vector2(Indent, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        var icon = UiKit.Icon(Icon(sourceEntry.Type), IconSize);
        icon.MouseFilter = Control.MouseFilterEnum.Pass;
        icon.TooltipText = GameDescription(sourceEntry.Type);
        if (crossed) AddSlash(icon);
        icon.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
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
        Settings.CardTabs = settings.CardTabs;
        Settings.PlainTabs = settings.PlainTabs;
        Settings.CrossedTabs = settings.CrossedTabs;
        Settings.AimMarker = settings.AimMarker;
        Settings.Chances = settings.Chances;
        Settings.MostLikely = settings.MostLikely;
        Settings.IntentOdds = settings.IntentOdds;
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
    /// An option's row from its table entry (decisions #122, #187): a master at its section's level; any other indented under the
    /// switch it depends on, greyed out and saying what it needs while that's off. When other rows grey out with it, the page is built
    /// again on a change.
    /// </summary>
    private void Option(MenuOption option, Action<bool> set, Control? icon = null)
    {
        if (!Matches(option.Name, option.Description)) return;
        var needs = NeedsOf(option.Needs);
        var parent = IsParent(option);
        SimpleRow(option.Name, NeedsText(option.Description, needs), option.Value!(Settings), enabled =>
        {
            set(enabled);
            Saved();
            if (parent) Refresh();
        }, icon ?? IconSpacer(), needs == null, indent: !option.IsMaster);
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
        marginContainer.AddThemeConstantOverride(GodotNames.Name("margin_top"), _rows!.GetChildCount() == 0 ? 2 : 18);
        marginContainer.AddThemeConstantOverride(GodotNames.Name("margin_bottom"), 6);
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
        panelContainer.AddThemeStyleboxOverride(GodotNames.Name("panel"), UiKit.PanelStyle(_row++ % 2 == 0 ? RowA : RowB, radius: 4, margin: 8));
        panelContainer.AddChild(row);
        _rows!.AddChild(panelContainer);
    }

    /// <summary>
    /// A kind of tag's row icon: the tag as cards show it (decision #187), the tag art with its icon or text, so a kind of tag reads
    /// apart from the feature or display whose art it carries (the lethal tag's skull from the Death Skull). Its bare icon or text
    /// when the tag art can't be had.
    /// </summary>
    private static Control TabPreview(string key)
    {
        Cards.TabSpec? spec;
        try
        {
            spec = key switch
            {
                "retain" => new Cards.TabSpec(key, Cards.GameIcons.Enchantment<MegaCrit.Sts2.Core.Models.Enchantments.Steady>()),
                "sly" => new Cards.TabSpec(key, Cards.GameIcons.Power<MegaCrit.Sts2.Core.Models.Powers.MasterPlannerPower>()),
                "shuffle" => new Cards.TabSpec(key, Cards.GameIcons.DiscardPile()),
                "lethal" => new Cards.TabSpec(key, Cards.GameIcons.Skull()),
                "drawn" => new Cards.TabSpec(key, Icon: null, Text: "40%"),
                "replay" => new Cards.TabSpec(key, Icon: null, Text: "2×"),
                _ => null,
            };
        }
        catch
        {
            spec = null;
        }
        if (spec == null) return IconSpacer();
        return TagIcon(spec) is { } tag ? tag : spec.Text != null ? TextIcon(spec.Text) : ArtIcon(spec.Icon);
    }

    /// <summary>A bare plain tag, and a bare crossed one: the tag art itself (Tags on cards, Plain tags, Crossed tags).</summary>
    private static Cards.TabSpec PlainBanner => new("plain", Icon: null);
    private static Cards.TabSpec CrossedBanner => new("crossed", Icon: null, Crossed: true);

    /// <summary>
    /// Tags drawn as on a card, the game's enchantment tab every tag copies (decision #187), overlapping as on a crowded card; null when
    /// the tag art can't be had (the caller's fallback is a blank or the bare icon, never another row's art).
    /// </summary>
    private static Control? TagIcon(params Cards.TabSpec[] tabs) => Cards.CardOverlayRenderer.Standalone(tabs, IconSize);

    /// <summary>
    /// A family's switch on Tags by source (decision #187): a few of its tags as cards show them, so it reads as that family's whole set,
    /// apart from any one source's row; the card glyph for the tags whose icon is a card (return tags).
    /// </summary>
    private static Control FamilyIcon(string key)
    {
        Cards.TabSpec[] tabs;
        try
        {
            tabs = key switch
            {
                "proc:*" => new[]
                {
                    new Cards.TabSpec(key, ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.OrnamentalFan>().Icon),
                    new Cards.TabSpec(key, Cards.GameIcons.Power<MegaCrit.Sts2.Core.Models.Powers.FeelNoPainPower>()),
                    new Cards.TabSpec(key, Cards.GameIcons.Power<MegaCrit.Sts2.Core.Models.Powers.ThornsPower>(), Crossed: true),
                },
                "double-play:*" => new[]
                {
                    new Cards.TabSpec(key, Cards.GameIcons.Power<MegaCrit.Sts2.Core.Models.Powers.EchoFormPower>()),
                    new Cards.TabSpec(key, Cards.GameIcons.Power<MegaCrit.Sts2.Core.Models.Powers.BurstPower>()),
                },
                "potion:*" => new[]
                {
                    new Cards.TabSpec(key, ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.ReptileTrinket>().Icon),
                    new Cards.TabSpec(key, ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.BeltBuckle>().Icon),
                },
                "free:*" => new[]
                {
                    new Cards.TabSpec(key, Cards.GameIcons.Power<MegaCrit.Sts2.Core.Models.Powers.FreeAttackPower>()),
                    new Cards.TabSpec(key, Cards.GameIcons.Power<MegaCrit.Sts2.Core.Models.Powers.FreeSkillPower>()),
                },
                "return:*" => new[] { new Cards.TabSpec(key, Cards.GameIcons.CardGlyph()) },
                _ => new[] { PlainBanner },
            };
        }
        catch
        {
            tabs = new[] { PlainBanner };
        }
        return TagIcon(tabs) ?? IconSpacer();
    }

    /// <summary>The game's art for a feature, beside its switch here and on Quick setup (null: none that reads as it).</summary>
    internal static Texture2D? FeatureArt(string id)
    {
        try
        {
            return id switch
            {
                "incoming-damage" => Cards.GameIcons.Heart(),
                "teammate-incoming-damage" => Cards.GameIcons.Teammate(),
                "death-skull" => Cards.GameIcons.Skull(),
                "intent-details" => Cards.GameIcons.Intent("buff"),
                "intent-forecast" => Cards.GameIcons.Intent("unknown"),
                "play-preview" => Cards.GameIcons.Intent("attack"),
                "card-indicators" => Cards.GameIcons.Enchantment<MegaCrit.Sts2.Core.Models.Enchantments.Steady>(),
                "proc-indicators" => ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.Kunai>().Icon,
                "relic-counters" => ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.PenNib>().Icon,
                "potion-tabs" => ModelDb.AllPotions.Select(potion => Cards.GameIcons.Potion(potion)).FirstOrDefault(Cards.GameIcons.Valid),
                "shuffle-warning" => Cards.GameIcons.DiscardPile(),
                "exploration-progress" => Cards.GameIcons.Clock(),
                "reward-tags" => ReminderPlaceArt(ReminderPlace.Reward),
                "event-reminders" => ReminderPlaceArt(ReminderPlace.Event),
                "shop-reminders" => ReminderPlaceArt(ReminderPlace.Shop),
                "map-reminders" => ReminderPlaceArt(ReminderPlace.Map),
                "rest-preview" => Cards.GameIcons.Heal(),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A reminder place's art: the card reward icon, the event and shop room icons, the top bar's map button.</summary>
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

    /// <summary>
    /// A row's icon from the game's art; a blank of the same size when there's none. <paramref name="corner"/>: text in its lower-right
    /// corner, as the game places a power's amount (a chance's "%" on the art of what it's on: decision #187).
    /// </summary>
    private static Control ArtIcon(Texture2D? tex, string? corner = null)
    {
        if (!Cards.GameIcons.Valid(tex)) return corner == null ? IconSpacer() : TextIcon(corner);
        var icon = UiKit.Icon(tex, IconSize);
        if (corner == null) return icon;
        var label = Label(corner, size: 18, Colors.White);
        label.AddThemeColorOverride(GodotNames.Name("font_outline_color"), Colors.Black);
        label.AddThemeConstantOverride(GodotNames.Name("outline_size"), 6);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        label.VerticalAlignment = VerticalAlignment.Bottom;
        label.Size = new Vector2(IconSize + 6, IconSize + 6);
        icon.AddChild(label);
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
        if (icon != null)
        {
            // Centred in the row: a row a long description makes taller kept it at the top.
            icon.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(icon);
        }
        row.AddChild(Label(name, size: 20, Text, NameWidth));
        var label = Label(desc, size: 18, Muted);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(label);
        AddRow(row);
    }

    /// <summary>A slider option's row from its table entry, indented under the switch it depends on and greyed out while that's off.</summary>
    private void Slider(MenuOption option, double value, double min, double max, double step, Action<double> set, Func<double, string> fmt)
    {
        if (!Matches(option.Name, option.Description)) return;
        var name = option.Name;
        var needs = NeedsOf(option.Needs);
        var desc = NeedsText(option.Description, needs);
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
        button.AddThemeStyleboxOverride(GodotNames.Name("normal"), off);
        button.AddThemeStyleboxOverride(GodotNames.Name("hover"), offHover);
        button.AddThemeStyleboxOverride(GodotNames.Name("pressed"), on);
        button.AddThemeStyleboxOverride(GodotNames.Name("hover_pressed"), onHover);
        button.AddThemeStyleboxOverride(GodotNames.Name("focus"), new StyleBoxEmpty());
        button.AddThemeStyleboxOverride(GodotNames.Name("disabled"), Box(new Color(0.1f, 0.1f, 0.1f, 0.5f), new Color(0.3f, 0.3f, 0.3f)));
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
        button.AddThemeFontSizeOverride(GodotNames.Name("font_size"), size);
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
        button.AddThemeStyleboxOverride(GodotNames.Name("normal"), normal);
        button.AddThemeStyleboxOverride(GodotNames.Name("hover"), hover);
        button.AddThemeStyleboxOverride(GodotNames.Name("pressed"), pressed);
        button.AddThemeStyleboxOverride(GodotNames.Name("hover_pressed"), pressed);
        button.AddThemeStyleboxOverride(GodotNames.Name("focus"), new StyleBoxEmpty());
        button.AddThemeColorOverride(GodotNames.Name("font_color"), Text);
        button.AddThemeColorOverride(GodotNames.Name("font_hover_color"), Colors.White);
        button.AddThemeColorOverride(GodotNames.Name("font_pressed_color"), Accent.Lightened(0.4f));
        button.AddThemeColorOverride(GodotNames.Name("font_hover_pressed_color"), Colors.White);
        return button;
    }

    internal static Label Label(string text, int size, Color color, float width = 0)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride(GodotNames.Name("font_size"), size);
        label.AddThemeColorOverride(GodotNames.Name("font_color"), color);
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
