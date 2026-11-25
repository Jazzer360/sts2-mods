using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;

namespace SpireQoL.Menu;

public enum SourceGroup { Relic, YourPower, EnemyPower, Card }

/// <summary>
/// One relic or power the mod knows about, as a row in the settings menu.
/// </summary>
/// <param name="Type">The game's class name (e.g. "PenNib", "FeelNoPainPower").</param>
/// <param name="Tag">What its on-card tab marks and shows; null = it has no tab of its own.</param>
/// <param name="Tracker">What its Relic Counters row shows; null = no tray row.</param>
/// <param name="TagPrefix">"proc:" (Proc Indicators) or "double-play:" (Card Indicators).</param>
/// <param name="TrackerDefault">Tray row on by default (turn timers are off).</param>
/// <param name="TagDefault">
/// Its tab on by default. Off for a power that acts on every card you play, whatever the card, and never runs out (Afterimage,
/// Serpent Form): its tab would be on every card, saying nothing about this one (decision #153). A tab that's on every card
/// until it's used up (Vigor) stays on.
/// </param>
/// <param name="Reward">Outside fights (Run Reminders, Data/ChoiceReminders): what its tab on cards a card reward offers marks; null = none.</param>
/// <param name="Event">What its icon beside event options marks; null = none.</param>
/// <param name="Shop">What its icon beside shop prices marks; null = none.</param>
/// <param name="Map">What its icon beside the map legend marks (pathing, decision #65); null = none.</param>
/// <param name="Shows">
/// Which of what the simulator works out about its bonus its tab shows (decision #244: display configuration, never a gate on the simulation):
/// a play that sets a payout up, gives it up, or makes it smaller (Sim.TabShows). Both directions of a payout unless it says otherwise.
/// </param>
public sealed record SourceEntry(string Type, SourceGroup Group, string? Tag, string? Tracker = null,
    string TagPrefix = "proc:", bool TrackerDefault = true, string? Reward = null, string? Event = null, string? Shop = null, string? Map = null,
    bool TagDefault = true, Sim.TabShows Shows = Sim.TabShows.Default)
{
    public string? TagKey => Tag == null ? null : TagPrefix + Type;

    /// <summary>In fights: an on-card (or potion) tab or a tracker row.</summary>
    public bool InFights => Tag != null || Tracker != null;

    /// <summary>Outside fights: a Run Reminders icon or row somewhere.</summary>
    public bool OutsideFights => Reward != null || Event != null || Shop != null || Map != null;

    /// <summary>What its reminder marks at one place; null = it shows nothing there.</summary>
    public string? Reminder(SpireQoL.Data.ReminderPlace place) => place switch
    {
        SpireQoL.Data.ReminderPlace.Reward => Reward,
        SpireQoL.Data.ReminderPlace.Event => Event,
        SpireQoL.Data.ReminderPlace.Map => Map,
        _ => Shop,
    };
}

/// <summary>An on-card tab that isn't tied to one relic or power, or a family of them (TabGroups).</summary>
/// <param name="Feature">
/// The feature that shows these tags: a kind's row sits under that feature's switch (decision #186); a family's switch, on Tags by
/// source, greys out while it's off (decision #187).
/// </param>
/// <param name="Chance">A chance: it shows only with Show chances on too (QolSettings.Shown), and greys out while that's off.</param>
/// <param name="OnCards">
/// Tags on cards (QolSettings.Shown hides them while Tags on cards is off); false for the potion tags, which hang under potions.
/// </param>
public sealed record TabEntry(string Key, string Name, string Description, string Feature = "proc-indicators", bool Chance = false, bool OnCards = true);

/// <summary>
/// Everything the settings menu lists. Must cover every rule in the card predictions (Combat/CardPredictions), every
/// double-play source, and every RelicSpecs / PowerSpecs entry (SettingsCatalog.Check logs any
/// tab key seen in play that isn't here, and the menu lists such keys under "Other").
/// Text follows docs/FEATURES.md "Proc Indicators" and "Relic Counters".
/// </summary>
public static class SettingsCatalog
{
    public static readonly IReadOnlyList<TabEntry> CardTabs = new[]
    {
        new TabEntry("retain", "Retain", "Cards that stay in your hand at end of turn (permanently or this turn only). Steady icon.", Feature: "card-indicators"),
        new TabEntry("sly", "Sly", "Cards with Sly (permanently or this turn only). Master Planner icon.", Feature: "card-indicators"),
        new TabEntry("replay", "Replay", "Cards that replay themselves (Hidden Gem, Transfigure, Sword Sage, enchantments): a \"2×\" tag with the total plays.", Feature: "card-indicators"),
        new TabEntry("lethal", "Lethal", "Skull on cards that kill an enemy before it acts, or make it surely more likely than ending your turn now: the card's hits, orbs it evokes, procs it sets off, and your end of turn (Juggernaut, orb passives, poison...). When a roll decides it, the tag carries the chance (the Death chances setting, under Show chances; a range while the outcomes are still being explored). The aim marker (the targeting arrow head in the corner) means only some targets give it."),
        new TabEntry("drawn", "Draw chance", "Cards whose effect depends on what they draw: Escape Plan shows the chance it draws a Skill (its block); Pillage the most likely number of Attacks it draws, Scrape the most likely number of cards it draws that cost 0, each with its chance (ties go to the higher number). Worked out from your draw pile, never its order.", Chance: true),
        new TabEntry("shuffle", "Reshuffle", "Discard pile icon on cards whose draw would shuffle your discard pile back into your draw pile.", Feature: "shuffle-warning"),
    };

