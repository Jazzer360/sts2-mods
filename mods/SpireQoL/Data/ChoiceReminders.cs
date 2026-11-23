using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireQoL.Data;

/// <summary>What choosing an event option does, as far as relics care (read from the option's code).</summary>
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
/// Out-of-combat reminders (docs/FEATURES.md "Run Reminders"): which relics change what a choice
/// outside combat does. Written from each relic's hook bodies in the decompile; every relic with a
/// choice hook has a rule here or a reviewed reason in Diagnostics/RelicChoiceTracking.cs.
/// </summary>
public static class ChoiceReminders
{
    private const ChoiceEffect AnyCard = ChoiceEffect.CardToDeck | ChoiceEffect.CurseToDeck;

    public static readonly IReadOnlyList<ReminderRule> Rules = new ReminderRule[]
    {
        // AfterCardChangedPiles into your deck (transforms included, clones not for Bing Bong).
        new(nameof(BingBong), AnyCard, CrossedOn: ChoiceEffect.CurseToDeck, OnCardReward: true, Expect: AnyCard),
        new(nameof(BookOfFiveRings), AnyCard, OnCardReward: true),
        new(nameof(LuckyFysh), AnyCard, OnCardReward: true, Expect: AnyCard),
        new(nameof(DarkstonePeriapt), ChoiceEffect.CurseToDeck, Expect: ChoiceEffect.CurseToDeck),
        // TryModifyCardRewardAlternatives: the sacrifice button, counting toward a relic.
        // TryModifyCardBeingAddedToDeck: cards joining your deck from an event are upgraded or
        // enchanted when they qualify (rewards and shops already show it on the offered cards).
        new(nameof(MoltenEgg), ChoiceEffect.CardToDeck, ShopIcon: false),
        new(nameof(FrozenEgg), ChoiceEffect.CardToDeck, ShopIcon: false),
        new(nameof(ToxicEgg), ChoiceEffect.CardToDeck, ShopIcon: false),
        new(nameof(FresnelLens), ChoiceEffect.CardToDeck, ShopIcon: false),
        // Cards that are stronger in fights in a way their text doesn't show (Data/CardAffinity): options that give one, or
        // that make the change they look for (Miniature Cannon: an upgrade; Mystic Lighter: an enchantment, Tezcatara's soup).
        // On cards you're offered (rewards, event choices, the shop), a tab on the ones they apply to.
        new(nameof(StrikeDummy), ChoiceEffect.CardToDeck, ShopIcon: false, CardTab: true),
        new(nameof(FakeStrikeDummy), ChoiceEffect.CardToDeck, ShopIcon: false, CardTab: true),
        new(nameof(MiniatureCannon), ChoiceEffect.CardToDeck | ChoiceEffect.UpgradeCard, ShopIcon: false, CardTab: true),
        new(nameof(MysticLighter), ChoiceEffect.CardToDeck | ChoiceEffect.EnchantCard, ShopIcon: false, CardTab: true),
        new(nameof(VitruvianMinion), ChoiceEffect.CardToDeck, ShopIcon: false, CardTab: true),
        // HP loss outside combat (ModifyHpLostAfterOsty; Beating Remnant only caps in combat).
        new(nameof(TungstenRod), ChoiceEffect.HpLoss, Expect: ChoiceEffect.HpLoss),
        // Gold (ModifyGoldGained, AfterGoldGained).
        new(nameof(BowlerHat), ChoiceEffect.GoldGain, Expect: ChoiceEffect.GoldGain),
        new(nameof(DragonFruit), ChoiceEffect.GoldGain, Expect: ChoiceEffect.GoldGain),
        new(nameof(Ectoplasm), ChoiceEffect.GoldGain, CrossedOn: ChoiceEffect.GoldGain),
        // Potions (ShouldProcurePotion; Belt Buckle's Dexterity needs no potions at combat start).
        new(nameof(Sozu), ChoiceEffect.Potion, CrossedOn: ChoiceEffect.Potion),
        new(nameof(BeltBuckle), ChoiceEffect.Potion, CrossedOn: ChoiceEffect.Potion),
        // Shops: buying anything ends Maw Bank's gold per room (AfterItemPurchased sets HasItemBeenBought).
        // On the map, Maw Bank's gold per room ends at the first shop you buy in.
        new(nameof(MawBank), ChoiceEffect.Purchase, CrossedOn: ChoiceEffect.Purchase, Map: PathRoom.Shop, MapCrossed: PathRoom.Shop),
        // Rest-site heals, mimicked ones included (none of these check isMimicked); on the map, they make campfires better.
        new(nameof(RegalPillow), ChoiceEffect.RestHeal, Map: PathRoom.RestSite),
        new(nameof(StoneHumidifier), ChoiceEffect.RestHeal, Map: PathRoom.RestSite),
        new(nameof(DreamCatcher), ChoiceEffect.RestHeal, Map: PathRoom.RestSite),
        new(nameof(TinyMailbox), ChoiceEffect.RestHeal, Map: PathRoom.RestSite),

        // ---- Pathing (decision #65): relics and quest cards that make a kind of room unusually good or bad, beside the map
        // legend's rows. Read from each one's hooks (CoverageWatch.RoomTestingHooks); the rest are in MapReviewed.
        // Elites: TryModifyRewards / AfterRoomEntered / turn hooks / AfterCombatVictory on RoomType.Elite.
        new(nameof(BlackStar), ChoiceEffect.None, Map: PathRoom.Elite),
        new(nameof(WhiteStar), ChoiceEffect.None, Map: PathRoom.Elite),
        new(nameof(SlingOfCourage), ChoiceEffect.None, Map: PathRoom.Elite),
        new(nameof(BoomingConch), ChoiceEffect.None, Map: PathRoom.Elite),
        new(nameof(TormentousCollar), ChoiceEffect.None, Map: PathRoom.Elite),
        new(nameof(SwordOfStone), ChoiceEffect.None, Map: PathRoom.Elite),
        new(nameof(WarHammer), ChoiceEffect.None, Map: PathRoom.Elite),
        // Monsters: an extra card reward (Prayer Wheel), an upgrade every few monster fights (Fishing Rod, AfterCombatEnd).
        new(nameof(PrayerWheel), ChoiceEffect.None, Map: PathRoom.Monster),
        new(nameof(FishingRod), ChoiceEffect.None, Map: PathRoom.Monster),
        // Rest sites: a heal on entering (Eternal Feather), energy next fight (the tea sets), rest-site options (TryModifyRestSiteOptions),
        // Miniature Tent (ShouldDisableRemainingRestSiteOptions).
        new(nameof(EternalFeather), ChoiceEffect.None, Map: PathRoom.RestSite),
        new(nameof(VenerableTeaSet), ChoiceEffect.None, Map: PathRoom.RestSite),
        new(nameof(FakeVenerableTeaSet), ChoiceEffect.None, Map: PathRoom.RestSite),
        new(nameof(Girya), ChoiceEffect.None, Map: PathRoom.RestSite),
        new(nameof(Shovel), ChoiceEffect.None, Map: PathRoom.RestSite),
        new(nameof(MeatCleaver), ChoiceEffect.None, Map: PathRoom.RestSite),
        new(nameof(PaelsGrowth), ChoiceEffect.None, Map: PathRoom.RestSite),
        new(nameof(PumpkinCandle), ChoiceEffect.None, Map: PathRoom.RestSite),
        new(nameof(Wuppy), ChoiceEffect.None, Map: PathRoom.RestSite),
        new(nameof(Byrdpip), ChoiceEffect.None, Map: PathRoom.RestSite),
        new(nameof(MiniatureTent), ChoiceEffect.None, Map: PathRoom.RestSite),
        new(nameof(ByrdonisEgg), ChoiceEffect.None, Map: PathRoom.RestSite),
        // ? rooms: a heal on entering one (Planisphere), Dowsing Rod's quest (4 of them), what a ? can be (ModifyUnknownMapPointRoomTypes:
        // shown only while it changes it, Golden Compass's act and Lantern Key's act 3).
        new(nameof(Planisphere), ChoiceEffect.None, Map: PathRoom.Unknown),
        new(nameof(Dowsing), ChoiceEffect.None, Map: PathRoom.Unknown),
        new(nameof(JuzuBracelet), ChoiceEffect.None, Map: PathRoom.Unknown),
        new(nameof(GoldenCompass), ChoiceEffect.None, Map: PathRoom.Unknown),
        new(nameof(LanternKey), ChoiceEffect.None, Map: PathRoom.Unknown),
        // Shops: a heal on entering (Meal Ticket), buys everything on entering (Lord's Parasol), cheaper prices (ModifyMerchantPrice:
        // Membership Card, The Courier, which also restocks what you buy).
        new(nameof(MealTicket), ChoiceEffect.None, Map: PathRoom.Shop),
        new(nameof(LordsParasol), ChoiceEffect.None, Map: PathRoom.Shop),
        new(nameof(MembershipCard), ChoiceEffect.None, Map: PathRoom.Shop),
        new(nameof(TheCourier), ChoiceEffect.None, Map: PathRoom.Shop),
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

    private static readonly Dictionary<string, ReminderRule> ByRelic = Rules.ToDictionary(reminderRule => reminderRule.Relic);

    public static ReminderRule? For(string relic) => ByRelic.GetValueOrDefault(relic);

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
    /// Game calls ("Type.Method", "Type.Prefix*", constructors as "Type..ctor") and what they mean.
    /// Events only (combat never shows event options).
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
