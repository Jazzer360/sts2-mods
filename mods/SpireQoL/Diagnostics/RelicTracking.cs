namespace SpireQoL.Diagnostics;

/// <summary>
/// Goal 2 (docs/ROADMAP.md): every relic that can affect a fight is tracked, by a tab on the cards it
/// reacts to or a tracker row (SettingsCatalog, RelicSpecs), or has a reviewed reason here written
/// from its hook bodies: "none: why nothing needs showing" or "gap: what should be shown". The
/// relics come from the decompile (CoverageLedger.RelicFightHooks, scripts/coverage-ledger.mjs); a
/// relic with fight hooks and none of these is reported when you have it (CoverageWatch).
/// </summary>
internal static class RelicTracking
{
    public static readonly Dictionary<string, string> Reasons = new()
    {
        // ---- start of combat (effect on screen before your first decision)
        ["Anchor"] = "none: BeforeCombatStart block",
        ["FakeAnchor"] = "none: BeforeCombatStart block",
        ["Byrdpip"] = "none: BeforeCombatStart pet",
        ["DelicateFrond"] = "none: BeforeCombatStart potion",
        ["FakeSneckoEye"] = "none: BeforeCombatStart Confused",
        ["SneckoEye"] = "none: BeforeCombatStart Confused, bigger hand draw",
        ["FurCoat"] = "none: enemies in marked rooms start at 1 HP",
        ["MeatOnTheBone"] = "none: heals after combat when low (its status shows it)",
        ["Pantograph"] = "none: BeforeCombatStart heal in boss fights",
        ["PetrifiedToad"] = "none: BeforeCombatStartLate potion",
        ["PhilosophersStone"] = "none: max energy; Strength to enemies as they enter (intents show it)",
        ["TeaOfDiscourtesy"] = "none: BeforeCombatStart Dazed into your draw pile (its counter shows combats left)",
        ["BoundPhylactery"] = "none: summons at the start of combat and each turn (Osty shows it)",
        ["PhylacteryUnbound"] = "none: summons at the start of combat and each turn (Osty shows it)",
        ["GhostSeed"] = "none: cards entering combat become Ethereal (shown on the card)",

        ["BathRobes"] = "none: BeforeCombatStart full heal in a boss fight (once)",
        ["Byrdine"] = "none: BeforeCombatStart pet",
        ["Wuppy"] = "none: its pet takes the powered attacks aimed at you while it lives (simulator: Incoming Damage leaves them out)",

        // ---- start of your turn (on screen before your decisions that turn)
        ["Akabeko"] = "none: Vigor on turn 1 (the Vigor tab marks the attack that spends it)",
        ["BagOfMarbles"] = "none: Vulnerable to all enemies on turn 1",
        ["Bellows"] = "none: upgrades your hand from turn 2",
        ["BigHat"] = "none: cards into your hand on turn 1",
        ["BloodVial"] = "none: heals on turn 1",
        ["FakeBloodVial"] = "none: heals on turn 1",
        ["BoneTea"] = "none: upgrades a card from turn 2 for its combats (its counter shows combats left)",
        ["BoomingConch"] = "none: draw and energy on turn 1 of elite fights",
        ["Bread"] = "none: max energy, less energy on turn 1",
        ["Brimstone"] = "none: Strength to you and enemies each turn (intents and powers show it)",
        ["ChoicesParadox"] = "none: a card choice on turn 1",
        ["CrackedCore"] = "none: channels Lightning on turn 1",
        ["Crossbow"] = "none: cards into your hand at the start of each turn",
        ["DiamondDiadem"] = "none: block and Blur on turn 1",
        ["DivineDestiny"] = "none: stars on turn 1",
        ["FencingManual"] = "none: forges on turn 1",
        ["FestivePopper"] = "none: damages all enemies on turn 1",
        ["GamblingChip"] = "none: discard and redraw on turn 1",
        ["HistoryCourse"] = "none: replays last turn's attack at the start of your turn",
        ["InfusedCore"] = "none: channels Lightning on turn 1; Lightning's values are shown on the orbs",
        ["Lantern"] = "none: energy on turn 1",
        ["MercuryHourglass"] = "none: damages all enemies at the start of each turn",
        ["MrStruggles"] = "none: damages all enemies at the start of each turn",
        ["OrangeDough"] = "none: cards into your hand on turn 1",
        ["PowerCell"] = "none: cards into your hand on turn 1",
        ["RedMask"] = "none: Weak to all enemies on turn 1",
        ["RoyalPoison"] = "none: damages you on turn 1",
        ["RunicCapacitor"] = "none: orb slots on turn 1",
        ["Sai"] = "none: block at the start of each turn",
        ["SealOfGold"] = "none: energy for gold at the start of each turn",
        ["SymbioticVirus"] = "none: channels Dark on turn 1",
        ["ToastyMittens"] = "none: exhaust a card for Strength at the start of each turn",
        ["TwistedFunnel"] = "none: poison to all enemies on turn 1",
        ["VeryHotCocoa"] = "none: energy on turn 1",
        ["VexingPuzzlebox"] = "none: a card into your hand on turn 1",
        ["WhisperingEarring"] = "none: max energy; plays your turn-1 hand for you",
        ["VenerableTeaSet"] = "none: energy at the start of the next combat",
        ["FakeVenerableTeaSet"] = "none: energy at the start of the next combat",

        ["BathTowel"] = "none: heals on turn 1",
        ["BizarreLamp"] = "none: Joy and Sadness into your hand on turn 1",
        ["ExerciseMat"] = "none: Stretch on turn 1 (its bonus is in the block the game shows)",
        ["Ocarina"] = "none: a melody card into your hand on turn 1",
        ["TormentousCollar"] = "none: Weak to all enemies on turn 1 of elite fights",

        // ---- hand draw and energy (the hand and energy you see)
        ["BagOfPreparation"] = "none: bigger turn-1 hand",
        ["BigMushroom"] = "none: smaller hand draw",
        ["BlessedAntler"] = "none: max energy; cards into your draw pile on turn 1",
        ["BloodSoakedRose"] = "none: max energy",
        ["Ectoplasm"] = "none: max energy",
        ["FuneraryMask"] = "none: a card into your draw pile on turn 1",
        ["JeweledMask"] = "none: a card into your hand on turn 1",
        ["NinjaScroll"] = "none: cards into your hand on turn 1",
        ["PaelsBlood"] = "none: bigger hand draw",
        ["PrismaticGem"] = "none: max energy",
        ["PumpkinCandle"] = "none: max energy while it has charges (its counter shows them)",
        ["RadiantPearl"] = "none: cards into your hand on turn 1",
        ["RingOfTheDrake"] = "none: bigger hand draw on early turns",
        ["RingOfTheSnake"] = "none: bigger turn-1 hand",
        ["Sozu"] = "none: max energy",
        ["SpikedGauntlets"] = "none: max energy; costs are shown on the cards",
        ["Toolbox"] = "none: a card choice on turn 1",
        ["IceCream"] = "none: unspent energy carries over (your energy shows it)",
        ["PaelsFlesh"] = "gap: extra energy from turn 3 (its counter shows the turn); a turn-timer row would show when it starts",

        // ---- in the numbers the game shows (card damage and block, orb values, power amounts)
        ["FakeStrikeDummy"] = "none: Strike damage on the cards",
        ["StrikeDummy"] = "none: Strike damage on the cards",
        ["MiniatureCannon"] = "none: damage on the cards",
        ["MysticLighter"] = "none: damage on the cards",
        ["UndyingSigil"] = "none: damage multiplier in the numbers shown",
        ["VitruvianMinion"] = "none: damage and block multipliers in the numbers shown",
        ["GoldPlatedCables"] = "none: the front orb's extra passive (Incoming Damage counts it)",
        ["RuinedHelmet"] = "none: doubles your first Strength gain (the power shows it)",
        ["SneckoSkull"] = "none: +1 poison per application (poison shown on enemies; the simulator counts it)",

        ["Thumbscrew"] = "none: a hit more on your multi-hit attacks (in the card's hit count and the mod's)",
        ["Storyteller"] = "none: Aim at the start of combat and each end of turn (its power shows it)",
        ["Fertilizer"] = "gap: Strength when you create a Sadness: no tab",
        ["MightyWings"] = "gap: 2 Strength when one of your hits deals 30 or more: no tab",
        ["WhittlingKnife"] = "gap: Aim when you shuffle: no tab",

        // ---- your end of turn and the enemy turn (shown by Incoming Damage)
        ["CloakClasp"] = "none: end-of-turn block (Incoming Damage)",
        ["Orichalcum"] = "none: end-of-turn block (Incoming Damage)",
        ["FakeOrichalcum"] = "none: end-of-turn block (Incoming Damage)",
        ["ScreamingFlagon"] = "none: end-of-turn damage with an empty hand (Incoming Damage)",
        ["TungstenRod"] = "none: HP loss reduced (Incoming Damage)",
        ["BeatingRemnant"] = "none: HP loss capped per turn (Incoming Damage)",
        ["TheBoot"] = "none: your attack hits below its minimum lose the enemy its minimum (the simulator counts it: lethal, wake)",
        ["LunarPastry"] = "gap: stars at the end of your turn (Black Hole's end-of-turn damage isn't simulated yet)",
        ["SturdyClamp"] = "none: keeps up to 10 block into your next turn",
        ["RingingTriangle"] = "none: keeps your hand on turn 1 (v0.113.0: Retain Hand from AfterRoomEntered, a power)",
        ["RunicPyramid"] = "none: keeps your hand",
        ["Bookmark"] = "none: a random retained card costs 1 less until played (shown on the card next turn)",

        ["ObsidianVice"] = "none: end of turn, damage per card in hand to a random enemy (simulator: lethal, Death Skull)",

        // ---- other
        ["Fiddle"] = "none: blocks your draws during your turn (the Shuffle Warning and draw counts account for it)",
        ["LizardTail"] = "none: saves you from dying once (simulator; Incoming Damage shows it when it will)",
        ["LavaLamp"] = "none: notes damage taken for its end-of-combat upgrade",
        ["BookRepairKnife"] = "none: heals when enemies die to Doom",
        ["ReptileTrinket"] = "gap: Strength when you use a potion (potions have no tabs yet)",
        ["BeltBuckle"] = "gap: reacts to having no potions (potions have no tabs yet)",
    };
}
