using System.Text.Json.Serialization;

namespace SpireQoL.Sim;

/// <summary>What a simulator condition tests (decision #190): each one a test the game's code makes, asked of the simulator's own state.</summary>
public enum SimTest
{
    /// <summary>The play's last attack killed a creature it hit (its DamageResults' WasTargetKilled: Sunder, Knockout Blow, Timber).</summary>
    AttackKilled,
    /// <summary>The play owner's Osty is missing or dead (Osty.CheckMissingWithAnim; IsOstyAlive is its negation).</summary>
    OstyMissing,
    /// <summary>The play's target is impaled by anyone's arrow, or by the play owner's (ArrowCmd.CreatureIsImpaledByArrow, ...ByPlayersArrow).</summary>
    TargetImpaled,
    /// <summary>A count on the state compared with a number (your hand's cards, the ones a filter lets through, plays finished this turn).</summary>
    Count,
    /// <summary>Another condition doesn't hold.</summary>
    Not,
    /// <summary>Every one of several conditions holds.</summary>
    All,
    /// <summary>At least one of several conditions holds.</summary>
    Any,
    /// <summary>The play's target intends to attack (MonsterModel.IntendsToAttack), and isn't stunned since.</summary>
    TargetIntendsToAttack,
    /// <summary>The play's last shot had an arrow to shoot at a living target (ArrowCmd.ShootArrowAtTarget's and TryShootArrow's result).</summary>
    ShotHadArrow,
    /// <summary>
    /// A test decided when it was read (KnownCondition: a monster's own flag or counter the simulator doesn't follow, the slot it stands in),
    /// as it stood then.
    /// </summary>
    Known,
    /// <summary>
    /// The play's target's death sets off Fatal: every power on it answers PowerModel.ShouldOwnerDeathTriggerFatal true (The Hunt's reward),
    /// its powers' answer as the adapter read it (SimEnemy.DeathSetsOffFatal); unknown where one's couldn't be read.
    /// </summary>
    TargetDeathSetsOffFatal,
    /// <summary>
    /// A holder has a copy of a power aimed at a player (decision #322: Imitation Learning's power aimed at the play's target, Frantic Escape's enemy
    /// Sandpit aimed at its player), each copy's own as the simulator keeps it (SimEngine.AimedAmount).
    /// </summary>
    AimedCopy,
}

/// <summary>Whose copies of a power a play asks about or changes (decision #322).</summary>
public enum AimHolder
{
    /// <summary>The play's owner (base.Owner.Creature).</summary>
    PlayOwner,
    /// <summary>The first enemy that has a power (CombatState.Enemies.FirstOrDefault(c => c.HasPower&lt;T&gt;()): Frantic Escape's Sandpit enemy).</summary>
    FirstEnemyWithPower,
}

/// <summary>The player a copy of a power is aimed at, as a play names them (decision #322).</summary>
public enum AimedPlayer
{
    /// <summary>The play's owner (base.Owner, base.Owner.Creature).</summary>
    PlayOwner,
    /// <summary>The play's target, a player (cardPlay.Target.Player, cardPlay.Target).</summary>
    PlayTarget,
}

/// <summary>How a counted condition compares its count with its number.</summary>
public enum SimCompare { Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual }

