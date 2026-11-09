namespace SpireQoL.Menu;

public enum SourceGroup { Relic, YourPower, EnemyPower }

/// <summary>
/// One relic or power the mod knows about, as a row in the settings menu.
/// </summary>
/// <param name="Type">The game's class name (e.g. "PenNib", "FeelNoPainPower").</param>
/// <param name="Tag">What its on-card tab marks and shows; null = it has no tab of its own.</param>
/// <param name="Tracker">What its Relic Counters row shows; null = no tray row.</param>
/// <param name="TagPrefix">"proc:" (Proc Indicators) or "double-play:" (Card Indicators).</param>
/// <param name="TrackerDefault">Tray row on by default (turn timers are off).</param>
public sealed record SourceEntry(string Type, SourceGroup Group, string? Tag, string? Tracker = null,
    string TagPrefix = "proc:", bool TrackerDefault = true)
{
    public string? TagKey => Tag == null ? null : TagPrefix + Type;
}

/// <summary>An on-card tab that isn't tied to one relic or power.</summary>
public sealed record TabEntry(string Key, string Name, string Description);

/// <summary>
/// Everything the settings menu lists. Must cover every rule in ProcIndicatorsFeature, every
/// double-play source, and every RelicSpecs / PowerSpecs entry (SettingsCatalog.Check logs any
/// tab key seen in play that isn't here, and the menu lists such keys under "Other").
/// Text follows docs/FEATURES.md "Proc Indicators" and "Relic Counters".
/// </summary>
public static class SettingsCatalog
{
    public static readonly IReadOnlyList<TabEntry> CardTabs = new[]
    {
        new TabEntry("retain", "Retain", "Cards that stay in your hand at end of turn (permanently or this turn only). Steady icon."),
        new TabEntry("sly", "Sly", "Cards with Sly (permanently or this turn only). Master Planner icon."),
        new TabEntry("replay", "Replay", "Cards that replay themselves (Hidden Gem, Transfigure, Sword Sage, enchantments): a \"2×\" tab with the total plays."),
        new TabEntry("shuffle", "Reshuffle", "Discard pile icon on cards whose draw would shuffle your discard pile back into your draw pile."),
    };

    /// <summary>Group switches: off hides the whole family.</summary>
    public static readonly IReadOnlyList<TabEntry> TabGroups = new[]
    {
        new TabEntry("proc:*", "All proc tabs", "Every relic and power tab below (Relics, Your powers, Enemy powers)."),
        new TabEntry("double-play:*", "All double-play tabs", "Tabs on cards that will play more than once (Echo Form, Burst, Duplication...)."),
    };

    /// <summary>Marks a crossed tab (helps the enemy, hurts you, or gives up a relic); the menu draws the red slash.</summary>
    public const string CrossedPrefix = "crossed: ";
    private const string Crossed = CrossedPrefix;

