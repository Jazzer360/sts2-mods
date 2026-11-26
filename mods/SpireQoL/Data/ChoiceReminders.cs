using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;

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
/// card rule; only the map legend's rooms are written here, from each relic's hook bodies. Every relic with a choice hook has a reading that
/// reacts, a map rule here, or a reviewed reason in Diagnostics/RelicChoiceTracking.cs.
/// </summary>
public static class ChoiceReminders
{
    /// <summary>
    /// Pathing (decision #65, docs/PATHING.md): relics and quest cards that make a kind of room unusually good or bad, beside the map legend's rows,
    /// and the rooms where they work against you. Read from each one's hooks (CoverageWatch.RoomTestingHooks); the rest are in MapReviewed.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (PathRoom Rooms, PathRoom Crossed)> MapRules = new Dictionary<string, (PathRoom Rooms, PathRoom Crossed)>
    {
        // Shops: buying anything ends Maw Bank's gold per room (AfterItemPurchased sets HasItemBeenBought): it ends at the first shop you buy in.
        [nameof(MawBank)] = (PathRoom.Shop, PathRoom.Shop),
        // Rest-site heals; they make campfires better.
        [nameof(RegalPillow)] = (PathRoom.RestSite, PathRoom.None),
        [nameof(StoneHumidifier)] = (PathRoom.RestSite, PathRoom.None),
        [nameof(DreamCatcher)] = (PathRoom.RestSite, PathRoom.None),
        [nameof(TinyMailbox)] = (PathRoom.RestSite, PathRoom.None),
        // Elites: TryModifyRewards / AfterRoomEntered / turn hooks / AfterCombatVictory on RoomType.Elite.
        [nameof(BlackStar)] = (PathRoom.Elite, PathRoom.None),
        [nameof(WhiteStar)] = (PathRoom.Elite, PathRoom.None),
        [nameof(SlingOfCourage)] = (PathRoom.Elite, PathRoom.None),
        [nameof(BoomingConch)] = (PathRoom.Elite, PathRoom.None),
        [nameof(TormentousCollar)] = (PathRoom.Elite, PathRoom.None),
        [nameof(SwordOfStone)] = (PathRoom.Elite, PathRoom.None),
        [nameof(WarHammer)] = (PathRoom.Elite, PathRoom.None),
        // Monsters: an extra card reward (Prayer Wheel), an upgrade every few monster fights (Fishing Rod, AfterCombatEnd).
        [nameof(PrayerWheel)] = (PathRoom.Monster, PathRoom.None),
        [nameof(FishingRod)] = (PathRoom.Monster, PathRoom.None),
        // Rest sites: a heal on entering (Eternal Feather), energy next fight (the tea sets), rest-site options (TryModifyRestSiteOptions),
        // Miniature Tent (ShouldDisableRemainingRestSiteOptions).
        [nameof(EternalFeather)] = (PathRoom.RestSite, PathRoom.None),
        [nameof(VenerableTeaSet)] = (PathRoom.RestSite, PathRoom.None),
        [nameof(FakeVenerableTeaSet)] = (PathRoom.RestSite, PathRoom.None),
        [nameof(Girya)] = (PathRoom.RestSite, PathRoom.None),
        [nameof(Shovel)] = (PathRoom.RestSite, PathRoom.None),
        [nameof(MeatCleaver)] = (PathRoom.RestSite, PathRoom.None),
        [nameof(PaelsGrowth)] = (PathRoom.RestSite, PathRoom.None),
        [nameof(PumpkinCandle)] = (PathRoom.RestSite, PathRoom.None),
        [nameof(Wuppy)] = (PathRoom.RestSite, PathRoom.None),
        [nameof(Byrdpip)] = (PathRoom.RestSite, PathRoom.None),
        [nameof(MiniatureTent)] = (PathRoom.RestSite, PathRoom.None),
        [nameof(ByrdonisEgg)] = (PathRoom.RestSite, PathRoom.None),
        // ? rooms: a heal on entering one (Planisphere), Dowsing Rod's quest (4 of them), what a ? can be (ModifyUnknownMapPointRoomTypes:
        // shown only while it changes it, Golden Compass's act and Lantern Key's act 3).
        [nameof(Planisphere)] = (PathRoom.Unknown, PathRoom.None),
        [nameof(Dowsing)] = (PathRoom.Unknown, PathRoom.None),
        [nameof(JuzuBracelet)] = (PathRoom.Unknown, PathRoom.None),
        [nameof(GoldenCompass)] = (PathRoom.Unknown, PathRoom.None),
        [nameof(LanternKey)] = (PathRoom.Unknown, PathRoom.None),
        // Shops: a heal on entering (Meal Ticket), buys everything on entering (Lord's Parasol), cheaper prices (ModifyMerchantPrice:
        // Membership Card, The Courier, which also restocks what you buy).
        [nameof(MealTicket)] = (PathRoom.Shop, PathRoom.None),
        [nameof(LordsParasol)] = (PathRoom.Shop, PathRoom.None),
        [nameof(MembershipCard)] = (PathRoom.Shop, PathRoom.None),
        [nameof(TheCourier)] = (PathRoom.Shop, PathRoom.None),
    };

