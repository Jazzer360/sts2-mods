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

    /// <summary>Block a play gave its player from evoking Frost orbs (the log's amount), for the play audit's block check.</summary>
    public const string FrostEvokeBlock = "FrostOrb";

    /// <summary>Damage a play's orb evokes dealt the enemies (block and HP: the log's amount), for the play audit's damage with no card.</summary>
    public const string OrbEvokeDamage = "OrbEvokeDamage";

    /// <summary>Orbs channeled during a play (OrbChanneledEntry, every player's), for the play audit's channel count.</summary>
    public const string PlayOrbChanneled = "OrbChanneled";

    private static readonly string[] EventNames = Enum.GetValues<CardEvent>().Select(cardEvent => "event:" + cardEvent).ToArray();

    /// <summary>
    /// A card event of the play's own player, in the play log (decision #189): over every play of a replay and what follows it, the least and
    /// the most over its outcomes (SimOutcome.PlayFiredEvery, PlayFiredAny), for the play check's counts: "event:Exhausted".
    /// </summary>
    public static string PlayEvent(CardEvent cardEvent) => EventNames[(int)cardEvent];

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

/// <summary>How many of each card event the current play has caused for its player (Unceasing Top: nothing drawn or created). Immutable.</summary>
public sealed class CardEventCounts
{
    public static readonly CardEventCounts None = new(new int[Enum.GetValues<CardEvent>().Length]);

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