    public static readonly IReadOnlyList<SourceEntry> Sources = new SourceEntry[]
    {
        // ------------------------------------------------------------------ relics
        new("PenNib", SourceGroup.Relic, "Attacks while it's ready: this one deals double", "Attacks played, toward the doubled 10th"),
        new("ArtOfWar", SourceGroup.Relic, Crossed + "your first attack this turn (gives up its energy next turn)"),
        new("RippleBasin", SourceGroup.Relic, Crossed + "your first attack this turn (gives up its end-of-turn block)"),
        new("PaelsTears", SourceGroup.Relic, Crossed + "cards that spend your last energy (gives up next turn's carry-over)"),
        new("PaelsEye", SourceGroup.Relic, Crossed + "any card while armed and nothing played yet (gives up the extra turn)"),
        new("Vambrace", SourceGroup.Relic, "Cards that gain block, while unused this combat"),
        new("PaelsLegion", SourceGroup.Relic, "Cards that gain block, while it's off cooldown (double block)"),
        new("BurningSticks", SourceGroup.Relic, "Skills that exhaust themselves, while unused this combat"),
        new("UnsettlingLamp", SourceGroup.Relic, "Cards that debuff enemies, while unused this combat"),
        new("MusicBox", SourceGroup.Relic, "Attacks, while unused this turn"),
        new("ThrowingAxe", SourceGroup.Relic, "The card it will play twice", TagPrefix: "double-play:"),
        new("IntimidatingHelmet", SourceGroup.Relic, "Cards spending at least 2 energy: block"),
        new("DaughterOfTheWind", SourceGroup.Relic, "Attacks: block"),
        new("Permafrost", SourceGroup.Relic, "Your first power this combat: block"),
        new("LostWisp", SourceGroup.Relic, "Powers: damage to all enemies"),
        new("GamePiece", SourceGroup.Relic, "Powers: draws"),
        new("MummifiedHand", SourceGroup.Relic, "Powers, when another card in hand still costs energy or stars (one of them becomes free)"),
        new("HelicalDart", SourceGroup.Relic, "Shivs: Dexterity"),
        new("IvoryTile", SourceGroup.Relic, "Cards spending at least its energy threshold"),
        new("ChemicalX", SourceGroup.Relic, "X-cost cards"),
        new("RazorTooth", SourceGroup.Relic, "Upgradable attacks and skills: upgrades them"),
        new("RainbowRing", SourceGroup.Relic, "The card that completes attack + skill + power this turn"),
        new("Kunai", SourceGroup.Relic, "The attack that completes its count: Dexterity", "Attacks this turn, toward the next Dexterity"),
        new("Shuriken", SourceGroup.Relic, "The attack that completes its count: Strength", "Attacks this turn, toward the next Strength"),
        new("Nunchaku", SourceGroup.Relic, "The attack that completes its count: energy", "Attacks played, toward the next energy"),
        new("OrnamentalFan", SourceGroup.Relic, "The attack that completes its count: block", "Attacks this turn, toward the next block"),
        new("Kusarigama", SourceGroup.Relic, "The attack that completes its count: damage", "Attacks this turn, toward the next hit"),
        new("LetterOpener", SourceGroup.Relic, "The skill that completes its count: damage to all", "Skills this turn, toward the next hit"),
        new("TuningFork", SourceGroup.Relic, "The skill that completes its count: block", "Skills played, toward the next block"),
        new("IronClub", SourceGroup.Relic, "The card that completes its count: draw", "Cards played, toward the next draw"),
        new("CharonsAshes", SourceGroup.Relic, "Cards that exhaust: damage to all per exhaust"),
        new("ForgottenSoul", SourceGroup.Relic, "Cards that exhaust: damage per exhaust"),
        new("JossPaper", SourceGroup.Relic, "Cards whose exhausts complete its count", "Cards exhausted, toward the next trigger"),
        new("Tingsha", SourceGroup.Relic, "Cards that discard: damage per discard"),
        new("ToughBandages", SourceGroup.Relic, "Cards that discard: block per discard"),
        new("MiniRegent", SourceGroup.Relic, "Star-cost cards, first star spend this turn"),
        new("GalacticDust", SourceGroup.Relic, "Star-cost cards that complete its count: block", "Stars spent, toward the next block"),
        new("Regalite", SourceGroup.Relic, "Cards that create cards (first this turn): block"),
        new("TheAbacus", SourceGroup.Relic, "Cards whose draw causes a shuffle: block"),
        new("Metronome", SourceGroup.Relic, "Cards whose channels reach its orb count: damage to all", "Orbs channeled, toward the hit"),
        new("BoneFlute", SourceGroup.Relic, "Cards that make Osty attack: block"),
        new("UnceasingTop", SourceGroup.Relic, "Your last card in hand, if it draws nothing"),
        new("HandDrill", SourceGroup.Relic, "Attacks that break an enemy's block"),
        new("GremlinHorn", SourceGroup.Relic, "Attacks that kill an enemy"),
        new("CentennialPuzzle", SourceGroup.Relic, "Cards that cost you HP (first time this combat)"),
        new("SelfFormingClay", SourceGroup.Relic, "Cards that cost you HP"),
        new("DemonTongue", SourceGroup.Relic, "Cards that cost you HP (first time this turn)"),
        new("EmotionChip", SourceGroup.Relic, "Cards that cost you HP"),
        new("RedSkull", SourceGroup.Relic, "Cards that take you to half HP or below"),
        new("BrilliantScarf", SourceGroup.Relic, null, "Cards this turn, toward the free one"),
        new("VelvetChoker", SourceGroup.Relic, null, "Plays left this turn; red at 1"),
        new("Pocketwatch", SourceGroup.Relic, null, "Cards this turn: green while under its limit, red once over"),
        new("StoneCalendar", SourceGroup.Relic, null, "Turn timer: turns until its damage", TrackerDefault: false),
        new("HappyFlower", SourceGroup.Relic, null, "Turn timer: turns until its energy", TrackerDefault: false),
        new("FakeHappyFlower", SourceGroup.Relic, null, "Turn timer: turns until its energy", TrackerDefault: false),
        new("Pendulum", SourceGroup.Relic, null, "Turn timer: turns until its draw", TrackerDefault: false),
        new("PollinousCore", SourceGroup.Relic, null, "Turn timer: turns until its draw", TrackerDefault: false),
        new("HornCleat", SourceGroup.Relic, null, "Turn timer: block on turn 2", TrackerDefault: false),
        new("Candelabra", SourceGroup.Relic, null, "Turn timer: energy on turn 2", TrackerDefault: false),
        new("CaptainsWheel", SourceGroup.Relic, null, "Turn timer: block on turn 3", TrackerDefault: false),
        new("Chandelier", SourceGroup.Relic, null, "Turn timer: energy on turn 3", TrackerDefault: false),
        new("SparklingRouge", SourceGroup.Relic, null, "Turn timer: Strength and Dexterity on turn 3", TrackerDefault: false),

        // ------------------------------------------------------------------ your powers
        new("EchoFormPower", SourceGroup.YourPower, "Cards it will play twice", TagPrefix: "double-play:"),
        new("BurstPower", SourceGroup.YourPower, "Skills it will play twice", TagPrefix: "double-play:"),
        new("DuplicationPower", SourceGroup.YourPower, "Cards it will play twice", TagPrefix: "double-play:"),
        new("OneTwoPunchPower", SourceGroup.YourPower, "Attacks it will play twice", TagPrefix: "double-play:"),
        new("SignalBoostPower", SourceGroup.YourPower, "Powers it will play twice", TagPrefix: "double-play:"),
        new("AfterimagePower", SourceGroup.YourPower, "Every card: block per play"),
        new("RagePower", SourceGroup.YourPower, "Attacks: block"),
        new("SpiritOfAshPower", SourceGroup.YourPower, "Ethereal cards: block"),
        new("DanseMacabrePower", SourceGroup.YourPower, "Cards costing 2+ energy: block"),
        new("FeelNoPainPower", SourceGroup.YourPower, "Cards that exhaust (themselves or others): block per exhaust"),
        new("ChildOfTheStarsPower", SourceGroup.YourPower, "Star-cost cards: block per star"),
        new("ShroudPower", SourceGroup.YourPower, "Cards that apply Doom: block per Doom that lands"),
        new("PillarOfCreationPower", SourceGroup.YourPower, "Cards that create cards: block per card"),
        new("GravityPower", SourceGroup.YourPower, "Every card: damage to all"),
        new("SerpentFormPower", SourceGroup.YourPower, "Every card: damage to a random enemy"),
        new("PanachePower", SourceGroup.YourPower, "The card that completes its count: damage to all", "Cards this turn, toward the next hit"),
        new("BlackHolePower", SourceGroup.YourPower, "Cards that spend or gain stars: damage to all"),
        new("HauntPower", SourceGroup.YourPower, "Soul: damage"),
        new("SleightOfFleshPower", SourceGroup.YourPower, "Cards that debuff enemies: damage per debuff that lands"),
        new("JuggernautPower", SourceGroup.YourPower, "Cards that gain block (also from other procs): damage per gain"),
        new("SpeedsterPower", SourceGroup.YourPower, "Cards that draw: damage per card drawn"),
        new("CacophonyPower", SourceGroup.YourPower, "Cards whose draws finish its countdown: damage", "Cards drawn, toward the next hit (number only)"),
        new("SmokestackPower", SourceGroup.YourPower, "Cards that create statuses: damage per status"),
        new("DevourLifePower", SourceGroup.YourPower, "Soul: summon"),
        new("StormPower", SourceGroup.YourPower, "Powers: channels Lightning"),
        new("SubroutinePower", SourceGroup.YourPower, "Powers"),
        new("CalamityPower", SourceGroup.YourPower, "Attacks"),
        new("EnvenomPower", SourceGroup.YourPower, "Attacks"),
        new("ConcoctPower", SourceGroup.YourPower, "Attacks"),
        new("ReaperFormPower", SourceGroup.YourPower, "Attacks"),
        new("MonarchsGazePower", SourceGroup.YourPower, "Attacks"),
        new("JugglingPower", SourceGroup.YourPower, "Your 3rd attack this turn"),
        new("MasterPlannerPower", SourceGroup.YourPower, "Skills: gain Sly"),
        new("MonologuePower", SourceGroup.YourPower, "Every card"),
        new("TheSealedThronePower", SourceGroup.YourPower, "Every card: gain stars"),
        new("PaleBlueDotPower", SourceGroup.YourPower, "Your 5th card this turn", "Cards this turn, toward 5 (\"done\" once it fired)"),
        new("DarkEmbracePower", SourceGroup.YourPower, "Cards that exhaust: draw"),
        new("OrbitPower", SourceGroup.YourPower, "Cards whose energy finishes its count", "Energy spent, toward the next energy"),
        new("AutomationPower", SourceGroup.YourPower, "Cards whose draws finish its count", "Cards drawn, toward the next energy"),
        new("CorrosiveWavePower", SourceGroup.YourPower, "Cards that draw"),
        new("StratagemPower", SourceGroup.YourPower, "Cards whose draw causes a shuffle"),
        new("ViciousPower", SourceGroup.YourPower, "Cards that apply Vulnerable"),
        new("ArsenalPower", SourceGroup.YourPower, "Cards that create cards"),
        new("TrashToTreasurePower", SourceGroup.YourPower, "Cards that create statuses"),
        new("RupturePower", SourceGroup.YourPower, "Cards that cost you HP: Strength"),
        new("SeekingEdgePower", SourceGroup.YourPower, "Sovereign Blade: hits all enemies"),
        new("ParryPower", SourceGroup.YourPower, "Sovereign Blade: block"),
        new("SwordSagePower", SourceGroup.YourPower, "Sovereign Blade: replays"),
        new("TenderPower", SourceGroup.YourPower, Crossed + "every card"),
        new("SmoggyPower", SourceGroup.YourPower, Crossed + "skills"),

        // ------------------------------------------------------------------ powers on enemies
        new("TagTeamPower", SourceGroup.EnemyPower, "Attacks aimed at that enemy play twice", TagPrefix: "double-play:"),
        new("StranglePower", SourceGroup.EnemyPower, "Yours, on an enemy. Every card: damage to it"),
        new("OblivionPower", SourceGroup.EnemyPower, "Yours, on an enemy. Every card"),
        new("ConquerorPower", SourceGroup.EnemyPower, "Yours, on an enemy. Sovereign Blade: more damage to it"),
        new("SlowPower", SourceGroup.EnemyPower, "Every card: it takes more damage"),
        new("BurrowedPower", SourceGroup.EnemyPower, "Attacks that break its block: stuns it"),
        new("FlutterPower", SourceGroup.EnemyPower, "Attacks that hit it enough times: stuns it"),
        new("PlowPower", SourceGroup.EnemyPower, "Attacks that bring it to its threshold"),
        new("ShriekPower", SourceGroup.EnemyPower, "Attacks that bring it to its threshold"),
        new("TheGambitPower", SourceGroup.EnemyPower, "Attacks that deal it unblocked damage: it dies"),
        new("EnragePower", SourceGroup.EnemyPower, Crossed + "skills: it gains Strength"),
        new("SneakyPower", SourceGroup.EnemyPower, Crossed + "attacks"),
        new("SkittishPower", SourceGroup.EnemyPower, Crossed + "attacks (first unblocked hit): it gains block"),
        new("ImitationLearningPower", SourceGroup.EnemyPower, Crossed + "powers"),
        new("WitheringPresencePower", SourceGroup.EnemyPower, Crossed + "the card that completes its count: Wither in your hand", "Your cards, toward the next Wither (red)"),
        new("GalvanicPower", SourceGroup.EnemyPower, Crossed + "Galvanized cards: damage to you"),
        new("VitalSparkPower", SourceGroup.EnemyPower, Crossed + "Tainted cards"),
        new("ThornsPower", SourceGroup.EnemyPower, Crossed + "attacks that hit it: damage to you"),
        new("CurlUpPower", SourceGroup.EnemyPower, Crossed + "attacks that hit it: it gains block"),
        new("PersonalHivePower", SourceGroup.EnemyPower, Crossed + "attacks that hit it: Dazed in your draw pile"),
        new("AsleepPower", SourceGroup.EnemyPower, Crossed + "attacks that deal it unblocked damage: wakes it"),
        new("SlumberPower", SourceGroup.EnemyPower, Crossed + "attacks that hit it enough times: wakes it"),
        new("SlipperyPower", SourceGroup.EnemyPower, Crossed + "attacks that hit it: HP loss capped at 1"),
        new("InfestedPower", SourceGroup.EnemyPower, Crossed + "attacks that kill it"),
        new("SteamEruptionPower", SourceGroup.EnemyPower, Crossed + "attacks that kill it"),
        new("SurprisePower", SourceGroup.EnemyPower, Crossed + "attacks that kill it"),
        new("CrabRagePower", SourceGroup.EnemyPower, Crossed + "attacks that kill another enemy"),
        new("RavenousPower", SourceGroup.EnemyPower, Crossed + "attacks that kill another enemy"),
    };

