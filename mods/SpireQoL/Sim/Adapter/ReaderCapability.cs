using SpireQoL.Procs.Programs;

namespace SpireQoL.Sim;

/// <summary>
/// What the model reader (HookReader) would need to read a body it stops at (decision #195, round eight): every gap it names carries one, set
/// where the reader stops, so the measurement (HookGapTests, Snapshots/hook-gaps.txt) counts the classes each capability would unlock across
/// every kind of model without reading the gap's words.
/// </summary>
public enum ReaderCapability
{
    // ---- hooks no read model runs
    /// <summary>The HP-loss pipeline (ModifyHpLostBeforeOsty and the like, their AfterModifying).</summary>
    HpLossPipeline,
    /// <summary>BeforeAttack and AfterAttack (and an attack command to change).</summary>
    AttackHooks,
    /// <summary>The play's result location (ModifyCardPlayResultLocation, AfterModifying).</summary>
    ResultLocation,
    /// <summary>The damage cap and AfterModifyingDamageAmount.</summary>
    DamageCap,
    /// <summary>BeforeDamageReceived.</summary>
    BeforeDamageReceived,
    /// <summary>The block hooks (AfterBlockBroken, ShouldClearBlock, AfterModifyingBlockAmount, AfterBlockGained...).</summary>
    BlockHooks,
    /// <summary>AfterCurrentHpChanged.</summary>
    HpChanged,
    /// <summary>AfterCardEnteredCombat.</summary>
    CardEnteredCombat,
    /// <summary>Arrows (AfterArrowShot, AfterArrowImpaled, an arrow's owner, a command on arrows).</summary>
    Arrows,
    /// <summary>Orbs (AfterOrbEvoked, ModifyOrbValue, EvokeLast, an orb made first, an orb's own passive and evoke).</summary>
    Orbs,
    /// <summary>The power amount given (ModifyPowerAmountGiven, AfterModifyingPowerAmountGiven, BeforePowerAmountChanged).</summary>
    PowerAmountGiven,
    /// <summary>Card costs and keywords (TryModifyEnergyCost, TryModifyStarCost, TryModifyKeywordsInCombat, ModifyXValue).</summary>
    CardCosts,
    /// <summary>The hand draw (ModifyHandDraw, AfterModifyingHandDraw).</summary>
    HandDraw,
    /// <summary>A turn's start the simulation doesn't reach (an extra turn).</summary>
    TurnStart,
    /// <summary>Multiplayer scaling (GetScaledAmountForMultiplayer, the players' count).</summary>
    MultiplayerScaling,
    /// <summary>A death the game holds back (ShouldStopCombatFromEnding, ShouldCreatureBeRemovedFromCombatAfterDeath, ShouldDie...).</summary>
    DeathHeldBack,
    /// <summary>Any other hook no read model runs (AfterHandEmptied, ShouldPlay, BeforePotionUsed...).</summary>
    OtherHooks,
    /// <summary>The effect of a model the engine doesn't run from code at all: an enchantment's or affliction's OnPlay, a potion's OnUse, an orb's passive or evoke.</summary>
    EffectNotRun,

    // ---- commands
    /// <summary>PlayerCmd.GainStars for someone else.</summary>
    Stars,
    /// <summary>A card changed (CardCmd.ApplyKeyword, Upgrade, SetToFreeThisTurn, a card's own method).</summary>
    CardChanged,
    /// <summary>A card made (CardPileCmd.AddGeneratedCardToCombat, a made card).</summary>
    CardMade,
    /// <summary>CardPileCmd.AutoPlayFromDrawPile.</summary>
    AutoPlayFromDrawPile,
    /// <summary>OstyCmd (a summon).</summary>
    Osty,
    /// <summary>A stun or a kill (CreatureCmd.Stun, CreatureCmd.Kill: outcome tabs).</summary>
    StunOrKill,
    /// <summary>A power's amount changed by a command (PowerCmd.ModifyAmount, SetAmount).</summary>
    PowerAmountChanged,
    /// <summary>A command on another power (PowerCmd.Remove of another).</summary>
    CommandOnAnotherPower,
    /// <summary>A command on another creature (the players, a card's owner, a roll's list) or by another dealer.</summary>
    CommandOnAnotherCreature,
    /// <summary>A potion's chosen target (OnUse's target: a decision's target, as a card play's).</summary>
    UseTarget,
    /// <summary>A command the reader has no translation for (none of the above).</summary>
    CommandNotTranslated,

