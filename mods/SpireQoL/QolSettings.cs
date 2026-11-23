using Sts2Mods.Core.Ui;

namespace SpireQoL;

/// <summary>
/// Everything the player can configure. Saved as JSON in mods/SpireQoL/SpireQoL.settings.cfg.
/// Hand-editable; unknown or missing keys fall back to defaults.
/// </summary>
public sealed class QolSettings
{
    /// <summary>Counts the settings menu's changes (not saved): part of the board probe, so a changed setting is redrawn (decision #130).</summary>
    internal static int Revision;

    /// <summary>Feature id → enabled. Missing ids use the feature's default.</summary>
    public Dictionary<string, bool> Features { get; set; } = new();

    /// <summary>
    /// Relic Counters tray: relic or power class name (e.g. "PenNib", "OrbitPower") → shown.
    /// Missing = the default: shown, except turn timers (Horn Cleat, Happy Flower...), which are off.
    /// On-card tabs for relics are separate (Tabs, "proc:PenNib").
    /// </summary>
    public Dictionary<string, bool> Relics { get; set; } = new();

    /// <summary>Widget id → placement. Filled with defaults on first run.</summary>
    public Dictionary<string, WidgetLayout> Widgets { get; set; } = new();

    public const int CurrentLayoutVersion = 4;

    /// <summary>Bumped when default widget positions change; older saved positions get reset.</summary>
    public int LayoutVersion { get; set; }

    public const int CurrentSettingsVersion = 5;

    /// <summary>
    /// Features split out of another (decision #185): each new id and the id it came out of. Ethereal Fade came out of Keyword Tags
    /// (card-indicators); Reward Tags, Event Reminders, Shop Reminders and Map Reminders out of the old run-reminders.
    /// </summary>
    public static readonly IReadOnlyList<(string Id, string From)> SplitFeatures = new[]
    {
        (SpireQoL.Features.EtherealFadeFeature.FeatureId, "card-indicators"),
        (SpireQoL.Features.RewardTagsFeature.FeatureId, RetiredRunReminders),
        (SpireQoL.Features.EventRemindersFeature.FeatureId, RetiredRunReminders),
        (SpireQoL.Features.ShopRemindersFeature.FeatureId, RetiredRunReminders),
        (SpireQoL.Features.MapRemindersFeature.FeatureId, RetiredRunReminders),
    };

    /// <summary>The feature the four reminders outside fights were, before decision #185: no feature has the id now.</summary>
    public const string RetiredRunReminders = "run-reminders";

    /// <summary>
    /// The first load after a split (decision #185): a feature with no saved switch starts as the one it came out of was (the old one's
    /// saved switch; none saved, it keeps its default). The retired id's switch is dropped once read, so the file lists only features.
    /// Run every load, before the features are applied: a feature already saved keeps its own switch.
    /// </summary>
    public static void CarryOverSplitFeatures(Dictionary<string, bool> features)
    {
        foreach (var (id, from) in SplitFeatures)
            if (!features.ContainsKey(id) && features.TryGetValue(from, out var on)) features[id] = on;
        features.Remove(RetiredRunReminders);
    }

    /// <summary>
    /// Decision #185: each reminder place has its own feature switch now, in place of its group switch ("reminder-event:*": every relic's
    /// reminder there). A group switched off becomes each relic's switch there off (what you saw stays as it was), and the group key goes.
    /// </summary>
    /// <param name="typesAt">The relics and cards with a reminder at a place (the settings menu's rows: SettingsCatalog.Sources).</param>
    public void FoldReminderPlaceSwitches(Func<Data.ReminderPlace, IEnumerable<string>> typesAt)
    {
        foreach (var place in Enum.GetValues<Data.ReminderPlace>())
        {
            if (!Tabs.Remove(Data.ChoiceReminders.Key(place, "*"), out var on) || on) continue;
            foreach (var type in typesAt(place)) Tabs[Data.ChoiceReminders.Key(place, type)] = false;
        }
    }

    /// <summary>Bumped when a setting's meaning changes; older files are migrated once (ModEntry.Migrate).</summary>
    public int SettingsVersion { get; set; }

    /// <summary>
    /// Send crashes and audit mismatches (with the game state at the time; nothing personal) to the
    /// mod's Sentry project, where they become GitHub issues. See README "Error reporting".
    /// </summary>
    public bool ReportErrors { get; set; } = true;

