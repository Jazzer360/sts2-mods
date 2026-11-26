using System.Text.Json.Serialization;

namespace SpireQoL.Sim;

/// <summary>
/// One step of a card's script (docs/SIMULATOR.md "Card scripts"). The engine runs a script's actions in order on every
/// branch; an action may run hooks and branch. Actions are immutable values shared by every branch.
/// </summary>
public abstract record SimAction;

/// <summary>
/// A card play (CardModel.OnPlayWrapper): its body runs once per play (PlayCount, from the game's ModifyCardPlayCount), with
/// BeforeCardPlayed before each play and AfterCardPlayed after it (docs/SIMULATOR.md "The game model").
/// </summary>
/// <param name="Play">The play (its PlayIndex is set for each play).</param>
/// <param name="Body">The card's own actions (OnPlay), then its enchantment's and affliction's.</param>
public sealed record CardPlayAction(SimCardPlay Play, IReadOnlyList<SimAction> Body) : SimAction
{
    /// <summary>The card is played from its owner's hand (it has left it, though HandCount drops only at CardResolved).</summary>
    public bool FromHand { get; init; }
    /// <summary>The card in its owner's hand it is (SimPlay.HandCard; 0: none).</summary>
    public int HandCard { get; init; }
    /// <summary>
    /// Its play count is decided by the simulator's models as it starts (Hook.ModifyCardPlayCount: Echo Form on the fight's log), from
    /// Play.PlayCount as the count the sources it doesn't model gave. False: Play.PlayCount is the whole count.
    /// </summary>
    public bool CountByModels { get; init; }
    /// <summary>
    /// The card upgraded, for its plays after one upgrades it (CardCmd.Upgrade of the card in play, SimEngine.UpgradeCardInPlay): each later play
    /// is this card's, with this body, as OnPlayWrapper's next OnPlay reads the upgraded card. Null: none read (a later play is then unknown).
    /// </summary>
    public CardVersion? Upgraded { get; init; }
}

/// <summary>A card as a change during its play leaves it (its upgraded copy): the card its later plays' hooks see, and what each of them does.</summary>
public sealed record CardVersion(SimCard Card, IReadOnlyList<SimAction> Body);

/// <summary>
/// A potion drunk or thrown (PotionModel.OnUseWrapper, decision #313): its class, whose it is, and its target (an enemy's id, a player: you or a
/// teammate it's thrown at; Who.None for none).
/// </summary>
/// <param name="Fairy">It's one of the potions <see cref="SimPlayer.Fairies"/> counts: using it leaves one fewer to hold back a death.</param>
public sealed record SimPotionUse(string Name, int Owner, int Target, bool Fairy = false);

/// <summary>
/// A potion's use (PotionModel.OnUseWrapper, decision #313): it leaves its owner's belt (RemoveBeforeUse), BeforePotionUsed, its OnUse (the
/// body, read from its code: SimAdapter.Potion), the game's log of it (History.PotionUsed), then AfterPotionUsed. No card is played: nothing
/// a card play counts or reacts to runs.
/// </summary>
public sealed record PotionUseAction(SimPotionUse Use, IReadOnlyList<SimAction> Body) : SimAction;

/// <summary>
/// The damage of one attack, worked out at each hit from the state (docs/SIMULATOR.md "Pipelines"): the card's base value
/// (per target when it's calculated on the target), its props, and the captured terms of sources the simulator doesn't model,
/// per target. Fixed: the numbers are final (no modelled terms are added).
/// </summary>
public sealed record DamageSpec(decimal Base, SimProps Props)
{
    public IReadOnlyDictionary<int, decimal>? BaseOn { get; init; }
    public IReadOnlyDictionary<int, CapturedTerms>? Captured { get; init; }
    public bool Fixed { get; init; }
    /// <summary>What the base gains with each play of the card before this one (decision #160: Rampage's Increase, Momentum).</summary>
    public decimal GrowsEachPlay { get; init; }
    /// <summary>The enchantment's captured terms are on the first play only: it turns itself off after a play (decision #160: Vigorous).</summary>
    public bool EnchantFirstPlayOnly { get; init; }

