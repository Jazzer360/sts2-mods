using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using Sts2Mods.Core;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;
using SpireQoL.Menu;

namespace SpireQoL.Diagnostics;

/// <summary>
/// Coverage of the game's combat mechanics: every game class (relic, power, card, monster, orb,
/// potion, enchantment, affliction) that overrides a combat hook (CoverageHooks.txt) must have a rule
/// in the mod, a reviewed reason it doesn't matter to any prediction, or a known-gap note.
///
/// The ledger (CoverageLedger.Data.cs, generated from the decompile by scripts/coverage-ledger.mjs)
/// records each class's hooks for the game version it was made from. When the mod loads, the running
/// game's classes are compared with it (reflection): classes that are new, whose hooks changed, or
/// that nobody reviewed are "unexpected". The first time an unexpected class shows up in a fight
/// (a relic you have, a power on anyone, a card in your hand, an enemy, an orb, an enchantment or
/// affliction on a card) it's logged and reported once. Known gaps are logged, not reported.
/// Card code (draws, channels, block, poison, exhausts, discards, created cards) is checked too:
/// see CardCoverage.
/// </summary>
internal static partial class CoverageLedger
{
    public enum Status { Covered, Ignored, Gap, Unreviewed, New, Changed }

    public sealed record Entry(string Type, Status Status, string Note, string Hooks);