    private static HashSet<string>? _knownKeys;

    /// <summary>Every tab key in the catalog.</summary>
    public static bool IsKnownTabKey(string key)
    {
        _knownKeys ??= CardTabs.Select(t => t.Key)
            .Concat(TabGroups.Select(t => t.Key))
            .Concat(Sources.Where(s => s.TagKey != null).Select(s => s.TagKey!))
            .ToHashSet();
        return _knownKeys.Contains(key);
    }

    /// <summary>
    /// Logs tracker specs missing from the catalog, so a new relic doesn't silently lack a switch.
    /// (Missing tab keys are logged as they first appear; see QolSettings.TabEnabled.)
    /// </summary>
    public static void Check(Sts2Mods.Core.ModLog log)
    {
        var tracked = Sources.Where(s => s.Tracker != null).Select(s => s.Type).ToHashSet();
        foreach (var spec in Data.RelicSpecs.All.Where(s => s.InTray))
        {
            if (!tracked.Contains(spec.TypeName)) log.Warn($"settings menu: relic tracker {spec.TypeName} isn't in SettingsCatalog");
        }
        foreach (var spec in Data.PowerSpecs.All)
        {
            if (!tracked.Contains(spec.TypeName)) log.Warn($"settings menu: power tracker {spec.TypeName} isn't in SettingsCatalog");
        }
        var dupes = Sources.GroupBy(s => s.Type).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0) log.Warn($"settings menu: duplicate catalog rows {string.Join(", ", dupes)}");
    }
}