    /// <summary>
    /// The base follows a count of the fight's log as the simulation goes (a calculated value: Murder's cards drawn, Gold Axe's plays): the
    /// base was worked out on the board, at that count; each more adds Per (decision #191).
    /// </summary>
    public LoggedAmount? Logged { get; init; }

    public decimal BaseFor(int target) => BaseOn != null && BaseOn.TryGetValue(target, out var baseOnTarget) ? baseOnTarget : Base;
    public CapturedTerms TermsFor(int target) => Captured != null && Captured.TryGetValue(target, out var terms) ? terms : CapturedTerms.None;
}

/// <summary>
/// An amount a calculated value gives (CalculatedVar: base + extra × its multiplier), its multiplier a count of the fight's log (decision
/// #191): the amount on the board was worked out at <paramref name="AtStart"/>; each one the ledger counts more adds <paramref name="Per"/>
/// (the extra value times what each entry adds to the multiplier).
/// </summary>
public sealed record LoggedAmount(HistoryQuery Query, decimal Per, int AtStart)
{
    /// <summary>What the ledger counted since the board, for the play's owner; 0 when the start state has no seed for it (the board's amount).</summary>
    public decimal More(SimState state, int owner) => SimEngine.HistoryCount(state, owner, Query) is { } now ? Per * (now - AtStart) : 0m;
}

/// <summary>
/// An attack (AttackCommand): BeforeAttack, its hits (each through BeforeDamageReceived, the damage pipeline, block and HP
/// loss, AfterDamageGiven and AfterDamageReceived), then AfterAttack. Dealer: you unless the play is Osty's.
/// </summary>
public sealed record AttackAction(int Hits, SimPlay.Target Mode, DamageSpec Damage) : SimAction
{
    /// <summary>Where its hit count comes from (decision #189): Hits, or what the simulator reads as it runs (Fiend Fire: the cards taken).</summary>
    public SimCount HitsFrom { get; init; }
    /// <summary>Its hits follow a count of the fight's log as the simulation goes (Finisher's Attacks this turn: decision #191).</summary>
    public LoggedAmount? HitsLogged { get; init; }
    /// <summary>The enemy aimed at (Mode Single; -1: the play's target).</summary>
    [JsonConverter(typeof(CreatureIdJson))]
    public int TargetId { get; init; } = Who.None;
    /// <summary>Osty makes the attack (his hits: Thorns hits him; your Envenom and The Boot don't apply).</summary>
    public bool ByOsty { get; init; }
    /// <summary>Rain Shot: after a hit on the target, every other hittable enemy takes that hit's damage (overkill included).</summary>
    public bool Splash { get; init; }
    /// <summary>
    /// Echoing Slash: the hit on every enemy (Mode All) runs once more for each enemy a round killed (attackCount +=
    /// WasTargetKilled), until a round kills none.
    /// </summary>
    public bool RepeatPerKill { get; init; }
    /// <summary>A shot's attack: it only happens when the shot had an arrow (ArrowCmd.ShootArrowAtTarget runs the attack inside ImpaleTarget).</summary>
    public bool NeedsArrow { get; init; }
    /// <summary>
    /// Blossom Shot: once the attack is over, its player gains Stretch equal to the damage its hits dealt (the sum of each result's
    /// TotalDamage + OverkillDamage: blocked, unblocked and overkill), none when it didn't happen (a shot with no arrow).
    /// </summary>
    public bool StretchFromDamage { get; init; }
}

/// <summary>
/// Double Energy (PlayerCmd.GainEnergy(Energy)): its player gains as much energy as they have when it resolves, the cost already
/// spent (decision #117).
/// </summary>
public sealed record GainEnergyAsHeldAction : SimAction;