    /// <summary>
    /// Every class with combat hooks, reviewed from its hook bodies in the decompile: "covered: hook →
    /// what handles it in the mod", "ignore: hook → why it can't change a prediction", or "gap: hook →
    /// what isn't modelled yet" (logged when present; the tracker lists them). A class without an entry
    /// is unreviewed and reported when it shows up. Review again when its hooks change in a game update.
    /// </summary>
    private static readonly Dictionary<string, string> Reviewed = new()
    {
        // ======== relics
        ["ArtOfWar"] = "covered: AfterCardPlayed/AfterSideTurnEnd track attacks per turn; crossed tab on the first attack (proc rule)",
        ["BeatingRemnant"] = "covered: ModifyHpLostAfterOsty caps HP lost per turn; simulator (RemnantCap, carries over from your turn)",
        ["BiiigHug"] = "covered: AfterShuffle adds a Soot to the draw pile; crossed shuffle tab, counted as a created status (PlayFacts)",
        ["BingBong"] = "ignore: AfterCardChangedPiles copies cards added to your deck (deck, not combat piles)",
        ["BoneFlute"] = "covered: AfterAttack by Osty gains block; block tab for Osty-attack cards (proc rule, simulator block)",
        ["BookOfFiveRings"] = "ignore: AfterCardChangedPiles heals when cards are added to your deck (deck, not combat)",
        ["BookRepairKnife"] = "ignore: AfterDiedToDoom heals you (predictions are about HP lost)",
        ["BrilliantScarf"] = "covered: AfterCardPlayed counts cards this turn (tracker row)",
        ["BurningSticks"] = "covered: AfterCardExhausted clones the first exhausted Skill (proc tab, created card)",
        ["CaptainsWheel"] = "covered: AfterBlockCleared gains block on turn 3, at the start of your turn (tracker turn timer)",
        ["CentennialPuzzle"] = "covered: AfterDamageReceived draws on your first HP loss (proc tab, PlayChain draws)",
        ["CharonsAshes"] = "covered: AfterCardExhausted damages all enemies (proc damage; simulator for Ethereal exhausts at end of turn)",
        ["ChemicalX"] = "covered: BeforeCardPlayed flashes for X cards (proc tab; X value through Hook.ModifyXValue)",
        ["CloakClasp"] = "covered: BeforeSideTurnEnd block per card in hand (simulator)",
        ["DarkstonePeriapt"] = "ignore: AfterCardChangedPiles max HP when a curse joins your deck (deck, not combat)",
        ["DaughterOfTheWind"] = "covered: AfterCardPlayed block on Attacks (proc block)",
        ["DemonTongue"] = "covered: AfterDamageReceived heals the first HP loss on your turn (proc tab)",
        ["EmotionChip"] = "covered: AfterDamageReceived arms on HP loss (proc tab)",
        ["FakeOrichalcum"] = "covered: BeforeSideTurnEnd block if you had none (simulator, as Orichalcum)",
        ["Fiddle"] = "covered: ShouldDraw blocks draws on your turn (PlayChain: no draws, no shuffle)",
        ["ForgottenSoul"] = "covered: AfterCardExhausted damages a random enemy (proc damage; simulator for Ethereal exhausts)",
        ["FurCoat"] = "ignore: AfterCreatureAddedToCombat sets enemies' HP to 1 in marked rooms (read live)",
        ["GalacticDust"] = "covered: AfterStarsSpent block per stars spent (proc rule, tracker)",
        ["GamePiece"] = "covered: AfterCardPlayed draws on a Power (PlayChain)",
        ["GhostSeed"] = "ignore: AfterCardEnteredCombat makes cards Ethereal (keywords read from the card)",
        ["GremlinHorn"] = "covered: AfterDeath energy and a draw (tab from the simulator's kills)",
        ["HandDrill"] = "covered: AfterBlockBroken Vulnerable (tab from the simulator's block breaks)",
        ["HelicalDart"] = "covered: AfterCardPlayed Dexterity on Shivs (proc tab)",
        ["HistoryCourse"] = "ignore: AfterAutoPrePlayPhaseEntered replays an attack at the start of your turn (before your plays)",
        ["HornCleat"] = "covered: AfterBlockCleared block on turn 2, at the start of your turn (tracker turn timer)",
        ["InfusedCore"] = "covered: ModifyOrbValue raises Lightning (live orbs read through the game; new orbs in SimAdapter.Fresh)",
        ["IntimidatingHelmet"] = "covered: BeforeCardPlayed block on costly cards (proc block)",
        ["IronClub"] = "covered: AfterCardPlayed draws every N cards (PlayChain, tracker)",
        ["IvoryTile"] = "covered: AfterCardPlayed energy on costly cards (proc tab)",
        ["JossPaper"] = "covered: AfterCardExhausted draws at its count (PlayChain); its end-of-turn Ethereal count draws after the turn",
        ["Kunai"] = "covered: AfterCardPlayed Dexterity every N attacks (proc tab, tracker)",
        ["Kusarigama"] = "covered: AfterCardPlayed damages a random enemy every N attacks (proc damage, tracker)",
        ["LavaLamp"] = "ignore: AfterDamageReceived only notes you took damage (end-of-combat upgrades)",
        ["LetterOpener"] = "covered: AfterCardPlayed damages all enemies every N skills (proc damage, tracker)",
        ["LizardTail"] = "covered: ShouldDieLate/AfterPreventingDeath heals you from 0 to 50% of max HP once (simulator: SimPlayer.LizardTailHeal; Incoming Damage's revive row)",
        ["LostWisp"] = "covered: AfterCardPlayed damages all enemies on a Power (proc damage)",
        ["LuckyFysh"] = "ignore: AfterCardChangedPiles gold when cards join your deck (deck, not combat)",
        ["LunarPastry"] = "gap: AfterSideTurnEnd gains stars at the end of your turn; Black Hole's end-of-turn damage from them isn't simulated",
        ["MeatOnTheBone"] = "ignore: AfterCurrentHpChanged only sets its status (heals after combat)",
        ["Metronome"] = "covered: AfterOrbChanneled damages all enemies at its count (proc damage, second pass, tracker)",
        ["MiniRegent"] = "covered: AfterStarsSpent Strength once per turn (proc tab)",
        ["MummifiedHand"] = "covered: AfterCardPlayed frees a random hand card on a Power (proc tab; costs read live)",
        ["MusicBox"] = "covered: Before/AfterCardPlayed clones the first attack as Ethereal (proc tab, created card)",
        ["Nunchaku"] = "covered: AfterCardPlayed energy every N attacks (proc tab, tracker)",
        ["Orichalcum"] = "covered: BeforeSideTurnEnd block if you had none (simulator)",
        ["OrnamentalFan"] = "covered: AfterCardPlayed block every N attacks (proc block, tracker)",
        ["PaelsEye"] = "gap: crossed tab covers giving up its extra turn (ShouldTakeExtraTurnEarly, AfterTakingExtraTurn); its BeforeSideTurnEndEarly exhaust of your hand (no cards played) isn't simulated (Feel No Pain, Charon's Ashes at end of turn)",
        ["PaelsLegion"] = "covered: AfterCardPlayed doubles block of the first block card off cooldown (proc tab, tracker)",
        ["PaelsTears"] = "covered: BeforeSideTurnEnd notes leftover energy (crossed tab on cards spending your last energy)",
        ["ParryingShield"] = "covered: AfterSideTurnEnd damages a random enemy with enough block (simulator)",
        ["PenNib"] = "covered: Before/AfterCardPlayed doubles every 10th attack (proc tab, tracker; damage through the game's math)",
        ["Permafrost"] = "covered: AfterCardPlayed block on the first Power (proc block)",
        ["PhilosophersStone"] = "ignore: AfterCreatureAddedToCombat gives enemies Strength as they enter (intents read live)",
        ["Pocketwatch"] = "covered: AfterCardPlayed counts cards this turn (tracker)",
        ["RainbowRing"] = "covered: AfterCardPlayed Strength and Dexterity on attack + skill + power in a turn (proc tab)",
        ["RazorTooth"] = "covered: AfterCardPlayed upgrades the played card (proc tab)",
        ["RedSkull"] = "covered: AfterCurrentHpChanged Strength at low HP (proc tab on HP-loss cards)",
        ["Regalite"] = "covered: AfterCardGeneratedForCombat block on the first created card per turn (proc block)",
        ["RippleBasin"] = "covered: BeforeSideTurnEnd block if no attack was played (simulator; crossed tab on the first attack)",
        ["ScreamingFlagon"] = "covered: BeforeSideTurnEnd damages all enemies with an empty hand (simulator)",
        ["SelfFormingClay"] = "covered: AfterDamageReceived block next turn on HP loss (proc tab)",
        ["Shuriken"] = "covered: AfterCardPlayed Strength every N attacks (proc tab, tracker)",
        ["SneckoSkull"] = "covered: ModifyPowerAmountGivenAdditive +1 poison per application (simulator: card poison, Envenom)",
        ["SparklingRouge"] = "covered: AfterBlockCleared Strength and Dexterity on turn 3, at the start of your turn (tracker turn timer)",
        ["StoneCalendar"] = "covered: BeforeSideTurnEnd damages all enemies on its turn (simulator, tracker)",
        ["SturdyClamp"] = "ignore: ShouldClearBlock keeps up to 10 block into your next turn",
        ["TheAbacus"] = "covered: AfterShuffle block (proc block on cards that shuffle)",
        ["TheBoot"] = "covered: ModifyHpLostAfterOstyLate raises your attack hits below its minimum to it (simulator BootMinimum, after Hardened Shell and Slippery)",
        ["Tingsha"] = "covered: AfterCardDiscarded damages a random enemy on your turn (proc damage)",
        ["ToughBandages"] = "covered: AfterCardDiscarded block on your turn (proc block)",
        ["TungstenRod"] = "covered: ModifyHpLostAfterOsty reduces HP lost (simulator)",
        ["TuningFork"] = "covered: AfterCardPlayed block every N skills (proc block, tracker)",
        ["UnceasingTop"] = "covered: AfterHandEmptied draws (proc tab)",
        ["UnsettlingLamp"] = "covered: proc tab on the card that arms it (temporary debuffs too); ModifyPowerAmountGivenMultiplicative doubles that card's first play's debuffs: the adapter doubles its Strength loss and poison (random poison, Bouncing Flask, isn't doubled)",
        ["Vambrace"] = "covered: AfterCardPlayed doubles the first block card (proc tab)",
        ["VelvetChoker"] = "covered: ShouldPlay limits cards per turn (tracker; playability read live)",
        ["WhisperingEarring"] = "ignore: AfterAutoPrePlayPhaseEnteredLate plays your turn-1 hand (before your plays)",
        // v0.113.0
        ["Byrdpip"] = "ignore: AfterCardPlayed counts Byrd Swoop plays toward its rest-site evolve option (out of combat)",
        ["ConductorsBaton"] = "covered: AfterCardPlayed applies Conductor's Baton (powered attacks on you deal 75% until your next turn) on the card that brings you to three melodies this turn (proc tab); once applied, intents read it through the game's damage hooks",
        ["Fertilizer"] = "gap: AfterCardGeneratedForCombat gives Strength when you create a Sadness: no proc tab on the cards that create one",
        ["MightyWings"] = "gap: AfterDamageGiven gives 2 Strength when your (or Osty's) powered attack deals 30 or more in one hit: no proc tab",
        ["ObsidianVice"] = "covered: BeforeSideTurnEnd deals its damage per card in your hand to a random enemy (simulator, yours and teammates')",
        ["Storyteller"] = "ignore: BeforeSideTurnEnd gives Aim, which only adds to your arrow attacks (read through the game's damage hooks on your next turn); AfterRoomEntered gives it at the start of combat",
        ["Thumbscrew"] = "covered: ModifyAttackHitCount adds a hit to your multi-hit attacks (applied through Hook.ModifyAttackHitCount in PlayFacts.HitsPerPlay, as the card preview does)",
        ["Twine"] = "covered: AfterArrowDislodged gains block per arrow of yours dislodged (proc tab: block when every aim dislodges, Fool.Dislodged)",
        ["WhittlingKnife"] = "gap: AfterShuffle gives Aim when you shuffle: no tab on cards that shuffle",
        ["Wuppy"] = "covered: AfterCurrentHpChanged/BeforeCombatStart keep a pet Wuppy that takes the powered attacks aimed at you while it lives (WuppysLoyaltyPower: simulator, your Wuppy; a teammate's isn't modelled)",

        // ======== powers
        ["AdaptablePower"] = "gap: AfterDeath/ShouldAllowHitting: Test Subject revives (a kill may not stick: lethal can be wrong)",
        ["AfterimagePower"] = "covered: AfterCardPlayed block per card (proc block)",
        ["ArsenalPower"] = "covered: AfterCardGeneratedForCombat Strength per created card (proc tab)",
        ["AsleepPower"] = "covered: AfterDamageReceived wakes it on damage through block (wake tab from the simulator)",
        ["AutomationPower"] = "covered: AfterCardDrawn energy every N draws (proc tab, tracker, PlayChain draws)",
        ["BarricadePower"] = "covered: ShouldClearBlock keeps enemy block (simulator Barricade)",
        ["BattlewornDummyTimeLimitPower"] = "ignore: AfterSideTurnEnd the dummy escapes",
        ["BeaconOfHopePower"] = "gap: multiplayer: AfterBlockGained gives teammates half of each block its owner gains on the players' side; a teammate's gives you block from their end-of-turn block (Plating, Frost, Orichalcum), not simulated (Incoming Damage errs high); yours changes nothing for you",
        ["BlackHolePower"] = "covered: AfterCardPlayed/AfterStarsGained damages all enemies on stars (proc damage, second pass)",
        ["BlockNextTurnPower"] = "ignore: AfterBlockCleared block at the start of your next turn",
        ["BlurPower"] = "ignore: ShouldClearBlock keeps your block into your next turn",
        ["BorrowedTimePower"] = "ignore: AfterSideTurnEnd removes itself",
        ["BufferPower"] = "covered: ModifyHpLostAfterOstyLate stops HP loss (simulator Buffer)",
        ["BurrowedPower"] = "covered: AfterBlockBroken stuns, ShouldClearBlock keeps block (simulator)",
        ["BurstPower"] = "covered: double plays (Hook.ModifyCardPlayCount; double-play tab); AfterSideTurnEnd removes itself",
        ["CacophonyPower"] = "covered: AfterCardDrawn damages a random enemy every N draws (proc damage, tracker)",
        ["CalamityPower"] = "covered: Before/AfterCardPlayed creates a card per attack (proc tab)",
        ["ChainsOfBindingPower"] = "covered: Bound afflictions on drawn cards (draw-reactive: noted by the audit); playability read live",
        ["ChildOfTheStarsPower"] = "covered: AfterStarsSpent block per star (proc block)",
        ["ColossusPower"] = "ignore: AfterSideTurnEnd ticks down (its effect is in the game's damage math)",
        ["ConcoctPower"] = "covered: AfterDamageGiven poison on attack hits through block (proc tab; simulator, as Envenom)",
        ["ConfusedPower"] = "ignore: AfterCardDrawn random costs (costs read live; draws are random)",
        ["ConquerorPower"] = "covered: Sovereign Blade tab; AfterSideTurnEnd ticks down",
        ["ConstrictPower"] = "covered: AfterSideTurnEnd damages you (simulator, blockable debuff)",
        ["ConsumingShadowPower"] = "covered: AfterSideTurnEnd evokes your last orb (simulator)",
        ["CorrosiveWavePower"] = "covered: AfterCardDrawn poisons all enemies per draw (proc tab)",
        ["CorruptionPower"] = "covered: ModifyCardPlayResultLocation exhausts played Skills (PlayFacts.ExhaustsSelf)",
        ["CoveredPower"] = "covered: ModifyDamageMultiplicative zeroes powered attacks on the covered player: the intent's damage to you is read through the game's damage hooks; AfterDeath/AfterSideTurnEnd remove it",
        ["CrabRagePower"] = "covered: AfterDeath of an ally gives Strength and block (simulator; crossed kill tab)",
        ["CurlUpPower"] = "covered: block after a card's attack hits it (simulator; crossed tab)",
        ["DampenPower"] = "ignore: AfterDeath ends its card downgrade (cards read live)",
        ["DanseMacabrePower"] = "covered: BeforeCardPlayed block on costly cards (proc block)",
        ["DarkEmbracePower"] = "covered: AfterCardExhausted draws (PlayChain); Ethereal exhausts draw after your turn",
        ["DebilitatePower"] = "covered: doubles Weak's effect (Weak intents in SimAdapter.AdjustedHits); AfterSideTurnEnd ticks down",
        ["DemisePower"] = "covered: AfterSideTurnEnd HP loss (simulator, unblockable debuff)",
        ["DevourLifePower"] = "covered: AfterCardPlayed summons on Souls (proc summon)",
        ["DieForYouPower"] = "covered: Osty takes your attacks (simulator OstyHp)",
        ["DisintegrationPower"] = "covered: AfterSideTurnEndLate damages you (simulator, late blockable debuff)",
        ["DoomPower"] = "ignore: Doom kills at the end of the enemies' own turn, after they attack (BeforeSideTurnEnd, enemy side)",
        ["DoubleDamagePower"] = "ignore: AfterSideTurnEnd ticks down (damage math)",
        ["DuplicationPower"] = "covered: double plays (Hook.ModifyCardPlayCount; double-play tab); AfterSideTurnEnd removes itself",
        ["EchoFormPower"] = "covered: double plays (Hook.ModifyCardPlayCount; double-play tab)",
        ["EnragePower"] = "covered: AfterCardPlayed Strength on Skills (simulator intents; crossed tab)",
        ["EnvenomPower"] = "covered: AfterDamageGiven poison on attack hits through block (simulator)",
        ["EscapeArtistPower"] = "ignore: AfterSideTurnEnd ticks down",
        ["FeelNoPainPower"] = "covered: AfterCardExhausted block (proc block; simulator for Ethereal exhausts)",
        ["FeralPower"] = "covered: ModifyCardPlayResultLocation returns 0-cost attacks to your hand (proc tab)",
        ["FlameBarrierPower"] = "covered: AfterDamageReceived retaliates (simulator)",
        ["FlankingPower"] = "ignore: AfterSideTurnEnd removes itself (damage math)",
        ["FlutterPower"] = "covered: AfterDamageReceived stuns after enough hits (simulator)",
        ["FocusPower"] = "covered: ModifyOrbValue (live orbs through the game; new orbs in SimAdapter.Fresh)",
        ["FrailPower"] = "ignore: AfterSideTurnEnd ticks down (block math)",
        ["FreeAttackPower"] = "ignore: BeforeCardPlayed uses a charge (costs read live)",
        ["FreePowerPower"] = "ignore: BeforeCardPlayed uses a charge (costs read live)",
        ["FreeSkillPower"] = "ignore: BeforeCardPlayed uses a charge (costs read live)",
        ["GalvanicPower"] = "covered: AfterCardPlayed damages you for Galvanized cards (crossed proc damage)",
        ["GigantificationPower"] = "covered: triples one attack through ModifyDamageMultiplicative (in the card's damage preview); AfterAttack takes a stack (tab on attacks that spend it; a replay's later plays without it once it runs out)",
        ["GravityPower"] = "covered: AfterCardPlayed damages all enemies (proc damage)",
        ["GuardedPower"] = "ignore: AfterDeath removes itself",
        ["HailstormPower"] = "covered: BeforeSideTurnEnd damages all enemies with enough Frost (simulator)",
        ["HardenedShellPower"] = "covered: ModifyHpLostBeforeOstyLate caps HP lost per turn (simulator ShellLeft)",
        ["HatchPower"] = "ignore: AfterSideTurnEnd counts down to hatching (a later turn)",
        ["HauntPower"] = "covered: AfterCardPlayed damages a random enemy on Souls (proc damage)",
        ["HeistPower"] = "ignore: BeforeDeath returns stolen gold",
        ["HellraiserPower"] = "covered: auto-plays drawn Strikes (draw-reactive and auto-play: noted by the audit, not predicted)",
        ["HexPower"] = "ignore: afflicts cards (read from the card)",
        ["HighVoltagePower"] = "ignore: AfterSideTurnEnd Strength at the end of its own turn (later attacks)",
        ["IllusionPower"] = "gap: AfterDeath/ShouldAllowHitting revives (a kill may not stick: lethal can be wrong)",
        ["ImbalancedPower"] = "covered: AfterDamageGiven stuns on a fully blocked hit (Imbalanced tab from the simulator)",
        ["ImitationLearningPower"] = "covered: copies your Powers (crossed proc tab)",
        ["InfernoPower"] = "covered: AfterDamageReceived damages all enemies (unpowered) each time you lose HP on your turn (proc tab on cards that cost HP; simulator: end-of-turn and in-play HP loss, yours; a teammate's isn't modelled). AfterPlayerTurnStart's self-damage is next turn",
        ["InfestedPower"] = "covered: AfterDeath spawns Wrigglers (crossed kill tab from the simulator)",
        ["IntangiblePower"] = "covered: ModifyHpLostAfterOsty caps hits at 1 (simulator HitCap); AfterSideTurnEnd ticks down",
        ["InterceptPower"] = "covered: ModifyDamageMultiplicative multiplies powered attacks on its owner by covered players + 1: read through the game's damage hooks for whoever it's on",
        ["IterationPower"] = "covered: AfterCardDrawn draws on the first status drawn (draw-reactive: noted by the audit)",
        ["JuggernautPower"] = "covered: AfterBlockGained damages a random enemy (simulator)",
        ["JugglingPower"] = "covered: BeforeCardPlayed marks your third attack this turn, AfterCardPlayed copies it into your hand once it resolves (proc tab)",
        ["KnockdownPower"] = "ignore: AfterSideTurnEnd removes itself",
        ["MagicBombPower"] = "covered: AfterSideTurnEnd damages you (simulator, blockable debuff)",
        ["MasterPlannerPower"] = "covered: AfterCardPlayed makes Skills Sly (proc tab)",
        ["MayhemPower"] = "ignore: AfterAutoPrePlayPhaseEntered auto-plays at the start of your turn",
        ["MonarchsGazePower"] = "gap: AfterDamageGiven lowers Strength per hit (simulator intents for single-target and all-enemy attacks; not random-target ones)",
        ["MonologuePower"] = "covered: AfterCardPlayed Strength per card (proc tab)",
        ["NecroMasteryPower"] = "covered: AfterCurrentHpChanged damages all enemies when Osty loses HP: a card that kills Osty (proc tab; simulator). Not modelled: Osty losing HP to an enemy's Thorns or attacks",
        ["NemesisPower"] = "ignore: AfterSideTurnEnd Intangible at the end of its own turn (your next turn)",
        ["NoBlockPower"] = "ignore: AfterSideTurnEnd ticks down (block math)",
        ["NoDrawPower"] = "covered: ShouldDraw blocks draws (PlayChain)",
        ["NoEnergyGainPower"] = "ignore: AfterSideTurnEnd removes itself",
        ["NostalgiaPower"] = "covered: ModifyCardPlayResultLocation puts your first Amount attacks and skills each turn on top of your draw pile (proc tab); later plays read piles live",
        ["OblivionPower"] = "covered: AfterCardPlayed Doom per card (proc tab)",
        ["OneTwoPunchPower"] = "covered: double plays (Hook.ModifyCardPlayCount; double-play tab); AfterSideTurnEnd removes itself",
        ["OrbitPower"] = "covered: AfterEnergySpent energy every N spent (proc tab, tracker)",
        ["PagestormPower"] = "covered: AfterCardDrawn draws on Ethereal draws (draw-reactive: noted by the audit)",
        ["PainfulStabsPower"] = "ignore: AfterAttack adds Wounds during its attack",
        ["PaleBlueDotPower"] = "covered: AfterCardPlayed draws next turn after 5 cards (proc tab, tracker)",
        ["PanachePower"] = "covered: AfterCardPlayed damages all enemies every 5 cards (proc damage, tracker)",
        ["PaperCutsPower"] = "ignore: AfterDamageGiven max HP",
        ["PersonalHivePower"] = "covered: AfterDamageReceived Dazed into your draw pile (crossed proc tab)",
        ["PillarOfCreationPower"] = "covered: AfterCardGeneratedForCombat block per created card (proc block)",
        ["PlatingPower"] = "covered: BeforeSideTurnEndEarly block (simulator)",
        ["PlowPower"] = "covered: AfterDamageReceived stuns at its HP threshold (simulator)",
        ["PossessSpeedPower"] = "ignore: returns stolen Dexterity when it dies",
        ["PossessStrengthPower"] = "ignore: returns stolen Strength when it dies",
        ["RagePower"] = "covered: AfterCardPlayed block on attacks (proc block)",
        ["RavenousPower"] = "covered: AfterDeath of an ally stuns it (simulator; crossed kill tab)",
        ["ReaperFormPower"] = "covered: AfterDamageGiven Doom on attack damage (proc tab; Doom kills after the enemy turn)",
        ["ReattachPower"] = "gap: AfterDeath/ShouldAllowHitting segments revive (a kill may not stick: lethal can be wrong)",
        ["ReboundPower"] = "ignore: ModifyCardPlayResultLocation puts played cards on your draw pile after their effects (later plays read piles live)",
        ["ReflectPower"] = "covered: AfterDamageReceived reflects blocked damage (simulator)",
        ["RegenPower"] = "ignore: BeforeSideTurnEndEarly heals",
        ["RetainHandPower"] = "ignore: AfterSideTurnEnd ticks down",
        ["RingingPower"] = "ignore: ShouldPlay one Ringing card per turn (playability read live)",
        ["RitualPower"] = "ignore: AfterSideTurnEnd Strength at the end of its own turn (later attacks)",
        ["RupturePower"] = "covered: Strength on HP loss from your cards (proc tab)",
        ["SandpitPower"] = "ignore: these hooks only move creatures and change the music",
        ["SelfFormingClayPower"] = "ignore: AfterBlockCleared block next turn",
        ["SerpentFormPower"] = "covered: AfterCardPlayed damages a random enemy per card (proc damage)",
        ["ShadowmeldPower"] = "ignore: AfterSideTurnEnd removes itself",
        ["ShriekPower"] = "covered: AfterDamageReceived stuns at its HP threshold (simulator)",
        ["ShrinkPower"] = "ignore: ticks down (damage math)",
        ["ShroudPower"] = "covered: AfterPowerAmountChanged block when you apply Doom (proc block)",
        ["SicEmPower"] = "ignore: AfterDamageGiven summons Osty when Osty hits it (Osty read live)",
        ["SkittishPower"] = "covered: AfterAttack block when a card's attack gets through, once per turn (simulator; crossed tab)",
        ["SleightOfFleshPower"] = "covered: AfterPowerAmountChanged damages enemies you debuff (proc damage)",
        ["SlipperyPower"] = "covered: ModifyHpLostAfterOsty 1 HP per hit while it has charges (simulator)",
        ["SlothPower"] = "ignore: ShouldPlay limits cards per turn (playability read live)",
        ["SlowPower"] = "covered: counts cards for the damage math (proc tab)",
        ["SlumberPower"] = "covered: AfterDamageReceived takes a stack per hit through block and wakes it stunned on the last (simulator: SimEnemy.Slumber; wake tab)",
        ["SmoggyPower"] = "covered: Smog makes Skills unplayable after one (crossed proc tab)",
        ["SmokestackPower"] = "covered: AfterCardGeneratedForCombat damages all enemies per status (proc damage)",
        ["SneakyPower"] = "covered: AfterCardPlayed block on your attacks (simulator; crossed tab)",
        ["SoulboundPower"] = "gap: AfterCardGeneratedForCombat adds Souls when you create Souls (created-card counts)",
        ["SpeedsterPower"] = "covered: AfterCardDrawn damages all enemies per draw (proc damage, PlayChain draws)",
        ["SpiritOfAshPower"] = "covered: BeforeCardPlayed block on Ethereal cards (proc block)",
        ["StampedePower"] = "gap: AfterAutoPostPlayPhaseEntered auto-plays random Attacks at the end of your turn (end-of-turn damage not simulated)",
        ["SteamEruptionPower"] = "covered: AfterDeath effect (crossed kill tab from the simulator)",
        ["StockPower"] = "gap: AfterDeath spawns another Axebot (the fight isn't over when it dies)",
        ["StormPower"] = "covered: AfterCardPlayed channels Lightning per Power (proc tab; simulator channels)",
        ["StranglePower"] = "covered: AfterCardPlayed damages it per card (proc damage)",
        ["StratagemPower"] = "covered: AfterShuffle picks cards (proc tab on shuffles)",
        ["SubroutinePower"] = "covered: AfterCardPlayed energy on Powers (proc tab)",
        ["SuckPower"] = "ignore: AfterAttack Strength after its own attack (later attacks)",
        ["SurprisePower"] = "covered: AfterDeath effect (crossed kill tab from the simulator)",
        ["SurroundedPower"] = "covered: BeforeCardPlayed turns you to face a back-attacking target; ModifyDamageMultiplicative: the one behind you hits 50% harder (intents after your targeted play; potions and deaths aren't predicted)",
        ["SwipePower"] = "ignore: BeforeDeath returns a stolen card",
        ["SwordSagePower"] = "covered: Sovereign Blade replays (double-play row, Sovereign Blade tab)",
        ["TaintedPower"] = "ignore: AfterSideTurnEnd removes itself",
        ["TangledPower"] = "ignore: afflicts attacks (read from the card)",
        ["TemporaryDexterityPower"] = "ignore: expires at the end of the turn, after your end-of-turn block",
        ["TemporaryFocusPower"] = "ignore: expires at the end of the turn, after your orbs' passives",
        ["TemporaryStrengthPower"] = "ignore: expires at the end of the turn (Strength-loss cards: simulator intents)",
        ["TenderPower"] = "covered: AfterCardPlayed Strength and Dexterity down per card (crossed proc tab)",
        ["TerritorialPower"] = "ignore: AfterSideTurnEnd Strength at the end of its own turn (later attacks)",
        ["TheBombPower"] = "covered: BeforeSideTurnEnd damages all enemies on its last tick (simulator)",
        ["TheGambitPower"] = "covered: AfterDamageReceived dies to an attack through block (simulator)",
        ["TheSealedThronePower"] = "covered: BeforeCardPlayed stars per card (proc tab, Black Hole second pass)",
        ["ThornsPower"] = "covered: BeforeDamageReceived damages attackers (crossed proc damage and simulator block loss for enemies' Thorns; simulator for yours)",
        ["ThunderPower"] = "covered: AfterOrbEvoked damages Lightning's living targets again (proc tab on cards that evoke Lightning; simulator)",
        ["ToricToughnessPower"] = "ignore: AfterBlockCleared block at the start of its own turn",
        ["TrashToTreasurePower"] = "covered: AfterCardGeneratedForCombat channels a random orb per status (proc tab; simulator channels an unknown orb)",
        ["UnderworldPower"] = "ignore: AfterDamageGiven Doom from other players' attacks (Doom kills after the enemy turn)",
        ["VeilpiercerPower"] = "ignore: BeforeCardPlayed uses a charge",
        ["ViciousPower"] = "covered: AfterPowerAmountChanged draws when you apply Vulnerable (PlayChain)",
        ["VigorPower"] = "covered: its bonus is in the game's damage math (card preview); AfterAttack takes it all (tab on attacks that spend it; a replay's later plays without it)",
        ["VitalSparkPower"] = "covered: Tainted cards (crossed proc tab)",
        ["VoidFormPower"] = "ignore: costs (read live)",
        ["VulnerablePower"] = "ignore: AfterSideTurnEnd ticks down (damage math)",
        ["WeakPower"] = "ignore: AfterSideTurnEnd ticks down (damage math; Weak you apply: simulator intents)",
        ["WitheringPresencePower"] = "covered: AfterCardPlayed Wither every N cards (crossed proc tab, tracker)",
        // v0.113.0
        ["AimPower"] = "ignore: ModifyDamageAdditive adds to your arrow attacks (read through the game's damage hooks); BeforeAttack/AfterAttack spend it after that attack",
        ["AllIsWellPower"] = "gap: AfterCardGeneratedForCombat gains block when you create a Sadness: no proc tab, not simulated",
        ["AmnesiaPower"] = "ignore: AfterPlayerTurnStart energy loss and ModifyHandDraw at the start of your turn; AfterSideTurnEnd removes it",
        ["CompostPower"] = "gap: AfterDeath gives your side's players energy when an enemy dies: no proc tab on kills",
        ["CoveringFirePower"] = "gap: AfterArrowShot gives your teammates block when you shoot an arrow from a card: no proc tab; their block isn't simulated before the enemy turn",
        ["DislikeYouPower"] = "ignore: ModifyDamageMultiplicative raises arrow damage to its owner (read through the game's damage hooks); AfterSideTurnEnd removes it",
        ["EncorePower"] = "ignore: ShouldPlay limits the plays left this turn (playability read live); BeforeCardPlayed/AfterCardPlayed count them; AfterSideTurnEnd removes it",
        ["EnsemblePower"] = "ignore: TryModifyEnergyCostInCombatLate makes teammates' cards free (costs read live); AfterCardPlayed counts down on their plays",
        ["ExplosiveArrowsPower"] = "covered: AfterArrowImpaled deals its amount to every enemy when your shot impales (proc damage: simulator)",
        ["ExplosivePower"] = "gap: AfterDeath damages every living player when its owner dies: no crossed tab on the kill, not in Incoming Damage",
        ["ExtraTurnPower"] = "gap: ShouldTakeExtraTurn gives you another turn before the enemies act (AfterTakingExtraTurn counts it down): the forecast still shows the enemy turn next",
        ["FreeCardPower"] = "ignore: TryModifyEnergyCostInCombatLate makes your next card free (costs read live); BeforeCardPlayed uses it up",
        ["ImpaledPower"] = "covered: AfterDamageReceived counts the applier's powered hits and dislodges an arrow every third; AfterDeath dislodges them all (Fool.DislodgesOn: Twine and Yah tabs)",
        ["InsatiableDominionPower"] = "gap: AfterSideTurnEndLate deals 2 + turn number to its owner (you, in a Dread run) after your end of turn: not in Incoming Damage",
        ["KiVersePower"] = "covered: ModifyCardPlayCount replays your Wah, Cho and Ki cards (read through the game's play count: replay tab); AfterModifyingCardPlayCount/AfterSideTurnEnd remove it",
        ["LagavulinMatriarchDominionPower"] = "ignore: AfterSideTurnEnd lowers its owner's Strength and Dexterity after your end of turn (block already gained) and goes",
        ["LostLutePower"] = "gap: AfterCardPlayed auto-plays cards from the top of your draw pile on your first Wah, Cho and Ki each turn: proc tab; what those cards do is hidden (not simulated)",
        ["LullabyPower"] = "covered: AfterCardPlayed gains block once per turn on the card that completes Wah, Cho and Ki (proc block: simulator)",
        ["MotifPower"] = "covered: AfterCardPlayed draws its amount per melody (Wah, Cho, Ki) the card plays first this turn: proc tab; the draws count for the Shuffle Warning and draw procs (PlayChain)",
        ["QuietusPower"] = "ignore: ModifyDamageMultiplicative multiplies your next arrow attack (read through the game's damage hooks); AfterArrowShot breaks shot arrows on impact; BeforeAttack/AfterAttack/AfterSideTurnEnd remove it",
        ["ShowOffPower"] = "ignore: AfterDeath raises Show Off's Aim when an enemy dies (read from the card on its next play); AfterSideTurnEnd removes it",
        ["SimpletonPower"] = "gap: AfterAutoPostPlayPhaseEntered auto-plays your cheapest card(s) at the end of your turn (a random one among ties): not simulated",
        ["SongPathPower"] = "covered: AfterCardPlayed gains block on your first Cho each turn (proc block: simulator)",
        ["StretchPower"] = "covered: ModifyBlockAdditive adds to your next powered block card's block (read through the game's block hooks; a replay's later plays without it in the simulator); BeforeCardPlayed/AfterCardPlayed spend it (Stretch tab on block cards that spend it, checked against the amount after the play)",
        ["TemporaryThornsPower"] = "covered: BeforeApplied/AfterPowerAmountChanged apply Thorns (read as Thorns: simulator); AfterSideTurnEnd removes it after the other side's turn",
        ["TunePower"] = "ignore: AfterSideTurnEnd gives a free card next turn when you played a Ki",
        ["ViolentChordPower"] = "covered: AfterCardPlayed damages every enemy on your first Wah each turn (proc damage: simulator)",
        ["WuppysLoyaltyPower"] = "covered: ModifyPreBlockDamageTarget sends powered attacks on its pet owner to Wuppy while it lives, whole, before block (simulator: SimPlayer.WuppyHp, for you; a teammate's Wuppy isn't modelled)",
        ["YahPower"] = "covered: AfterArrowDislodged gives energy once per turn when your arrow is dislodged (proc tab when some aim dislodges, Fool.Dislodged, or the simulator has the play kill an enemy holding your arrows in every branch)",

        // ======== cards
        ["BansheesCry"] = "ignore: its own cost (read from the card)",
        ["Bombardment"] = "ignore: auto-plays at the start of your turn",
        ["DrumOfBattle"] = "ignore: AfterCardExhausted energy when it's exhausted",
        ["Enthralled"] = "ignore: playability (CanPlay)",
        ["Flatten"] = "ignore: its own cost after Osty attacks (read from the card)",
        ["HowlFromBeyond"] = "gap: AfterAutoPostPlayPhaseEntered auto-plays at the end of your turn (end-of-turn damage not simulated)",
        ["IAmInvincible"] = "gap: AfterAutoPostPlayPhaseEntered auto-plays at the end of your turn (end-of-turn block not simulated)",
        ["KinglyKick"] = "ignore: its own cost on draw (read from the card)",
        ["KinglyPunch"] = "ignore: its own cost on draw (read from the card)",
        ["MakeItSo"] = "gap: AfterCardPlayedLate returns to your hand after enough plays (hand counts)",
        ["Melancholy"] = "ignore: its own cost when an enemy dies (read from the card)",
        ["Midnight"] = "ignore: its own cost (read from the card)",
        ["Normality"] = "ignore: playability (CanPlay)",
        ["Pinpoint"] = "ignore: its own cost (read from the card)",
        ["Regret"] = "covered: BeforeSideTurnEnd HP loss per card in hand (simulator)",
        ["RightHandHand"] = "ignore: AfterCardPlayedLate returns itself to your hand (hand read live)",
        ["RocketPunch"] = "ignore: its own cost when statuses are created (read from the card)",
        ["SovereignBlade"] = "ignore: AfterCardChangedPiles forge visuals",
        ["Stomp"] = "ignore: its own cost (read from the card)",
        ["Void"] = "ignore: AfterCardDrawn energy loss on a random draw",
        // v0.113.0
        ["EpicRetrieval"] = "gap: AfterArrowDislodged gains energy when its own hit dislodges your arrow (the card glows gold then): no proc tab",
        ["SpoilsMap"] = "ignore: AfterCardChangedPiles keeps its map quest (out of combat)",
        ["WhistlingArrows"] = "ignore: AfterArrowShot returns it from your discard pile to your hand (hand read live)",

        // ======== monsters
        ["Aeonglass"] = "ignore: hooks set music and match created Withers to its upgrades",
        ["Crusher"] = "ignore: hooks only change music",
        ["DecimillipedeSegment"] = "ignore: hook only changes a texture (its revive is ReattachPower)",
        ["KinPriest"] = "gap: AfterDeath of its last follower changes its move mid-turn (the simulator keeps the intent from the start of the play)",
        ["LagavulinMatriarch"] = "ignore: AfterDamageReceived only marks its shell awake (visual)",
        ["Queen"] = "gap: AfterDeath of the Amalgam switches her to the enraged move mid-turn (the simulator keeps the intent from the start of the play)",
        ["Rocket"] = "ignore: hooks only change music",
        ["SoulFysh"] = "ignore: hooks only change music",
        ["TestSubject"] = "ignore: hooks only change color and music (its revive is AdaptablePower)",
        ["TheInsatiable"] = "ignore: hook only changes music",
        ["Vantom"] = "ignore: hook only changes music",
        ["WaterfallGiant"] = "ignore: hook only changes music",

        // ======== potions, enchantments, afflictions
        ["FairyInABottle"] = "covered: ShouldDie (before Lizard Tail's ShouldDieLate)/AfterPreventingDeath heals you from 0 to 30% of max HP, used up (simulator: SimPlayer.Fairies; Incoming Damage's revive row)",
        ["Glam"] = "ignore: AfterCardPlayed only marks it used (its replay is read through the game: replay tab)",
        ["Goopy"] = "ignore: AfterCardPlayed grows its amount (its bonus is read through the game)",
        ["Imbued"] = "ignore: auto-plays at the start of your turn",
        ["Slither"] = "ignore: AfterCardDrawn random cost on draw (costs read live)",
        ["Vigorous"] = "ignore: AfterCardPlayed disables it after the first play (its bonus is read through the game)",
        // v0.113.0: OnPlay acts on every play of the card (replays too) until AfterCardPlayed disables it
        ["Sown"] = "gap: OnPlay energy, on each play of the card until it finishes (AfterCardPlayed): enchantment energy isn't counted (the play check explains it)",
        ["Swift"] = "covered: OnPlay draws its amount the first time the card is played (AfterCardPlayed disables it): counted in the card's own draws",
        ["Hexed"] = "ignore: clears itself",
    };

