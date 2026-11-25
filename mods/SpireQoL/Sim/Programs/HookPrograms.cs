using System.Text.Json.Serialization;

namespace SpireQoL.Sim;

/// <summary>
/// The game hooks a model read from its code can answer (decision #195), each where the simulator runs the same hook for every model
/// (SimModel): the card play's, the card events' (AfterCardEvent by its event), a power landing, and the damage and block math.
/// </summary>
public enum HookName
{
    BeforeCardPlayed,
    AfterCardPlayed,
    AfterCardDrawn,
    AfterCardExhausted,
    AfterCardDiscarded,
    AfterCardGeneratedForCombat,
    AfterShuffle,
    AfterStarsSpent,
    AfterStarsGained,
    AfterEnergySpent,
    AfterArrowDislodged,
    AfterPowerAmountChanged,
    BeforeSideTurnEndVeryEarly,
    BeforeSideTurnEndEarly,
    BeforeSideTurnEnd,
    AfterSideTurnEnd,
    AfterSideTurnEndLate,
    ModifyCardPlayCount,
    AfterModifyingCardPlayCount,
    AfterDamageGiven,
    AfterDamageReceived,
    AfterDeath,
    ModifyDamageAdditive,
    ModifyDamageMultiplicative,
    ModifyDamageMultiplicativeLate,
    ModifyBlockAdditive,
    ModifyBlockMultiplicative,
    /// <summary>PowerModel.AfterApplied: a new instance of the power landed (a copy of an instanced one: each application).</summary>
    AfterApplied,
    /// <summary>Hook.BeforeSideTurnStart as the enemies' turn starts (the one turn start a simulation reaches): its participants the enemies.</summary>
    BeforeSideTurnStart,
    /// <summary>Hook.AfterSideTurnStart as the enemies' turn starts: its participants the enemies.</summary>
    AfterSideTurnStart,
    /// <summary>Hook.AfterSideTurnStart's second pass (AfterSideTurnStartLate) as the enemies' turn starts.</summary>
    AfterSideTurnStartLate,
    /// <summary>Hook.AfterOrbChanneled: an orb was channeled for a player (its player the hook's).</summary>
    AfterOrbChanneled,
    /// <summary>A command of the model's own another class calls (PoisonPower.Trigger, which Outbreak calls on each enemy's poison): ModelSpec.Commands.</summary>
    OwnCommand,
    /// <summary>Hook.ModifyHpLost's passes (SimEngine.HpLossPipeline): each a Modify hook on the HP the hit would take, in the game's order.</summary>
    ModifyHpLostBeforeOsty,
    ModifyHpLostBeforeOstyLate,
    ModifyHpLostAfterOsty,
    ModifyHpLostAfterOstyLate,
    /// <summary>Hook.AfterModifyingHpLostBeforeOsty and AfterOsty: told to the models whose pass changed the HP lost (a flash, Buffer's stack).</summary>
    AfterModifyingHpLostBeforeOsty,
    AfterModifyingHpLostAfterOsty,
    /// <summary>Hook.ModifyOrbValue: an orb's value as each listener in turn leaves it (FocusPower, InfusedCore), HookFact.OrbValueSoFar the amount so far.</summary>
    ModifyOrbValue,
    /// <summary>Hook.ModifyOrbPassiveTriggerCount (each listener's ModifyOrbPassiveTriggerCounts): how many times an orb's passive goes off (Gold-Plated Cables).</summary>
    ModifyOrbPassiveTriggerCounts,
    /// <summary>Hook.AfterModifyingOrbPassiveTriggerCount: told to the listeners whose term changed the count (a flash).</summary>
    AfterModifyingOrbPassiveTriggerCount,
    /// <summary>Hook.AfterOrbEvoked: an orb was evoked, with the creatures its evoke acted on (Thunder hits the living ones again).</summary>
    AfterOrbEvoked,
    /// <summary>An orb's own Passive (OrbModel.Passive), run each time OrbModel.TriggerPassive goes off: ModelKind.Orb's program.</summary>
    Passive,
    /// <summary>An orb's own Evoke (OrbModel.Evoke), run by OrbCmd's evoke: ModelKind.Orb's program.</summary>
    Evoke,
    /// <summary>
    /// An enchantment's own OnPlay (EnchantmentModel.OnPlay), run by the engine after each play's own effects (CardModel.OnPlayWrapper):
    /// ModelKind.Enchantment's program.
    /// </summary>
    OnPlay,
    /// <summary>Hook.BeforeAttack: an attack (AttackCommand) is about to make its hits (SimEngine.RunAttack): its attacker, props and card.</summary>
    BeforeAttack,
    /// <summary>Hook.AfterAttack: an attack made its hits.</summary>
    AfterAttack,
    /// <summary>Hook.ModifyDamage's cap (ModifyDamageCap): the most a damage can be, the smallest of the listeners' (SimEngine's caps).</summary>
    ModifyDamageCap,
    /// <summary>Hook.AfterModifyingDamageAmount: told to the listeners whose term changed a damage (a flash).</summary>
    AfterModifyingDamageAmount,
    /// <summary>
    /// Hook.BeforeDamageReceived: a damage worked out, about to meet its target's block (CreatureCmd.Damage, after Hook.ModifyDamage), on any creature:
    /// its dealer, target, props and card (Thorns hits the dealer back).
    /// </summary>
    BeforeDamageReceived,
    /// <summary>Hook.AfterArrowShot: a player tried a shot (ArrowCmd.TryShootArrow: with an arrow or with none), from a card or not: its shooter the hook's player.</summary>
    AfterArrowShot,
    /// <summary>Hook.AfterArrowImpaled: a player's arrow impaled a creature (ArrowCmd.ImpaleTarget), after the shot's attack: its shooter the hook's player.</summary>
    AfterArrowImpaled,
    /// <summary>
    /// Hook.ModifyPreBlockDamageTarget: the creature a damage hits instead, before its target's block, as each listener in turn leaves it (from none:
    /// Wuppy's Loyalty takes a powered attack on its player); a creature-valued return (HookFact.CreatureId).
    /// </summary>
    ModifyPreBlockDamageTarget,
    /// <summary>Hook.ModifyUnblockedDamageTarget: the creature that takes what got past the block, as each listener in turn leaves it (Die For You's Osty).</summary>
    ModifyUnblockedDamageTarget,
    /// <summary>Hook.ShouldAllowHitting: whether a creature can be hit and receive powers (Creature.IsHittable, CanReceivePowers): 1 it can.</summary>
    ShouldAllowHitting,
    /// <summary>Hook.ShouldCreatureBeRemovedFromCombatAfterDeath: whether a creature leaves the fight as it dies (CreatureCmd.Kill): 1 it does.</summary>
    ShouldCreatureBeRemovedFromCombatAfterDeath,
    /// <summary>PowerModel.ShouldPowerBeRemovedAfterOwnerDeath: whether the power comes off its owner as it dies (Creature.RemoveAllPowersAfterDeath).</summary>
    ShouldPowerBeRemovedAfterOwnerDeath,
}

/// <summary>When an orb's passive goes off: the OrbModel trigger hook its class overrides with a call of TriggerPassive (OrbQueue calls them).</summary>
public enum OrbTrigger
{
    /// <summary>BeforeTurnEndOrbTrigger: as its owner's turn ends (OrbQueue.BeforeTurnEnd, in CombatManager.DoTurnEnd), front orb first.</summary>
    TurnEnd,
    /// <summary>AfterTurnStartOrbTrigger: as its owner's next turn starts, past the turns a simulation covers.</summary>
    TurnStart,
}

