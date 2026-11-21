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
    private static readonly ConcurrentDictionary<(PlayStage, string Tag), string> TagNames = new();
    private static readonly string[][] TypeNames = Enum.GetValues<PlayStage>()
        .Select(stage => Enum.GetValues<SimCardType>().Select(type => $"{stage}:{type}").ToArray()).ToArray();

    /// <summary>A source's entry under its owner: "Name@id" (an enemy's or a teammate's power, a model bound to its owner's play).</summary>
    public static string Owned(string name, int owner) => OwnedNames.GetOrAdd((name, owner), k => k.Name + "@" + k.Owner);

    /// <summary>The play log's entry for a firing set off by a card event (the Indirect procs setting): "~" before its name.</summary>
    public static string Indirect(string key) => IndirectNames.GetOrAdd(key, k => "~" + k);

    /// <summary>The play log's entry for a source: "Name" for yours, "Name@id" for anyone else's (an enemy's is also under "Name").</summary>
    public static string Logged(string name, int? owner) => owner is { } id && id != Who.You ? Owned(name, id) : name;

    /// <summary>Every play of the turn at this stage: "Started", "Finished".</summary>
    public static string Plays(PlayStage stage) => stage == PlayStage.Started ? "Started" : "Finished";

    /// <summary>The turn's plays of one card type: "Started:Attack", "Finished:Skill".</summary>
    public static string Plays(PlayStage stage, SimCardType type) => TypeNames[(int)stage][(int)type];

    /// <summary>The turn's plays of cards with this tag: "Finished#Shiv", "Started#Wah".</summary>
    public static string Plays(PlayStage stage, string tag) => TagNames.GetOrAdd((stage, tag), k => (k.Item1 == PlayStage.Started ? "Started#" : "Finished#") + k.Tag);

    /// <summary>Block gains from card plays this turn (Unmovable counts the earlier ones).</summary>
    public const string CardBlock = "CardBlock";

    /// <summary>A player's turn counts with one more play at this stage: in all, of its type, and of each of its tags.</summary>
    public static PowerSet CountPlay(PowerSet turn, PlayStage stage, SimCardType type, IEnumerable<string> tags)
    {
        var t = turn.Add(Plays(stage), 1).Add(Plays(stage, type), 1);
        foreach (var tag in tags) t = t.Add(Plays(stage, tag), 1);
        return t;
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

    public CardEventCounts Add(CardEvent ev, int n = 1)
    {
        var counts = (int[])_counts.Clone();
        counts[(int)ev] += n;
        return new CardEventCounts(counts);
    }
}