    /// <summary>
    /// Family switches: off hides the whole family, whatever each source's own switch. Each heads its sources on Tags by source, the
    /// same switch at the top of every list it covers (decision #187).
    /// </summary>
    public static readonly IReadOnlyList<TabEntry> TabGroups = new[]
    {
        new TabEntry("proc:*", "All proc tags", "Every tag for a relic or power a card sets off. Off: none, whatever each one's own switch."),
        new TabEntry("return:*", "All return tags", "Tags on cards whose play brings a card back to your hand (Make It So, Right Hand Hand, Whistling Arrows). Off: none, whatever each one's own switch."),
        new TabEntry("potion:*", "All potion tags", "Tags under your potions (Reptile Trinket, Belt Buckle, White Beast Statue). Off: none, whatever each one's own switch.", Feature: "potion-tabs", OnCards: false),
        new TabEntry("free:*", "All free-card tags", "Tags on cards that would use up a free card (Tune, Unrelenting, Pounce, Synthesis): the energy saved, or crossed when it's wasted. Off: none, whatever each one's own switch."),
        new TabEntry("double-play:*", "All double-play tags", "Tags on cards that will play more than once (Echo Form, Burst, Duplication, Throwing Axe, Tag Team...). Off: none, whatever each one's own switch.", Feature: "card-indicators"),
    };

    /// <summary>The family a source's tag is in (its TagPrefix's group switch); null for a source with no tag.</summary>
    public static TabEntry? FamilyOf(SourceEntry sourceEntry) =>
        sourceEntry.TagKey == null ? null : TabGroups.FirstOrDefault(family => family.Key == sourceEntry.TagPrefix + "*");

    /// <summary>Marks a crossed tab (helps the enemy, hurts you, or gives up a relic); the menu draws the red slash.</summary>
    public const string CrossedPrefix = "crossed: ";
    private const string Crossed = CrossedPrefix;

    /// <summary>The tab keys that are off until you turn them on (SourceEntry.TagDefault, decision #153).</summary>
    public static IReadOnlySet<string> TabsOffByDefault => _tabsOffByDefault ??= Sources.Where(source => !source.TagDefault && source.TagKey != null).Select(source => source.TagKey!).ToHashSet();
    private static HashSet<string>? _tabsOffByDefault;

    /// <summary>What a class's Proc Tags row shows of its bonus (SourceEntry.Shows, decision #244); the default for a class with no such row.</summary>
    public static Sim.TabShows ShowsOf(string type) => (_shows ??= Sources.Where(source => source.TagPrefix == "proc:" && source.Tag != null)
        .ToDictionary(source => source.Type, source => source.Shows, StringComparer.Ordinal)).TryGetValue(type, out var shows) ? shows : Sim.TabShows.Default;
    private static Dictionary<string, Sim.TabShows>? _shows;

