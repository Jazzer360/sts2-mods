namespace SpireQoL.Sim;

/// <summary>Cards in a player's piles a card plays (CardCmd.AutoPlay, decision #360; decompile v0.114.0).</summary>
public static partial class SimEngine
{
    /// <summary>
    /// AutoPlayCardsAction: the cards of the play owner's pile its code takes, one after another, each played free with its own play (CardCmd.AutoPlay;
    /// SimDrawCard.FromPile for a pile's card, SimHandCard.Plays for a hand card), in the middle of the card that plays them (its own state put back
    /// after). One random card a round (a branch per card there by its count, never the pile's order); or the list its code makes as it begins, each
    /// card in the pile's order, or shuffled and its first so many (a branch per card left by its count, each round). It stops once the fight is over
    /// (CardCmd.AutoPlay does nothing then). What can't be followed is unknown where it runs, never left out: cards the board didn't read for this
    /// auto-play, a card its filter can't be asked of, a hand or pile that changed in a way the simulator doesn't follow card by card.
    /// </summary>
    private static void RunAutoPlayCards(BranchSet branches, AutoPlayCardsAction action)
    {
        if (action.Way == AutoPlayWay.Random)
        {
            for (var i = 0; i < action.Count; i++) branches.Each(branch => AutoPlayRandom(branch, action));
            return;
        }
        branches.Do(branch => ListCards(branch, action));
        var most = branches.States.Count == 0 ? 0 : branches.States.Max(branch => branch.Player(Owner(branch))?.PileAutoPlays?.Listed?.Left ?? 0);
        for (var i = 0; i < most; i++) branches.Each(branch => AutoPlayListed(branch, action));
        branches.Do(branch =>
        {
            if (branch.Player(Owner(branch)) is { PileAutoPlays.Listed: not null }) Listed(branch, Owner(branch), listed: null);
        });
    }

    private static int Owner(SimState state) => state.CurrentPlay?.Owner ?? Who.You;

    /// <summary>Whether a card's keys list the auto-play (true), can't tell (null: its filter can't be asked of it), or don't (false).</summary>
    private static bool? Lists(IReadOnlyList<string>? keys, string key) => keys == null ? false : keys.Contains(key) ? true : keys.Contains(key + "?") ? null : false;

    /// <summary>The board read the owner's cards for this auto-play (SimPlayer.AutoPlayKeys); else the branch is unknown.</summary>
    private static bool ReadFor(SimState state, SimPlayer player, AutoPlayCardsAction action)
    {
        if (player.PileAutoPlays?.Keys.Contains(action.Key) == true) return true;
        state.Unmodelled ??= $"{action.Key}: the cards it auto-plays weren't read on this board";
        return false;
    }

    /// <summary>One card at random among the ones there now its filter lets through (or its fallback's, with none), played.</summary>
    private static IEnumerable<SimState> AutoPlayRandom(SimState state, AutoPlayCardsAction action)
    {
        var owner = Owner(state);
        if (state.Player(owner) is not { } simPlayer || !state.TakesPart(owner) || FightOver(state) || !ReadFor(state, simPlayer, action)) return new[] { state };
        switch (action.Pile)
        {
            case SimPile.Hand:
            {
                if (simPlayer.AutoHand == null || state.AutoPlaysUnknown)
                {
                    state.Unmodelled ??= $"{action.Key}: an auto-play from a hand that changed in a way not simulated";
                    return new[] { state };
                }
                var playing = state.CurrentPlay?.Card.HandId ?? 0;
                var candidates = simPlayer.AutoHand.Where(card => card.Id != playing && Lists(card.AutoPlayedBy, action.Key) != false).ToList();
                if (candidates.Any(card => Lists(card.AutoPlayedBy, action.Key) == null))
                {
                    state.Unmodelled ??= $"{action.Key}: a card in your hand its filter can't be asked of";
                    return new[] { state };
                }
                return PlayFromHand(state, owner, candidates, action);
            }
            case SimPile.Draw:
            {
                if (simPlayer.Draw is not { } pile || pile.Picked > 0)
                {
                    state.Unmodelled ??= $"{action.Key}: an auto-play from a draw pile the simulator doesn't know card by card";
                    return new[] { state };
                }
                var picks = Candidates(pile, action.Key);
                if (picks == null || picks.Count == 0 && action.Fallback != null && (picks = Candidates(pile, action.Fallback)) == null)
                {
                    state.Unmodelled ??= $"{action.Key}: a card in your draw pile its filter can't be asked of";
                    return new[] { state };
                }
                if (picks.Count == 0) return new[] { state };
                var total = picks.Sum(pick => pick.Count);
                var result = new List<SimState>();
                foreach (var (card, count, top, group) in picks)
                {
                    var branch = picks.Count == 1 ? state : state.Clone();
                    branch.Weight = state.Weight * count / total;
                    branch.MutablePlayer(owner).Draw = top >= 0 ? pile.WithoutTopAt(top) : pile.WithoutOneOf(group);
                    result.AddRange(PlayFromPile(branch, owner, card, forceExhaust: false, action.Upgraded, action.AtTarget));
                }
                return result;
            }
            default:
                state.Unmodelled ??= $"{action.Key}: a random auto-play from your {action.Pile} pile isn't followed";
                return new[] { state };
        }
    }