    // ---- the body's shape
    /// <summary>A loop over items (creatures, cards).</summary>
    LoopOverItems,
    /// <summary>A pick (a choice in the hook).</summary>
    Pick,
    /// <summary>Its own data: a card or a play kept, a collection changed, a field used directly.</summary>
    OwnData,
    /// <summary>A monster's own state set (its flag, its next move).</summary>
    MonsterStateSet,
    /// <summary>Another model's value set (PowerModel.Target, DynamicVar.BaseValue, AfflictionModel.Amount).</summary>
    OtherModelValueSet,
    /// <summary>A model's own method (its body read where it's called, its parameters).</summary>
    OwnMethod,
    /// <summary>A call into another model's method (its owner rebound).</summary>
    ModelCall,

    // ---- queries
    /// <summary>The fight's log (the history ledger, decision #191).</summary>
    FightLog,
    /// <summary>The card's facts (its pile, tags, keywords, class; an auto-play; an Ethereal exhaust).</summary>
    CardFacts,
    /// <summary>The damage's or block's props.</summary>
    DamageProps,
    /// <summary>A creature's side or presence.</summary>
    CreatureSide,
    /// <summary>A creature's HP.</summary>
    CreatureHp,
    /// <summary>The hit's result.</summary>
    HitResult,
    /// <summary>The hook's amount.</summary>
    HookAmount,
    /// <summary>The hook's player (a shuffle's).</summary>
    HookPlayer,
    /// <summary>The power's applier (alive, its side).</summary>
    Applier,
    /// <summary>The owner's powers.</summary>
    OwnerPowers,
    /// <summary>The owner's energy.</summary>
    OwnerEnergy,
    /// <summary>The turn number.</summary>
    TurnNumber,
    /// <summary>Arithmetic the reader doesn't read (Math.Pow, a negation, a decimal of another kind).</summary>
    Arithmetic,
    /// <summary>A value the reader can't follow (a nullable's GetValueOrDefault, a local it can't place).</summary>
    ValueNotFollowed,
    /// <summary>The code couldn't be read at all (an exception in the reader).</summary>
    NotRead,
}

