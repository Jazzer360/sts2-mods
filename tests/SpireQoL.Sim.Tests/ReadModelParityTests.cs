using System.Text;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Each model read from the game's code (decision #195) against the hand-written model it replaced (HandWrittenModels.cs, kept as it was): every
/// hook either answers, called the same way on the same boards (the owner you, a teammate or an enemy; plays of every type, cost and owner;
/// every card event for you and for another; powers landing of each kind from each applier; damage and block of every dealer, target, kind and
/// source), must leave the same branches (each one's state key, weight and play log) or return the same number. A difference the read model
/// makes on purpose (it follows a line of the game's code the hand-written one left out) is reviewed below with that line, and nothing else is.
/// </summary>
public class ReadModelParityTests
{
    private static readonly Dictionary<string, SimModel> HandWritten = typeof(ReadModelParityTests).Assembly.GetTypes()
        .Where(type => type.Namespace == typeof(HandWritten.StrengthPower).Namespace && !type.IsAbstract && typeof(SimModel).IsAssignableFrom(type))
        .Select(type => (SimModel)Activator.CreateInstance(type)!).ToDictionary(model => model.Name, StringComparer.Ordinal);

    /// <summary>
    /// Crossings reviewed (a read model's tab is crossed by whose side it helps: decision #198): the model, and why its crossing differs from the
    /// hand-written one's on the owners the game gives it. Held to differing (EveryReviewedDifferenceIsOne).
    /// </summary>
    private static readonly Dictionary<string, string> ReviewedCrossing = new(StringComparer.Ordinal)
    {
        ["SneakyPower"] = "its block on another player's Attack helps its owner, a player (the owner board): your side, a plain tab, where the hand-written model's was crossed on the enemy it wrongly acted on (a fix)",
    };

    /// <summary>
    /// Classes given a settings row since their conversion (decision #244, the owner's call): their tab is new, judged on the turn's outcome where the
    /// hand-written model had none (BonusTabRowTests holds it to their code); what they do is compared as before.
    /// </summary>
    private static readonly Dictionary<string, string> NewTabs = new(StringComparer.Ordinal)
    {
        ["Orichalcum"] = "its block as your turn ends with none: a crossed tab on a play that gives you block",
        ["FakeOrichalcum"] = "as Orichalcum's",
        ["CloakClasp"] = "its block per card in hand as your turn ends: a crossed tab on a play that leaves fewer (off by default)",
        ["ObsidianVice"] = "its damage per card in hand as your turn ends: a crossed tab on a play that leaves fewer (off by default)",
        ["ScreamingFlagon"] = "its damage as your turn ends with an empty hand: a plain tab on a play that empties it",
        ["HailstormPower"] = "its damage as your turn ends with a Frost orb: plain on a play that channels your first, crossed on one that evokes your last",
    };