    /// <summary>
    /// Quick setup (the settings menu's first page, decision #147) was shown at the first launch and closed: it doesn't open itself again. Hidden: not in the settings menu. A settings file from before it counts as done (SettingsVersion 3).
    /// </summary>
    public bool SetupDone { get; set; }

    /// <summary>
    /// Developer mode (decision #108; on the settings menu's General page only in a developer's game, decision #186): null follows the marker file the developer's own builds install
    /// (SpireQoL.dev beside the DLL, never in a release zip); true or false overrides it. On: reports and session summaries are
    /// tagged dev, and the session metrics leave them out. See docs/DEVELOPMENT.md "Dev mode".
    /// </summary>
    public bool? DevMode { get; set; }

    /// <summary>
    /// Dev mode: where the dev dashboard's files go (environment variables expanded); none: %USERPROFILE%\SpireQoL-dev. Not under
    /// AppData or Program Files: the browser won't open folders there. See docs/DEVELOPMENT.md "Dev dashboard".
    /// </summary>
    public string? DevFolder { get; set; }

    public string UnlockLayoutKey { get; set; } = "F8";
    public string HideAllKey { get; set; } = "F9";
    public string SettingsKey { get; set; } = "F10";

    /// <summary>
    /// On-card tab kind → shown. Keys are tab keys ("retain", "sly", "replay", "shuffle",
    /// "relic:PenNib", "proc:FeelNoPainPower", "double-play:BurstPower"...) or a group switch
    /// ("relic:*", "proc:*", "double-play:*"). A tab shows only if both its own key and its group
    /// are on, so a group set to false hides the whole group. Missing = shown.
    /// Keys are added here (as shown) the first time a tab of that kind appears, so the file lists
    /// everything that can be turned off.
    /// </summary>
    public Dictionary<string, bool> Tabs { get; set; } = new();

    /// <summary>
    /// Multiplayer (experimental, decision #24): teammates are simulated as full players. Off: they're left
    /// out of every prediction (an enemy a teammate would finish still counts as attacking you).
    /// </summary>
    public bool MultiplayerPredictions { get; set; } = true;

    /// <summary>
    /// Multiplayer: predictions also use the cards in teammates' hands (decision #38), as if they'd told you: their
    /// Ethereal cards exhausting (Feel No Pain, Charon's Ashes, Forgotten Soul), Burn, Decay, Regret. Off: only what
    /// you can see (their hand size). The checks always use them.
    /// </summary>
    public bool TeammateHands { get; set; }

    /// <summary>
    /// Chances (decision #122): every chance the mod shows, all at once. Off: only what's certain (a chance that's hidden isn't
    /// shown at all; a `?` or a range on what is shown stays, as it marks what isn't settled).
    /// </summary>
    public bool Chances { get; set; } = true;

    /// <summary>Death Skull, your skull on Incoming Damage, lethal tabs: also show the chance when a roll decides a death.</summary>
    public bool DeathChance { get; set; } = true;

    /// <summary>
    /// Incoming Damage shows the most likely way the enemy turn goes, with its chance (decision #155). Off, or with Chances off: the
    /// worst case, as before. Hovering it lists every outcome either way.
    /// </summary>
    public bool MostLikely { get; set; } = true;

    /// <summary>Intent Forecast: the random pick after the certain turns, each move with its chance (off: the certain turns only).</summary>
    public bool IntentOdds { get; set; } = true;

    /// <summary>Every tab on cards (decision #122): off hides them all; the kinds and each tab's own switch are under it.</summary>
    public bool CardTabs { get; set; } = true;

    /// <summary>Card tabs by kind (decision #122): plain ones (something extra happens), crossed ones (warnings).</summary>
    public bool PlainTabs { get; set; } = true;
    public bool CrossedTabs { get; set; } = true;

    /// <summary>The aim marker on tabs that happen only at some targets (off: the tab shows without it).</summary>
    public bool AimMarker { get; set; } = true;

