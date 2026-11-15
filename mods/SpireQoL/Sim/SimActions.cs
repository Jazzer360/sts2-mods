namespace SpireQoL.Sim;

/// <summary>
/// A card play (CardModel.OnPlayWrapper): its body runs once per play (PlayCount, from the game's ModifyCardPlayCount), with
/// BeforeCardPlayed before each play and AfterCardPlayed after it (docs/SIMULATOR_V2.md "The game model").
/// </summary>
/// <param name="Play">The play (its PlayIndex is set for each play).</param>
/// <param name="Body">The card's own actions (OnPlay), then its enchantment's and affliction's.</param>
public sealed record CardPlayAction(SimCardPlay Play, IReadOnlyList<SimAction> Body) : SimAction;

/// <summary>
/// The damage of one attack, worked out at each hit from the state (docs/SIMULATOR_V2.md "Pipelines"): the card's base value
/// (per target when it's calculated on the target), its props, and the captured terms of sources the simulator doesn't model,
/// per target. Fixed: the numbers are final (no modelled terms are added).
/// </summary>
public sealed record DamageSpec(decimal Base, SimProps Props)
{
    public IReadOnlyDictionary<int, decimal>? BaseOn { get; init; }
    public IReadOnlyDictionary<int, CapturedTerms>? Captured { get; init; }
    public bool Fixed { get; init; }

    public decimal BaseFor(int target) => BaseOn != null && BaseOn.TryGetValue(target, out var b) ? b : Base;
    public CapturedTerms TermsFor(int target) => Captured != null && Captured.TryGetValue(target, out var c) ? c : CapturedTerms.None;
}

/// <summary>
/// An attack (AttackCommand): BeforeAttack, its hits (each through BeforeDamageReceived, the damage pipeline, block and HP
/// loss, AfterDamageGiven and AfterDamageReceived), then AfterAttack. Dealer: you unless the play is Osty's.
/// </summary>
public sealed record AttackAction(int Hits, SimPlay.Target Mode, DamageSpec Damage) : SimAction
{
    /// <summary>The enemy aimed at (Mode Single; -1: the play's target).</summary>
    public int TargetId { get; init; } = -1;
    /// <summary>Osty makes the attack (his hits: Thorns hits him; your Envenom and The Boot don't apply).</summary>
    public bool ByOsty { get; init; }
    /// <summary>Timber: when a hit kills the target, a second attack hits every enemy with this damage (OnKillDamage).</summary>
    public DamageSpec? OnKill { get; init; }
    /// <summary>Rain Shot: after a hit on the target, every other hittable enemy takes that hit's damage (overkill included).</summary>
    public bool Splash { get; init; }
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
public sealed record ApplyPowerAction(string Power, int Amount) : SimAction
{
    public enum Where { Target, AllEnemies, You, Creature }
    public Where To { get; init; } = Where.Target;
    public int Creature { get; init; }
    public bool Debuff { get; init; }
}

/// <summary>HP the card costs you (CreatureCmd.Damage on you, Unblockable and Unpowered), through the HP-loss pipeline.</summary>
public sealed record LoseHpAction(int Amount) : SimAction;

/// <summary>
/// Card events of a play (from what the card does: PlayFacts' counts), each card's hooks in turn. Indirect: an exhaust, discard,
/// draw, creation or shuffle the card causes (the Indirect procs setting shows their tabs); the card's own exhaust is direct.
/// </summary>
public sealed record CardEventAction(CardEvent Event, int Count) : SimAction
{
    public bool Indirect { get; init; }
}