/// <summary>
/// A draw the card acts on, by what it drew (decision #118: CardPileCmd.Draw's result), run with its reaction in one step so the
/// branches of what was drawn stay apart until it's settled: Escape Plan's block when it draws a Skill, Pillage drawing again while
/// it draws an Attack (and your hand has room), Scrape discarding what it drew unless it costs 0. Each draw is a branch per profile
/// of your draw pile (decision #90), which carries the type or the cost while such a card is in your hand.
/// </summary>
public sealed record DrawnCardAction(int Count) : SimAction
{
    public bool Indirect { get; init; } = true;
    /// <summary>Escape Plan: <see cref="Block"/> when the card it drew is of this type.</summary>
    public SimCardType BlockIf { get; init; }
    public GainBlockAction? Block { get; init; }
    /// <summary>Pillage: draws again while the card it drew is of this type and your hand has room.</summary>
    public SimCardType DrawAgainIf { get; init; }
    /// <summary>Scrape: discards the cards it drew unless they cost 0.</summary>
    public bool DiscardUnlessFree { get; init; }
}

/// <summary>
/// What a block gain's amount adds to its Base as it runs (CreatureCmd.GainBlock's amount, read from the card's code: decision #194): nothing,
/// what the play's last attack dealt (Fisticuffs: its results' TotalDamage + OverkillDamage), the gainer's own block (Entrench), or the play's
/// target's block times PerBlock (Mimic's calculated block on its target).
/// </summary>
public enum SimBlockBase { Given, AttackDealt, OwnBlock, TargetBlock }

/// <summary>Block (CreatureCmd.GainBlock) through the block pipeline, Times times; to you or the play's targeted teammate.</summary>
public sealed record GainBlockAction(int Times, decimal Base, SimProps Props) : SimAction
{
    /// <summary>Where its number of gains comes from (decision #189): Times, or what the simulator reads as it runs.</summary>
    public SimCount TimesFrom { get; init; }
    /// <summary>What its amount adds to Base as it runs (SimBlockBase).</summary>
    public SimBlockBase BaseFrom { get; init; }
    /// <summary>For SimBlockBase.TargetBlock: the block per block the target has (a calculated value's multiplier: its CalculationExtra).</summary>
    public decimal PerBlock { get; init; }
    public CapturedTerms? Captured { get; init; }
    /// <summary>Fixed: the amount is final (no modelled terms).</summary>
    public bool Fixed { get; init; }
    /// <summary>Who gains it: you (Who.You), or a teammate (Who.Teammate(k)).</summary>
    public int To { get; init; } = Who.You;
    /// <summary>What the base gains with each play of the card before this one (decision #160: Goopy's +1).</summary>
    public decimal GrowsEachPlay { get; init; }
    /// <summary>The enchantment's captured terms are on the first play only: it turns itself off after a play (decision #160).</summary>
    public bool EnchantFirstPlayOnly { get; init; }
}

/// <summary>Applies a power (PowerCmd.Apply): to the play's target, every enemy, or one creature. Artifact takes a debuff.</summary>
public sealed record ApplyPowerAction(PowerId Power, int Amount) : SimAction
{
    public enum Where { Target, AllEnemies, You, Creature, RandomEnemy }
    public Where To { get; init; } = Where.Target;
    public int Creature { get; init; }
    public bool Debuff { get; init; }
    /// <summary>The amount is what the applier has of that power when it lands (Cho Ki: PowerCmd.Apply of your Stretch amount).</summary>
    public bool CopiesOwn { get; init; }
    /// <summary>The amount is what the play's target has of that power when it lands (Molten Fist: PowerCmd.Apply of its Vulnerable amount).</summary>
    public bool CopiesTarget { get; init; }
    /// <summary>The amount is what the play's target has of this other power when it lands (Dominate: your Strength, its Vulnerable).</summary>
    public PowerId? TargetsAmountOf { get; init; }
    /// <summary>
    /// The amount is what the play's last attack dealt, block included (Blight Strike: PowerCmd.Apply&lt;DoomPower&gt; of the sum of its results'
    /// TotalDamage), as it lands.
    /// </summary>
    public bool FromAttackDealt { get; init; }
    /// <summary>Applications (RandomEnemy: each to a random hittable enemy, a branch per target: Bouncing Flask).</summary>
    public int Times { get; init; } = 1;
    /// <summary>
    /// The new copy is aimed at this player once it lands (decision #322: Imitation Learning's PlayerTarget = cardPlay.Target.Player after its
    /// PowerCmd.Apply): its amount kept under that player (SimEngine.AimNewCopy). Null: not aimed.
    /// </summary>
    public AimedPlayer? AimedAt { get; init; }
    /// <summary>
    /// Who applies it, when it isn't the play's player (decision #335): the player a potion is thrown at applying it to themselves (Duplicator,
    /// Focus Potion: PowerCmd.Apply's applier the target). Null: the play's player.
    /// </summary>
    public int? Applier { get; init; }
}

