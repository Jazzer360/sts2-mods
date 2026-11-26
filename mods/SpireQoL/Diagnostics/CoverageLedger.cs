using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Combat;
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
    internal static IReadOnlyDictionary<string, string> ReviewedEntries => Reviewed;

    /// <summary>The ledger's data for the tests: the hooks reviewed per class, beta's (the decompile's) and main's where they differ.</summary>
    internal static IReadOnlyList<(string Folder, string Type, string Hooks)> BetaHooks => Generated;

    internal static IReadOnlyList<(string Folder, string Type, string Hooks)> MainHooks => GeneratedMain;

    internal static IReadOnlyList<string> ReviewedHooks => Hooks;

    /// <summary>The hooks reviewed per class for enchantments and afflictions only (OnPlay: a card's own is its program).</summary>
    internal static IReadOnlyList<string> ReviewedOnCardHooks => OnCardHooks;

    private static readonly Dictionary<string, string> Reviewed = new()
    {
        // ======== relics
        ["ArtOfWar"] = "covered: read from its code (ReadModel, decision #239): AfterCardPlayed marks an Attack played this turn, AfterSideTurnEnd keeps it as last turn's; its AfterEnergyReset pays its energy on !AnyAttacksPlayedLastTurn (past the turn predicted), so the play that turns that off gives it up: its crossed tab, checked by the relic going dark (its AfterCardPlayed sets Status Normal)",
        ["BeatingRemnant"] = "covered: read from its code (ReadModel, decision #195): ModifyHpLostAfterOsty caps HP its owner loses at MaxHpLoss less its DamageReceivedThisTurn, which AfterDamageReceived counts (its BeforeSideTurnStart reset comes as its owner's next turn starts)",
        ["BiiigHug"] = "covered: read from its code (ReadModel, decision #257): AfterShuffle makes a Soot into your draw pile at a random spot (AddGeneratedCardToCombat, you its creator: your created-card hooks hear it), so a later draw can find it; no tab (decision #263)",
        ["BingBong"] = "ignore: AfterCardChangedPiles copies cards added to your deck (deck, not combat piles)",
        ["BoneFlute"] = "covered: read from its code (ReadModel, decision #228): AfterAttack gains block when its owner's Osty made the attack (command.Attacker's monster of the type Player.Osty gets, its PetOwner the owner); block tab for Osty-attack cards",
        ["BookOfFiveRings"] = "ignore: AfterCardChangedPiles heals when cards are added to your deck (deck, not combat)",
        ["BookRepairKnife"] = "covered: read from its code (ReadModel, decision #310): AfterDiedToDoom heals you 3 for each enemy a Doom kill took whose death sets off Fatal (each of its powers answering ShouldOwnerDeathTriggerFatal true, read by the adapter)",
        ["BrilliantScarf"] = "covered: AfterCardPlayed counts cards this turn (tracker row)",
        ["BurningSticks"] = "covered: read from its code (ReadModel, decision #252): AfterCardExhausted makes a copy of its owner's first exhausted Skill this combat (card.CreateClone() into the hand, its own WasUsedThisCombat) (proc tab, created card)",
        ["CaptainsWheel"] = "covered: AfterBlockCleared gains block on turn 3, at the start of your turn (tracker turn timer)",
        ["CentennialPuzzle"] = "covered: read from its code (ReadModel, decision #214): AfterDamageReceived draws its Cards, one at a time (a counter loop to a decimal bound), on its owner's first HP loss of the combat (UsedThisCombat its own field), queued until the hit is over; draw tab with the cards drawn",
        ["CharonsAshes"] = "covered: read from its code (ReadModel, decision #195): AfterCardExhausted damages all enemies (model CharonsAshes: every exhaust, Ethereal ones at end of turn included)",
        ["ChemicalX"] = "covered: BeforeCardPlayed flashes for X cards (proc tab; X value through Hook.ModifyXValue)",
        ["CloakClasp"] = "covered: read from its code (ReadModel, decision #195): BeforeSideTurnEnd gives its Block per card in its owner's hand (named with the count in Incoming Damage); its tab, crossed on a play that leaves fewer cards in hand as the turn ends (its payout's amount, decision #244; off by default)",
        ["DarkstonePeriapt"] = "ignore: AfterCardChangedPiles max HP when a curse joins your deck (deck, not combat)",
        ["DaughterOfTheWind"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed block on Attacks (proc block)",
        ["DemonTongue"] = "covered: read from its code (ReadModel, decision #239): AfterDamageReceived heals its owner's first HP loss on their turn (_triggeredThisTurn its own field); its tab is its reaction to your HP loss, judged on the play's outcomes (a bonus where every outcome sets it off)",
        ["EmotionChip"] = "covered: read from its code (ReadModel, decision #239): AfterDamageReceived flashes on its owner's HP loss (its AfterPlayerTurnStart passives come as their next turn starts); its tab is its reaction to your HP loss, judged on the play's outcomes",
        ["FakeOrichalcum"] = "covered: read from its code (ReadModel, decision #195): BeforeSideTurnEndVeryEarly sets ShouldTrigger with no block; BeforeSideTurnEnd gives its block (as Orichalcum); its tab, crossed on a play that gives you block when you'd end the turn with none (its payout given up, decisions #239, #244)",
        ["Fiddle"] = "covered: ShouldDraw blocks draws on your turn (SimEngine.DrawsStopped: no draws, no shuffle)",
        ["ForgottenSoul"] = "covered: read from its code (ReadModel, decision #195): AfterCardExhausted damages a random enemy (model ForgottenSoul: every exhaust, Ethereal ones at end of turn included)",
        ["FurCoat"] = "gap: AfterCreatureAddedToCombat sets an enemy's HP to 1 as it enters a marked room's fight (CreatureCmd.SetCurrentHp of the creature added, its own marked-room test): the enemies there at the start are read live; one added mid-fight (a death's spawn: SimEngine.Spawn, which runs the hook's read listeners, decision #325) is unknown with Fur Coat on the board (SimSpawn.JoinUnread), its body not read (TRACKER)",
        ["GalacticDust"] = "covered: read from its code (ReadModel, decision #195): AfterStarsSpent counts the stars spent (its own StarsSpent) and gives its Block per Stars of them (proc rule, tracker)",
        ["GamePiece"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed draws on a Power (simulator model; its draws in the play's log)",
        ["GhostSeed"] = "ignore: AfterCardEnteredCombat makes cards Ethereal (keywords read from the card)",
        ["GremlinHorn"] = "covered: read from its code (ReadModel, decision #195): AfterDeath of an enemy gives energy and a draw, queued until the hit is over (a bonus tab on a play that kills in every outcome, read from what its AfterDeath waits on)",
        ["HandDrill"] = "covered: read from its code (ReadModel, decision #285): AfterBlockBroken puts its Vulnerable on a creature other than a player whose block its owner (or its owner's pet) broke, inside the hit; its tab judged on how the turn ends (a play that breaks some enemy's block in every outcome: HookOutcome.BreaksBlock)",
        ["HelicalDart"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed Dexterity on Shivs (proc tab)",
        ["HistoryCourse"] = "ignore: AfterAutoPrePlayPhaseEntered replays an attack at the start of your turn (before your plays)",
        ["HornCleat"] = "covered: AfterBlockCleared block on turn 2, at the start of your turn (tracker turn timer)",
        ["GoldPlatedCables"] = "covered: read from its code (ReadModel, decision #214): ModifyOrbPassiveTriggerCounts gives its owner's front orb one more passive trigger (whichever orb is in front as the passives go off), AfterModifyingOrbPassiveTriggerCount flashes",
        ["InfusedCore"] = "covered: read from its code (ReadModel, decision #214): ModifyOrbValue adds its ExtraDamage to its owner's Lightning (after Focus, in the listeners' order); its AfterSideTurnStart channels as its owner's first turn starts, past the turns a simulation covers",
        ["IntimidatingHelmet"] = "covered: read from its code (ReadModel, decision #195): BeforeCardPlayed block on costly cards (proc block)",
        ["IronClub"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed draws every N cards (simulator model, tracker; its draws in the play's log)",
        ["IvoryTile"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed energy on costly cards (proc tab)",
        ["JossPaper"] = "covered: read from its code (ReadModel, decision #214): AfterCardExhausted counts its owner's exhausts (CardsExhausted) and draws one per ExhaustAmount (a let keeps the count before the store takes the draws off); an Ethereal one its turn end exhausts counts in its EtherealCount, which AfterSideTurnEnd adds before drawing (after the cards in hand act)",
        ["Kunai"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed Dexterity every N attacks (proc tab, tracker)",
        ["Kusarigama"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed damages a random enemy every N attacks (proc damage, tracker)",
        ["LavaLamp"] = "ignore: AfterDamageReceived only notes you took damage (end-of-combat upgrades)",
        ["LetterOpener"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed damages all enemies every N skills (proc damage, tracker)",
        ["LizardTail"] = "covered: ShouldDieLate/AfterPreventingDeath heals you from 0 to 50% of max HP once (simulator: SimPlayer.LizardTailHeal; Incoming Damage's revive row)",
        ["LostWisp"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed damages all enemies on a Power (proc damage)",
        ["LuckyFysh"] = "ignore: AfterCardChangedPiles gold when cards join your deck (deck, not combat)",
        ["LunarPastry"] = "covered: read from its code (ReadModel, decision #195): AfterSideTurnEnd gives you stars (one stars-gained event); Black Hole's model reacts, in relic order with Parrying Shield",
        ["MeatOnTheBone"] = "ignore: AfterCurrentHpChanged only sets its status (heals after combat)",
        ["Metronome"] = "covered: read from its code (ReadModel, decision #195): AfterOrbChanneled counts your orbs channeled (its own OrbsChanneled) and damages all enemies at its count (proc damage, second pass, tracker)",
        ["MiniRegent"] = "covered: read from its code (ReadModel, decision #195): AfterStarsSpent Strength once per turn (proc tab)",
        ["MummifiedHand"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed makes a random card in hand that costs free on a Power (proc tab; a branch per card for the cheapest pick at the turn's end, decision #223)",
        ["MusicBox"] = "covered: read from its code (ReadModel, decision #195): BeforeCardPlayed keeps the first Attack its owner plays while unused this turn (the play bound), AfterCardPlayed makes an Ethereal copy of it into the hand (proc tab, a card made; decision #226)",
        ["Nunchaku"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed energy every N attacks (proc tab, tracker)",
        ["Orichalcum"] = "covered: read from its code (ReadModel, decision #195): BeforeSideTurnEndVeryEarly sets ShouldTrigger with no block; BeforeSideTurnEnd gives its block; its tab, crossed on a play that gives you block when you'd end the turn with none (its payout given up, decisions #239, #244)",
        ["OrnamentalFan"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed block every N attacks (proc block, tracker)",
        ["PaelsEye"] = "covered: crossed tab covers giving up its extra turn (ShouldTakeExtraTurnEarly, AfterTakingExtraTurn); its model answers ShouldTakeExtraTurnEarly while armed (SimEngine.ExtraTurns), and Incoming Damage then shows only its icon and +1 turn (decision #319); the simulator exhausts your hand at BeforeSideTurnEndEarly (Feel No Pain, Charon's Ashes, Forgotten Soul; nothing left in hand to act)",
        ["PaelsLegion"] = "covered: read from its code (ReadModel, decision #269): ModifyBlockMultiplicative doubles its owner's card block off cooldown for no card play kept or the one kept; AfterModifyingBlockAmount keeps the first card play whose block it changed; AfterCardPlayed of that card play lets it go and starts the cooldown (its Turns); AfterSideTurnStart counts it down past the turns predicted (proc tab: its term changing a block, tracker)",
        ["PaelsTears"] = "covered: BeforeSideTurnEnd notes leftover energy (crossed tab on cards that leave no energy once the play has resolved, energy it gets back counted)",
        ["ParryingShield"] = "covered: read from its code (ReadModel, decision #239): AfterSideTurnEnd damages a random hittable enemy when its owner's block is at least its Block; whether that holds as your turn ends, with the play against without it, is its tab, plain where the play makes it pay (its row shows that direction only: decision #244)",
        ["PenNib"] = "covered: read from its code (ReadModel, decision #272): BeforeCardPlayed counts its owner's Attacks (AttacksPlayed, its setter's % 10) and keeps the 10th's card play (AttackToDouble, by its identity); ModifyDamageMultiplicative doubles that card play's powered hits by its owner or their Osty (or a card outside the play pile at 9: a preview); AfterCardPlayed of that card play lets it go (proc tab, tracker)",
        ["Permafrost"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed block on the first Power (proc block)",
        ["PhilosophersStone"] = "covered: read from its code (ReadModel, decision #325): AfterCreatureAddedToCombat gives a creature of the other side Strength as it joins mid-fight (SimEngine.AddedToCombat, after a death's spawn); the enemies there at the start are read live; ModifyMaxEnergy is never run (a player's max energy sets only a turn's start energy, which no simulation follows)",
        ["Pocketwatch"] = "covered: AfterCardPlayed counts cards this turn (tracker); the play that takes it past CardThreshold gives up ModifyHandDraw's extra cards next turn (crossed tab, model Pocketwatch)",
        ["RainbowRing"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed Strength and Dexterity on attack + skill + power in a turn (proc tab)",
        ["RazorTooth"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed upgrades the played card (CardCmd.Upgrade: a replay's later plays are its upgraded copy's, decision #218; proc tab)",
        ["RedSkull"] = "covered: AfterCurrentHpChanged Strength at low HP (proc tab on HP-loss cards), taken back by a heal above its threshold (decision #342)",
        ["Regalite"] = "covered: read from its code (ReadModel, decision #195): AfterCardGeneratedForCombat block on the first created card per turn (proc block)",
        ["RippleBasin"] = "covered: read from its code (ReadModel, decision #239): BeforeSideTurnEnd gains its Block when the fight's log has no Attack of its owner's this turn; the play that logs one gives it up: its crossed tab, checked by the relic going dark (its AfterCardPlayed sets Status Normal)",
        ["ScreamingFlagon"] = "covered: read from its code (ReadModel, decision #195): BeforeSideTurnEnd damages all enemies with an empty hand; its tab, plain on a play that leaves your hand empty (its payout set up, decisions #239, #244)",
        ["SelfFormingClay"] = "covered: read from its code (ReadModel, decision #239): AfterDamageReceived applies SelfFormingClayPower (block as its owner's next turn starts) on their HP loss; its tab is its reaction to your HP loss, judged on the play's outcomes",
        ["Shuriken"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed Strength every N attacks (proc tab, tracker)",
        ["SneckoSkull"] = "covered: read from its code (ReadModel, decision #287): ModifyPowerAmountGivenAdditive adds its Poison value to Poison its owner gives (card poison, Envenom), in Hook.ModifyPowerAmountGiven's additive pass; AfterModifyingPowerAmountGiven flashes",
        ["SparklingRouge"] = "covered: AfterBlockCleared Strength and Dexterity on turn 3, at the start of your turn (tracker turn timer)",
        ["StoneCalendar"] = "covered: read from its code (ReadModel, decision #195): BeforeSideTurnEnd damages all enemies on the turn its DamageTurn names (the players' turn number, tracker)",
        ["SturdyClamp"] = "ignore: ShouldClearBlock keeps up to 10 block into your next turn",
        ["TheAbacus"] = "covered: read from its code (ReadModel, decision #195): AfterShuffle block (proc block on cards that shuffle)",
        ["TheBoot"] = "covered: read from its code (ReadModel, decision #195): ModifyHpLostAfterOstyLate raises its owner's (or Osty's) powered attack hits that lose another creature 1+ HP to its DamageMinimum; AfterModifyingHpLostAfterOsty flashes (proc tab)",
        ["Tingsha"] = "covered: read from its code (ReadModel, decision #195): AfterCardDiscarded damages a random enemy on your turn (proc damage)",
        ["ToughBandages"] = "covered: read from its code (ReadModel, decision #195): AfterCardDiscarded block on your turn (proc block)",
        ["TungstenRod"] = "covered: read from its code (ReadModel, decision #195): ModifyHpLostAfterOsty takes its HpLossReduction off HP its owner loses; AfterModifyingHpLostAfterOsty flashes (proc tab on cards that cost you HP)",
        ["TuningFork"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed block every N skills (proc block, tracker)",
        ["UnceasingTop"] = "covered: AfterHandEmptied draws (proc tab)",
        ["UnsettlingLamp"] = "covered: proc tab on the card that arms it (temporary debuffs too); ModifyPowerAmountGivenMultiplicative doubles that card's first play's debuffs: the adapter doubles its Strength loss and poison (random poison, Bouncing Flask, isn't doubled)",
        ["Vambrace"] = "covered: read from its code (ReadModel, decision #272): ModifyBlockMultiplicative doubles its owner's card block until BlockGainedThisCombat, for the card play it keeps or any while it keeps none; AfterModifyingBlockAmount keeps the block's card play (TriggeringCard, by its identity); AfterCardPlayed of that card play spends it (proc tab)",
        ["VelvetChoker"] = "covered: ShouldPlay limits cards per turn (tracker; playability read live)",
        ["WhisperingEarring"] = "ignore: AfterAutoPrePlayPhaseEnteredLate plays your turn-1 hand (before your plays); ModifyMaxEnergy raises its owner's max energy, read only by the energy reset as their turn starts",
        // v0.113.0
        ["Byrdpip"] = "covered: read from its code (ReadModel, decision #214): AfterCardPlayed counts its owner's Byrd Swoop plays (SwoopsPlayed, up to 6) toward its rest-site evolve option; nothing in a fight reads the count",
        ["ConductorsBaton"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed applies Conductor's Baton (powered attacks on you deal 75% until your next turn) once a turn (its own _triggeredThisTurn) on a card of yours played with three melodies this turn (the history ledger; proc tab); once applied, intents read it through the game's damage hooks",
        ["Fertilizer"] = "covered: read from its code (ReadModel, decision #195): AfterCardGeneratedForCombat gives Strength when you create a Sadness: proc tab on cards that create one (MadeCards: the made card's class carried on its event, tested as its code tests it: `card is Sadness`)",
        ["MightyWings"] = "covered: read from its code (ReadModel, decision #195): AfterDamageGiven gives 2 Strength when your (or Osty's) powered attack hit deals 30 or more (blocked plus HP lost): proc tab when the simulator's biggest hit of the play reaches it in every branch; the simulator gives the attack's later hits the Strength",
        ["ObsidianVice"] = "covered: read from its code (ReadModel, decision #195): BeforeSideTurnEnd deals its Damage per card in its owner's hand to a random hittable enemy (a branch each); its tab, crossed on a play that leaves fewer cards in hand as the turn ends (its payout's amount, decision #244; off by default)",
        ["Storyteller"] = "ignore: read from its code (ReadModel, decision #195): BeforeSideTurnEnd gives Aim, which only adds to your arrow attacks, none of them after your turn ends; AfterRoomEntered gives it at the start of combat",
        ["Thumbscrew"] = "covered: ModifyAttackHitCount adds a hit to your multi-hit attacks (applied through Hook.ModifyAttackHitCount in BoardCounts.HitsPerPlay, as the card preview does)",
        ["Twine"] = "covered: read from its code (ReadModel, decision #236): AfterArrowDislodged gains its Block for its owner when the arrow knocked loose is its owner's (arrow.owner: the event's player; the simulator dislodges arrows: ImpaledPower model, Recall, Uproot)",
        ["WhittlingKnife"] = "covered: read from its code (ReadModel, decision #195): AfterShuffle gives Aim: proc tab on cards whose draws or own code shuffle (the simulation's shuffle event)",
        ["Wuppy"] = "covered: AfterCurrentHpChanged/BeforeCombatStart keep a pet Wuppy that takes the powered attacks aimed at its owner while it lives (WuppysLoyaltyPower: simulator, yours and teammates')",
        // Listed with the turn-start, hand-flush, potion, cost, energy and draw hooks (decision #323). "Turn 1 only": before the fight's first
        // decision, and a projection's turn (decision #306) is never turn 1. A projection leaves the hand and its draw out (decision #304) and
        // doesn't follow energy, stars, cards, block or HP (ForecastIgnores, decision #308); nothing in a simulation reads max energy.
        ["Akabeko"] = "ignore: read from its code (ReadModel): AfterSideTurnStart gives its owner Vigor on their turn 1 only (TurnNumber <= 1)",
        ["BagOfMarbles"] = "ignore: read from its code (ReadModel): BeforeSideTurnStart puts Vulnerable on every hittable enemy on its owner's turn 1 only (TurnNumber <= 1)",
        ["BagOfPreparation"] = "ignore: ModifyHandDraw adds its Cards to its owner's hand draw on turn 1 only",
        ["BathTowel"] = "ignore: read from its code (ReadModel): AfterPlayerTurnStartLate heals its owner on their turn 1 only (TurnNumber <= 1)",
        ["Bellows"] = "ignore: read from its code (ReadModel): AfterPlayerTurnStart upgrades its owner's hand on their turn 1 only (TurnNumber > 1 returns)",
        ["BeltBuckle"] = "covered: read from its code (ReadModel, decision #313): AfterPotionUsed gives its owner Dexterity when the potion drunk in a fight was their last (potion tab); AfterPotionProcured takes it off once they hold one again (decision #362: a card's procure, Alchemize's); AfterPotionDiscarded does the same for a potion thrown away, a choice the mod predicts nothing for (its Dexterity is on the board at the next decision)",
        ["DragonFruit"] = "covered: read from its code (ReadModel, decision #362): AfterGoldGained gains its owner max HP when they gain gold (CreatureCmd.GainMaxHp), followed where a play gains gold (PlayerCmd.GainGold: Hand of Greed's kill)",
        ["BigHat"] = "ignore: read from its code (ReadModel): AfterSideTurnStart makes Ethereal cards into its owner's hand on their turn 1 only (TurnNumber <= 1)",
        ["BigMushroom"] = "ignore: ModifyHandDraw takes its Cards off its owner's hand draw on turn 1 only (TurnNumber != 1 returns)",
        ["BizarreLamp"] = "ignore: read from its code (ReadModel): BeforeHandDraw makes a Joy and a Sadness into its owner's hand on their turn 1 only (TurnNumber <= 1)",
        ["BlessedAntler"] = "covered: read from its code (ReadModel, decision #325): ModifyMaxEnergy never run (read only by the energy reset as its owner's turn starts, which no simulation follows); BeforeHandDraw's Dazed on turn 1 only, unknown where it runs, which changes nothing a projection follows",
        ["BloodSoakedRose"] = "ignore: ModifyMaxEnergy raises its owner's max energy, read only by the energy reset as their turn starts",
        ["BloodVial"] = "ignore: read from its code (ReadModel): AfterPlayerTurnStartLate heals its owner on their turn 1 only (TurnNumber <= 1)",
        ["BoneTea"] = "ignore: read from its code (ReadModel): AfterPlayerTurnStart upgrades its owner's hand on their turn 1 only (TurnNumber > 1 returns), while it has combats left",
        ["Bookmark"] = "ignore: AfterFlush, as its owner's hand is flushed at their turn's end, makes a random retained card that costs cost 1 less until played: a cost in the next turn's hand, which no simulation reads",
        ["BoomingConch"] = "ignore: ModifyHandDraw (more cards) and AfterSideTurnStart (energy) on its owner's turn 1 of an elite fight only",
        ["BoundPhylactery"] = "covered: read from its code (ReadModel): AfterEnergyResetLate summons Osty for its owner as each of their turns after the first starts (a projection's next turn runs it)",
        ["Bread"] = "covered: read from its code (ReadModel, decision #325): ModifyMaxEnergy never run (read only by the energy reset as its owner's turn starts, which no simulation follows); AfterSideTurnStart's energy on turn 1 only, unknown where it runs, which changes nothing a projection follows",
        ["Brimstone"] = "covered: read from its code (ReadModel): AfterSideTurnStart gives its owner and every living enemy Strength as its owner's side's turn starts (a projection's next turn runs it)",
        ["Candelabra"] = "covered: read from its code (ReadModel): AfterSideTurnStart energy as its owner's turn 2 starts (tracker turn timer)",
        ["Chandelier"] = "covered: read from its code (ReadModel): AfterSideTurnStart energy as its owner's turn 3 starts (tracker turn timer)",
        ["ChoicesParadox"] = "ignore: read from its code (ReadModel): AfterPlayerTurnStart offers its owner a card for their hand on their turn 1 only (TurnNumber != 1 returns)",
        ["CrackedCore"] = "ignore: read from its code (ReadModel): BeforeSideTurnStart channels Lightning for its owner on their turn 1 only (TurnNumber <= 1)",
        ["Crossbow"] = "covered: read from its code (ReadModel): AfterSideTurnStart makes a random Attack, free this turn, into its owner's hand as each of their turns starts; its body isn't read (a loop), so a projection that reaches it is unknown",
        ["DiamondDiadem"] = "ignore: read from its code (ReadModel): AfterSideTurnStart gives its owner block and Blur on their turn 1 only (TurnNumber <= 1)",
        ["DivineDestiny"] = "ignore: read from its code (ReadModel): AfterSideTurnStart gives its owner stars on their turn 1 only (TurnNumber <= 1)",
        ["Ectoplasm"] = "ignore: ModifyMaxEnergy raises its owner's max energy, read only by the energy reset as their turn starts",
        ["ExerciseMat"] = "ignore: read from its code (ReadModel): AfterSideTurnStart gives its owner Stretch on their turn 1 only (TurnNumber <= 1)",
        ["FakeBloodVial"] = "ignore: read from its code (ReadModel): AfterPlayerTurnStartLate heals its owner on their turn 1 only (TurnNumber <= 1)",
        ["FakeHappyFlower"] = "covered: tracker turn timer; read from its code (ReadModel): AfterPlayerTurnStartEarly gives energy as its owner's turn starts once BeforeSideTurnStart's count (TurnsSeen) comes round; that count isn't read (it sets Status), so a projection that reaches it is unknown",
        ["FakeVenerableTeaSet"] = "ignore: read from its code (ReadModel): AfterEnergyReset gives energy at the first energy reset of the fight after a rest (GainEnergyInNextCombat), its owner's turn 1",
        ["FencingManual"] = "ignore: read from its code (ReadModel): AfterSideTurnStart forges for its owner on their turn 1 only (TurnNumber <= 1)",
        ["FestivePopper"] = "ignore: read from its code (ReadModel): AfterPlayerTurnStart damages every hittable enemy on its owner's turn 1 only (TurnNumber == 1)",
        ["FuneraryMask"] = "ignore: read from its code (ReadModel): BeforeHandDraw makes Souls into its owner's draw pile on their turn 1 only (TurnNumber == 1)",
        ["GamblingChip"] = "ignore: read from its code (ReadModel): AfterPlayerTurnStart lets its owner discard and redraw on their turn 1 only (TurnNumber <= 1)",
        ["HappyFlower"] = "covered: tracker turn timer; read from its code (ReadModel): AfterPlayerTurnStartEarly gives energy as its owner's turn starts once BeforeSideTurnStart's count (TurnsSeen) comes round; that count isn't read (it sets Status), so a projection that reaches it is unknown",
        ["IceCream"] = "ignore: ShouldPlayerResetEnergy keeps its owner's unspent energy past turn 1, as their next turn starts: energy, which a projection doesn't follow",
        ["JeweledMask"] = "ignore: read from its code (ReadModel): BeforeHandDraw puts a random Power from its owner's draw pile into their hand, free this combat, on their turn 1 only (TurnNumber > 1 returns)",
        ["Lantern"] = "ignore: read from its code (ReadModel): AfterSideTurnStart gives its owner energy on their turn 1 only (TurnNumber <= 1)",
        ["MercuryHourglass"] = "covered: read from its code (ReadModel): AfterPlayerTurnStart damages every hittable enemy as each of its owner's turns starts (a projection's next turn runs it)",
        ["MrStruggles"] = "covered: read from its code (ReadModel): AfterPlayerTurnStart damages every hittable enemy by its owner's turn number (unpowered) as each of their turns starts (a projection's next turn runs it)",
        ["NinjaScroll"] = "ignore: read from its code (ReadModel): BeforeHandDraw makes Shivs into its owner's hand on their turn 1 only (TurnNumber <= 1)",
        ["Ocarina"] = "ignore: read from its code (ReadModel): BeforeHandDraw makes a random melody card into its owner's hand on their turn 1 only (TurnNumber <= 1)",
        ["OrangeDough"] = "ignore: read from its code (ReadModel): AfterSideTurnStart makes random Colorless cards into its owner's hand on their turn 1 only (TurnNumber <= 1)",
        ["PaelsBlood"] = "ignore: ModifyHandDraw adds its Cards to its owner's hand draw, which a projection leaves out",
        ["PaelsFlesh"] = "covered: read from its code (ReadModel, decision #325): ModifyMaxEnergy never run (read only by the energy reset as its owner's turn starts, which no simulation follows); BeforeSideTurnStart and AfterSideTurnStart only refresh its display and Status (tracker turn timer), unknown where it runs, which changes nothing a projection follows",
        ["Pendulum"] = "ignore: BeforeHandDraw counts its owner's turns (TurnsSeen, its Status), ModifyHandDraw adds its Cards to the hand draw when the count comes round: the hand's draw, which a projection leaves out (tracker turn timer)",
        ["PhylacteryUnbound"] = "covered: read from its code (ReadModel): AfterSideTurnStart summons Osty for its owner as each of their turns starts (a projection's next turn runs it)",
        ["PollinousCore"] = "ignore: BeforeHandDraw counts its owner's turns, ModifyHandDraw adds its Cards to the hand draw once the count reaches Turns, AfterModifyingHandDraw starts it over: the hand's draw, which a projection leaves out (tracker turn timer)",
        ["PowerCell"] = "ignore: read from its code (ReadModel): BeforeSideTurnStart puts 0-cost cards from its owner's draw pile into their hand on their turn 1 only (TurnNumber <= 1)",
        ["PrismaticGem"] = "ignore: ModifyMaxEnergy raises its owner's max energy, read only by the energy reset as their turn starts",
        ["PumpkinCandle"] = "ignore: ModifyMaxEnergy raises its owner's max energy while it has charges (KindleCount), read only by the energy reset as their turn starts",
        ["RadiantPearl"] = "ignore: read from its code (ReadModel): BeforeHandDraw makes Luminesces into its owner's hand on their turn 1 only (TurnNumber == 1)",
        ["RedMask"] = "ignore: read from its code (ReadModel): BeforeSideTurnStart puts Weak on every hittable enemy on its owner's turn 1 only (TurnNumber <= 1)",
        ["ReptileTrinket"] = "covered: read from its code (ReadModel, decision #313): AfterPotionUsed gives its owner Strength this turn (Reptile Trinket's power) for each potion they drink in a fight (potion tab)",
        ["RingOfTheDrake"] = "ignore: ModifyHandDraw adds its Cards to its owner's hand draw up to its Turns: the hand's draw, which a projection leaves out",
        ["RingOfTheSnake"] = "ignore: ModifyHandDraw adds its Cards to its owner's hand draw on turn 1 only",
        ["RoyalPoison"] = "ignore: read from its code (ReadModel): AfterPlayerTurnStart damages its owner on their turn 1 only (TurnNumber <= 1)",
        ["RuinedHelmet"] = "covered: read from its code (ReadModel, decision #324): TryModifyPowerAmountReceived doubles the first Strength its owner gains in a fight (more than 0, UsedThisCombat unset), in Hook.ModifyPowerAmountReceived's pass as a power lands (SimEngine.AmountReceived); AfterModifyingPowerAmountReceived spends it",
        ["RunicCapacitor"] = "ignore: read from its code (ReadModel): AfterSideTurnStart adds orb slots for its owner on their turn 1 only (TurnNumber <= 1)",
        ["RunicPyramid"] = "ignore: ShouldFlush keeps its owner's hand as their turn ends: the flush only moves cards to the discard pile (CardPileCmd.Add, no discard hooks), and nothing a simulation reads after the turn's end looks at the hand (a projection leaves it out)",
        ["Sai"] = "covered: read from its code (ReadModel): AfterSideTurnStart block for its owner as each of their turns starts (a projection's next turn runs it)",
        ["SealOfGold"] = "covered: read from its code (ReadModel): AfterSideTurnStart trades its owner's gold for energy as each of their turns starts; its body isn't read (their gold), so a projection that reaches it is unknown",
        ["SneckoEye"] = "ignore: ModifyHandDraw adds its Cards to its owner's hand draw, which a projection leaves out",
        ["Sozu"] = "ignore: ModifyMaxEnergy raises its owner's max energy, read only by the energy reset as their turn starts",
        ["SpikedGauntlets"] = "covered: read from its code (ReadModel): TryModifyEnergyCostInCombat makes its owner's Powers cost 1 more wherever a simulation reads a hand card's cost (SimEngine.HandCost, decision #334); ModifyMaxEnergy raises their max energy, read only by the energy reset as their turn starts",
        ["SymbioticVirus"] = "ignore: read from its code (ReadModel): AfterSideTurnStart channels Dark for its owner on their turn 1 only (TurnNumber <= 1)",
        ["ThrowingAxe"] = "covered: read from its code (ReadModel): its ModifyCardPlayCount plays its owner's first card of a fight twice (Hook.ModifyCardPlayCount), AfterModifyingCardPlayCount marks it used (double-play tab)",
        ["ToastyMittens"] = "covered: read from its code (ReadModel): AfterPlayerTurnStart has its owner exhaust a card from their hand and gives Strength as each of their turns starts; its body isn't read (a pick), so a projection that reaches it is unknown",
        ["Toolbox"] = "ignore: read from its code (ReadModel): BeforeHandDraw offers its owner a Colorless card for their hand on their turn 1 only (TurnNumber == 1)",
        ["TormentousCollar"] = "ignore: read from its code (ReadModel): BeforeSideTurnStart puts Weak on every hittable enemy on its owner's turn 1 of an elite fight only (TurnNumber <= 1)",
        ["TwistedFunnel"] = "ignore: read from its code (ReadModel): BeforeSideTurnStart poisons every hittable enemy on its owner's turn 1 only (TurnNumber > 1 returns)",
        ["VenerableTeaSet"] = "ignore: read from its code (ReadModel): AfterEnergyReset gives energy at the first energy reset of the fight after a rest (GainEnergyInNextCombat), its owner's turn 1",
        ["VeryHotCocoa"] = "ignore: read from its code (ReadModel): AfterSideTurnStart gives its owner energy on their turn 1 only (TurnNumber <= 1)",
        ["VexingPuzzlebox"] = "ignore: read from its code (ReadModel): AfterPlayerTurnStart makes a random card, free this turn, into its owner's hand on their turn 1 only (TurnNumber == 1)",

        // ======== powers
        ["AdaptablePower"] = "ignore: AfterDeath sends Test Subject to its Respawn move (heal and buff, no attack) and ShouldAllowHitting keeps it out of hits until then, so its kill holds for the turn (the simulator's dead-before-acting); ShouldStopCombatFromEnding only keeps the fight going, which the simulator never ends early; ShouldCreatureBeRemovedFromCombatAfterDeath and ShouldPowerBeRemovedAfterOwnerDeath keep the dead Test Subject and this power in the fight for its respawn, past the turn",
        ["AfterimagePower"] = "covered: read from its code (ReadModel, decision #195): BeforeCardPlayed binds its amount to the play, AfterCardPlayed blocks it (proc block)",
        ["ArsenalPower"] = "covered: read from its code (ReadModel, decision #195): AfterCardGeneratedForCombat Strength per created card (proc tab)",
        ["AsleepPower"] = "covered: read from its code (ReadModel, decision #307): AfterDamageReceived, HP through block wakes it: its Plating goes, a stun with Lagavulin Matriarch's WakeUpMove and SLASH_MOVE after it, the power goes; BeforeSideTurnEndVeryEarly takes its Plating on its last stack, AfterSideTurnEnd a stack (its WakeUpMove there only sets what the game shows). Its tab compares the enemy's next move with and without the play",
        ["AutomationPower"] = "covered: read from its code (ReadModel, decision #195), each copy on its own (decision #196): AfterCardDrawn energy every N draws (proc tab, tracker; the simulation's draws)",
        ["BarricadePower"] = "covered: read from its code (ReadModel, decision #285): ShouldClearBlock keeps its owner's block as its side's turn starts (SimEngine.ClearsBlock); AfterApplied only sets the applier's name its tooltip shows",
        ["BattlewornDummyTimeLimitPower"] = "ignore: AfterSideTurnEnd the dummy escapes",
        ["BeaconOfHopePower"] = "covered: AfterBlockGained gives every other player half of each block its owner gains on the players' side: the simulator shares every block gain (yours and teammates', their end-of-turn Plating, Orichalcum and Frost included), one hop as the game does",
        ["BlackHolePower"] = "covered: read from its code (ReadModel, decision #250): AfterCardPlayed damages every hittable enemy its amount (unpowered) on the last play of its owner's card that spent stars (cardPlay.Resources.StarsSpent, IsLastInSeries), AfterStarsGained on each stars gain of its owner's (proc damage, second pass)",
        ["BlockNextTurnPower"] = "ignore: AfterBlockCleared block at the start of your next turn",
        ["BlurPower"] = "ignore: ShouldClearBlock keeps your block into your next turn (SimEngine.ClearsBlock asks it as a projection's next turn starts); AfterSideTurnStart decrements it then (its turn-start gap: ForecastIgnores)",
        ["BorrowedTimePower"] = "covered: read from its code (ReadModel): TryModifyEnergyCostInCombat adds its amount to its owner's card costs wherever a simulation reads a hand card's cost (SimEngine.HandCost, decision #334); AfterSideTurnEnd removes itself",
        ["BufferPower"] = "covered: read from its code (ReadModel, decision #230): ModifyHpLostAfterOstyLate stops HP its owner would lose (each branch's own Brightest Flame case), AfterModifyingHpLostAfterOsty takes a stack; its GetScaledAmountForMultiplayer never runs (ShouldScaleInMultiplayer is false)",
        ["BurrowedPower"] = "covered: read from its code (ReadModel, decision #285): AfterBlockBroken on its owner's block, its owner a Tunneler: Tunneler.GetStunned read where it's called (its IsStunned, which nothing a simulation reaches reads), CreatureCmd.Stun of its owner, PowerCmd.Remove of itself; ShouldClearBlock keeps its owner's block as its side's turn starts; AfterRemoved's block loss acts on a block already broken",
        ["BurstPower"] = "covered: read from its code (ReadModel, decision #195): ModifyCardPlayCount replays your Skills, AfterModifyingCardPlayCount a stack used (the double-play tab from the game's hook; decided as each card play starts: decision #192); AfterSideTurnEnd removes itself",
        ["CacophonyPower"] = "covered: read from its code (ReadModel, decision #195), each copy on its own (decision #196): AfterCardDrawn damages a random enemy every N draws (proc damage, tracker)",
        ["CalamityPower"] = "covered: read from its code (ReadModel, decision #268): BeforeCardPlayed binds its owner's Attack, AfterCardPlayed makes its amount of random Attacks of its owner's character into their hand (MakeRandomInHandStep: each a card made, its class the roll's; proc tab)",
        ["ChainsOfBindingPower"] = "covered: Bound afflictions on drawn cards (draw-reactive: noted by the audit); playability read live",
        ["ChildOfTheStarsPower"] = "covered: read from its code (ReadModel, decision #195): AfterStarsSpent block per star spent (proc block)",
        ["ColossusPower"] = "covered: read from its code (ReadModel, decision #195): ModifyDamageMultiplicative halves powered attacks on it from a Vulnerable dealer; AfterSideTurnEnd ticks down as the enemies' turn ends (decision #302: the enemies' side's turn end, run after their turn)",
        ["ConcoctPower"] = "covered: read from its code (ReadModel, decision #195): AfterDamageGiven poison on attack hits through block (proc tab); AfterSideTurnEnd goes as the other side's turn ends",
        ["ConfusedPower"] = "ignore: AfterCardDrawn random costs (costs read live; draws are random)",
        ["ConquerorPower"] = "covered: read from its code (ReadModel, decision #247): ModifyDamageMultiplicative doubles a Sovereign Blade's powered attack on its owner, its tab where that term changes the play's damage (the aim marker where another target has none); AfterSideTurnEnd ticks down",
        ["ConstrictPower"] = "covered: read from its code (ReadModel, decision #195): AfterSideTurnEnd deals its amount to its owner, blockable, dealt by its owner; AfterDeath of its applier removes it",
        ["ConsumingShadowPower"] = "covered: read from its code (ReadModel, decision #291): AfterSideTurnEnd evokes its owner's last orb its amount of times while they have orbs (OrbCmd.EvokeLast)",
        ["CorrosiveWavePower"] = "covered: read from its code (ReadModel, decision #195): AfterCardDrawn poisons all enemies per draw (proc tab)",
        ["CorruptionPower"] = "covered: ModifyCardPlayResultLocation exhausts played Skills (in the simulator its model, decided once before a card play's plays: decision #192; SimAdapter.ExhaustsAfterPlay for the tags); TryModifyEnergyCostInCombatLate makes its owner's Skills cost 0 wherever a simulation reads a hand card's cost (its model's Late pass: SimEngine.HandCost, decision #334)",
        ["CoveredPower"] = "covered: ModifyDamageMultiplicative zeroes powered attacks on the covered player: the intent's damage to you is read through the game's damage hooks; AfterDeath/AfterSideTurnEnd remove it",
        ["CrabRagePower"] = "covered: read from its code (ReadModel, decision #195): AfterDeath of an ally gives Strength and block, then goes (a crossed tab on a play that kills another enemy, read from what its AfterDeath waits on)",
        ["CurlUpPower"] = "covered: read from its code (ReadModel, decision #274): AfterDamageReceived keeps the card of a powered hit on its owner (the card play it comes from), AfterCardPlayed of that card play gives its block and sets Louse Progenitor's Curled; crossed tab",
        ["DampenPower"] = "covered: hand-written (decision #328): AfterDeath of its last caster removes it (its casters read by creature from its own data), and its AfterRemoved upgrades again the cards its AfterApplied downgraded (CardCmd.Upgrade): a card upgraded mid-play isn't followed, so with any downgraded card that's unknown where it happens (TRACKER); AfterApplied's downgrade, in the enemies' turn, changes only cards nothing after it in a simulation plays",
        ["DanseMacabrePower"] = "covered: read from its code (ReadModel, decision #195): BeforeCardPlayed block on costly cards (proc block)",
        ["DarkEmbracePower"] = "covered: read from its code (ReadModel, decision #195): AfterCardExhausted draws its amount for its owner's exhaust, an Ethereal one at the turn's end counted instead (etherealCount); AfterSideTurnEnd draws amount × that count",
        ["DebilitatePower"] = "covered: read from its code (ReadModel, decision #195): AfterSideTurnEnd ticks down as a player owner's turn ends; its Vulnerable and Weak multipliers are read by those models",
        ["DemisePower"] = "covered: read from its code (ReadModel, decision #195): AfterSideTurnEnd deals its amount to its owner, unblockable, no dealer",
        ["DevourLifePower"] = "covered: read from its code (ReadModel, round nineteen): AfterCardPlayed summons its amount for the Soul's player (OstyCmd.Summon: SummonStep, SimEngine.Summon) when its owner plays a Soul (summon tab)",
        ["DieForYouPower"] = "covered: read from its code (ReadModel, decision #265): ModifyUnblockedDamageTarget gives its Osty (a pet with his own powers) the powered attacks on his player that get past their block while he lives; ShouldAllowHitting lets no dead creature receive powers; ShouldCreatureBeRemovedFromCombatAfterDeath and ShouldPowerBeRemovedAfterOwnerDeath keep a dead Osty in the fight with it",
        ["DisintegrationPower"] = "covered: read from its code (ReadModel, decision #195): AfterSideTurnEndLate deals its amount to its owner, blockable, dealt by its owner",
        ["DoomPower"] = "covered: read from its code (ReadModel, decision #310): BeforeSideTurnEnd (the enemies' side) and AfterSideTurnEnd (the players') kill each creature of the side ending whose Doom is at least its HP (DoomKill of GetDoomedCreatures, KillEachStep), then tell AfterDiedToDoom; its IsOwnerDoomed read for End of Days' kill",
        ["DoubleDamagePower"] = "covered: read from its code (ReadModel, decision #195): ModifyDamageMultiplicative, AfterSideTurnEnd's Decrement",
        ["DuplicationPower"] = "covered: read from its code (ReadModel, decision #195): ModifyCardPlayCount replays any card of yours, AfterModifyingCardPlayCount a stack used (the double-play tab from the game's hook; decision #192); AfterSideTurnEnd removes itself",
        ["EchoFormPower"] = "covered: read from its code (ReadModel, decision #195): double plays (Hook.ModifyCardPlayCount: the double-play tab from the game's hook; in the simulator its code decides each card play's count as it starts, on the fight's log: decision #192); its AfterApplied and AfterCardPlayed only show",
        ["EnragePower"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed Strength on any Skill (simulator intents; a crossed tab, read from its code: a play anyone makes helps its owner)",
        ["EnvenomPower"] = "covered: read from its code (ReadModel, decision #195): AfterDamageGiven poisons the target of its owner's powered hits that get HP through",
        ["EscapeArtistPower"] = "covered: read from its code (ReadModel, decision #214): AfterSideTurnEnd goes down by one while above 1 as its owner's side's turn ends (StartPulsing only shows)",
        ["FeelNoPainPower"] = "covered: read from its code (ReadModel, decision #195): AfterCardExhausted block (proc block; simulator for Ethereal exhausts)",
        ["FeralPower"] = "covered: ModifyCardPlayResultLocation returns 0-cost attacks to your hand, decided once before a card play's plays, counted by AfterModifyingCardPlayResultLocation; AfterApplied counts the 0-cost attacks started this turn from the fight's log (proc tab; decision #192)",
        ["FlameBarrierPower"] = "covered: read from its code (ReadModel, decision #241): AfterDamageReceived damages the dealer of a powered attack on its owner (the enemy turn's hits on a player go through the damage on a player); AfterSideTurnEnd removes it as the other side's turn ends, after the enemy turn it hits back in (decision #302: the enemies' side's turn end, run after their turn)",
        ["FlankingPower"] = "covered: read from its code (ReadModel, decision #195): ModifyDamageMultiplicative multiplies powered attacks on its owner by its amount but its applier's, each copy its own (Instanced: its amount and applier); AfterSideTurnEnd removes itself on its owner's turn end",
        ["FlutterPower"] = "covered: read from its code (ReadModel, decision #251): ModifyDamageMultiplicative takes powered attacks on its owner to its DamageDecrease in percent; AfterDamageReceived goes down by one on each powered hit through its block, and at 0 stuns its owner (its IsHovering store beside the stun waits for a later turn); its tab is the stun outcome rule",
        ["FocusPower"] = "covered: read from its code (ReadModel, decision #214): ModifyOrbValue, max(value + Amount, 0) on its owner's orbs, in every orb value the engine works out (a new orb's, after a Focus change, a store)",
        ["FrailPower"] = "covered: read from its code (ReadModel, decision #195): ModifyBlockMultiplicative; AfterSideTurnEnd ticks down after the enemies' turn (decision #302: the enemies' side's turn end, run after their turn), a debuff new on a player then skipping that tick",
        ["FreeAttackPower"] = "covered: BeforeCardPlayed uses a charge on any Attack from hand (free: tab with the energy saved; crossed when wasted)",
        ["FreePowerPower"] = "covered: BeforeCardPlayed uses a charge on any Power from hand (free: tab with the energy saved; crossed when wasted)",
        ["FreeSkillPower"] = "covered: BeforeCardPlayed uses a charge on any Skill from hand (free: tab with the energy saved; crossed when wasted)",
        ["GalvanicPower"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed damages the player who played a Galvanized card (crossed proc damage, decision #221); AfterCardEnteredCombat afflicts a Power entering the fight, which no simulated play plays",
        ["GigantificationPower"] = "covered: read from its code (ReadModel, decision #228): BeforeAttack keeps its owner's first powered attack from an Attack card, ModifyDamageMultiplicative triples that attack (or any while none is kept), AfterAttack lets it go and takes a stack (tab on attacks that spend it)",
        ["GravityPower"] = "covered: read from its code (ReadModel, decision #195): BeforeCardPlayed binds its amount to the play, AfterCardPlayed damages all enemies with it (proc damage); AfterSideTurnEnd removes it",
        ["GuardedPower"] = "covered: read from its code (ReadModel, decision #195): ModifyDamageMultiplicative its DamageDecrease on powered attacks on its owner, each copy its own; AfterDeath removes the copy whose applier died",
        ["HailstormPower"] = "covered: read from its code (ReadModel, decision #195): BeforeSideTurnEnd deals its amount to every hittable enemy when its owner has its FrostOrbs Frost orbs; its tab, plain on a play that channels your first Frost, crossed on one that evokes your last, with the chance when a random orb decides it (decisions #239, #159, #244)",
        ["HardenedShellPower"] = "covered: read from its code (ReadModel, decision #278): ModifyHpLostBeforeOstyLate caps the HP its owner loses at its amount less its own data's damageReceivedThisTurn (0 left alone), AfterModifyingHpLostBeforeOsty flashes, AfterDamageReceived adds the HP a hit not fully blocked took, BeforeSideTurnStart clears the count as every side's turn starts (before the enemies' poison ticks); its HpDisplay only shown",
        ["HardToKillPower"] = "covered: read from its code (ReadModel; v0.114.0): ModifyHpLostBeforeOstyLate caps the HP a hit takes past block at its amount on its owner (0 left alone); AfterModifyingHpLostBeforeOsty flashes. Before v0.114.0 a ModifyDamageCap, which the game's own damage math covered",
        ["HatchPower"] = "covered: read from its code (ReadModel, decision #195): AfterSideTurnEnd counts down as its owner's side's turn ends, an enemy's after its turn (decision #302: the enemies' side's turn end, run after their turn); hatching is a later turn",
        ["HauntPower"] = "covered: read from its code (ReadModel, decision #214): AfterCardPlayed damages a random hittable enemy by its amount (unblockable, unpowered, no dealer) on each Soul its owner plays (proc damage)",
        ["HeistPower"] = "ignore: BeforeDeath returns stolen gold",
        ["HellraiserPower"] = "covered: auto-plays a drawn Strike (AfterCardDrawnEarly) at a random enemy: SimEngine.HellraiserPlays (decision #121); past the cap on endless auto-plays against enemies that can't die, not simulated",
        ["HexPower"] = "covered: afflicts cards (read from the card); AfterDeath removes it with its caster, and its cards' Ethereal goes: the simulator skips their end-of-turn exhaust once the caster is dead (decision #85)",
        ["HighVoltagePower"] = "covered: read from its code (ReadModel, decision #195): AfterSideTurnEnd Strength as its owner's side's turn ends, an enemy's after its turn (decision #302: the enemies' side's turn end, run after their turn), for its later attacks",
        ["IllusionPower"] = "ignore: AfterDeath sets its Revive move (heal, no attack) and ShouldAllowHitting keeps it out of hits until it revives, so its kill holds for the turn; the simulator never ends the fight early; AfterApplied gives its owner Minion (on the board before any play: it's summoned so); ShouldCreatureBeRemovedFromCombatAfterDeath keeps the dead creature in the fight for its revive, past the turn",
        ["ImbalancedPower"] = "covered: read from its code (ReadModel, decision #271): AfterDamageGiven on any hit its owner deals that a block takes whole (result.WasFullyBlocked): on Bowlbug Rock it sets its IsOffBalance, which its Headbutt reads to stun itself (its move's MoveStunAction), else CreatureCmd.Stun of its owner; its tab judged on how the turn ends (stunned in every outcome)",
        ["ImitationLearningPower"] = "covered: on a teammate aimed at you, BeforeCardPlayed clones your Power card (once per play series) and AfterCardPlayed auto-plays it for them (plain proc tab on your Power cards)",
        ["InfernoPower"] = "covered: read from its code (ReadModel, decision #195): AfterDamageReceived damages all enemies (unpowered) each time its owner loses HP on their side's turn (proc tab on cards that cost HP; simulator: end-of-turn and in-play HP loss, yours and teammates'). AfterPlayerTurnStart's self-damage runs as the players' next turn starts, past what a simulation covers",
        ["InfestedPower"] = "covered: read from its code (ReadModel, decision #276): AfterDeath adds four Wrigglers, each set up to start stunned before it joins (SpawnStep: the adapter reads each with every HP it can start with, none an earlier one took: 5, 4, 3, 2 values), joining once the hit is over; ShouldStopCombatFromEnding true; its tab judged on how the turn ends (crossed on a play that can kill it)",
        ["IntangiblePower"] = "covered: read from its code (ReadModel, decision #229): ModifyDamageCap caps a damage to its owner at 1 and ModifyHpLostAfterOsty the HP it takes, each flashing when it changes one (AfterModifyingDamageAmount, AfterModifyingHpLostAfterOsty); AfterSideTurnEnd ticks down as the enemies' turn ends. Hits back in the enemy turn (Thorns, Flame Barrier, Reflect) are damage like any other, capped by it there too",
        ["InterceptPower"] = "covered: read from its code (ReadModel, decision #270): ModifyDamageMultiplicative multiplies powered attacks on its owner by the creatures it covers + 1, its own data's coveredCreatures.Count (which CoveredPower.AfterApplied adds to); AfterSideTurnEnd removes it as the enemies' turn ends (decision #302: the enemies' side's turn end, run after their turn)",
        ["IterationPower"] = "covered: read from its code (ReadModel, decision #195): AfterCardDrawn draws its amount on the first Status drawn this turn: the fight's log, in its place among the listeners; the simulator draws them from your draw pile, in order (decision #90)",
        ["JuggernautPower"] = "covered: AfterBlockGained damages a random enemy (simulator)",
        ["JugglingPower"] = "covered: read from its code (ReadModel, decision #268): BeforeCardPlayed counts its owner's Attacks this turn and keeps the third (its data's cardToClone: the play bound), AfterCardPlayed copies it into their hand its amount of times (beta; main's BeforeCardPlayed copies it as it's played, read from main's code); AfterApplied counts the Attacks started this turn (the fight's log), AfterSideTurnEnd starts the count over (proc tab)",
        ["KnockdownPower"] = "covered: read from its code (ReadModel, decision #195): ModifyDamageMultiplicative multiplies powered attacks on its owner by its amount but its applier's, each copy its own; AfterSideTurnEnd removes itself on its owner's turn end",
        ["MagicBombPower"] = "covered: read from its code (ReadModel, decision #279): AfterSideTurnEnd, while its owner takes part and its applier lives: Flash(), its amount to its owner (unpowered, dealt by its owner), then PowerCmd.Remove; AfterDeath of its applier (not prevented) removes it; each application its own copy (Instanced)",
        ["MasterPlannerPower"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed makes Skills Sly (CardCmd.ApplyKeyword on the card played, decision #218; proc tab)",
        ["MayhemPower"] = "ignore: AfterAutoPrePlayPhaseEntered auto-plays at the start of your turn",
        ["MonarchsGazePower"] = "covered: read from its code (ReadModel, decision #239): AfterDamageGiven puts MonarchsGazeStrengthDownPower (a temporary Strength loss: its IsPositive is false) on whatever its owner's powered attack hits; trigger tab",
        ["MonologuePower"] = "covered: read from its code (ReadModel, decision #195), each copy on its own (decision #196): AfterCardPlayed Strength per card (proc tab)",
        ["NecroMasteryPower"] = "covered: AfterCurrentHpChanged damages all enemies when Osty loses HP: a card that kills Osty (proc tab; simulator). Not modelled: Osty losing HP to an enemy's Thorns or attacks",
        ["NemesisPower"] = "covered: read from its code (ReadModel, decision #307: PowerCmd.Remove of its owner's Intangible instance): AfterSideTurnEnd, every other turn of its own Intangible, else its Intangible removed",
        ["NoBlockPower"] = "covered: read from its code (ReadModel, decision #195): ModifyBlockMultiplicative makes its owner's block from a card that isn't Unpowered 0 (props.HasFlag, HookFact.PropsHave); its AfterSideTurnEnd ticks down as the enemies' turn ends (decision #302: the enemies' side's turn end, run after their turn)",
        ["NoDrawPower"] = "covered: ShouldDraw blocks draws and flashes (SimEngine.DrawPrevented: its tag from the play's log)",
        ["NoEnergyGainPower"] = "ignore: ModifyEnergyGain stops its owner's energy gains (AfterModifyingEnergyGain flashes) and AfterSideTurnEnd removes it, but nothing in the game applies it (only the model list names it, on both branches)",
        ["NostalgiaPower"] = "covered: read from its code (ReadModel, decision #290): ModifyCardPlayResultLocation puts its owner's Attack or Skill bound for the discard pile on top of their draw pile while fewer than its amount of their Attacks and Skills started this turn (the fight's log asked for each type, summed), decided once before a card play's plays; its return changing the location is its proc tab; AfterModifyingCardPlayResultLocation flashes",
        ["OblivionPower"] = "covered: read from its code (ReadModel, decision #195): each card its applier plays applies Doom to it, as its applier (proc tab)",
        ["OneTwoPunchPower"] = "covered: read from its code (ReadModel, decision #195): ModifyCardPlayCount replays your Attacks, AfterModifyingCardPlayCount a stack used (the double-play tab from the game's hook; decision #192); AfterSideTurnEnd removes itself",
        ["OrbitPower"] = "covered: read from its code (ReadModel, decision #195), each copy on its own (decision #196): AfterEnergySpent energy every N spent (proc tab, tracker)",
        ["PagestormPower"] = "covered: read from its code (ReadModel, decision #195): AfterCardDrawn draws its amount on each Ethereal card drawn, in its place among the listeners; the simulator draws them from your draw pile, in order (decision #90)",
        ["PainfulStabsPower"] = "ignore: AfterAttack adds Wounds during its attack",
        ["PaleBlueDotPower"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed applies Draw Cards Next Turn after 5 cards this turn (the fight's log; proc tab, tracker); AfterSideTurnEnd lets it act again",
        ["PanachePower"] = "covered: read from its code (ReadModel, decision #195), each copy on its own (decision #196): AfterCardPlayed damages all enemies every 5 cards (proc damage, tracker)",
        ["PaperCutsPower"] = "covered: read from its code (ReadModel, decision #327): AfterDamageGiven takes its amount of max HP from a player its owner's powered attack got through to (CreatureCmd.LoseMaxHp: LoseMaxHpStep, SimEngine.LoseMaxHp): HP above the new max lost first as a damage, then the max set",
        ["PersonalHivePower"] = "covered: read from its code (ReadModel, decision #257): AfterDamageReceived makes its amount of Dazed into the draw pile of a powered hit's dealer (Osty's player for his), no creator (no created-card hook hears them); crossed proc tab with the count (decision #161)",
        ["PillarOfCreationPower"] = "covered: read from its code (ReadModel, decision #195): AfterCardGeneratedForCombat block per created card (proc block)",
        ["PlatingPower"] = "covered: read from its code (ReadModel, decision #289): BeforeSideTurnEndEarly block for its owner as their side's turn ends; AfterApplied sets an enemy's Decrement to the players' count; AfterSideTurnStart takes that many off an enemy's as the enemies' turn starts past the first round (a player's, as their next turn starts, past the turns a simulation covers); BeforeSideTurnStart's block waits on the players' side's turn starting",
        ["PlowPower"] = "covered: read from its code (ReadModel, decision #275): AfterDamageReceived, HP through that leaves its owner at or below its amount: every Temporary Strength and its Strength removed, Ceremonial Beast's SetStunned (its Ritual on Dread; its flags only show) and a stun with its own stunned move, else a stun; the power goes; its tab judged on how the turn ends",
        ["PossessSpeedPower"] = "covered: hand-written (decision #326): AfterPowerAmountChanged counts the Dexterity its owner takes from each player (its own data's stolenDexterity, read by creature into its holder's fields), AfterDeath of its owner gives each player it back (PowerCmd.Apply, no applier), mid-play included",
        ["PossessStrengthPower"] = "covered: hand-written (decision #326): AfterPowerAmountChanged counts the Strength its owner takes from each player (its own data's stolenStrength, read by creature into its holder's fields), AfterDeath of its owner gives each player it back (PowerCmd.Apply, no applier): a kill during a play gives the Strength back for its later hits",
        ["RagePower"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed block on attacks (proc block)",
        ["RavenousPower"] = "covered: read from its code (ReadModel, decision #251): AfterDeath of another creature on its side while it lives stuns it and gives it its amount of Strength (its IsRavenous store beside the stun waits for a later turn); crossed kill tab, a poison death at the start of the enemies' turn included",
        ["ReaperFormPower"] = "covered: read from its code (ReadModel, decision #195): AfterDamageGiven Doom on its target by what each powered hit of its owner (or its pet) dealt, block included (proc tab; Doom kills after the enemy turn)",
        ["ReattachPower"] = "ignore: AfterDeath sends a segment to its dead move (reattaching at the end of its turn, no attack) and ShouldAllowHitting keeps it out of hits, so its kill holds for the turn; the simulator never ends the fight early; ShouldCreatureBeRemovedFromCombatAfterDeath and ShouldPowerBeRemovedAfterOwnerDeath keep the segment in the fight for its reattaching; ShouldOwnerDeathTriggerFatal is true only once every other segment is dead (read by the adapter: unknown where it isn't a constant, SimAdapter.DeathSetsOffFatal)",
        ["ReboundPower"] = "covered: read from its code (ReadModel, decision #286): ModifyCardPlayResultLocation puts its owner's card bound for the discard pile on top of their draw pile (a location with its pile and position set), decided once before a card play's plays; AfterModifyingCardPlayResultLocation uses a stack (PowerCmd.Decrement); AfterSideTurnEnd removes it as its owner's side's turn ends",
        ["ReflectPower"] = "covered: read from its code (ReadModel, decision #241): AfterDamageReceived damages the dealer of a powered attack on its owner for what its block took; AfterSideTurnStart ticks it down as its owner's side's turn starts, past a simulation",
        ["RegenPower"] = "covered: read from its code (ReadModel, decision #195): BeforeSideTurnEndEarly heals its living owner its amount, up to max HP (CreatureCmd.Heal), and goes down by 1",
        ["RetainHandPower"] = "ignore: ShouldFlush keeps its owner's hand as their turn ends (the flush only moves cards to the discard pile, and nothing a simulation reads after the turn's end looks at the hand); AfterSideTurnEnd ticks down",
        ["RingingPower"] = "ignore: ShouldPlay one Ringing card per turn (playability read live); AfterApplied afflicts its owner's cards with Ringing and AfterRemoved clears it (afflictions read from the card; an enemy's move gives it, past your plays)",
        ["RitualPower"] = "covered: read from its code (ReadModel, decision #195): AfterApplied marks one an enemy got on its own turn; AfterSideTurnEnd Strength at the end of its owner's side's turn (a player's after your plays, an enemy's after its turn (decision #302: the enemies' side's turn end, run after their turn))",
        ["RupturePower"] = "covered: read from its code (ReadModel, decision #243): AfterDamageReceived gives its owner Strength as it loses HP on its side's turn, at once, or, for HP its card in play costs, added to what BeforeCardPlayed bound to that play and given by AfterCardPlayed; its tab is the HP-loss reaction rule (decision #239)",
        ["SandpitPower"] = "covered: hand-written (decision #321): one copy per player it's aimed at (its Target), each with its own amount; AfterSideTurnStartLate, as the enemies' side's turn starts, decrements each copy, and a copy at 0 goes, its AfterRemoved killing that player and their Osty with force unless its holder or that player is dead already (Incoming Damage's Death Skull, Play Preview, the forecast); a Frantic Escape adds to the copy aimed at its player (decision #322). AfterApplied, AfterPowerAmountChanged, AfterCreatureAddedToCombat, AfterOstyRevived and BeforeSideTurnEnd only move creatures on screen and change the music",
        ["SelfFormingClayPower"] = "ignore: AfterBlockCleared block next turn",
        ["SerpentFormPower"] = "covered: read from its code (ReadModel, decision #268): BeforeCardPlayed binds its amount to its owner's play, AfterCardPlayed deals it (unpowered) to a random hittable enemy (a branch per enemy; proc damage)",
        ["ShadowmeldPower"] = "covered: read from its code (ReadModel, decision #280): ModifyBlockMultiplicative multiplies its owner's block by (decimal)Math.Pow(2.0, Amount); AfterSideTurnEnd removes it as its owner's side's turn ends",
        ["ShriekPower"] = "covered: read from its code (ReadModel, decision #251): AfterDamageReceived stuns its owner when HP through its block leaves it at or below its amount, and goes; its tab is the stun outcome rule",
        ["ShrinkPower"] = "ignore: ticks down (damage math); AfterApplied and AfterRemoved only resize its owner on screen and name its applier",
        ["ShroudPower"] = "covered: read from its code (ReadModel, decision #195): AfterPowerAmountChanged block when you apply Doom (proc block)",
        ["SicEmPower"] = "covered: read from its code (ReadModel, round nineteen): AfterDamageGiven summons its amount for the dealer's player (OstyCmd.Summon: SummonStep) when its applier's Osty damages the enemy that has it (summon tab); AfterSideTurnEnd removes it; only your instance is read (SimReaders.Powers: InstancedPerApplier), a teammate's Osty's hits aren't simulated",
        ["SkittishPower"] = "covered: AfterAttack block when a card's attack gets through, once per turn (simulator; crossed tab); AfterSideTurnEnd clears its turn's use as the other side's turn ends",
        ["SleightOfFleshPower"] = "covered: read from its code (ReadModel, decision #195): AfterPowerAmountChanged damages enemies you debuff (proc damage)",
        ["SlipperyPower"] = "covered: read from its code (ReadModel, decision #239): ModifyHpLostAfterOsty lets 1 HP of each damage through, AfterDamageReceived spends a charge; its crossed tab is its reaction to its own HP loss, judged on the turn's outcomes (a warning where ending the turn now sets it off in none)",
        ["SlothPower"] = "ignore: ShouldPlay limits cards per turn (playability read live); BeforeSideTurnStart starts its count over as its owner's turn starts (its turn-start gap: ForecastIgnores)",
        ["SlowPower"] = "covered: read from its code (ReadModel, decision #229): AfterCardPlayed counts the cards played (its SlowAmount), ModifyDamageMultiplicative raises powered attacks on its owner by a tenth each, flashing as it does (AfterModifyingDamageAmount: proc tab); AfterSideTurnStart resets it as its owner's turn starts",
        ["SlumberPower"] = "covered: read from its code (ReadModel, decision #307): AfterDamageReceived takes a stack per hit through block and on the last stuns it with Slumbering Beetle's WakeUpMove (its Plating goes as it acts) and ROLL_OUT_MOVE after it; AfterSideTurnEnd a stack, the last one's WakeUpMove taking its Plating. Its tab compares the enemy's next move with and without the play",
        ["SmoggyPower"] = "covered: Smog makes Skills unplayable after one (crossed proc tab)",
        ["SmokestackPower"] = "covered: read from its code (ReadModel, decision #195): AfterCardGeneratedForCombat damages all enemies per status you create, by the made card's type (a Sadness is a Status) (proc damage)",
        ["SneakyPower"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed gives its owner block on another's Attack (its owner a player: a teammate's block on your Attack, a plain tab, decision #198)",
        ["SoulboundPower"] = "ignore: AfterCardGeneratedForCombat gives the power's owner (a teammate you put it on, or you from a teammate's) Souls in their draw pile when its applier creates Souls: nothing a play or the forecast depends on",
        ["SpeedsterPower"] = "covered: read from its code (ReadModel, decision #195): AfterCardDrawn damages all enemies per draw (proc damage; the simulation's draws)",
        ["SpiritOfAshPower"] = "covered: read from its code (ReadModel, decision #195): BeforeCardPlayed block on Ethereal cards (proc block)",
        ["StampedePower"] = "covered: AfterAutoPostPlayPhaseEntered auto-plays random Attacks from your hand: the simulator branches over the Attacks it can pick and their targets (decision #39); the forecast check skips a turn where a pick changed your hand. A teammate's: left out (a random card from their hand)",
        ["SteamEruptionPower"] = "covered: read from its code (ReadModel, decision #276): AfterDeath calls its Waterfall Giant's TriggerAboutToBlowState: its HP set to 999999999 (a revive: it stays in the fight with only this power) and its next move forced to About to Blow (read from the monster: no attack); ShouldStopCombatFromEnding, ShouldCreatureBeRemovedFromCombatAfterDeath and ShouldPowerBeRemovedAfterOwnerDeath read; its tab judged on how the turn ends (crossed on a play that can kill it)",
        ["StockPower"] = "covered: read from its code (ReadModel, decision #276): AfterDeath while stock is left adds the next Axebot, set up before it joins with one stock less (its HP and its Stock from that, read by the adapter: SpawnStep; the amount it was read with asked as the death runs, another one unknown: decision #315), whose first move (Boot Up) attacks no one; ShouldStopCombatFromEnding true (Thorns killing the last Axebot during its attack leaves its move going on); its tab judged on how the turn ends (crossed on a play that can kill it)",
        ["StormPower"] = "covered: read from its code (ReadModel, decision #195): BeforeCardPlayed binds its amount to a Power played, AfterCardPlayed channels that much Lightning",
        ["StranglePower"] = "covered: read from its code (ReadModel, decision #195): BeforeCardPlayed binds its amount to a card its applier (a player) plays; AfterCardPlayed deals it to its owner (unblockable, no dealer)",
        ["StratagemPower"] = "covered: AfterShuffle picks cards (proc tab on shuffles)",
        ["SubroutinePower"] = "covered: read from its code (ReadModel, decision #195): BeforeCardPlayed binds its amount to a Power played, AfterCardPlayed gives that much energy, one at a time (proc tab)",
        ["SuckPower"] = "ignore: AfterAttack Strength after its own attack (later attacks)",
        ["SurprisePower"] = "covered: AfterDeath effect (crossed kill tab from the simulator); the gremlins it adds read from its code (SimAdapter.SpawnsOf: each CreatureCmd.Add, its monster's HP range and starting powers, and the Heist it gives its Fat Gremlin for each Thievery, decision #346); ShouldStopCombatFromEnding true",
        ["SurroundedPower"] = "covered: BeforeCardPlayed turns you to face a back-attacking target; AfterDeath turns you to face the first enemy left when all are on one side; BeforePotionUsed turns you to face a back-attacking potion target (decision #313); ModifyDamageMultiplicative: the one behind you hits 50% harder (intents after your targeted play or potion, or a kill)",
        ["SwipePower"] = "ignore: BeforeDeath returns a stolen card",
        ["SwordSagePower"] = "covered: Sovereign Blade replays (double-play row, Sovereign Blade tab)",
        ["TaintedPower"] = "covered: read from its code (ReadModel, decision #195): ModifyDamageAdditive; AfterSideTurnEnd removes itself after the enemies' turn (decision #302: the enemies' side's turn end, run after their turn)",
        ["TangledPower"] = "ignore: AfterApplied afflicts its owner's Attacks with Entangled, AfterCardEnteredCombat a new one, AfterRemoved clears them, and TryModifyEnergyCostInCombat raises an Entangled card's cost: afflictions and costs read from the card at each decision (an enemy's move gives it, past your plays)",
        ["TemporaryDexterityPower"] = "ignore: BeforeApplied gives its owner Dexterity as it lands (AfterPowerAmountChanged more as it grows), which changes only a card's block: the play or potion that gives it gains none after it (Anticipate, Speed Potion), and the next decision reads it live; it expires at the end of the turn, after your end-of-turn block",
        ["TemporaryFocusPower"] = "covered: BeforeApplied gives its owner Focus as it lands (AfterPowerAmountChanged more as it grows): the simulator lands it as Focus (SimAdapter.FocusSignOf); it expires at the end of the turn, after your orbs' passives",
        ["TemporaryStrengthPower"] = "covered: BeforeApplied gives its owner Strength as it lands (AfterPowerAmountChanged more as it grows; less for a debuff one): the simulator lands it as Strength (SimAdapter: a temporary Strength power is Strength); it expires at the end of the turn (Strength-loss cards: simulator intents)",
        ["TenderPower"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed Strength and Dexterity down per card, applied by its applier (crossed proc tab: it hurts you; a replay's later plays hit and block 1 less each in the simulator)",
        ["TerritorialPower"] = "covered: read from its code (ReadModel, decision #195): AfterSideTurnEnd Strength as its owner's side's turn ends, an enemy's after its turn (decision #302: the enemies' side's turn end, run after their turn), for its later attacks",
        ["TheBombPower"] = "covered: read from its code (ReadModel, decision #195): BeforeSideTurnEnd ticks each copy down, then a copy on its last tick damages all enemies by its own Damage (each copy its own: decision #196) and goes; a Relic Counters row for each copy (the turns left from its count and its last tick, read from its code, and its Damage: decision #248), no tab (the owner's call)",
        ["TheGambitPower"] = "covered: read from its code (ReadModel, decision #256): AfterDamageReceived kills its owner (CreatureCmd.Kill) as a powered attack gets HP through its block, and goes: a player (The Gambit card's own) dies unless a Fairy in a Bottle or Lizard Tail holds it back; on an enemy, the kill-itself outcome tab",
        ["TheSealedThronePower"] = "covered: read from its code (ReadModel, decision #195): BeforeCardPlayed stars per card (proc tab, Black Hole second pass)",
        ["ThornsPower"] = "covered: read from its code (ReadModel, decision #241): BeforeDamageReceived damages the dealer of a powered attack on its owner (main's also of Omnislice's splash): an enemy's on your attacks (its crossed damage tab), yours on an enemy's attack in the enemy turn",
        ["ThunderPower"] = "covered: read from its code (ReadModel, decision #214): AfterOrbEvoked flashes and damages a Lightning evoke's living targets again, its amount, from its owner (proc tab on cards that evoke Lightning)",
        ["ToricToughnessPower"] = "ignore: AfterBlockCleared block at the start of its own turn",
        ["TrashToTreasurePower"] = "covered: AfterCardGeneratedForCombat channels its amount of random orbs per status you create, by the made card's type (a Sadness is a Status) (model TrashToTreasurePower: unknown orbs)",
        ["UnderworldPower"] = "covered: read from its code (ReadModel, decision #195): AfterDamageGiven Doom on the target of another player's attack, by what it dealt (Doom kills after the enemy turn)",
        ["VeilpiercerPower"] = "ignore: BeforeCardPlayed uses a charge",
        ["ViciousPower"] = "covered: read from its code (ReadModel, decision #195): AfterPowerAmountChanged draws when you apply Vulnerable (simulator model; its draws in the play's log)",
        ["VigorPower"] = "covered: read from its code (ReadModel, decision #228): BeforeAttack keeps its owner's first powered attack and its amount then, ModifyDamageAdditive adds its amount to that attack's hits (or any while none is kept), AfterAttack takes back what it started with (PowerCmd.ModifyAmount; tab on attacks that spend it); an enemy's in the enemy turn too (decision #233)",
        ["VitalSparkPower"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed puts Tainted on the player who played a Tainted card (crossed proc tab, decision #223); AfterPowerAmountChanged only sets the amount its cards' Tainted shows; AfterCardEnteredCombat afflicts a Skill entering the fight, which no simulated play plays",
        ["VoidFormPower"] = "ignore: costs (read live): TryModifyEnergyCostInCombatLate and TryModifyStarCost make its owner's first cards each turn free; BeforeSideTurnStart starts that count over as their turn starts (a projection leaves the hand out); AfterApplied, AfterRemoved, BeforeApplied and BeforePowerAmountChanged only start, stop and hide its visuals",
        ["VulnerablePower"] = "covered: read from its code (ReadModel, decision #195): ModifyDamageMultiplicative with the dealer's Paper Phrog and Cruelty and its owner's Debilitate, each read from the method it calls; AfterSideTurnEnd ticks down as the enemies' turn ends (decision #302: the enemies' side's turn end, run after their turn), a debuff new on a player then skipping that tick (PowerCmd.TickDownDuration's SkipNextDurationTick)",
        ["WeakPower"] = "covered: read from its code (ReadModel, decision #195): ModifyDamageMultiplicative with the target's Paper Krane and its owner's Debilitate, each read from the method it calls; AfterSideTurnEnd ticks down as the enemies' turn ends (decision #302: the enemies' side's turn end, run after their turn), a debuff new on a player then skipping that tick (PowerCmd.TickDownDuration's SkipNextDurationTick)",
        ["WitheringPresencePower"] = "covered: AfterCardPlayed Wither every N of its Target's cards (crossed proc tab, tracker); the Aeonglass has one per player, and the simulator reads only yours",
        // v0.113.0
        ["AimPower"] = "covered: read from its code (ReadModel, decision #232): BeforeAttack keeps its owner's first powered arrow attack and its amount then, ModifyDamageAdditive adds its amount to that attack's hits (Heavy Shot's own ModifyAimAmount read from its code), AfterAttack takes back what it started with",
        ["AllIsWellPower"] = "covered: read from its code (ReadModel, decision #195): AfterCardGeneratedForCombat gains block for each Sadness you create: proc tab with the block on cards that create one (MadeCards: a made Sadness; the simulator lands it)",
        ["AmnesiaPower"] = "ignore: AfterPlayerTurnStart energy loss and ModifyHandDraw at the start of your turn; AfterSideTurnEnd removes it",
        ["CompostPower"] = "covered: read from its code (ReadModel, decision #277): AfterDeath of a creature on the other side: Flash(), then its amount of energy for each living player on its owner's side (a loop over GetTeammatesOf(base.Owner) where alive and a player, PlayerCmd.GainEnergy of each one's); its tab judged on how the turn ends (yours or a teammate's, a bonus when the play kills in every branch)",
        ["CoveringFirePower"] = "covered: read from its code (ReadModel, decision #242): AfterArrowShot gives every other living player its block on each shot of its owner's from a card (with no arrow too), a loop over its side with its filter; no tab by design (teammate-only, decision #25: no settings row)",
        ["DislikeYouPower"] = "covered: read from its code (ReadModel, decision #195): ModifyDamageMultiplicative raises a powered arrow attack on its owner by its amount in percent (props.HasFlag, HookFact.PropsHave); AfterSideTurnEnd removes it as your turn ends",
        ["EncorePower"] = "ignore: ShouldPlay limits the plays left this turn (playability read live); BeforeCardPlayed/AfterCardPlayed count them; AfterSideTurnEnd removes it",
        ["EnsemblePower"] = "covered: read from its code (ReadModel): TryModifyEnergyCostInCombatLate makes the cards of everyone but its owner in their hand or being played free wherever a simulation reads a hand card's cost (SimEngine.HandCost, decision #334); AfterCardPlayed counts down on their last plays",
        ["ExplosiveArrowsPower"] = "covered: read from its code (ReadModel, decision #242): AfterArrowImpaled deals its amount to every hittable enemy when its owner's arrow impales (the Impaled's first arrow is its applier's: the shooter)",
        ["ExplosivePower"] = "covered: read from its code (ReadModel, decision #195): AfterDeath damages every living player (unpowered, blockable), wherever it dies (your card, end of turn, its poison tick), through the damage math on each player (decision #220); crossed kill tab",
        ["ExtraTurnPower"] = "covered: read from its code (ReadModel, decision #195): ShouldTakeExtraTurn gives its owner another turn before the enemies act while its amount is above 0 (SimEngine.ExtraTurns); Incoming Damage then shows only its icon and +1 turn (decision #319); AfterTakingExtraTurn acts as that turn begins, past the horizon",
        ["ImpaledPower"] = "covered: AfterDamageReceived counts the powered hits from its applier's cards (v0.114.0: source.Owner.Creature == Applier, Osty's included) and dislodges an arrow every third; AfterDeath dislodges them all (model ImpaledPower: your arrows in it and its counter; a teammate's arrows come back to them at its death, TeammatesImpaledPower; their hit counter isn't kept, as their only powered hits in your turn are a Howl from Beyond auto-play)",
        ["InsatiableDominionPower"] = "covered: read from its code (ReadModel, decision #283): AfterSideTurnEndLate deals its Damage (unpowered) to its owner as their side's turn ends; AfterApplied sets its Damage to its DisplayAmount, 2 + its owner's turn number (0 for a creature with no player); AfterSideTurnStart, the players' next turn, past every simulation",
        ["KiVersePower"] = "covered: read from its code (ReadModel, decision #195): ModifyCardPlayCount replays your Wah, Cho and Ki cards, AfterModifyingCardPlayCount a stack used (the replay tab from the game's play count; decision #192); AfterSideTurnEnd removes it",
        ["LagavulinMatriarchDominionPower"] = "covered: read from its code (ReadModel, decision #195): AfterSideTurnEnd lowers its owner's Strength and Dexterity after your end of turn (block already gained) and goes: nothing predicted after it reads them",
        ["LostLutePower"] = "ignore: AfterCardPlayed auto-plays cards from the top of your draw pile on your first Wah, Cho and Ki each turn: proc tab; which cards those are is hidden draw order, never predicted (the play check doesn't count what they do)",
        ["LullabyPower"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed gains block once per turn (its own ProccdThisTurn) on the card that completes Wah, Cho and Ki; BeforeSideTurnStart clears its flag as its owner's turn starts (not the enemies')",
        ["MotifPower"] = "covered: read from its code (ReadModel, decision #195): AfterCardPlayed draws its amount per melody (Wah, Cho, Ki) the card plays first this turn: the fight's log, draw tab with the cards drawn (decision #132); the draws count for the Shuffle Tag and draw procs (the play's log)",
        ["QuietusPower"] = "covered: read from its code (ReadModel, decision #242): ModifyDamageMultiplicative multiplies its owner's arrow attack, BeforeAttack keeps that attack and AfterAttack removes it; AfterArrowShot destroys the shot's arrow once it impales (it never comes back); AfterSideTurnEnd removes it",
        ["ShowOffPower"] = "ignore: AfterDeath raises Show Off's Aim when an enemy dies (read from the card on its next play); AfterSideTurnEnd removes it",
        ["SimpletonPower"] = "covered: AfterAutoPostPlayPhaseEntered auto-plays your cheapest card(s) (a random one among ties): the simulator branches over the cards it can pick from your hand and their targets (decision #39). A teammate's: left out (their hand, and their plays aren't built as theirs; decision #80)",
        ["SongPathPower"] = "covered: read from its code (ReadModel, decision #253): AfterCardPlayed gains its amount of block (unpowered) when the card its owner plays is their first Cho this turn (MelodyCmd.IsFirstPlayThisTurn: the play itself, HookFact.PlayIsFirst) (proc block)",
        ["StretchPower"] = "covered: read from its code (ReadModel, decision #269): BeforeCardPlayed keeps its owner's first card play that gains block (its own or its enchantment's: Adroit) with its amount then; ModifyBlockAdditive gives that play's powered block the amount, noting it gave it; AfterCardPlayed of that card play lets it go and spends the amount (PowerCmd.ModifyAmount) when it gave it (Stretch tab)",
        ["TemporaryThornsPower"] = "covered: BeforeApplied/AfterPowerAmountChanged apply Thorns (read as Thorns: simulator); AfterSideTurnEnd removes it after the other side's turn",
        ["TunePower"] = "covered: read from its code (ReadModel, decision #195; v0.114.0): AfterCardPlayed, when the card its owner plays is their first Ki this turn (MelodyCmd.IsFirstPlayThisTurn), upgrades its amount of random upgradable cards in their hand (UpgradeRandomInHandStep, SimEngine.UpgradeInHand: followed while no upgradable card in that hand acts as the turn ends, else unknown; one Stampede or Simpleton could auto-play makes those auto-plays unknown, decision #347) (proc tab)",
        ["ViolentChordPower"] = "covered: read from its code (ReadModel, decision #253): AfterCardPlayed damages every hittable enemy its amount (unpowered) when the card its owner plays is their first Wah this turn (MelodyCmd.IsFirstPlayThisTurn: the play itself, HookFact.PlayIsFirst) (proc damage)",
        ["WuppysLoyaltyPower"] = "covered: read from its code (ReadModel, decision #265): ModifyPreBlockDamageTarget sends powered attacks on its pet owner to its Wuppy (a pet with its own powers, SimPlayer.WuppyHp) while it lives, whole, before block, its own result told and logged; ShouldAllowHitting, ShouldCreatureBeRemovedFromCombatAfterDeath and ShouldPowerBeRemovedAfterOwnerDeath as Die For You's",
        ["YahPower"] = "covered: read from its code (ReadModel, decision #236): AfterArrowDislodged gives its amount of energy when the arrow knocked loose is its owner's, once a turn (its own data's triggeredThisTurn, reset by BeforeSideTurnStart as its owner's turn starts)",
        // Listed with the turn-start, hand-flush, potion, cost, energy, draw and a power's own hooks (decision #323); the same terms as the relics'.
        ["AeonglassDominionPower"] = "covered: read from its code (ReadModel): BeforeHandDraw makes a Wither on top of its owner's draw pile as each of their turns starts; its making isn't read, so a projection that reaches it is unknown",
        ["AggressionPower"] = "ignore: BeforeSideTurnStart moves random Attacks from its owner's discard pile to their hand, upgraded, as their turn starts: cards, which a projection doesn't follow (its turn-start gap: ForecastIgnores)",
        ["ArcheryPower"] = "covered: read from its code (ReadModel): AfterSideTurnStart gives its owner Aim as their side's turn starts (a projection's next turn runs it)",
        ["ArtifactPower"] = "covered: read from its code (ReadModel, decision #331): TryModifyPowerAmountReceived takes a debuff (GetTypeForAmount at the amount so far) that's shown (IsVisible) landing on its owner, in its place in the received pass; AfterModifyingPowerAmountReceived decrements it",
        ["BiasedCognitionPower"] = "covered: read from its code (ReadModel): AfterSideTurnStart takes Focus off its owner as their side's turn starts (a projection's next turn runs it)",
        ["CallOfTheVoidPower"] = "covered: read from its code (ReadModel): BeforeHandDraw makes random Ethereal cards into its owner's hand as each of their turns starts; its making isn't read, so a projection that reaches it is unknown",
        ["ClarityPower"] = "ignore: ModifyHandDraw adds a card to its owner's hand draw and AfterSideTurnStart decrements it as their turn starts: the hand's draw, which a projection leaves out (its turn-start gap: ForecastIgnores)",
        ["ConductorsBatonPower"] = "covered: read from its code (ReadModel): its ModifyDamageMultiplicative lowers powered attacks on its owner; AfterSideTurnStart removes it as their side's turn starts (a projection's next turn runs it)",
        ["CoolantPower"] = "ignore: AfterSideTurnStart gives its owner block for each kind of orb they hold as their turn starts: block, which a projection doesn't follow (its turn-start gap: ForecastIgnores)",
        ["CountdownPower"] = "covered: AfterSideTurnStart puts Doom on a random hittable enemy as its owner's turn starts: not followed there (its turn-start gap, decision #306), so a projection that reaches it is unknown",
        ["CreativeAiPower"] = "covered: read from its code (ReadModel): BeforeHandDraw makes random Powers into its owner's hand as each of their turns starts; its making isn't read, so a projection that reaches it is unknown",
        ["CrimsonMantlePower"] = "covered: read from its code (ReadModel): AfterPlayerTurnStart damages its owner (its SelfDamage) and gives them block as each of their turns starts (a projection's next turn runs it)",
        ["CuriousPower"] = "covered: read from its code (ReadModel): TryModifyEnergyCostInCombat lowers its owner's Powers' costs that are above 0 by its amount, at least 0, wherever a simulation reads a hand card's cost (SimEngine.HandCost, decision #334)",
        ["DemesnePower"] = "ignore: ModifyHandDraw and ModifyMaxEnergy add its amount to its owner's hand draw and max energy, read only as their turn starts (a projection leaves the hand out and doesn't follow energy)",
        ["DemonFormPower"] = "covered: read from its code (ReadModel): AfterSideTurnStart gives its owner Strength as their side's turn starts (a projection's next turn runs it); AfterApplied and AfterRemoved only start and stop its visual",
        ["DrawCardsNextTurnPower"] = "ignore: ModifyHandDraw adds its amount to its owner's next hand draw, AfterModifyingHandDraw removes it: the hand's draw, which a projection leaves out",
        ["EcstasyPower"] = "covered: read from its code (ReadModel): AfterPlayerTurnStart turns Sadnesses in its owner's hand into Joys as each of their turns starts; its body isn't read (a loop), so a projection that reaches it is unknown",
        ["EnergyNextTurnPower"] = "covered: read from its code (ReadModel): AfterEnergyReset gives its owner its energy as their next turn starts, then removes it; its energy isn't read (energy for someone else), so a projection that reaches it is unknown",
        ["EnnuiPower"] = "ignore: BeforeHandDraw makes Sadnesses into its owner's hand and ModifyHandDraw adds to their hand draw as their turn starts: the hand, which a projection leaves out (its turn-start gap: ForecastIgnores)",
        ["EntropyPower"] = "covered: read from its code (ReadModel): AfterPlayerTurnStart has its owner transform cards from their hand as each of their turns starts; its body isn't read (a pick), so a projection that reaches it is unknown",
        ["FastenPower"] = "ignore: AfterModifyingBlockAmount only flashes (its block math is ModifyBlock's, read through the game)",
        ["ForegoneConclusionPower"] = "covered: read from its code (ReadModel): BeforeHandDraw has its owner pick cards from their draw pile into their hand as their next turn starts, then removes it; its body isn't read (a shuffle, a pick), so a projection that reaches it is unknown",
        ["FriendshipPower"] = "ignore: ModifyMaxEnergy raises its owner's max energy, read only by the energy reset as their turn starts",
        ["FurnacePower"] = "ignore: AfterSideTurnStart forges for its owner as their turn starts: a Sovereign Blade's damage, which a projection doesn't follow (its turn-start gap: ForecastIgnores)",
        ["GenesisPower"] = "covered: read from its code (ReadModel): AfterEnergyReset gives its owner stars as each of their turns starts (a projection's next turn runs it)",
        ["GodByrdPower"] = "covered: read from its code (ReadModel): AfterPlayerTurnStart damages every hittable enemy by its amount (unpowered) as its owner's next turn starts, then removes it; its test (AmountOnTurnStart) isn't read, so a projection that reaches it is unknown",
        ["HelloWorldPower"] = "covered: read from its code (ReadModel): BeforeHandDraw makes random Common cards into its owner's hand as each of their turns starts; its making isn't read, so a projection that reaches it is unknown",
        ["HibernatePower"] = "covered: read from its code (ReadModel): AfterPlayerTurnStart decrements it as its owner's turn starts; its test (player.Creature) isn't read, so a projection that reaches it is unknown",
        ["HiddenRootsPower"] = "covered: read from its code (ReadModel): AfterSideTurnStart gives its owner Stretch as their side's turn starts (a projection's next turn runs it)",
        ["InfiniteBladesPower"] = "covered: read from its code (ReadModel): BeforeHandDraw makes Shivs into its owner's hand as each of their turns starts; its making isn't read, so a projection that reaches it is unknown",
        ["LightningRodPower"] = "covered: read from its code (ReadModel): AfterEnergyReset channels Lightning for its owner and decrements it as each of their turns starts (a projection's next turn runs it)",
        ["LoopPower"] = "covered: read from its code (ReadModel): AfterPlayerTurnStart sets off its owner's front orb's passive its amount of times as each of their turns starts; OrbCmd.Passive isn't read, so a projection that reaches it is unknown",
        ["MachineLearningPower"] = "ignore: ModifyHandDraw adds its amount to its owner's hand draw, which a projection leaves out",
        ["MindRotPower"] = "ignore: ModifyHandDraw takes its amount off its owner's hand draw (AfterModifyingHandDraw flashes), which a projection leaves out",
        ["MinionPower"] = "covered: ShouldOwnerDeathTriggerFatal false, read by the adapter (SimAdapter.DeathSetsOffFatal: its death sets off no Fatal); ShouldPowerBeRemovedAfterOwnerDeath false only keeps it on its dead owner",
        ["MythicFormPower"] = "covered: read from its code (ReadModel): BeforeHandDraw makes random Tarot cards into its owner's hand as each of their turns starts; its making isn't read, so a projection that reaches it is unknown",
        ["NeurosurgePower"] = "covered: read from its code (ReadModel): AfterSideTurnStart puts Doom on its owner as their side's turn starts (a projection's next turn runs it)",
        ["NightmarePower"] = "covered: read from its code (ReadModel): BeforeHandDraw makes copies of the card it keeps into its owner's hand as their next turn starts, then removes it; its making isn't read, so a projection that reaches it is unknown",
        ["NoxiousFumesPower"] = "covered: read from its code (ReadModel): AfterSideTurnStart poisons every hittable enemy as its owner's side's turn starts (a projection's next turn runs it)",
        ["PhantomBladesPower"] = "covered: its hand-written model (PhantomBladesPower): its ModifyDamageAdditive on its owner's first Shiv each turn; TryModifyKeywordsInCombat gives their Shivs Retain, which only keeps them in hand at the turn's end; AfterApplied and AfterRemoved only refresh the Shivs' keywords on screen",
        ["PoisonPower"] = "covered: read from its code (ReadModel): AfterSideTurnStart ticks its owner's poison (Trigger: a damage per tick) as their side's turn starts: an enemy's as the enemies' turn starts (SimEngine.EnemyTurnStart), a player's in a projection's next turn",
        ["PrepTimePower"] = "covered: read from its code (ReadModel): AfterSideTurnStart gives its owner Vigor as their side's turn starts (a projection's next turn runs it)",
        ["PyrePower"] = "ignore: ModifyMaxEnergy raises its owner's max energy, read only by the energy reset as their turn starts",
        ["RadiancePower"] = "covered: read from its code (ReadModel): AfterEnergyReset gives its owner energy and decrements it as each of their turns starts; its energy isn't read (energy for someone else), so a projection that reaches it is unknown",
        ["RampartPower"] = "ignore: AfterSideTurnStart gives every Turret Operator block as the players' turn starts (no extra turn): on the board before their decisions, and block, which a projection doesn't follow (its turn-start gap: ForecastIgnores)",
        ["RollingBoulderPower"] = "covered: read from its code (ReadModel): AfterPlayerTurnStart damages every hittable enemy and grows as each of its owner's turns starts; its damage isn't read (commands in a callback), so a projection that reaches it is unknown",
        ["SentryModePower"] = "covered: read from its code (ReadModel): BeforeHandDraw makes Sweeping Gazes into its owner's hand as each of their turns starts; its making isn't read, so a projection that reaches it is unknown",
        ["ShadowStepPower"] = "covered: read from its code (ReadModel): AfterSideTurnStart gives its owner Double Damage as their side's turn starts, then removes it (a projection's next turn runs it)",
        ["SignalBoostPower"] = "covered: read from its code (ReadModel): its ModifyCardPlayCount plays its owner's Powers twice (Hook.ModifyCardPlayCount), AfterModifyingCardPlayCount decrements it (double-play tab)",
        ["SpectrumShiftPower"] = "covered: read from its code (ReadModel): BeforeHandDraw makes random Colorless cards into its owner's hand as each of their turns starts; its making isn't read, so a projection that reaches it is unknown",
        ["SpinnerPower"] = "covered: read from its code (ReadModel): AfterEnergyReset channels Glass for its owner as each of their turns starts (a projection's next turn runs it)",
        ["StarNextTurnPower"] = "covered: read from its code (ReadModel): AfterEnergyReset gives its owner stars as their next turn starts, then removes it; its stars aren't read (stars for someone else), so a projection that reaches it is unknown",
        ["SummonNextTurnPower"] = "covered: read from its code (ReadModel): AfterPlayerTurnStart summons Osty for its owner as their next turn starts, then removes it; its test (AmountOnTurnStart) isn't read, so a projection that reaches it is unknown",
        ["TagTeamPower"] = "covered: its hand-written model (TagTeamPower): its ModifyCardPlayCount plays Attacks aimed at its owner once more for each other applier's copy, AfterModifyingCardPlayCount removes each copy that did (double-play tab)",
        ["TankPower"] = "covered: read from its code (ReadModel, decision #329): AfterApplied gives every other living player of its owner's side Guarded (a loop over CombatState.GetTeammatesOf(base.Owner), applied by its owner), and ModifyDamageMultiplicative makes powered attacks on its owner deal its DamageIncrease (1.5), so a card that gives it carries it; AfterRemoved (taking back the Guarded it gave) is unknown where it runs",
        ["ToolsOfTheTradePower"] = "ignore: ModifyHandDraw adds to its owner's hand draw and AfterPlayerTurnStart has them discard cards as their turn starts: the hand, which a projection leaves out (its turn-start gap: ForecastIgnores)",
        ["TyrannyPower"] = "ignore: ModifyHandDraw adds to its owner's hand draw and AfterPlayerTurnStart has them exhaust cards as their turn starts: the hand, which a projection leaves out (its turn-start gap: ForecastIgnores)",
        ["WasteAwayPower"] = "ignore: ModifyMaxEnergy lowers its owner's max energy, read only by the energy reset as their turn starts",
        ["WellLaidPlansPower"] = "ignore: ShouldFlush keeps its owner's hand as their turn ends: the flush only moves cards to the discard pile (CardPileCmd.Add, no discard hooks), and nothing a simulation reads after the turn's end looks at the hand",
        ["WraithFormPower"] = "covered: read from its code (ReadModel): AfterSideTurnStart takes Dexterity off its owner as their side's turn starts (a projection's next turn runs it)",
        ["YouAreMinePower"] = "covered: read from its code (ReadModel): AfterPlayerTurnStart, from its owner's turn 2, gives them Weak, Vulnerable and Frail and removes it; its body isn't read (acts on player.Creature), so a projection that reaches it is unknown",

        // ======== cards
        ["BansheesCry"] = "ignore: its own cost (read from the card)",
        ["Bombardment"] = "ignore: auto-plays at the start of your turn",
        ["DrumOfBattle"] = "covered: AfterCardExhausted energy when it's exhausted (SimPlayer.ExhaustingInHand, ExhaustFromHandAction; its own exhaust under Corruption)",
        ["Enthralled"] = "ignore: playability (CanPlay)",
        ["Flatten"] = "ignore: its own cost after Osty attacks (read from the card)",
        ["HowlFromBeyond"] = "covered: AfterAutoPostPlayPhaseEntered auto-plays it from its owner's exhaust pile at the end of the turn: the simulator plays each copy in the auto-play phase, yours and teammates' (their card: SimEngine.AutoPostPlay in player order)",
        ["IAmInvincible"] = "covered: AfterAutoPostPlayPhaseEntered auto-plays it from the top of your draw pile: the simulator gains its block when every card in its owner's draw pile is a copy of it (otherwise the top is hidden draw order), yours and teammates'; a play that draws the whole pile leaves it uncertain",
        ["KinglyKick"] = "ignore: its own cost on draw (read from the card)",
        ["KinglyPunch"] = "ignore: its own cost on draw (read from the card)",
        ["MakeItSo"] = "covered: AfterCardPlayedLate returns it to your hand (from any pile but your hand) when your Skills played this turn reach a multiple of 3: a return tab on the Skill that does it, its count in the relic tracker, and your hand size after (Procs.ReturnToHand; a full hand sends it to the discard pile)",
        ["Melancholy"] = "ignore: its own cost when an enemy dies (read from the card)",
        ["Midnight"] = "ignore: its own cost (read from the card)",
        ["Normality"] = "ignore: playability (CanPlay)",
        ["Pinpoint"] = "ignore: its own cost (read from the card)",
        ["Regret"] = "covered: BeforeSideTurnEnd counts the hand (model RegretCard, a card listener after your powers and relics); its OnTurnEndInHand HP loss of that count is in the simulator (SimEngine.HandAtTurnEnd)",
        ["RightHandHand"] = "covered: AfterCardPlayedLate returns it from your discard pile when you play a card whose energy value is 2 or more: a return tab on those cards and your hand size after (Procs.ReturnToHand)",
        ["RocketPunch"] = "ignore: its own cost when statuses are created (read from the card)",
        ["SovereignBlade"] = "ignore: AfterCardChangedPiles forge visuals; main's AfterTransformedFrom only removes its blade's node (looks)",
        ["Stomp"] = "ignore: its own cost (read from the card)",
        ["Void"] = "covered: AfterCardDrawn energy loss when it's drawn (model VoidCard, a card listener after your powers' and relics' draw reactions: SimDrawCard.EnergyLostOnDraw, CardEnergy.LostWhenDrawn)",
        // v0.113.0
        ["EpicRetrieval"] = "covered: AfterArrowDislodged energy when its own hit dislodges your arrow (CardEnergy.Rules: SimCardPlay.ArrowEnergy)",
        ["SpoilsMap"] = "ignore: AfterCardChangedPiles keeps its map quest and AfterCreated sets the act it marks on the map (out of combat)",
        ["WhistlingArrows"] = "covered: AfterArrowShot returns it from your discard pile when you shoot an arrow: a return tab on shots with an arrow ready and your hand size after (Procs.ReturnToHand)",
        // Listed with the turn-start and cost hooks (decision #323).
        ["Bolas"] = "ignore: BeforeHandDraw returns it to its owner's hand as their turn starts when they played it last turn: the hand, which a projection leaves out",
        ["Bounce"] = "ignore: BeforeHandDraw returns it to its owner's hand as their turn starts when they played it last turn: the hand, which a projection leaves out",
        ["ChoLimbs"] = "gap: TryModifyEnergyCostInCombat takes 2 off its own cost once a Wah or Ki melody is played this turn: cards aren't read as hook models, so a board with it in a pile has every hand card's live cost unknown (SimState.CostsUnknown, decision #334): Simpleton's and Mummified Hand's picks there are unknown",
        ["Coda"] = "gap: TryModifyEnergyCostInCombat takes 2 off its own cost for each melody played this turn: cards aren't read as hook models, so a board with it in a pile has every hand card's live cost unknown (SimState.CostsUnknown, decision #334): Simpleton's and Mummified Hand's picks there are unknown",
        ["ThrummingHatchet"] = "ignore: BeforeHandDraw returns it to its owner's hand as their turn starts when they played it last turn: the hand, which a projection leaves out",

        // ======== monsters (each hook read as a program, decision #195: MonsterHookTests holds these entries to what its code does)
        // AfterAddedToRoom, BeforeRemovedFromRoom and OnDieToDoom (decision #330): what a monster's AfterAddedToRoom does as a fight starts is on
        // the board the first decision reads; one a death adds mid-fight starts with the powers it gives itself (SimAdapter.StartingPowers, read
        // from its code); one a move summons isn't in the enemy turn's state yet (TRACKER: summons in a move).
        ["Aeonglass"] = "ignore: AfterAddedToRoom PowerCmd.Apply Withering Presence on each opponent and Artifact on itself at the fight's start (on the board the first decision reads); its other hooks set music and match created Withers to its upgrades",
        ["Crusher"] = "ignore: AfterAddedToRoom PowerCmd.Apply Back Attack, Crab Rage and (beta) Plating to itself at the fight's start (on the board the first decision reads); its other hooks only change music",
        ["DecimillipedeSegment"] = "ignore: AfterAddedToRoom sets its max HP (CreatureCmd.SetMaxAndCurrentHp) and PowerCmd.Apply Reattach to itself at the fight's start (on the board the first decision reads); AfterDeath only changes a texture (its revive is ReattachPower)",
        ["KinPriest"] = "ignore: AfterDeath of its last follower only calls AllFollowerDeathResponse, which plays a talk line; its move doesn't change",
        ["LagavulinMatriarch"] = "ignore: AfterAddedToRoom puts it to sleep at the fight's start (PowerCmd.Apply Plating and Asleep, IsAwake: on the board the first decision reads); AfterDamageReceived only marks its shell awake (visual)",
        ["Queen"] = "ignore: AfterAddedToRoom keeps the Amalgam at the fight's start (on the board); BeforeRemovedFromRoom only clears an animation; AfterDeath of the Amalgam stores HasAmalgamDied (her conditional branches read it as it stands: MoveConditions) and calls SetMoveImmediate, swapping Burn Bright for Me (Strength to her allies, 20 block) for Enrage (2 Strength): neither attacks, so neither's block or Strength changes a hit this turn, and with the Amalgam dead Burn Bright's Strength has no one to land on",
        ["Rocket"] = "ignore: AfterAddedToRoom PowerCmd.Apply Surrounded on its opponents and Back Attack, Crab Rage and (beta) Plating on itself at the fight's start (on the board the first decision reads); its other hooks only change music and sound",
        ["SoulFysh"] = "ignore: hooks only change music",
        ["TestSubject"] = "ignore: AfterAddedToRoom PowerCmd.Apply Adaptable and Enrage to itself at the fight's start (on the board the first decision reads) and listens for Intangible to change its color, which BeforeRemovedFromRoom stops; its other hooks only change color and music (its revive is AdaptablePower)",
        ["TheInsatiable"] = "ignore: hook only changes music",
        ["Vantom"] = "ignore: AfterAddedToRoom PowerCmd.Apply Slippery to itself at the fight's start (on the board the first decision reads); AfterDeath only changes music",
        ["WaterfallGiant"] = "ignore: AfterAddedToRoom sets its CurrentPressureGunDamage at the fight's start (on the board: its Pressure Gun intent reads it as it stands); its other hooks only change music and sound",
        ["Axebot"] = "covered: AfterAddedToRoom PowerCmd.Apply Stock of its StockAmount while above 0: on the board at the fight's start, and an Axebot a Stock's death adds mid-fight starts with it (SimAdapter.StartingPowers, read from its code)",
        ["BattleFriendV1"] = "ignore: AfterAddedToRoom PowerCmd.Apply 3 of Battleworn Dummy's time limit to itself at the fight's start (on the board the first decision reads)",
        ["BattleFriendV2"] = "ignore: AfterAddedToRoom PowerCmd.Apply 3 of Battleworn Dummy's time limit to itself at the fight's start (on the board the first decision reads)",
        ["BattleFriendV3"] = "ignore: AfterAddedToRoom PowerCmd.Apply 3 of Battleworn Dummy's time limit to itself at the fight's start (on the board the first decision reads)",
        ["BowlbugEgg"] = "ignore: AfterAddedToRoom only runs MonsterModel's own (nothing)",
        ["BowlbugRock"] = "ignore: AfterAddedToRoom PowerCmd.Apply Imbalanced to itself at the fight's start (on the board the first decision reads)",
        ["BygoneEffigy"] = "ignore: AfterAddedToRoom PowerCmd.Apply Slow to itself at the fight's start (on the board the first decision reads)",
        ["Byrdonis"] = "ignore: AfterAddedToRoom PowerCmd.Apply Territorial to itself at the fight's start (on the board the first decision reads)",
        ["Chomper"] = "ignore: AfterAddedToRoom PowerCmd.Apply 2 Artifact to itself at the fight's start (on the board the first decision reads)",
        ["CorpseSlug"] = "ignore: AfterAddedToRoom PowerCmd.Apply Ravenous to itself at the fight's start (on the board the first decision reads)",
        ["CubexConstruct"] = "ignore: AfterAddedToRoom gains 13 block (CreatureCmd.GainBlock) and PowerCmd.Apply Artifact at the fight's start (on the board the first decision reads), and marks itself burrowed and listens for its HP falling, both for its looks and sound only; BeforeRemovedFromRoom stops them",
        ["Entomancer"] = "ignore: AfterAddedToRoom PowerCmd.Apply Personal Hive to itself at the fight's start (on the board the first decision reads)",
        ["Exoskeleton"] = "ignore: AfterAddedToRoom PowerCmd.Apply 9 Hard to Kill to itself at the fight's start (on the board the first decision reads)",
        ["EyeWithTeeth"] = "covered: AfterAddedToRoom PowerCmd.Apply Illusion to itself, read as it joins (SimAdapter.StartingPowers, with the Minion Illusion's AfterApplied gives it) as Fogmog's move adds it in the enemy turn (MoveSpawnAction, decision #381)",
        ["FossilStalker"] = "ignore: AfterAddedToRoom PowerCmd.Apply 3 Suck to itself at the fight's start (on the board the first decision reads)",
        ["FrogKnight"] = "ignore: AfterAddedToRoom PowerCmd.Apply Plating to itself and clears HasBeetleCharged (its conditional branch reads it as it stands) at the fight's start (on the board the first decision reads)",
        ["GasBomb"] = "gap: AfterAddedToRoom PowerCmd.Apply Minion to itself as Living Fog's move summons it, in a loop while a slot is free, which the enemy turn doesn't follow yet (TRACKER: summons in a move); on the board from the next decision",
        ["GlobeHead"] = "ignore: AfterAddedToRoom PowerCmd.Apply Galvanic to itself at the fight's start (on the board the first decision reads)",
        ["GremlinMerc"] = "ignore: AfterAddedToRoom PowerCmd.Apply Surprise to itself and Thievery for each player at the fight's start (on the board the first decision reads)",
        ["Guardbot"] = "ignore: AfterAddedToRoom only places it where Fabricator's summon drops it (looks)",
        ["InfestedPrism"] = "ignore: AfterAddedToRoom PowerCmd.Apply Vital Spark to itself at the fight's start (on the board the first decision reads)",
        ["Inklet"] = "ignore: AfterAddedToRoom PowerCmd.Apply Slippery to itself at the fight's start (on the board the first decision reads)",
        ["KinFollower"] = "ignore: AfterAddedToRoom PowerCmd.Apply Minion to itself at the fight's start (on the board the first decision reads)",
        ["KnowledgeDemon"] = "ignore: BeforeRemovedFromRoom only changes music",
        ["LivingShield"] = "ignore: AfterAddedToRoom PowerCmd.Apply 25 Rampart to itself at the fight's start (on the board the first decision reads)",
        ["LouseProgenitor"] = "ignore: AfterAddedToRoom PowerCmd.Apply Curl Up to itself at the fight's start (on the board the first decision reads)",
        ["MechaKnight"] = "ignore: AfterAddedToRoom PowerCmd.Apply 3 Artifact to itself at the fight's start (on the board the first decision reads)",
        ["MysteriousKnight"] = "ignore: AfterAddedToRoom PowerCmd.Apply 6 Strength and 6 Plating to itself at the fight's start (on the board the first decision reads)",
        ["Noisebot"] = "ignore: AfterAddedToRoom only places it where Fabricator's summon drops it (looks)",
        ["Ovicopter"] = "ignore: AfterAddedToRoom and BeforeRemovedFromRoom only start and stop a sound",
        ["OwlMagistrate"] = "ignore: BeforeRemovedFromRoom only stops a sound",
        ["Parafright"] = "covered: AfterAddedToRoom PowerCmd.Apply Illusion to itself, read as it joins (SimAdapter.StartingPowers, with the Minion Illusion's AfterApplied gives it) as The Obscura's move adds it in the enemy turn (MoveSpawnAction, decision #381)",
        ["PhantasmalGardener"] = "ignore: AfterAddedToRoom PowerCmd.Apply Skittish to itself at the fight's start (on the board the first decision reads)",
        ["PhrogParasite"] = "ignore: AfterAddedToRoom PowerCmd.Apply 4 Infested to itself at the fight's start (on the board the first decision reads)",
        ["PunchConstruct"] = "ignore: AfterAddedToRoom PowerCmd.Apply Artifact to itself and takes its StartingHpReduction off its HP at the fight's start (on the board the first decision reads)",
        ["ScrollOfBiting"] = "ignore: AfterAddedToRoom PowerCmd.Apply 2 Paper Cuts to itself at the fight's start (on the board the first decision reads)",
        ["Seapunk"] = "ignore: AfterAddedToRoom only runs MonsterModel's own (nothing)",
        ["SewerClam"] = "ignore: AfterAddedToRoom PowerCmd.Apply Plating to itself at the fight's start (on the board the first decision reads)",
        ["SkulkingColony"] = "ignore: AfterAddedToRoom PowerCmd.Apply 20 Hardened Shell to itself at the fight's start (on the board the first decision reads)",
        ["SlumberingBeetle"] = "ignore: AfterAddedToRoom PowerCmd.Apply Plating and 3 Slumber to itself at the fight's start (on the board the first decision reads); the rest is its sleep's look and sound",
        ["SnappingJaxfruit"] = "ignore: AfterAddedToRoom and BeforeRemovedFromRoom only start and stop a sound",
        ["SoulNexus"] = "ignore: AfterAddedToRoom listens for its death and BeforeRemovedFromRoom clears an animation: looks only",
        ["Stabbot"] = "ignore: AfterAddedToRoom only places it where Fabricator's summon drops it (looks)",
        ["TerrorEel"] = "ignore: AfterAddedToRoom PowerCmd.Apply Shriek to itself at the fight's start (on the board the first decision reads)",
        ["TheAdversaryMkOne"] = "ignore: AfterAddedToRoom PowerCmd.Apply Artifact (0) to itself at the fight's start (on the board the first decision reads)",
        ["TheAdversaryMkTwo"] = "ignore: AfterAddedToRoom PowerCmd.Apply 1 Artifact to itself at the fight's start (on the board the first decision reads)",
        ["TheAdversaryMkThree"] = "ignore: AfterAddedToRoom PowerCmd.Apply 2 Artifact to itself at the fight's start (on the board the first decision reads)",
        ["TheForgotten"] = "ignore: AfterAddedToRoom PowerCmd.Apply Possess Speed to itself at the fight's start (on the board the first decision reads)",
        ["TheLost"] = "ignore: AfterAddedToRoom PowerCmd.Apply Possess Strength to itself at the fight's start (on the board the first decision reads)",
        ["ThievingHopper"] = "ignore: AfterAddedToRoom PowerCmd.Apply 5 Escape Artist to itself at the fight's start (on the board the first decision reads); BeforeRemovedFromRoom only stops a sound",
        ["TorchHeadAmalgam"] = "ignore: AfterAddedToRoom PowerCmd.Apply Minion to itself at the fight's start (on the board the first decision reads); OnDieToDoom only hides its torches (looks)",
        ["ToughEgg"] = "gap: AfterAddedToRoom PowerCmd.Apply Hatch to itself (1 or 2 by whose turn it is) as Ovicopter's move summons it, or hatches it (CreatureCmd.SetMaxAndCurrentHp of a roll), which the enemy turn doesn't follow yet (TRACKER: summons in a move); on the board from the next decision",
        ["TwoTailedRat"] = "ignore: AfterAddedToRoom only runs MonsterModel's own (nothing)",
        ["Zapbot"] = "gap: AfterAddedToRoom PowerCmd.Apply 2 High Voltage to itself as Fabricator's move summons it, a random bot of a set, which the enemy turn doesn't follow yet (TRACKER: summons in a move); on the board from the next decision",

        // ======== potions, enchantments, afflictions
        ["FairyInABottle"] = "covered: ShouldDie (before Lizard Tail's ShouldDieLate)/AfterPreventingDeath heals you from 0 to 30% of max HP, used up (simulator: SimPlayer.Fairies; Incoming Damage's revive row)",
        // OnPlay: acts on every play of the card, after the card's own (CardModel: EnchantmentModel.OnPlay).
        // Its EnchantDamage*, EnchantBlock* and EnchantPlayCount (decision #330): the game's damage and block math, and its play count
        // (CardModel.GetEnchantedReplayCount, which CardPlays.Count takes), called live by the adapter for each play.
        ["Adroit"] = "covered: read from its code (ReadModel, an enchantment: decision #217): OnPlay block of its Amount on every play, after the card's own; RecalculateValues sets that block to its Amount (read with it)",
        ["Corrupted"] = "covered: read from its code (ReadModel, an enchantment: decision #217): OnPlay 2 unblockable damage to its card's owner on every play (the HP a card costs); its EnchantDamageMultiplicative 1.5× on a powered attack is the game's damage math, called live",
        ["Inky"] = "covered: read from its code (ReadModel, an enchantment: decision #217): OnPlay Weak on the play's target, or every enemy for a card aimed at them all, on every play",
        ["Momentum"] = "covered: OnPlay raises the card's damage by its Amount for its later plays; its EnchantDamageAdditive is the game's math, called live, which reads it on later turns, and a replay in the same play gets it (PlayGrowth, decision #160)",
        ["Glam"] = "covered: EnchantPlayCount plays its card Times more while it isn't Disabled: the game's play count, called live (CardModel.GetEnchantedReplayCount: replay tab); AfterCardPlayed only marks it used",
        ["Goopy"] = "covered: AfterCardPlayed grows its amount, so its EnchantBlockAdditive (Amount - 1, the game's math, called live) is read through the game on later turns and a replay in the same play gets 1 more (PlayGrowth, decision #160)",
        ["Imbued"] = "ignore: auto-plays at the start of your turn",
        ["Instinct"] = "covered: EnchantDamageMultiplicative doubles its card's powered attack damage: the game's damage math, called live",
        ["Nimble"] = "covered: EnchantBlockAdditive adds its Amount to its card's block: the game's block math, called live",
        ["Sharp"] = "covered: EnchantDamageAdditive adds its Amount to its card's powered attack damage: the game's damage math, called live",
        ["Slither"] = "ignore: AfterCardDrawn random cost on draw (costs read live)",
        ["Spiral"] = "covered: EnchantPlayCount plays its card Times more: the game's play count, called live (CardModel.GetEnchantedReplayCount: replay tab)",
        ["TezcatarasEmber"] = "covered: EnchantDamageAdditive adds its Damage to its card's powered attack damage: the game's damage math, called live",
        ["Vigorous"] = "covered: AfterCardPlayed disables it after the first play (its EnchantDamageAdditive bonus is the game's math, called live, on later turns), so a replay in the same play loses the bonus (PlayGrowth: EnchantFirstPlayOnly, decision #160)",
        // v0.113.0: OnPlay acts on every play of the card (replays too) until AfterCardPlayed disables it
        ["Sown"] = "covered: read from its code (ReadModel, an enchantment: decision #217): OnPlay energy while Normal, after the card's own; AfterCardPlayed (beta) or OnPlay (main) disables it",
        ["Swift"] = "covered: read from its code (ReadModel, an enchantment: decision #217): OnPlay draws its amount while Normal; AfterCardPlayed (beta) or OnPlay (main) disables it; the Return tags count it from its OnPlay (ReadModel.DrawsOnPlay)",
        // Listed with the shuffle and hand-flush hooks (decision #323).
        ["PerfectFit"] = "covered: read from its code (ReadModel): ModifyShuffleOrder puts its card on top of the draw pile when the discard pile is shuffled in (not the fight's first shuffle): the simulator's shuffle (SimDrawPile.Shuffled) reads it from its code",
        ["SlumberingEssence"] = "ignore: BeforeFlush makes its card in hand cost 1 less until played as its owner's hand is flushed: a cost for the next turn, after everything a simulation reads (HookReader: it only changes card costs at the flush)",
        ["Hexed"] = "covered: read from its code (ReadModel, an affliction: decision #225): AfterCardEnteredCombat clears it from its own card entering without Hex, which no simulated play plays; its Ethereal is HexPower's (SimAdapter.EtherealOnlyFromHex)",

        // ======== orbs (OrbModel's own hooks, decision #330; each read from its code: decision #214)
        ["DarkOrb"] = "covered: read from its code (ReadModel, OrbKind.Dark): Passive adds its passive value to its evoke value, BeforeTurnEndOrbTrigger runs it as its owner's turn ends; Evoke damages the hittable enemy with the least HP",
        ["FrostOrb"] = "covered: read from its code (ReadModel, OrbKind.Frost): Passive block for its owner (every player under Hibernate), BeforeTurnEndOrbTrigger runs it as its owner's turn ends; Evoke the same with its evoke value",
        ["GlassOrb"] = "covered: read from its code (ReadModel, OrbKind.Glass): Passive damages every hittable enemy and loses 1 of its value, BeforeTurnEndOrbTrigger runs it as its owner's turn ends; Evoke damages every hittable enemy",
        ["LightningOrb"] = "covered: read from its code (ReadModel, OrbKind.Lightning): Passive damages a random enemy (a branch each), BeforeTurnEndOrbTrigger runs it as its owner's turn ends; Evoke the same with its evoke value",
        ["PlasmaOrb"] = "covered: read from its code (ReadModel, OrbKind.Plasma): Passive energy, AfterTurnStartOrbTrigger runs it as its owner's turn starts; Evoke energy",
    };

    private static readonly string[] Folders = { "Relics", "Powers", "Cards", "Monsters", "Orbs", "Potions", "Enchantments", "Afflictions" };

    /// <summary>
    /// What an enchantment or affliction does on every play of its card, after the card (CardModel: EnchantmentModel.OnPlay,
    /// AfflictionModel.OnPlay): reviewed like a hook (a card's own OnPlay is what its program reads). Kept in step with
    /// scripts/coverage-ledger.mjs.
    /// </summary>
    private static readonly string[] OnCardHooks = { "OnPlay" };

    private static bool OnCard(Type type) => typeof(EnchantmentModel).IsAssignableFrom(type) || typeof(AfflictionModel).IsAssignableFrom(type);

    private static Dictionary<Type, Entry>? _byType;

    /// <summary>The running game's classes against the ledger (built once).</summary>
    /// <remarks>Keyed by type: two classes can share a name in different folders (Pael's Legion the relic and the monster).</remarks>
    public static IReadOnlyDictionary<Type, Entry> ByType => _byType ??= Build();

    private static Dictionary<Type, Entry> Build()
    {
        var hookSet = Hooks.ToHashSet();
        var onCardSet = hookSet.Concat(OnCardHooks).ToHashSet();
        var ledger = Generated.ToDictionary(folderType => (folderType.Folder, folderType.Type), folderType => folderType);
        // Main's hooks where they differ from beta's (one DLL serves both branches, decision #163): either set is the reviewed one.
        var onMain = GeneratedMain.ToDictionary(folderType => (folderType.Folder, folderType.Type), folderType => folderType.Hooks);
        var result = new Dictionary<Type, Entry>();
        foreach (var type in ModelTypes())
        {
            string hooks;
            try
            {
                hooks = string.Join(",", type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(method => (OnCard(type) ? onCardSet : hookSet).Contains(method.Name) && method.IsVirtual && method.GetBaseDefinition().DeclaringType != type)
                    .Select(method => method.Name).Distinct().OrderBy(name => name, StringComparer.Ordinal));
            }
            catch
            {
                continue;
            }
            var key = (type.Namespace?.Split('.').Last() ?? "", type.Name);
            var known = ledger.TryGetValue(key, out var folderType);
            if (!known && hooks.Length == 0) continue;
            Entry entry;
            if (!known) entry = new Entry(type.Name, Status.New, "not in the ledger (new in this game version?)", hooks);
            else if (folderType.Hooks != hooks && !(onMain.TryGetValue(key, out var mainHooks) && mainHooks == hooks))
                entry = new Entry(type.Name, Status.Changed, $"hooks were {folderType.Hooks}, now {hooks}", hooks);
            else if (Reviewed.TryGetValue(type.Name, out var why))
                entry = new Entry(type.Name, why.StartsWith("gap:", StringComparison.Ordinal) ? Status.Gap : why.StartsWith("covered:", StringComparison.Ordinal) ? Status.Covered : Status.Ignored, why, hooks);
            else entry = new Entry(type.Name, Status.Unreviewed, "not reviewed", hooks);
            result[type] = entry;
        }
        return result;
    }

    /// <summary>The game's model classes the coverage check and the audits read (relics, powers, cards...).</summary>
    public static IEnumerable<Type> ModelTypes()
    {
        Type[] all;
        try { all = typeof(AbstractModel).Assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { all = ex.Types.Where(type => type != null).ToArray()!; }
        return all.Where(type => type is { IsAbstract: false, IsClass: true } && typeof(AbstractModel).IsAssignableFrom(type)
                              && Folders.Any(folder => type.Namespace == "MegaCrit.Sts2.Core.Models." + folder));
    }

    /// <summary>
    /// Earlier game versions players still run whose coverage was reviewed before the ledger moved on (decision #125): not
    /// reported as a version the coverage wasn't checked against. v0.113.0, the first beta build (patched to v0.113.1), was reviewed in 0.31.29, and
    /// v0.113.1 until beta moved to v0.114.0 (the rules that changed there are told from the running game's code: BranchRules, read models).
    /// Main's (SupportedVersions.Main) and the one before it, v0.111.0 (code-identical to v0.111.1), too.
    /// </summary>
    internal static readonly string[] ReviewedGameVersions = { "v0.113.1", "v0.113.0", "v0.111.0", SupportedVersions.Main };

    /// <summary>Whether the coverage was checked against this game version: the ledger's, or one reviewed before it.</summary>
    internal static bool ReviewedVersion(string game) =>
        ReviewedGameVersions.Append(LedgerGameVersion).Any(version => System.Text.RegularExpressions.Regex.IsMatch(game, @"(^|[^0-9.])v?" + System.Text.RegularExpressions.Regex.Escape(version.TrimStart('v')) + @"($|[^0-9])"));

    /// <summary>One summary line at load: the game's version against the ledger's, and the counts.</summary>
    public static void LogSummary(ModLog log, Sts2Mods.Core.Diagnostics.ErrorReporter? reporter)
    {
        try
        {
            var game = SafeGameVersion();
            var byStatus = ByType.Values.GroupBy(entry => entry.Status).ToDictionary(status => status.Key, status => status.ToList());
            int N(Status status) => byStatus.TryGetValue(status, out var entries) ? entries.Count : 0;
            log.Detail($"[coverage] game {game}, ledger {LedgerGameVersion}: {ByType.Count} classes with combat hooks; covered {N(Status.Covered)}, ignored {N(Status.Ignored)}, " +
                     $"known gaps {N(Status.Gap)}, unreviewed {N(Status.Unreviewed)}, new {N(Status.New)}, changed {N(Status.Changed)}.");
            foreach (var status in new[] { Status.New, Status.Changed, Status.Unreviewed })
                if (byStatus.TryGetValue(status, out var otherEntries))
                    log.Detail($"[coverage] {status.ToString().ToLowerInvariant()}: {string.Join(", ", otherEntries.Select(entry => entry.Type))}");
            foreach (var entry in CardCoverage.Issues)
                log.Detail($"[coverage] card code: {entry.Key}: {string.Join("; ", entry.Value)}");
            if (!string.IsNullOrEmpty(game) && !ReviewedVersion(game))
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
/// The game versions the mod is reviewed for, one per Steam branch (decision #170): beta's is the coverage ledger's (generated from the beta
/// decompile, which the mod is written for: decision #163), main's is set once docs/DEVELOPMENT.md "When main updates" is done. The branch
/// checks (GameBranchTests) fail when either decompile, or the installed game, is a build these don't name, so a new build on either branch
/// can't go unreviewed. Players still on an older build of a branch aren't supported apart from it.
/// </summary>
internal static class SupportedVersions
{
    public const string Beta = CoverageLedger.LedgerGameVersion;
    public const string Main = "v0.111.1";
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
    public string DisplayName => "Coverage Audit (log only)";
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
            _catalogProblems = CatalogCheck.Problems(ModEntry.Features.States.Keys);
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
            foreach (var problem in _catalogProblems ?? new List<string>())
            {
                // Logged only: tests/SpireQoL.Catalog.Tests runs the same check before every release (decision #46).
                ModEntry.Log.Warn($"[coverage] settings: {problem}");
            }
        }
        // Tab kinds shown in play that the catalog doesn't describe (the menu lists them under "Other"); looked at when one is added.
        if (_ctx.Settings.Tabs.Count != _tabsLookedAt)
        {
            _tabsLookedAt = _ctx.Settings.Tabs.Count;
            foreach (var key in _ctx.Settings.Tabs.Keys)
            {
                if (key.EndsWith(":*", StringComparison.Ordinal) || SettingsCatalog.IsKnownTabKey(key) || !_unknownTabs.Add(key)) continue;
                Unexpected(key, "tab-not-in-catalog", "a tab kind was shown that the settings catalog doesn't describe.");
            }
        }
        var me = Sts2State.Me;
        var combat = Sts2State.Combat;
        if (me == null || combat == null) return;
        // What's in the fight changes only with the board (the probe, decision #130): looked at again then, by index (a dev review: this
        // rebuilt the game's creature list every frame).
        var probe = BoardProbe.Hash(me, combat);
        if (probe == _lookedAt) return;
        _lookedAt = probe;
        var relics = me.Relics;
        for (var i = 0; i < relics.Count; i++) Check(relics[i]);
        CheckAll(combat.Allies);
        CheckAll(combat.Enemies);
        var hand = Sts2State.Hand(me);
        for (var i = 0; i < hand.Count; i++)
        {
            Check(hand[i]);
            if (hand[i].Enchantment != null) Check(hand[i].Enchantment!);
            if (hand[i].Affliction != null) Check(hand[i].Affliction!);
        }
        var orbs = me.PlayerCombatState?.OrbQueue?.Orbs;
        if (orbs != null) for (var i = 0; i < orbs.Count; i++) Check(orbs[i]);
    }

    private int _tabsLookedAt = -1, _lookedAt;

    private void CheckAll(IReadOnlyList<MegaCrit.Sts2.Core.Entities.Creatures.Creature> creatures)
    {
        for (var i = 0; i < creatures.Count; i++)
        {
            var powers = creatures[i].Powers;
            for (var j = 0; j < powers.Count; j++) Check(powers[j]);
            if (creatures[i].Monster is { } monster) Check(monster);
        }
    }

    private void Check(AbstractModel model)
    {
        var type = model.GetType();
        if (!_checked.Add(type)) return;
        try
        {
            var log = ModEntry.Log;
            if (CoverageLedger.ByType.TryGetValue(type, out var entry))
            {
                switch (entry.Status)
                {
                    case CoverageLedger.Status.Gap:
                        log.Detail($"[coverage] known gap in this fight: {type.Name} ({entry.Note})");
                        break;
                    case CoverageLedger.Status.Unreviewed or CoverageLedger.Status.New or CoverageLedger.Status.Changed:
                        Unexpected(type.Name, entry.Status.ToString().ToLowerInvariant(), $"{entry.Note}. Combat hooks: {entry.Hooks}.");
                        break;
                }
            }
            if (model is RelicModel) CheckRelicTracking(type);
            if (CardCoverage.Issues.TryGetValue(type.Name, out var all))
            {
                // Issues about teammates only count in a fight with other players.
                var withTeammates = Sts2State.Combat?.Players.Count > 1;
                var issues = all.Where(issue => withTeammates || !issue.StartsWith(CardCoverage.TeammatesOnly, StringComparison.Ordinal)).ToList();
                if (issues.Count > 0)
                    Unexpected(type.Name, "card-code", $"its code does what the mod doesn't count: {string.Join("; ", issues)}.");
            }
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"[coverage] check failed for {type.Name}: {ex.Message}");
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "coverage-check");
        }
    }

    /// <summary>
    /// Goal 2: a relic that can affect a fight has a tab, a tracker row or a reviewed reason
    /// (RelicTracking). A relic the ledger doesn't list is checked by its hooks (new in a game update).
    /// </summary>
    private static void CheckRelicTracking(Type type)
    {
        CheckRelicChoices(type);
        CheckRelicCards(type);
        CheckRelicRooms(type);
        var name = type.Name;
        var listed = CoverageLedger.RelicFightHooks.Any(relicFightHook => relicFightHook.Type == name);
        if (!listed)
        {
            var outOfCombat = CoverageLedger.OutOfCombatHooks.ToHashSet();
            // Hooks are AbstractModel's public virtuals, as scripts/coverage-ledger.mjs reads them; its protected ones
            // (DeepCloneFields, AfterCloned) are model copying, not hooks (Yummy Cookie only changes its icon there).
            var fight = type.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly)
                .Where(method => method.IsVirtual && !method.IsSpecialName && method.GetBaseDefinition() is { IsPublic: true } otherMethod && otherMethod.DeclaringType != type && otherMethod.DeclaringType?.Name == "AbstractModel"
                            && !outOfCombat.Contains(method.Name))
                .Select(method => method.Name).Distinct().ToList();
            if (fight.Count == 0) return;
            Unexpected(name, "relic-new", $"a relic the tracking review doesn't list (new in this game version?) with fight hooks: {string.Join(", ", fight)}.");
            return;
        }
        if (SettingsCatalog.Sources.Any(sourceEntry => sourceEntry.Type == name && (sourceEntry.Tag != null || sourceEntry.Tracker != null))) return;
        if (SpireQoL.Data.RelicSpecs.For(name) != null) return;
        if (RelicTracking.Reasons.TryGetValue(name, out var why))
        {
            if (why.StartsWith("gap:", StringComparison.Ordinal)) ModEntry.Log.Detail($"[coverage] known tracking gap in this fight: {name} ({why})");
            return;
        }
        Unexpected(name, "relic-untracked", "a relic that can affect fights with no tab, no tracker row and no reviewed reason (RelicTracking).");
    }

    /// <summary>
    /// Goal 3: a relic that changes a choice outside combat (a hook in Diagnostics/ChoiceHooks.txt) has a
    /// Run Reminders rule (Data/ChoiceReminders) or a reviewed reason (RelicChoiceTracking). Read from the
    /// relic's own overrides, so relics new in a game update are caught too.
    /// </summary>
    private static void CheckRelicChoices(Type type)
    {
        var name = type.Name;
        if (SpireQoL.Data.ChoiceReminders.For(name) != null || RelicChoiceTracking.Reasons.ContainsKey(name)) return;
        var choice = CoverageLedger.ChoiceHooks.ToHashSet();
        var hooks = type.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(method => method.IsVirtual && method.GetBaseDefinition().DeclaringType != type && choice.Contains(method.Name))
            .Select(method => method.Name).Distinct().ToList();
        if (hooks.Count == 0) return;
        Unexpected(name, "relic-choice-unreviewed", $"a relic that changes choices outside combat with no reminder rule and no reviewed reason (RelicChoiceTracking): {string.Join(", ", hooks)}.");
    }

    /// <summary>Hooks where a relic changes or boosts certain cards (docs/CARD_ODDS.md): read for a test of the card.</summary>
    internal static readonly string[] CardHooks =
    {
        "TryModifyCardBeingAddedToDeck", "TryModifyCardBeingAddedToDeckLate", "TryModifyCardRewardOptions", "TryModifyCardRewardOptionsLate",
        "ModifyMerchantCardCreationResults", "ModifyDamageAdditive", "ModifyDamageMultiplicative", "ModifyBlockAdditive", "ModifyBlockMultiplicative",
    };

    /// <summary>What a hook reads when it tests a card: its tags, type, upgrade, enchantment, rarity, keywords or cost, or an enchantment's own check.</summary>
    private static readonly HashSet<string> CardTests = new()
    {
        "CardModel.get_Tags", "CardModel.get_Type", "CardModel.get_IsUpgraded", "CardModel.get_IsUpgradable", "CardModel.get_CurrentUpgradeLevel",
        "CardModel.get_Enchantment", "CardModel.get_Rarity", "CardModel.get_Keywords", "CardModel.get_EnergyCost", "EnchantmentModel.CanEnchant",
        "EggRelicHelper.UpgradeValidCards",
    };

    /// <summary>
    /// A relic whose card hooks (<see cref="CardHooks"/>) test a card has a card rule (Data/CardAffinity) or a reviewed reason
    /// (CardAffinity.Reviewed): Run Reminders shows it beside event options only when a card it applies to can come up.
    /// Read from the relic's own overrides' code, so relics new in a game update are caught too.
    /// </summary>
    internal static List<string> CardTestingHooks(Type type)
    {
        var hooks = new List<string>();
        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (!method.IsVirtual || method.GetBaseDefinition().DeclaringType == type || !CardHooks.Contains(method.Name)) continue;
            List<CodeInstruction> code;
            try { code = SpireQoL.Diagnostics.GameCode.Instructions(method); } catch { continue; }
            if (code.Any(codeInstruction => codeInstruction.operand is MethodBase otherMethod && otherMethod.DeclaringType != null
                    && (CardTests.Contains(otherMethod.DeclaringType.Name + "." + otherMethod.Name)
                        || (otherMethod.Name == "CanEnchant" && typeof(EnchantmentModel).IsAssignableFrom(otherMethod.DeclaringType)))))
                hooks.Add(method.Name);
        }
        return hooks;
    }

    /// <summary>
    /// Hooks that change the map or a kind of room by themselves (docs/PATHING.md): overriding one is a room test. The room reading's table
    /// (Data/RoomReadings: the rest site's, the shop's, what a ? can be, a chest, the map's own).
    /// </summary>
    internal static IReadOnlySet<string> MapHooks => SpireQoL.Data.RoomReadings.MapHookNames;

    /// <summary>
    /// Pathing (docs/PATHING.md, decision #65): the hooks of a relic or card that act on a kind of room. A map hook it overrides, or an
    /// override whose code (async bodies and lambdas included) reads a room's type (AbstractRoom.RoomType, an encounter's RoomType,
    /// MapPoint.PointType) or tests a room's class (`room is MerchantRoom`). Each is "Hook: what it reads", for the review in
    /// the room reading (Data/RoomReadings, decision #374), which reads each one. Read from the compiled code apart from the reading, so new ones are caught.
    /// </summary>
    internal static List<string> RoomTestingHooks(Type type)
    {
        var found = new List<string>();
        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (!method.IsVirtual || method.GetBaseDefinition().DeclaringType == type || method.IsSpecialName) continue;
            if (MapHooks.Contains(method.Name)) { found.Add(method.Name + ": map hook"); continue; }
            var reads = new SortedSet<string>();
            foreach (var code in BodiesOf(method, type))
            {
                foreach (var codeInstruction in code)
                {
                    if (codeInstruction.operand is MethodBase otherMethod && otherMethod.DeclaringType != null
                        && ((otherMethod.Name == "get_RoomType" && (typeof(MegaCrit.Sts2.Core.Rooms.AbstractRoom).IsAssignableFrom(otherMethod.DeclaringType) || typeof(EncounterModel).IsAssignableFrom(otherMethod.DeclaringType)))
                            || (otherMethod.Name == "get_PointType" && otherMethod.DeclaringType.Name == "MapPoint")))
                        reads.Add(otherMethod.DeclaringType.Name + "." + otherMethod.Name[4..]);
                    if ((codeInstruction.opcode == System.Reflection.Emit.OpCodes.Isinst || codeInstruction.opcode == System.Reflection.Emit.OpCodes.Castclass) && codeInstruction.operand is Type otherType
                        && typeof(MegaCrit.Sts2.Core.Rooms.AbstractRoom).IsAssignableFrom(otherType) && otherType != typeof(MegaCrit.Sts2.Core.Rooms.AbstractRoom))
                        reads.Add("is " + otherType.Name);
                }
            }
            if (reads.Count > 0) found.Add(method.Name + ": " + string.Join(", ", reads));
        }
        return found;
    }

    /// <summary>A method's code, its async state machine's and the lambdas it points at (one level), as Harmony reads them.</summary>
    private static IEnumerable<List<CodeInstruction>> BodiesOf(MethodInfo method, Type owner)
    {
        var bodies = new List<List<CodeInstruction>>();
        void Add(MethodBase otherMethod) { try { bodies.Add(SpireQoL.Diagnostics.GameCode.Instructions(otherMethod)); } catch { } }
        Add(method);
        if (method.GetCustomAttribute<System.Runtime.CompilerServices.AsyncStateMachineAttribute>()?.StateMachineType is { } type
            && type.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is { } moveNext)
            Add(moveNext);
        foreach (var lambda in bodies.ToList().SelectMany(codeInstructions => codeInstructions).Where(codeInstruction => codeInstruction.opcode == System.Reflection.Emit.OpCodes.Ldftn).Select(codeInstruction => codeInstruction.operand)
                     .OfType<MethodInfo>().Where(otherMethod => otherMethod.DeclaringType == owner || otherMethod.DeclaringType?.DeclaringType == owner).Distinct())
            Add(lambda);
        return bodies;
    }

    /// <summary>
    /// Pathing (decisions #65, #374): every hook of a relic's that acts on a kind of room is read by its room reading (Data/RoomReadings), with nothing
    /// in it unread.
    /// </summary>
    private static void CheckRelicRooms(Type type)
    {
        var name = type.Name;
        var reading = SpireQoL.Data.RoomReadings.For(name);
        var unread = RoomTestingHooks(type).Where(hook => reading == null || !reading.Hooks.Contains(hook[..hook.IndexOf(':')])).ToList();
        var gaps = reading?.Notes.Where(note => note.Contains("? ", StringComparison.Ordinal)).ToList() ?? new List<string>();
        if (unread.Count == 0 && gaps.Count == 0) return;
        Unexpected(name, "relic-room-unreviewed", $"a relic whose hooks act on a kind of room, not read by its room reading (Data/RoomReadings): {string.Join("; ", unread.Concat(gaps))}.");
    }

    private static void CheckRelicCards(Type type)
    {
        var name = type.Name;
        if (SpireQoL.Data.CardAffinity.For(name) != null || SpireQoL.Data.CardAffinity.Reviewed.ContainsKey(name)) return;
        var hooks = CardTestingHooks(type);
        if (hooks.Count == 0) return;
        Unexpected(name, "relic-card-unreviewed", $"a relic whose hooks test a card, with no card rule and no reviewed reason (CardAffinity): {string.Join(", ", hooks)}.");
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
