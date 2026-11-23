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
    /// The game's ShouldDraw (CardPileCmd.DrawInternal checks it first: no draw, no shuffle): a draw inside a play does nothing while
    /// its player has No Draw (NoDrawPower.ShouldDraw), or Fiddle (Fiddle.ShouldDraw: on its owner's own turn, which a play is). A dev
    /// review: Dark Embrace's draw on Forgotten Ritual's exhaust was counted under No Draw.
    /// </summary>
    internal static bool DrawsStopped(SimState state, int player) =>
        state.CurrentPlay != null && (PowersOf(state, player).Has(PowerId.NoDraw) || RelicsOf(state, player).Has(RelicId.Fiddle));

    /// <summary>
    /// One draw (CardPileCmd.Draw): none into a full hand (the card stays in the pile); the discard pile shuffled in when the
    /// draw pile is empty; then the top card when it's known, else a branch per profile in the rest, weighted by its count
    /// (each card equally likely: the order is hidden). The card joins your hand (what acts at end of turn, Ethereal, what
    /// Stampede or Simpleton could pick), then its reactions run.
    /// </summary>
    private static IEnumerable<SimState> DrawOne(SimState state, int player, bool indirect, bool record = false, Action<SimState>? onDrawn = null)
    {
        if (DrawsStopped(state, player)) return new[] { state };
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

    /// <summary>
    /// A card from a pile joins its player's hand (drawn, or taken: TakeToHandAction), with <paramref name="id"/> its id there: what
    /// it changes in the hand (acts at the end of the turn, acts when exhausted, Ethereal, what Stampede or Simpleton could pick). Its
    /// count in the hand is the caller's.
    /// </summary>
    private static void JoinHand(SimState state, SimPlayer simPlayer, SimDrawCard card, int id)
    {
        if (card.TurnEnd != null) simPlayer.TurnEndCards = new List<TurnEndCard>(simPlayer.TurnEndCards) { card.TurnEnd };
        if (card.Exhausting != null) simPlayer.ExhaustingInHand = new List<ExhaustingCard>(simPlayer.ExhaustingInHand) { card.Exhausting with { HandCard = id } };
        if (card.Fading) simPlayer.Fading++;
        if (card.HexFading) simPlayer.HexFading++;
        if (simPlayer.AutoHand != null && card.Pickable)
        {
            // Its own id in your hand (two copies are two cards, with one key: SimHandCard.Key); plays as the adapter built it for a card in hand.
            if (card.Auto != null)
                simPlayer.AutoHand = new List<SimHandCard>(simPlayer.AutoHand) { card.Auto.CopyWithId(id) };
            else state.AutoPlaysUnknown = true;
        }
    }

    /// <summary>The id in the hand of the <paramref name="taken"/>th card taken from a pile in this simulation.</summary>
    private static int TakenId(int taken) => -1_000_000 - taken;

    /// <summary>
    /// A play taking cards from its owner's draw or discard pile into their hand (TakeToHandAction), one after another: each leaves
    /// the pile and joins the hand (no draw: nothing that reacts to draws hears it). With no pile read (a teammate's), only the count
    /// is kept.
    /// </summary>
    internal static IEnumerable<SimState> RunTakeToHand(SimState state, TakeToHandAction take)
    {
        var player = state.CurrentPlay?.Owner ?? Who.You;
        if (!DrawsFromPile(state, player) || take.Count <= 0) return new[] { state };
        return TakeToHand(state, player, take, take.Eligible, take.Count);
    }

    /// <summary>The profile with this key in the pile a take comes from.</summary>
    private static SimDrawCard? ProfileIn(SimDrawPile pile, bool fromDiscard, string key) => fromDiscard
        ? pile.Discard.FirstOrDefault(group => group.Card.Key == key)?.Card
        : pile.Top.FirstOrDefault(card => card.Key == key) ?? pile.Rest.FirstOrDefault(group => group.Card.Key == key)?.Card;

    /// <summary>How many cards of this profile the pile a take comes from holds now.</summary>
    private static int InPile(SimDrawPile pile, bool fromDiscard, string key) => fromDiscard
        ? pile.Discard.Where(group => group.Card.Key == key).Sum(group => group.Count)
        : pile.Top.Count(card => card.Key == key) + pile.Rest.Where(group => group.Card.Key == key).Sum(group => group.Count);

    private static IEnumerable<SimState> TakeToHand(SimState state, int player, TakeToHandAction take, IReadOnlyDictionary<string, int> eligible, int count)
    {
        var pile = state.Player(player)!.Draw!;
        // The cards it can take, by profile: as many as it could take of each, and as many as the pile still holds.
        var candidates = eligible.Where(entry => entry.Value > 0)
            .Select(entry => (Key: entry.Key, Available: Math.Min(entry.Value, InPile(pile, take.FromDiscard, entry.Key))))
            .Where(candidate => candidate.Available > 0).OrderBy(candidate => candidate.Key, StringComparer.Ordinal).ToList();
        var total = candidates.Sum(candidate => candidate.Available);
        if (count <= 0 || total == 0) return new[] { state };
        if (count >= total)
        {
            // Every one it can take: no pick to make.
            foreach (var (key, available) in candidates)
                for (var i = 0; i < available; i++) TakeOne(state, player, key, take.FromDiscard);
            return new[] { state };
        }
        if (!take.Random)
        {
            // Your pick is your later decision (decision #167): a plain card when there is one, the same whichever you take when they're
            // all one profile, else unknown.
            var plain = candidates.FindIndex(candidate => ProfileIn(pile, take.FromDiscard, candidate.Key) is { Plain: true });
            if (plain < 0 && candidates.Count > 1) state.Unmodelled ??= "you choose which card it takes into your hand, and they differ in what the simulation follows";
            var chosen = candidates[Math.Max(0, plain)].Key;
            TakeOne(state, player, chosen, take.FromDiscard);
            return TakeToHand(state, player, take, Less(eligible, chosen), count - 1);
        }
        var result = new List<SimState>();
        for (var i = 0; i < candidates.Count; i++)
        {
            var branch = candidates.Count == 1 ? state : state.Clone();
            branch.Weight = state.Weight * candidates[i].Available / total;
            TakeOne(branch, player, candidates[i].Key, take.FromDiscard);
            result.AddRange(TakeToHand(branch, player, take, Less(eligible, candidates[i].Key), count - 1));
        }
        return result;
    }

    private static IReadOnlyDictionary<string, int> Less(IReadOnlyDictionary<string, int> eligible, string key) =>
        eligible.ToDictionary(entry => entry.Key, entry => entry.Key == key ? entry.Value - 1 : entry.Value);

    /// <summary>
    /// One card of this profile out of its pile into the hand: from the discard pile, or the draw pile's unordered rest when it's there,
    /// else its known top.
    /// </summary>
    private static void TakeOne(SimState state, int player, string key, bool fromDiscard)
    {
        var simPlayer = state.MutablePlayer(player);
        var pile = simPlayer.Draw!;
        SimDrawCard card;
        if (fromDiscard)
        {
            var discardIndex = pile.Discard.FindIndex(group => group.Card.Key == key);
            card = pile.Discard[discardIndex].Card;
            simPlayer.Draw = pile.WithoutOneDiscarded(discardIndex);
        }
        else if (pile.Rest.FindIndex(group => group.Card.Key == key) is var restIndex and >= 0)
        {
            card = pile.Rest[restIndex].Card;
            simPlayer.Draw = pile.WithoutOneOf(restIndex);
        }
        else
        {
            var topIndex = pile.Top.FindIndex(drawCard => drawCard.Key == key);
            card = pile.Top[topIndex];
            simPlayer.Draw = pile.WithoutTopAt(topIndex);
        }
        simPlayer.TakenToHand++;
        JoinHand(state, simPlayer, card, TakenId(simPlayer.TakenToHand));
    }

    /// <summary>
    /// Cards auto-played from the top of a player's draw pile (CardPileCmd.AutoPlayFromDrawPile, CardPilePosition.Top), one after
    /// another: the discard pile shuffled in when the draw pile is empty (ShuffleIfNecessary); the known top card, else one card of the
    /// rest, a branch per profile by its count (the order is hidden). Each plays as built for the pile (SimDrawCard.FromPile, or
    /// FromPileUpgraded when <paramref name="upgrade"/>): free, at a random hittable enemy for a card aimed at one (CardCmd.AutoPlay),
    /// a branch each, in the middle of whatever played it (its own state put back after, as Hellraiser's). Then it goes to the
    /// discard pile, or to the exhaust pile when <paramref name="forceExhaust"/> (Havoc: ExhaustOnNextPlay) or it exhausts itself.
    /// </summary>
    internal static IEnumerable<SimState> AutoPlayFromDraw(SimState state, int player, int count, bool forceExhaust, bool upgrade = false)
    {
        IEnumerable<SimState> states = new[] { state };
        for (var i = 0; i < count; i++) states = Then(states, branch => AutoPlayTop(branch, player, forceExhaust, upgrade));
        return states;
    }

    private static IEnumerable<SimState> AutoPlayTop(SimState state, int player, bool forceExhaust, bool upgrade)
    {
        if (state.Player(player) is not { } simPlayer || !state.TakesPart(player)) return new[] { state };
        if (!DrawsFromPile(state, player))
        {
            state.AutoPlaysUnknown = true;
            state.Unmodelled ??= "a card auto-played from a draw pile the simulator doesn't read";
            return new[] { state };
        }
        var pile = simPlayer.Draw!;
        if (pile.Count == 0)
        {
            if (pile.DiscardCount == 0) return new[] { state };
            state.MutablePlayer(player).Draw = pile.Shuffled();
            return Then(ProcCardEvent(state, CardEvent.Shuffled, count: 1, player), branch => AutoPlayTop(branch, player, forceExhaust, upgrade));
        }
        if (pile.Top.Count > 0)
        {
            state.MutablePlayer(player).Draw = pile.WithoutTop();
            return PlayFromPile(state, player, pile.Top[0], forceExhaust, upgrade);
        }
        var total = pile.RestCount;
        var result = new List<SimState>();
        for (var i = 0; i < pile.Rest.Count; i++)
        {
            var branch = pile.Rest.Count == 1 ? state : state.Clone();
            branch.Weight = state.Weight * pile.Rest[i].Count / total;
            branch.MutablePlayer(player).Draw = pile.WithoutOneOf(i);
            result.AddRange(PlayFromPile(branch, player, pile.Rest[i].Card, forceExhaust, upgrade));
        }
        return result;
    }

    /// <summary>A card off the top of the draw pile plays itself (AutoPlayFromDraw), then goes where it goes.</summary>
    private static IEnumerable<SimState> PlayFromPile(SimState state, int player, SimDrawCard card, bool forceExhaust, bool upgrade)
    {
        if ((upgrade ? card.FromPileUpgraded : card.FromPile) is not { } built)
        {
            state.AutoPlaysUnknown = true;
            state.Unmodelled ??= "a card auto-played from your draw pile whose play isn't built";
            return new[] { state };
        }
        if (player == Who.You) state.AutoPlayed++;
        var plays = built.Plays.Count == 1 && built.Plays[0].TargetId < 0
            ? built.Plays
            : built.Plays.Where(play => state.Enemy(play.TargetId) is { Alive: true, Hittable: true }).ToList();
        if (plays.Count == 0)
        {
            // Unplayable, or no enemy to hit: to the discard pile unplayed (CardCmd.MoveToResultPileWithoutPlaying).
            Discarded(state, player, card);
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
                if (card.ExhaustsWhenPlayed) result.Add(copy);
                else if (forceExhaust) result.AddRange(ProcCardEvent(copy, CardEvent.Exhausted, count: 1, player, indirect: true));
                else
                {
                    Discarded(copy, player, card);
                    result.Add(copy);
                }
            }
        }
        return result;
    }

    private static void Discarded(SimState state, int player, SimDrawCard card)
    {
        var simPlayer = state.MutablePlayer(player);
        simPlayer.Draw = simPlayer.Draw!.WithDiscarded(card);
    }

    /// <summary>
    /// A card made into a player's draw pile at a random spot (CardPileCmd.AddGeneratedCardToCombat, CardPilePosition.Random: Personal
    /// Hive's Dazed, Biiig Hug's Soot), so a later draw in the same simulation can draw it: with cards known on top, a branch for each
    /// spot among them (1 in the pile's count + 1 each) and one for anywhere below them, into the unordered rest. Its profile is the
    /// board's (SimPlayer.MadeCards); with none read (no pile, or not built), what it does later isn't known.
    /// </summary>
    internal static IEnumerable<SimState> IntoDrawPile(SimState state, int player, string card)
    {
        if (!DrawsFromPile(state, player)) return new[] { state };
        var simPlayer = state.Player(player)!;
        if (simPlayer.MadeCards?.GetValueOrDefault(card) is not { } profile)
        {
            state.Unmodelled ??= $"a {card} made into your draw pile, whose draw isn't built";
            return new[] { state };
        }
        var pile = simPlayer.Draw!;
        var slots = pile.Count + 1;
        var result = new List<SimState>();
        for (var i = 0; i <= pile.Top.Count; i++)
        {
            var branch = i == pile.Top.Count ? state : state.Clone();
            branch.Weight = state.Weight * (i < pile.Top.Count ? 1 : pile.RestCount + 1) / slots;
            branch.MutablePlayer(player).Draw = i < pile.Top.Count ? pile.WithTopAt(i, profile) : pile.WithInRest(profile);
            result.Add(branch);
        }
        return result;
    }

    /// <summary>The card is in your hand: what it changes there, then the draw reactions and the draws they chain.</summary>
    private static IEnumerable<SimState> Drawn(SimState state, int player, SimDrawCard card, bool indirect, bool record = false)
    {
        var simPlayer = state.MutablePlayer(player);
        simPlayer.HandCount++;
        simPlayer.Drawn++;
        // The draw a card acts on (RunDrawnCard): this card, and its id in AutoHand. Copied, not added to: branches share the list.
        if (record && state.DrawRecord != null) state.DrawRecord = new List<(SimDrawCard, int)>(state.DrawRecord) { (card, -1000 - simPlayer.Drawn) };
        // Void: you lose its energy as it's drawn (PlayerCmd.LoseEnergy: no hooks, never below 0).
        if (card.EnergyLostOnDraw > 0) simPlayer.Energy = Math.Max(0, simPlayer.Energy - card.EnergyLostOnDraw);
        if (card.Status) simPlayer.StatusDrawn++;
        JoinHand(state, simPlayer, card, -1000 - simPlayer.Drawn);
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