    /// <summary>
    /// Differences reviewed: "Model.Hook" â†’ the game's line the read model follows and the hand-written one didn't, and why it changes no
    /// prediction (or which, a fix).
    /// </summary>
    private static readonly Dictionary<string, string> Reviewed = new(StringComparer.Ordinal)
    {
        ["DoubleDamagePower.AfterSideTurnEnd"] = "PowerCmd.Decrement(this) when its owner takes part: it goes down as your turn ends. After it no card of yours plays this turn (the auto-plays come before), so no damage it doubles is left: no prediction changes",
        ["RagePower.AfterSideTurnEnd"] = "PowerCmd.Remove(this) when its owner takes part: it goes as your turn ends. No card of yours plays after it (the auto-plays come before): no prediction changes",
        ["GravityPower.AfterSideTurnEnd"] = "PowerCmd.Remove(this) when its owner takes part: it goes as your turn ends. No card of yours plays after it (the auto-plays come before): no prediction changes",
        ["BurstPower.AfterSideTurnEnd"] = "PowerCmd.Remove(this) when its owner takes part: it goes as your turn ends. No card of yours plays after it (the auto-plays come before): no prediction changes",
        ["DuplicationPower.AfterSideTurnEnd"] = "PowerCmd.Remove(this) when its owner takes part: as Burst's",
        ["OneTwoPunchPower.AfterSideTurnEnd"] = "PowerCmd.Remove(this) when its owner takes part: as Burst's",
        ["KiVersePower.AfterSideTurnEnd"] = "PowerCmd.Remove(this) when its owner takes part: as Burst's",
        ["CorrosiveWavePower.AfterSideTurnEnd"] = "PowerCmd.Remove(this) when its owner takes part: it goes as your turn ends. You draw nothing after it this turn: no prediction changes",
        ["SleightOfFleshPower.AfterPowerApplied"] = "!(power is ITemporaryPower): a temporary power landing (Dark Shackles) doesn't set it off, the Strength its BeforeApplied applies does. The simulator lands a temporary Strength power as that Strength (SimAdapter.PowersFrom), which both count: no prediction changes",
        ["ShroudPower.AfterPowerApplied"] = "applier == base.Owner && power is DoomPower, with no test of the target: Doom you put on a player sets it off too (the hand-written model wanted an enemy). Nothing of yours puts Doom on a player today",
        ["ConstrictPower.AfterDeath"] = "!wasRemovalPrevented && creature == base.Applier: PowerCmd.Remove(this), it goes as its applier dies, where the hand-written model kept it and dealt nothing as the turn ended with its applier dead. Nothing is dealt either way (the scenario's turn end shows it): no prediction changes",
        ["SneakyPower.AfterCardPlayed"] = "cardPlay.Card.Owner.Creature != base.Owner && cardPlay.Card.Type == Attack: its owner gains block on another player's Attack. Only players have it (the owner board: the multiplayer card Sneaky puts it on its player), where the hand-written model acted only on an enemy, so it never followed the game's Sneaky; the read model does (a fix since round three, multiplayer only: the single-player golden can't reach it)",
        ["CrabRagePower.AfterDeath"] = "CreatureCmd.GainBlock(base.Owner, Block): an enemy's block from a read model goes through the block math and is its reaction (SimEngine.EnemyBlock: logged, SimEnemy.ReactedWithBlock), where the hand-written model added it raw; with its Unpowered props nothing changes the 99, and nothing shown reads that flag: no prediction changes. Its Strength and its block are each logged, as each is a command of its code",
        ["GremlinHorn.AfterDeath"] = "Flash(), then PlayerCmd.GainEnergy and CardPileCmd.Draw (queued until the hit is over, as the hand-written model did): the read model logs its firing (the game's Flash). Its tab is judged on the outcome (a kill in every branch), not on the log: what's shown doesn't change",
        ["IterationPower.AfterCardEvent"] = "Flash(), then CardPileCmd.Draw: the read model logs its firing (the game's Flash) where the hand-written model logged none. It has no tab (no Proc Tags row): what's shown doesn't change; the play check now holds its flash to the game's",
        ["OblivionPower.BeforeCardPlayed"] = "with AfterCardPlayed (the play's two hooks in turn): PowerCmd.Apply<DoomPower>(base.Owner, value, base.Applier): each card its applier plays puts the Doom it had as the card started (its own data's) on its owner, as its applier's, where the hand-written model only logged its tab. Doom acts as its owner's side's turn ends, which no simulation reaches; Shroud's block on Doom you apply (applier == its owner) now follows it (a fix)",
        ["OblivionPower.AfterCardPlayed"] = "PowerCmd.Apply<DoomPower>(base.Owner, value, base.Applier): as BeforeCardPlayed's review",
        ["OblivionPower.AfterSideTurnEnd"] = "side == CombatSide.Player: PowerCmd.Remove(this), it goes as your turn ends. No card of yours plays after it: no prediction changes",
        ["ReaperFormPower.AfterDamageGiven"] = "dealer == base.Owner (or its pet's PetOwner) && props.IsPoweredAttack() && result.TotalDamage > 0: PowerCmd.Apply<DoomPower>(target, TotalDamage Ã— Amount, base.Owner): the Doom lands on the hit's target (the hand-written model only logged its tab), whoever it is, and on a fully blocked hit too (TotalDamage counts block, where the hand-written model wanted HP through): its tab shows on such a hit, as the game acts on it (a fix). Doom acts as its owner's side's turn ends, which no simulation reaches; Shroud's block on the Doom you apply now follows it",
        ["PagestormPower.AfterCardEvent"] = "card.Keywords.Contains(Ethereal): Flash(), then CardPileCmd.Draw: the read model logs its firing (its acting step) where the hand-written model logged none. It has no tab (no Proc Tags row): what's shown doesn't change; the play check now holds its flash to the game's",
        ["LunarPastry.AfterSideTurnEnd"] = "PlayerCmd.GainStars as your turn ends: the read model logs its firing, as every read model's acting step does, where the hand-written model logged only the stars. It has no tab, and the checks read from its code that it never flashes (decision #181): nothing shown changes",
        ["PaleBlueDotPower.AfterCardPlayed"] = "PowerCmd.Apply<DrawCardsNextTurnPower>(base.Owner, base.Amount): the read model puts Draw Cards Next Turn on its owner where the hand-written model only fired. It acts as its owner's next turn starts, outside a simulation: no prediction changes",
        ["VulnerablePower.ModifyDamageMultiplicative"] = "dealer.Player?.GetRelic<PaperPhrog>(), and PaperPhrog's and CrueltyPower's own `target == base.Owner` returns: Osty's hits get no Paper Phrog (a pet's creature has no Player; the hand-written model gave him his player's), a fix where Osty hits a Vulnerable enemy with Paper Phrog; and a creature's own Phrog and Cruelty don't raise a powered attack on itself (none happens today)",
        ["MonologuePower.AfterSideTurnEnd"] = "PowerCmd.Remove(this), then Strength -StrengthApplied (what its copy gave this turn, taken back) when its owner takes part: it goes as your turn ends. No card of yours plays after it this turn (the auto-plays come before), so no damage reads that Strength: no prediction changes. Its Apply logs a firing there (the read models' acting steps do), outside any play's log, so no tab reads it",
        ["SlowPower.AfterSideTurnStart"] = "participants.Contains(base.Owner): DynamicVars[\"SlowAmount\"].BaseValue = 0 as its owner's side's turn starts (the enemies'), where the hand-written model left the count to the adapter's next reading. No card of yours plays in the enemies' turn, so no attack on it reads the count after: no prediction changes",
        ["BufferPower.AfterModifyingHpLost"] = "its code answers Hook.AfterModifyingHpLostAfterOsty only (PowerCmd.Decrement), its term being the after-Osty Late pass's: the engine tells it after that pass, never before Osty, so the board's call before Osty takes no stack, where the hand-written model took one on either call: no prediction changes",
        ["BoneFlute.AfterAttack"] = "command.Attacker?.Monster is Osty && command.Attacker.PetOwner?.Creature == base.Owner.Creature: CreatureCmd.GainBlock, with no test of Osty alive: every attack Osty made gives the block, one he died during included (a hit back: Thorns), where the hand-written model wanted him above 0 HP after it (a fix)",
        ["MonarchsGazePower.AfterDamageGiven"] = "dealer == base.Owner && props.IsPoweredAttack(): PowerCmd.Apply<MonarchsGazeStrengthDownPower>(target, ...), with no test of the target: a powered attack of its owner's on a player takes their Strength too, where the hand-written model wanted an enemy. No card of a player's makes a powered attack on a player: no prediction changes",
        ["SelfFormingClay.AfterDamageReceived"] = "target == base.Owner && result.UnblockedDamage > 0: PowerCmd.Apply<SelfFormingClayPower>(base.Owner, BlockNextTurn), where the hand-written model only had its tab. The power gives its block as its owner's next turn starts, past every simulation: no prediction changes. Its tab is the reaction's rule (decision #239), judged as the hand-written one's was",
        ["DemonTongue.AfterDamageReceived"] = "_triggeredThisTurn = true; Flash(); CreatureCmd.Heal(base.Owner, UnblockedDamage): the same heal, its once-a-turn flag its own field; the read model logs its Flash (the flash log, decision #199) where the hand-written one logged the heal as a firing. A heal is no tab's amount, and its tab is the reaction's rule (decision #239), which reads either log: what's shown doesn't change",
        ["SlipperyPower.AfterDamageReceived"] = "target == base.Owner && result.UnblockedDamage >= 1: Flash(); PowerCmd.Decrement(this): the same charge spent; the read model logs its Flash (the flash log, decision #199) where the hand-written one logged a firing and set its enemy's SlipperyTriggered. Its tab is the reaction's rule (decision #239), which reads either log, over the whole turn as the flag was: what's shown doesn't change",
        ["ArtOfWar.BeforeCardPlayed"] = "its code has no BeforeCardPlayed: the hand-written model logged its crossed tab's firing on the turn's first Attack there; the read model logs it where its payout (AfterEnergyReset's energy on !AnyAttacksPlayedLastTurn, which AfterSideTurnEnd sets from AnyAttacksPlayedThisTurn) goes off: AfterCardPlayed's store (decision #239). The play's log is the same over the play (BonusTabParityTests); this board's turn is 0, before any the game numbers, where the payout is never on",
        ["ArtOfWar.AfterCardPlayed"] = "AnyAttacksPlayedThisTurn = true on its owner's Attack while !AnyAttacksPlayedLastTurn: the store that gives its payout up logs the crossed tab's firing (decision #239), as ArtOfWar.BeforeCardPlayed's review says",
        ["RippleBasin.BeforeCardPlayed"] = "its code has no BeforeCardPlayed: the hand-written model logged its crossed tab's firing on the turn's first Attack there; the read model's is logged where the fight's log counts the play that turns its payout off (BeforeSideTurnEnd's block on no Attack of its owner's this turn: SimEngine.Count, decision #239). The play's log is the same over the play (BonusTabParityTests)",
        ["ThornsPower.BeforeDamageReceived"] = "target == base.Owner && dealer != null && props.IsPoweredAttack(): CreatureCmd.Damage(dealer, base.Amount, Unpowered | SkipHurtAnim, base.Owner), whoever owns it. On a player (your Thorns, a teammate's) it hits back the enemy whose attack is landing: the engine's enemy turn did that by hand (TakesHit), and the hand-written model left players' Thorns to it, so the two differ there by design (the enemy turn's tests and the single-player golden hold the read model to it). On an enemy, the only difference is a powered attack of its own on itself (dealer == target == its owner), which it now hits back as its code says: no card or move makes one",
        ["RupturePower.AfterDamageReceived"] = "target == base.Owner && result.UnblockedDamage > 0 && CurrentSide == base.Owner.Side: PowerCmd.Apply<StrengthPower>(base.Owner, base.Amount) at once, or, for HP the card in play costs (its cardSource the card BeforeCardPlayed added), playedCards[cardSource] += base.Amount, given as that play ends (AfterCardPlayed); the hand-written model only had its tab. The Strength now lands (a fix): an attack's later hits after a Thorns hit back on you, and the plays after a card's own HP cost, count it. Its tab is the reaction's rule (decision #239), judged as the hand-written one's was",
        ["RavenousPower.AfterDeath"] = "target != base.Owner && target.Side == base.Owner.Side && !base.Owner.IsDead: CreatureCmd.Stun(base.Owner), then PowerCmd.Apply<StrengthPower>(base.Owner, base.Amount, base.Owner): its Strength lands on its stunned owner, where the hand-written model only stunned it. A stunned enemy makes no move this turn (its stunned move does nothing: read from its code), so nothing it deals changes: no prediction changes",
        ["HardToKillPower.ModifyHpLostBeforeOstyLate"] = "v0.114.0 moved Hard to Kill from ModifyDamageCap (the damage before block, capped at its amount) to ModifyHpLostBeforeOstyLate (the HP a hit takes past block: Math.Min(amount, Amount), 0 left alone) and its flash to AfterModifyingHpLostBeforeOsty: the read model is the new code; the hand-written one, frozen at v0.113.1, capped the damage",
        ["HardToKillPower.ModifyDamageCap"] = "v0.114.0: Hard to Kill no longer caps the damage (its cap is ModifyHpLostBeforeOstyLate now, reviewed there): the read model leaves it whole",
        ["HardToKillPower.AfterModifyingDamageAmount"] = "v0.114.0: its flash moved to AfterModifyingHpLostBeforeOsty with its cap (reviewed at ModifyHpLostBeforeOstyLate)",
        ["QuietusPower.BeforeAttack"] = "the second attack's hit asked of the model after the first attack spent the power (AfterAttack's PowerCmd.Remove): the hand-written model answered x0 for a power that's gone, the read model x1; the engine never asks it there (a power that's gone is no listener), so no damage changes",
        ["QuietusPower.AfterAttack"] = "the same two attacks as QuietusPower.BeforeAttack's review (the scenario runs both hooks): the second attack's hit asked of a power that's gone, which the engine never asks",
        ["QuietusPower.AfterSideTurnEnd"] = "participants.Contains(base.Owner): PowerCmd.Remove(this), it goes as your turn ends. No card of yours plays after it (the auto-plays come before): no prediction changes",
        ["TheGambitPower.AfterDamageReceived"] = "target == base.Owner && props.IsPoweredAttack() && result.UnblockedDamage > 0: PowerCmd.Remove(this); Flash(); CreatureCmd.Kill(base.Owner): its owner is a player (the owner board: The Gambit card applies it to its own player), who loses all their HP and dies unless a Fairy in a Bottle or Lizard Tail holds it back (Kill's force is false), where the hand-written model acted only on an enemy: an enemy's attack through your block with The Gambit on you now shows your death (a fix). Its enemy tab is held as recorded (GambitTabParityTests)",
        ["PersonalHivePower.AfterDamageReceived"] = "target == base.Owner && dealer != null && props.IsPoweredAttack(): `if (dealer.Monster is Osty) dealer = dealer.PetOwner.Creature`, then base.Amount of CardPileCmd.AddGeneratedCardToCombat(CreateCard<Dazed>(dealer.Player), PileType.Draw, null, Random): your Osty's powered hit makes them for you, where the hand-written model wanted a player dealer (a fix); each is told to Hook.AfterCardGeneratedForCombat with no creator, which no created-card hook hears. An enemy's own attack on itself (the board's dealer 0) has no player to make them for (dealer.Player is null): the read model marks that simulation unknown; no enemy's attack hits an enemy",
        ["SicEmPower.AfterDamageGiven"] = "dealer?.Monster is Osty osty && osty.Creature.PetOwner != null && base.Applier != null && osty.Creature.PetOwner.Creature == base.Applier && target == base.Owner: OstyCmd.Summon(choiceContext, dealer.PetOwner, base.Amount, this): the summon happens (Osty's max HP and HP up by its amount: SimEngine.Summon), where the hand-written model only logged its tab (a fix, its tab the same). Its code asks nothing of the damage, so a hit fully blocked summons too, as the hand-written model's Total > 0 allowed",
        ["BiiigHug.AfterCardEvent"] = "AfterShuffle: CardPileCmd.AddGeneratedCardToCombat(CreateCard<Soot>(base.Owner), PileType.Draw, base.Owner, Random): the Soot is a card you made, so Hook.AfterCardGeneratedForCombat tells your created-card hooks of it (a Status: Pillar of Creation's block, Smokestack's damage, Arsenal's Strength, Regalite, Supermassive's count), where the hand-written model only put it in the pile (a fix)",
        ["ViciousPower.AfterPowerApplied"] = "amount > 0 && applier == base.Owner && power is VulnerablePower, with no test of the target: Vulnerable you put on a player sets it off too (the hand-written model wanted an enemy). Nothing of yours puts Vulnerable on a player today",
    };