    /// <summary>The draw pile's cards an auto-play's filter lets through: each known top card, then each profile of the rest; null when one can't be asked.</summary>
    private static List<(SimDrawCard Card, int Count, int Top, int Group)>? Candidates(SimDrawPile pile, string key)
    {
        var candidates = new List<(SimDrawCard, int, int, int)>();
        for (var i = 0; i < pile.Top.Count; i++)
            switch (Lists(pile.Top[i].AutoPlayedBy, key))
            {
                case null: return null;
                case true: candidates.Add((pile.Top[i], 1, i, -1)); break;
            }
        for (var i = 0; i < pile.Rest.Count; i++)
            switch (Lists(pile.Rest[i].Card.AutoPlayedBy, key))
            {
                case null: return null;
                case true: candidates.Add((pile.Rest[i].Card, pile.Rest[i].Count, -1, i)); break;
            }
        return candidates;
    }

    /// <summary>
    /// One of <paramref name="candidates"/> from the owner's hand played (CombatCardSelection: each equally likely), at a random hittable enemy for a card
    /// aimed at one (its plays, one per such enemy), in the middle of the play that auto-plays it (its own state put back after).
    /// </summary>
    private static IEnumerable<SimState> PlayFromHand(SimState state, int owner, List<SimHandCard> candidates, AutoPlayCardsAction action)
    {
        if (candidates.Count == 0) return new[] { state };
        var yours = owner == Who.You;
        var frame = state.SaveFrame();
        var result = new List<SimState>();
        foreach (var card in candidates)
        {
            var branch = candidates.Count == 1 ? state : state.Clone();
            branch.Weight = state.Weight / candidates.Count;
            var simPlayer = branch.MutablePlayer(owner);
            simPlayer.AutoHand = simPlayer.AutoHand!.Where(handCard => handCard.Id != card.Id).ToList();
            if (yours) branch.AutoPlayed++;
            if (card.Plays.Count == 0)
            {
                branch.AutoPlaysUnknown = true;
                branch.Unmodelled ??= $"{action.Key}: {card.Name} auto-played from your hand, a play that isn't simulated";
                result.Add(branch);
                continue;
            }
            var target = branch.CurrentPlay?.Target ?? Who.None;
            var plays = card.Plays.Count == 1 && card.Plays[0].TargetId < 0 ? card.Plays
                : action.AtTarget ? card.Plays.Where(play => play.TargetId == target).ToList()
                : card.Plays.Where(play => branch.Enemy(play.TargetId) is { Alive: true, Hittable: true }).ToList();
            if (plays.Count == 0)
            {
                // No enemy to hit: it goes to the discard pile unplayed (CardCmd.AutoPlay).
                simPlayer.HandCount = Math.Max(0, simPlayer.HandCount - 1);
                result.Add(branch);
                continue;
            }
            foreach (var play in plays)
            {
                var copy = plays.Count == 1 ? branch : branch.Clone();
                copy.Weight = branch.Weight / plays.Count;
                var sub = new BranchSet(copy, nested: true);
                ApplyPlay(sub, play);
                foreach (var after in sub.States)
                {
                    after.RestoreFrame(frame);
                    result.Add(after);
                }
            }
        }
        return result;
    }

