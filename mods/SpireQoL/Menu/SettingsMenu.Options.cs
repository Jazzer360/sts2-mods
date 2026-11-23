namespace SpireQoL.Menu;

/// <summary>
/// One option's row in the settings menu (decision #187): what it's called and says, whether it's on, and what it needs on. An option
/// with needs sits indented under the switch it depends on and is greyed out, saying what it needs, while one of them is off; one
/// without is a master switch at its section's level. The menu reads both from here, so where a row sits and when it greys out
/// can't drift apart (`SettingsOptionTests`).
/// </summary>
/// <param name="Id">Its id, for the options that need it (apart from every feature's id).</param>
/// <param name="Value">Whether it's on; null for a slider.</param>
/// <param name="Needs">
/// What it needs on: each group a list of switches (a feature by its id, or an option by its id), any one of which will do; every
/// group is needed, and the first one that isn't met is named on the greyed-out row.
/// </param>
internal sealed record MenuOption(string Id, string Name, string Description, Func<QolSettings, bool>? Value, params string[][] Needs)
{
    /// <summary>A master switch: it needs nothing, and sits at its section's level.</summary>
    public bool IsMaster => Needs.Length == 0;
}

public sealed partial class SettingsMenu
{
    /// <summary>A group of switches, any one of which will do.</summary>
    private static string[] Any(params string[] switchIds) => switchIds;

    // ------------------------------------------------------------------ In fights: on your cards

    internal static readonly MenuOption TagsOnCards = new("card-tabs", "Tags on cards",
        "Every tag on cards: on the cards in your hand and on the cards you're offered between fights. Off: none (each feature's other parts stay).",
        settings => settings.CardTabs);

    internal static readonly MenuOption PlainTags = new("plain-tabs", "Plain tags: something extra happens",
        "Which kinds of tag show, on every feature with tags: the cards you hold, the cards you're offered and your potions. A plain tag means playing the card (or taking it, or drinking the potion) does something extra: a proc, a replay, a reshuffle, a chance. Off: only warnings.",
        settings => settings.PlainTabs, Any(TagsOnCards.Id, "potion-tabs"));

    internal static readonly MenuOption CrossedTags = new("crossed-tabs", "Crossed tags: warnings",
        "Which kinds of tag show, on every feature with tags: the cards you hold, the cards you're offered and your potions. A crossed tag, with a red slash, means it might be bad for you: it helps the enemy, hurts you, or gives up a relic's bonus. Off: no warnings.",
        settings => settings.CrossedTabs, Any(TagsOnCards.Id, "potion-tabs"));

    internal static readonly MenuOption TargetMarker = new("aim-marker", "Target marker",
        "The targeting arrow head in a tag's corner when it happens only at some targets. Off: the tag shows without it.",
        settings => settings.AimMarker, Any(TagsOnCards.Id), Any("card-indicators", "proc-indicators"));

    internal static readonly MenuOption AmountsOnProcTags = new("proc-numbers", "Amounts on proc tags",
        "Show the amount on proc tags: block gained, damage dealt, the amount summoned, cards drawn and energy gained. Off: icon only.",
        settings => settings.ProcNumbers, Any(TagsOnCards.Id), Any("proc-indicators"));

    internal static readonly MenuOption IndirectProcs = new("proc-indirect", "Indirect procs",
        "Also count procs set off through the card's other effects (exhausting or discarding other cards, drawing, creating cards) and everything those set off in turn.",
        settings => settings.ProcIndirect, Any(TagsOnCards.Id), Any("proc-indicators"));

    internal static readonly MenuOption EtherealOpacity = new("ethereal-opacity", "Ethereal opacity",
        "How see-through Ethereal cards are (1 = not at all).",
        Value: null, Any(Features.EtherealFadeFeature.FeatureId));

    // ------------------------------------------------------------------ In fights: beside you

    internal static readonly MenuOption TrackerFlashesEarly = new("pulse-one-step-early", "Tracker flashes early",
        "Relic Counters rows also flash two steps before firing (not just one).",
        settings => settings.PulseOneStepEarly, Any("relic-counters"));

    internal static readonly MenuOption TrackerFlashSpeed = new("flash-hz", "Tracker flash speed",
        "Flashes per second on Relic Counters rows about to fire.",
        Value: null, Any("relic-counters"));

    // ------------------------------------------------------------------ In fights: chances