/// <summary>Each capability's words in the measurement, and which a hook or a value needs.</summary>
public static class ReaderCapabilities
{
    /// <summary>The capability's words in Snapshots/hook-gaps.txt.</summary>
    public static string Text(ReaderCapability capability) => capability switch
    {
        ReaderCapability.HpLossPipeline => "the HP-loss pipeline",
        ReaderCapability.AttackHooks => "hooks: BeforeAttack and AfterAttack",
        ReaderCapability.ResultLocation => "the play's result location (ModifyCardPlayResultLocation)",
        ReaderCapability.DamageCap => "the damage cap and AfterModifyingDamageAmount",
        ReaderCapability.BeforeDamageReceived => "hooks: BeforeDamageReceived",
        ReaderCapability.BlockHooks => "hooks: block (AfterBlockBroken, ShouldClearBlock, AfterModifyingBlockAmount)",
        ReaderCapability.HpChanged => "hooks: AfterCurrentHpChanged",
        ReaderCapability.CardEnteredCombat => "hooks: AfterCardEnteredCombat",
        ReaderCapability.Arrows => "arrows (AfterArrowShot, AfterArrowImpaled, an arrow's owner, ArrowCmd)",
        ReaderCapability.Orbs => "orbs (AfterOrbEvoked, ModifyOrbValue, EvokeLast, an orb made first)",
        ReaderCapability.PowerAmountGiven => "hooks: the power amount given",
        ReaderCapability.CardCosts => "card costs and keywords (cards' own hooks, Part B3)",
        ReaderCapability.HandDraw => "hooks: the hand draw",
        ReaderCapability.TurnStart => "a turn's start (an extra turn)",
        ReaderCapability.MultiplayerScaling => "multiplayer scaling (GetScaledAmountForMultiplayer, the players' count)",
        ReaderCapability.DeathHeldBack => "a death the game holds back (ShouldStopCombatFromEnding, ShouldDie and the like)",
        ReaderCapability.OtherHooks => "hooks: other (AfterHandEmptied, ShouldPlay, BeforePotionUsed...)",
        ReaderCapability.EffectNotRun => "the engine runs no read effect of its kind (an enchantment's or affliction's OnPlay, a potion's OnUse, an orb's passive or evoke)",
        ReaderCapability.Stars => "commands: GainStars",
        ReaderCapability.CardChanged => "commands: a card changed (ApplyKeyword, Upgrade, SetToFreeThisTurn)",
        ReaderCapability.CardMade => "commands: a card made (AddGeneratedCardToCombat, a made card)",
        ReaderCapability.AutoPlayFromDrawPile => "commands: AutoPlayFromDrawPile",
        ReaderCapability.Osty => "commands: OstyCmd.Summon",
        ReaderCapability.StunOrKill => "outcomes: a stun or a kill (outcome tabs)",
        ReaderCapability.PowerAmountChanged => "commands: a power's amount changed (PowerCmd.ModifyAmount)",
        ReaderCapability.CommandOnAnotherPower => "a command on another power (PowerCmd.Remove of another)",
        ReaderCapability.CommandOnAnotherCreature => "a command on another creature (the players, a card's owner, a roll's list)",
        ReaderCapability.UseTarget => "a potion's chosen target (OnUse's target, as a card play's)",
        ReaderCapability.CommandNotTranslated =>"commands: another command (no translation)",
        ReaderCapability.LoopOverItems => "a loop over items (creatures, cards)",
        ReaderCapability.Pick => "a pick",
        ReaderCapability.OwnData => "its own data: a card or a play kept, a field used directly",
        ReaderCapability.MonsterStateSet => "a monster's own state set (its flag, its next move)",
        ReaderCapability.OtherModelValueSet => "another model's value set (PowerModel.Target, DynamicVar.BaseValue, AfflictionModel.Amount)",
        ReaderCapability.OwnMethod => "a model's own method (its body read where it's called)",
        ReaderCapability.ModelCall => "a call into another model's method, its owner rebound",
        ReaderCapability.FightLog => "the fight's log (the history ledger, decision #191)",
        ReaderCapability.CardFacts => "queries: the card's facts (its pile, tags, keywords, class; an auto-play; an Ethereal exhaust)",
        ReaderCapability.DamageProps => "queries: the damage's or block's props",
        ReaderCapability.CreatureSide => "queries: a creature's side or presence",
        ReaderCapability.CreatureHp => "queries: a creature's HP",
        ReaderCapability.HitResult => "queries: the hit's result",
        ReaderCapability.HookAmount => "queries: the hook's amount",
        ReaderCapability.HookPlayer => "queries: the hook's player (a shuffle's)",
        ReaderCapability.Applier => "queries: the power's applier (alive, its side)",
        ReaderCapability.OwnerPowers => "queries: the owner's powers",
        ReaderCapability.OwnerEnergy => "the owner's energy",
        ReaderCapability.TurnNumber => "the turn number",
        ReaderCapability.Arithmetic => "queries: arithmetic (Math.Pow, a negation)",
        ReaderCapability.ValueNotFollowed => "a value the reader can't follow (a nullable's GetValueOrDefault, a local it can't place)",
        ReaderCapability.NotRead => "the code couldn't be read",
        _ => capability.ToString(),
    };