    private static readonly string[] Folders = { "Relics", "Powers", "Cards", "Monsters", "Orbs", "Potions", "Enchantments", "Afflictions" };

    private static Dictionary<Type, Entry>? _byType;

    /// <summary>The running game's classes against the ledger (built once).</summary>
    /// <remarks>Keyed by type: two classes can share a name in different folders (Pael's Legion the relic and the monster).</remarks>
    public static IReadOnlyDictionary<Type, Entry> ByType => _byType ??= Build();

    private static Dictionary<Type, Entry> Build()
    {
        var hookSet = Hooks.ToHashSet();
        var ledger = Generated.ToDictionary(g => (g.Folder, g.Type), g => g);
        var result = new Dictionary<Type, Entry>();
        foreach (var t in ModelTypes())
        {
            string hooks;
            try
            {
                hooks = string.Join(",", t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(m => hookSet.Contains(m.Name) && m.IsVirtual && m.GetBaseDefinition().DeclaringType != t)
                    .Select(m => m.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal));
            }
            catch
            {
                continue;
            }
            var known = ledger.TryGetValue((t.Namespace?.Split('.').Last() ?? "", t.Name), out var g);
            if (!known && hooks.Length == 0) continue;
            Entry e;
            if (!known) e = new Entry(t.Name, Status.New, "not in the ledger (new in this game version?)", hooks);
            else if (g.Hooks != hooks) e = new Entry(t.Name, Status.Changed, $"hooks were {g.Hooks}, now {hooks}", hooks);
            else if (Reviewed.TryGetValue(t.Name, out var why))
                e = new Entry(t.Name, why.StartsWith("gap:") ? Status.Gap : why.StartsWith("covered:") ? Status.Covered : Status.Ignored, why, hooks);
            else e = new Entry(t.Name, Status.Unreviewed, "not reviewed", hooks);
            result[t] = e;
        }
        return result;
    }