/// <summary>What a counted condition counts, on the state when it's asked.</summary>
public enum SimCounted
{
    /// <summary>The cards in the play owner's hand (the played card has left it: it's in play while its OnPlay runs).</summary>
    HandCards,
    /// <summary>
    /// Of those, the ones a filter of the card's lets through (Impatience's Attacks): set as the card play starts and followed as cards leave
    /// and join the hand (SimState.TakeMatching, as a filtered take's are); not known once a card joined whose kind wasn't read.
    /// </summary>
    HandMatching,
    /// <summary>The card plays the play owner finished this turn, any card (their turn counts: FTL's CanDrawCard).</summary>
    PlaysFinishedThisTurn,
    /// <summary>This card's own plays finished this turn: the ones before the card play, then each play of a replay (Fetch's HasBeenPlayedThisTurn).</summary>
    CardPlaysFinished,
    /// <summary>The arrows the play owner can shoot now (ArrowCmd.PlayerHasAnyArrows: SimPlayer.Arrows, which shots spend and dislodges give back).</summary>
    ArrowsAvailable,
    /// <summary>
    /// The play owner's count of a query of the game's log of the fight (decision #191: the history ledger, CountCondition.Query): an exhaust
    /// this turn (Forgotten Ritual, Evil Eye), HP lost this turn (Spite), Doom applied this turn (Death's Door), Osty's attacks (Flatten).
    /// </summary>
    History,
    /// <summary>The cards in the play owner's exhaust pile (CardPile.GetCards(Owner, Exhaust): Pact's End).</summary>
    ExhaustPile,
    /// <summary>The play's target's amount of a power (CountCondition.Power: HasPower, GetPowerAmount), 0 when it's dead or there's none.</summary>
    TargetPower,
    /// <summary>The play's target's HP (Creature.CurrentHp; IsAlive is above 0, IsDead 0 or less), 0 when it's dead or gone.</summary>
    TargetHp,
    /// <summary>The play owner's amount of a power (CountCondition.Power: their HasPower, GetPowerAmount: Cho Ki's Stretch).</summary>
    OwnerPower,
    /// <summary>The HP of the monster whose code asks (decision #195: a conditional move branch's CurrentHp), 0 when it's dead.</summary>
    MonsterHp,
    /// <summary>The asking monster's amount of a power (CountCondition.Power: its HasPower: Lagavulin Matriarch's Asleep), 0 when it's dead.</summary>
    MonsterPower,
    /// <summary>
    /// The living creatures of the asking monster's side (its GetTeammatesOf counted by IsAlive: Fabricator's room to fabricate), without it
    /// when CountCondition.ExcludingItself (Living Shield's allies).
    /// </summary>
    MonsterSideAlive,
    /// <summary>
    /// The asking monster's own state (CountCondition.Field: "Monster.BowlbugRock.IsOffBalance", a flag 1 or 0), in its fields (SimEnemy.Fields), seeded
    /// from the game's monster and set by a power's code on its owner's monster (a MonsterStateStep: Imbalanced's); 0 when it's dead.
    /// </summary>
    MonsterState,
    /// <summary>
    /// A number of the asking monster's own that its moves count (decision #411: CountCondition.Field, "Monster.TwoTailedRat.TurnsUntilSummonable",
    /// which each of its moves counts down), in its fields: seeded from the game's monster, each of its moves' readable stores followed in a
    /// projection (MoveStoreAction), one it can't read forgetting it. Not there: not known.
    /// </summary>
    MonsterCounter,
    /// <summary>
    /// The encounter's slots no living enemy stands in (EncounterModel.GetNextSlot, empty when there's none: Two-Tailed Rat's room to call for
    /// backup): SimState.EncounterSlots less the living enemies with SimEnemy.InSlot; not known once a creature joined whose slot isn't read.
    /// </summary>
    FreeSlots,
    /// <summary>
    /// The asking monster's living teammates (without it when CountCondition.ExcludingItself) whose next move is CountCondition.Move (their
    /// Monster.NextMove.Id: SimEnemy.NextMoveId); not known while one of them has a next move that isn't (decision #411).
    /// </summary>
    SideNextMove,
}

/// <summary>
/// A condition the simulator asks of its own state when the action it guards runs (decision #190: conditions apart from effects): any
/// condition can guard any effect (WhenAction), so a card's combination of the two needs no code of its own. The adapter translates the
/// card's code's tests (ProgramCondition) into these, each translated once for every card.
/// </summary>
public abstract record SimCondition(SimTest Test)
{
    /// <summary>What it tests.</summary>
    [JsonIgnore] public SimTest Test { get; } = Test;

    /// <summary>
    /// Where the card's code asks it (its branch: ConditionLiteral.At), when it's one of the code's tests: asked there once a play, and
    /// that answer kept for the rest of the play (a later step under the same if, its else), as the code asks it once. Null: asked each time.
    /// </summary>
    public string? At { get; init; }
}

/// <summary>The play's last attack killed a creature it hit (DamageResult.WasTargetKilled over the attack's results).</summary>
public sealed record AttackKilledCondition() : SimCondition(SimTest.AttackKilled);

/// <summary>The play owner's Osty is missing or dead (Osty.CheckMissingWithAnim).</summary>
public sealed record OstyMissingCondition() : SimCondition(SimTest.OstyMissing);

/// <summary>The play's target intends to attack (MonsterModel.IntendsToAttack: Go for the Eyes), not stunned since.</summary>
public sealed record TargetIntendsToAttackCondition() : SimCondition(SimTest.TargetIntendsToAttack);

/// <summary>
/// The play's target's death sets off Fatal (cardPlay.Target.Powers.All(p => p.ShouldOwnerDeathTriggerFatal()): The Hunt's): SimEnemy.DeathSetsOffFatal;
/// unknown where its powers' code doesn't say (Reattach's), false for a target that isn't an enemy.
/// </summary>
public sealed record TargetDeathSetsOffFatalCondition() : SimCondition(SimTest.TargetDeathSetsOffFatal);

