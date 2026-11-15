using System.Text.Json.Serialization;

namespace SpireQoL.Sim;

/// <summary>
/// The powers on a creature, or a player's relic counters, as the simulator keeps them (docs/SIMULATOR_V2.md): name →
/// amount, plus one number of data a model may keep (a counter, a flag). Immutable: a change makes a new set, so branches
/// share a set until one of them changes it, and copying a state copies no powers. Order is kept (it's the game's hook
/// listener order: CombatState.IterateHookListeners runs a creature's powers in the order they were applied).
/// </summary>
public sealed class PowerSet
{
    public readonly record struct Entry(string Name, int Amount, int Data);

    public static readonly PowerSet Empty = new(Array.Empty<Entry>());

    private readonly Entry[] _items;

    [JsonConstructor]
    public PowerSet(Entry[] items) => _items = items ?? Array.Empty<Entry>();

    /// <summary>The entries, in order (JSON, listeners).</summary>
    public Entry[] Items => _items;

    [JsonIgnore]
    public int Count => _items.Length;

    public bool Has(string name) => IndexOf(name) >= 0;

    /// <summary>The amount (0 when absent).</summary>
    public int this[string name] => IndexOf(name) is var i and >= 0 ? _items[i].Amount : 0;

    public int DataOf(string name) => IndexOf(name) is var i and >= 0 ? _items[i].Data : 0;

    private int IndexOf(string name)
    {
        for (var i = 0; i < _items.Length; i++)
            if (ReferenceEquals(_items[i].Name, name) || _items[i].Name == name) return i;
        return -1;
    }

    /// <summary>The set with <paramref name="name"/> at this amount and data (added last if new); removed when <paramref name="remove"/>.</summary>
    public PowerSet With(string name, int amount, int? data = null, bool remove = false)
    {
        var i = IndexOf(name);
        if (remove)
        {
            if (i < 0) return this;
            var less = new Entry[_items.Length - 1];
            Array.Copy(_items, 0, less, 0, i);
            Array.Copy(_items, i + 1, less, i, _items.Length - i - 1);
            return new PowerSet(less);
        }
        if (i >= 0)
        {
            var e = _items[i];
            var d = data ?? e.Data;
            if (e.Amount == amount && e.Data == d) return this;
            var copy = (Entry[])_items.Clone();
            copy[i] = new Entry(name, amount, d);
            return new PowerSet(copy);
        }
        var more = new Entry[_items.Length + 1];
        Array.Copy(_items, more, _items.Length);
        more[^1] = new Entry(name, amount, data ?? 0);
        return new PowerSet(more);
    }

    /// <summary>The set with <paramref name="delta"/> added to the amount (added if new).</summary>
    public PowerSet Add(string name, int delta) => delta == 0 && Has(name) ? this : With(name, this[name] + delta);

    public PowerSet Without(string name) => With(name, 0, remove: true);

    /// <summary>For branch keys: every entry's name and numbers.</summary>
    public void Hash(Action<long> add)
    {
        add(_items.Length);
        foreach (var e in _items)
        {
            add(e.Name.GetHashCode());
            add(e.Amount);
            add(e.Data);
        }
    }

    public override string ToString() => string.Join(", ", _items.Select(e => e.Data != 0 ? $"{e.Name} {e.Amount} ({e.Data})" : $"{e.Name} {e.Amount}"));
}
