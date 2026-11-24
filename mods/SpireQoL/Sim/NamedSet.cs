global using PowerSet = SpireQoL.Sim.NamedSet<SpireQoL.Sim.PowerId>;
global using RelicSet = SpireQoL.Sim.NamedSet<SpireQoL.Sim.RelicId>;
global using CardSet = SpireQoL.Sim.NamedSet<SpireQoL.Sim.CardId>;
global using NamedCounts = SpireQoL.Sim.NamedSet<string>;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpireQoL.Sim;

/// <summary>
/// The simulator's named numbers: name → amount, plus one number of data a model may keep (a counter, a flag). Four kinds, each
/// its own type, so one can't stand in for another:
/// <list type="bullet">
/// <item><see cref="PowerSet"/>: the powers on a creature, by <see cref="PowerId"/>, in the order they were applied (the game's hook
/// listener order: CombatState.IterateHookListeners runs a creature's powers in that order).</item>
/// <item><see cref="RelicSet"/>: a player's relics, by <see cref="RelicId"/>, with each one's counter and flag.</item>
/// <item><see cref="CardSet"/>: the cards in a player's piles that listen for a hook the simulator models, by <see cref="CardId"/>,
/// with how many copies (SimPlayer.Cards).</item>
/// <item><see cref="NamedCounts"/>: counts under names <see cref="SimKeys"/> builds (the play log, what's bound to a play, a player's
/// turn counts).</item>
/// </list>
/// Immutable: a change makes a new set, so branches share a set until one of them changes it, and copying a state copies none.
/// </summary>
public sealed class NamedSet<TName> where TName : IEquatable<TName>
{
    public readonly record struct Entry(TName Name, int Amount, int Data);

    public static readonly NamedSet<TName> Empty = new(Array.Empty<Entry>());

    private readonly Entry[] _items;

    [JsonConstructor]
    public NamedSet(Entry[] items) => _items = items ?? Array.Empty<Entry>();

    /// <summary>The entries, in order (JSON, listeners).</summary>
    public Entry[] Items => _items;

    [JsonIgnore]
    public int Count => _items.Length;

    public bool Has(TName name) => IndexOf(name) >= 0;

    /// <summary>The amount (0 when absent).</summary>
    public int this[TName name] => IndexOf(name) is var index and >= 0 ? _items[index].Amount : 0;

    public int DataOf(TName name) => IndexOf(name) is var index and >= 0 ? _items[index].Data : 0;

    private int IndexOf(TName name)
    {
        for (var i = 0; i < _items.Length; i++)
            if (_items[i].Name.Equals(name)) return i;
        return -1;
    }

    /// <summary>The set with <paramref name="name"/> at this amount and data (added last if new); removed when <paramref name="remove"/>.</summary>
    public NamedSet<TName> With(TName name, int amount, int? data = null, bool remove = false)
    {
        var index = IndexOf(name);
        if (remove)
        {
            if (index < 0) return this;
            var less = new Entry[_items.Length - 1];
            Array.Copy(_items, 0, less, 0, index);
            Array.Copy(_items, index + 1, less, index, _items.Length - index - 1);
            return new NamedSet<TName>(less);
        }
        if (index >= 0)
        {
            var entry = _items[index];
            var newData = data ?? entry.Data;
            if (entry.Amount == amount && entry.Data == newData) return this;
            var copy = (Entry[])_items.Clone();
            copy[index] = new Entry(name, amount, newData);
            return new NamedSet<TName>(copy);
        }
        var more = new Entry[_items.Length + 1];
        Array.Copy(_items, more, _items.Length);
        more[^1] = new Entry(name, amount, data ?? 0);
        return new NamedSet<TName>(more);
    }

    /// <summary>The set with <paramref name="delta"/> added to the amount (added if new).</summary>
    public NamedSet<TName> Add(TName name, int delta) => delta == 0 && Has(name) ? this : With(name, this[name] + delta);

    public NamedSet<TName> Without(TName name) => With(name, amount: 0, remove: true);

    /// <summary>For branch keys: every entry's name and numbers.</summary>
    public void Hash(ref BranchKey key)
    {
        key.Add(_items.Length);
        foreach (var entry in _items)
        {
            key.Add(entry.Name.GetHashCode());
            key.Add(entry.Amount);
            key.Add(entry.Data);
        }
    }

    public override string ToString() => string.Join(", ", _items.Select(entry => entry.Data != 0 ? $"{entry.Name} {entry.Amount} ({entry.Data})" : $"{entry.Name} {entry.Amount}"));
}

/// <summary>
/// A power, by the name of the game's class (ThornsPower): <c>PowerId.Thorns</c> for each one the game has (GameNames.g.cs), and
/// <c>new PowerId("X")</c> for one it adds after that list was made (the simulator keeps every power on a creature, modelled or not).
/// </summary>
[JsonConverter(typeof(SimNameJson<PowerId>))]
public readonly partial record struct PowerId(string Name) : ISimName<PowerId>
{
    public static PowerId Of(string name) => new(name);
    public override string ToString() => Name;
}

/// <summary>A relic, by the name of the game's class (PenNib): <c>RelicId.PenNib</c> for each one the game has (GameNames.g.cs).</summary>
[JsonConverter(typeof(SimNameJson<RelicId>))]
public readonly partial record struct RelicId(string Name) : ISimName<RelicId>
{
    public static RelicId Of(string name) => new(name);
    public override string ToString() => Name;
}

/// <summary>A typed name over the game's class name.</summary>
public interface ISimName<TSelf> : IEquatable<TSelf> where TSelf : ISimName<TSelf>
{
    string Name { get; }
    static abstract TSelf Of(string name);
}

/// <summary>A typed name in JSON is its plain name, as replay cases and reports have always held it.</summary>
public sealed class SimNameJson<T> : JsonConverter<T> where T : ISimName<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => T.Of(reader.GetString() ?? "");
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(value.Name);
}

/// <summary>A card, by the name of the game's class (Duet): <c>CardId.Duet</c> for each one the game has (GameNames.g.cs).</summary>
[JsonConverter(typeof(SimNameJson<CardId>))]
public readonly partial record struct CardId(string Name) : ISimName<CardId>
{
    public static CardId Of(string name) => new(name);
    public override string ToString() => Name;
}
