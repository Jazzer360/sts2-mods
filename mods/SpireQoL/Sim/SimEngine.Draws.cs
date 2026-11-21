namespace SpireQoL.Sim;

public static partial class SimEngine
{
    /// <summary>CardPile.MaxCardsInHand.</summary>
    public const int MaxHand = 10;

    /// <summary>A player's draws go through their draw pile (decision #90): only yours, when the adapter filled it.</summary>
    internal static bool DrawsFromPile(SimState state, int player) => state.Player(player)?.Draw != null;

    /// <summary>
    /// <paramref name="count"/> draws, one after another (CardPileCmd.Draw), each with its reactions before the next: the draw
    /// reactions (Speedster, Corrosive Wave, Automation...) and the draws they chain (Iteration, Pagestorm) come first, so with
    /// a hand nearly full the order is the game's.
    /// </summary>
    /// <param name="onDrawn">Called for each card these draws give (not the ones their reactions chain), before its reactions: a
    /// draw tab counting what its source drew (decision #132).</param>
    internal static IEnumerable<SimState> DrawCards(SimState state, int player, int count, bool indirect, Action<SimState>? onDrawn = null)
    {
        IEnumerable<SimState> states = new[] { state };
        for (var i = 0; i < count; i++) states = Then(states, branch => DrawOne(branch, player, indirect, onDrawn: onDrawn));
        return states;
    }

    /// <summary>
    /// A draw the card acts on (DrawnCardAction, decision #118): the draws, recording the cards they gave (not the ones their
    /// reactions chain: CardPileCmd.Draw returns its own), then the card's reaction to them, all in one step. Draw by draw, the
    /// branches merge (decision #120): two that drew the same cards in another order are one (the record is in the key while set),
    /// so Pillage's work grows with the draws, not with the orders they could come in.
    /// </summary>
    internal static IEnumerable<SimState> RunDrawnCard(SimState state, DrawnCardAction drawnCardAction)
    {
        var player = state.CurrentPlay?.Owner ?? Who.You;
        if (!DrawsFromPile(state, player))
        {
            // No pile read (a teammate's): the draws are counted, and what they gave isn't known.
            state.Unmodelled ??= "a card that acts on what it draws, with no draw pile to draw from";
            return ProcCardEvent(state, CardEvent.Drawn, drawnCardAction.Count, player, drawnCardAction.Indirect);
        }
        // A draw inside another's (a card it drew played itself: Hellraiser) records its own, then gives the outer one's back.
        var outerRecord = state.DrawRecord;
        state.DrawRecord = new List<(SimDrawCard, int)>();
        IEnumerable<SimState> states = new[] { state };
        if (drawnCardAction.DrawAgainIf != SimCardType.None)
        {
            // Pillage (its do-while): draw; again while what it drew is an Attack and your hand has room. A draw that gave
            // nothing (a full hand, empty piles) ends it. One level per draw, merged, the finished ones set aside.
            var done = new List<SimState>();
            IEnumerable<SimState> going = states;
            for (var drawn = 0; going.Any(); drawn++)
            {
                var next = new List<SimState>();
                foreach (var copy in Then(going, branch => DrawOne(branch, player, drawnCardAction.Indirect, record: true)))
                    (copy.DrawRecord!.Count > drawn && copy.DrawRecord[^1].Card.Type == drawnCardAction.DrawAgainIf
                        && copy.Player(player)!.HandCount - copy.Player(player)!.PlayingFromHand < MaxHand ? next : done).Add(copy);
                going = next;
            }
            states = done;
        }
        else
            for (var i = 0; i < drawnCardAction.Count; i++) states = Then(states, branch => DrawOne(branch, player, drawnCardAction.Indirect, record: true));
        return Then(states, branch =>
        {
            var drawn = branch.DrawRecord!;
            branch.DrawRecord = outerRecord;
            // What its chance tab counts (decision #119).
            branch.DrawnTally = drawnCardAction.Block != null ? (drawn.Count > 0 && drawn[0].Card.Type == drawnCardAction.BlockIf ? 1 : 0)
                : drawnCardAction.DrawAgainIf != SimCardType.None ? drawn.Count(draw => draw.Card.Type == drawnCardAction.DrawAgainIf)
                : drawn.Count(draw => draw.Card.Free == true);
            // Escape Plan: the card it drew (none into a full hand or from empty piles: no block).
            if (drawnCardAction.Block != null && drawn.Count > 0 && drawn[0].Card.Type == drawnCardAction.BlockIf) return BlockOnce(branch, drawnCardAction.Block);
            if (!drawnCardAction.DiscardUnlessFree) return new[] { branch };
            var discard = drawn.Where(draw => draw.Card.Free == false).ToList();
            if (discard.Count == 0) return new[] { branch };
            // A Sly card plays itself when discarded (CardModel.OnDiscard): not simulated.
            if (discard.Any(draw => draw.Card.Sly)) branch.Unmodelled ??= "a Sly card discarded by Scrape plays itself";
            var simPlayer = branch.MutablePlayer(player);
            foreach (var (card, id) in discard)
            {
                simPlayer.HandCount--;
                if (card.TurnEnd != null && simPlayer.TurnEndCards.IndexOf(card.TurnEnd) is var at and >= 0)
                    simPlayer.TurnEndCards = simPlayer.TurnEndCards.Where((_, index) => index != at).ToList();
                if (card.Fading) simPlayer.Fading--;
                if (card.HexFading) simPlayer.HexFading--;
                if (simPlayer.AutoHand != null) simPlayer.AutoHand = simPlayer.AutoHand.Where(handCard => handCard.Id != id).ToList();
            }
            return ProcCardEvent(branch, CardEvent.Discarded, discard.Count, player);
        });
    }