    /// <summary>What a hook no read model runs would need: its group.</summary>
    public static ReaderCapability OfHook(string hook) => hook switch
    {
        _ when hook.Contains("HpLost", StringComparison.Ordinal) => ReaderCapability.HpLossPipeline,
        "BeforeAttack" or "AfterAttack" => ReaderCapability.AttackHooks,
        "ModifyCardPlayResultLocation" or "AfterModifyingCardPlayResultLocation" => ReaderCapability.ResultLocation,
        "ModifyDamageCap" or "AfterModifyingDamageAmount" => ReaderCapability.DamageCap,
        "BeforeDamageReceived" => ReaderCapability.BeforeDamageReceived,
        "AfterBlockBroken" or "ShouldClearBlock" or "AfterModifyingBlockAmount" or "AfterBlockGained" or "AfterBlockCleared" or "AfterPreventingBlockClear" or "BeforeBlockGained" => ReaderCapability.BlockHooks,
        "AfterCurrentHpChanged" => ReaderCapability.HpChanged,
        "AfterCardEnteredCombat" => ReaderCapability.CardEnteredCombat,
        _ when hook.Contains("Arrow", StringComparison.Ordinal) => ReaderCapability.Arrows,
        _ when hook.Contains("Orb", StringComparison.Ordinal) => ReaderCapability.Orbs,
        "BeforePowerAmountChanged" or "AfterModifyingPowerAmountGiven" => ReaderCapability.PowerAmountGiven,
        _ when hook.StartsWith("ModifyPowerAmountGiven", StringComparison.Ordinal) => ReaderCapability.PowerAmountGiven,
        "TryModifyStarCost" or "TryModifyKeywordsInCombat" or "ModifyXValue" => ReaderCapability.CardCosts,
        _ when hook.StartsWith("TryModifyEnergyCost", StringComparison.Ordinal) => ReaderCapability.CardCosts,
        "ModifyHandDraw" or "AfterModifyingHandDraw" => ReaderCapability.HandDraw,
        "AfterTakingExtraTurn" => ReaderCapability.TurnStart,
        _ when hook.StartsWith("ShouldTakeExtraTurn", StringComparison.Ordinal) => ReaderCapability.TurnStart,
        "GetScaledAmountForMultiplayer" => ReaderCapability.MultiplayerScaling,
        "ShouldStopCombatFromEnding" or "ShouldCreatureBeRemovedFromCombatAfterDeath" or "ShouldPowerBeRemovedAfterOwnerDeath" or "ShouldDie" or "ShouldDieLate" or "AfterPreventingDeath" => ReaderCapability.DeathHeldBack,
        _ => ReaderCapability.OtherHooks,
    };

    /// <summary>What a command the reader has no translation for would need, by the command.</summary>
    public static ReaderCapability OfCommand(string command) => command switch
    {
        "CardCmd.ApplyKeyword" or "CardCmd.Upgrade" or "CardCmd.SetToFreeThisTurn" or "CardCmd.RemoveKeyword" => ReaderCapability.CardChanged,
        _ when command.Contains("Generated", StringComparison.Ordinal) => ReaderCapability.CardMade,
        "CardPileCmd.AutoPlayFromDrawPile" => ReaderCapability.AutoPlayFromDrawPile,
        _ when command.StartsWith("OstyCmd.", StringComparison.Ordinal) => ReaderCapability.Osty,
        "CreatureCmd.Stun" or "CreatureCmd.Kill" => ReaderCapability.StunOrKill,
        _ when command.StartsWith("ArrowCmd.", StringComparison.Ordinal) => ReaderCapability.Arrows,
        _ when command.StartsWith("OrbCmd.", StringComparison.Ordinal) => ReaderCapability.Orbs,
        "PowerCmd.ModifyAmount" or "PowerCmd.SetAmount" => ReaderCapability.PowerAmountChanged,
        _ => ReaderCapability.CommandNotTranslated,
    };