    [System.Text.Json.Serialization.JsonIgnore] public bool ShowDeathChance => Chances && DeathChance;
    [System.Text.Json.Serialization.JsonIgnore] public bool ShowIntentOdds => Chances && IntentOdds;
    [System.Text.Json.Serialization.JsonIgnore] public bool ShowMostLikely => Chances && MostLikely;
    [System.Text.Json.Serialization.JsonIgnore] public bool ShowEventOdds => Chances && EventOdds;

    /// <summary>The draw chance tab's key: a chance, so it follows <see cref="Chances"/> too.</summary>
    public const string DrawChanceKey = "drawn";

    /// <summary>
    /// A tab as it's shown, after the settings by kind: null when its kind is off; without its aim marker when that's off.
    /// The key's own switch is <see cref="TabEnabled"/>.
    /// </summary>
    public Cards.TabSpec? Shown(Cards.TabSpec tab)
    {
        if (!CardTabs || (tab.Crossed ? !CrossedTabs : !PlainTabs)) return null;
        if (tab.Key == DrawChanceKey && !Chances) return null;
        return tab.Aimed && !AimMarker ? tab with { Aimed = false } : tab;
    }

    /// <summary>Show the amount on block / damage / summon proc tabs (off = icon only).</summary>
    public bool ProcNumbers { get; set; } = true;

    /// <summary>Count procs one step removed (a card that exhausts or discards other cards, or draws into a shuffle).</summary>
    public bool ProcIndirect { get; set; } = true;

    /// <summary>Event Reminders: the chance under a card relic's icon beside an event option (off = icon only).</summary>
    public bool EventOdds { get; set; } = true;

    /// <summary>Event Reminders: the Inspect button beside event options whose cards are drawn at random from a known pool.</summary>
    public bool EventInspect { get; set; } = true;

    // Ethereal Fade: how see-through an Ethereal card is
    public float EtherealOpacity { get; set; } = 0.8f;

    // Replaced by Tabs in 0.19.0; read once from older files, then dropped (see ModEntry.Migrate).
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? ShowDoublePlay { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? DimBlockedCards { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? ShowInnateEternalBadges { get; set; }

    // Relic Counters
    // Replaced by per-relic Relics entries in 0.23.0 (turn timers default off); read once, then dropped.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? ShowTurnTimers { get; set; }
    public bool PulseOneStepEarly { get; set; }
    public float FlashHz { get; set; } = 2.5f;


    /// <summary>Is this relic or power shown in the Relic Counters tray?</summary>
    public bool TrackerEnabled(string typeName, bool defaultOn = true) => Relics.TryGetValue(typeName, out var on) ? on : defaultOn;

    /// <summary>A tab's own switch, ignoring its group (for the settings menu).</summary>
    public bool TabOwn(string key) => Tabs.TryGetValue(key, out var on) ? on : TabDefault(key);

    /// <summary>A tab's switch before you've touched it: on, apart from the tabs that would be on every card (decision #153).</summary>
    public static bool TabDefault(string key) => !Menu.SettingsCatalog.TabsOffByDefault.Contains(key);

    /// <summary>Set when a new tab key or group was added to <see cref="Tabs"/> and should be saved.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool TabsDirty { get; set; }

    public bool TabEnabled(string key)
    {
        var group = GroupOf(key);
        if (group != null && !Tabs.ContainsKey(group)) { Tabs[group] = true; TabsDirty = true; }
        if (!Tabs.TryGetValue(key, out var on)) { on = TabDefault(key); Tabs[key] = on; TabsDirty = true; }
        if (!on) return false;
        return group == null || Tabs[group];
    }


    /// <summary>
    /// A tab key's group switch ("proc:Thorns" → "proc:*"; null for none), worked out once per key: every tab shown asks every frame,
    /// and building it made two strings each time (a dev review). The keys are the game's sources: a set that stops growing.
    /// </summary>
    private static string? GroupOf(string key)
    {
        if (GroupKeys.TryGetValue(key, out var group)) return group;
        var colon = key.IndexOf(':');
        group = colon > 0 ? key[..colon] + ":*" : null;
        GroupKeys[key] = group;
        return group;
    }

    private static readonly Dictionary<string, string?> GroupKeys = new();

    public WidgetLayout Widget(string id, WidgetLayout fallback)
    {
        if (!Widgets.TryGetValue(id, out var layout))
        {
            layout = fallback.Clone();
            Widgets[id] = layout;
        }
        return layout;
    }
}