    /// <summary>
    /// Relics and cards whose hooks act on a kind of room (CoverageWatch.RoomTestingHooks) with no pathing rule, and why: read from
    /// their hook bodies. Checked against the installed game by the catalog tests.
    /// </summary>
    public static readonly Dictionary<string, string> MapReviewed = new()
    {
        // Every fight alike: they make fights easier or better in general, not one kind of room.
        ["BronzeScales"] = "every combat", ["BurningSticks"] = "every combat", ["DataDisk"] = "every combat", ["DivineRight"] = "every combat",
        ["EmberTea"] = "every combat (its first few)", ["GhostSeed"] = "every combat", ["Gorget"] = "every combat", ["Metronome"] = "every combat",
        ["OddlySmoothStone"] = "every combat", ["Permafrost"] = "every combat", ["PhilosophersStone"] = "every combat", ["RedSkull"] = "every combat",
        ["RingingTriangle"] = "every combat", ["SongBow"] = "every combat", ["StoneCalendar"] = "every combat", ["StoneCracker"] = "every combat",
        ["Storyteller"] = "every combat", ["SwordOfJade"] = "every combat", ["ThrowingAxe"] = "every combat", ["Vajra"] = "every combat",
        ["VelvetChoker"] = "every combat", ["LavaLamp"] = "every combat (an upgraded reward after a fight without damage)",
        ["WongosMysteryTicket"] = "every combat counts toward its relic",
        // The boss: every path ends there, so it never changes a route.
        ["AmethystAubergine"] = "boss rewards (every path ends at the boss)", ["LavaRock"] = "boss rewards (every path ends at the boss)",
        ["BathRobes"] = "boss fight (every path ends at the boss)", ["Pantograph"] = "boss fight (every path ends at the boss)",
        // The map shows it.
        ["FurCoat"] = "marks rooms on the map (the map shows the marks)", ["WingedBoots"] = "travel off the path (the map offers it)",
        ["SpoilsMap"] = "changes the map (the map shows it)",
        // Whichever treasure room you enter first has no chest (ShouldGenerateTreasure: TreasureRoomsEntered > 1), however many the map
        // has: no route avoids it, so no mark. (Every path also crosses exactly one: the act map's treasure row, StandardActMap.)
        ["SilverCrucible"] = "the first treasure room you enter has no chest, whichever it is: no route avoids it",
        // In a fight.
        ["TheHunt"] = "a card's effect in a fight",
        // Shop cards changed like every card you get (ModifyMerchantCardCreationResults): rewards and events too, not shops in particular.
        ["MoltenEgg"] = "upgrades shop cards like every card you get", ["FrozenEgg"] = "upgrades shop cards like every card you get",
        ["ToxicEgg"] = "upgrades shop cards like every card you get", ["FresnelLens"] = "enchants shop cards like every card you get",
    };

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ReminderRule?> ByRelic = new();

    /// <summary>A relic's rule: its reading's (decision #359) with its map rule; null when it changes no choice. Kept once the game's cards are known.</summary>
    public static ReminderRule? For(string relic)
    {
        if (ByRelic.TryGetValue(relic, out var known)) return known;
        var reading = RelicReadings.For(relic);
        var hasMap = MapRules.TryGetValue(relic, out var map);
        var rule = reading is not { Reacts: true } && !hasMap ? null : RuleOf(relic, reading, map.Rooms, map.Crossed);
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

    /// <summary>Every relic of the loaded game that has a rule, and every card with a map rule (a quest card: Dowsing).</summary>
    public static IReadOnlyList<ReminderRule> Rules => RelicReadings.LoadedRelics.Concat(MapRules.Keys).Distinct().Order(StringComparer.Ordinal).Select(For).OfType<ReminderRule>().ToList();

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
