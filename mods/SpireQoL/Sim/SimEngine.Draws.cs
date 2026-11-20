namespace SpireQoL.Sim;

public static partial class SimEngine
{
    /// <summary>CardPile.MaxCardsInHand.</summary>
    public const int MaxHand = 10;

    /// <summary>A player's draws go through their draw pile (decision #90): only yours, when the adapter filled it.</summary>
    internal static bool DrawsFromPile(SimState s, int player) => PlayerOf(s, player)?.Draw != null;

    /// <summary>
    /// <paramref name="count"/> draws, one after another (CardPileCmd.Draw), each with its reactions before the next: the draw
    /// reactions (Speedster, Corrosive Wave, Automation...) and the draws they chain (Iteration, Pagestorm) come first, so with
    /// a hand nearly full the order is the game's.
    /// </summary>
    internal static IEnumerable<SimState> DrawCards(SimState s, int player, int count, bool indirect)
    {
        IEnumerable<SimState> r = new[] { s };
        for (var i = 0; i < count; i++) r = Then(r, x => DrawOne(x, player, indirect));
        return r;
    }

    /// <summary>
    /// A draw the card acts on (DrawnCardAction, decision #118): the draws, recording the cards they gave (not the ones their
    /// reactions chain: CardPileCmd.Draw returns its own), then the card's reaction to them, all in one step.
    /// </summary>
    internal static IEnumerable<SimState> RunDrawnCard(SimState s, DrawnCardAction da)
    {
        var player = s.CurrentPlay?.Owner ?? Who.You;
        if (!DrawsFromPile(s, player))
        {
            // No pile read (a teammate's): the draws are counted, and what they gave isn't known.
            s.Unmodelled ??= "a card that acts on what it draws, with no draw pile to draw from";
            return ProcCardEvent(s, CardEvent.Drawn, da.Count, player, da.Indirect);
        }
        s.DrawRecord = new List<(SimDrawCard, int)>();
        IEnumerable<SimState> r = new[] { s };
        if (da.DrawAgainIf != SimCardType.None)
        {
            IEnumerable<SimState> Again(SimState x)
            {
                var before = x.DrawRecord!.Count;
                return Then(DrawOne(x, player, da.Indirect, record: true), y =>
                    y.DrawRecord!.Count > before && y.DrawRecord[^1].Card.Type == da.DrawAgainIf
                    && PlayerOf(y, player)!.HandCount - (y.PlayFromHand ? 1 : 0) < MaxHand ? Again(y) : new[] { y });
            }
            r = Again(s);
        }
        else
            for (var i = 0; i < da.Count; i++) r = Then(r, x => DrawOne(x, player, da.Indirect, record: true));
        return Then(r, x =>
        {
            var drawn = x.DrawRecord!;
            x.DrawRecord = null;
            // What its chance tab counts (decision #119).
            x.DrawnTally = da.Block != null ? (drawn.Count > 0 && drawn[0].Card.Type == da.BlockIf ? 1 : 0)
                : da.DrawAgainIf != SimCardType.None ? drawn.Count(d => d.Card.Type == da.DrawAgainIf)
                : drawn.Count(d => d.Card.Free == true);
            // Escape Plan: the card it drew (none into a full hand or from empty piles: no block).
            if (da.Block != null && drawn.Count > 0 && drawn[0].Card.Type == da.BlockIf) return BlockOnce(x, da.Block);
            if (!da.DiscardUnlessFree) return new[] { x };
            var discard = drawn.Where(d => d.Card.Free == false).ToList();
            if (discard.Count == 0) return new[] { x };
            // A Sly card plays itself when discarded (CardModel.OnDiscard): not simulated.
            if (discard.Any(d => d.Card.Sly)) x.Unmodelled ??= "a Sly card discarded by Scrape plays itself";
            var p = MutablePlayer(x, player);
            foreach (var (card, id) in discard)
            {
                p.HandCount--;
                if (card.TurnEnd != null && p.TurnEndCards.IndexOf(card.TurnEnd) is var at and >= 0)
                    p.TurnEndCards = p.TurnEndCards.Where((_, k) => k != at).ToList();
                if (card.Fading) p.Fading--;
                if (card.HexFading) p.HexFading--;
                if (p.AutoHand != null) p.AutoHand = p.AutoHand.Where(h => h.Id != id).ToList();
            }
            return ProcCardEvent(x, CardEvent.Discarded, discard.Count, player);
        });
    }