/// <summary>
/// PowerCmd.ModifyAmount of the holder's copy of <paramref name="Power"/> aimed at a player (decision #322: Imitation Learning's power already
/// aimed at its target, Frantic Escape's enemy Sandpit aimed at you), by <paramref name="Amount"/>: SimEngine.ModifyAimedCopy. Nothing without
/// such a copy (the code's own test of it guards the step).
/// </summary>
public sealed record ModifyAimedPowerAction(PowerId Power, AimHolder Holder, AimedPlayer Aim, int Amount) : SimAction
{
    /// <summary>FirstEnemyWithPower: the power that enemy has.</summary>
    public PowerId? HolderPower { get; init; }
    /// <summary>Its applier is the holder (Frantic Escape's sandpitEnemy); else the play's owner.</summary>
    public bool AppliedByHolder { get; init; }
}

/// <summary>
/// An enemy's move's attack (DamageCmd.Attack, decision #195): its intent's hits from <paramref name="FirstHit"/>, <paramref name="Hits"/> of
/// them (SimEnemy.Intent, or Hits), each through the damage pipeline as it lands, run by the enemy turn at its place in the move.
/// </summary>
public sealed record MoveAttackAction(int FirstHit, int Hits) : SimAction;

/// <summary>
/// A power an enemy's move applies (PowerCmd.Apply, decision #195): on each creature of <paramref name="To"/> (an enemy's id, Who.You,
/// Who.Teammate(k)) that's alive when it runs, its applier the enemy, Artifact taking a debuff.
/// </summary>
public sealed record MovePowerAction(PowerId Power, int Amount, IReadOnlyList<int> To, bool Debuff) : SimAction;

/// <summary>
/// Block an enemy's move gives (CreatureCmd.GainBlock, decision #195): on each enemy of <paramref name="To"/> alive when it runs, its amount
/// through the game's block math when the adapter read it.
/// </summary>
public sealed record MoveBlockAction(int Amount, IReadOnlyList<int> To) : SimAction;

/// <summary>
/// An enemy's move setting a property of its own monster that one of its attack intents' damage reads (decision #337: Waterfall Giant's About to
/// Blow storing its Steam Eruption as SteamEruptionDamage, Pressure Gun adding 5 to CurrentPressureGunDamage), kept in its fields under
/// <paramref name="Field"/> (IntentHit.BaseFrom) for the projection's later turns: the amount of its own power <paramref name="OwnPower"/> as the
/// step runs, else <paramref name="Amount"/> more than it was when <paramref name="Adds"/> (<paramref name="Now"/> as the adapter read it, until a
/// move set it), else <paramref name="Amount"/>. <paramref name="Unknown"/>: a value that couldn't be read, so a later hit reading it isn't known.
/// Only a projection runs it (SimState.Forecasting): no later turn's hit is simulated anywhere else.
/// </summary>
public sealed record MoveStoreAction(string Field, int Amount, bool Adds = false, int Now = 0, PowerId? OwnPower = null, string? Unknown = null) : SimAction;

/// <summary>
/// An enemy's move stunning itself (CreatureCmd.Stun of its own creature, decision #271: Bowlbug Rock's Headbutt when it's off balance, under a
/// WhenAction asked of it as the step runs: its own state, SimCounted.MonsterState). Its move this turn is under way, so the
/// stunned move is its next (SimEngine.Stun: it's stunned for the turn after).
/// </summary>
public sealed record MoveStunAction : SimAction;

/// <summary>
/// A card's stun of the enemy it's played at (CreatureCmd.Stun(cardPlay.Target), decision #299: Whistle): its next move the stunned move
/// (Creature.StunInternal), so it doesn't act this enemy turn; nothing on one that's dead (StunInternal's IsDead test).
/// </summary>
public sealed record StunTargetAction : SimAction;

