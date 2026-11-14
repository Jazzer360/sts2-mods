using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;

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
        new TabEntry("lethal", "Lethal", "Skull on cards that kill an enemy for certain before it acts: the card's hits, orbs it evokes, procs it sets off, and your end of turn (Juggernaut, orb passives, poison...). Random targets only count when every outcome kills."),
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
        new(nameof(PenNib), SourceGroup.Relic, "Attacks while it's ready: this one deals double", "Attacks played, toward the doubled 10th"),
        new(nameof(ArtOfWar), SourceGroup.Relic, Crossed + "your first attack this turn (gives up its energy next turn)"),
        new(nameof(RippleBasin), SourceGroup.Relic, Crossed + "your first attack this turn (gives up its end-of-turn block)"),
        new(nameof(PaelsTears), SourceGroup.Relic, Crossed + "cards that spend your last energy (gives up next turn's carry-over)"),
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
        new(nameof(BiiigHug), SourceGroup.Relic, "Cards whose draw causes a shuffle: a Soot into your draw pile (crossed)"),
        new(nameof(Metronome), SourceGroup.Relic, "Cards whose channels reach its orb count: damage to all", "Orbs channeled, toward the hit"),
        new(nameof(BoneFlute), SourceGroup.Relic, "Cards that make Osty attack: block"),
        new(nameof(UnceasingTop), SourceGroup.Relic, "Your last card in hand, if it draws nothing"),
        new(nameof(HandDrill), SourceGroup.Relic, "Attacks that break an enemy's block"),
        new(nameof(ParryingShield), SourceGroup.Relic, "Cards that make it fire at end of turn (your block reaches its threshold), crossed when they stop it"),
        new(nameof(GremlinHorn), SourceGroup.Relic, "Attacks that kill an enemy"),
        new(nameof(CentennialPuzzle), SourceGroup.Relic, "Cards that cost you HP (first time this combat)"),
        new(nameof(SelfFormingClay), SourceGroup.Relic, "Cards that cost you HP"),
        new(nameof(DemonTongue), SourceGroup.Relic, "Cards that cost you HP (first time this turn)"),
        new(nameof(EmotionChip), SourceGroup.Relic, "Cards that cost you HP"),
        new(nameof(RedSkull), SourceGroup.Relic, "Cards that take you to half HP or below"),
        new(nameof(BrilliantScarf), SourceGroup.Relic, null, "Cards this turn, toward the free one"),
        new(nameof(VelvetChoker), SourceGroup.Relic, null, "Plays left this turn; red at 1"),
        new(nameof(Pocketwatch), SourceGroup.Relic, null, "Cards this turn: green while under its limit, red once over"),
        new(nameof(StoneCalendar), SourceGroup.Relic, null, "Turn timer: turns until its damage", TrackerDefault: false),
        new(nameof(HappyFlower), SourceGroup.Relic, null, "Turn timer: turns until its energy", TrackerDefault: false),
        new(nameof(FakeHappyFlower), SourceGroup.Relic, null, "Turn timer: turns until its energy", TrackerDefault: false),
        new(nameof(Pendulum), SourceGroup.Relic, null, "Turn timer: turns until its draw", TrackerDefault: false),
        new(nameof(PollinousCore), SourceGroup.Relic, null, "Turn timer: turns until its draw", TrackerDefault: false),
        new(nameof(HornCleat), SourceGroup.Relic, null, "Turn timer: block on turn 2", TrackerDefault: false),
        new(nameof(Candelabra), SourceGroup.Relic, null, "Turn timer: energy on turn 2", TrackerDefault: false),
        new(nameof(CaptainsWheel), SourceGroup.Relic, null, "Turn timer: block on turn 3", TrackerDefault: false),
        new(nameof(Chandelier), SourceGroup.Relic, null, "Turn timer: energy on turn 3", TrackerDefault: false),
        new(nameof(SparklingRouge), SourceGroup.Relic, null, "Turn timer: Strength and Dexterity on turn 3", TrackerDefault: false),

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
        new("StretchPower", SourceGroup.YourPower, "Block cards that spend your Stretch"),
        new(nameof(VigorPower), SourceGroup.YourPower, "Attacks that spend your Vigor (Akabeko's on turn 1)"),
        new(nameof(GigantificationPower), SourceGroup.YourPower, "Attacks that spend a stack of Gigantification"),
        new(nameof(AfterimagePower), SourceGroup.YourPower, "Every card: block per play"),
        new(nameof(RagePower), SourceGroup.YourPower, "Attacks: block"),
        new(nameof(SpiritOfAshPower), SourceGroup.YourPower, "Ethereal cards: block"),
        new(nameof(DanseMacabrePower), SourceGroup.YourPower, "Cards costing 2+ energy: block"),
        new(nameof(FeelNoPainPower), SourceGroup.YourPower, "Cards that exhaust (themselves or others): block per exhaust"),
        new(nameof(ChildOfTheStarsPower), SourceGroup.YourPower, "Star-cost cards: block per star"),
        new(nameof(ShroudPower), SourceGroup.YourPower, "Cards that apply Doom: block per Doom that lands"),
        new(nameof(PillarOfCreationPower), SourceGroup.YourPower, "Cards that create cards: block per card"),
        new(nameof(GravityPower), SourceGroup.YourPower, "Every card: damage to all"),
        new(nameof(SerpentFormPower), SourceGroup.YourPower, "Every card: damage to a random enemy"),
        new(nameof(ThunderPower), SourceGroup.YourPower, "Cards that evoke Lightning: damage to its target again"),
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
        new(nameof(EnvenomPower), SourceGroup.YourPower, "Attacks"),
        new(nameof(ConcoctPower), SourceGroup.YourPower, "Attacks"),
        new(nameof(ReaperFormPower), SourceGroup.YourPower, "Attacks"),
        new(nameof(MonarchsGazePower), SourceGroup.YourPower, "Attacks"),
        new(nameof(JugglingPower), SourceGroup.YourPower, "Your 3rd attack this turn"),
        new(nameof(MasterPlannerPower), SourceGroup.YourPower, "Skills: gain Sly"),
        new(nameof(FeralPower), SourceGroup.YourPower, "0-cost Attacks, while it has charges this turn: back to your hand"),
        new("NostalgiaPower", SourceGroup.YourPower, "Your first Attacks and Skills this turn: onto your draw pile"),
        new(nameof(MonologuePower), SourceGroup.YourPower, "Every card"),
        new(nameof(TheSealedThronePower), SourceGroup.YourPower, "Every card: gain stars"),
        new(nameof(PaleBlueDotPower), SourceGroup.YourPower, "Your 5th card this turn", "Cards this turn, toward 5 (\"done\" once it fired)"),
        new(nameof(DarkEmbracePower), SourceGroup.YourPower, "Cards that exhaust: draw"),
        new(nameof(OrbitPower), SourceGroup.YourPower, "Cards whose energy finishes its count", "Energy spent, toward the next energy"),
        new(nameof(AutomationPower), SourceGroup.YourPower, "Cards whose draws finish its count", "Cards drawn, toward the next energy"),
        new(nameof(CorrosiveWavePower), SourceGroup.YourPower, "Cards that draw"),
        new(nameof(StratagemPower), SourceGroup.YourPower, "Cards whose draw causes a shuffle"),
        new(nameof(ViciousPower), SourceGroup.YourPower, "Cards that apply Vulnerable"),
        new(nameof(ArsenalPower), SourceGroup.YourPower, "Cards that create cards"),
        new(nameof(TrashToTreasurePower), SourceGroup.YourPower, "Cards that create statuses"),
        new(nameof(RupturePower), SourceGroup.YourPower, "Cards that cost you HP: Strength"),
        new(nameof(InfernoPower), SourceGroup.YourPower, "Cards that cost you HP: damage to all"),
        new(nameof(SeekingEdgePower), SourceGroup.YourPower, "Sovereign Blade: hits all enemies"),
        new(nameof(ParryPower), SourceGroup.YourPower, "Sovereign Blade: block"),
        new(nameof(SwordSagePower), SourceGroup.YourPower, "Sovereign Blade: replays"),
        new(nameof(TenderPower), SourceGroup.YourPower, Crossed + "every card"),
        new(nameof(SmoggyPower), SourceGroup.YourPower, Crossed + "skills"),

        // ------------------------------------------------------------------ powers on enemies
        new(nameof(TagTeamPower), SourceGroup.EnemyPower, "Attacks aimed at that enemy play twice", TagPrefix: "double-play:"),
        new(nameof(StranglePower), SourceGroup.EnemyPower, "Yours, on an enemy. Every card: damage to it"),
        new(nameof(OblivionPower), SourceGroup.EnemyPower, "Yours, on an enemy. Every card"),
        new(nameof(ConquerorPower), SourceGroup.EnemyPower, "Yours, on an enemy. Sovereign Blade: more damage to it"),
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
        new(nameof(ImitationLearningPower), SourceGroup.EnemyPower, Crossed + "powers"),
        new(nameof(WitheringPresencePower), SourceGroup.EnemyPower, Crossed + "the card that completes its count: Wither in your hand", "Your cards, toward the next Wither (red)"),
        new(nameof(GalvanicPower), SourceGroup.EnemyPower, Crossed + "Galvanized cards: damage to you"),
        new(nameof(VitalSparkPower), SourceGroup.EnemyPower, Crossed + "Tainted cards"),
        new(nameof(ThornsPower), SourceGroup.EnemyPower, Crossed + "attacks that hit it: damage to you"),
        new(nameof(CurlUpPower), SourceGroup.EnemyPower, Crossed + "attacks that hit it: it gains block"),
        new(nameof(PersonalHivePower), SourceGroup.EnemyPower, Crossed + "attacks that hit it: Dazed in your draw pile"),
        new(nameof(AsleepPower), SourceGroup.EnemyPower, Crossed + "cards after which damage could get through its block by the end of your turn (procs and end-of-turn damage included): wakes it"),
        new(nameof(SlumberPower), SourceGroup.EnemyPower, Crossed + "cards after which enough damage instances could get through its block by the end of your turn: wakes it"),
        new(nameof(SlipperyPower), SourceGroup.EnemyPower, Crossed + "attacks that hit it: HP loss capped at 1"),
        new(nameof(InfestedPower), SourceGroup.EnemyPower, Crossed + "attacks that kill it"),
        new(nameof(SteamEruptionPower), SourceGroup.EnemyPower, Crossed + "attacks that kill it"),
        new(nameof(SurprisePower), SourceGroup.EnemyPower, Crossed + "attacks that kill it"),
        new(nameof(CrabRagePower), SourceGroup.EnemyPower, Crossed + "attacks that kill another enemy"),
        new(nameof(RavenousPower), SourceGroup.EnemyPower, Crossed + "attacks that kill another enemy"),
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