    /// <summary>
    /// One draw (CardPileCmd.Draw): none into a full hand (the card stays in the pile); the discard pile shuffled in when the
    /// draw pile is empty; then the top card when it's known, else a branch per profile in the rest, weighted by its count
    /// (each card equally likely: the order is hidden). The card joins your hand (what acts at end of turn, Ethereal, what
    /// Stampede or Simpleton could pick), then its reactions run.
    /// </summary>
    private static IEnumerable<SimState> DrawOne(SimState state, int player, bool indirect, bool record = false, Action<SimState>? onDrawn = null)
    {
        var simPlayer = state.Player(player)!;
        // The cards being played have left their owner's hand already (the game moves each before OnPlay); HandCount drops each at CardResolved.
        var inHand = simPlayer.HandCount - simPlayer.PlayingFromHand;
        if (inHand >= MaxHand) return new[] { state };
        var pile = simPlayer.Draw!;
        if (pile.Count == 0)
        {
            if (pile.DiscardCount == 0) return new[] { state };
            simPlayer = state.MutablePlayer(player);
            simPlayer.Draw = pile.Shuffled();
            return Then(ProcCardEvent(state, CardEvent.Shuffled, count: 1, player), branch => DrawOne(branch, player, indirect, record, onDrawn));
        }
        if (pile.Top.Count > 0)
        {
            var nextPlayer = state.MutablePlayer(player);
            var top = pile.Top[0];
            nextPlayer.Draw = pile.WithoutTop();
            onDrawn?.Invoke(state);
            return Drawn(state, player, top, indirect, record);
        }
        var total = pile.RestCount;
        var result = new List<SimState>();
        for (var i = 0; i < pile.Rest.Count; i++)
        {
            var group = pile.Rest[i];
            var branch = pile.Rest.Count == 1 ? state : state.Clone();
            branch.Weight = state.Weight * group.Count / total;
            branch.MutablePlayer(player).Draw = pile.WithoutOneOf(i);
            onDrawn?.Invoke(branch);
            result.AddRange(Drawn(branch, player, group.Card, indirect, record));
        }
        return result;
    }

    /// <summary>
    /// Hellraiser (HellraiserPower.AfterCardDrawnEarly, decision #121): the Strike just drawn plays itself (CardCmd.AutoPlay, no
    /// target given: a random hittable enemy, a branch each), in the middle of whatever drew it; that play's own state is put
    /// back after, so the play that drew it carries on as it was. It's an auto-play: it costs nothing, and the card it is
    /// (CardModel) is still what the draw gave (Pillage reads an Attack). One with no play built (aimed at a teammate, or the
    /// game's cap on endless auto-plays against enemies that can't die) isn't simulated: `?`.
    /// </summary>
    private static IEnumerable<SimState> HellraiserPlays(SimState state, int player, SimDrawCard card, int id)
    {
        if (card.Auto is not { Plays.Count: > 0 } auto)
        {
            state.AutoPlaysUnknown = true;
            state.Unmodelled ??= "Hellraiser auto-plays a drawn Strike whose play isn't built";
            return new[] { state };
        }
        // The copy drawn plays (from your hand: it's there once drawn), so its plays are its own.
        var ownPlays = auto.CopyWithId(id).Plays;
        var plays = ownPlays.Count == 1 && ownPlays[0].TargetId < 0
            ? ownPlays
            : ownPlays.Where(play => state.Enemy(play.TargetId) is { Alive: true, Hittable: true }).ToList();
        if (player == Who.You) state.AutoPlayed++;
        var simPlayer = state.MutablePlayer(player);
        if (simPlayer.AutoHand != null) simPlayer.AutoHand = simPlayer.AutoHand.Where(handCard => handCard.Id != id).ToList();
        if (plays.Count == 0)
        {
            // No enemy to hit: it goes to the discard pile unplayed (CardCmd.AutoPlay).
            simPlayer.HandCount--;
            return new[] { state };
        }
        var (current, events, blockGains, attack) = (state.CurrentPlay, state.PlayEvents, state.PlayCardBlockGains, state.CurrentAttack);
        var result = new List<SimState>();
        foreach (var play in plays)
        {
            var branch = plays.Count == 1 ? state : state.Clone();
            branch.Weight = state.Weight / plays.Count;
            var sub = new BranchSet(branch, nested: true);
            ApplyPlay(sub, play);
            foreach (var copy in sub.States)
            {
                (copy.CurrentPlay, copy.PlayEvents, copy.PlayCardBlockGains, copy.CurrentAttack) = (current, events, blockGains, attack);
                result.Add(copy);
            }
        }
        return result;
    }


