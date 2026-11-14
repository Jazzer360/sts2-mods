namespace SpireQoL.Diagnostics;

/// <summary>
/// Goal 3 (docs/ROADMAP.md): every relic that changes a choice outside combat (it overrides a hook in
/// Diagnostics/ChoiceHooks.txt) has a Run Reminders rule (Data/ChoiceReminders.cs) or a reviewed
/// reason here, written from its hook bodies: "none: why nothing needs showing" or "gap: what should
/// be shown". scripts/coverage-ledger.mjs lists the relics missing both, and docs/RELICS.md shows it.
/// </summary>
internal static class RelicChoiceTracking
{
    public static readonly Dictionary<string, string> Reasons = new()
    {
        // ---- the game already shows it on the choice
        ["DingyRug"] = "none: changes which cards a reward offers (you see the offered cards)",
        ["PrismaticGem"] = "none: changes which cards a reward offers (you see the offered cards)",
        ["Glitter"] = "none: enchants offered reward cards (shown on the offered cards)",
        ["WingCharm"] = "none: enchants an offered reward card (shown on the offered card)",
        ["SilkenTress"] = "none: enchants offered reward cards (shown on the offered cards)",
        ["LavaLamp"] = "none: upgrades offered reward cards after a fight without damage (shown on the offered cards)",
        ["LastingCandy"] = "none: an extra card on every other reward (shown on the reward; its counter shows the count)",
        ["SilverCrucible"] = "none: upgrades offered reward cards (shown on them) and skips the first treasure chest (its counter shows uses left)",
        ["RegalPillow"] = "none: rest heals more (the game adds it to the rest option's text); events that rest get its icon (ChoiceReminders)",
        ["StoneHumidifier"] = "none: max HP when you rest (the game adds it to the rest option's text); events that rest get its icon",
        ["DreamCatcher"] = "none: a card reward when you rest (the game adds it to the rest option's text); events that rest get its icon",
        ["TinyMailbox"] = "none: potions when you rest (the game adds it to the rest option's text); events that rest get its icon",
        ["Byrdpip"] = "none: adds a rest-site option (shown as the option)",
        ["Girya"] = "none: adds a rest-site option (shown as the option)",
        ["MeatCleaver"] = "none: adds a rest-site option (shown as the option)",
        ["PaelsGrowth"] = "none: adds a rest-site option (shown as the option)",
        ["PumpkinCandle"] = "none: adds a rest-site option (shown as the option)",
        ["Shovel"] = "none: adds a rest-site option (shown as the option)",
        ["Wuppy"] = "none: adds a rest-site option (shown as the option)",
        ["MiniatureTent"] = "none: keeps the other rest-site options open after one (the options stay clickable)",
        ["MembershipCard"] = "none: cheaper shop prices (the shop shows the prices)",
        ["TheCourier"] = "none: cheaper shop prices and restocked items (the shop shows them)",
        ["AmethystAubergine"] = "none: adds a reward (shown on the rewards screen)",
        ["BlackStar"] = "none: adds a relic reward after elites (shown on the rewards screen)",
        ["Driftwood"] = "none: changes the rewards (shown on the rewards screen)",
        ["LavaRock"] = "none: adds a relic reward (shown on the rewards screen)",
        ["PrayerWheel"] = "none: adds a card reward (shown on the rewards screen)",
        ["WhiteStar"] = "none: adds a reward (shown on the rewards screen)",
        ["WongosMysteryTicket"] = "none: adds rewards (shown on the rewards screen)",
        ["WhiteBeastStatue"] = "none: forces a potion reward (shown on the rewards screen)",
        ["FurCoat"] = "none: marks rooms on the map (the map shows the marks)",
        ["GoldenCompass"] = "none: changes the map (the map shows it)",
        ["JuzuBracelet"] = "none: ? rooms are never fights (no choice it changes: every ? is the same)",
        ["WingedBoots"] = "none: lets you travel off the path (the map offers it)",

        // ---- combat only
        ["BeatingRemnant"] = "none: caps HP loss per turn only in combat (no effect on event damage)",
        ["TheBoot"] = "none: raises your attacks' damage on enemies (combat only)",

        // ---- gaps
        ["MawBank"] = "gap: buying anything in a shop turns off its gold per room: a crossed icon on shop items",
    };
}
