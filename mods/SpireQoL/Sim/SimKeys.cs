using System.Collections.Concurrent;

namespace SpireQoL.Sim;

/// <summary>When a play is counted in a player's turn counts (SimPlayer.Turn): as it starts (CardPlaysStarted), or once it's finished.</summary>
public enum PlayStage { Started, Finished }

/// <summary>
/// The names entries go under in the state's named counts (PowerSet): the play log (SimState.Fired), what models bound to a play
/// (SimState.Bound) and each player's turn counts (SimPlayer.Turn). Each name's shape is written here once, and the writers and
/// readers all ask for it here. Every name is made once (reading a count allocates nothing) and stays what replay cases and
/// reports already hold.
/// </summary>
public static class SimKeys
{
    private static readonly ConcurrentDictionary<(string Name, int Owner), string> OwnedNames = new();
    private static readonly ConcurrentDictionary<string, string> IndirectNames = new();

    private static readonly string[][] TypeNames = Enum.GetValues<PlayStage>()
        .Select(stage => Enum.GetValues<SimCardType>().Select(type => $"{stage}:{type}").ToArray()).ToArray();

    /// <summary>A source's entry under its owner: "Name@id" (an enemy's or a teammate's power, a model bound to its owner's play).</summary>
    public static string Owned(string name, int owner) => OwnedNames.GetOrAdd((name, owner), nameOwner => nameOwner.Name + "@" + nameOwner.Owner);

    /// <summary>The play log's entry for a firing set off by a card event (the Indirect procs setting): "~" before its name.</summary>
    public static string Indirect(string key) => IndirectNames.GetOrAdd(key, plain => "~" + plain);

    private static readonly ConcurrentDictionary<string, string> UnknownFlashNames = new();

    /// <summary>
    /// The flash log's entry for a flash its code calls on a condition the reader doesn't read (FlashStep.Unknown): "?" before the source's
    /// entry. It may have flashed or not; only the play check asks, and treats it as not known.
    /// </summary>
    public static string UnknownFlash(string key) => UnknownFlashNames.GetOrAdd(key, plain => "?" + plain);

    private static readonly ConcurrentDictionary<string, string> SetUpNames = new();

    /// <summary>
    /// The play log's name for a payout the play decides being set up by it (decision #250: Tune's free card on the turn's first Ki): "+" before
    /// the source's name, logged as the source's own firings are (SimState.LogFired: "+Name", "+Name@id").
    /// </summary>
    public static string SetUp(string name) => SetUpNames.GetOrAdd(name, plain => "+" + plain);

    /// <summary>The play log's entry for a source: "Name" for yours, "Name@id" for anyone else's (an enemy's is also under "Name").</summary>
    public static string Logged(string name, int? owner) => owner is { } id && id != Who.You ? Owned(name, id) : name;

    /// <summary>Every play of the turn at this stage: "Started", "Finished".</summary>
    public static string Plays(PlayStage stage) => stage == PlayStage.Started ? "Started" : "Finished";

    /// <summary>The turn's plays of one card type: "Started:Attack", "Finished:Skill".</summary>
    public static string Plays(PlayStage stage, SimCardType type) => TypeNames[(int)stage][(int)type];

    private static readonly string[][] TagNames = Enum.GetValues<PlayStage>()
        .Select(stage => Enum.GetValues<SimCardTag>().Select(tag => $"{stage}#{tag}").ToArray()).ToArray();

    /// <summary>The turn's plays of cards with this tag: "Finished#Shiv", "Started#Wah".</summary>
    public static string Plays(PlayStage stage, SimCardTag tag) => TagNames[(int)stage][(int)tag];

    /// <summary>Block gains from card plays this turn (Unmovable counts the earlier ones).</summary>
    public const string CardBlock = "CardBlock";

    /// <summary>Block a play gave its player from evoking orbs (Frost's: the log's amount), for the play audit's block check.</summary>
    public const string OrbEvokeBlock = "OrbEvokeBlock";

    /// <summary>Damage a play's orb evokes dealt the enemies (block and HP: the log's amount), for the play audit's damage with no card.</summary>
    public const string OrbEvokeDamage = "OrbEvokeDamage";

    /// <summary>Orbs channeled during a play (OrbChanneledEntry, every player's), for the play audit's channel count.</summary>
    public const string PlayOrbChanneled = "OrbChanneled";

    /// <summary>Each event's key by its number (CardEvent's numbers have gaps: a number no event has is null).</summary>
    private static readonly string?[] EventNames = Enumerable.Range(0, CardEventCounts.Size)
        .Select(number => Enum.IsDefined(typeof(CardEvent), number) ? "event:" + (CardEvent)number : null).ToArray();

    /// <summary>
    /// A card event of the play's own player, in the play log (decision #189): over every play of a replay and what follows it, the least and
    /// the most over its outcomes (SimOutcome.PlayFiredEvery, PlayFiredAny), for the play check's counts: "event:Exhausted".
    /// </summary>
    public static string PlayEvent(CardEvent cardEvent) => EventNames[(int)cardEvent]!;

    /// <summary>A player's turn counts with one more play at this stage: in all, of its type, and of each of its tags.</summary>
    public static NamedCounts CountPlay(NamedCounts turn, PlayStage stage, SimCardType type, IEnumerable<SimCardTag> tags)
    {
        var counts = turn.Add(Plays(stage), delta: 1).Add(Plays(stage, type), delta: 1);
        foreach (var tag in tags) counts = counts.Add(Plays(stage, tag), delta: 1);
        return counts;
    }
}

/// <summary>A card event raised inside a hit or a death, run once it's over (SimEngine.Raise, DrainPending).</summary>
/// <param name="Source">The model that raised it, when its tab counts what it gives (a draw or energy tab: decision #132).</param>
public sealed record PendingEvent(CardEvent Event, int Player, string? Source, int Count);

/// <summary>A power that landed where hooks can't branch (inside a hit or a death), its AfterPowerApplied listeners told once that's over.</summary>
public sealed record PendingPower(PowerId Power, int Amount, int Applier, int Target, bool Debuff);

/// <summary>How many of each card event the current play has caused for its player (Unceasing Top: nothing drawn or created). Immutable.</summary>
public sealed class CardEventCounts
{
    /// <summary>One past the largest event number (CardEvent keeps its numbers, with gaps).</summary>
    internal static readonly int Size = Enum.GetValues<CardEvent>().Max(cardEvent => (int)cardEvent) + 1;

    public static readonly CardEventCounts None = new(new int[Size]);

    private readonly int[] _counts;

    private CardEventCounts(int[] counts) => _counts = counts;

    public int this[CardEvent ev] => _counts[(int)ev];

    public CardEventCounts Add(CardEvent cardEvent, int n = 1)
    {
        var counts = (int[])_counts.Clone();
        counts[(int)cardEvent] += n;
        return new CardEventCounts(counts);
    }
}