    internal static readonly MenuOption ShowChances = new("chances", "Show chances",
        "Every chance the mod shows, when a roll decides an outcome. Off: only what's certain. A ? or a range on what's shown stays: it marks what isn't settled yet.",
        settings => settings.Chances);

    internal static readonly MenuOption DeathChances = new("death-chance", "Death chances",
        "On the Death Skull, on your skull on Incoming Damage, and on lethal tags, when a roll decides a death (a random Lightning target). Off: certain deaths only (your skull still shows whenever you might die).",
        settings => settings.DeathChance, Any(ShowChances.Id), Any("death-skull", "incoming-damage", "proc-indicators"));

    internal static readonly MenuOption MostLikely = new("most-likely", "Incoming Damage: most likely outcome",
        "When a roll decides how the enemy turn goes (a draw at the end of your turn, a random target), the sum shows the most likely way, with its chance; the skull still shows whenever you might die. Hover the sum for every outcome. Off: the worst case.",
        settings => settings.MostLikely, Any(ShowChances.Id), Any("incoming-damage"));

    internal static readonly MenuOption DrawChanceTag = new("draw-chance", "Draw chance tag",
        "On Escape Plan, Pillage and Scrape: the chance of what their draw gives (the same switch as Draw chance under Proc Tags).",
        settings => settings.TabOwn(QolSettings.DrawChanceKey), Any(ShowChances.Id), Any(TagsOnCards.Id), Any("proc-indicators"));

    internal static readonly MenuOption IntentRandomMoves = new("intent-odds", "Intent Forecast's random moves",
        "After the turns an enemy's rules make certain, the move a random pick can make, each with its chance. Off: the certain turns only.",
        settings => settings.IntentOdds, Any(ShowChances.Id), Any("intent-forecast"));

    // ------------------------------------------------------------------ In fights: multiplayer

    internal static readonly MenuOption MultiplayerPredictions = new("multiplayer-predictions", "Multiplayer predictions (experimental)",
        "Count teammates in predictions: their orbs and end-of-turn effects, their block and Beacon of Hope, and each enemy hit on every player. Still being checked against real co-op games.",
        settings => settings.MultiplayerPredictions);

    internal static readonly MenuOption TeammateHands = new("teammate-hands", "Count teammates' hands",
        "Predictions also use the cards in teammates' hands, which you can't see unless they tell you: Ethereal cards exhausting at end of turn (Feel No Pain block, Charon's Ashes and Forgotten Soul damage), Burn, Decay and Regret. Off: only their hand size counts.",
        settings => settings.TeammateHands, Any(MultiplayerPredictions.Id));

    // ------------------------------------------------------------------ Between fights

    internal static readonly MenuOption InspectEventCards = new("event-inspect", "Inspect event cards",
        "A magnifier beside an event option that gives cards at random from a known pool (a reward, a transform). Click it to see every card it can give, mark the ones you'd like, and get your chance of being offered one.",
        settings => settings.EventInspect, Any(Features.EventRemindersFeature.FeatureId));

    internal static readonly MenuOption EventChance = new("event-odds", "Chance under event icons",
        "Under the icon of a relic that cares about certain cards (an egg, Fresnel Lens, Strike Dummy...) beside an event option, the chance one of its cards comes up. Off: icon only.",
        settings => settings.EventOdds, Any(ShowChances.Id), Any(Features.EventRemindersFeature.FeatureId));

    // ------------------------------------------------------------------ General

    internal static readonly MenuOption SendErrorReports = new("report-errors", "Send error reports",
        "Send crashes and prediction mismatches, with the game state at the time, and a summary of counts per play session (nothing personal), so they can be fixed.",
        settings => settings.ReportErrors);

    internal static readonly MenuOption DevMode = new("dev-mode", "Dev mode",
        "Keeps error reports and session summaries on this computer instead of sending them, and writes the dev dashboard's files. Applies at the next launch.",
        settings => settings.DevMode ?? ModEntry.DevMode);

    /// <summary>Every option row, by its id (the fields above: declared after them, so they're set).</summary>
    internal static readonly IReadOnlyDictionary<string, MenuOption> Options = typeof(SettingsMenu)
        .GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
        .Where(field => field.FieldType == typeof(MenuOption))
        .Select(field => (MenuOption)field.GetValue(null)!)
        .ToDictionary(option => option.Id);
}