    /// <summary>
    /// What a command's target the reader can't act on would need: its own data (a roll over a list it keeps), an arrow's owner, else a command on
    /// another creature (the players, a card's owner, a roll's list).
    /// </summary>
    public static ReaderCapability OfTarget(ProgramValue value) =>
        value.Parts().Any(part => part is QueryValue { Name: "GetInternalData" }) ? ReaderCapability.OwnData
        : value.Parts().Any(part => part is ParameterValue { Name: "arrow" } or NamedValue { Name: "arrow" }) ? ReaderCapability.Arrows
        : ReaderCapability.CommandOnAnotherCreature;

    /// <summary>What a value needs, or <paramref name="otherwise"/> where what it reads says nothing more (a creature the code names, compared).</summary>
    public static ReaderCapability OfValue(ProgramValue value, ReaderCapability otherwise) => OfValue(value) is var capability && capability != ReaderCapability.ValueNotFollowed ? capability : otherwise;

    /// <summary>
    /// What a value or a test the reader can't translate would need, from what it reads: the first part of it the reader has no fact for (the
    /// fight's log, the props, the hit's result, the card, an arrow, the applier, a creature's HP or side, the turn, the energy...).
    /// </summary>
    public static ReaderCapability OfValue(ProgramValue value)
    {
        var parts = value.Parts().ToList();
        bool Named(params string[] names) => parts.Any(part => part is ParameterValue parameter && names.Contains(parameter.Name) || part is NamedValue named && names.Contains(named.Name));
        bool Queried(params string[] names) => parts.Any(part => part is QueryValue query && names.Contains(query.Name));
        if (ProgramReader.ReadsHistory(value)) return ReaderCapability.FightLog;
        if (Queried("TurnNumber")) return ReaderCapability.TurnNumber;
        if (Queried("Energy") && parts.Any(part => part is QueryValue { Name: "PlayerCombatState" })) return ReaderCapability.OwnerEnergy;
        if (Queried("Players") && Queried("Count")) return ReaderCapability.MultiplayerScaling;
        if (Named("result") || Queried("TotalDamage", "UnblockedDamage")) return ReaderCapability.HitResult;
        // The attack command a BeforeAttack kept to change (Vigor's commandToModify): the attack hooks.
        if (Queried("commandToModify")) return ReaderCapability.AttackHooks;
        if (Named("props") || Queried("HasFlag", "IsCardOrMonsterMove")) return ReaderCapability.DamageProps;
        if (Named("arrow") || Queried("Arrows")) return ReaderCapability.Arrows;
        if (Queried("Applier")) return ReaderCapability.Applier;
        if (Named("causedByEthereal", "cardSource", "card") || Queried("Keywords", "IsAutoPlay", "CostsX", "Affliction", "Pile", "Tags", "EnergyCost", "TargetType")) return ReaderCapability.CardFacts;
        if (Named("cardPlay") || Queried("GetInternalData") || parts.Any(part => part is NullValue)) return ReaderCapability.OwnData;
        if (Queried("CurrentHp", "MaxHp")) return ReaderCapability.CreatureHp;
        if (Named("side", "dealer", "target") || Queried("Side", "IsMonster")) return ReaderCapability.CreatureSide;
        if (Queried("Powers")) return ReaderCapability.OwnerPowers;
        if (Named("amount")) return ReaderCapability.HookAmount;
        if (Named("player", "shuffler")) return ReaderCapability.HookPlayer;
        if (Queried("Pow", "op_UnaryNegation")) return ReaderCapability.Arithmetic;
        if (Named("value", "direction")) return ReaderCapability.OwnMethod;
        return ReaderCapability.ValueNotFollowed;
    }
}