/// <summary>
/// HP the card costs its owner (CreatureCmd.Damage of its owner with the props every card's HP cost passes, SimEngine.HpCostProps), dealt by them
/// from the card in play, through the one path for damage to a player (SimEngine.DamagePlayer, decision #219).
/// </summary>
public sealed record LoseHpAction(int Amount) : SimAction;

/// <summary>
/// CreatureCmd.LoseMaxHp of the card's owner from its play (Brightest Flame: decision #341): HP above the new max lost first, as the HP its card costs
/// (CreatureCmd.Damage with the card as its source: dealt by its owner, Unblockable, Unpowered, Move), then the max set, at least 1 (SimEngine.LoseMaxHp).
/// </summary>
public sealed record LoseMaxHpAction(int Amount) : SimAction;

/// <summary>
/// A part of the play the adapter couldn't read from the game (Sts2State.ReadScope: a member a game update renamed): what's built
/// from it is a guess, so the play is unknown (SimState.Unmodelled), named by the member.
/// </summary>
public sealed record UnreadAction(string Member) : SimAction;

/// <summary>Something the play does that the simulator doesn't follow (SimState.Unmodelled, decision #92): the play is unknown, with why.</summary>
public sealed record NotModelledAction(string Reason) : SimAction;

/// <summary>
/// The play's target's debuffs as the card's code takes them where it runs (Misery, decision #293: each power on the target whose amount is a
/// debuff, PowerModel.TypeForCurrentAmount, with its amount, before its attack), kept for the rest of the play (SimState.PlayTests) for
/// <see cref="SpreadKeptDebuffsAction"/>.
/// </summary>
public sealed record KeepTargetDebuffsAction : SimAction;

/// <summary>
/// The debuffs <see cref="KeepTargetDebuffsAction"/> kept, onto every hittable enemy but the play's target, enemy by enemy, each in the order the
/// target had them (Misery's loops: PowerCmd.ModifyAmount of the one already there, else PowerCmd.Apply of a clone: both through the amount
/// hooks and Artifact), applied by the play's owner.
/// </summary>
public sealed record SpreadKeptDebuffsAction : SimAction;

/// <summary>
/// Cards of the playing player's leaving their hand mid-play: the ones it discards and exhausts from there (its takes: TakeFromHandAction),
/// right after they go. Count: how many (Whole: every card in the hand now, whatever they were).
/// </summary>
public sealed record LeaveHandAction(int Count) : SimAction
{
    public bool Whole { get; init; }
}

/// <summary>
/// Where a count a play's action uses comes from (decision #189): a number the adapter gave, or something the simulator reads on its own
/// state when the action runs, so a replay's later play counts the board the earlier ones left.
/// </summary>
public enum SimCount
{
    /// <summary>The number the action gives.</summary>
    Given,
    /// <summary>The cards this play's last take took (TakeFromHandAction: Fiend Fire's hits, Storm of Steel's Shivs, Calculated Gamble's draws).</summary>
    CardsTaken,
    /// <summary>The room in the play owner's hand now (The Moon's Shivs, Crash Landing's statuses, Scrawl's draws).</summary>
    HandRoom,
}

/// <summary>How a TakeFromHandAction takes its cards.</summary>
public enum HandTakeWay
{
    /// <summary>Every card of the collection in the hand now (Fiend Fire your hand, Second Wind its non-Attacks).</summary>
    Each,
    /// <summary>Cards you pick, up to Most (Acrobatics, Burning Pact, Purity): how many is exact, which never is (decision #188).</summary>
    Pick,
    /// <summary>Cards a roll takes, up to Most (True Grit, Thrash).</summary>
    Random,
}

