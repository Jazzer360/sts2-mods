using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using SpireQoL.Procs.Programs;

namespace SpireQoL.Sim;

/// <summary>
/// The history ledger's seed (decision #191): which questions to the game's log of the fight this board's cards, relics and powers ask
/// (read from their code: ProgramReader.HistoryQueries, translated by HistoryQueryReader), and each player's count of each from the
/// game's log, taken once, in the snapshot the rest of the start state is read in. The simulator counts forward from there; it never
/// reads the game's log while it runs.
/// </summary>
internal static class HistoryLedger
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IReadOnlyList<HistoryQuery>> Cache = new();

    /// <summary>The questions a model type's code asks that the ledger counts (its reviewed gaps left out: HistoryQueryReader.GapKinds).</summary>
    internal static IReadOnlyList<HistoryQuery> QueriesOf(Type type) => Cache.GetOrAdd(type, owner =>
    {
        try
        {
            return ProgramReader.HistoryQueries(owner, RuntimeCode.Read)
                .SelectMany(found => HistoryQueryReader.Read((QueryValue)found.Query).Counted)
                .Where(query => !query.IsTurnPlayCount).Distinct().ToList();
        }
        catch
        {
            return Array.Empty<HistoryQuery>();
        }
    });

    /// <summary>
    /// The questions the fight's models ask: every player's cards (every pile), the powers their cards apply and the cards they make, every
    /// creature's powers, every player's relics.
    /// </summary>
    internal static IReadOnlyList<HistoryQuery> QueriesFor(ICombatState combat)
    {
        var types = new HashSet<Type>();
        foreach (var player in combat.Players)
        {
            foreach (var card in player.PlayerCombatState?.AllCards ?? Enumerable.Empty<CardModel>())
            {
                if (!types.Add(card.GetType())) continue;
                foreach (var step in CardPrograms.Of(card.GetType()).All())
                {
                    var made = step switch
                    {
                        CommandStep { Command: { DeclaringType: "PowerCmd", Name: "Apply", GenericArguments: [var power] } } => "MegaCrit.Sts2.Core.Models.Powers." + power,
                        CreateStep create => "MegaCrit.Sts2.Core.Models.Cards." + create.Card,
                        _ => null,
                    };
                    if (made != null && typeof(CardModel).Assembly.GetType(made) is { } madeType) types.Add(madeType);
                }
            }
            foreach (var relic in player.Relics) types.Add(relic.GetType());
        }
        foreach (var creature in combat.Creatures)
            foreach (var power in creature.Powers)
                types.Add(power.GetType());
        var queries = new List<HistoryQuery>();
        foreach (var type in types)
            foreach (var query in QueriesOf(type))
                if (!queries.Contains(query)) queries.Add(query);
        return queries;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IReadOnlyDictionary<string, ProgramValue>> Multipliers = new();

    /// <summary>
    /// A card's calculated value whose multiplier is a count of the fight's log the ledger counts (Finisher's CalculatedHits: Attacks finished
    /// this turn; Murder's CalculatedDamage: cards drawn): its question, what each entry adds (its extra value, times the multiplier's own
    /// factor), and the count now, for its owner. Null for any other value.
    /// </summary>
    internal static LoggedAmount? LoggedOf(CardModel card, string name, ICombatState combat)
    {
        try
        {
            if (!card.DynamicVars.TryGetValue(name, out var dynamicVar) || dynamicVar is not MegaCrit.Sts2.Core.Localization.DynamicVars.CalculatedVar calculated || card.Owner is not { } owner) return null;
            var multipliers = Multipliers.GetOrAdd(card.GetType(), type => ProgramReader.CalculatedMultipliers(type, RuntimeCode.Read));
            if (!multipliers.TryGetValue(name, out var multiplier) || Linear(multiplier) is not var (question, factor) || HistoryQueryReader.Read(question).Query is not { } query) return null;
            var extra = typeof(MegaCrit.Sts2.Core.Localization.DynamicVars.CalculatedVar)
                .GetMethod("GetExtraVar", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(calculated, null) as MegaCrit.Sts2.Core.Localization.DynamicVars.DynamicVar;
            if (extra == null) return null;
            var turn = NamedCounts.Empty;
            var fight = Seed(new[] { query }, owner, combat, ref turn);
            return new LoggedAmount(query, extra.BaseValue * factor, (query.Scope == HistoryScope.ThisTurn ? turn : fight)[query.Key]);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A multiplier that is a count of the log (Count(...)), plus a constant (1 + Count(...): Rattle, Tear Asunder): the count and its factor.</summary>
    private static (QueryValue Count, decimal Factor)? Linear(ProgramValue value) => value switch
    {
        QueryValue { Type: "Enumerable", Name: "Count" } count when ProgramReader.ReadsHistory(count) => (count, 1m),
        ArithmeticValue { Op: "add", Left: ConstantValue, Right: var counted } => Linear(counted),
        ArithmeticValue { Op: "add", Left: var counted, Right: ConstantValue } => Linear(counted),
        _ => null,
    };

    /// <summary>A player's counts of the questions, from the game's log now: this turn's into <paramref name="turn"/>, the fight's returned.</summary>
    internal static NamedCounts Seed(IReadOnlyList<HistoryQuery> queries, Player player, ICombatState combat, ref NamedCounts turn)
    {
        var fight = NamedCounts.Empty;
        if (queries.Count == 0) return fight;
        CombatHistory history;
        try { history = CombatManager.Instance.History; }
        catch { return fight; }
        foreach (var query in queries)
        {
            var count = 0;
            foreach (var entry in history.Entries)
                if (Counts(query, entry, player, combat)) count++;
            if (query.Scope == HistoryScope.ThisTurn) turn = turn.With(query.Key, count);
            else fight = fight.With(query.Key, count);
        }
        return fight;
    }

    /// <summary>
    /// Whether <paramref name="query"/> counts the entry for <paramref name="player"/>: the game's own test of each entry, written once per
    /// test kind (whose it is, when, its detail), held to the code's own question by the catalog's seed test (HistorySeedTests).
    /// </summary>
    internal static bool Counts(HistoryQuery query, CombatHistoryEntry entry, Player player, ICombatState combat)
    {
        if (KindOf(entry) != query.Kind) return false;
        if (query.Scope == HistoryScope.ThisTurn && !entry.HappenedThisTurn(combat)) return false;
        switch (query.Whose)
        {
            case HistoryWhose.Owner when !OwnersEntry(entry, player):
            case HistoryWhose.OwnersOsty when player.Osty == null || entry.Actor != player.Osty:
                return false;
        }
        var play = entry switch { CardPlayStartedEntry started => started.CardPlay, CardPlayFinishedEntry finished => finished.CardPlay, _ => null };
        var details = query.Details;
        if (details.HasFlag(HistoryDetail.FirstInSeries) && play?.IsFirstInSeries != true) return false;
        if (details.HasFlag(HistoryDetail.NotAutoPlay) && play?.IsAutoPlay != false) return false;
        if (details.HasFlag(HistoryDetail.ZeroEnergy) && play?.Resources.EnergyValue != 0) return false;
        if (details.HasFlag(HistoryDetail.Ethereal) && entry is not CardPlayFinishedEntry { WasEthereal: true }) return false;
        if (details.HasFlag(HistoryDetail.NotHandDraw) && entry is not CardDrawnEntry { FromHandDraw: false }) return false;
        if (details.HasFlag(HistoryDetail.Unblocked) && entry is not DamageReceivedEntry { Result.UnblockedDamage: > 0 }) return false;
        if (details.HasFlag(HistoryDetail.PoweredAttack) && !(entry is DamageReceivedEntry damage && damage.Result.Props.IsPoweredAttack())) return false;
        var card = play?.Card ?? entry switch
        {
            CardDrawnEntry drawn => drawn.Card,
            CardExhaustedEntry exhausted => exhausted.Card,
            CardDiscardedEntry discarded => discarded.Card,
            CardGeneratedEntry generated => generated.Card,
            _ => null,
        };
        if (query.CardType != SimCardType.None && (card == null || SimReaders.TypeOf(card) != query.CardType)) return false;
        if (query.CardTag is { } tag && (card == null || !SimReaders.TagsOf(card).Contains(tag))) return false;
        if (query.Card is { } cardClass && (card == null || !IsA(card.GetType(), cardClass.Name))) return false;
        if (query.Power is { } power && !(entry is PowerReceivedEntry received && IsA(received.Power.GetType(), power.Name))) return false;
        if (query.Orb is { } orb && !(entry is OrbChanneledEntry channeled && SimAdapter.KindOf(channeled.Orb) == orb)) return false;
        return true;
    }

    /// <summary>The entry is the player's, as its kind's own creature or player says (the play's player, who made the card, who applied it...).</summary>
    private static bool OwnersEntry(CombatHistoryEntry entry, Player player) => entry switch
    {
        CardPlayStartedEntry started => started.CardPlay.Player == player,
        CardPlayFinishedEntry finished => finished.CardPlay.Player == player,
        CardGeneratedEntry generated => generated.Creator == player,
        PowerReceivedEntry received => received.Applier == player.Creature,
        _ => entry.Actor == player.Creature,
    };

    private static bool IsA(Type type, string className)
    {
        for (var current = type; current != null; current = current.BaseType)
            if (current.Name == className) return true;
        return false;
    }

    /// <summary>An entry's kind, by its class (CardExhaustedEntry: CardExhausted), worked out once per class.</summary>
    private static HistoryKind? KindOf(CombatHistoryEntry entry) => Kinds.GetOrAdd(entry.GetType(), type =>
        type.Name.EndsWith("Entry", StringComparison.Ordinal) && Enum.TryParse<HistoryKind>(type.Name[..^"Entry".Length], out var kind) ? kind : null);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, HistoryKind?> Kinds = new();
}
