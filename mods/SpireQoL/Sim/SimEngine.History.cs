namespace SpireQoL.Sim;

/// <summary>
/// The history ledger (decision #191): the counts of the game's log of the fight the board's cards, relics and powers ask about
/// (SimState.History), each player's seeded by the adapter from the game's log in the start state, then counted forward here by the
/// simulator's own events, at the place the game logs each one (just before its After hook). This turn's counts start over as the
/// enemies' turn starts (the entries the player's turn left no longer happened "this turn": CombatHistoryEntry.HappenedThisTurn).
/// </summary>
public static partial class SimEngine
{
    /// <summary>Counts an event the game logs into every player's ledger whose queries count it.</summary>
    internal static void Record(SimState state, HistoryEvent entry)
    {
        var queries = state.History;
        if (queries.Count == 0) return;
        foreach (var query in queries)
        {
            if (query.Kind != entry.Kind || query.IsTurnPlayCount) continue;
            foreach (var id in state.PlayerIds)
            {
                if (state.Player(id) == null) continue;
                var unknown = entry.Unknown(query) && entry.CountsFor(query with { CardType = SimCardType.None, CardTag = null, Card = null }, id);
                if (!unknown && !entry.CountsFor(query, id)) continue;
                var player = state.MutablePlayer(id);
                var counts = query.Scope == HistoryScope.ThisTurn ? player.Turn : player.Fight;
                counts = unknown ? counts.With(query.Key, counts[query.Key], data: 1) : counts.Add(query.Key, delta: 1);
                if (query.Scope == HistoryScope.ThisTurn) player.Turn = counts;
                else player.Fight = counts;
            }
        }
    }

    /// <summary>
    /// A player's count of a query (its ledger entry); null when an event it may count wasn't read (its card's kind), or when the start state
    /// has no seed for it.
    /// </summary>
    public static int? HistoryCount(SimState state, int player, HistoryQuery query)
    {
        if (state.Player(player) is not { } simPlayer) return 0;
        // A question the adapter didn't seed has no count to go on: unknown, never 0.
        if (!query.IsTurnPlayCount && !state.History.Contains(query)) return null;
        var counts = query.Scope == HistoryScope.ThisTurn ? simPlayer.Turn : simPlayer.Fight;
        return counts.DataOf(query.Key) != 0 && !query.IsTurnPlayCount ? null : counts[query.Key];
    }

    /// <summary>A card play's entry (CardPlayStartedEntry, CardPlayFinishedEntry) for the ledger's queries of plays.</summary>
    private static void RecordPlay(SimState state, SimCardPlay play, PlayStage stage)
    {
        var details = HistoryDetail.None;
        if (play.PlayIndex == 0) details |= HistoryDetail.FirstInSeries;
        if (!play.AutoPlay) details |= HistoryDetail.NotAutoPlay;
        if (play.Card.Energy == 0) details |= HistoryDetail.ZeroEnergy; // Resources.EnergyValue: an X cost's X too
        if (stage == PlayStage.Finished && play.Card.Ethereal) details |= HistoryDetail.Ethereal;
        var kind = stage == PlayStage.Started ? HistoryKind.CardPlayStarted : HistoryKind.CardPlayFinished;
        Record(state, new HistoryEvent(kind, play.Owner) { Details = details, CardType = play.Card.Type, CardTags = play.Card.Tags, CardKnown = true });
    }

    /// <summary>
    /// The entry the game logs for a card event (CardDrawn, CardExhausted, CardDiscarded, CardGenerated, EnergySpent, StarsModified), its
    /// card's kind when the event knows it.
    /// </summary>
    private static void RecordCardEvent(SimState state, CardEvent cardEvent, int player)
    {
        if (state.History.Count == 0 || !Who.IsPlayer(player)) return;
        var entry = cardEvent switch
        {
            CardEvent.Drawn => new HistoryEvent(HistoryKind.CardDrawn, player) { Details = HistoryDetail.NotHandDraw, CardType = DrawnType(state), CardKnown = state.DrawnCard != null },
            CardEvent.Exhausted => new HistoryEvent(HistoryKind.CardExhausted, player),
            CardEvent.Discarded => new HistoryEvent(HistoryKind.CardDiscarded, player),
            CardEvent.Generated => new HistoryEvent(HistoryKind.CardGenerated, player),
            CardEvent.GeneratedStatus => new HistoryEvent(HistoryKind.CardGenerated, player) { CardType = SimCardType.Status },
            CardEvent.GeneratedSadness => new HistoryEvent(HistoryKind.CardGenerated, player) { CardType = SimCardType.Curse, Card = CardId.Sadness, CardKnown = true },
            CardEvent.EnergySpent => new HistoryEvent(HistoryKind.EnergySpent, player),
            CardEvent.StarsGained or CardEvent.StarsSpent => new HistoryEvent(HistoryKind.StarsModified, player),
            _ => (HistoryEvent?)null,
        };
        if (entry is { } logged) Record(state, logged);
    }

    /// <summary>
    /// The type of the card drawn as its profile has it: a Status when its profile says so (SimDrawCard.Status: the adapter marks the Status cards
    /// when something on the board reads them), else the type it read (none when nothing reads it).
    /// </summary>
    private static SimCardType DrawnType(SimState state) => state.DrawnCard is { Status: true } ? SimCardType.Status : state.DrawnCard?.Type ?? SimCardType.None;

    /// <summary>DamageReceivedEntry for a player: the HP they lost (Result.UnblockedDamage) and the damage's props.</summary>
    private static void RecordDamageReceived(SimState state, int player, SimProps props, int lost)
    {
        if (state.History.Count == 0) return;
        var details = lost > 0 ? HistoryDetail.Unblocked : HistoryDetail.None;
        if (props.IsPoweredAttack()) details |= HistoryDetail.PoweredAttack;
        Record(state, new HistoryEvent(HistoryKind.DamageReceived, player) { Details = details });
    }

    /// <summary>The enemies' turn starts: the counts of the players' turn start over (nothing of theirs happened this turn yet).</summary>
    private static void TurnCountsStartOver(SimState state)
    {
        foreach (var id in state.PlayerIds)
            if (state.Player(id) is { Turn.Count: > 0 }) state.MutablePlayer(id).Turn = NamedCounts.Empty;
    }
}