/// <summary>
/// Cards a play takes from its owner's hand (CardCmd.Exhaust, Discard, Transform, CardPileCmd.Add back onto the draw pile), decided on
/// the state as the action runs (decision #189): each play takes what it finds there, so a replay's later play takes what the earlier
/// ones left and what they drew. They leave the hand (unless they're transformed in place), then each sets off <see cref="Event"/> one
/// card at a time, with <see cref="EachCard"/> after each (a loop's other commands: Second Wind's block). How many it took is the play's
/// CardsTaken (SimCount), for what counts them later (Fiend Fire's hits).
/// </summary>
public sealed record TakeFromHandAction(HandTakeWay Way) : SimAction
{
    /// <summary>What each card taken sets off (Exhausted, Discarded, Generated for a transform); null: nothing (put back on the draw pile).</summary>
    public CardEvent? Event { get; init; }
    /// <summary>The card a transform makes of each, when its code names it (Begone's Minion Strike): its created-card event's card. Null: not known.</summary>
    public SimCard? Made { get; init; }
    /// <summary>
    /// A pick its test sends one way or the other (The Hermit: a Curse is exhausted, any other card discarded): Event for a card the
    /// filter lets through, this for one it doesn't. When the hand holds both, which happens is your pick (decision #188): neither is set off.
    /// </summary>
    public CardEvent? EventIfNot { get; init; }
    /// <summary>A pick's or a roll's most cards a play.</summary>
    public int Most { get; init; } = 1;
    /// <summary>
    /// The take's filter (its card and step, SimDrawCard.TakenBy): only the cards in the hand it lets through. Null: any card.
    /// </summary>
    public string? Filter { get; init; }
    /// <summary>The cards in the hand the filter lets through as the card play starts (the adapter's count; the simulator follows it after).</summary>
    public int Matching { get; init; }
    /// <summary>The cards leave the hand (false: transformed where they are).</summary>
    public bool Leaves { get; init; } = true;
    /// <summary>From the owner's draw pile instead of the hand (Cleanse): the pick is a card of the pile (decision #188).</summary>
    public bool FromDrawPile { get; init; }
    /// <summary>What the play does after each card it takes, before the next (the take's loop's other commands).</summary>
    public IReadOnlyList<SimAction> EachCard { get; init; } = Array.Empty<SimAction>();
}

/// <summary>
/// Cards a play moves from your draw pile (Wish, Secret Weapon, Seeker Strike, Anointed...) or your discard pile (FromDiscard:
/// Hologram, Dredge, The World...) into your hand with CardPileCmd.Add, <paramref name="Count"/> of them: each leaves its pile and
/// joins the hand as it is (what acts there at the end of your turn, Ethereal, what Stampede could pick; the hand's count is the
/// play's HandDelta). Eligible: how many cards of each pile profile (SimDrawCard.Key) it can take. Random (Anointed, The World:
/// TakeRandom): each eligible card equally likely, a branch per profile. Otherwise you choose, a later decision (decision #167): a
/// plain card when there is one, else unknown when the ones you could take differ.
/// </summary>
public sealed record TakeToHandAction(int Count, IReadOnlyDictionary<string, int> Eligible) : SimAction
{
    public bool Random { get; init; }
    public bool FromDiscard { get; init; }
    /// <summary>
    /// Each card joins the hand's count as it's taken, into a full hand going to the discard pile instead (CardPileCmd.Add), so what
    /// the play does next finds it there (decision #189). False: the play's HandDelta counts it (the test shorthand).
    /// </summary>
    public bool JoinsHand { get; init; }
    /// <summary>Where its count comes from as it runs (decision #189): Count, or at most Count of your hand's room (The World).</summary>
    public SimCount CountFrom { get; init; }
}

/// <summary>
/// Cards auto-played from the top of the play owner's draw pile (CardPileCmd.AutoPlayFromDrawPile with CardPilePosition.Top), one
/// after another: Havoc (one, exhausted after: ForceExhaust), Cascade (X), The Devil (one per card it exhausted; Upgrade: The Devil+
/// upgrades each first).
/// </summary>
public sealed record AutoPlayFromDrawAction(int Count) : SimAction
{
    public bool ForceExhaust { get; init; }
    public bool Upgrade { get; init; }
    /// <summary>Where its count comes from as it runs (decision #189): Count, or the cards the play took (The Devil: one per card it exhausted).</summary>
    public SimCount CountFrom { get; init; }
}