    public static readonly IReadOnlyList<SourceEntry> Sources = new SourceEntry[]
    {
        // ------------------------------------------------------------------ relics
        new(nameof(PenNib), SourceGroup.Relic, "Attacks while it's ready: this one deals double", "Attacks played, toward the doubled 10th"),
        new(nameof(ArtOfWar), SourceGroup.Relic, Crossed + "your first attack this turn (gives up its energy next turn)"),
        new(nameof(RippleBasin), SourceGroup.Relic, Crossed + "your first attack this turn (gives up its end-of-turn block)"),
        new(nameof(PaelsTears), SourceGroup.Relic, Crossed + "cards that leave you with no energy once played (gives up next turn's carry-over)"),
        new(nameof(PaelsEye), SourceGroup.Relic, Crossed + "any card while armed and nothing played yet (gives up the extra turn)"),
        new(nameof(Vambrace), SourceGroup.Relic, "Cards that gain block, while unused this combat"),
        new("PaelsLegion", SourceGroup.Relic, "Cards that gain block, while it's off cooldown (double block)"),
        new(nameof(BurningSticks), SourceGroup.Relic, "Skills that exhaust themselves, while unused this combat"),
        new(nameof(UnsettlingLamp), SourceGroup.Relic, "Cards that debuff an enemy Artifact won't block, while unused this combat"),
        new(nameof(MusicBox), SourceGroup.Relic, "Attacks, while unused this turn"),
        new(nameof(ThrowingAxe), SourceGroup.Relic, "The card it will play twice", TagPrefix: "double-play:"),
        new(nameof(IntimidatingHelmet), SourceGroup.Relic, "Cards spending at least 2 energy: block"),
        new(nameof(DaughterOfTheWind), SourceGroup.Relic, "Attacks: block"),
        new(nameof(Permafrost), SourceGroup.Relic, "Your first power this combat: block"),
        new(nameof(LostWisp), SourceGroup.Relic, "Powers: damage to all enemies"),
        new(nameof(GamePiece), SourceGroup.Relic, "Powers: draws"),
        new(nameof(MummifiedHand), SourceGroup.Relic, "Powers, when another card in hand still costs energy or stars (one of them becomes free)"),
        new(nameof(HelicalDart), SourceGroup.Relic, "Shivs: Dexterity"),
        new(nameof(IvoryTile), SourceGroup.Relic, "Cards spending at least its energy threshold"),
        new(nameof(ChemicalX), SourceGroup.Relic, "X-cost cards"),
        new(nameof(RazorTooth), SourceGroup.Relic, "Upgradable attacks and skills: upgrades them"),
        new(nameof(RainbowRing), SourceGroup.Relic, "The card that completes attack + skill + power this turn"),
        new(nameof(Kunai), SourceGroup.Relic, "The attack that completes its count: Dexterity", "Attacks this turn, toward the next Dexterity"),
        new(nameof(Shuriken), SourceGroup.Relic, "The attack that completes its count: Strength", "Attacks this turn, toward the next Strength"),
        new(nameof(Nunchaku), SourceGroup.Relic, "The attack that completes its count: energy", "Attacks played, toward the next energy"),
        new(nameof(OrnamentalFan), SourceGroup.Relic, "The attack that completes its count: block", "Attacks this turn, toward the next block"),
        new("Twine", SourceGroup.Relic, "Cards that knock your arrows loose: block per arrow"),
        new("ConductorsBaton", SourceGroup.Relic, "The card that brings you to three melodies this turn (less damage from attacks)"),
        new(nameof(TheBoot), SourceGroup.Relic, "Attacks with a hit that gets past block for less than its minimum: raised to it"),
        new(nameof(Kusarigama), SourceGroup.Relic, "The attack that completes its count: damage", "Attacks this turn, toward the next hit"),
        new(nameof(LetterOpener), SourceGroup.Relic, "The skill that completes its count: damage to all", "Skills this turn, toward the next hit"),
        new(nameof(TuningFork), SourceGroup.Relic, "The skill that completes its count: block", "Skills played, toward the next block"),
        new(nameof(IronClub), SourceGroup.Relic, "The card that completes its count: draw", "Cards played, toward the next draw"),
        new(nameof(CharonsAshes), SourceGroup.Relic, "Cards that exhaust: damage to all per exhaust"),
        new(nameof(ForgottenSoul), SourceGroup.Relic, "Cards that exhaust: damage per exhaust"),
        new(nameof(JossPaper), SourceGroup.Relic, "Cards whose exhausts complete its count", "Cards exhausted, toward the next trigger"),
        new(nameof(Tingsha), SourceGroup.Relic, "Cards that discard: damage per discard"),
        new(nameof(ToughBandages), SourceGroup.Relic, "Cards that discard: block per discard"),
        new(nameof(MiniRegent), SourceGroup.Relic, "Star-cost cards, first star spend this turn"),
        new(nameof(GalacticDust), SourceGroup.Relic, "Star-cost cards that complete its count: block", "Stars spent, toward the next block"),
        new(nameof(Regalite), SourceGroup.Relic, "Cards that create cards (first this turn): block"),
        new(nameof(TheAbacus), SourceGroup.Relic, "Cards whose draw causes a shuffle: block"),
        new(nameof(WhittlingKnife), SourceGroup.Relic, "Cards whose draw causes a shuffle: Aim"),
        new(nameof(Metronome), SourceGroup.Relic, "Cards whose channels reach its orb count: damage to all", "Orbs channeled, toward the hit"),
        new(nameof(BoneFlute), SourceGroup.Relic, "Cards that make Osty attack: block"),
        new(nameof(UnceasingTop), SourceGroup.Relic, "Your last card in hand, if it draws nothing"),
        new(nameof(HandDrill), SourceGroup.Relic, "Attacks that break an enemy's block"),
        new(nameof(ParryingShield), SourceGroup.Relic, "Cards that make it fire at end of turn (your block reaches its threshold)", Shows: Sim.TabShows.SetsUp),
        new(nameof(Orichalcum), SourceGroup.Relic, Crossed + "cards that give you block when you'd end your turn with none (gives up its end-of-turn block)", Shows: Sim.TabShows.GivesUp),
        new(nameof(FakeOrichalcum), SourceGroup.Relic, Crossed + "cards that give you block when you'd end your turn with none (gives up its end-of-turn block)", Shows: Sim.TabShows.GivesUp),
        new(nameof(CloakClasp), SourceGroup.Relic, Crossed + "cards that leave fewer cards in your hand at the end of your turn (less block from it): nearly every card", TagDefault: false,
            Shows: Sim.TabShows.GivesUp | Sim.TabShows.Reduces),
        new(nameof(ObsidianVice), SourceGroup.Relic, Crossed + "cards that leave fewer cards in your hand at the end of your turn (less damage from it): nearly every card", TagDefault: false,
            Shows: Sim.TabShows.GivesUp | Sim.TabShows.Reduces),
        new(nameof(ScreamingFlagon), SourceGroup.Relic, "Cards that leave your hand empty: its damage to all at the end of your turn", Shows: Sim.TabShows.SetsUp),
        new(nameof(GremlinHorn), SourceGroup.Relic, "Attacks that kill an enemy"),
        new(nameof(MightyWings), SourceGroup.Relic, "Attacks with a hit of 30 or more (block included), at the right target whatever random outcomes do: Strength"),
        new(nameof(Fertilizer), SourceGroup.Relic, "Cards that create a Sadness: Strength"),
        new(nameof(CentennialPuzzle), SourceGroup.Relic, "Plays that make you lose HP (their cost, or enemy Thorns past your block): first time this combat"),
        new(nameof(SelfFormingClay), SourceGroup.Relic, "Plays that make you lose HP (their cost, or enemy Thorns past your block)"),
        new(nameof(DemonTongue), SourceGroup.Relic, "Plays that make you lose HP (their cost, or enemy Thorns past your block): first time this turn"),
        new(nameof(EmotionChip), SourceGroup.Relic, "Plays that make you lose HP (their cost, or enemy Thorns past your block)"),
        new(nameof(RedSkull), SourceGroup.Relic, "Plays that take you to half HP or below"),
        new(nameof(BrilliantScarf), SourceGroup.Relic, Tag: null, "Cards this turn, toward the free one"),
        new(nameof(VelvetChoker), SourceGroup.Relic, Tag: null, "Plays left this turn; red at 1"),
        new(nameof(Pocketwatch), SourceGroup.Relic, Crossed + "the card that takes this turn past its limit of cards (gives up next turn's extra draws)", "Cards this turn: green while under its limit, red once over"),
        new(nameof(StoneCalendar), SourceGroup.Relic, Tag: null, "Turn timer: turns until its damage", TrackerDefault: false),
        new(nameof(HappyFlower), SourceGroup.Relic, Tag: null, "Turn timer: turns until its energy", TrackerDefault: false),
        new(nameof(FakeHappyFlower), SourceGroup.Relic, Tag: null, "Turn timer: turns until its energy", TrackerDefault: false),
        new(nameof(Pendulum), SourceGroup.Relic, Tag: null, "Turn timer: turns until its draw", TrackerDefault: false),
        new(nameof(PollinousCore), SourceGroup.Relic, Tag: null, "Turn timer: turns until its draw", TrackerDefault: false),
        new(nameof(HornCleat), SourceGroup.Relic, Tag: null, "Turn timer: block on turn 2", TrackerDefault: false),
        new(nameof(Candelabra), SourceGroup.Relic, Tag: null, "Turn timer: energy on turn 2", TrackerDefault: false),
        new(nameof(CaptainsWheel), SourceGroup.Relic, Tag: null, "Turn timer: block on turn 3", TrackerDefault: false),
        new(nameof(PaelsFlesh), SourceGroup.Relic, Tag: null, "Turn timer: 1 more energy from turn 3", TrackerDefault: false),
        new(nameof(Chandelier), SourceGroup.Relic, Tag: null, "Turn timer: energy on turn 3", TrackerDefault: false),
        new(nameof(SparklingRouge), SourceGroup.Relic, Tag: null, "Turn timer: Strength and Dexterity on turn 3", TrackerDefault: false),

        // Outside fights (Run Reminders): tabs on card reward cards, event options and shop prices (a column each).
        new(nameof(BingBong), SourceGroup.Relic, Tag: null, Reward: "Tag on every card: the card you take is copied", Event: "Options that add a card (crossed when it adds a curse)", Shop: "Cards (copied)"),
        new(nameof(BookOfFiveRings), SourceGroup.Relic, Tag: null, Reward: "Tag on every card (toward its heal); a heart in its corner on the pick that heals, crossed when part of the heal is wasted", Event: "Options that add a card", Shop: "Cards (toward the heal)"),
        new(nameof(LuckyFysh), SourceGroup.Relic, Tag: null, Reward: "Tag on every card: taking one gives gold (none with Ectoplasm)", Event: "Options that add a card", Shop: "Cards, with the price after its gold"),
        new(nameof(DarkstonePeriapt), SourceGroup.Relic, Tag: null, Event: "Options that add a curse (max HP)"),
        new(nameof(MoltenEgg), SourceGroup.Relic, Tag: null, Event: "Options that can add an Attack it upgrades"),
        new(nameof(FrozenEgg), SourceGroup.Relic, Tag: null, Event: "Options that can add a Power it upgrades"),
        new(nameof(ToxicEgg), SourceGroup.Relic, Tag: null, Event: "Options that can add a Skill it upgrades"),
        new(nameof(FresnelLens), SourceGroup.Relic, Tag: null, Event: "Options that can add a card it enchants (Nimble on cards that gain block)"),
        // Card tabs outside fights (decision #62): on cards you're offered (rewards, event choices, the shop) it makes stronger.
        new(nameof(StrikeDummy), SourceGroup.Relic, Tag: null, Reward: "Tag on Strikes, and cards that make one (Begone!)",
            Event: "Options that can add a Strike; tag on Strikes an event offers", Shop: "Tag on Strikes, and cards that make one"),
        new(nameof(FakeStrikeDummy), SourceGroup.Relic, Tag: null, Reward: "Tag on Strikes, and cards that make one (Begone!)",
            Event: "Options that can add a Strike; tag on Strikes an event offers", Shop: "Tag on Strikes, and cards that make one"),
        new(nameof(MiniatureCannon), SourceGroup.Relic, Tag: null, Reward: "Tag on upgraded Attacks",
            Event: "Options that upgrade an Attack of yours or can add an upgraded one (an egg's); tag on upgraded Attacks an event offers", Shop: "Tag on upgraded Attacks"),
        new(nameof(MysticLighter), SourceGroup.Relic, Tag: null, Reward: "Tag on enchanted Attacks",
            Event: "Options that enchant an Attack of yours or can add an enchanted one (Fresnel Lens'); tag on enchanted Attacks an event offers", Shop: "Tag on enchanted Attacks"),
        new(nameof(VitruvianMinion), SourceGroup.Relic, Tag: null, Reward: "Tag on Minion cards, and cards that make one",
            Event: "Options that can add a Minion card; tag on Minion cards an event offers", Shop: "Tag on Minion cards, and cards that make one"),
        new(nameof(TungstenRod), SourceGroup.Relic, "Plays where HP loss gets past your block (their cost, enemy Thorns): 1 less", Event: "Options that cost HP (1 less)"),
        new(nameof(BowlerHat), SourceGroup.Relic, Tag: null, Event: "Options that give gold (more gold)"),
        new(nameof(DragonFruit), SourceGroup.Relic, Tag: null, Event: "Options that give gold (max HP)"),
        new(nameof(Ectoplasm), SourceGroup.Relic, Tag: null, Event: "Crossed. Options that give gold (you get none)"),
        new(nameof(Sozu), SourceGroup.Relic, Tag: null, Event: "Crossed. Options that give a potion (you get none)", Shop: "Crossed. Potions (you get none)"),
        new(nameof(BeltBuckle), SourceGroup.Relic, "Potion tag on your last potion: drinking it gives Dexterity", TagPrefix: "potion:",
            Event: "Crossed. Options that give a potion (no Dexterity while you hold it)", Shop: "Crossed. Potions (no Dexterity while you hold it)"),
        // Cards that come back to your hand (decision #42): tabs on the play that brings them back, with the card as the icon.
        new("MakeItSo", SourceGroup.Card, "Skills that bring it back to your hand (every 3rd Skill this turn)", "Skills played this turn toward its next return", TagPrefix: "return:"),
        new("RightHandHand", SourceGroup.Card, "Cards costing 2 or more while it's in your discard pile", TagPrefix: "return:"),
        new("WhistlingArrows", SourceGroup.Card, "Cards that shoot an arrow while it's in your discard pile", TagPrefix: "return:"),
        new(nameof(ReptileTrinket), SourceGroup.Relic, "Potion tag on every potion: drinking one gives Strength this turn", TagPrefix: "potion:"),
        new(nameof(WhiteBeastStatue), SourceGroup.Relic, "Potion tag on every potion while your slots are full: a potion reward comes after this fight", TagPrefix: "potion:"),
        new(nameof(MawBank), SourceGroup.Relic, Tag: null, Shop: "Crossed. Every item (buying anything ends its gold per room)",
            Map: "Crossed, beside Merchant until you buy something"),
        new(nameof(RegalPillow), SourceGroup.Relic, Tag: null, Event: "Options that rest (heals more)", Map: "Beside Rest (heals more)"),
        new(nameof(StoneHumidifier), SourceGroup.Relic, Tag: null, Event: "Options that rest (max HP)", Map: "Beside Rest (max HP)"),
        new(nameof(DreamCatcher), SourceGroup.Relic, Tag: null, Event: "Options that rest (a card reward)", Map: "Beside Rest (a card reward)"),
        new(nameof(TinyMailbox), SourceGroup.Relic, Tag: null, Event: "Options that rest (potions)", Map: "Beside Rest (potions)"),
        // Pathing (decision #65): beside the map legend's row for the kind of room they make unusually good or bad.
        new(nameof(BlackStar), SourceGroup.Relic, Tag: null, Map: "Beside Elite (a second relic)"),
        new(nameof(WhiteStar), SourceGroup.Relic, Tag: null, Map: "Beside Elite (a boss card reward)"),
        new(nameof(SlingOfCourage), SourceGroup.Relic, Tag: null, Map: "Beside Elite (Strength)"),
        new(nameof(BoomingConch), SourceGroup.Relic, Tag: null, Map: "Beside Elite (draw and energy)"),
        new(nameof(TormentousCollar), SourceGroup.Relic, Tag: null, Map: "Beside Elite (Weak)"),
        new(nameof(SwordOfStone), SourceGroup.Relic, Tag: null, Map: "Beside Elite, with its count"),
        new(nameof(WarHammer), SourceGroup.Relic, Tag: null, Map: "Beside Elite (upgrades)"),
        new(nameof(PrayerWheel), SourceGroup.Relic, Tag: null, Map: "Beside Enemy (a second card reward)"),
        new(nameof(FishingRod), SourceGroup.Relic, Tag: null, Map: "Beside Enemy, with its count"),
        new(nameof(EternalFeather), SourceGroup.Relic, Tag: null, Map: "Beside Rest, with the heal for your deck's size"),
        new(nameof(VenerableTeaSet), SourceGroup.Relic, Tag: null, Map: "Beside Rest (energy next fight)"),
        new(nameof(FakeVenerableTeaSet), SourceGroup.Relic, Tag: null, Map: "Beside Rest (energy next fight)"),
        new(nameof(Girya), SourceGroup.Relic, Tag: null, Map: "Beside Rest (Lift)"),
        new(nameof(Shovel), SourceGroup.Relic, Tag: null, Map: "Beside Rest (Dig)"),
        new(nameof(MeatCleaver), SourceGroup.Relic, Tag: null, Map: "Beside Rest (Cook)"),
        new(nameof(PaelsGrowth), SourceGroup.Relic, Tag: null, Map: "Beside Rest while a card in your deck has Clone (its option)"),
        new(nameof(PumpkinCandle), SourceGroup.Relic, Tag: null, Map: "Beside Rest (Kindle)"),
        new(nameof(Wuppy), SourceGroup.Relic, Tag: null, Map: "Beside Rest (Restore)"),
        new(nameof(Byrdpip), SourceGroup.Relic, Tag: null, Map: "Beside Rest (Evolve)"),
        new(nameof(MiniatureTent), SourceGroup.Relic, Tag: null, Map: "Beside Rest (more than one option)"),
        new(nameof(Planisphere), SourceGroup.Relic, Tag: null, Map: "Beside Unknown, with its heal"),
        new(nameof(JuzuBracelet), SourceGroup.Relic, Tag: null, Map: "Beside Unknown (never a fight)"),
        new(nameof(GoldenCompass), SourceGroup.Relic, Tag: null, Map: "Beside Unknown in its act (always an event)"),
        new(nameof(MealTicket), SourceGroup.Relic, Tag: null, Map: "Beside Merchant, with its heal"),
        new(nameof(LordsParasol), SourceGroup.Relic, Tag: null, Map: "Beside Merchant (buys everything)"),
        new(nameof(MembershipCard), SourceGroup.Relic, Tag: null, Map: "Beside Merchant (half price)"),
        new(nameof(TheCourier), SourceGroup.Relic, Tag: null, Map: "Beside Merchant (cheaper, and what you buy restocks)"),
        new("Dowsing", SourceGroup.Card, Tag: null, Map: "Beside Unknown while in your deck, with the rooms left"),
        new("ByrdonisEgg", SourceGroup.Card, Tag: null, Map: "Beside Rest while in your deck (Hatch)"),
        new("LanternKey", SourceGroup.Card, Tag: null, Map: "Beside Unknown in act 3 while in your deck (always an event)"),

        // ------------------------------------------------------------------ your powers
        new(nameof(EchoFormPower), SourceGroup.YourPower, "Cards it will play twice", TagPrefix: "double-play:"),
        new(nameof(BurstPower), SourceGroup.YourPower, "Skills it will play twice", TagPrefix: "double-play:"),
        new(nameof(DuplicationPower), SourceGroup.YourPower, "Cards it will play twice", TagPrefix: "double-play:"),
        new(nameof(OneTwoPunchPower), SourceGroup.YourPower, "Attacks it will play twice", TagPrefix: "double-play:"),
        new(nameof(SignalBoostPower), SourceGroup.YourPower, "Powers it will play twice", TagPrefix: "double-play:"),
        new("KiVersePower", SourceGroup.YourPower, "Wah, Cho and Ki cards it will play twice", TagPrefix: "double-play:"),
        new("ViolentChordPower", SourceGroup.YourPower, "Your first Wah this turn: damage to every enemy"),
        new("SongPathPower", SourceGroup.YourPower, "Your first Cho this turn: block"),
        new("LullabyPower", SourceGroup.YourPower, "The card that completes Wah, Cho and Ki this turn: block"),
        new("MotifPower", SourceGroup.YourPower, "Your first Wah, Cho or Ki this turn: draws"),
        new("LostLutePower", SourceGroup.YourPower, "Your first Wah, Cho or Ki this turn: plays the top of your draw pile"),
        new("ExplosiveArrowsPower", SourceGroup.YourPower, "Shots with an arrow to shoot: damage to every enemy"),
        new("YahPower", SourceGroup.YourPower, "Cards that knock one of your arrows loose (first time this turn): energy"),
        new("StretchPower", SourceGroup.YourPower, "Block cards (and Adroit cards) that spend your Stretch"),
        new(nameof(VigorPower), SourceGroup.YourPower, "Attacks that spend your Vigor (Akabeko's on turn 1)"),
        new(nameof(GigantificationPower), SourceGroup.YourPower, "Attacks that spend a stack of Gigantification"),
        new(nameof(AfterimagePower), SourceGroup.YourPower, "Every card: block per play", TagDefault: false),
        new(nameof(RagePower), SourceGroup.YourPower, "Attacks: block"),
        new(nameof(SpiritOfAshPower), SourceGroup.YourPower, "Ethereal cards: block"),
        new(nameof(DanseMacabrePower), SourceGroup.YourPower, "Cards costing 2+ energy: block"),
        new(nameof(FeelNoPainPower), SourceGroup.YourPower, "Cards that exhaust (themselves or others): block per exhaust"),
        new(nameof(ChildOfTheStarsPower), SourceGroup.YourPower, "Star-cost cards: block per star"),
        new(nameof(ShroudPower), SourceGroup.YourPower, "Cards that apply Doom: block per Doom that lands"),
        new(nameof(PillarOfCreationPower), SourceGroup.YourPower, "Cards that create cards: block per card"),
        new(nameof(AllIsWellPower), SourceGroup.YourPower, "Cards that create a Sadness: block per Sadness"),
        new(nameof(CompostPower), SourceGroup.YourPower, "Attacks that kill an enemy (yours or a teammate's: energy for every player)"),
        new(nameof(GravityPower), SourceGroup.YourPower, "Every card: damage to all", TagDefault: false),
        new(nameof(SerpentFormPower), SourceGroup.YourPower, "Every card: damage to a random enemy", TagDefault: false),
        new(nameof(ThunderPower), SourceGroup.YourPower, "Cards that evoke Lightning: damage to its target again"),
        new(nameof(HailstormPower), SourceGroup.YourPower, "Cards that channel your first Frost orb: its damage to all at the end of your turn, with the chance when a random orb decides it (Chaos: 20%). Crossed on cards that evoke your last, with the chance too",
            Shows: Sim.TabShows.Default | Sim.TabShows.SetsUpByChance),
        new(nameof(IterationPower), SourceGroup.YourPower, "Cards that draw your first Status this turn: its draws, with the chance when what you draw decides it",
            Shows: Sim.TabShows.Default | Sim.TabShows.FiresByChance),
        new(nameof(PanachePower), SourceGroup.YourPower, "The card that completes its count: damage to all", "Cards this turn, toward the next hit"),
        new(nameof(BlackHolePower), SourceGroup.YourPower, "Cards that spend or gain stars: damage to all"),
        new(nameof(HauntPower), SourceGroup.YourPower, "Soul: damage"),
        new(nameof(SleightOfFleshPower), SourceGroup.YourPower, "Cards that debuff enemies: damage per debuff that lands"),
        new(nameof(JuggernautPower), SourceGroup.YourPower, "Cards that gain block (also from other procs): damage per gain"),
        new(nameof(SpeedsterPower), SourceGroup.YourPower, "Cards that draw: damage per card drawn"),
        new(nameof(CacophonyPower), SourceGroup.YourPower, "Cards whose draws finish its countdown: damage", "Cards drawn, toward the next hit (number only)"),
        new(nameof(SmokestackPower), SourceGroup.YourPower, "Cards that create statuses: damage per status"),
        new(nameof(DevourLifePower), SourceGroup.YourPower, "Soul: summon"),
        new(nameof(StormPower), SourceGroup.YourPower, "Powers: channels Lightning"),
        new(nameof(SubroutinePower), SourceGroup.YourPower, "Powers"),
        new(nameof(CalamityPower), SourceGroup.YourPower, "Attacks"),
        new(nameof(EnvenomPower), SourceGroup.YourPower, "Attacks with a hit that gets through block: poison"),
        new(nameof(ConcoctPower), SourceGroup.YourPower, "Attacks with a hit that gets through block: poison"),
        new(nameof(ReaperFormPower), SourceGroup.YourPower, "Attacks"),
        new(nameof(MonarchsGazePower), SourceGroup.YourPower, "Attacks"),
        new(nameof(JugglingPower), SourceGroup.YourPower, "Your 3rd attack this turn"),
        new(nameof(MasterPlannerPower), SourceGroup.YourPower, "Skills: gain Sly"),
        new(nameof(FeralPower), SourceGroup.YourPower, "0-cost Attacks, while it has charges this turn: back to your hand"),
        new("NostalgiaPower", SourceGroup.YourPower, "Your first Attacks and Skills this turn: onto your draw pile"),
        new(nameof(MonologuePower), SourceGroup.YourPower, "Every card", TagDefault: false),
        new(nameof(TheSealedThronePower), SourceGroup.YourPower, "Every card: gain stars", TagDefault: false),
        new(nameof(PaleBlueDotPower), SourceGroup.YourPower, "Your 5th card this turn", "Cards this turn, toward 5 (\"done\" once it fired)"),
        new(nameof(DarkEmbracePower), SourceGroup.YourPower, "Cards that exhaust: draw"),
        new(nameof(OrbitPower), SourceGroup.YourPower, "Cards whose energy finishes its count", "Energy spent, toward the next energy"),
        new(nameof(TheBombPower), SourceGroup.YourPower, null, "Turns until it explodes, and its damage to all (a row for each one)"),
        new(nameof(AutomationPower), SourceGroup.YourPower, "Cards whose draws finish its count", "Cards drawn, toward the next energy"),
        new(nameof(CorrosiveWavePower), SourceGroup.YourPower, "Cards that draw"),
        new(nameof(NoDrawPower), SourceGroup.YourPower, "Cards whose draws it stops (Battle Trance)"),
        new(nameof(StratagemPower), SourceGroup.YourPower, "Cards whose draw causes a shuffle"),
        new(nameof(ViciousPower), SourceGroup.YourPower, "Cards that apply Vulnerable"),
        new(nameof(ArsenalPower), SourceGroup.YourPower, "Cards that create cards"),
        new(nameof(TrashToTreasurePower), SourceGroup.YourPower, "Cards that create statuses"),
        new(nameof(TunePower), SourceGroup.YourPower, "Your first Ki this turn: upgrades random cards in your hand"),
        // Free card powers (Procs/FreeCards.cs): playing a card from your hand uses one up, whatever it costs.
        new(nameof(FreeAttackPower), SourceGroup.YourPower, "Your Attacks (Unrelenting): the energy it saves, ? when you couldn't know its cost (Snecko Eye, Frantic Escape). Crossed on 0-cost and X-cost Attacks", TagPrefix: SpireQoL.Procs.FreeCards.Prefix),
        new(nameof(FreeSkillPower), SourceGroup.YourPower, "Your Skills (Pounce): the energy it saves, ? when you couldn't know its cost (Snecko Eye, Frantic Escape). Crossed on 0-cost and X-cost Skills", TagPrefix: SpireQoL.Procs.FreeCards.Prefix),
        new(nameof(FreePowerPower), SourceGroup.YourPower, "Your Powers (Synthesis): the energy it saves, ? when you couldn't know its cost. Crossed on 0-cost Powers", TagPrefix: SpireQoL.Procs.FreeCards.Prefix),
        new(nameof(RupturePower), SourceGroup.YourPower, "Plays that make you lose HP (their cost, or enemy Thorns past your block): Strength"),
        new(nameof(InfernoPower), SourceGroup.YourPower, "Plays that make you lose HP (their cost, or enemy Thorns past your block): damage to all, each time"),
        new(nameof(NecroMasteryPower), SourceGroup.YourPower, "Cards that kill Osty: damage to all (Osty's HP times its amount)"),
        new(nameof(SeekingEdgePower), SourceGroup.YourPower, "Sovereign Blade: hits all enemies"),
        new(nameof(ParryPower), SourceGroup.YourPower, "Sovereign Blade: block"),
        new(nameof(SwordSagePower), SourceGroup.YourPower, "Sovereign Blade: replays"),
        new(nameof(TenderPower), SourceGroup.YourPower, Crossed + "every card"),
        new(nameof(SmoggyPower), SourceGroup.YourPower, Crossed + "skills"),

        // ------------------------------------------------------------------ powers on enemies
        new(nameof(TagTeamPower), SourceGroup.EnemyPower, "Attacks aimed at that enemy play twice", TagPrefix: "double-play:"),
        new(nameof(StranglePower), SourceGroup.EnemyPower, "Yours, on an enemy. Every card: damage to it", TagDefault: false),
        new(nameof(SicEmPower), SourceGroup.EnemyPower, "Yours, on an enemy. Cards whose Osty hit damages it: summon"),
        new(nameof(OblivionPower), SourceGroup.EnemyPower, "Yours, on an enemy. Every card", TagDefault: false),
        new(nameof(ConquerorPower), SourceGroup.EnemyPower, "On an enemy (from anyone's Conqueror). Sovereign Blade hitting it: double damage to it (the aim marker when another target has none)"),
        new(nameof(SlowPower), SourceGroup.EnemyPower, "Every card: it takes more damage"),
        new(nameof(BurrowedPower), SourceGroup.EnemyPower, "Attacks that break its block: stuns it"),
        new(nameof(FlutterPower), SourceGroup.EnemyPower, "Attacks that hit it enough times: stuns it"),
        new(nameof(PlowPower), SourceGroup.EnemyPower, "Attacks that bring it to its threshold"),
        new(nameof(ShriekPower), SourceGroup.EnemyPower, "Attacks that bring it to its threshold"),
        new(nameof(TheGambitPower), SourceGroup.EnemyPower, "Attacks that deal it unblocked damage: it dies"),
        new(nameof(EnragePower), SourceGroup.EnemyPower, Crossed + "skills: it gains Strength"),
        new(nameof(SneakyPower), SourceGroup.EnemyPower, Crossed + "attacks"),
        new(nameof(ImbalancedPower), SourceGroup.EnemyPower, "Cards after which its first hit is fully blocked in every outcome, when it isn't now (it's stunned)"),
        new(nameof(SkittishPower), SourceGroup.EnemyPower, Crossed + "attacks (first unblocked hit): it gains block"),
        new(nameof(ImitationLearningPower), SourceGroup.YourPower, "your Power cards a teammate's Imitation Learning will copy"),
        new(nameof(WitheringPresencePower), SourceGroup.EnemyPower, Crossed + "the card that completes its count: Wither in your hand", "Your cards, toward the next Wither (red)"),
        new(nameof(GalvanicPower), SourceGroup.EnemyPower, Crossed + "Galvanized cards: damage to you"),
        new(nameof(VitalSparkPower), SourceGroup.EnemyPower, Crossed + "Tainted cards"),
        new(nameof(ThornsPower), SourceGroup.EnemyPower, Crossed + "attacks that hit it: damage to you"),
        new(nameof(CurlUpPower), SourceGroup.EnemyPower, Crossed + "attacks that hit it: it gains block"),
        new(nameof(PersonalHivePower), SourceGroup.EnemyPower, Crossed + "attacks that hit it: Dazed in your draw pile, how many (its amount per hit)"),
        new(nameof(AsleepPower), SourceGroup.EnemyPower, Crossed + "cards after which damage could get through its block by the end of your turn (procs and end-of-turn damage included) and make its next move an attack where it would stay asleep: wakes it"),
        new(nameof(SlumberPower), SourceGroup.EnemyPower, Crossed + "cards after which enough damage instances could get through its block by the end of your turn that its next move is an attack where it would stay asleep: wakes it"),
        new(nameof(SlipperyPower), SourceGroup.EnemyPower, Crossed + "attacks that hit it: HP loss capped at 1"),
        new(nameof(InfestedPower), SourceGroup.EnemyPower, Crossed + "attacks that kill it"),
        new(nameof(SteamEruptionPower), SourceGroup.EnemyPower, Crossed + "attacks that kill it"),
        new(nameof(SurprisePower), SourceGroup.EnemyPower, Crossed + "attacks that kill it"),
        new(nameof(ExplosivePower), SourceGroup.EnemyPower, Crossed + "attacks that kill it (its death damages every player)"),
        new(nameof(StockPower), SourceGroup.EnemyPower, Crossed + "attacks that kill it while it has stock left (a new Axebot takes its place)"),
        new(nameof(CrabRagePower), SourceGroup.EnemyPower, Crossed + "attacks that kill another enemy"),
        new(nameof(RavenousPower), SourceGroup.EnemyPower, Crossed + "attacks that kill another enemy"),
    };