/// <summary>What a loop of a model's code goes over (foreach): each item is HookWho.Each inside it.</summary>
public enum HookItems
{
    /// <summary>Every player in the fight (CombatState.Players), in their order.</summary>
    Players,
    /// <summary>Every enemy that can be hit (CombatState.HittableEnemies), as the loop starts.</summary>
    HittableEnemies,
    /// <summary>
    /// The owner's side's creatures (CombatState.GetTeammatesOf(base.Owner), itself included): for a player's model every player in their order and
    /// your Osty while he lives (the pets the simulator keeps), for an enemy's the living enemies.
    /// </summary>
    Teammates,
}

/// <summary>A command of a model's own (a public method of its class that returns a Task and takes nothing: PoisonPower.Trigger), read as a program.</summary>
public sealed record CommandProgram(string Method, IReadOnlyList<HookStep> Steps);

/// <summary>
/// How a power's applications are kept (the game's PowerInstanceType): one instance its amounts stack into, a copy of its own per application
/// (Instanced: each The Bomb played counts down on its own), or a copy per applier (InstancedPerApplier: each player's Strangle).
/// </summary>
public enum PowerCopies { One, EachApplication, PerApplier }

/// <summary>
/// What a hook's code asks of its parameters and its model, each one game query (or a small family of them) translated once for every
/// model that asks it (SimAdapter.HookFacts): a closed set, as the card conditions' SimTest is.
/// </summary>
public enum HookFact
{
    /// <summary>The power's own amount (base.Amount).</summary>
    Amount,
    /// <summary>The card played is its owner's (cardPlay.Card.Owner == base.Owner, or .Creature == base.Owner).</summary>
    PlayIsOwners,
    /// <summary>The hook's card's type (the card played, drawn, exhausted or made), as SimCardType's number.</summary>
    CardType,
    /// <summary>The card played is Ethereal (its Keywords contain Ethereal).</summary>
    CardEthereal,
    /// <summary>The card played can still be upgraded (cardPlay.Card.IsUpgradable: Razor Tooth's test), as the plays before left it.</summary>
    CardUpgradable,
    /// <summary>The card played's affliction is the class named in Of (cardPlay.Card.Affliction is Galvanized: SimCard.Affliction, by name).</summary>
    CardAffliction,
    /// <summary>The card played has the tag named in Of (its Tags contain Shiv).</summary>
    CardTag,
    /// <summary>The card played's cost as it's played (EnergyCost.GetResolved()).</summary>
    CardEnergy,
    /// <summary>A card event's card is its owner's (card.Owner == base.Owner; a made card's creator is base.Owner).</summary>
    EventIsOwners,
    /// <summary>The event's card is of the class named in Of (card is Sadness).</summary>
    CardIs,
    /// <summary>It's its owner's side's turn (CurrentSide == Side): the players', until the enemy turn starts.</summary>
    OwnersTurn,
    /// <summary>The power landing was applied by its owner (applier == base.Owner).</summary>
    ApplierIsOwner,
    /// <summary>The power landing is the one named in Of (power is VulnerablePower).</summary>
    PowerIs,
    /// <summary>The power landing is one of the classes listed in Of, comma-separated (power is ITemporaryPower: the classes that are).</summary>
    PowerIn,
    /// <summary>How much of the power landed (amount).</summary>
    PowerAmount,
    /// <summary>The power landing is a debuff for its amount (power.GetTypeForAmount(amount) == Debuff).</summary>
    PowerIsDebuff,
    /// <summary>The stars a spend took (AfterStarsSpent's amount): the stars of the play spending them, as the simulator spends stars (one event a play).</summary>
    StarsSpent,
    /// <summary>The power landed on an enemy (power.Owner.IsEnemy).</summary>
    PowerOnEnemy,
    /// <summary>The damage's dealer is the owner (dealer == base.Owner).</summary>
    DealerIsOwner,
    /// <summary>The damage's dealer is one of the owner's pets (base.Owner.Pets.Contains(dealer): their Osty).</summary>
    DealerIsOwnersPet,
    /// <summary>The damage's or block's target is the owner (target == base.Owner).</summary>
    TargetIsOwner,
    /// <summary>The damage is a powered attack (props.IsPoweredAttack()).</summary>
    PoweredAttack,
    /// <summary>The block is a powered card's or move's (props.IsPoweredCardOrMonsterMoveBlock()).</summary>
    PoweredBlock,
    /// <summary>The damage or block is a card's or a monster move's (props.IsCardOrMonsterMove(): its Move flag), powered or not.</summary>
    CardOrMonsterMove,
    /// <summary>The damage or block comes from a card (cardSource != null).</summary>
    FromCard,
    /// <summary>The damage's or block's card is the owner's (cardSource.Owner.Creature == base.Owner).</summary>
    CardIsOwners,
    /// <summary>The owner takes part in the side's turn ending or starting (participants.Contains(base.Owner)): a living player as the players' turn ends, a living enemy as the enemies' starts.</summary>
    TakesPart,
    /// <summary>
    /// The amount the model bound to the play in progress is taken (its own data's Remove(cardPlay.Card, out amount), asked once a hook run):
    /// 1 when one was bound, which is then gone; the amount is <see cref="BoundTaken"/>.
    /// </summary>
    TakeBound,
    /// <summary>The amount this hook run took from what the model bound to the play (Remove's out value; 0 when none was).</summary>
    BoundTaken,
    /// <summary>
    /// The model has a card play bound, left there, whichever (a card its code keeps in a field of its own: MusicBox's CardBeingPlayed != null).
    /// </summary>
    BoundHeld,
    /// <summary>
    /// The card the model keeps is the card played (MusicBox's cardPlay.Card == CardBeingPlayed): something is bound to this card play, not to one
    /// it's played inside (decision #267).
    /// </summary>
    BoundToPlay,
    /// <summary>The card play's count as the listeners before it left it (ModifyCardPlayCount's playCount).</summary>
    PlayCountSoFar,
    /// <summary>The HP a hit took past block (AfterDamageGiven's and AfterDamageReceived's result.UnblockedDamage).</summary>
    HitUnblocked,
    /// <summary>What a hit's block took (AfterDamageGiven's and AfterDamageReceived's result.BlockedDamage: Reflect's damage back).</summary>
    HitBlocked,
    /// <summary>
    /// The damage's card is the play the model bound (its own data's ContainsKey(cardSource): Rupture's, the card in play its BeforeCardPlayed bound),
    /// what it bound still there.
    /// </summary>
    SourceBound,
    /// <summary>The hook's card is there (AfterArrowShot's source: a shot a card makes).</summary>
    HookCardThere,
    /// <summary>The creature named in Creature is alive (creature.IsAlive).</summary>
    CreatureAlive,
    /// <summary>The owner's block (base.Owner.Creature.Block).</summary>
    OwnerBlock,
    /// <summary>The cards in the owner's hand (PileType.Hand.GetPile(base.Owner).Cards.Count; IsEmpty is this at 0).</summary>
    OwnerHandCount,
    /// <summary>The owner is dead (base.Owner.IsDead): a player who died, an enemy with no HP left.</summary>
    OwnerDead,
    /// <summary>The players' turn number this turn (base.Owner.PlayerCombatState.TurnNumber: SimState.TurnNumber).</summary>
    TurnNumber,
    /// <summary>The owner's orbs of the kind named in Of (OrbQueue.Orbs.Count(o => o is FrostOrb): an OrbKind's name).</summary>
    OwnerOrbCount,
    /// <summary>
    /// The creature that died is the one who applied this power (AfterDeath's creature == base.Applier): the power keeps its applier as its Data
    /// (DataCreature), from the adapter's read and from where it lands (ModelSpec.UsesApplier).
    /// </summary>
    DeadIsApplier,
    /// <summary>The creature that died is the owner (AfterDeath's creature == base.Owner).</summary>
    DeadIsOwner,
    /// <summary>The power has an applier (base.Applier isn't null): its Data names one (ModelSpec.UsesApplier).</summary>
    HasApplier,
    /// <summary>The power's applier is a player (base.Applier?.Player isn't null).</summary>
    ApplierIsPlayer,
    /// <summary>The card played is its applier's (cardPlay.Card.Owner == base.Applier.Player).</summary>
    PlayIsAppliers,
    /// <summary>The creature named in Creature is there (dealer != null, target != null).</summary>
    CreatureThere,
    /// <summary>The creature named in Creature is a player (creature.IsPlayer, creature.Player != null): a pet (Osty) isn't.</summary>
    CreatureIsPlayer,
    /// <summary>The creature named in Creature is a monster (creature.IsMonster): an enemy, or a pet (Osty).</summary>
    CreatureIsMonster,
    /// <summary>The creature named in Creature has the power named in Of (creature.HasPower&lt;T&gt;(), creature.GetPower&lt;T&gt;() != null).</summary>
    CreatureHasPower,
    /// <summary>The creature named in Creature is a player with the relic named in Of (creature.Player?.GetRelic&lt;T&gt;() != null).</summary>
    CreatureHasRelic,
    /// <summary>The amount of the power named in Of on the creature named in Creature (a called power's base.Amount: Cruelty's).</summary>
    CreaturePowerAmount,
    /// <summary>The creatures named in Creature and Other are the same one (target == base.Owner of a model called on another creature).</summary>
    SameCreature,
    /// <summary>The creatures named in Creature and Other are on the same side (creature.Side == base.Owner.Side).</summary>
    SameSide,
    /// <summary>The creature named in Creature is on the players' side (its Side is the side whose turn ends: the simulator ends the players').</summary>
    CreatureOnPlayersSide,
    /// <summary>It's the enemies' turn (CombatState.CurrentSide == CombatSide.Enemy): from the enemy turn's start.</summary>
    EnemiesTurn,
    /// <summary>
    /// The power named in Of summed over the owner's living opponents (Sum(Where(CombatState.GetOpponentsOf(base.Owner), c =&gt; c.IsAlive),
    /// a =&gt; a.GetPowerAmount&lt;T&gt;()): PoisonPower.TriggerCount's Accelerant): the living players for an enemy, the living enemies for a player.
    /// </summary>
    OpponentsPowerAmount,
    /// <summary>The card a damage or block comes from is the class named in Of (`cardSource is SovereignBlade`: Conqueror's).</summary>
    SourceCardIs,
    /// <summary>What the hit dealt, its block included (result.TotalDamage: Mighty Wings' threshold, Reaper Form's Doom).</summary>
    HitTotal,
    /// <summary>
    /// The card exhausted is an Ethereal card its owner's turn end exhausts (AfterCardExhausted's causedByEthereal: Dark Embrace and Joss Paper count
    /// it, to act as the turn ends), not one a play exhausts: SimState.EtherealNow.
    /// </summary>
    CausedByEthereal,
    /// <summary>The HP the hit would take as the HP-loss pipeline reaches this pass (Hook.ModifyHpLost*'s amount: the passes before it applied).</summary>
    HpLostSoFar,
    /// <summary>The orb a hook is given is its owner's (orb.Owner == base.Owner, base.Owner.Player).</summary>
    OrbIsOwners,
    /// <summary>The orb a hook is given is of the kind named in Of (orb is LightningOrb: an OrbKind's name).</summary>
    OrbIs,
    /// <summary>The orb's value as the listeners before this one left it (ModifyOrbValue's value).</summary>
    OrbValueSoFar,
    /// <summary>An orb passive's trigger count as the listeners before this one left it (ModifyOrbPassiveTriggerCounts' triggerCount).</summary>
    TriggerCountSoFar,
    /// <summary>The orb is its owner's front orb (orb == base.Owner.PlayerCombatState.OrbQueue.Orbs[0]).</summary>
    OrbIsFront,
    /// <summary>The enemies that can be hit (CombatState.HittableEnemies.Count; a player's model's hittable opponents).</summary>
    HittableEnemyCount,
    /// <summary>The card played is the model's own card (cardPlay.Card == base.Card: an enchantment's card).</summary>
    PlayIsModelCard,
    /// <summary>Whom the model's own card is aimed at (base.Card.TargetType), as SimPlay.Target's number (an enchantment's card: Inky's every enemy).</summary>
    ModelCardTargets,
    /// <summary>
    /// The damage's or block's props have the flag named in Of (props.HasFlag(ValueProp.Unpowered): a SimProps name, the game's flag by its name),
    /// or an attack's (command.DamageProps.HasFlag(ValueProp.FromArrow)).
    /// </summary>
    PropsHave,
    /// <summary>The attack's id in this simulation (BeforeAttack's and AfterAttack's command: AttackInfo.Id), as a model keeps it (decision #228).</summary>
    AttackId,
    /// <summary>The id of the attack under way (SimState.CurrentAttack; 0 outside an attack): a Modify hook's damage is that attack's hit.</summary>
    CurrentAttack,
    /// <summary>The attack's card's identity (its HandId; 0 for none, or a card not read from a hand), as a model keeps it.</summary>
    AttackCardId,
    /// <summary>The identity of the card a damage comes from (cardSource: its HandId; 0 for none).</summary>
    SourceCardId,
    /// <summary>The attack is a card's (command.ModelSource is CardModel): the simulator's attacks are card plays', so whether it has a card.</summary>
    AttackFromCard,
    /// <summary>The id of a creature the code names (Creature), as a model keeps it (the attacker of the attack it keeps); Who.None for none.</summary>
    CreatureId,
    /// <summary>
    /// A creature the code names (Creature) is a player's pet, the one the simulator keeps (Who.YourOsty): its monster is the class Player.Osty gets
    /// (creature.Monster is Osty).
    /// </summary>
    CreatureIsPet,
    /// <summary>The card a damage or block comes from is being played (cardSource.Pile.Type == PileType.Play): the damage's card play is there.</summary>
    SourceInPlay,
    /// <summary>Which play of its card play the damage's card is in (cardSource.CurrentPlayIndex: 0 the first).</summary>
    SourcePlayIndex,
    /// <summary>The Modify hook is given a card play (cardPlay != null): the damage's or block's.</summary>
    FromPlay,
    /// <summary>One of the damage's card's own values, by its key in Of (heavyShot.DynamicVars["AimMultiplier"]: SimCard.Vars); 0 when it has none.</summary>
    SourceCardVar,
    /// <summary>The amount a power is applied with as PowerCmd.Apply scales it for the players (GetScaledAmountForMultiplayer's amount: PowerRule).</summary>
    ScaledAmount,
    /// <summary>The players in the fight (combatState.Players.Count): you and your teammates.</summary>
    PlayerCount,
    /// <summary>
    /// The fight's multiplayer scaling (MultiplayerScalingModel.GetMultiplayerScaling(encounter, act): SimState.EncounterScaling, read by the adapter
    /// from the game's own math).
    /// </summary>
    EncounterScaling,
    /// <summary>
    /// The stars the card played spent (cardPlay.Resources.StarsSpent): its card's stars (SimCard.Stars), the same on each play of its card play,
    /// as the game hands every play the one resources the card was paid with (CardModel.OnPlayWrapper).
    /// </summary>
    PlayStars,
    /// <summary>The play is its card play's last (cardPlay.IsLastInSeries: PlayIndex == PlayCount - 1).</summary>
    PlayIsLast,
    /// <summary>
    /// The HP of the creature named in Creature (creature.CurrentHp: Shriek's target.CurrentHp &lt;= base.Amount), 0 for one dead or gone; a player
    /// whose HP the simulation doesn't know makes it unknown.
    /// </summary>
    CreatureHp,
    /// <summary>
    /// The card played is the first its player started this turn of the plays named in Of ("" every play, "Type:Attack", "Tag:Cho"): the game's
    /// FirstOrDefault(CardPlaysStarted, e =&gt; this turn, its player's, ...)?.CardPlay == cardPlay (MelodyCmd.IsFirstPlayThisTurn), the play itself,
    /// as it started (SimCardPlay.FirstThisTurn).
    /// </summary>
    PlayIsFirst,
}