    /// <summary>
    /// The relics whose hand-written model's amount the adapter read from one of their values (SimReaders.RelicAmountVars), by that value's key:
    /// the board gives the amount as the adapter did, so the two read the same number.
    /// </summary>
    private static readonly Dictionary<string, string> RelicAmountVars = new(StringComparer.Ordinal) {
        [RelicId.CharonsAshes.Name] = "Damage", [RelicId.ForgottenSoul.Name] = "Damage", [RelicId.Orichalcum.Name] = "Block", [RelicId.FakeOrichalcum.Name] = "Block",
        [RelicId.ScreamingFlagon.Name] = "Damage", [RelicId.CloakClasp.Name] = "Block", [RelicId.ObsidianVice.Name] = "Damage", [RelicId.LunarPastry.Name] = "Stars", [RelicId.StoneCalendar.Name] = "Damage",
        [RelicId.MightyWings.Name] = "StrengthPower", [RelicId.TungstenRod.Name] = "HpLossReduction", [RelicId.BeatingRemnant.Name] = "MaxHpLoss", [RelicId.TheBoot.Name] = "DamageMinimum", [RelicId.RippleBasin.Name] = "Block", [RelicId.ParryingShield.Name] = "Damage",
    };

    /// <summary>
    /// The relics whose hand-written model's Data the adapter read from one of their values (SimReaders.Relics), by that value's key: the board
    /// gives the Data as the adapter did (Mighty Wings' DamageThreshold).
    /// </summary>
    private static readonly Dictionary<string, string> RelicDataVars = new(StringComparer.Ordinal) { [RelicId.MightyWings.Name] = "DamageThreshold", [RelicId.ParryingShield.Name] = "Block" };

    /// <summary>
    /// The powers whose hand-written model's Data the adapter read from one of their values (SimReaders.Powers), by that value's key: the board
    /// gives the Data as the adapter did (Hailstorm's FrostOrbs, the Frost orbs it needs).
    /// </summary>
    private static readonly Dictionary<string, string> PowerDataVars = new(StringComparer.Ordinal) { [PowerId.Hailstorm.Name] = "FrostOrbs", [PowerId.Flutter.Name] = "DamageDecrease" };

    /// <summary>
    /// The powers whose hand-written model's amount the adapter read from one of their values, by that value's key: the board gives the amount
    /// as the adapter did (Crab Rage's Strength).
    /// </summary>
    private static readonly Dictionary<string, string> PowerAmountVars = new(StringComparer.Ordinal) { [PowerId.CrabRage.Name] = "StrengthPower" };

    /// <summary>
    /// The relics whose hand-written model the adapter told whether this was its turn (Stone Calendar's Data: turns until), by the value naming
    /// that turn: the board's turn is it, as the adapter's Data 0 was.
    /// </summary>
    private static readonly Dictionary<string, string> TurnVars = new(StringComparer.Ordinal) { [RelicId.StoneCalendar.Name] = "DamageTurn" };

    /// <summary>
    /// "Model.Var": values the hand-written model wrote as a constant (Colossus' 0.5) that the read model reads from its values: the board gives
    /// the game's value (its canonical one), so the two read the same number.
    /// </summary>
    private static readonly HashSet<string> HandWrittenConstants = new(StringComparer.Ordinal) { $"{PowerId.Colossus.Name}.DamageDecrease", $"{PowerId.ConductorsBaton.Name}.DamageDecrease", $"{PowerId.Vulnerable.Name}.DamageIncrease", $"{PowerId.Weak.Name}.DamageDecrease" };

    /// <summary>
    /// The counters a hand-written model kept as its power's Data (the adapter read each copy's count there: SimReaders.Powers, Counters) and the
    /// fields the read model keeps the same count in, as the adapter reads them from the game's object: 3 left either way.
    /// </summary>
    private static readonly Dictionary<string, (int Data, (string Field, int Value)[] Fields)> CounterFields = new(StringComparer.Ordinal)
    {
        // AutomationPower: Data is DisplayAmount, its data's cardsLeft.
        [PowerId.Automation.Name] = (3, new[] { ("Data.cardsLeft", 3) }),
        // CacophonyPower: DisplayAmount is its Cards value, which it counts down.
        [PowerId.Cacophony.Name] = (3, new[] { (HookField.VarPrefix + "Cards", 3) }),
        // PanachePower: its CardsLeft value; counting once alreadyApplied (a Data of 0 was "just applied").
        [PowerId.Panache.Name] = (3, new[] { (HookField.VarPrefix + "CardsLeft", 3), ("Data.alreadyApplied", 1) }),
        // SlowPower: the cards played this turn (its SlowAmount value, which it counts).
        [PowerId.Slow.Name] = (1, new[] { (HookField.VarPrefix + "SlowAmount", 1) }),
        // OrbitPower: DisplayAmount is 4 - energySpent % 4.
        [PowerId.Orbit.Name] = (3, new[] { ("Data.energySpent", 1), ("Data.triggerCount", 0) }),
    };

    /// <summary>
    /// The counts a hand-written relic model kept as its Data (the adapter read it there: SimReaders.Relics) and the field the read model keeps the
    /// same count in, as the adapter reads it from the game's object: Beating Remnant's HP lost this turn.
    /// </summary>
    private static readonly Dictionary<string, (int Data, string Field)> RelicCounterFields = new(StringComparer.Ordinal)
    {
        [RelicId.BeatingRemnant.Name] = (6, "DamageReceivedThisTurn"),
        // Joss Paper: the Ethereal exhausts its turn end counted (its EtherealCount; the hand-written model's Data).
        [RelicId.JossPaper.Name] = (1, "EtherealCount"),
    };

    /// <summary>
    /// The counts a hand-written relic model kept as its amount (the adapter read it there: SimReaders.Relics) and the field the read model keeps the
    /// same count in: Joss Paper's exhausts toward its next draw (CardsExhausted), one short of the board's ExhaustAmount (6).
    /// </summary>
    private static readonly Dictionary<string, (int Amount, string Field)> RelicAmountFields = new(StringComparer.Ordinal)
    {
        [RelicId.JossPaper.Name] = (5, "CardsExhausted"),
    };

    /// <summary>
    /// The powers whose hand-written model kept the attack bound to it in its Data (the attack's id and the amount it started with) where the read
    /// model keeps them in its own data's fields (decision #228): two ways of keeping one thing, left out of the key; the hits compare what each does with it.
    /// </summary>
    private static readonly HashSet<string> KeptAttackData = new(StringComparer.Ordinal) { PowerId.Vigor.Name, PowerId.Gigantification.Name, PowerId.Aim.Name, PowerId.Quietus.Name };

    /// <summary>
    /// The powers whose hand-written model kept a once-a-turn flag in its Data (PowerUse.UsedThisTurn) where the read model keeps its own data's
    /// field (Yah's triggeredThisTurn): two ways of keeping one thing, left out of the key; what each does with it is compared (a second arrow
    /// knocked loose gives nothing).
    /// </summary>
    private static readonly HashSet<string> FlagInData = new(StringComparer.Ordinal) { PowerId.Yah.Name };

    public static TheoryData<string> Models() => new(HandWritten.Keys.OrderBy(name => name, StringComparer.Ordinal));