    /// <summary>
    /// Cards a relic or power puts into a player's hand (CardPileCmd.AddGeneratedCardToCombat to PileType.Hand), or a played card a
    /// power sends back there (Feral): each takes a slot while the hand has room, and in a full hand goes to the discard pile instead
    /// (CardPileCmd.Add). The cards being played still hold their slots until they resolve. An Ethereal one exhausts at end of turn
    /// (Fading); what they are isn't known to a pick from the hand (Stampede).
    /// </summary>
    internal static void AddToHand(SimState state, int player, int count, bool ethereal = false)
    {
        if (count <= 0 || state.Player(player) == null) return;
        var simPlayer = state.MutablePlayer(player);
        var added = Math.Min(count, Math.Max(0, MaxHand - (simPlayer.HandCount - simPlayer.PlayingFromHand)));
        simPlayer.HandCount += added;
        if (ethereal) simPlayer.Fading += added;
        if (added > 0 && simPlayer.AutoHand != null) state.AutoPlaysUnknown = true;
    }

    /// <summary>The card is in your hand: what it changes there, then the draw reactions and the draws they chain.</summary>
    private static IEnumerable<SimState> Drawn(SimState state, int player, SimDrawCard card, bool indirect, bool record = false)
    {
        var simPlayer = state.MutablePlayer(player);
        simPlayer.HandCount++;
        simPlayer.Drawn++;
        // The draw a card acts on (RunDrawnCard): this card, and its id in AutoHand. Copied, not added to: branches share the list.
        if (record && state.DrawRecord != null) state.DrawRecord = new List<(SimDrawCard, int)>(state.DrawRecord) { (card, -1000 - simPlayer.Drawn) };
        if (card.TurnEnd != null) simPlayer.TurnEndCards = new List<TurnEndCard>(simPlayer.TurnEndCards) { card.TurnEnd };
        // Void: you lose its energy as it's drawn (PlayerCmd.LoseEnergy: no hooks, never below 0).
        if (card.EnergyLostOnDraw > 0) simPlayer.Energy = Math.Max(0, simPlayer.Energy - card.EnergyLostOnDraw);
        if (card.Exhausting != null) simPlayer.ExhaustingInHand = new List<ExhaustingCard>(simPlayer.ExhaustingInHand) { card.Exhausting with { HandCard = -1000 - simPlayer.Drawn } };
        if (card.Fading) simPlayer.Fading++;
        if (card.HexFading) simPlayer.HexFading++;
        if (card.Status) simPlayer.StatusDrawn++;
        if (simPlayer.AutoHand != null && card.Pickable)
        {
            // Its own id in your hand (two copies are two cards, with one key: SimHandCard.Key); plays as the adapter built it for a card in hand.
            if (card.Auto != null)
                simPlayer.AutoHand = new List<SimHandCard>(simPlayer.AutoHand) { card.Auto.CopyWithId(-1000 - simPlayer.Drawn) };
            else state.AutoPlaysUnknown = true;
        }
        // Hellraiser auto-plays a drawn Strike (AfterCardDrawnEarly: before the other draw reactions).
        IEnumerable<SimState> states = card.Strike && simPlayer.Powers[PowerId.Hellraiser] > 0
            ? Then(HellraiserPlays(state, player, card, -1000 - simPlayer.Drawn), branch => FireCardEvent(branch, CardEvent.Drawn, player, indirect))
            : FireCardEvent(state, CardEvent.Drawn, player, indirect);
        // Iteration (IterationPower.AfterCardDrawn): the first Status you draw this turn draws its amount more.
        if (card.Status && simPlayer.Powers[PowerId.Iteration] is > 0 and var iteration && simPlayer.StatusDrawn <= 1)
            states = Then(states, branch => DrawCards(branch, player, iteration, indirect: true));
        // Pagestorm (PagestormPower.AfterCardDrawn): each Ethereal card you draw draws its amount more.
        if (card.Ethereal && simPlayer.Powers[PowerId.Pagestorm] is > 0 and var pagestorm)
            states = Then(states, branch => DrawCards(branch, player, pagestorm, indirect: true));
        return states;
    }
}
