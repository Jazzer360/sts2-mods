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
}

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

    public decimal BaseFor(int target) => BaseOn != null && BaseOn.TryGetValue(target, out var baseOnTarget) ? baseOnTarget : Base;
    public CapturedTerms TermsFor(int target) => Captured != null && Captured.TryGetValue(target, out var terms) ? terms : CapturedTerms.None;
}

/// <summary>
/// An attack (AttackCommand): BeforeAttack, its hits (each through BeforeDamageReceived, the damage pipeline, block and HP
/// loss, AfterDamageGiven and AfterDamageReceived), then AfterAttack. Dealer: you unless the play is Osty's.
/// </summary>
public sealed record AttackAction(int Hits, SimPlay.Target Mode, DamageSpec Damage) : SimAction
{
    /// <summary>The enemy aimed at (Mode Single; -1: the play's target).</summary>
    [JsonConverter(typeof(CreatureIdJson))]
    public int TargetId { get; init; } = Who.None;
    /// <summary>Osty makes the attack (his hits: Thorns hits him; your Envenom and The Boot don't apply).</summary>
    public bool ByOsty { get; init; }
    /// <summary>Timber: when a hit kills the target, a second attack hits every enemy with this damage (OnKillDamage).</summary>
    public DamageSpec? OnKill { get; init; }
    /// <summary>Rain Shot: after a hit on the target, every other hittable enemy takes that hit's damage (overkill included).</summary>
    public bool Splash { get; init; }
    /// <summary>
    /// Echoing Slash: the hit on every enemy (Mode All) runs once more for each enemy a round killed (attackCount +=
    /// WasTargetKilled), until a round kills none.
    /// </summary>
    public bool RepeatPerKill { get; init; }
    /// <summary>A shot's attack: it only happens when the shot had an arrow (ArrowCmd.ShootArrowAtTarget runs the attack inside ImpaleTarget).</summary>
    public bool NeedsArrow { get; init; }
    /// <summary>Sunder: energy for the play's player once the attack is over, when one of its hits killed the target (decision #117).</summary>
    public int OnKillEnergy { get; init; }
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

/// <summary>Block (CreatureCmd.GainBlock) through the block pipeline, Times times; to you or the play's targeted teammate.</summary>
public sealed record GainBlockAction(int Times, decimal Base, SimProps Props) : SimAction
{
    public CapturedTerms? Captured { get; init; }
    /// <summary>Fixed: the amount is final (no modelled terms).</summary>
    public bool Fixed { get; init; }
    /// <summary>Who gains it: you (Who.You), or a teammate (Who.Teammate(k)).</summary>
    public int To { get; init; } = Who.You;
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
    /// <summary>Applications (RandomEnemy: each to a random hittable enemy, a branch per target: Bouncing Flask).</summary>
    public int Times { get; init; } = 1;
}

/// <summary>HP the card costs you (CreatureCmd.Damage on you, Unblockable and Unpowered), through the HP-loss pipeline.</summary>
public sealed record LoseHpAction(int Amount) : SimAction;

/// <summary>
/// A part of the play the adapter couldn't read from the game (Sts2State.ReadScope: a member a game update renamed): what's built
/// from it is a guess, so the play is unknown (SimState.Unmodelled), named by the member.
/// </summary>
public sealed record UnreadAction(string Member) : SimAction;

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
/// Card events of a play (from what the card does: PlayFacts' counts), each card's hooks in turn. Indirect: an exhaust, discard,
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
}

/// <summary>
/// Damage from a card that isn't an attack (CreatureCmd.Damage: Capture Spirit) to the play's target, through the damage pipeline
/// with the card as its source. No BeforeAttack or AfterAttack: Curl Up, Flutter and the like don't react.
/// </summary>
public sealed record DamageAction(DamageSpec Damage) : SimAction;

/// <summary>
/// Outbreak: every hittable enemy's poison triggers now (PoisonPower.Trigger), one enemy after another: a hit per trigger
/// (Accelerant adds triggers) for its stacks then, unblockable, the stacks going down by 1 after each hit it survives.
/// </summary>
public sealed record TriggerPoisonAction : SimAction;

/// <summary>
/// Expose: the play's target loses all its block (CreatureCmd.LoseBlock: AfterBlockBroken when it had some, Burrowed), then its
/// Artifact (PowerCmd.Remove), so the Vulnerable that follows lands.
/// </summary>
public sealed record StripBlockAction(bool RemoveArtifact) : SimAction;

/// <summary>The play's target dies if it's alive at or below this HP (Judgement: CreatureCmd.SetCurrentHp to 0).</summary>
public sealed record ExecuteAction(int Threshold) : SimAction;

/// <summary>Your Osty dies (CreatureCmd.Kill: Sacrifice, Bone Shards), losing all his HP (Necro Mastery reacts).</summary>
public sealed record KillOstyAction : SimAction;
