using Godot;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
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
        new("None", "No tags on cards.", new HashSet<string>(), ProcIndirect: false),
        new("Minimal", "What the card picks up: Retain, Sly, played twice.", new HashSet<string> { "card-indicators" }, ProcIndirect: false),
        new("Moderate", "Plus a tag for each relic or power playing it sets off, and a crossed tag when that's bad for you.",
            new HashSet<string> { "card-indicators", "proc-indicators" }, ProcIndirect: false),
        new("Full", "Plus what it sets off through the cards it draws, exhausts or discards, and a tag when a draw reshuffles your discard pile.",
            new HashSet<string>(CardTabFeatures), ProcIndirect: true),
    };

    /// <summary>One switch on the page: the features it turns on and off together (on while all of them are).</summary>
    internal sealed record SetupSwitch(string Name, string Description, string[] Features);

    /// <summary>
    /// Quick setup's groups (decision #186), in the order of the pages: each a switch for every feature it names, on while all of them
    /// are. The card tag levels come first; Chances and Everything after them.
    /// </summary>
    internal static readonly SetupSwitch[] SetupGroups =
    {
        new("On enemies", "On each enemy: the block and the buffs and debuffs its intent's move gives, its next moves when you hover it, and a skull when it dies before it acts if you end your turn now.",
            new[] { "intent-details", "intent-forecast", "death-skull" }),
        new("Beside you", "What ending your turn costs you, after block, beside your HP (and a teammate's when you hover them); the tray of relics that count up to something; tags under your potions for the relics drinking one sets off.",
            new[] { "incoming-damage", "teammate-incoming-damage", "relic-counters", "potion-tabs" }),
        new("Play Preview", "While you hold a card, the damage you'll take, the enemies' intents and the skulls show the turn as it would go after you play it.",
            new[] { "play-preview" }),
        new("Ethereal Fade", "An Ethereal card in your hand is see-through: it exhausts if it's still there at the end of your turn.",
            new[] { Features.EtherealFadeFeature.FeatureId }),
        new("Between fights", "Your relics that change a choice, on the choice: tags on the cards you're offered, icons beside event options, shop prices and the map legend; and the HP resting would leave you with.",
            new[] { Features.RewardTagsFeature.FeatureId, Features.EventRemindersFeature.FeatureId, Features.ShopRemindersFeature.FeatureId, Features.MapRemindersFeature.FeatureId, "rest-preview" }),
    };

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

        Section("Card tags");
        // One row of equal cells, so each Choose button sits on one line under its card.
        var levels = UiKit.Row(separation: 24);
        foreach (var level in CardTabLevels) LevelCell(levels, level);
        _rows!.AddChild(levels);
        var tabsShown = CurrentCardTabLevel() is { Features.Count: > 1 };
        Option("Amounts on tags", "The block, damage, draws or energy a tag's source gives, on the tag (Ornamental Fan's 4 block). Off: icon only.",
            Settings.ProcNumbers, enabled => Settings.ProcNumbers = enabled, tabsShown ? null : "Moderate or Full card tags", icon: TextIcon("4"));
        Option("Target marker", "The game's targeting arrow in a tag's corner when it happens only at some targets (aim at the right enemy).",
            Settings.AimMarker, enabled => Settings.AimMarker = enabled, tabsShown ? null : "Moderate or Full card tags", icon: ArtIcon(GameIcons.TargetArrow()));

        // Each group under the page its features are on.
        foreach (var page in new[] { FeaturePage.Fights, FeaturePage.Between })
        {
            Section(page == FeaturePage.Fights ? "In fights" : "Between fights");
            foreach (var setupSwitch in SetupGroups)
                if (PageOf(FeatureRows.First(featureRow => featureRow.Id == setupSwitch.Features[0]).Where) == page) SwitchRow(setupSwitch);
        }

        Section("Chances");
        var chance = TextIcon("50%");
        SimpleRow("Show chances", "When a roll decides something (which enemy a Lightning orb hits), its chance. Off: only what's certain; a range or ? still marks what isn't settled.",
            Settings.Chances, enabled => { Settings.Chances = enabled; Saved(); }, chance);

        Section("Everything");
        var everythingRow = RowBox();
        var everything = MakeButton("Everything", size: 22);
        everything.TooltipText = "Every feature and option on: the mod's defaults.";
        everything.Pressed += () => { ApplyEverything(); Close(); };
        everythingRow.AddChild(everything);
        everythingRow.AddChild(Wrapped("Every feature and option on, the mod's defaults, and done. Busy turns show four or five tags on a card; most show one or two."));
        AddRow(everythingRow);
    }

    /// <summary>A card tab level: a real card carrying what it shows, its name and what it adds, and a button (marked when it's the current one).</summary>
    private void LevelCell(HBoxContainer row, CardTabLevel level)
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
        cell.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        var choose = MakeButton(chosen ? "Chosen" : "Choose", size: 20);
        choose.Disabled = chosen;
        choose.Pressed += () => { ApplyCardTabs(level); Saved(); Refresh(); };
        cell.AddChild(choose);
        row.AddChild(cell);
    }

    private void SwitchRow(SetupSwitch setupSwitch) =>
        SimpleRow(setupSwitch.Name, setupSwitch.Description, setupSwitch.Features.All(FeatureOn), on =>
        {
            foreach (var id in setupSwitch.Features) SetFeature(id, on);
        }, FeatureIcon(setupSwitch.Features[0]));

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
            if (PageOf(where) != FeaturePage.General) _features.EnableLate(id);
        Saved();
    }

    /// <summary>
    /// What Everything sets: every feature that shows something on (the In fights and Between fights pages), every option on those
    /// pages and every reminder place at its default, chances included. Per-relic switches, the checks and error reports stay.
    /// </summary>
    internal static void Everything(QolSettings settings)
    {
        var defaults = new QolSettings();
        foreach (var (id, _, _, where) in FeatureRows)
            if (PageOf(where) != FeaturePage.General) settings.Features[id] = true;
        settings.ProcNumbers = defaults.ProcNumbers;
        settings.ProcIndirect = defaults.ProcIndirect;
        settings.AimMarker = defaults.AimMarker;
        settings.CardTabs = defaults.CardTabs;
        settings.PlainTabs = defaults.PlainTabs;
        settings.CrossedTabs = defaults.CrossedTabs;
        settings.Chances = defaults.Chances;
        settings.DeathChance = defaults.DeathChance;
        settings.IntentOdds = defaults.IntentOdds;
        settings.MostLikely = defaults.MostLikely;
        settings.EventOdds = defaults.EventOdds;
        settings.EventInspect = defaults.EventInspect;
        settings.Tabs[QolSettings.DrawChanceKey] = true;
        // The places' old group switches (folded into the features by decision #185): on, should a file still have one off.
        foreach (var place in Enum.GetValues<ReminderPlace>()) settings.Tabs[ChoiceReminders.Key(place, "*")] = true;
    }
}