/// <summary>A creature a hook's code names: its model's owner, or one of what the hook was given (the dealer, the target, the dead...).</summary>
public enum HookWho
{
    /// <summary>The model's owner (base.Owner, base.Owner.Creature).</summary>
    Owner,
    /// <summary>The damage's dealer (dealer: a Modify hook's, a hit's).</summary>
    Dealer,
    /// <summary>The damage's or block's target (target: a Modify hook's, a hit's).</summary>
    Target,
    /// <summary>The creature that died (AfterDeath's creature).</summary>
    Dead,
    /// <summary>The creature the power landing landed on (power.Owner in AfterPowerAmountChanged).</summary>
    PowerOwner,
    /// <summary>Who applied this power (base.Applier, kept as its Data).</summary>
    Applier,
    /// <summary>No creature (a null the code passes: PowerCmd.Apply with no applier).</summary>
    NoOne,
    /// <summary>The item of the loop the step is in (a ForEachStep's: each player, each enemy).</summary>
    Each,
    /// <summary>The attack's attacker (BeforeAttack's and AfterAttack's command.Attacker).</summary>
    Attacker,
    /// <summary>The player whose card the attack is (command.ModelSource as a card, its Owner): the play's.</summary>
    AttackCardOwner,
    /// <summary>The creature a redirect is so far (ModifyPreBlockDamageTarget's target: the listeners before it left it; none at first).</summary>
    RedirectSoFar,
    /// <summary>The creature a Should hook asks about (ShouldAllowHitting's and ShouldCreatureBeRemovedFromCombatAfterDeath's creature).</summary>
    Asked,
}