    [Fact]
    public void EveryReadModelReplacedAHandWrittenOne()
    {
        var read = ReadModelsFile.Specs().Select(spec => spec.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(HandWritten.Keys.OrderBy(name => name, StringComparer.Ordinal), read.Intersect(HandWritten.Keys).OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(HandWritten.Keys, name => Assert.IsType<ReadModel>(SimModels.Get(name)));
    }

    [Theory]
    [MemberData(nameof(Models))]
    public void TheReadModelDoesWhatTheHandWrittenOneDid(string name)
    {
        var hand = HandWritten[name];
        var read = Assert.IsType<ReadModel>(SimModels.Get(name));
        Assert.Equal(hand.Tab, read.Tab);
        Assert.Equal(hand.Seen, read.Seen);
        Assert.Equal(hand.Instanced, read.Instanced);
        if (!NewTabs.ContainsKey(name)) Assert.Equal(hand.JudgedOnOutcome, read.JudgedOnOutcome);
        // A tab judged on the outcome: the same rule for each owner (none, or a warning or a bonus, set off by the play or later).
        foreach (var owner in OwnersOf(read))
        {
            // Crossed for each owner as the hand-written model was (a read model's crossing is by whose side it helps: decision #198).
            if (!ReviewedCrossing.ContainsKey(name))
                Assert.True(hand.HarmfulFor(owner) == read.HarmfulFor(owner), $"{name}: crossed {read.HarmfulFor(owner)} for owner {owner}, the hand-written model {hand.HarmfulFor(owner)}");
            static string RuleOf(OutcomeRule? rule) => rule == null ? "none" : $"harmful {rule.Harmful}, in play {rule.InPlay}, by branch {rule.InBranch != null}, in some {rule.InSome != null}";
            if (!NewTabs.ContainsKey(name)) Assert.Equal(RuleOf(hand.OutcomeTab(Board(read, owner), new SimOutcome(), owner)), RuleOf(read.OutcomeTab(Board(read, owner), new SimOutcome(), owner)));
        }
        var differences = new List<string>();
        var compared = 0;
        foreach (var hook in Hooks.Where(hook => hand.Overrides(hook) || read.Overrides(hook)))
        {
            var difference = new List<string>();
            foreach (var owner in OwnersOf(read))
                foreach (var (scenario, call) in Calls(hook, owner))
                {
                    compared++;
                    var handResult = call(hand, Board(read, owner));
                    var readResult = call(read, Board(read, owner));
                    if (handResult != readResult) difference.Add($"{name}.{hook} owner {owner}, {scenario}:\n  hand: {handResult}\n  read: {readResult}");
                }
            if (difference.Count == 0) continue;
            if (Reviewed.ContainsKey($"{name}.{hook}")) continue;
            differences.AddRange(difference.Take(4));
        }
        Assert.True(compared > 0, $"{name}: no scenario compared");
        Assert.True(differences.Count == 0, string.Join("\n", differences));
    }

    [Fact]
    public void EveryReviewedDifferenceIsOne()
    {
        var stale = new List<string>();
        foreach (var key in Reviewed.Keys)
        {
            var (name, hook) = (key[..key.IndexOf('.', StringComparison.Ordinal)], key[(key.IndexOf('.', StringComparison.Ordinal) + 1)..]);
            Assert.True(HandWritten.ContainsKey(name), $"{key}: no hand-written {name}");
            var read = Assert.IsType<ReadModel>(SimModels.Get(name));
            var differs = OwnersOf(read).SelectMany(owner => Calls(hook, owner).Select(entry => (owner, entry.Call)))
                .Any(entry => entry.Call(HandWritten[name], Board(read, entry.owner)) != entry.Call(read, Board(read, entry.owner)));
            if (!differs) stale.Add(key);
        }
        foreach (var name in ReviewedCrossing.Keys)
        {
            var read = Assert.IsType<ReadModel>(SimModels.Get(name));
            if (OwnersOf(read).All(owner => HandWritten[name].HarmfulFor(owner) == read.HarmfulFor(owner))) stale.Add(name + " (crossing)");
        }
        Assert.True(stale.Count == 0, $"reviewed, but the read model does what the hand-written one did there: {string.Join(", ", stale)}");
    }

    // ---- the boards

    /// <summary>The hooks a model can answer (SimModel's), the ones with scenarios here.</summary>
    private static readonly string[] Hooks =
    {
        nameof(SimModel.ModifyDamageAdditive), nameof(SimModel.ModifyDamageMultiplicative), nameof(SimModel.ModifyDamageMultiplicativeLate),
        nameof(SimModel.ModifyBlockAdditive), nameof(SimModel.ModifyBlockMultiplicative),
        nameof(SimModel.BeforeCardPlayed), nameof(SimModel.AfterCardPlayed), nameof(SimModel.AfterCardEvent), nameof(SimModel.AfterPowerApplied),
        nameof(SimModel.BeforeSideTurnEndVeryEarly), nameof(SimModel.BeforeSideTurnEndEarly), nameof(SimModel.BeforeSideTurnEnd), nameof(SimModel.AfterSideTurnEnd),
        nameof(SimModel.AfterSideTurnEndLate), nameof(SimModel.ModifyCardPlayCount), nameof(SimModel.AfterModifyingCardPlayCount),
        nameof(SimModel.AfterDamageGiven), nameof(SimModel.AfterDamageReceived), nameof(SimModel.AfterDeath), nameof(SimModel.AfterApplied),
        nameof(SimModel.BeforeSideTurnStart), nameof(SimModel.AfterSideTurnStart), nameof(SimModel.AfterSideTurnStartLate), nameof(SimModel.AfterOrbChanneled),
        nameof(SimModel.ModifyHpLostBeforeOsty), nameof(SimModel.ModifyHpLostBeforeOstyLate), nameof(SimModel.ModifyHpLostAfterOsty), nameof(SimModel.ModifyHpLostAfterOstyLate),
        nameof(SimModel.AfterModifyingHpLost),
        nameof(SimModel.ModifyOrbValue), nameof(SimModel.ModifyOrbPassiveTriggerCounts), nameof(SimModel.AfterModifyingOrbPassiveTriggerCount), nameof(SimModel.AfterOrbEvoked),
        nameof(SimModel.BeforeAttack), nameof(SimModel.AfterAttack), nameof(SimModel.ModifyDamageCap), nameof(SimModel.AfterModifyingDamageAmount),
        nameof(SimModel.BeforeDamageReceived), nameof(SimModel.AfterArrowShot), nameof(SimModel.AfterArrowImpaled),
    };

    [Fact]
    public void EveryHookAReadModelAnswersHasScenarios()
    {
        // An orb's and an enchantment's own programs aren't a listener's hooks (the engine runs them where OrbCmd and OrbQueue do, and where its card plays): OrbParityTests and EnchantmentParityTests hold them.
        var answered = ReadModelsFile.Specs().Where(spec => spec.Kind is ModelKind.Power or ModelKind.Relic).SelectMany(spec => spec.Hooks).Select(program => ReadModel.SimHookOf(program.Hook)).Distinct();
        Assert.All(answered, hook => Assert.Contains(hook, Hooks));
    }

    /// <summary>
    /// The owners a model is compared on: the ones the game's code can give it (the owner board: ReadModels/power-owners.json, read from where
    /// both branches apply each power; a relic is a player's), you and a teammate for a player, enemy 0 for an enemy. A pet's powers aren't
    /// simulated (Osty's hits are captured).
    /// </summary>
    private static IEnumerable<int> OwnersOf(ReadModel model)
    {
        if (model.Spec.Kind == ModelKind.Relic) return new[] { Who.You, Who.Teammate(0) };
        var kinds = OwnerBoardFile.KindsOf(model.Name);
        return (kinds.Contains(OwnerBoardFile.Player) ? new[] { Who.You, Who.Teammate(0) } : Array.Empty<int>()).Concat(kinds.Contains(OwnerBoardFile.Enemy) ? new[] { 0 } : Array.Empty<int>());
    }

    /// <summary>
    /// A board with the model on <paramref name="owner"/> (a power at 3, a relic armed), its values as the adapter reads them (each one its
    /// code reads, one more than its canonical value, so the state's is what's read), you and a teammate with block and cards (the teammate with two
    /// Frost orbs), two enemies.
    /// </summary>
    private static SimState Board(ReadModel model, int owner)
    {
        var state = new SimState();
        state.You = new SimPlayer { Hp = 50, MaxHp = 70, Block = 4, HandCount = 3, Energy = 3 };
        state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 60, Block = 0, HandCount = 2, Energy = 2, OrbCapacity = 3 });
        state.Teammates[0].Orbs.Add(new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 });
        state.Teammates[0].Orbs.Add(new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 });
        state.Enemies.Add(new SimEnemy { Id = 0, Name = "E0", Hp = 30, Hits = new List<int> { 6 } });
        state.Enemies.Add(new SimEnemy { Id = 1, Name = "E1", Hp = 25, Block = 5, Hits = new List<int> { 3, 3 } });
        foreach (var variable in VarsOf(model.Spec))
        {
            var key = $"{owner}:{model.Name}:{variable.Key}";
            // A value the hand-written model had as a constant is the game's (its canonical value): the read model reads it, the same number.
            var value = variable.Key.EndsWith(".Props", StringComparison.Ordinal) || HandWrittenConstants.Contains($"{model.Name}.{variable.Key}") ? variable.Canonical : variable.Canonical + 1;
            state.Vars[key] = (int)value;
            if (value != (int)value) state.Fractions[key] = value;
        }
        // A value a read model keeps as its state ("Var.key": one its code stores, or each copy's own): the one the hand-written model read from
        // the adapter's values, on the power's one copy (or its own fields).
        foreach (var field in model.Spec.Values().OfType<HookField>().Where(field => field.Field.StartsWith(HookField.VarPrefix, StringComparison.Ordinal)).DistinctBy(field => field.Field))
        {
            var value = (int)field.Canonical + 1;
            state.Vars[$"{owner}:{model.Name}:{field.Field[HookField.VarPrefix.Length..]}"] = value;
            SimEngine.SetField(state, owner, model.HasCopies ? model.CopyFieldKey(1, field.Field) : model.FieldKey(field.Field), value);
        }
        // What a hand-written counter kept as its Data (its count: Automation's cards left), the read model keeps as a field of its own.
        if (CounterFields.TryGetValue(model.Name, out var counter))
            foreach (var (field, value) in counter.Fields) SimEngine.SetField(state, owner, model.HasCopies ? model.CopyFieldKey(1, field) : model.FieldKey(field), value);
        // The powers and relics its code asks other creatures about (creature.HasPower<T>(), Player.GetRelic<T>()), on enemy 1 and the teammate
        // (a dealer, a target; a relic on both players), so each question is asked of a creature with it and of one without; Impaled also as a teammate's arrows (the
        // simulator's ImpaledPowerOther) on enemy 0.
        foreach (var fact in model.Spec.Values().OfType<HookFactValue>())
        {
            if (fact is { Fact: HookFact.CreatureHasPower or HookFact.CreaturePowerAmount, Of: { } power })
            {
                // Only on a creature the game can give it (the owner board): Impaled on an enemy, never on the teammate.
                var kinds = OwnerBoardFile.KindsOf(power);
                foreach (var holder in new[] { 1, Who.Teammate(0) }.Where(holder => kinds.Contains(Who.IsEnemy(holder) ? OwnerBoardFile.Enemy : OwnerBoardFile.Player)))
                    SimEngine.SetPowers(state, holder, SimEngine.PowersOf(state, holder).With(new PowerId(power), amount: 2, data: 0));
                if (power == PowerId.Impaled.Name) SimEngine.SetPowers(state, 0, SimEngine.PowersOf(state, 0).With(SimEngine.ImpaledOther, amount: 1, data: 0));
            }
            if (fact is { Fact: HookFact.CreatureHasRelic, Of: { } relic })
                foreach (var holder in new[] { Who.You, Who.Teammate(0) }) SimEngine.SetRelics(state, holder, SimEngine.RelicsOf(state, holder).With(new RelicId(relic), amount: 0, data: 0));
        }
        // The questions its code asks of the fight's log, seeded as the adapter seeds the board's (HistoryLedger.Seed): none happened yet.
        var questions = model.Spec.Values().OfType<HookHistory>().Select(history => history.Query).Distinct().ToList();
        if (questions.Count > 0) state.History = questions;
        // The turn the hand-written model was told was its own (Stone Calendar's Data 0: its turn): the turn its value names.
        if (TurnVars.TryGetValue(model.Name, out var turnVar)) state.TurnNumber = state.Vars[$"{owner}:{model.Name}:{turnVar}"];
        // A power whose code asks who applied it keeps its applier as its Data (DataCreature), as the adapter reads it: you, for one that asks
        // whether its applier is a player or played the card, or compares it with a pet's owner (Sic 'Em's: its applier's Osty); else enemy 0 (one
        // that asks whether its applier died).
        static bool PetOwnersApplier(HookFactValue fact) => fact.Fact == HookFact.SameCreature
            && (fact.Creature is { Who: HookWho.Applier } && fact.Other is { PetOwner: true } || fact.Other is { Who: HookWho.Applier } && fact.Creature is { PetOwner: true });
        var playersApplier = model.Spec.Values().Any(value => value is HookFactValue { Fact: HookFact.ApplierIsPlayer or HookFact.PlayIsAppliers } || value is HookFactValue fact && PetOwnersApplier(fact));
        var powerData = PowerDataVars.TryGetValue(model.Name, out var dataVar) ? state.Vars[$"{owner}:{model.Name}:{dataVar}"] : CounterFields.TryGetValue(model.Name, out var counted) ? counted.Data
            : model.UsesApplier ? DataCreature.Of(playersApplier ? Who.You : 0) : 0;
        if (model.Spec.Kind == ModelKind.Power) SimEngine.SetPowers(state, owner, SimEngine.PowersOf(state, owner).With(new PowerId(model.Name), amount: PowerAmountVars.TryGetValue(model.Name, out var amountKey) ? state.Vars[$"{owner}:{model.Name}:{amountKey}"] : 3, data: powerData));
        var amount = RelicAmountVars.TryGetValue(model.Name, out var amountVar) ? state.Vars[$"{owner}:{model.Name}:{amountVar}"]
            : RelicAmountFields.TryGetValue(model.Name, out var amountField) ? amountField.Amount : 0;
        if (RelicAmountFields.TryGetValue(model.Name, out var countField)) SimEngine.SetField(state, owner, model.FieldKey(countField.Field), countField.Amount);
        // A relic whose amount is its value keeps no flag (SimReaders.Relics); any other starts armed.
        var data = RelicDataVars.TryGetValue(model.Name, out var relicDataVar) ? state.Vars[$"{owner}:{model.Name}:{relicDataVar}"]
            : RelicCounterFields.TryGetValue(model.Name, out var relicCounter) ? relicCounter.Data : RelicAmountVars.ContainsKey(model.Name) ? 0 : (int)RelicUse.Armed;
        if (RelicCounterFields.TryGetValue(model.Name, out var counterField)) SimEngine.SetField(state, owner, model.FieldKey(counterField.Field), counterField.Data);
        if (model.Spec.Kind == ModelKind.Relic) SimEngine.SetRelics(state, owner, SimEngine.RelicsOf(state, owner).With(new RelicId(model.Name), amount, data));
        return state;
    }

    private static IEnumerable<HookVar> VarsOf(ModelSpec spec) => spec.Values().OfType<HookVar>().DistinctBy(variable => variable.Key);

    // ---- the calls, each a hook on a board, its result as text

    private static readonly SimCardType[] Types = { SimCardType.Attack, SimCardType.Skill, SimCardType.Power, SimCardType.Status, SimCardType.Curse };

    private static IEnumerable<(string Scenario, Func<SimModel, SimState, string> Call)> Calls(string hook, int owner)
    {
        var others = new[] { owner, owner == Who.You ? Who.Teammate(0) : Who.You, 0 }.Distinct().ToArray();
        switch (hook)
        {
            case nameof(SimModel.ModifyDamageAdditive) or nameof(SimModel.ModifyDamageMultiplicative) or nameof(SimModel.ModifyDamageMultiplicativeLate):
                foreach (var damage in Damages(owner))
                    yield return ($"{damage}", (model, state) => Number(hook switch
                    {
                        nameof(SimModel.ModifyDamageAdditive) => model.ModifyDamageAdditive(state, owner, damage),
                        nameof(SimModel.ModifyDamageMultiplicative) => model.ModifyDamageMultiplicative(state, owner, damage),
                        _ => model.ModifyDamageMultiplicativeLate(state, owner, damage),
                    }));
                break;
            case nameof(SimModel.AfterArrowShot):
                // A shot each player tries, with an arrow or with none, from a card's play or not: what it does (block for the others).
                foreach (var shooter in others.Where(Who.IsPlayer))
                    foreach (var withArrow in new[] { true, false })
                        foreach (var fromCard in new[] { true, false })
                            yield return ($"a shot by {shooter}, {(withArrow ? "with an arrow" : "with none")}{(fromCard ? ", from a card" : "")}", (model, state) =>
                            {
                                state.ArrowAwayAt = withArrow ? 0 : Who.None;
                                state.CurrentPlay = fromCard ? new SimCardPlay(new SimCard("Shot", SimCardType.Attack), shooter, Target: 0, PlayIndex: 0, PlayCount: 1, AutoPlay: false) : null;
                                return Branches(model.AfterArrowShot(state, owner, shooter));
                            });
                break;
            case nameof(SimModel.AfterArrowImpaled):
                // Each player's arrow impaling each enemy.
                foreach (var shooter in others.Where(Who.IsPlayer))
                    foreach (var target in new[] { 0, 1 })
                        yield return ($"{shooter}'s arrow impales {target}", (model, state) => Branches(model.AfterArrowImpaled(state, owner, target, shooter)));
                break;
            case nameof(SimModel.BeforeDamageReceived):
                // A damage of every dealer, target, kind and card about to land (none, some), inside the damage: the state it's given changes
                // (Thorns hitting the dealer back: your Osty alive, as a dealer is).
                foreach (var damage in Damages(owner))
                    foreach (var amount in new[] { 0, 4 })
                        yield return ($"{damage}, {amount} about to land", (model, state) =>
                        {
                            state.You.OstyHp = 10;
                            model.BeforeDamageReceived(state, owner, damage, amount);
                            return Branches(new[] { state }) + $" // Osty {state.You.OstyHp}";
                        });
                break;
            case nameof(SimModel.ModifyDamageCap):
                // The most a damage of every dealer, target, kind and card can be (Hook.ModifyDamage's cap).
                foreach (var damage in Damages(owner))
                    yield return ($"{damage}", (model, state) => Number(model.ModifyDamageCap(state, owner, damage)));
                break;
            case nameof(SimModel.AfterModifyingDamageAmount):
                // Its term changed a damage (the engine tells the ones that did): what it does then (a flash), inside the damage.
                foreach (var card in new[] { null, new SimCard("Strike", SimCardType.Attack) })
                    yield return ($"after it changed a damage, card {card?.Name ?? "none"}", (model, state) =>
                    {
                        model.AfterModifyingDamageAmount(state, owner, card);
                        return Branches(new[] { state });
                    });
                break;
            case nameof(SimModel.ModifyHpLostBeforeOsty) or nameof(SimModel.ModifyHpLostBeforeOstyLate) or nameof(SimModel.ModifyHpLostAfterOsty) or nameof(SimModel.ModifyHpLostAfterOstyLate):
                // The HP a hit would take as the pass starts (none, one, some, more than a cap), for every dealer, target, kind and card.
                foreach (var damage in Damages(owner))
                    foreach (var amount in new[] { 0m, 1m, 3m, 12m, 30m })
                        yield return ($"{damage}, {amount} HP", (model, state) => Number(hook switch
                        {
                            nameof(SimModel.ModifyHpLostBeforeOsty) => model.ModifyHpLostBeforeOsty(state, owner, damage, amount),
                            nameof(SimModel.ModifyHpLostBeforeOstyLate) => model.ModifyHpLostBeforeOstyLate(state, owner, damage, amount),
                            nameof(SimModel.ModifyHpLostAfterOsty) => model.ModifyHpLostAfterOsty(state, owner, damage, amount),
                            _ => model.ModifyHpLostAfterOstyLate(state, owner, damage, amount),
                        }));
                break;
            case nameof(SimModel.AfterModifyingHpLost):
                // Its pass changed the HP lost, before Osty's passes or after: what it does then (a flash, a stack spent), inside the damage.
                foreach (var afterOsty in new[] { false, true })
                    yield return ($"after its pass {(afterOsty ? "after Osty" : "before Osty")}", (model, state) =>
                    {
                        model.AfterModifyingHpLost(state, owner, afterOsty);
                        return Branches(new[] { state });
                    });
                break;
            case nameof(SimModel.ModifyBlockAdditive) or nameof(SimModel.ModifyBlockMultiplicative):
                foreach (var block in Blocks(owner))
                    yield return ($"{block}", (model, state) => Number(hook == nameof(SimModel.ModifyBlockAdditive) ? model.ModifyBlockAdditive(state, owner, block) : model.ModifyBlockMultiplicative(state, owner, block)));
                break;
            case nameof(SimModel.BeforeCardPlayed) or nameof(SimModel.AfterCardPlayed):
                // A run of plays in turn, each through both hooks (a counter's count, a once-a-turn flag), each step's outcome compared.
                var run = Plays(others).Where(play => play.Owner == owner && play.PlayIndex == 0 && play.Card.Energy == 1).ToList();
                if (run.Count > 0)
                    yield return ("a run of plays in turn", (model, state) => Sequence(state, Enumerable.Range(0, 24).Select(index => run[index * 7 % run.Count]), (branch, play) =>
                    {
                        // As the engine runs a play: BeforeCardPlayed, the play started counted, the play finished counted, AfterCardPlayed.
                        branch.CurrentPlay = play;
                        // What it's the first of this turn, as it starts (SimEngine's Count: SimCardPlay.FirstThisTurn).
                        var started = play with { FirstThisTurn = SimKeys.FirstThisTurn(SimEngine.TurnOf(branch, play.Owner), play.Card) };
                        return model.BeforeCardPlayed(branch, owner, play).Select(before =>
                        {
                            var player = before.MutablePlayer(play.Owner);
                            player.Turn = SimKeys.CountPlay(SimKeys.CountPlay(player.Turn, PlayStage.Started, play.Card.Type, play.Card.Tags), PlayStage.Finished, play.Card.Type, play.Card.Tags);
                            before.CurrentPlay = started;
                            return before;
                        }).SelectMany(after => model.AfterCardPlayed(after, owner, started)).ToList();
                    }));
                foreach (var play in Plays(others))
                {
                    yield return ($"{hook} of {Describe(play)}", (model, state) =>
                    {
                        state.CurrentPlay = play;
                        return Branches(hook == nameof(SimModel.BeforeCardPlayed) ? model.BeforeCardPlayed(state, owner, play) : model.AfterCardPlayed(state, owner, play));
                    });
                    // The play's two hooks in turn, as the engine runs them (what BeforeCardPlayed binds, AfterCardPlayed acts on).
                    yield return ($"both hooks of {Describe(play)}", (model, state) =>
                    {
                        state.CurrentPlay = play;
                        return Branches(model.BeforeCardPlayed(state, owner, play).SelectMany(branch => model.AfterCardPlayed(branch, owner, play)).ToList());
                    });
                }
                break;
            case nameof(SimModel.AfterCardEvent):
                // Twelve of each event in turn (a counter's count, a once-a-turn flag), each step's outcome compared.
                foreach (var cardEvent in Enum.GetValues<CardEvent>().Where(_ => Who.IsPlayer(owner)))
                    yield return ($"twelve {cardEvent} for {owner} in turn", (model, state) => Sequence(state, Enumerable.Repeat(0, 12), (branch, _) => model.AfterCardEvent(branch, owner, cardEvent, owner)));
                foreach (var cardEvent in Enum.GetValues<CardEvent>())
                    foreach (var player in others.Where(Who.IsPlayer))
                        foreach (var card in new[] { null, new SimCard("Skilled", SimCardType.Skill), new SimCard("Attacker", SimCardType.Attack), new SimCard(CardId.Sadness.Name, SimCardType.Status), new SimCard(CardId.Dazed.Name, SimCardType.Status), new SimCard("Cursed", SimCardType.Curse) })
                            foreach (var drawn in new[] { null, new SimDrawCard("status") { Status = true }, new SimDrawCard("ethereal") { Ethereal = true } })
                                yield return ($"{cardEvent} for {player}, card {card?.Type}, drawn {drawn?.Key}", (model, state) =>
                                {
                                    state.EventCard = card;
                                    state.DrawnCard = drawn;
                                    return Branches(model.AfterCardEvent(state, owner, cardEvent, player));
                                });
                break;
            case nameof(SimModel.AfterPowerApplied):
                foreach (var power in new[] { PowerId.Vulnerable, PowerId.Weak, PowerId.Doom, PowerId.Strength, PowerId.Poison, PowerId.DarkShackles, PowerId.Dexterity })
                    foreach (var amount in new[] { 2, -1 })
                        foreach (var applier in others)
                            foreach (var target in new[] { 0, 1, Who.You, Who.Teammate(0) })
                                foreach (var debuff in new[] { true, false })
                                    yield return ($"{power.Name} {amount} by {applier} on {target}, debuff {debuff}", (model, state) =>
                                        Branches(model.AfterPowerApplied(state, owner, power, amount, applier, target, debuff)));
                break;
            case nameof(SimModel.AfterDamageGiven) or nameof(SimModel.AfterDamageReceived):
                // A hit of every dealer, target, kind and outcome (blocked, through, a kill), inside the damage: the state it's given changes.
                foreach (var hit in Hits(owner))
                    yield return ($"{hit}", (model, state) =>
                    {
                        if (hook == nameof(SimModel.AfterDamageGiven)) model.AfterDamageGiven(state, owner, hit); else model.AfterDamageReceived(state, owner, hit);
                        return Branches(new[] { state });
                    });
                break;
            case nameof(SimModel.AfterDeath):
                // A creature dies (the applier, another enemy, the owner), then the players' turn ends: what the death changed shows there.
                foreach (var dead in new[] { 0, 1, owner }.Distinct().Where(Who.IsEnemy))
                    yield return ($"enemy {dead} dies, then the players' turn ends", (model, state) =>
                    {
                        state.Enemy(dead)!.Hp = 0;
                        model.AfterDeath(state, owner, dead);
                        return Branches(new[] { state }) + " // " + Sequence(state, TurnEndHooks, (branch, turnEndHook) => turnEndHook(model, branch, owner));
                    });
                break;
            case nameof(SimModel.AfterApplied):
                // The power lands on its owner (new, or on top of the one there), by each applier, as SimEngine.ApplyPower lands it; then the
                // players' turn ends, where what it set up shows.
                foreach (var applier in others)
                    foreach (var fresh in new[] { true, false })
                        yield return ($"applied by {applier}, {(fresh ? "new" : "on top")}, then the players' turn ends", (model, state) =>
                        {
                            var power = new PowerId(model.Name);
                            if (fresh) SimEngine.SetPowers(state, owner, SimEngine.PowersOf(state, owner).Without(power));
                            Land(model, state, owner, applier, power, amount: 2, had: !fresh);
                            return Branches(new[] { state }) + " // " + Sequence(state, TurnEndHooks, (branch, turnEndHook) => turnEndHook(model, branch, owner));
                        });
                break;
            case nameof(SimModel.ModifyCardPlayCount) or nameof(SimModel.AfterModifyingCardPlayCount):
                // Each play's shape decided four times in turn, as the engine decides it (DecideCardPlay): the count from 1 and from 2, and
                // AfterModifyingCardPlayCount when it changed it (a stack used each time); each count and each step's outcome compared.
                foreach (var play in Plays(others))
                    yield return ($"the play count of {Describe(play)}, four times", (model, state) =>
                    {
                        var text = new StringBuilder();
                        foreach (var from in new[] { 1, 2, 1, 1 })
                        {
                            // A listener only while it's there (the engine lists the powers and relics on the board): a power used up answers no more.
                            if (!SimEngine.PowersOf(state, owner).Has(new PowerId(model.Name)) && !SimEngine.RelicsOf(state, owner).Has(new RelicId(model.Name))) { text.Append("gone // "); continue; }
                            var count = model.ModifyCardPlayCount(state, owner, play, from);
                            if (count != from) model.AfterModifyingCardPlayCount(state, owner, play);
                            text.Append($"{from} -> {count}: ").Append(Branches(new[] { state })).Append(" // ");
                        }
                        return text.ToString();
                    });
                break;
            case nameof(SimModel.BeforeSideTurnEndVeryEarly) or nameof(SimModel.BeforeSideTurnEndEarly) or nameof(SimModel.BeforeSideTurnEnd) or nameof(SimModel.AfterSideTurnEnd)
                or nameof(SimModel.AfterSideTurnEndLate):
                // The players' turn ending, every hook in the game's order (what an earlier one sets, a later one reads), on the board and with no
                // block and an empty hand; each hook's outcome compared.
                foreach (var (scenario, emptied) in new[] { ("the board", false), ("no block, an empty hand", true) })
                    yield return ($"the players' turn ending, {scenario}", (model, state) =>
                    {
                        if (emptied) foreach (var id in state.PlayerIds) { var player = state.MutablePlayer(id); player.Block = 0; player.HandCount = 0; }
                        return Sequence(state, TurnEndHooks, (branch, turnEndHook) => turnEndHook(model, branch, owner));
                    });
                break;
            case nameof(SimModel.BeforeSideTurnStart) or nameof(SimModel.AfterSideTurnStart) or nameof(SimModel.AfterSideTurnStartLate):
                // The enemies' turn starting, every hook in the game's order, then a powered attack on its owner from enemy 0 and from you (what
                // a power that goes as a turn starts changed); each hook's outcome compared.
                yield return ("the enemies' turn starting", (model, state) =>
                {
                    state.EnemyTurnNow = true;
                    var after = Sequence(state, TurnStartHooks, (branch, turnStartHook) => turnStartHook(model, branch, owner));
                    var attacks = string.Join(", ", new[] { 0, Who.You }.Select(dealer => model.ModifyDamageMultiplicative(state, owner, new DamageCtx(dealer, owner, SimProps.Move, Card: null, Play: null))));
                    return after + " // attacks on its owner Ã— " + attacks;
                });
                break;
            case nameof(SimModel.AfterOrbChanneled):
                // Twelve orbs channeled for its owner in turn (a counter's count), and one for each other player.
                if (Who.IsPlayer(owner))
                    yield return ($"twelve orbs channeled for {owner} in turn", (model, state) => Sequence(state, Enumerable.Repeat(0, 12), (branch, _) => model.AfterOrbChanneled(branch, owner, owner)));
                foreach (var player in others.Where(Who.IsPlayer))
                    yield return ($"an orb channeled for {player}", (model, state) => Branches(model.AfterOrbChanneled(state, owner, player)));
                break;
            case nameof(SimModel.BeforeAttack) or nameof(SimModel.AfterAttack):
                // Two attacks in turn, as the engine runs one (SimEngine.RunAttack: BeforeAttack, its hits' damage worked out, AfterAttack), of every
                // attacker, props and card: each step's outcome and the damage a hit of each attack gets compared (what an attack kept, the next one
                // reads). Osty's attacks with him alive, and once at 0 HP (he died during it).
                foreach (var attacker in new[] { owner, Who.You, Who.YourOsty, Who.Teammate(0) }.Distinct().Where(attacker => !Who.IsEnemy(attacker)))
                    foreach (var props in new[] { SimProps.Move, SimProps.Move | SimProps.Unpowered, SimProps.Move | SimProps.FromArrow })
                        foreach (var type in new SimCardType?[] { SimCardType.Attack, SimCardType.Skill, null })
                            foreach (var ostyHp in attacker == Who.YourOsty ? new[] { 5, 0 } : new[] { 5 })
                                yield return ($"two attacks by {attacker}, {props}, card {type?.ToString() ?? "none"}{(ostyHp == 0 ? ", Osty at 0 HP" : "")}", (model, state) =>
                                {
                                    state.You.OstyHp = ostyHp;
                                    var player = attacker == Who.YourOsty ? Who.You : attacker;
                                    var text = new StringBuilder();
                                    IEnumerable<SimState> states = new[] { state };
                                    foreach (var id in new[] { 1, 2 })
                                    {
                                        var card = type is { } cardType ? new SimCard(id == 1 ? "First" : "Second", cardType) : null;
                                        var play = new SimCardPlay(card ?? new SimCard("Played", SimCardType.Skill), player, Target: 0, PlayIndex: 0, PlayCount: 1, AutoPlay: false);
                                        var attack = new AttackInfo(id, attacker, props, card);
                                        var hit = new DamageCtx(attacker, Target: 0, props, card, card == null ? null : play);
                                        states = states.SelectMany(branch =>
                                        {
                                            branch.CurrentPlay = play;
                                            branch.CurrentAttack = id;
                                            return model.BeforeAttack(branch, owner, attack);
                                        }).ToList();
                                        text.Append(Branches(states)).Append(" // hit: ")
                                            .Append(string.Join(",", states.Select(branch => $"{Number(model.ModifyDamageAdditive(branch, owner, hit))} x{Number(model.ModifyDamageMultiplicative(branch, owner, hit))}")))
                                            .Append(" // ");
                                        states = states.SelectMany(branch => model.AfterAttack(branch, owner, attack)).ToList();
                                        foreach (var branch in states) branch.CurrentAttack = 0;
                                        text.Append(Branches(states)).Append(" // ");
                                    }
                                    return text.ToString();
                                });
                break;
            // The orbs' hooks (decision #214): every orb kind, each player's, front or not, from every value and count.
            case nameof(SimModel.ModifyOrbValue):
                foreach (var orb in Orbs(others))
                    foreach (var value in new[] { -2m, 0m, 3m, 8m })
                        yield return ($"{orb}, value {value}", (model, state) => Number(model.ModifyOrbValue(state, owner, orb, value)));
                break;
            case nameof(SimModel.ModifyOrbPassiveTriggerCounts):
                foreach (var orb in Orbs(others))
                    foreach (var count in new[] { 1, 2 })
                        yield return ($"{orb}, count {count}", (model, state) => Number(model.ModifyOrbPassiveTriggerCounts(state, owner, orb, count)));
                break;
            case nameof(SimModel.AfterModifyingOrbPassiveTriggerCount):
                foreach (var orb in Orbs(others))
                    yield return ($"{orb}", (model, state) =>
                    {
                        model.AfterModifyingOrbPassiveTriggerCount(state, owner, orb);
                        return Branches(new[] { state });
                    });
                break;
            case nameof(SimModel.AfterOrbEvoked):
                // An evoke of each kind by each player, its target alive, dead, or none.
                foreach (var orb in Orbs(others))
                    foreach (var targets in new[] { new[] { 0 }, new[] { 0, 1 }, Array.Empty<int>() })
                        foreach (var killed in new[] { false, true })
                            yield return ($"{orb}, targets {string.Join(",", targets)}{(killed ? ", the first dead" : "")}", (model, state) =>
                            {
                                if (killed && state.Enemy(0) is { } first) first.Hp = 0;
                                state.OrbTargets = targets;
                                return Branches(model.AfterOrbEvoked(state, owner, orb));
                            });
                break;
        }
    }

    /// <summary>An orb of each kind of each player's, in front or not.</summary>
    private static IEnumerable<OrbCtx> Orbs(IEnumerable<int> creatures) =>
        from player in creatures.Where(Who.IsPlayer)
        from kind in new[] { OrbKind.Lightning, OrbKind.Frost, OrbKind.Dark, OrbKind.Glass, OrbKind.Plasma }
        from index in new[] { 0, 1 }
        select new OrbCtx(player, index, kind);

    /// <summary>The hooks of the enemies' side's turn starting, in the game's order (CombatManager.StartTurn).</summary>
    private static readonly Func<SimModel, SimState, int, IEnumerable<SimState>>[] TurnStartHooks =
    {
        (model, state, owner) => model.BeforeSideTurnStart(state, owner), (model, state, owner) => model.AfterSideTurnStart(state, owner),
        (model, state, owner) => model.AfterSideTurnStartLate(state, owner),
    };

    /// <summary>
    /// A power landing on its owner as SimEngine.ApplyPower lands it, with the model given in place of the one registered: its amount added, then a
    /// read model's landing (its applier, a copy of its own) and AfterApplied for a new instance or copy; a hand-written one's AfterApplied for a
    /// new instance, or for every copy of an instanced one.
    /// </summary>
    private static void Land(SimModel model, SimState state, int owner, int applier, PowerId power, int amount, bool had)
    {
        SimEngine.SetPowers(state, owner, SimEngine.PowersOf(state, owner).Add(power, amount));
        if (model is ReadModel read)
        {
            if (!had && read.UsesApplier) SimEngine.SetPowers(state, owner, SimEngine.PowersOf(state, owner).With(power, SimEngine.PowersOf(state, owner)[power], DataCreature.Of(applier)));
            var copy = read.Land(state, owner, applier, amount, had);
            if (!had || copy > 0) read.AfterApplied(state, owner, applier, amount, copy);
        }
        else if (!had || model.Instanced) model.AfterApplied(state, owner, applier, amount);
    }

    /// <summary>The hooks of the players' side's turn ending, in the game's order (decision #51).</summary>
    private static readonly Func<SimModel, SimState, int, IEnumerable<SimState>>[] TurnEndHooks =
    {
        (model, state, owner) => model.BeforeSideTurnEndVeryEarly(state, owner), (model, state, owner) => model.BeforeSideTurnEndEarly(state, owner),
        (model, state, owner) => model.BeforeSideTurnEnd(state, owner), (model, state, owner) => model.AfterSideTurnEnd(state, owner),
        (model, state, owner) => model.AfterSideTurnEndLate(state, owner),
    };

    private static IEnumerable<SimCardPlay> Plays(int[] players)
    {
        foreach (var player in players.Where(Who.IsPlayer))
            foreach (var type in Types)
                foreach (var energy in new[] { 0, 1, 2, 3 })
                    foreach (var (index, count) in new[] { (0, 1), (1, 2) })
                    {
                        var card = new SimCard("Played", type) { Energy = energy, Ethereal = energy == 1, Stars = energy == 2 ? 2 : 0, Upgradable = energy == 3, Tags = energy == 2 ? new() { SimCardTag.Wah } : new(),
                            Affliction = energy switch { 1 => SimAffliction.Galvanized, 2 => SimAffliction.Tainted, _ => SimAffliction.None } };
                        yield return new SimCardPlay(card, player, Target: 0, index, count, AutoPlay: energy == 3);
                    }
    }

    private static IEnumerable<DamageCtx> Damages(int owner)
    {
        var card = new SimCard("Strike", SimCardType.Attack);
        foreach (var dealer in new int?[] { owner, Who.You, Who.YourOsty, Who.Teammate(0), 0, null }.Distinct())
            foreach (var target in new[] { owner, 1, Who.You, Who.Teammate(0) }.Distinct())
                foreach (var props in new[] { SimProps.Move, SimProps.Move | SimProps.Unpowered, SimProps.Unpowered, SimProps.None, SimProps.Move | SimProps.FromArrow })
                    foreach (var player in new[] { (int?)null, owner, Who.You, Who.Teammate(0) }.Distinct())
                    {
                        var play = player is { } playedBy && Who.IsPlayer(playedBy) ? new SimCardPlay(card, playedBy, target, PlayIndex: 0, PlayCount: 1, AutoPlay: false) : null;
                        yield return new DamageCtx(dealer, target, props, play?.Card, play);
                        if (play == null) yield return new DamageCtx(dealer, target, props, card, Play: null);
                    }
    }

    private static IEnumerable<DamageEvent> Hits(int owner)
    {
        var card = new SimCard("Strike", SimCardType.Attack);
        foreach (var dealer in new int?[] { owner, Who.You, Who.YourOsty, Who.Teammate(0), 0, null }.Distinct())
            foreach (var target in new[] { owner, 1, Who.You, Who.Teammate(0) }.Distinct())
                foreach (var props in new[] { SimProps.Move, SimProps.Move | SimProps.Unpowered, SimProps.Unpowered })
                    foreach (var (blocked, unblocked, killed) in new[] { (5, 0, false), (2, 4, false), (0, 9, true) })
                        yield return new DamageEvent(dealer, target, props, props.HasFlag(SimProps.Move) ? card : null, blocked + unblocked, blocked, unblocked, killed);
    }

    private static IEnumerable<BlockCtx> Blocks(int owner)
    {
        var card = new SimCard("Defend", SimCardType.Skill);
        foreach (var target in new[] { owner, Who.You, Who.Teammate(0), 0 }.Distinct())
            foreach (var props in new[] { SimProps.Move, SimProps.Move | SimProps.Unpowered, SimProps.Unpowered, SimProps.None })
                foreach (var player in new[] { (int?)null, owner, Who.You, Who.Teammate(0) }.Distinct())
                {
                    var play = player is { } playedBy && Who.IsPlayer(playedBy) ? new SimCardPlay(card, playedBy, target, PlayIndex: 0, PlayCount: 1, AutoPlay: false) : null;
                    yield return new BlockCtx(target, props, play?.Card, play);
                }
    }

    private static string Describe(SimCardPlay play) =>
        $"{play.Card.Type} by {play.Owner} costing {play.Card.Energy}{(play.Card.Ethereal ? " Ethereal" : "")}{(play.Card.Stars > 0 ? $" {play.Card.Stars} stars" : "")} (play {play.PlayIndex + 1} of {play.PlayCount}{(play.AutoPlay ? ", auto" : "")})";

    private static string Number(decimal value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// A copy without what models keep of their own: every relic entry's counter and flag, every read model's fields (its copies' amounts
    /// included, and the frozen counter models' lists of copies kept there). The power's entry holds the copies' total either way.
    /// </summary>
    private static SimState Stripped(SimState state)
    {
        var copy = state.Clone();
        // What a payout decided where it's paid kept for its tab (SimState.Payouts, decision #239): the tab's bookkeeping, not an effect; a class
        // given a row since its conversion keeps it where its hand-written model kept nothing (NewTabs).
        copy.Payouts = NamedCounts.Empty;
        foreach (var id in copy.PlayerIds)
        {
            var player = copy.MutablePlayer(id);
            foreach (var relic in player.Relics.Items) player.Relics = player.Relics.With(relic.Name, amount: 0, data: 0);
            player.Fields = NamedCounts.Empty;
            // A hand-written counter's count in its power's Data (the read model keeps it in its fields).
            foreach (var power in player.Powers.Items.Where(power => CounterFields.ContainsKey(power.Name.Name) || KeptAttackData.Contains(power.Name.Name) || FlagInData.Contains(power.Name.Name)))
                player.Powers = player.Powers.With(power.Name, power.Amount, data: 0);
        }
        foreach (var enemy in copy.Enemies)
        {
            enemy.Fields = NamedCounts.Empty;
            foreach (var power in enemy.Powers.Items.Where(power => CounterFields.ContainsKey(power.Name.Name) || KeptAttackData.Contains(power.Name.Name) || FlagInData.Contains(power.Name.Name)))
                enemy.Powers = enemy.Powers.With(power.Name, power.Amount, data: 0);
        }
        return copy;
    }

    /// <summary>Calls in turn, each on every branch the one before left; each step's branches as text.</summary>
    private static string Sequence<TStep>(SimState state, IEnumerable<TStep> steps, Func<SimState, TStep, IEnumerable<SimState>> call)
    {
        IEnumerable<SimState> states = new[] { state };
        var text = new StringBuilder();
        foreach (var step in steps)
        {
            states = states.SelectMany(branch => call(branch, step)).ToList();
            text.Append(Branches(states)).Append(" // ");
        }
        return text.ToString();
    }

    /// <summary>
    /// A hook's branches as text: each one's state key, weight, what it logged and whether it's unknown, in key order, identical ones merged. What each kind of model
    /// keeps of its own is left out of the key (a relic's counter and flag on its entry, a read model's fields), as two ways of keeping the same
    /// thing: the sequences compare what they do with it.
    /// </summary>
    private static string Branches(IEnumerable<SimState> states)
    {
        // Identical branches as one, their weights summed (decision #107: a read model's steps merge them; a hand-written model's may not have).
        var lines = states.ToList().Select(branch =>
        {
            var text = new StringBuilder();
            text.Append($"key {Stripped(branch).Key():X16}");
            text.Append(" fired [" + string.Join(",", branch.Fired.Items.Select(entry => $"{entry.Name}:{entry.Amount}/{entry.Data}")) + "]");
            text.Append(" any [" + string.Join(",", branch.FiredAny.Items.Select(entry => $"{entry.Name}:{entry.Amount}/{entry.Data}")) + "]");
            if (branch.Unmodelled != null) text.Append(" unknown: " + branch.Unmodelled);
            return (Text: text.ToString(), branch.Weight);
        }).GroupBy(line => line.Text, StringComparer.Ordinal).Select(group => $"{group.Key} weight {group.Sum(line => line.Weight):0.####}").Order(StringComparer.Ordinal);
        return string.Join(" | ", lines);
    }
}
