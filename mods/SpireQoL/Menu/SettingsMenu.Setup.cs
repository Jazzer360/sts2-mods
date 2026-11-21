using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using SpireQoL.Cards;
using SpireQoL.Data;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Menu;

/// <summary>
/// Quick setup (decision #147): the settings menu's first page, and what it opens on at the first launch (QolSettings.SetupDone).
/// One switch per part of the mod a player meets, with the game's own art beside it; card tabs as four levels, each shown on a real
/// card; and Everything, the mod's defaults. Every change applies at once, as on the other pages.
/// </summary>
public sealed partial class SettingsMenu
{
    /// <summary>How much card tabs show: the card tab features it turns on (the rest of CardTabFeatures off) and indirect procs.</summary>
    internal sealed record CardTabLevel(string Name, string Summary, IReadOnlySet<string> Features, bool ProcIndirect);

    /// <summary>The features the card tab levels decide.</summary>
    internal static readonly IReadOnlySet<string> CardTabFeatures = new HashSet<string> { "card-indicators", "proc-indicators", "shuffle-warning" };

    internal static readonly CardTabLevel[] CardTabLevels =
    {
        new("None", "No tabs on cards.", new HashSet<string>(), ProcIndirect: false),
        new("Minimal", "What the card picks up: Retain, Sly, played twice. Ethereal cards fade.", new HashSet<string> { "card-indicators" }, ProcIndirect: false),
        new("Moderate", "Plus a tab for each relic or power playing it sets off, and a crossed tab when that's bad for you.",
            new HashSet<string> { "card-indicators", "proc-indicators" }, ProcIndirect: false),
        new("Full", "Plus what it sets off through the cards it draws, exhausts or discards, and a tab when a draw reshuffles your discard pile.",
            new HashSet<string>(CardTabFeatures), ProcIndirect: true),
    };

    /// <summary>One switch on the page: the features it turns on and off together.</summary>
    private sealed record SetupSwitch(string Name, string Description, string[] Features, Func<Texture2D?> Icon);

    private static readonly SetupSwitch[] FightSwitches =
    {
        new("Damage you'll take", "What ending your turn costs you, after block, beside your HP (and a teammate's, when you hover them).",
            new[] { "incoming-damage", "teammate-incoming-damage" }, GameIcons.Heart),
        new("Enemies that die", "A skull beside an enemy that dies before it acts if you end your turn now.", new[] { "death-skull" }, GameIcons.Skull),
        new("Enemy intents", "On each enemy's intent: the block its move gains, and the buffs and debuffs it applies with their amounts.",
            new[] { "intent-details" }, () => GameIcons.Intent("buff")),
        new("Enemy's next moves", "Hover an enemy: its moves after this one, every turn its rules make certain, then what a random pick can make.",
            new[] { "intent-forecast" }, () => GameIcons.Intent("unknown")),
        new("Counter relics", "A tray beside your HP of the relics and powers that count up to something, flashing when they're about to fire.",
            new[] { "relic-counters" }, () => RelicIcon<PenNib>()),
        new("Potion tabs", "A tab under a potion for each relic that drinking it sets off.", new[] { "potion-tabs" }, () => PotionIcon()),
    };

    /// <summary>Run Reminders by place (one switch each); the feature itself is on while any of them is.</summary>
    private static readonly (ReminderPlace Place, string Name, string Description, Func<Texture2D?> Icon)[] ReminderSwitches =
    {
        (ReminderPlace.Reward, "Card rewards", "A tab on a card you're offered when one of your relics cares about it (an egg upgrades it, a relic counts it).", GameIcons.CardReward),
        (ReminderPlace.Event, "Events", "Your relics that change an event's choice, beside the option, and Inspect for an option that gives random cards.",
            () => GameIcons.Room(MegaCrit.Sts2.Core.Map.MapPointType.Unknown, MegaCrit.Sts2.Core.Rooms.RoomType.Event)),
        (ReminderPlace.Shop, "Shops", "Your relics that change a price or a card for sale, beside it.",
            () => GameIcons.Room(MegaCrit.Sts2.Core.Map.MapPointType.Shop, MegaCrit.Sts2.Core.Rooms.RoomType.Shop)),
        (ReminderPlace.Map, "Map", "Your relics that care about a kind of room, beside the map legend's row.", GameIcons.MapButton),
    };