/// <summary>A creature a hook's code names (HookWho), or the player who owns it when it's a pet (creature.PetOwner.Creature: Osty's player).</summary>
public sealed record HookCreature(HookWho Who)
{
    public bool PetOwner { get; init; }

    public override string ToString() => PetOwner ? Who + ".PetOwner" : Who.ToString();
}

/// <summary>
/// An operation of the code on two values (decimal throughout; a true-or-false is 1 or 0). Min and Max are Math.Min and Math.Max
/// (PoisonPower.TriggerCount's Min(Amount, 1 + Accelerant)).
/// </summary>
public enum HookOperation { Add, Subtract, Multiply, Divide, Remainder, Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual, And, Or, Min, Max }

/// <summary>A value a hook's code works out (an amount, a test), read from its instructions: decimal, a true-or-false is 1 or 0.</summary>
public abstract record HookValue;

/// <summary>A constant in the code.</summary>
public sealed record HookConstant(decimal Value) : HookValue;

/// <summary>What the hook's parameters or its model say (HookFact), with the class it names when it names one.</summary>
public sealed record HookFactValue(HookFact Fact) : HookValue
{
    public string? Of { get; init; }

    /// <summary>The creature a creature's fact is about (CreatureThere, CreatureHasPower, SameCreature...); null for the others.</summary>
    public HookCreature? Creature { get; init; }

    /// <summary>The other creature a comparison of two is about (SameCreature, SameSide).</summary>
    public HookCreature? Other { get; init; }
}

/// <summary>
/// One of the model's values (base.DynamicVars[Key]), as the adapter read it (SimEngine.Var); its canonical value (the class's CanonicalVars, read
/// from its code) when the state has none.
/// </summary>
public sealed record HookVar(string Key) : HookValue
{
    public decimal Canonical { get; init; }
}

/// <summary>
/// One of the model's own fields or properties (Kunai's AttacksPlayedThisTurn): simulator state (SimPlayer.Fields, SimEnemy.Fields), seeded from
/// the game and changed by its programs' stores.
/// </summary>
public sealed record HookField(string Field) : HookValue
{
    /// <summary>The prefix of one of a model's own values kept as its state ("Var.CardsLeft"): one its code stores, or each copy's own.</summary>
    public const string VarPrefix = "Var.";

    /// <summary>
    /// What it is before anything stores it (a new instance's: one of its own values its code stores, "Var.CardsLeft", starts at the canonical
    /// value its CanonicalVars give it; any other field at 0).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public decimal Canonical { get; init; }
}

/// <summary>
/// A question to the game's log of the fight (CombatManager.Instance.History: Count or Any of its entries, decision #191), as the history
/// ledger counts it for the owner's player (SimEngine.HistoryCount): its count, or 1 when there's any (Any). A count the ledger doesn't know
/// makes the simulation unknown, never 0.
/// </summary>
public sealed record HookHistory(HistoryQuery Query) : HookValue
{
    public bool Any { get; init; }
}

/// <summary>
/// An orb's value through the game's pipeline (OrbModel.ModifyOrbValue: Hook.ModifyOrbValue over the listeners, Focus and Infused Core), for the
/// orb whose value it is: its PassiveVal or EvokeVal getter's code (ModelSpec.OrbValues).
/// </summary>
public sealed record HookOrbValue(HookValue Base) : HookValue;

/// <summary>
/// One of the orb's values as the game shows them (its PassiveVal, its EvokeVal: SimOrb.Passive, SimOrb.Evoke), the pipeline's output, which the
/// engine works out again from the getter's code (ModelSpec.OrbValues) whenever what it reads changes.
/// </summary>
public sealed record HookOrbProperty(string Property) : HookValue;

/// <summary>A local the code keeps (a LetStep's), read after a store changed what it was worked out from (Glass's passiveVal, Joss Paper's count).</summary>
public sealed record HookLocal(string Name) : HookValue;

/// <summary>Two values the code combines or compares.</summary>
public sealed record HookMath(HookOperation Operation, HookValue Left, HookValue Right) : HookValue;

/// <summary>
/// One test the code makes at one place (<paramref name="At"/>: its method and offset), 1 when it holds: asked once a hook run, where a step first
/// needs it, as the game asks it once at its branch (MiniRegent's !UsedThisTurn, asked before the store that sets it, holds for the Strength after it).
/// </summary>
public sealed record HookTest(string At, HookValue Test) : HookValue;

/// <summary>Not: 1 when the value is 0.</summary>
public sealed record HookNot(HookValue Inner) : HookValue;

/// <summary>One way through a method to a value it returns: the value, when the way's test holds.</summary>
public sealed record HookWay(HookValue When, HookValue Value);

/// <summary>What a method returns: the value of the first of its ways whose test holds (each return of the code, in order).</summary>
public sealed record HookWays(IReadOnlyList<HookWay> Ways) : HookValue;