    private static HashSet<string>? _knownKeys;

    /// <summary>Every tab key in the catalog.</summary>
    public static bool IsKnownTabKey(string key)
    {
        _knownKeys ??= CardTabs.Select(tabEntry => tabEntry.Key)
            .Concat(TabGroups.Select(tabEntry => tabEntry.Key))
            .Concat(Sources.Where(sourceEntry => sourceEntry.TagKey != null).Select(sourceEntry => sourceEntry.TagKey!))
            // Run Reminders keep their switches in the same store (one per place); not card tabs, so not under "Other".
            .Concat(Sources.SelectMany(sourceEntry => Enum.GetValues<SpireQoL.Data.ReminderPlace>()
                .Where(reminderPlace => sourceEntry.Reminder(reminderPlace) != null).Select(reminderPlace => SpireQoL.Data.ChoiceReminders.Key(reminderPlace, sourceEntry.Type))))
            .ToHashSet();
        return _knownKeys.Contains(key);
    }

    /// <summary>
    /// Logs tracker specs missing from the catalog, so a new relic doesn't silently lack a switch.
    /// (Missing tab keys are logged as they first appear; see QolSettings.TabEnabled.)
    /// </summary>
    public static void Check(Sts2Mods.Core.ModLog log)
    {
        var tracked = Sources.Where(sourceEntry => sourceEntry.Tracker != null).Select(sourceEntry => sourceEntry.Type).ToHashSet();
        foreach (var spec in Data.RelicSpecs.All.Where(relicCounterSpec => relicCounterSpec.InTray))
        {
            if (!tracked.Contains(spec.TypeName)) log.Warn($"settings menu: relic tracker {spec.TypeName} isn't in SettingsCatalog");
        }
        foreach (var spec in Data.PowerSpecs.All)
        {
            if (!tracked.Contains(spec.TypeName)) log.Warn($"settings menu: power tracker {spec.TypeName} isn't in SettingsCatalog");
        }
        var dupes = Sources.GroupBy(sourceEntry => sourceEntry.Type).Where(sameType => sameType.Count() > 1).Select(sameType => sameType.Key).ToList();
        if (dupes.Count > 0) log.Warn($"settings menu: duplicate catalog rows {string.Join(", ", dupes)}");
    }
}
