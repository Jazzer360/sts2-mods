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
    /// <summary>Energy it takes from you when drawn (Void: its own AfterCardDrawn, PlayerCmd.LoseEnergy).</summary>
    public int EnergyLostOnDraw { get; init; }
    /// <summary>It acts when it's exhausted (SimPlayer.ExhaustingInHand once drawn; its HandCard is set then).</summary>
    public ExhaustingCard? Exhausting { get; init; }
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
    /// <summary>
    /// How it plays when auto-played from the pile itself (CardPileCmd.AutoPlayFromDrawPile: Havoc, Cascade, The Devil, Lost Lute),
    /// while something that does that is in your hand or on you: as built for a card that isn't in your hand (no hand card, its hand
    /// delta its own). No plays: it goes to the discard pile unplayed (Unplayable, or aimed at a teammate). Null: not simulated (`?`).
    /// FromPileUpgraded: the same, upgraded first (The Devil+).
    /// </summary>
    public SimHandCard? FromPile { get; init; }
    public SimHandCard? FromPileUpgraded { get; init; }
    /// <summary>Its play exhausts it (SimAdapter.ExhaustsAfterPlay): played from the pile, it goes to the exhaust pile, not the discard pile.</summary>
    public bool ExhaustsWhenPlayed { get; init; }
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
    /// <summary>
    /// The filtered takes from your hand that would take it once it's there (TakeFromHandAction.Filter: Second Wind's non-Attacks, The
    /// Hermit's Curse), while a card in your hand takes that way (decision #189); null otherwise.
    /// </summary>
    public IReadOnlyList<string>? TakenBy { get; init; }

    /// <summary>
    /// Nothing about it is followed (every field but its key at its default: the adapter's plain profile): moving it changes only a
    /// pile's count. A choice inside a play takes such a card when it can (decision #167).
    /// </summary>
    public bool Plain => this == new SimDrawCard(Key);
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

    /// <summary>
    /// Cards a pick took from the pile with no profile taken for them (Cleanse: you choose which, among cards that differ; decision
    /// #188, a pick is a card, never a particular one): the pile holds that many fewer, each draw still weighted by the profiles' counts.
    /// </summary>
    public int Picked { get; init; }

    public int RestCount => Rest.Sum(group => group.Count);
    public int Count => Math.Max(0, Top.Count + RestCount - Picked);
    public int DiscardCount => Discard.Sum(group => group.Count);

    /// <summary>The top card drawn.</summary>
    internal SimDrawPile WithoutTop() => WithoutTopAt(0);

    /// <summary>The known top card at <paramref name="index"/> taken out (the cards above it stay on top, in order).</summary>
    internal SimDrawPile WithoutTopAt(int index) => new() { Top = Top.Where((_, at) => at != index).ToList(), Rest = Rest, Discard = Discard, Picked = Picked };

    /// <summary>One card a pick took, which isn't followed (Picked).</summary>
    internal SimDrawPile WithOnePicked() => new() { Top = Top, Rest = Rest, Discard = Discard, Picked = Picked + 1 };

    /// <summary>One card of the group at <paramref name="groupIndex"/> drawn from the rest.</summary>
    internal SimDrawPile WithoutOneOf(int groupIndex) => new()
    {
        Top = Top,
        Rest = Rest.Select((group, index) => index == groupIndex ? group with { Count = group.Count - 1 } : group).Where(group => group.Count > 0).ToList(),
        Discard = Discard,
        Picked = Picked,
    };

    /// <summary>One card of the discard pile's group at <paramref name="groupIndex"/> taken out.</summary>
    internal SimDrawPile WithoutOneDiscarded(int groupIndex) => new()
    {
        Top = Top, Rest = Rest,
        Discard = Discard.Select((group, index) => index == groupIndex ? group with { Count = group.Count - 1 } : group).Where(group => group.Count > 0).ToList(),
        Picked = Picked,
    };

    /// <summary>A card put among the known top cards, at <paramref name="index"/> (the ones above it stay above).</summary>
    internal SimDrawPile WithTopAt(int index, SimDrawCard card)
    {
        var top = new List<SimDrawCard>(Top);
        top.Insert(index, card);
        return new() { Top = top, Rest = Rest, Discard = Discard, Picked = Picked };
    }

    /// <summary>A card put somewhere in the unordered rest.</summary>
    internal SimDrawPile WithInRest(SimDrawCard card) => new()
    {
        Top = Top,
        Rest = Rest.Any(group => group.Card.Key == card.Key)
            ? Rest.Select(group => group.Card.Key == card.Key ? group with { Count = group.Count + 1 } : group).ToList()
            : Rest.Append(new SimDrawGroup(card, Count: 1)).OrderBy(group => group.Card.Key, StringComparer.Ordinal).ToList(),
        Discard = Discard,
        Picked = Picked,
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
        Picked = Picked,
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

    /// <summary>
    /// CardPileCmd.Shuffle: every card of the draw pile (its known top too) and the discard pile, with <paramref name="added"/> put
    /// into the draw pile first (Reboot: your hand), shuffled together: order unknown, except a single Perfect Fit card on top.
    /// </summary>
    internal SimDrawPile ShuffledWithDiscard(IEnumerable<SimDrawGroup> added)
    {
        var all = Top.Select(card => new SimDrawGroup(card, Count: 1)).Concat(Rest).Concat(Discard).Concat(added)
            .GroupBy(group => group.Card.Key).Select(sameProfile => new SimDrawGroup(sameProfile.First().Card, sameProfile.Sum(group => group.Count)))
            .Where(group => group.Count > 0).OrderBy(group => group.Card.Key, StringComparer.Ordinal).ToList();
        // The cards a pick took stay gone: which ones isn't known, so the shuffled pile holds that many fewer.
        var shuffled = new SimDrawPile { Discard = all }.Shuffled();
        return new SimDrawPile { Top = shuffled.Top, Rest = shuffled.Rest, Discard = shuffled.Discard, Picked = Picked };
    }

    /// <summary>For the branch key: what's left, by profile.</summary>
    internal void Hash(ref BranchKey key)
    {
        key.Add(Top.Count);
        foreach (var drawn in Top) key.Add(drawn.Key.GetHashCode());
        foreach (var group in Rest) { key.Add(group.Card.Key.GetHashCode()); key.Add(group.Count); }
        var discarded = 0;
        foreach (var group in Discard) discarded += group.Count;
        key.Add(discarded);
        key.Add(Picked);
    }
}