/// <summary>Who a hook's command acts on.</summary>
public enum HookTarget
{
    /// <summary>The model's owner (base.Owner, base.Owner.Creature, base.Owner.Player): damage to it, through the pipeline on it.</summary>
    Owner,
    /// <summary>Every enemy that can be hit (CombatState.HittableEnemies).</summary>
    AllEnemies,
    /// <summary>A random enemy that can be hit (a roll over HittableEnemies): a branch per enemy, never the roll.</summary>
    RandomEnemy,
    /// <summary>The creature the power landing landed on (power.Owner in AfterPowerAmountChanged).</summary>
    PowerOwner,
    /// <summary>The creature a hit landed on (AfterDamageGiven's and AfterDamageReceived's target).</summary>
    HitTarget,
    /// <summary>The enemy that can be hit with the least HP (MinBy(HittableEnemies, c => c.CurrentHp): the first such, as MinBy keeps it).</summary>
    WeakestEnemy,
    /// <summary>The living creatures an evoke acted on (AfterOrbEvoked's targets.Where(c => c.IsAlive)).</summary>
    EvokeTargets,
    /// <summary>The item of the loop the step is in (HookWho.Each: each player, each enemy).</summary>
    Each,
    /// <summary>The card play's target (cardPlay.Target: the enemy an enchanted card is aimed at).</summary>
    PlayTarget,
    /// <summary>
    /// Every living player creature, in player order, as the command starts (CombatState.PlayerCreatures.Where(c =&gt; c.IsAlive): Explosive's
    /// targets); each skipped once dead, as CreatureCmd.Damage skips a dead target.
    /// </summary>
    LivingPlayers,
    /// <summary>The player who played the card (cardPlay.Card.Owner.Creature: Galvanic's damage to whoever plays a Galvanized card).</summary>
    PlayOwner,
    /// <summary>
    /// The damage's dealer (BeforeDamageReceived's and a hit's dealer: Thorns, Flame Barrier and Reflect hit it back), whoever it is: a player, a
    /// player's pet (Osty), an enemy.
    /// </summary>
    Dealer,
}

/// <summary>One command of a hook's code, on the condition its code runs it on (null: always).</summary>
public abstract record HookStep
{
    public HookValue? When { get; init; }
}

/// <summary>CreatureCmd.GainBlock to the owner: its amount and props through the block pipeline (SimEngine.ProcBlock).</summary>
public sealed record GainBlockStep(HookValue Amount, HookValue Props) : HookStep
{
    /// <summary>Who gains it, when it isn't the owner (a loop's item: Frost's block for each other player with Hibernate). Null: the owner.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HookTarget? To { get; init; }
}

/// <summary>CreatureCmd.Damage to <paramref name="Target"/> through the damage pipeline, from the owner unless NoDealer.</summary>
public sealed record DamageStep(HookTarget Target, HookValue Amount, HookValue Props) : HookStep
{
    /// <summary>No dealer (the dealer argument is null).</summary>
    public bool NoDealer { get; init; }
}

/// <summary>CreatureCmd.Heal of the owner: its amount, up to its max HP (no modifiers: the game's heal has none).</summary>
public sealed record HealStep(HookValue Amount) : HookStep;

/// <summary>CardPileCmd.Draw for the owner: its count of cards, each a card event.</summary>
public sealed record DrawStep(HookValue Count) : HookStep;

/// <summary>PlayerCmd.GainEnergy for the owner.</summary>
public sealed record GainEnergyStep(HookValue Amount) : HookStep;

/// <summary>
/// PlayerCmd.GainStars for the owner: one stars-gained event (the game's one AfterStarsGained), as a card's GainStars is (SimAdapter.Steps); the
/// simulator keeps no count of stars.
/// </summary>
public sealed record GainStarsStep(HookValue Amount) : HookStep;

/// <summary>
/// Its own Flash() at its place in the code: logged in the flash log (SimState.Flashed), which the play check holds to the game's flashes;
/// the tabs read the acting log, so a model that acts without flashing keeps its tab (decision #199).
/// </summary>
public sealed record FlashStep : HookStep
{
    /// <summary>
    /// Why its condition isn't read (a test the reader can't translate), when it isn't: it may flash or not as the hook runs, which only the
    /// play check asks (an unknown flash, logged apart: SimKeys.UnknownFlash), so the rest of the body still reads. Null: read, on When.
    /// </summary>
    public string? Unknown { get; init; }
}

/// <summary>PowerCmd.Apply of <paramref name="Power"/> on <paramref name="Target"/>, applied by the owner.</summary>
public sealed record ApplyPowerStep(string Power, HookTarget Target, HookValue Amount) : HookStep
{
    /// <summary>
    /// Its own amount changed (PowerCmd.ModifyAmount of itself): once at 0 or less it goes, after its listeners heard the change (PowerModel.
    /// ShouldRemoveDueToAmount: a power that can't go below 0). PowerCmd.Apply's own landing leaves that to the engine.
    /// </summary>
    public bool GoesAtZero { get; init; }

    /// <summary>It lands as a debuff (Artifact takes it): the power's type for its amount, as the adapter read it.</summary>
    public bool Debuff { get; init; }

    /// <summary>
    /// It lands as a debuff when its amount, worked out as it runs, is below 0 (a power that can go below 0, its AllowNegative: Monologue's
    /// Strength taken back, -StrengthApplied; power.GetTypeForAmount).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool DebuffWhenNegative { get; init; }

    /// <summary>
    /// Who applies it (PowerCmd.Apply's applier): the power's applier it keeps (base.Applier: Oblivion's Doom, Tender's Strength), no one (a
    /// null), or (null here) its owner.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public HookCreature? By { get; init; }
}

/// <summary>A store to one of the model's own fields or properties a program reads (this.AttacksPlayedThisTurn = ... + 1).</summary>
public sealed record StoreFieldStep(string Field, HookValue Value) : HookStep;

/// <summary>
/// A value bound to the play in progress, in the model's own data (amountsForPlayedCards.Add(cardPlay.Card, base.Amount) at BeforeCardPlayed):
/// what its AfterCardPlayed takes back (HookFact.TakeBound), so a power the play itself applies doesn't act on that play.
/// </summary>
public sealed record BindStep(HookValue Value) : HookStep;

/// <summary>What the model bound to the play in progress, taken (a Remove(cardPlay.Card, out amount) made for its out value: HookFact.BoundTaken).</summary>
public sealed record UnbindStep : HookStep;

/// <summary>A counter loop (for i &lt; Times): its commands, in order, Times times; a firing logged on the first round only (the game's one Flash).</summary>
public sealed record RepeatStep(HookValue Times, IReadOnlyList<HookStep> Steps) : HookStep;

/// <summary>
/// A local the code works out and keeps (int count = CardsExhausted) before a store changes what it read: worked out here, read after (HookLocal)
/// as it was then.
/// </summary>
public sealed record LetStep(string Name, HookValue Value) : HookStep;

/// <summary>A loop over items (foreach): its steps once for each item, in order, the item HookWho.Each (HookTarget.Each) inside it.</summary>
public sealed record ForEachStep(HookItems Over, IReadOnlyList<HookStep> Steps) : HookStep
{
    /// <summary>
    /// The loop's filter (Where(items, c => ...): Covering Fire's living players other than its owner), asked of each item as the loop reaches it, the
    /// item HookWho.Each; null: every item.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HookValue? Where { get; init; }
}

