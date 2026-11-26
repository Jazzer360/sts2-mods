namespace SpireQoL.Data;

/// <summary>What choosing an event option does, as far as relics and the event check care (read from the option's code as a program: decision #350).</summary>
[Flags]
public enum ChoiceEffect
{
    None = 0,
    /// <summary>A card joins your deck: added, transformed into, or a card reward.</summary>
    CardToDeck = 1,
    /// <summary>A curse joins your deck.</summary>
    CurseToDeck = 2,
    /// <summary>You lose HP (CreatureCmd.Damage).</summary>
    HpLoss = 4,
    /// <summary>You gain gold (directly or as a gold reward).</summary>
    GoldGain = 8,
    /// <summary>You get a potion (directly or as a potion reward).</summary>
    Potion = 16,
    /// <summary>A rest-site heal (events that let you rest: the rest-site relics fire).</summary>
    RestHeal = 32,
    /// <summary>You buy something in a shop (AfterItemPurchased). Shops only: no event call maps to it.</summary>
    Purchase = 64,
    /// <summary>A card in your deck is enchanted (CardCmd.Enchant, an enchantment pick from your deck).</summary>
    EnchantCard = 128,
    /// <summary>A card in your deck is upgraded (CardCmd.Upgrade, an upgrade pick from your deck).</summary>
    UpgradeCard = 256,
    // What else an option can do, read and checked like the rest (decision #350); no reminder looks at these today.
    /// <summary>You heal (CreatureCmd.Heal; a max HP gain heals as much).</summary>
    Heal = 512,
    /// <summary>Your max HP rises (CreatureCmd.GainMaxHp).</summary>
    MaxHpGain = 1024,
    /// <summary>Your max HP falls (CreatureCmd.LoseMaxHp).</summary>
    MaxHpLoss = 2048,
    /// <summary>You lose gold (PlayerCmd.LoseGold).</summary>
    GoldLoss = 4096,
    /// <summary>You lose a potion (PotionCmd.Discard).</summary>
    PotionLoss = 8192,
    /// <summary>A card leaves your deck (CardPileCmd.RemoveFromDeck, a removal pick, a transform's card).</summary>
    CardRemoval = 16384,
    /// <summary>You obtain a relic (RelicCmd.Obtain).</summary>
    RelicGain = 32768,
    /// <summary>You lose a relic (RelicCmd.Remove).</summary>
    RelicLoss = 65536,
    /// <summary>A fight starts (the event's EnterCombatWithoutExitingEvent).</summary>
    Fight = 131072,
}

/// <summary>
/// What each choice effect is (decision #373), in one table: the game commands an event option's program runs that make it (the option reader's meaning
/// of a step: OptionReader.EffectOf), and whether it works against you (you lose something by it). A relic's reaction that makes an effect against you
/// is crossed; one that takes such an effect away, or makes less of it, is plain, and the other way round for the rest (RelicReadings).
/// </summary>
public static class ChoiceEffects
{
    /// <param name="Steps">The game commands that make it ("Type.Method", "Type.Prefix*"; a choice is "CardSelectCmd.Method"); none for a shop's purchase.</param>
    public sealed record Meaning(ChoiceEffect Effect, bool AgainstYou, IReadOnlyList<string> Steps);