/// <summary>
/// The play owner's draw pile shuffled with their discard pile (CardPileCmd.Shuffle), after <paramref name="HandIn"/> joined the draw
/// pile (Reboot: every card in your hand, CardPileCmd.Add to the draw pile; the hand itself leaves with LeaveHandAction).
/// </summary>
public sealed record ShuffleDrawPileAction(IReadOnlyList<SimDrawGroup> HandIn) : SimAction
{
    /// <summary>Only when the draw pile holds fewer than this many cards and the discard pile has some (Ki Chant's, ShuffleIfNecessary's); 0: always.</summary>
    public int WhenShorterThan { get; init; }
}

/// <summary>How a play picks the cards it exhausts (ExhaustFromHandAction).</summary>
public enum ExhaustPick { Random, All, Chosen }

/// <summary>
/// A play exhausting others (after its Exhausted event): what the ones that act when exhausted do (SimPlayer.ExhaustingInHand:
/// Drum of Battle's energy). Every card in the hand (All), one at random (Random: each card equally likely), or the one you choose
/// (Chosen: your later decision, so unknown while one could be picked). Only and Except filter by type (None: any); FromDrawPile
/// (Cleanse): it takes from the draw pile.
/// </summary>
public sealed record ExhaustFromHandAction(ExhaustPick Pick) : SimAction
{
    public SimCardType Only { get; init; }
    public SimCardType Except { get; init; }
    public bool FromDrawPile { get; init; }
}

/// <summary>
/// Card events of a play (from what the card does: its program's steps), each card's hooks in turn. Indirect: an exhaust, discard,
/// draw, creation or shuffle the card causes (the Indirect procs setting shows their tabs); the card's own exhaust is direct.
/// </summary>
public sealed record CardEventAction(CardEvent Event, int Count) : SimAction
{
    public bool Indirect { get; init; }
    /// <summary>Only on a card play's first play (stars a card spends: a replay doesn't spend them again).</summary>
    public bool FirstPlayOnly { get; init; }
    /// <summary>The card the event is about, when known (the played card exhausting itself: Burning Sticks reads its type).</summary>
    public SimCard? Card { get; init; }
    /// <summary>
    /// The player it happens to (who draws, who created the card: Who.Teammate(k) for Huddle Up's draws and Blade Symphony's
    /// Shivs); null: the play's owner.
    /// </summary>
    public int? Player { get; init; }
    /// <summary>Where its count comes from (decision #189): Count, or what the simulator reads as it runs (the cards taken, your hand's room).</summary>
    public SimCount CountFrom { get; init; }
    /// <summary>
    /// Its events are cards joining the owner's hand as they happen (cards made into your hand; draws with no draw pile read), into a
    /// full hand none (decision #189). False: the play's HandDelta counts them (the test shorthand).
    /// </summary>
    public bool JoinsHand { get; init; }
    /// <summary>
    /// The player whose hand the cards join, when it isn't the player the event happens to: a card made by you for a teammate (Largesse's goes
    /// to their hand, its created-card event is yours: Hook.AfterCardGeneratedForCombat's creator). Null: the event's player.
    /// </summary>
    public int? IntoHandOf { get; init; }
    /// <summary>The filtered takes of the play each card it makes would be taken by (decision #189: SimDrawCard.TakenBy); null: not known.</summary>
    public IReadOnlyList<string>? TakenBy { get; init; }
    /// <summary>
    /// The card it makes, by its class's name, when it's one: into a full hand it goes to its owner's discard pile (CardPileCmd.Add), as its
    /// profile (SimPlayer.MadeCards). Null: not known (it isn't followed into the pile).
    /// </summary>
    public string? MadeCard { get; init; }
}

/// <summary>
/// Damage from a card that isn't an attack (CreatureCmd.Damage: Capture Spirit) to the play's target, through the damage pipeline
/// with the card as its source. No BeforeAttack or AfterAttack: Curl Up, Flutter and the like don't react.
/// </summary>
public sealed record DamageAction(DamageSpec Damage) : SimAction
{
    /// <summary>The enemy it's dealt to when it isn't the play's target (a potion's: decision #313); null: the play's target.</summary>
    public int? TargetId { get; init; }
    /// <summary>
    /// Every hittable enemy as the command starts, one after another, the deaths after the last (CreatureCmd.Damage of a list: Explosive
    /// Ampoule's HittableEnemies).
    /// </summary>
    public bool EveryEnemy { get; init; }
    /// <summary>
    /// The players it's dealt to first, in the fight's order (CreatureCmd.Damage of every creature but the pets: Foul Potion's, the fight's allies
    /// before its enemies), each still alive as their turn in the list comes, through the one path for damage to a player (decision #219); null: none.
    /// </summary>
    public IReadOnlyList<int>? Players { get; init; }
    /// <summary>What deals it, as Incoming Damage names a player's damage (the potion's or card's class).</summary>
    public string? Source { get; init; }
}