/// <summary>
/// CreatureCmd.Stun of its owner (an enemy: Creature.StunInternal does nothing on a dead creature, and the game throws on a player): it's stunned
/// (SimEngine.Stun), so its move this turn is the stunned move, which does nothing a simulation follows (its code is read: the Stun is given no
/// delegate that calls a command). What its code then stores on its monster for later (its flag) waits for a turn past every simulation.
/// </summary>
public sealed record StunStep : HookStep;

/// <summary>
/// CreatureCmd.Kill of its owner: an enemy dies (its HP 0, its death told once the hit it's in is over, as every death the engine follows) and is
/// marked killed by its power (SimEnemy.KilledByItsPower: The Gambit's tab); a player loses all their HP (Creature.LoseHpInternal) and dies unless
/// Hook.ShouldDie holds it back (Fairy in a Bottle, Lizard Tail), which <paramref name="Forced"/> (the command's force) skips: SimEngine.KillPlayer.
/// </summary>
public sealed record KillStep(bool Forced = false) : HookStep;

/// <summary>
/// More on what the model bound to the play in progress (its own data's playedCards[cardSource] += Value, under the damage's card, the bound one:
/// Rupture's Strength from a card's own HP cost, given as the card is played): taken back with it (HookFact.TakeBound, BoundTaken).
/// </summary>
public sealed record BindAddStep(HookValue Value) : HookStep;

/// <summary>OrbCmd.Channel&lt;T&gt; for the owner: one orb of the kind named (SimEngine.ChannelKind).</summary>
public sealed record ChannelStep(OrbKind Orb) : HookStep;

/// <summary>
/// CardCmd.Upgrade of the card played (cardPlay.Card): a card that can still be upgraded is, so its later plays in this card play are its
/// upgraded copy's (SimEngine.UpgradeCardInPlay, decision #218).
/// </summary>
public sealed record UpgradeCardStep : HookStep;

/// <summary>
/// CardCmd.Upgrade of <paramref name="Count"/> random upgradable cards in its owner's hand (foreach over TakeRandom(the hand's upgradable cards,
/// Count): TunePower.AfterCardPlayed, v0.114.0). Within the turn only a card acting as it ends could change (SimPlayer.UpgradableAtTurnEnd): with
/// none, nothing; with any, which ones a roll picks isn't followed (SimState.Unmodelled).
/// </summary>
public sealed record UpgradeRandomInHandStep(HookValue Count) : HookStep;

/// <summary>
/// CardCmd.ApplyKeyword of the card played (cardPlay.Card), with the keyword named (the game's CardKeyword): it has it for its later plays in
/// this card play (SimEngine.KeywordCardInPlay, decision #218).
/// </summary>
public sealed record ApplyKeywordStep(string Keyword) : HookStep;

/// <summary>
/// A card in the owner's hand made free this turn (CardModel.SetToFreeThisTurn on the game's random pick from the hand: Mummified Hand's
/// NextItem over the cards that cost energy or stars, else the rest), as the card played's own play hook runs it (SimEngine.FreeInHand).
/// </summary>
public sealed record FreeInHandStep : HookStep;

/// <summary>
/// CardPileCmd.AddGeneratedCardToCombat of a copy of the card played into its owner's hand (cardPlay.Card.CreateClone(), Ethereal when its code
/// applies it to the copy first: Music Box's): a card made, joining the hand, its event's card the card played's facts with those keywords.
/// </summary>
/// <param name="OfEventCard">A copy of the card event's card instead (AfterCardExhausted's card.CreateClone(): Burning Sticks').</param>
public sealed record MakeCopyStep(bool Ethereal, bool OfEventCard = false) : HookStep;
/// <summary>
/// CardPileCmd.AddGeneratedCardToCombat(CombatState.CreateCard&lt;Card&gt;(player), PileType.Draw, creator, CardPilePosition.Random): a card of a class
/// its code names made into <paramref name="For"/>'s draw pile at a random spot (SimEngine.IntoDrawPile: a later draw can find it), then
/// Hook.AfterCardGeneratedForCombat told of it (its class, type and tags, the canonical card's), its creator the owner or none
/// (<paramref name="ByOwner"/>: a null creator, Personal Hive's, is no one's, so no created-card hook of anyone's hears it). Inside a hit it waits
/// for the hit (SimState.PendingIntoDraw). Biiig Hug's Soot, Personal Hive's Dazed.
/// </summary>
/// <param name="For">Whose card and pile it is: a creature id (the owner, the hit's dealer or its pet's player).</param>
public sealed record MakeIntoDrawStep(string Card, SimCardType Type, IReadOnlyList<SimCardTag> Tags, HookValue For, bool ByOwner) : HookStep
{
    /// <summary>A card against the player whose pile it goes into (a Status or a Curse: Dazed, Soot, Wound).</summary>
    public bool Harms => Type is SimCardType.Status or SimCardType.Curse;
}

/// <summary>
/// OstyCmd.Summon(choiceContext, summoner, amount, this): <paramref name="Amount"/> summoned for <paramref name="Summoner"/>, as SimEngine.Summon runs
/// the command: its owner (a relic's base.Owner), the player who played the card (cardPlay.Card.Owner: Devour Life), or the hit's dealer's pet owner
/// (<see cref="PetOwner"/>: dealer.PetOwner, Sic 'Em's Osty's player).
/// </summary>
public sealed record SummonStep(HookTarget Summoner, HookValue Amount) : HookStep
{
    /// <summary>The summoner is the pet owner of the creature <see cref="Summoner"/> names (the dealer's player when the dealer is their Osty).</summary>
    public bool PetOwner { get; init; }
}

/// <summary>PowerCmd.Remove(this): the power itself goes from its owner.</summary>
public sealed record RemovePowerStep : HookStep;

/// <summary>PowerCmd.Decrement(this), PowerCmd.TickDownDuration(this): the power itself goes down by one, gone at 0.</summary>
public sealed record DecrementPowerStep : HookStep;

/// <summary>One hook of a model, as its code runs it: its commands in order, or for a Modify hook the value it returns.</summary>
public sealed record HookProgram(HookName Hook, IReadOnlyList<HookStep> Steps)
{
    /// <summary>A Modify hook's value (null for a hook that runs commands).</summary>
    public HookValue? Returns { get; init; }
}

/// <summary>
/// A tab judged on how the turn ends (SimModel.OutcomeTab), read from what a model's AfterDeath waits on (HookReader.OutcomeOf): its owner's death
/// (an enemy's: a warning on a play that can kill it), another death on its owner's side (an enemy's: a warning on a play that kills another
/// enemy), a death on the other side (a player's: a bonus on a play that kills an enemy in every outcome).
/// </summary>
public enum HookOutcome
{
    KillsIt, KillsAnother, KillsOne,
    /// <summary>
    /// Its AfterDamageReceived acts on its owner losing HP (every step waits on target == base.Owner and result.UnblockedDamage &gt; 0) and gives
    /// nothing a tab counts (decision #132): on you, a bonus on a play you lose HP during in every outcome (Self-Forming Clay's block next turn);
    /// on an enemy, a warning on a play that makes it act in some outcome where ending the turn now wouldn't (Slippery's 1 HP a hit): decision #239.
    /// </summary>
    ReactsToHpLoss,
    /// <summary>
    /// It stuns its owner (a StunStep in a hook other than AfterDeath, which tells the deaths above first): on an enemy, a bonus on a play that
    /// leaves it stunned and alive for its turn in every outcome where ending the turn now wouldn't (OutcomeRules.Stuns: Shriek, Flutter).
    /// </summary>
    Stuns,
    /// <summary>
    /// It kills its owner (a KillStep in a hook a play sets off): on an enemy, a bonus on a play that makes it die so in every outcome where ending
    /// the turn now wouldn't (The Gambit's, as a powered attack gets HP through).
    /// </summary>
    KillsItself,
}