    private static Texture2D? RelicIcon<T>() where T : RelicModel
    {
        try { return ModelDb.Relic<T>().Icon; } catch { return null; }
    }

    private static Texture2D? PotionIcon()
    {
        try { return ModelDb.AllPotions.Select(potion => GameIcons.Potion(potion)).FirstOrDefault(GameIcons.Valid); } catch { return null; }
    }

    private bool _setupShown;

    /// <summary>The first launch: the menu opens on Quick setup over the main menu, once; closing it sets SetupDone.</summary>
    private void FirstLaunch()
    {
        if (_setupShown || Settings.SetupDone || IsOpen) return;
        if (NGame.Instance?.MainMenu is not { } mainMenu || !GodotObject.IsInstanceValid(mainMenu) || !mainMenu.IsVisibleInTree()) return;
        _setupShown = true;
        _page = Page.Setup;
        Open();
    }

    private void BuildSetup()
    {
        Section("Quick setup");
        Paragraph("Spire QoL works out what the game already knows: the damage you'll take, which enemies die before they act, and what each card would set off. "
            + "It never looks ahead at a random roll or the draw pile's order. Pick what you'd like to see; every change applies at once, and the other pages have the rest.");
        var everythingRow = RowBox();
        var everything = MakeButton("Everything", size: 22);
        everything.TooltipText = "Every feature and option on: the mod's defaults.";
        everything.Pressed += () => { ApplyEverything(); Close(); };
        everythingRow.AddChild(everything);
        everythingRow.AddChild(Wrapped("Every feature and option on, the mod's defaults, and done. Busy turns show four or five tabs on a card; most show one or two."));
        AddRow(everythingRow);

        Section("Card tabs");
        var levels = Flow();
        foreach (var level in CardTabLevels) LevelCell(levels, level);
        _rows!.AddChild(levels);
        var tabsShown = CurrentCardTabLevel() is { Features.Count: > 1 };
        Option("Amounts on tabs", "The block, damage, draws or energy a tab's source gives, on the tab (Ornamental Fan's 4 block). Off: icon only.",
            Settings.ProcNumbers, enabled => Settings.ProcNumbers = enabled, tabsShown ? null : "Moderate or Full card tabs");
        Option("Target marker", "The game's targeting arrow in a tab's corner when it happens only at some targets (aim at the right enemy).",
            Settings.AimMarker, enabled => Settings.AimMarker = enabled, tabsShown ? null : "Moderate or Full card tabs");

        Section("In fights");
        foreach (var setupSwitch in FightSwitches) SwitchRow(setupSwitch);

        Section("Between fights");
        foreach (var (place, name, desc, icon) in ReminderSwitches)
        {
            var key = ChoiceReminders.Key(place, "*");
            SimpleRow(name, desc, FeatureOn("run-reminders") && Settings.TabOwn(key), on =>
            {
                Settings.Tabs[key] = on;
                // Run Reminders is on while any place is.
                SetFeature("run-reminders", ReminderSwitches.Any(other => Settings.TabOwn(ChoiceReminders.Key(other.Place, "*"))));
            }, SetupIcon(icon));
        }
        SwitchRow(new SetupSwitch("Rest sites", "Beside your character at a rest site: the HP resting would leave you with, and what each relic adds.",
            new[] { "rest-preview" }, GameIcons.Heal));

        Section("Chances");
        var chance = Label("50%", size: 18, Text);
        chance.CustomMinimumSize = new Vector2(IconSize, IconSize);
        chance.HorizontalAlignment = HorizontalAlignment.Center;
        SimpleRow("Show chances", "When a roll decides something (which enemy a Lightning orb hits), its chance. Off: only what's certain; a range or ? still marks what isn't settled.",
            Settings.Chances, enabled => { Settings.Chances = enabled; Saved(); }, chance);
    }