    /// <summary>
    /// One draw (CardPileCmd.Draw): none into a full hand (the card stays in the pile); the discard pile shuffled in when the
    /// draw pile is empty; then the top card when it's known, else a branch per profile in the rest, weighted by its count
    /// (each card equally likely: the order is hidden). The card joins your hand (what acts at end of turn, Ethereal, what
    /// Stampede or Simpleton could pick), then its reactions run.
    /// </summary>
    private static IEnumerable<SimState> DrawOne(SimState s, int player, bool indirect, bool record = false)
    {
        var p = PlayerOf(s, player)!;
        // The card being played has left your hand already (the game moves it before OnPlay); HandCount drops at CardResolved.
        var inHand = p.HandCount - (s.PlayFromHand ? 1 : 0);
        if (inHand >= MaxHand) return new[] { s };
        var pile = p.Draw!;
        if (pile.Count == 0)
        {
            if (pile.DiscardCount == 0) return new[] { s };
            p = MutablePlayer(s, player);
            p.Draw = pile.Shuffled();
            return Then(ProcCardEvent(s, CardEvent.Shuffled, 1, player), x => DrawOne(x, player, indirect, record));
        }
        if (pile.Top.Count > 0)
        {
            var mp = MutablePlayer(s, player);
            var top = pile.Top[0];
            mp.Draw = pile.WithoutTop();
            return Drawn(s, player, top, indirect, record);
        }
        var total = pile.RestCount;
        var result = new List<SimState>();
        for (var i = 0; i < pile.Rest.Count; i++)
        {
            var g = pile.Rest[i];
            var c = pile.Rest.Count == 1 ? s : s.Clone();
            c.Weight = s.Weight * g.Count / total;
            MutablePlayer(c, player).Draw = pile.WithoutOneOf(i);
            result.AddRange(Drawn(c, player, g.Card, indirect, record));
        }
        return result;
    }

    private static SimPlayer MutablePlayer(SimState s, int player) => player == Who.You ? s.P : s.MutableTeammate(Who.TeammateIndex(player));

    /// <summary>The card is in your hand: what it changes there, then the draw reactions and the draws they chain.</summary>
    private static IEnumerable<SimState> Drawn(SimState s, int player, SimDrawCard card, bool indirect, bool record = false)
    {
        var p = MutablePlayer(s, player);
        p.HandCount++;
        p.Drawn++;
        // The draw a card acts on (RunDrawnCard): this card, and its id in AutoHand. Copied, not added to: branches share the list.
        if (record && s.DrawRecord != null) s.DrawRecord = new List<(SimDrawCard, int)>(s.DrawRecord) { (card, -1000 - p.Drawn) };
        if (card.TurnEnd != null) p.TurnEndCards = new List<TurnEndCard>(p.TurnEndCards) { card.TurnEnd };
        if (card.Fading) p.Fading++;
        if (card.HexFading) p.HexFading++;
        if (card.Status) p.StatusDrawn++;
        if (p.AutoHand != null && card.Pickable)
        {
            // Its own id in your hand (two copies are two cards); plays as the adapter built it for a card in hand.
            if (card.Auto != null)
                p.AutoHand = new List<SimHandCard>(p.AutoHand) { new() { Id = -1000 - p.Drawn, Name = card.Auto.Name, IsAttack = card.Auto.IsAttack, Cost = card.Auto.Cost, Plays = card.Auto.Plays } };
            else s.AutoPlaysUnknown = true;
        }
        // Hellraiser auto-plays a drawn Strike (AfterCardDrawnEarly): not simulated, so later auto-plays aren't either.
        if (card.Strike && p.Powers["HellraiserPower"] > 0)
        {
            s.AutoPlaysUnknown = true;
            s.Unmodelled ??= "Hellraiser auto-plays a drawn Strike";
        }

        IEnumerable<SimState> r = FireCardEvent(s, CardEvent.Drawn, player, indirect);
        // Iteration (IterationPower.AfterCardDrawn): the first Status you draw this turn draws its amount more.
        if (card.Status && p.Powers["IterationPower"] is > 0 and var iteration && p.StatusDrawn <= 1)
            r = Then(r, x => DrawCards(x, player, iteration, indirect: true));
        // Pagestorm (PagestormPower.AfterCardDrawn): each Ethereal card you draw draws its amount more.
        if (card.Ethereal && p.Powers["PagestormPower"] is > 0 and var pagestorm)
            r = Then(r, x => DrawCards(x, player, pagestorm, indirect: true));
        return r;
    }
}