/// <summary>
/// What a model pays out after the play, on a condition its code tests (decision #239): a command that gives (block, damage to the enemies, energy,
/// cards, an orb, a heal, a buff on its owner) in a hook of the players' turn's end or of their next turn's start, and the test it waits on.
/// <paramref name="Hook"/>: the game's hook it's in; <paramref name="Condition"/>: its test as the code reads it there.
/// </summary>
public sealed record Payout(string Hook, HookValue Condition)
{
    /// <summary>It's in a hook of the players' next turn's start (AfterEnergyReset, AfterPlayerTurnStart...), past every simulation: never run.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool NextTurn { get; init; }

    /// <summary>
    /// Its condition as it stands during your turn (<see cref="Condition"/> with what the turn's end stores before the payout put in its place:
    /// Art of War's AnyAttacksPlayedLastTurn is its AnyAttacksPlayedThisTurn), when what it reads only a play changes (the model's own state its play
    /// hooks store, the fight's log of card plays): decided by the play, so a play that turns it off gives up the bonus there and then. Null:
    /// decided where it's paid (Parrying Shield's block as your turn ends).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HookValue? DuringTheTurn { get; init; }
}

/// <summary>
/// Whether a model is a power (on a creature: its PowerSet entry), a relic (on a player: its RelicSet entry) or an orb (in a player's orb slots: a
/// SimOrb, its programs run where OrbCmd and OrbQueue run them, never a listener).
/// </summary>
public enum ModelKind
{
    Power, Relic, Orb,
    /// <summary>
    /// An enchantment on a card (SimCard.Enchantment): its OnPlay run by the engine after each play of its card, its AfterCardPlayed after the
    /// play's listeners, never a listener of its own (decision #217).
    /// </summary>
    Enchantment,
    /// <summary>
    /// An affliction on a card (SimCard.Affliction): a fact of its card the powers' code tests; read whole, it has nothing the engine runs (decision #225),
    /// and one that doesn't read whole makes its card's plays unknown.
    /// </summary>
    Affliction,
}

/// <summary>
/// A power or a relic as its code says (decision #195): its hooks' programs, read from the game's code by the adapter
/// (SimAdapter.HookSpecs), and its tab, from what its commands give. Pure data: the same in the game and in the tests (written as JSON).
/// </summary>
public sealed record ModelSpec(string Name, ModelKind Kind, IReadOnlyList<HookProgram> Hooks)
{
    /// <summary>Its tab, from its commands (block, damage, draws, energy; a trigger otherwise): the hand-written models' rule.</summary>
    public TabKind? Tab { get; init; }

    /// <summary>
    /// A play anyone makes, not only its owner, sets off something that helps its owner (block, a buff on itself): read from its programs
    /// (HookReader.HelpsOwnerOf). Its tab is crossed where that owner is on the other side from the player whose card plays (an enemy's
    /// Enrage), plain on yours (a teammate's Sneaky): ReadModel.HarmfulFor (decision #198).
    /// </summary>
    public bool HelpsOwner { get; init; }

    /// <summary>
    /// A play sets off something that hurts its owner: a debuff on itself or damage to it (HookReader.HurtsOwnerOf). Its tab is crossed where that
    /// owner is a player (Tender), plain on an enemy: ReadModel.HarmfulFor.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HurtsOwner { get; init; }

    /// <summary>
    /// A play sets off something that hurts the player who made it: damage or a debuff on the card's owner (HookReader.HurtsPlayOwnerOf: Galvanic).
    /// Its tab is crossed whoever owns it: the play hurts you (ReadModel.HarmfulFor).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HurtsPlayOwner { get; init; }

    /// <summary>Its tab is judged on how the turn ends, by what its AfterDeath waits on (HookReader.OutcomeOf); it has no play tab then. Null: none.</summary>
    public HookOutcome? Outcome { get; init; }

    /// <summary>
    /// What it pays out after the play and on what (decision #239): its tab says a play gives the bonus up or sets it up. A payout decided by the
    /// play (Payout.DuringTheTurn) is a crossed play tab, logged as the play turns it off; one decided where it's paid is a tab judged on how
    /// the turn ends. Null: none (or no settings row to show its tab: ReadModels.Specs).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<Payout>? Payouts { get; init; }

    /// <summary>
    /// A play hook of its code sets its Status to Normal (the relic going dark: the game's own sign that the play gave its bonus up, Art of War's
    /// and Ripple Basin's AfterCardPlayed): the play check holds its crossed tab to that (ActedSeen.StatusCleared).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool DarkensOnPlay { get; init; }

    /// <summary>
    /// Its damage term changing a damage is it acting (decision #247: Conqueror's ×2 on a Sovereign Blade's hit on its owner): a class whose own
    /// commands give it no tab but whose Modify damage hook returns a term, which the game tells it changed a damage (Hook.AfterModifyingDamageAmount
    /// on the modifiers) where its code answers nothing there. Its trigger tab is logged then. False with no settings row (ReadModels.Specs).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool TermActs { get; init; }

    /// <summary>
    /// An enchantment whose ModifyShuffleOrder moves its card to the front of the cards a shuffle puts into the draw pile (Perfect Fit's
    /// Remove(base.Card), Insert(0, base.Card), on a shuffle that isn't the fight's first): its card is on top after a shuffle (SimDrawCard.OnTopWhenShuffled).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool OnTopWhenShuffled { get; init; }

    /// <summary>
    /// How its applications are kept (a power's InstanceType): each copy, with its own amount, its own state and its applier, runs its hooks on
    /// its own, in the order they landed (ReadModel). One: none of its own.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public PowerCopies Copies { get; init; }

    /// <summary>
    /// Its own commands another class calls, each read as a program (PoisonPower.Trigger: Outbreak's card calls it on each enemy's poison, a
    /// ModelCommandAction): run where they're called, never as hooks. Null: none read.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CommandProgram>? Commands { get; init; }

    /// <summary>An orb's kind (the engine's name for its class, OrbKind); null for a power or a relic.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrbKind? Orb { get; init; }

    /// <summary>When an orb's passive goes off (the trigger hook its class overrides with TriggerPassive); null: never.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrbTrigger? PassiveAt { get; init; }

    /// <summary>
    /// An orb's values (PassiveVal, EvokeVal), each its getter's code: what SimOrb.Passive and Evoke are worked out from. A value whose getter is one
    /// of its own fields (Dark's EvokeVal is _evokeVal) is that field: <see cref="OrbAliases"/>, and isn't here.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, HookValue>? OrbValues { get; init; }

    /// <summary>An orb's own fields that are one of its values (Dark's _evokeVal is its EvokeVal): a store to the field stores the value.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? OrbAliases { get; init; }

    /// <summary>An orb's own fields as a new one starts (its field initializers: Glass's _passiveVal 4, Dark's _evokeVal 6).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, decimal>? Starts { get; init; }

    /// <summary>
    /// Why its hooks weren't read, for a class whose hooks read whole at the reviewed builds and no longer do (a game update): no program, and
    /// every simulation it's in is unknown (SimState.Unmodelled, decision #92). Null: read.
    /// </summary>
    public string? Unread { get; init; }

