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
    /// One draw (CardPileCmd.Draw): none into a full hand (the card stays in the pile); the discard pile shuffled in when the
    /// draw pile is empty; then the top card when it's known, else a branch per profile in the rest, weighted by its count
    /// (each card equally likely: the order is hidden). The card joins your hand (what acts at end of turn, Ethereal, what
    /// Stampede or Simpleton could pick), then its reactions run.
    /// </summary>
    private static IEnumerable<SimState> DrawOne(SimState s, int player, bool indirect)
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
            return Then(ProcCardEvent(s, CardEvent.Shuffled, 1, player), x => DrawOne(x, player, indirect));
        }
        if (pile.Top.Count > 0)
        {
            var mp = MutablePlayer(s, player);
            var top = pile.Top[0];
            mp.Draw = pile.WithoutTop();
            return Drawn(s, player, top, indirect);
        }
        var total = pile.RestCount;
        var result = new List<SimState>();
        for (var i = 0; i < pile.Rest.Count; i++)
        {
            var g = pile.Rest[i];
            var c = pile.Rest.Count == 1 ? s : s.Clone();
            c.Weight = s.Weight * g.Count / total;
            MutablePlayer(c, player).Draw = pile.WithoutOneOf(i);
            result.AddRange(Drawn(c, player, g.Card, indirect));
        }
        return result;
    }

    private static SimPlayer MutablePlayer(SimState s, int player) => player == Who.You ? s.P : s.MutableTeammate(Who.TeammateIndex(player));

    /// <summary>The card is in your hand: what it changes there, then the draw reactions and the draws they chain.</summary>
    private static IEnumerable<SimState> Drawn(SimState s, int player, SimDrawCard card, bool indirect)
    {
        var p = MutablePlayer(s, player);
        p.HandCount++;
        p.Drawn++;
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
        if (card.Strike && p.Powers["HellraiserPower"] > 0) s.AutoPlaysUnknown = true;

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
