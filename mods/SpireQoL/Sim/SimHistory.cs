using System.Text.Json.Serialization;

namespace SpireQoL.Sim;

/// <summary>
/// The kinds of entries in the game's log of the fight (CombatManager.Instance.History, MegaCrit.Sts2.Core.Combat.History.Entries), as
/// the history ledger counts them (decision #191).
/// </summary>
public enum HistoryKind
{
    CardPlayStarted, CardPlayFinished, CardDrawn, CardDiscarded, CardExhausted, CardGenerated, CardAfflicted, DamageReceived,
    CreatureAttacked, BlockGained, PowerReceived, EnergySpent, OrbChanneled, StarsModified, Summoned, ArrowShot, MonsterPerformedMove,
    PotionUsed,
}

/// <summary>Which entries a query counts by when they happened: this side's turn (CombatHistoryEntry.HappenedThisTurn), or the whole fight.</summary>
public enum HistoryScope { ThisTurn, ThisFight }

/// <summary>
/// Whose entries a query counts, by the entry's own creature or player (the play's player, who drew or exhausted the card, who made it,
/// who applied the power, who took the damage, who attacked): anyone's, the owner's (the player whose card, relic or power asks), or the
/// owner's Osty's.
/// </summary>
public enum HistoryWhose { Anyone, Owner, OwnersOsty }

/// <summary>The tests of an entry's detail a query makes besides its kind, scope and whose (each one the game's code makes: decision #191).</summary>
[Flags]
public enum HistoryDetail
{
    None = 0,
    /// <summary>DamageReceivedEntry: Result.UnblockedDamage &gt; 0 (Spite, Tear Asunder).</summary>
    Unblocked = 1,
    /// <summary>A play's Resources.EnergyValue == 0: played for no energy (Feral).</summary>
    ZeroEnergy = 2,
    /// <summary>A play's IsFirstInSeries: not a replay's later play (Echo Form).</summary>
    FirstInSeries = 4,
    /// <summary>CardPlayFinishedEntry.WasEthereal (Banshee's Cry, Pull From Below).</summary>
    Ethereal = 8,
    /// <summary>A play that isn't an auto-play (!CardPlay.IsAutoPlay: Pael's Eye).</summary>
    NotAutoPlay = 16,
    /// <summary>CardDrawnEntry: not the turn's hand draw (!FromHandDraw: Death March).</summary>
    NotHandDraw = 32,
    /// <summary>The damage's props are a powered attack (Result.Props.IsPoweredAttack()).</summary>
    PoweredAttack = 64,
}

/// <summary>
/// One question about the game's log of the fight (decision #191): entries of a kind, in a scope, whose, with the detail its code tests
/// (a card's type or tag, a power's class, a made card's class, an orb's kind, the detail flags). The ledger keeps its count per player
/// (SimPlayer.Turn for this turn, SimPlayer.Fight for the fight) under its <see cref="Key"/>: seeded by the adapter from the game's log,
/// then counted forward by the simulator's own events (SimEngine.Record). Read from the game's code by HistoryQueryReader, so a card,
/// relic or power that asks it needs no code of its own.
/// </summary>
public sealed record HistoryQuery(HistoryKind Kind, HistoryScope Scope, HistoryWhose Whose)
{
    public HistoryDetail Details { get; init; }
    /// <summary>The entry's card's type (None: any): a play's card, the card drawn, exhausted, discarded, made.</summary>
    public SimCardType CardType { get; init; }
    /// <summary>The entry's card has this tag (CardTag; null: any).</summary>
    public SimCardTag? CardTag { get; init; }
    /// <summary>PowerReceivedEntry.Power is this power (null: any).</summary>
    public PowerId? Power { get; init; }
    /// <summary>The entry's card is of this class (CardGeneratedEntry.Card is Sadness; null: any).</summary>
    public CardId? Card { get; init; }
    /// <summary>OrbChanneledEntry.Orb is of this kind (None: any).</summary>
    public OrbKind? Orb { get; init; }