/// <summary>
/// A card calling a power's own command on each hittable enemy's instance of it, one enemy after another (Outbreak: PoisonPower.Trigger on each
/// enemy's poison): the command as the power's code runs it (ModelSpec.Commands, a read model's RunCommand), decision #210.
/// </summary>
public sealed record ModelCommandAction(string Power, string Method) : SimAction;

/// <summary>
/// CreatureCmd.Kill of each hittable enemy whose own <paramref name="Power"/> answers its <paramref name="Question"/> (End of Days: DoomPower.DoomKill of
/// GetDoomedCreatures(HittableEnemies), each one's Doom at least its HP, the question read from the power's code: ModelSpec.Tests), the list worked
/// out as the action starts, then the listeners told it (Hook.AfterDiedToDoom: <see cref="TellsDiedToDoom"/>) unless it's empty.
/// </summary>
public sealed record KillEachAction(PowerId Power, string Question) : SimAction
{
    public bool TellsDiedToDoom { get; init; }
}

/// <summary>An older report's name for Outbreak's ModelCommandAction (PoisonPower.Trigger on each enemy's poison), read as it.</summary>
public sealed record TriggerPoisonAction : SimAction;

/// <summary>
/// Expose: the play's target loses all its block (CreatureCmd.LoseBlock: AfterBlockBroken when it had some, Burrowed), then its
/// Artifact (PowerCmd.Remove), so the Vulnerable that follows lands.
/// </summary>
public sealed record StripBlockAction(bool RemoveArtifact) : SimAction;

/// <summary>
/// A power of the play's owner taken off them (PowerCmd.Remove&lt;T&gt;(Owner.Creature): Rain Shot's Aim and Quietus after its shot), its
/// AfterRemoved told (SimEngine.RemovePower). Nothing when they don't have it.
/// </summary>
public sealed record RemoveOwnPowerAction(PowerId Power) : SimAction;

/// <summary>The play's target dies if it's alive at or below this HP (Judgement: CreatureCmd.SetCurrentHp to 0).</summary>
public sealed record ExecuteAction(int Threshold) : SimAction;

/// <summary>Your Osty dies (CreatureCmd.Kill: Sacrifice, Bone Shards), losing all his HP (Necro Mastery reacts).</summary>
public sealed record KillOstyAction : SimAction;

/// <summary>
/// OstyCmd.Summon of <paramref name="Amount"/> for a player (Bodyguard, Reanimate, Legion of Bone's each player), as SimEngine.Summon runs it.
/// </summary>
public sealed record SummonAction(int Amount) : SimAction
{
    /// <summary>The summoner (null: the play's player).</summary>
    public int? Player { get; init; }
}

/// <summary>CreatureCmd.Heal of <paramref name="Amount"/> on a player or their Osty (Not Yet, Spur), up to max HP (SimEngine.Heal).</summary>
public sealed record HealAction(int Amount) : SimAction
{
    /// <summary>The play's player's Osty, not the player.</summary>
    public bool Osty { get; init; }
    /// <summary>The player healed (a potion thrown at a teammate: decision #335); null: the play's player.</summary>
    public int? Player { get; init; }
}

/// <summary>
/// CreatureCmd.GainMaxHp of <paramref name="Amount"/> on a player (Feed, Fruit Juice): their max HP up by it (CreatureCmd.SetMaxHp), then a heal of
/// what it went up by (CreatureCmd.Heal), as SimEngine.GainMaxHp runs it.
/// </summary>
public sealed record GainMaxHpAction(int Amount) : SimAction
{
    /// <summary>The player who gains it (a potion thrown at a teammate); null: the play's player.</summary>
    public int? Player { get; init; }
}