    public static readonly IReadOnlyList<Meaning> Table = new Meaning[]
    {
        new(ChoiceEffect.CardToDeck, AgainstYou: false, new[] { "CardPileCmd.Add", "CardCmd.Transform*" }),
        new(ChoiceEffect.CurseToDeck, AgainstYou: true, new[] { "CardPileCmd.AddCurse*" }),
        new(ChoiceEffect.HpLoss, AgainstYou: true, new[] { "CreatureCmd.Damage" }),
        new(ChoiceEffect.GoldGain, AgainstYou: false, new[] { "PlayerCmd.GainGold" }),
        new(ChoiceEffect.Potion, AgainstYou: false, new[] { "PotionCmd.TryToProcure" }),
        new(ChoiceEffect.RestHeal, AgainstYou: false, new[] { "PlayerCmd.MimicRestSiteHeal" }),
        new(ChoiceEffect.Purchase, AgainstYou: false, Array.Empty<string>()),
        new(ChoiceEffect.EnchantCard, AgainstYou: false, new[] { "CardCmd.Enchant*", "CardSelectCmd.FromDeckForEnchantment" }),
        new(ChoiceEffect.UpgradeCard, AgainstYou: false, new[] { "CardCmd.Upgrade*", "CardSelectCmd.FromDeckForUpgrade" }),
        new(ChoiceEffect.Heal, AgainstYou: false, new[] { "CreatureCmd.Heal", "CreatureCmd.GainMaxHp", "PlayerCmd.MimicRestSiteHeal" }),
        new(ChoiceEffect.MaxHpGain, AgainstYou: false, new[] { "CreatureCmd.GainMaxHp" }),
        new(ChoiceEffect.MaxHpLoss, AgainstYou: true, new[] { "CreatureCmd.LoseMaxHp" }),
        new(ChoiceEffect.GoldLoss, AgainstYou: true, new[] { "PlayerCmd.LoseGold" }),
        new(ChoiceEffect.PotionLoss, AgainstYou: true, new[] { "PotionCmd.Discard" }),
        new(ChoiceEffect.CardRemoval, AgainstYou: true, new[] { "CardCmd.Transform*", "CardPileCmd.RemoveFromDeck", "CardSelectCmd.FromDeckForRemoval" }),
        new(ChoiceEffect.RelicGain, AgainstYou: false, new[] { "RelicCmd.Obtain*" }),
        new(ChoiceEffect.RelicLoss, AgainstYou: true, new[] { "RelicCmd.Remove*" }),
        new(ChoiceEffect.Fight, AgainstYou: false, new[] { "EventModel.EnterCombat*" }),
    };

    /// <summary>The effects that work against you.</summary>
    public static readonly ChoiceEffect AgainstYou = Table.Where(meaning => meaning.AgainstYou).Aggregate(ChoiceEffect.None, (all, meaning) => all | meaning.Effect);

    /// <summary>What a step does (None when no reminder or check looks at it): every effect whose steps name it.</summary>
    public static ChoiceEffect Of(string step)
    {
        var effect = ChoiceEffect.None;
        foreach (var meaning in Table)
            foreach (var name in meaning.Steps)
                if (name.EndsWith('*') ? step.StartsWith(name[..^1], StringComparison.Ordinal) : step == name) effect |= meaning.Effect;
        return effect;
    }
}

/// <param name="Relic">The relic's class name.</param>
/// <param name="On">Choices it reacts to: its icon goes next to those event options.</param>
/// <param name="CrossedOn">Choices where it works against you (red slash).</param>
/// <param name="OnCardReward">It shows as a tab on every card a card reward offers when taking that card sets it off (RunReminders.SetOffByTaking).</param>
/// <param name="Expect">Effects that always make it flash when they happen (the check reports an icon
/// whose effect happened without the relic reacting). Relics that react without flashing leave it empty.</param>
/// <param name="ShopIcon">Its icon goes beside shop items it reacts to; false when the shop already shows the effect on the item
/// (the eggs and Fresnel Lens change the offered card itself).</param>
/// <param name="CardTab">It shows as a tab on the cards you're offered that it applies to (decision #62): on card rewards, an
/// event's choice of cards and shop cards, each place with its own switch (the same keys as the other reminders).</param>
/// <param name="Map">Pathing (decision #65, docs/PATHING.md): the kinds of room it makes unusually good or bad; its icon goes beside
/// those rows of the map legend. A quest card in your deck can have one too (Dowsing).</param>
/// <param name="MapCrossed">Of those, the rooms where it works against you (red slash: Maw Bank on shops).</param>
public sealed record ReminderRule(string Relic, ChoiceEffect On, ChoiceEffect CrossedOn = ChoiceEffect.None, bool OnCardReward = false,
    ChoiceEffect Expect = ChoiceEffect.None, bool ShopIcon = true, bool CardTab = false, PathRoom Map = PathRoom.None, PathRoom MapCrossed = PathRoom.None)
{
    /// <summary>Effects a shop item can have: buying it, and a card or a potion joining you.</summary>
    public const ChoiceEffect ShopEffects = ChoiceEffect.Purchase | ChoiceEffect.CardToDeck | ChoiceEffect.Potion;

    /// <summary>Where its reminder shows (each place has its own switch).</summary>
    public bool In(ReminderPlace place) => place switch
    {
        ReminderPlace.Reward => OnCardReward || CardTab,
        ReminderPlace.Event => (On & ~ChoiceEffect.Purchase) != 0 || CardTab,
        ReminderPlace.Shop => (ShopIcon && (On & ShopEffects) != 0) || CardTab,
        ReminderPlace.Map => Map != PathRoom.None,
        _ => false,
    };
}