    /// <summary>A card tab level: a real card carrying what it shows, its name and what it adds, and a button (marked when it's the current one).</summary>
    private void LevelCell(HFlowContainer flow, CardTabLevel level)
    {
        var (card, tabs) = level.Name switch
        {
            "Minimal" => (Card<BallLightning>(MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Retain), new[] { Tab("retain", Enchantment<Steady>()) }),
            "Moderate" => (Card<DaggerThrow>(), new[] { Tab("proc:Kunai", Relic<Kunai>()) }),
            "Full" => (Card<Claw>(), new[] { Tab("proc:PenNib", Relic<PenNib>()), Tab("proc:OrnamentalFan", Relic<OrnamentalFan>(), number: 4), Tab("shuffle", GameIcons.DiscardPile()) }),
            _ => (Card<Bash>(), Array.Empty<TabSpec>()),
        };
        var chosen = CurrentCardTabLevel() == level;
        var cell = Cell();
        if (card != null)
        {
            var example = new ShownCard(card, GuideCardScale, GuideCellWidth, tabs, opacity: null);
            cell.AddChild(example.Root);
            _guideTicks.Add((_, _) => example.Tick());
        }
        cell.AddChild(Label(level.Name, size: 22, chosen ? Accent : Text));
        cell.AddChild(CellText(level.Summary));
        var choose = MakeButton(chosen ? "Chosen" : "Choose", size: 20);
        choose.Disabled = chosen;
        choose.Pressed += () => { ApplyCardTabs(level); Saved(); Refresh(); };
        cell.AddChild(choose);
        flow.AddChild(cell);
    }

    private void SwitchRow(SetupSwitch setupSwitch) =>
        SimpleRow(setupSwitch.Name, setupSwitch.Description, setupSwitch.Features.All(FeatureOn), on =>
        {
            foreach (var id in setupSwitch.Features) SetFeature(id, on);
        }, SetupIcon(setupSwitch.Icon));

    private static Control SetupIcon(Func<Texture2D?> icon)
    {
        Texture2D? tex;
        try { tex = icon(); } catch { tex = null; }
        return GameIcons.Valid(tex) ? UiKit.Icon(tex, IconSize) : IconSpacer();
    }

    /// <summary>The card tab level the settings are at; null when they're a mix the levels don't make.</summary>
    private CardTabLevel? CurrentCardTabLevel() =>
        CardTabLevels.FirstOrDefault(level => CardTabFeatures.All(id => FeatureOn(id) == level.Features.Contains(id))
            && (!level.Features.Contains("proc-indicators") || Settings.ProcIndirect == level.ProcIndirect));

    private void ApplyCardTabs(CardTabLevel level)
    {
        foreach (var id in CardTabFeatures) SetFeature(id, level.Features.Contains(id));
        Settings.ProcIndirect = level.ProcIndirect;
        Settings.CardTabs = true;
    }

    /// <summary>Everything: every display feature and option to the mod's defaults (Everything(QolSettings)), with the features started.</summary>
    private void ApplyEverything()
    {
        Everything(Settings);
        foreach (var (id, _, _, where) in FeatureRows)
            if (where != FeaturePage.General) _features.EnableLate(id);
        Saved();
    }

    /// <summary>
    /// What Everything sets: every feature that shows something on (the In fights and Outside fights pages), every option on those
    /// pages and every reminder place at its default, chances included. Per-relic switches, the checks and error reports stay.
    /// </summary>
    internal static void Everything(QolSettings settings)
    {
        var defaults = new QolSettings();
        foreach (var (id, _, _, where) in FeatureRows)
            if (where != FeaturePage.General) settings.Features[id] = true;
        settings.ProcNumbers = defaults.ProcNumbers;
        settings.ProcIndirect = defaults.ProcIndirect;
        settings.AimMarker = defaults.AimMarker;
        settings.CardTabs = defaults.CardTabs;
        settings.PlainTabs = defaults.PlainTabs;
        settings.CrossedTabs = defaults.CrossedTabs;
        settings.Chances = defaults.Chances;
        settings.DeathChance = defaults.DeathChance;
        settings.IntentOdds = defaults.IntentOdds;
        settings.EventOdds = defaults.EventOdds;
        settings.EventInspect = defaults.EventInspect;
        settings.Tabs[QolSettings.DrawChanceKey] = true;
        foreach (var (place, _, _, _) in ReminderSwitches) settings.Tabs[ChoiceReminders.Key(place, "*")] = true;
    }
}