    /// <summary>The list an auto-play of a list makes as it begins (SimPileAutoPlays.Listed): its filter's cards there now.</summary>
    private static void ListCards(SimState state, AutoPlayCardsAction action)
    {
        var owner = Owner(state);
        if (state.Player(owner) is not { } simPlayer || !state.TakesPart(owner) || FightOver(state) || !ReadFor(state, simPlayer, action)) return;
        if (simPlayer.PileAutoPlays!.Listed != null)
        {
            state.Unmodelled ??= $"{action.Key}: an auto-play of a list inside another's";
            return;
        }
        switch (action.Pile, action.Way)
        {
            case (SimPile.Hand, AutoPlayWay.Each) when simPlayer.AutoHand != null && !state.AutoPlaysUnknown:
            {
                var playing = state.CurrentPlay?.Card.HandId ?? 0;
                var listed = simPlayer.AutoHand.Where(card => card.Id != playing && Lists(card.AutoPlayedBy, action.Key) != false).ToList();
                if (listed.Any(card => Lists(card.AutoPlayedBy, action.Key) == null))
                {
                    state.Unmodelled ??= $"{action.Key}: a card in your hand its filter can't be asked of";
                    return;
                }
                Listed(state, owner, new SimListedCards(listed.Select(card => card.Id).ToList(), Array.Empty<SimDrawGroup>(), listed.Count));
                return;
            }
            case (SimPile.Discard, AutoPlayWay.Shuffled) when simPlayer.Draw is { } pile:
            {
                var listed = new List<SimDrawGroup>();
                foreach (var group in pile.Discard)
                    switch (Lists(group.Card.AutoPlayedBy, action.Key))
                    {
                        case null:
                            state.Unmodelled ??= $"{action.Key}: a card in your discard pile its filter can't be asked of";
                            return;
                        case true: listed.Add(group); break;
                    }
                var count = listed.Sum(group => group.Count);
                Listed(state, owner, new SimListedCards(Array.Empty<int>(), listed, Math.Min(action.Count, count)));
                return;
            }
            case (SimPile.Exhaust, AutoPlayWay.Each) when simPlayer.PileAutoPlays.ExhaustCards is { } exhausted:
            {
                if (exhausted.Count != simPlayer.ExhaustPile)
                {
                    state.Unmodelled ??= $"{action.Key}: cards exhausted in this simulation aren't followed one by one";
                    return;
                }
                var listed = new List<SimDrawGroup>();
                foreach (var card in exhausted)
                    switch (Lists(card.AutoPlayedBy, action.Key))
                    {
                        case null:
                            state.Unmodelled ??= $"{action.Key}: a card in your exhaust pile its filter can't be asked of";
                            return;
                        case true: listed.Add(new SimDrawGroup(card, Count: 1)); break;
                    }
                Listed(state, owner, new SimListedCards(Array.Empty<int>(), listed, listed.Count));
                return;
            }
        }
        state.Unmodelled ??= $"{action.Key}: an auto-play of cards in your {action.Pile} pile the simulator doesn't follow card by card here";
    }