/// <summary>Where a Run Reminders icon or row shows outside fights.</summary>
public enum ReminderPlace { Reward, Event, Shop, Map }

/// <summary>The kinds of room on the map legend (MapPointType), as flags for a pathing rule.</summary>
[Flags]
public enum PathRoom
{
    None = 0,
    Monster = 1,
    Elite = 2,
    RestSite = 4,
    Unknown = 8,
    Shop = 16,
    Treasure = 32,
}

/// <summary>
/// Out-of-combat reminders (docs/FEATURES.md "Run Reminders"): which relics change what a choice outside combat does. A relic's choices are
/// read from its own hook code (Data/RelicReadings, decision #359): what it reacts to, plain or crossed, what the event check expects of it, its
/// card rule; and the map legend's rooms of a relic or a quest card too (Data/RoomReadings, decision #374). Every relic with a choice hook has a
/// reading that reacts, a map row, or a reviewed reason in Diagnostics/RelicChoiceTracking.cs.
/// </summary>
public static class ChoiceReminders
{
    /// <summary>
    /// Models whose room reading (Data/RoomReadings, decision #374) gives a row the map shouldn't show, and why: read from their hook bodies. A model
    /// whose reading gives no row needs no reason (the reading says why: every fight, the boss's, what the map shows).
    /// </summary>
    public static readonly Dictionary<string, string> MapReviewed = new()
    {
        // Whichever treasure room you enter first has no chest (ShouldGenerateTreasure: TreasureRoomsEntered > 1), however many the map
        // has: no route avoids it, so no mark (decision #115). (Every path also crosses exactly one: the act map's treasure row, StandardActMap.)
        ["SilverCrucible"] = "the first treasure room you enter has no chest, whichever it is: no route avoids it",
    };

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ReminderRule?> ByRelic = new();

    /// <summary>
    /// A relic's or a card's rule: its choice reading's (decision #359) with its room reading's rows (decision #374; none when they're reviewed as not
    /// shown, MapReviewed); null when it changes no choice. Kept once the game's cards are known.
    /// </summary>
    public static ReminderRule? For(string relic)
    {
        if (ByRelic.TryGetValue(relic, out var known)) return known;
        var reading = RelicReadings.For(relic);
        var rooms = MapReviewed.ContainsKey(relic) ? null : RoomReadings.For(relic);
        var (map, mapCrossed) = rooms == null ? (PathRoom.None, PathRoom.None) : (rooms.Rooms, rooms.Crossed);
        var rule = reading is not { Reacts: true } && map == PathRoom.None ? null : RuleOf(relic, reading, map, mapCrossed);
        if (RelicReadings.Kept(relic)) ByRelic.TryAdd(relic, rule);
        return rule;
    }