    /// <summary>The game's model classes the coverage check and the audits read (relics, powers, cards...).</summary>
    public static IEnumerable<Type> ModelTypes()
    {
        Type[] all;
        try { all = typeof(AbstractModel).Assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { all = ex.Types.Where(t => t != null).ToArray()!; }
        return all.Where(t => t is { IsAbstract: false, IsClass: true } && typeof(AbstractModel).IsAssignableFrom(t)
                              && Folders.Any(f => t.Namespace == "MegaCrit.Sts2.Core.Models." + f));
    }

    /// <summary>One summary line at load: the game's version against the ledger's, and the counts.</summary>
    public static void LogSummary(ModLog log, Sts2Mods.Core.Diagnostics.ErrorReporter? reporter)
    {
        try
        {
            var game = SafeGameVersion();
            var by = ByType.Values.GroupBy(e => e.Status).ToDictionary(g => g.Key, g => g.ToList());
            int N(Status s) => by.TryGetValue(s, out var l) ? l.Count : 0;
            log.Info($"[coverage] game {game}, ledger {LedgerGameVersion}: {ByType.Count} classes with combat hooks; covered {N(Status.Covered)}, ignored {N(Status.Ignored)}, " +
                     $"known gaps {N(Status.Gap)}, unreviewed {N(Status.Unreviewed)}, new {N(Status.New)}, changed {N(Status.Changed)}.");
            foreach (var s in new[] { Status.New, Status.Changed, Status.Unreviewed })
                if (by.TryGetValue(s, out var l))
                    log.Info($"[coverage] {s.ToString().ToLowerInvariant()}: {string.Join(", ", l.Select(e => e.Type))}");
            foreach (var c in CardCoverage.Issues)
                log.Info($"[coverage] card code: {c.Key}: {string.Join("; ", c.Value)}");
            if (!string.IsNullOrEmpty(game) && !game.Contains(LedgerGameVersion.TrimStart('v')))
            {
                log.Warn($"[coverage] the game ({game}) isn't the version the mod's coverage was checked against ({LedgerGameVersion}): " +
                         "new or changed mechanics are reported as they show up.");
                reporter?.Mismatch("coverage", $"Game version {game} differs from the coverage ledger ({LedgerGameVersion})",
                    new[] { "coverage", "game-version", game },
                    $"The mod's coverage ledger was generated from {LedgerGameVersion}; the game is {game}. Rerun scripts/decompile.ps1 and scripts/coverage-ledger.mjs, then review new and changed classes.");
            }
        }
        catch (Exception ex)
        {
            log.Warn($"[coverage] couldn't check coverage: {ex.Message}");
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "coverage-summary");
        }
    }