    /// <summary>The next card of the list played: the hand's next card, the exhaust pile's next, or one of the shuffled ones left (a branch each by its count).</summary>
    private static IEnumerable<SimState> AutoPlayListed(SimState state, AutoPlayCardsAction action)
    {
        var owner = Owner(state);
        if (state.Player(owner) is not { PileAutoPlays.Listed: { Left: > 0 } listed } simPlayer) return new[] { state };
        if (!state.TakesPart(owner) || FightOver(state))
        {
            Listed(state, owner, listed with { Left = 0 });
            return new[] { state };
        }
        if (listed.HandCards.Count > 0)
        {
            var id = listed.HandCards[0];
            Listed(state, owner, listed with { HandCards = listed.HandCards.Skip(1).ToList(), Left = listed.Left - 1 });
            if (state.AutoPlaysUnknown || simPlayer.AutoHand?.FirstOrDefault(card => card.Id == id) is not { } card)
            {
                state.Unmodelled ??= $"{action.Key}: a card it listed in your hand left it, or the hand changed in a way not simulated, before its turn";
                return new[] { state };
            }
            return PlayFromHand(state, owner, new List<SimHandCard> { card }, action);
        }
        if (action.Pile == SimPile.Exhaust) return PlayFromExhaust(state, owner, listed, action);
        // A shuffled list: any card left, by its count.
        var total = listed.PileCards.Sum(group => group.Count);
        var result = new List<SimState>();
        for (var i = 0; i < listed.PileCards.Count; i++)
        {
            var group = listed.PileCards[i];
            var branch = listed.PileCards.Count == 1 ? state : state.Clone();
            branch.Weight = state.Weight * group.Count / total;
            var index = i;
            Listed(branch, owner, listed with
            {
                PileCards = listed.PileCards.Select((other, at) => at == index ? other with { Count = other.Count - 1 } : other).Where(other => other.Count > 0).ToList(),
                Left = listed.Left - 1,
            });
            var player = branch.MutablePlayer(owner);
            var at = player.Draw!.Discard.FindIndex(discarded => discarded.Card.Key == group.Card.Key);
            if (at < 0)
            {
                branch.Unmodelled ??= $"{action.Key}: a card it listed in your discard pile left it before its turn";
                result.Add(branch);
                continue;
            }
            player.Draw = player.Draw.WithoutOneDiscarded(at);
            result.AddRange(PlayFromPile(branch, owner, group.Card, forceExhaust: false, action.Upgraded, action.AtTarget));
        }
        return result;
    }

    /// <summary>
    /// The exhaust pile's next listed card played: it leaves the pile (SimPlayer.ExhaustCards, ExhaustPile), plays, and goes back there when its play
    /// exhausts it (its own exhaust counts it in again: upgraded, when the auto-play upgraded it), else to the discard pile.
    /// </summary>
    private static IEnumerable<SimState> PlayFromExhaust(SimState state, int owner, SimListedCards listed, AutoPlayCardsAction action)
    {
        var card = listed.PileCards[0].Card;
        Listed(state, owner, listed with { PileCards = listed.PileCards.Skip(1).ToList(), Left = listed.Left - 1 });
        var player = state.MutablePlayer(owner);
        var piles = player.PileAutoPlays!;
        var exhausted = piles.ExhaustCards?.ToList();
        var at = exhausted?.FindIndex(other => other.Key == card.Key) ?? -1;
        if (at < 0 || exhausted!.Count != player.ExhaustPile)
        {
            state.Unmodelled ??= $"{action.Key}: a card it listed in your exhaust pile left it, or cards were exhausted that aren't followed one by one, before its turn";
            return new[] { state };
        }
        if (card.Upgradable && piles.ExhaustUpgraded) state.Unmodelled ??= $"{action.Key}: a card that could have been upgraded in your exhaust pile played from there";
        exhausted.RemoveAt(at);
        player.PileAutoPlays = piles with { ExhaustCards = exhausted };
        player.ExhaustPile--;
        // Upgraded first (Knife Trap+): what goes back is the upgraded card.
        var back = action.Upgraded && card.FromPileUpgraded != null ? card with { Key = card.Key + "|upgraded", FromPile = card.FromPileUpgraded } : card;
        return PlayFromPile(state, owner, card, forceExhaust: false, action.Upgraded, action.AtTarget,
            exhaustedBack: branch =>
            {
                var exhausting = branch.MutablePlayer(owner);
                if (exhausting.PileAutoPlays is { ExhaustCards: { } cards } now) exhausting.PileAutoPlays = now with { ExhaustCards = new List<SimDrawCard>(cards) { back } };
            });
    }

    /// <summary>The owner's auto-play under way listing <paramref name="listed"/> (SimPileAutoPlays.Listed).</summary>
    private static void Listed(SimState state, int owner, SimListedCards? listed)
    {
        var player = state.MutablePlayer(owner);
        player.PileAutoPlays = player.PileAutoPlays! with { Listed = listed };
    }
}