    /// <summary>The rule a reading and a map rule make: where its icons show (a boost's card tab; the shop's own cards show a modifier's change).</summary>
    public static ReminderRule RuleOf(string relic, RelicReading? reading, PathRoom map, PathRoom mapCrossed) => new(relic,
        reading?.On ?? ChoiceEffect.None, reading?.CrossedOn ?? ChoiceEffect.None,
        OnCardReward: reading != null && (reading.Joining & ChoiceEffect.CardToDeck) != 0,
        Expect: reading?.Expect ?? ChoiceEffect.None,
        ShopIcon: reading is not { ChangesShopCards: true } && reading?.Card is not { Kind: AffinityKind.Boosts },
        CardTab: reading?.Card is { Kind: AffinityKind.Boosts }, Map: map, MapCrossed: mapCrossed);

    /// <summary>Every relic of the loaded game that has a rule, and every card with a map row (a quest card: Dowsing).</summary>
    public static IReadOnlyList<ReminderRule> Rules => RelicReadings.LoadedRelics.Concat(RoomReadings.LoadedCardsWithRooms).Distinct().Order(StringComparer.Ordinal)
        .Select(For).OfType<ReminderRule>().ToList();

    /// <summary>The settings key for a relic's reminder at one place ("reminder-shop:MawBank").</summary>
    public static string Key(ReminderPlace place, string relic) => place switch
    {
        ReminderPlace.Reward => "reminder-reward:",
        ReminderPlace.Event => "reminder-event:",
        ReminderPlace.Map => "reminder-map:",
        _ => "reminder-shop:",
    } + relic;

    /// <summary>Recorded with the calls when the option makes a curse card (ModelDb.Card of a curse, the curse pool).</summary>
    public const string CurseMade = "(curse card)";

    /// <summary>
    /// Game calls ("Type.Method", "Type.Prefix*", constructors as "Type..ctor") and what they mean, for the flag-level scan of an option's
    /// calls. What an option does is its program reading's (OptionReader.Steps, decision #350); the scan is the check that the reading
    /// misses nothing it finds (OptionProgramTests). Events only (combat never shows event options).
    /// </summary>
    public static readonly IReadOnlyList<(string Call, ChoiceEffect Effect)> Calls = new[]
    {
        ("CardPileCmd.Add", ChoiceEffect.CardToDeck),
        ("CardCmd.Transform*", ChoiceEffect.CardToDeck),
        ("CardReward..ctor", ChoiceEffect.CardToDeck),
        ("CardPileCmd.AddCurse*", ChoiceEffect.CurseToDeck),
        (CurseMade, ChoiceEffect.CurseToDeck),
        ("CreatureCmd.Damage", ChoiceEffect.HpLoss),
        ("PlayerCmd.GainGold", ChoiceEffect.GoldGain),
        ("GoldReward..ctor", ChoiceEffect.GoldGain),
        ("PotionCmd.TryToProcure", ChoiceEffect.Potion),
        ("PotionReward..ctor", ChoiceEffect.Potion),
        ("PlayerCmd.MimicRestSiteHeal", ChoiceEffect.RestHeal),
        ("CardCmd.Enchant*", ChoiceEffect.EnchantCard),
        ("CardSelectCmd.FromDeckForEnchantment", ChoiceEffect.EnchantCard),
        ("CardCmd.Upgrade*", ChoiceEffect.UpgradeCard),
        ("CardSelectCmd.FromDeckForUpgrade", ChoiceEffect.UpgradeCard),
    };

    public static ChoiceEffect EffectsOf(IEnumerable<string> calls)
    {
        var choiceEffect = ChoiceEffect.None;
        foreach (var otherCall in calls)
        {
            foreach (var (call, effect) in Calls)
            {
                if (call.EndsWith('*') ? otherCall.StartsWith(call[..^1], StringComparison.Ordinal) : otherCall == call) choiceEffect |= effect;
            }
        }
        return choiceEffect;
    }
}
