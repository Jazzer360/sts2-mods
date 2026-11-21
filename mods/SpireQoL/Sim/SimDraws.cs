namespace SpireQoL.Sim;

/// <summary>
/// What about a card in your draw or discard pile matters if you draw it now (decision #90): its profile. Cards with the same
/// profile are interchangeable, so a draw branches once per profile, weighted by how many cards have it; everything that
/// doesn't matter is one plain profile. The adapter fills only what matters on this board (Status only with Iteration, Ethereal
/// only with Pagestorm...), so most piles are one or two profiles.
/// </summary>
public sealed record SimDrawCard(string Key)
{
    /// <summary>It acts in hand at the end of your turn (Burn, Regret...): SimEngine.HandAtTurnEnd.</summary>
    public TurnEndCard? TurnEnd { get; init; }
    /// <summary>Ethereal, and it exhausts at the end of your turn (Feel No Pain, Charon's Ashes...).</summary>
    public bool Fading { get; init; }
    /// <summary>Ethereal only through Hex (decision #85).</summary>
    public bool HexFading { get; init; }
    /// <summary>A Status (Iteration draws on the first one drawn each turn).</summary>
    public bool Status { get; init; }
    /// <summary>Ethereal (Pagestorm draws on each one drawn).</summary>
    public bool Ethereal { get; init; }
    /// <summary>A Strike while Hellraiser is up: it plays itself when drawn (SimEngine.HellraiserPlays), as <see cref="Auto"/>.</summary>
    public bool Strike { get; init; }
    /// <summary>Stampede or Simpleton could pick it once it's in your hand; Auto is how it plays (null: not simulated).</summary>
    public bool Pickable { get; init; }
    public SimHandCard? Auto { get; init; }
    /// <summary>Enchanted with Perfect Fit: a shuffle puts it on top (PerfectFit.ModifyShuffleOrder).</summary>
    public bool PerfectFit { get; init; }
    /// <summary>I Am Invincible: its block when it's the top card as your turn ends (it plays itself from there).</summary>
    public int InvincibleBlock { get; init; }
    /// <summary>Its type, while a card in your hand acts on the type of a card it draws (Escape Plan, Pillage: decision #118); else None.</summary>
    public SimCardType Type { get; init; }
    /// <summary>
    /// It costs 0 now (CostModifiers.All, not X), while a card in your hand acts on that (Scrape discards the rest: decision #118);
    /// null otherwise. <see cref="Sly"/>: it's Sly then (a Sly card plays itself when discarded: not simulated).
    /// </summary>
    public bool? Free { get; init; }
    public bool Sly { get; init; }
}

/// <summary>Cards of one profile in a pile, and how many.</summary>
public sealed record SimDrawGroup(SimDrawCard Card, int Count);

/// <summary>
/// Your draw pile as a perfect memory knows it (decision #90): cards known to be on top, in order (placed there), then the rest,
/// whose contents are known (the game lets you view the pile) but not their order; and your discard pile, shuffled in when the
/// draw pile runs out. Never changed in place (shared between branches): each draw makes a new one.
/// </summary>
public sealed class SimDrawPile
{
    public List<SimDrawCard> Top { get; init; } = new();
    public List<SimDrawGroup> Rest { get; init; } = new();
    public List<SimDrawGroup> Discard { get; init; } = new();

    public int RestCount => Rest.Sum(group => group.Count);
    public int Count => Top.Count + RestCount;
    public int DiscardCount => Discard.Sum(group => group.Count);

    /// <summary>The top card drawn.</summary>
    internal SimDrawPile WithoutTop() => new() { Top = Top.Skip(1).ToList(), Rest = Rest, Discard = Discard };

    /// <summary>One card of the group at <paramref name="groupIndex"/> drawn from the rest.</summary>
    internal SimDrawPile WithoutOneOf(int groupIndex) => new()
    {
        Top = Top,
        Rest = Rest.Select((group, index) => index == groupIndex ? group with { Count = group.Count - 1 } : group).Where(group => group.Count > 0).ToList(),
        Discard = Discard,
    };

    /// <summary>Some I Am Invincible in the pile.</summary>
    public bool HasInvincible => Top.Any(drawn => drawn.InvincibleBlock > 0) || Rest.Any(group => group.Card.InvincibleBlock > 0);

    /// <summary>A card played from the top goes to the discard pile.</summary>
    internal SimDrawPile WithDiscarded(SimDrawCard card) => new()
    {
        Top = Top, Rest = Rest,
        Discard = Discard.Any(group => group.Card.Key == card.Key)
            ? Discard.Select(group => group.Card.Key == card.Key ? group with { Count = group.Count + 1 } : group).ToList()
            : new List<SimDrawGroup>(Discard) { new(card, Count: 1) },
    };

    /// <summary>
    /// The discard pile shuffled in (CardPileCmd.ShuffleIfNecessary, the draw pile being empty): all of it, order unknown, except
    /// that a single card enchanted with Perfect Fit goes on top (with several, their order isn't known: none is).
    /// </summary>
    internal SimDrawPile Shuffled()
    {
        var fits = Discard.Where(group => group.Card.PerfectFit).ToList();
        if (fits.Count != 1 || fits[0].Count != 1) return new() { Top = new(), Rest = Discard.ToList(), Discard = new() };
        return new() { Top = new() { fits[0].Card }, Rest = Discard.Where(group => !group.Card.PerfectFit).ToList(), Discard = new() };
    }

    /// <summary>For the branch key: what's left, by profile.</summary>
    internal void Hash(Action<long> add)
    {
        add(Top.Count);
        foreach (var drawn in Top) add(drawn.Key.GetHashCode());
        foreach (var group in Rest) { add(group.Card.Key.GetHashCode()); add(group.Count); }
        add(DiscardCount);
    }
}