    public static string SafeGameVersion()
    {
        try { return MegaCrit.Sts2.Core.Nodes.NGame.GetGameVersion() ?? ""; }
        catch { return ""; }
    }
}

/// <summary>
/// Watches fights for game classes the ledger flags (new, changed, unreviewed, or a known gap) and
/// for cards whose code does something the mod doesn't count. Each is logged once per session;
/// unexpected ones are also reported (grouped by class).
/// </summary>
public sealed class CoverageWatch : IFeature, IFrameFeature
{
    private readonly QolContext _ctx;

    public CoverageWatch(QolContext ctx) => _ctx = ctx;

    public string Id => "coverage-watch";
    public string DisplayName => "Coverage Watch (log only)";
    public bool EnabledByDefault => true;

    private readonly HashSet<Type> _checked = new();
    private bool _summarized;
    // The ledger and the card-code scan read the whole game assembly once: on a worker thread, so
    // loading the game doesn't hitch. Nothing is checked until they're done.
    private Task? _building;
    private List<string>? _catalogProblems;
    private readonly HashSet<string> _unknownTabs = new();

    public void Apply(Harmony harmony)
    {
    }

    public void Attach(OverlayHost host)
    {
    }

    public void OnFrame(OverlayHost host)
    {
        _building ??= Task.Run(() =>
        {
            _ = CoverageLedger.ByType;
            _ = CardCoverage.Issues;
            _catalogProblems = CatalogCheck.Problems();
            CodeIndex.Prewarm(CoverageLedger.ModelTypes());
        });
        if (!_building.IsCompleted) return;
        if (!_summarized)
        {
            _summarized = true;
            if (_building.IsFaulted && _building.Exception?.GetBaseException() is { } buildError)
            {
                ModEntry.Log.Warn($"[coverage] couldn't build the ledger: {buildError.Message}");
                Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(buildError, "coverage-build");
            }
            CoverageLedger.LogSummary(ModEntry.Log, Sts2Mods.Core.Diagnostics.ErrorReporter.Instance);
            foreach (var p in _catalogProblems ?? new List<string>())
            {
                ModEntry.Log.Warn($"[coverage] settings: {p}");
                Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("coverage", $"Settings catalog: {p}", new[] { "coverage", "catalog", p },
                    $"{p}. Every tab and tracker row needs a row in Menu/SettingsCatalog.cs, and every row something behind it.");
            }
        }
        // Tab kinds shown in play that the catalog doesn't describe (the menu lists them under "Other").
        foreach (var key in _ctx.Settings.Tabs.Keys)
        {
            if (key.EndsWith(":*") || SettingsCatalog.IsKnownTabKey(key) || !_unknownTabs.Add(key)) continue;
            Unexpected(key, "tab-not-in-catalog", "a tab kind was shown that the settings catalog doesn't describe.");
        }
        var me = Sts2State.Me;
        var cs = Sts2State.Combat;
        if (me == null || cs == null) return;
        foreach (var r in me.Relics) Check(r);
        foreach (var c in cs.Creatures)
        {
            foreach (var p in c.Powers) Check(p);
            if (c.Monster != null) Check(c.Monster);
        }
        foreach (var card in Sts2State.Hand(me))
        {
            Check(card);
            if (card.Enchantment != null) Check(card.Enchantment);
            if (card.Affliction != null) Check(card.Affliction);
        }
        var orbs = me.PlayerCombatState?.OrbQueue?.Orbs;
        if (orbs != null) foreach (var o in orbs) Check(o);
    }