    /// <summary>
    /// Its name in the ledger. The plays of the turn a player started or finished, by type or by one tag, keep the names their counts
    /// had before the ledger (SimKeys.Plays: "Started", "Finished:Attack", "Started#Wah"), which replay cases and reports hold.
    /// </summary>
    [JsonIgnore]
    public string Key => Keys.GetOrAdd(this, query => query.Name());

    /// <summary>Each query's name, made once (reading it allocates nothing after); a field on the record would take part in its equality.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<HistoryQuery, string> Keys = new();

    /// <summary>
    /// The plays of the turn a player started or finished, all, of a type or of one tag (SimKeys.Plays): counted by SimKeys.CountPlay as each
    /// play starts and finishes, never by SimEngine.Record.
    /// </summary>
    [JsonIgnore]
    public bool IsTurnPlayCount =>
        Kind is HistoryKind.CardPlayStarted or HistoryKind.CardPlayFinished && Scope == HistoryScope.ThisTurn && Whose == HistoryWhose.Owner
        && Details == HistoryDetail.None && Power == null && Card == null && Orb == null && (CardType == SimCardType.None || CardTag == null);

    private string Name()
    {
        if (IsTurnPlayCount)
        {
            var stage = Kind == HistoryKind.CardPlayStarted ? PlayStage.Started : PlayStage.Finished;
            return CardTag is { } tag ? SimKeys.Plays(stage, tag) : CardType != SimCardType.None ? SimKeys.Plays(stage, CardType) : SimKeys.Plays(stage);
        }
        var parts = new List<string> { "history:" + Kind, Scope == HistoryScope.ThisTurn ? "turn" : "fight", Whose.ToString() };
        if (Details != HistoryDetail.None) parts.Add(Details.ToString().Replace(", ", "+", StringComparison.Ordinal));
        if (CardType != SimCardType.None) parts.Add("type " + CardType);
        if (CardTag is { } cardTag) parts.Add("tag " + cardTag);
        if (Power is { } power) parts.Add("power " + power.Name);
        if (Card is { } card) parts.Add("card " + card.Name);
        if (Orb is { } orb) parts.Add("orb " + orb);
        return string.Join("/", parts);
    }

    public override string ToString() => Key;
}

/// <summary>
/// Something the simulator did that the game logs (decision #191): an entry's kind, whose it is (a player's id, or Who.YourOsty), and its
/// detail, which each query of the ledger tests (HistoryQuery). Made where the game logs it, just before its After hook.
/// </summary>
public readonly record struct HistoryEvent(HistoryKind Kind, int Whose)
{
    public HistoryDetail Details { get; init; }
    public SimCardType CardType { get; init; }
    public IReadOnlyCollection<SimCardTag>? CardTags { get; init; }
    public PowerId? Power { get; init; }
    public CardId? Card { get; init; }
    public OrbKind? Orb { get; init; }
    /// <summary>Whether the event's detail is known: an event whose card isn't read can't say whether a query by type or class counts it.</summary>
    public bool CardKnown { get; init; }

    /// <summary>Whether <paramref name="query"/> counts this event for the player <paramref name="player"/> (whose ledger it is).</summary>
    public bool CountsFor(HistoryQuery query, int player)
    {
        if (query.Kind != Kind || (query.Details & ~Details) != 0) return false;
        var whose = query.Whose switch
        {
            HistoryWhose.Anyone => true,
            HistoryWhose.Owner => Whose == player,
            _ => player == Who.You && Whose == Who.YourOsty,
        };
        if (!whose) return false;
        if (query.CardType != SimCardType.None && CardType != query.CardType) return false;
        if (query.CardTag is { } tag && CardTags?.Contains(tag) != true) return false;
        if (query.Power is { } power && Power != power) return false;
        if (query.Card is { } card && Card != card) return false;
        if (query.Orb is { } orb && Orb != orb) return false;
        return true;
    }

    /// <summary>A query that tests a detail this event can't say (its card wasn't read): whether it counts isn't known.</summary>
    public bool Unknown(HistoryQuery query) => !CardKnown && (query.CardType != SimCardType.None || query.CardTag != null || query.Card != null);
}