    /// <summary>Every value its programs work out (each condition, amount and return, with the values inside them).</summary>
    public IEnumerable<HookValue> Values()
    {
        static IEnumerable<HookValue> Inside(HookValue? value) => value switch
        {
            null => Array.Empty<HookValue>(),
            HookMath math => new[] { value }.Concat(Inside(math.Left)).Concat(Inside(math.Right)),
            HookNot not => new[] { value }.Concat(Inside(not.Inner)),
            HookTest test => new[] { value }.Concat(Inside(test.Test)),
            HookWays ways => new[] { value }.Concat(ways.Ways.SelectMany(way => Inside(way.When).Concat(Inside(way.Value)))),
            HookOrbValue orbValue => new[] { value }.Concat(Inside(orbValue.Base)),
            _ => new[] { value },
        };
        static IEnumerable<HookValue> OfStep(HookStep step) => Inside(step.When).Concat(step switch
        {
            GainBlockStep gain => Inside(gain.Amount).Concat(Inside(gain.Props)),
            DamageStep damage => Inside(damage.Amount).Concat(Inside(damage.Props)),
            DrawStep draw => Inside(draw.Count),
            GainEnergyStep energy => Inside(energy.Amount),
            GainStarsStep stars => Inside(stars.Amount),
            HealStep heal => Inside(heal.Amount),
            SummonStep summon => Inside(summon.Amount),
            ApplyPowerStep apply => Inside(apply.Amount),
            StoreFieldStep store => Inside(store.Value),
            BindStep bind => Inside(bind.Value),
            BindAddStep added => Inside(added.Value),
            RepeatStep repeat => Inside(repeat.Times).Concat(repeat.Steps.SelectMany(OfStep)),
            ForEachStep each => Inside(each.Where).Concat(each.Steps.SelectMany(OfStep)),
            LetStep let => Inside(let.Value),
            MakeIntoDrawStep make => Inside(make.For),
            UpgradeRandomInHandStep upgrade => Inside(upgrade.Count),
            _ => Array.Empty<HookValue>(),
        });
        return Hooks.SelectMany(program => Inside(program.Returns).Concat(program.Steps.SelectMany(OfStep)))
            .Concat((Commands ?? Array.Empty<CommandProgram>()).SelectMany(command => command.Steps.SelectMany(OfStep)))
            .Concat((OrbValues ?? new Dictionary<string, HookValue>()).Values.SelectMany(Inside))
            .Concat((Payouts ?? Array.Empty<Payout>()).SelectMany(payout => Inside(payout.Condition).Concat(Inside(payout.DuringTheTurn))));
    }

    /// <summary>Every step of its programs, a counter loop's included.</summary>
    public IEnumerable<HookStep> Steps()
    {
        static IEnumerable<HookStep> All(HookStep step) => step switch
        {
            RepeatStep repeat => new[] { step }.Concat(repeat.Steps.SelectMany(All)),
            ForEachStep each => new[] { step }.Concat(each.Steps.SelectMany(All)),
            _ => new[] { step },
        };
        return Hooks.SelectMany(program => program.Steps).Concat((Commands ?? Array.Empty<CommandProgram>()).SelectMany(command => command.Steps)).SelectMany(All);
    }

    /// <summary>Its code asks who applied it (base.Applier): it keeps its applier as its Data (DataCreature), read and landed so.</summary>
    public bool UsesApplier() => Values().Any(value => value is HookFactValue { Fact: HookFact.DeadIsApplier or HookFact.HasApplier or HookFact.ApplierIsPlayer or HookFact.PlayIsAppliers }
        || value is HookFactValue fact && (fact.Creature?.Who == HookWho.Applier || fact.Other?.Who == HookWho.Applier))
        || Steps().Any(step => step is ApplyPowerStep { By.Who: HookWho.Applier });

    /// <summary>Its own fields and properties its programs read or store (the adapter seeds them from the game: SimReaders.Fields).</summary>
    public IEnumerable<string> Fields() =>
        Values().OfType<HookField>().Select(field => field.Field).Concat(Steps().OfType<StoreFieldStep>().Select(store => store.Field))
            .Distinct(StringComparer.Ordinal);

    /// <summary>The program of a hook, or null when it doesn't override it.</summary>
    public HookProgram? Of(HookName hook)
    {
        foreach (var program in Hooks) if (program.Hook == hook) return program;
        return null;
    }
}

/// <summary>
/// What PowerCmd does with a power class's amounts, read from its code for every power class (decision #234), whatever models it: whether it can go
/// below 0 (PowerModel.AllowNegative: ShouldRemoveDueToAmount takes it at 0, else at 0 or less), and its amount as PowerCmd.Apply scales a new one
/// on an enemy for the players (GetScaledAmountForMultiplayer, when its ShouldScaleInMultiplayer is true). Pure data: the same in the game and in
/// the tests (ReadModels/power-rules.json). Only a class that differs from PowerModel's own is listed: one not listed can't go below 0 and isn't scaled.
/// </summary>
public sealed record PowerRule(string Name)
{
    /// <summary>It can go below 0 (Strength, Dexterity, Focus): gone at exactly 0.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AllowNegative { get; init; }

    /// <summary>
    /// Its amount as PowerCmd.Apply scales a new one on an enemy in a fight of more than one player (GetScaledAmountForMultiplayer's returned value:
    /// HookFact.ScaledAmount, PlayerCount, EncounterScaling). Null: PowerCmd.Apply never scales it.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HookValue? ScaledForPlayers { get; init; }

    /// <summary>Why its scaling couldn't be read (a game update): a new one on an enemy in multiplayer makes the simulation unknown. Null: read.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Unread { get; init; }
}

/// <summary>How hook programs are written as JSON (the simulator tests' read models: Cases/read-models.json).</summary>
public static class HookProgramJson
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver { Modifiers = { ByKind } },
    };

    private static void ByKind(System.Text.Json.Serialization.Metadata.JsonTypeInfo typeInfo)
    {
        var kinds = typeInfo.Type == typeof(HookValue) || typeInfo.Type == typeof(HookStep)
            ? typeof(HookValue).Assembly.GetTypes().Where(type => !type.IsAbstract && typeInfo.Type.IsAssignableFrom(type)).OrderBy(type => type.Name, StringComparer.Ordinal).ToList()
            : null;
        if (kinds == null) return;
        typeInfo.PolymorphismOptions = new System.Text.Json.Serialization.Metadata.JsonPolymorphismOptions
        {
            TypeDiscriminatorPropertyName = "$kind",
            UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
        };
        foreach (var kind in kinds) typeInfo.PolymorphismOptions.DerivedTypes.Add(new System.Text.Json.Serialization.Metadata.JsonDerivedType(kind, kind.Name));
    }

    public static string Write(IEnumerable<ModelSpec> specs) => System.Text.Json.JsonSerializer.Serialize(specs.OrderBy(spec => spec.Name, StringComparer.Ordinal).ToList(), Options);

    public static List<ModelSpec> Read(string json) => System.Text.Json.JsonSerializer.Deserialize<List<ModelSpec>>(json, Options) ?? new List<ModelSpec>();

    public static string WriteRules(IEnumerable<PowerRule> rules) => System.Text.Json.JsonSerializer.Serialize(rules.OrderBy(rule => rule.Name, StringComparer.Ordinal).ToList(), Options);

    public static List<PowerRule> ReadRules(string json) => System.Text.Json.JsonSerializer.Deserialize<List<PowerRule>>(json, Options) ?? new List<PowerRule>();
}