/// <summary>
/// The holder has a copy of <paramref name="Power"/> aimed at the player (decision #322: FirstOrDefault(OfType&lt;T&gt;(holder.Powers), s =&gt; s.Target ==
/// player), or s.PlayerTarget): SimEngine.AimedAmount above 0. No holder (no enemy with the power): false.
/// </summary>
public sealed record AimedCopyCondition(PowerId Power, AimHolder Holder, AimedPlayer Aim) : SimCondition(SimTest.AimedCopy)
{
    /// <summary>FirstEnemyWithPower: the power that enemy has (the holder's own power, Sandpit's, unless the code asks for another).</summary>
    public PowerId? HolderPower { get; init; }
}

/// <summary>The play's last shot had an arrow to shoot at a living target (ArrowCmd.ShootArrowAtTarget's result: Hobbling Shot's debuffs).</summary>
public sealed record ShotHadArrowCondition() : SimCondition(SimTest.ShotHadArrow);

/// <summary>The play's target is impaled: by anyone's arrow, or with <paramref name="ByOwner"/> by the play owner's.</summary>
public sealed record TargetImpaledCondition(bool ByOwner) : SimCondition(SimTest.TargetImpaled);

/// <summary>A count on the state (<paramref name="Counted"/>) compared with <paramref name="Than"/>.</summary>
public sealed record CountCondition(SimCounted Counted, SimCompare Compare, int Than) : SimCondition(SimTest.Count)
{
    /// <summary>HandMatching: the filter's name (the card and its test), followed through the card play in SimState.TakeMatching.</summary>
    public string? Filter { get; init; }

    /// <summary>HandMatching: the cards in the hand the filter lets through as the card play starts (the adapter's count).</summary>
    public int Matching { get; init; }

    /// <summary>CardPlaysFinished: this card's plays finished this turn before the card play (the adapter's count from the game's history).</summary>
    public int Before { get; init; }

    /// <summary>History: the query of the fight's log it counts (its count in the ledger: SimEngine.HistoryCount).</summary>
    public HistoryQuery? Query { get; init; }

    /// <summary>TargetPower, OwnerPower, MonsterPower: the power.</summary>
    public PowerId? Power { get; init; }

    /// <summary>MonsterSideAlive: the asking monster itself isn't counted (c != base.Creature).</summary>
    public bool ExcludingItself { get; init; }
    /// <summary>MonsterState, MonsterCounter: its key among the monster's fields ("Monster.BowlbugRock.IsOffBalance").</summary>
    public string? Field { get; init; }

    /// <summary>SideNextMove: the move id its teammates' next moves are compared with ("CALL_FOR_BACKUP_MOVE").</summary>
    public string? Move { get; init; }
}

/// <summary>A test decided when it was read (SimTest.Known): a monster's own flag the simulator doesn't follow, the slot it stands in.</summary>
public sealed record KnownCondition(bool Holds) : SimCondition(SimTest.Known);

/// <summary>The condition doesn't hold.</summary>
public sealed record NotCondition(SimCondition Inner) : SimCondition(SimTest.Not);

/// <summary>Every one of <paramref name="Parts"/> holds.</summary>
public sealed record AllCondition(IReadOnlyList<SimCondition> Parts) : SimCondition(SimTest.All);

/// <summary>At least one of <paramref name="Parts"/> holds.</summary>
public sealed record AnyCondition(IReadOnlyList<SimCondition> Parts) : SimCondition(SimTest.Any);

/// <summary>
/// The actions <paramref name="Then"/> run on the branches where <paramref name="Condition"/> holds, asked as this action runs (decision
/// #190): any effect under any condition (Sunder's energy, Timber's attack on every enemy, Impatience's draws, Sacrifice's block).
/// </summary>
public sealed record WhenAction(SimCondition Condition, IReadOnlyList<SimAction> Then) : SimAction
{
    /// <summary>
    /// In an enemy's move: asked as the move starts, not as this step runs (decision #400): the code tested it once, before an earlier step of the
    /// move changed what it tests (Entomancer's Pheromone Spit adds to its Personal Hive, then gives Strength by the hive it had).
    /// </summary>
    public bool AskedAtMoveStart { get; init; }
}

/// <summary>
/// Asks a condition now and keeps its answer for the rest of the play (a local the card's code stores before later steps: Ki Hop's and Bow
/// Smack's wasImpaled, taken before their attack). Its tests need an At to be kept.
/// </summary>
public sealed record AskAction(SimCondition Condition) : SimAction;