    private void Check(AbstractModel m)
    {
        var t = m.GetType();
        if (!_checked.Add(t)) return;
        try
        {
            var log = ModEntry.Log;
            if (CoverageLedger.ByType.TryGetValue(t, out var e))
            {
                switch (e.Status)
                {
                    case CoverageLedger.Status.Gap:
                        log.Info($"[coverage] known gap in this fight: {t.Name} ({e.Note})");
                        break;
                    case CoverageLedger.Status.Unreviewed or CoverageLedger.Status.New or CoverageLedger.Status.Changed:
                        Unexpected(t.Name, e.Status.ToString().ToLowerInvariant(), $"{e.Note}. Combat hooks: {e.Hooks}.");
                        break;
                }
            }
            if (m is RelicModel) CheckRelicTracking(t);
            if (CardCoverage.Issues.TryGetValue(t.Name, out var all))
            {
                // Issues about teammates only count in a fight with other players.
                var withTeammates = Sts2State.Combat?.Players.Count > 1;
                var issues = all.Where(x => withTeammates || !x.StartsWith(CardCoverage.TeammatesOnly)).ToList();
                if (issues.Count > 0)
                    Unexpected(t.Name, "card-code", $"its code does what the mod doesn't count: {string.Join("; ", issues)}.");
            }
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[coverage] check failed for {t.Name}: {ex.Message}");
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "coverage-check");
        }
    }

    /// <summary>
    /// Goal 2: a relic that can affect a fight has a tab, a tracker row or a reviewed reason
    /// (RelicTracking). A relic the ledger doesn't list is checked by its hooks (new in a game update).
    /// </summary>
    private static void CheckRelicTracking(Type t)
    {
        var name = t.Name;
        var listed = CoverageLedger.RelicFightHooks.Any(r => r.Type == name);
        if (!listed)
        {
            var outOfCombat = CoverageLedger.OutOfCombatHooks.ToHashSet();
            // Hooks are AbstractModel's public virtuals, as scripts/coverage-ledger.mjs reads them; its protected ones
            // (DeepCloneFields, AfterCloned) are model copying, not hooks (Yummy Cookie only changes its icon there).
            var fight = t.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly)
                .Where(m => m.IsVirtual && !m.IsSpecialName && m.GetBaseDefinition() is { IsPublic: true } b && b.DeclaringType != t && b.DeclaringType?.Name == "AbstractModel"
                            && !outOfCombat.Contains(m.Name))
                .Select(m => m.Name).Distinct().ToList();
            if (fight.Count == 0) return;
            Unexpected(name, "relic-new", $"a relic the tracking review doesn't list (new in this game version?) with fight hooks: {string.Join(", ", fight)}.");
            return;
        }
        if (SettingsCatalog.Sources.Any(s => s.Type == name && (s.Tag != null || s.Tracker != null))) return;
        if (SpireQoL.Data.RelicSpecs.For(name) != null) return;
        if (RelicTracking.Reasons.TryGetValue(name, out var why))
        {
            if (why.StartsWith("gap:")) ModEntry.Log.Info($"[coverage] known tracking gap in this fight: {name} ({why})");
            return;
        }
        Unexpected(name, "relic-untracked", "a relic that can affect fights with no tab, no tracker row and no reviewed reason (RelicTracking).");
    }

    private static void Unexpected(string type, string kind, string details)
    {
        ModEntry.Log.Warn($"[coverage] unexpected in this fight: {type} ({kind}): {details}");
        Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("coverage", $"Coverage: {type} ({kind})", new[] { "coverage", kind, type },
            $"{type} showed up in a fight. {details}\nGame {CoverageLedger.SafeGameVersion()}, ledger {CoverageLedger.LedgerGameVersion}. " +
            "Review it against the decompile: add a rule, or a reason in CoverageLedger.Reviewed.",
            tags: new Dictionary<string, string> { ["check"] = kind, ["source"] = type });
    }
}
