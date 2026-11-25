namespace SpireQoL.Sim;

public static partial class SimEngine
{
    // ------------------------------------------------------------------ card plays

    /// <summary>
    /// A card play (CardModel.OnPlayWrapper): per play, BeforeCardPlayed, the play started, its body (OnPlay, then the
    /// enchantment's and affliction's), the play finished, AfterCardPlayed. The turn counters follow the game's history
    /// entries (CardPlayStarted after BeforeCardPlayed; CardPlayFinished before AfterCardPlayed).
    /// </summary>
    private static void RunCardPlay(BranchSet branches, CardPlayAction cardPlayAction)
    {
        // Played from your hand: it has left it until it resolves (SimPlayer.PlayingFromHand), and no exhaust can take it from there.
        if (cardPlayAction.FromHand)
            branches.Do(branch =>
            {
                if (branch.Player(cardPlayAction.Play.Owner) == null) return;
                var player = branch.MutablePlayer(cardPlayAction.Play.Owner);
                player.PlayingFromHand++;
                if (player.ExhaustingInHand.Any(card => card.HandCard == cardPlayAction.HandCard))
                    player.ExhaustingInHand = player.ExhaustingInHand.Where(card => card.HandCard != cardPlayAction.HandCard).ToList();
            });
        // Each filtered take's cards in the hand as the card play starts (decision #189): its plays follow them from here.
        var matching = NamedCounts.Empty;
        foreach (var take in Flattened(cardPlayAction.Body).OfType<TakeFromHandAction>())
            if (take.Filter != null && !matching.Has(take.Filter)) matching = matching.With(take.Filter, take.Matching);
        // And each condition's on the kind of cards in your hand (decision #190: Impatience's Attacks), followed the same way.
        foreach (var counted in HandFilters(cardPlayAction.Body))
            if (!matching.Has(counted.Filter!)) matching = matching.With(counted.Filter!, counted.Matching);
        branches.Do(branch => branch.TakeMatching = matching);
        // Decided once, before the first play (OnPlayWrapper): where the card goes after (Feral), then how many times it plays (Echo Form).
        branches.Do(branch => DecideCardPlay(branch, cardPlayAction));
        var most = branches.States.Count == 0 ? 1 : branches.States.Max(branch => branch.PlayCountNow);
        for (var i = 0; i < most; i++)
        {
            var index = i;
            // Branches whose card play has fewer plays are over (each its own count: a branch's ledger can differ).
            var over = branches.States.Where(branch => branch.PlayCountNow <= index).ToList();
            if (over.Count > 0)
            {
                branches.States = branches.States.Where(branch => branch.PlayCountNow > index).ToList();
                branches.Aside += over.Count;
            }
            try
            {
                // A play after one that upgraded the card is its upgraded copy's (CardCmd.Upgrade changes the card itself: the next OnPlay reads it):
                // the branches whose card was upgraded play that copy, the others the card as it was.
                if (cardPlayAction.Upgraded is not { } upgraded || branches.States.All(branch => branch.PlayUpgrades == 0))
                {
                    if (index > 0) branches.Do(branch => { if (branch.PlayUpgrades > 0) branch.Unmodelled ??= UpgradedUnread; });
                    PlayOnce(branches, cardPlayAction.Play, cardPlayAction.Body, index);
                }
                else if (branches.States.All(branch => branch.PlayUpgrades > 0)) PlayOnce(branches, cardPlayAction.Play with { Card = upgraded.Card }, upgraded.Body, index);
                else
                {
                    var upgradedBranches = branches.States.Where(branch => branch.PlayUpgrades > 0).ToList();
                    branches.States = branches.States.Where(branch => branch.PlayUpgrades == 0).ToList();
                    branches.Aside += upgradedBranches.Count;
                    PlayOnce(branches, cardPlayAction.Play, cardPlayAction.Body, index);
                    branches.Aside -= upgradedBranches.Count;
                    var plain = branches.States;
                    branches.States = upgradedBranches;
                    branches.Aside += plain.Count;
                    try { PlayOnce(branches, cardPlayAction.Play with { Card = upgraded.Card }, upgraded.Body, index); }
                    finally
                    {
                        branches.Aside -= plain.Count;
                        branches.States.AddRange(plain);
                    }
                }
            }
            finally
            {
                if (over.Count > 0)
                {
                    branches.Aside -= over.Count;
                    branches.States.AddRange(over);
                }
            }
        }
        // Sent to the exhaust pile by a model (Corruption's Skills): its own exhaust sets off what reacts to exhausts, then its own reaction
        // (Drum of Battle's energy). One that exhausts itself has its exhaust in the play's After actions.
        if (!cardPlayAction.Play.Card.ExhaustsSelf && branches.States.Any(branch => branch.PlayResultLocation == SimResultLocation.Exhaust))
        {
            var others = branches.States.Where(branch => branch.PlayResultLocation != SimResultLocation.Exhaust).ToList();
            branches.States = branches.States.Where(branch => branch.PlayResultLocation == SimResultLocation.Exhaust).ToList();
            branches.Aside += others.Count;
            try
            {
                Run(branches, new CardEventAction(CardEvent.Exhausted, Count: 1) { Card = cardPlayAction.Play.Card });
                if (cardPlayAction.Play.Card.ExhaustEnergy > 0)
                    Run(branches, new CardEventAction(CardEvent.EnergyGained, cardPlayAction.Play.Card.ExhaustEnergy) { Player = cardPlayAction.Play.Owner });
            }
            finally
            {
                branches.Aside -= others.Count;
                branches.States.AddRange(others);
            }
        }
        // The card goes where the shape said (CardPlay.ResultPile): back on top of its owner's hand (Feral), keeping its slot.
        branches.Do(branch =>
        {
            if (branch.PlayResultLocation == SimResultLocation.HandTop) AddToHand(branch, cardPlayAction.Play.Owner, count: 1);
            branch.CurrentPlay = null; branch.PlayTaken = 0; branch.TakeMatching = NamedCounts.Empty; branch.AttackKilled = false; branch.PlayTests = NamedCounts.Empty;
            branch.PlayCountNow = 0; branch.PlayResultLocation = SimResultLocation.Default; branch.PlayChanges = SimState.CardChanges.None;
        });
    }

    /// <summary>
    /// One play of a card play (CardModel.OnPlayWrapper's loop body): BeforeCardPlayed, the play started, its body (OnPlay), its enchantment's
    /// OnPlay, the play finished, AfterCardPlayed, its enchantment's AfterCardPlayed; <paramref name="play"/>'s card as the plays before left it.
    /// </summary>
    private static void PlayOnce(BranchSet branches, SimCardPlay play, IReadOnlyList<SimAction> body, int index)
    {
        // The card play's serial: its first play's, kept by its later ones (a card play inside it has its own, and the frame gives this one back).
        branches.Do(branch => { branch.CurrentPlay = play with { Card = WithKeywords(play.Card, branch.PlayChanges.Keywords), PlayIndex = index, PlayCount = branch.PlayCountNow,
            Serial = index > 0 && branch.CurrentPlay is { Serial: > 0 } earlier ? earlier.Serial : ++branch.CardPlaysStarted }; branch.PlayCardBlockGains = 0; branch.PlayEvents = CardEventCounts.None; branch.PlayTaken = 0; branch.AttackKilled = false; branch.PlayTests = NamedCounts.Empty; });
        HookStep(branches, nameof(SimModel.BeforeCardPlayed), (model, owner, branch) => model.BeforeCardPlayed(branch, owner, branch.CurrentPlay!));
        branches.Do(branch => Count(branch, branch.CurrentPlay!, PlayStage.Started));
        foreach (var action in body) Run(branches, action);
        // Its enchantment's OnPlay, after the card's own (CardModel.OnPlayWrapper), read from its code (decision #217).
        RunEnchantment(branches, play.Card, HookName.OnPlay);
        // Then its affliction's (CardModel.OnPlayWrapper: Affliction.OnPlay after the enchantment's): none has one to run (decision #225); one whose
        // code a game update made unreadable makes the play unknown.
        if (play.Card.Affliction is not (SimAffliction.None or SimAffliction.Other) && SimModels.UnreadReason(play.Card.Affliction.ToString()) is { } afflictionUnread)
            branches.Do(branch => branch.Unmodelled ??= afflictionUnread);
        branches.Do(branch => Count(branch, branch.CurrentPlay!, PlayStage.Finished));
        HookStep(branches, nameof(SimModel.AfterCardPlayed), (model, owner, branch) => model.AfterCardPlayed(branch, owner, branch.CurrentPlay!));
        // Its enchantment hears its own card's play (Swift and Sown turn themselves off): it's a listener among its card's pile's cards, after
        // its owner's powers and relics, and no other listener reads its state, so it runs once the play's listeners have.
        RunEnchantment(branches, play.Card, HookName.AfterCardPlayed);
    }

    private const string UpgradedUnread = "the card was upgraded during its play and its upgraded copy wasn't read: its later plays aren't known";

    /// <summary>
    /// CardCmd.Upgrade of the card in play (cardPlay.Card): a card that can still be upgraded is (UpgradeInternal), so its later plays in this card
    /// play are its upgraded copy's (CardPlayAction.Upgraded); one that can't is left as it is (the command's !IsUpgradable). A card upgraded a
    /// second time in one card play (a card with more than one upgrade) makes its later plays unknown: only one upgraded copy is read. The card
    /// itself changes (UpgradeInternal on it), so the listeners after this one in the same play see it upgraded.
    /// </summary>
    internal static void UpgradeCardInPlay(SimState state)
    {
        if (state.CurrentPlay is not { Card.Upgradable: true } play) return;
        state.PlayChanges = state.PlayChanges with { Upgrades = state.PlayChanges.Upgrades + 1 };
        if (state.PlayChanges.Upgrades > 1) state.Unmodelled ??= "the card was upgraded twice during its play: its later plays aren't known";
        else if (state.PlayChanges.Upgraded is { } upgraded) state.CurrentPlay = play with { Card = WithKeywords(upgraded.Card, state.PlayChanges.Keywords) };
    }

    /// <summary>
    /// CardCmd.ApplyKeyword of the card in play (cardPlay.Card: Master Planner's Sly): it has it from now on, for the listeners after this one and
    /// its later plays in this card play (card.AddKeyword). Of the keywords, the simulator's card carries Ethereal (SimCard.Ethereal, a hook's
    /// fact); nothing in a play reads the others, so nothing is kept for them (Sly acts as the card is discarded; Exhaust doesn't send it to the
    /// exhaust pile: its result pile was decided before its first play, CardModel.GetResultLocationForCardPlay).
    /// </summary>
    internal static void KeywordCardInPlay(SimState state, string keyword)
    {
        if (keyword != EtherealKeyword || state.CurrentPlay is not { } play || state.PlayChanges.Keywords.Has(keyword)) return;
        state.PlayChanges = state.PlayChanges with { Keywords = state.PlayChanges.Keywords.With(keyword, amount: 1) };
        state.CurrentPlay = play with { Card = WithKeywords(play.Card, state.PlayChanges.Keywords) };
    }

    /// <summary>The card with the keywords a play added that the simulator's card carries (Ethereal).</summary>
    private static SimCard WithKeywords(SimCard card, NamedCounts keywords) =>
        keywords.Has(EtherealKeyword) && !card.Ethereal ? card with { Ethereal = true } : card;

    private const string EtherealKeyword = "Ethereal";

    /// <summary>
    /// One of a card's enchantment's programs on each branch (decision #217: SimModels.Enchantment, read from its class's code). An enchantment whose
    /// code read whole at the reviewed builds and doesn't now makes the play unknown (decision #92); one with nothing to run, or whose code the
    /// reader never read, does nothing here (its card's damage and block math is the game's own, called live by the adapter).
    /// </summary>
    private static void RunEnchantment(BranchSet branches, SimCard card, HookName hook)
    {
        if (card.Enchantment is not { } enchantment) return;
        if (SimModels.Enchantment(enchantment.Name) is { } read)
        {
            if (read.Runs(hook)) branches.Each(branch => read.RunEnchantment(hook, branch, branch.CurrentPlay!));
            return;
        }
        if (SimModels.UnreadReason(enchantment.Name) is { } why) branches.Do(branch => branch.Unmodelled ??= why);
    }

    /// <summary>
    /// A card play's shape, before its first play (CardModel.OnPlayWrapper): Hook.ModifyCardPlayResultLocation over every listener in
    /// order, then AfterModifyingCardPlayResultLocation for each that changed it; then its play count: the action's own, or (CountByModels)
    /// that through Hook.ModifyCardPlayCount, then AfterModifyingCardPlayCount for each that changed it.
    /// </summary>
    private static void DecideCardPlay(SimState state, CardPlayAction cardPlayAction)
    {
        var play = cardPlayAction.Play with { PlayIndex = 0 };
        // Its own result pile first (CardModel.GetResultLocationForCardPlay): nowhere for a Power or a copy, the exhaust pile for one that
        // exhausts itself, else the discard pile.
        var location = play.Card.Type == SimCardType.Power || play.Card.Dupe ? SimResultLocation.Nowhere
            : play.Card.ExhaustsSelf ? SimResultLocation.Exhaust : SimResultLocation.Default;
        List<(SimModel Model, int Owner)>? moved = null;
        foreach (var (model, owner) in Listeners(state))
        {
            var before = location;
            location = model.ModifyCardPlayResultLocation(state, owner, play, location);
            if (location != before) (moved ??= new()).Add((model, owner));
        }
        if (moved != null) foreach (var (model, owner) in moved) model.AfterModifyingCardPlayResultLocation(state, owner, play, location);
        state.PlayResultLocation = location;
        var count = Math.Max(1, play.PlayCount);
        if (cardPlayAction.CountByModels)
        {
            List<(SimModel Model, int Owner)>? counted = null;
            foreach (var (model, owner) in Listeners(state))
            {
                var before = count;
                count = model.ModifyCardPlayCount(state, owner, play, count);
                if (count != before) (counted ??= new()).Add((model, owner));
            }
            if (counted != null) foreach (var (model, owner) in counted) model.AfterModifyingCardPlayCount(state, owner, play);
        }
        state.PlayCountNow = Math.Max(1, count);
        state.PlayChanges = SimState.CardChanges.None with { Upgraded = cardPlayAction.Upgraded };
    }

    /// <summary>
    /// A count the action reads on the state as it runs (decision #189): the number it gives, the cards the play's last take took, or the
    /// room in the play owner's hand now.
    /// </summary>
    internal static int CountOf(SimState state, SimCount from, int given)
    {
        switch (from)
        {
            case SimCount.CardsTaken: return state.PlayTaken;
            case SimCount.HandRoom:
                var owner = state.CurrentPlay?.Owner ?? Who.You;
                return state.Player(owner) is { } player ? Math.Min(given, RoomIn(player)) : given;
            default: return given;
        }
    }

    /// <summary>The most any branch counts (CountOf): how many rounds an action with a count per branch runs, each branch its own.</summary>
    private static int MostOf(BranchSet branches, SimCount from, int given) =>
        from == SimCount.Given ? given : branches.States.Count == 0 ? 0 : branches.States.Max(branch => CountOf(branch, from, given));

    /// <summary>
    /// Cards a play takes from its owner's hand (TakeFromHandAction, decision #189), on the state as it runs: every card of its collection
    /// there now, or a pick's or a roll's most of the ones it can take. They leave (the whole hand: what was followed in it goes with it),
    /// then each sets off its event and the take's per-card actions, one card after another; how many is the play's CardsTaken.
    /// </summary>
    private static void RunTakeFromHand(BranchSet branches, TakeFromHandAction take) => branches.Each(branch => TakeFromHand(branch, take));

    private static IEnumerable<SimState> TakeFromHand(SimState state, TakeFromHandAction take)
    {
        var owner = state.CurrentPlay?.Owner ?? Who.You;
        if (state.Player(owner) is not { } player) return new[] { state };
        int count;
        CardEvent? cardEvent = take.Event;
        if (take.FromDrawPile)
        {
            // A pick from the draw pile (Cleanse): as many as it holds (with no pile read, the adapter's count, less what's been taken).
            var inPile = player.Draw?.Count ?? Math.Max(0, take.Matching - state.TakeMatching[TakenKey(take)]);
            count = Math.Min(take.Most, inPile);
            if (player.Draw == null) state.TakeMatching = state.TakeMatching.Add(TakenKey(take), count);
            state.PlayTaken = count;
            IEnumerable<SimState> picked = new[] { state };
            for (var i = 0; i < count; i++) picked = Then(picked, branch => ExhaustChosenFromDrawPile(branch, owner));
            return TakenEvents(picked, take, cardEvent, count);
        }
        var inHand = Math.Max(0, player.HandCount - player.PlayingFromHand);
        var known = take.Filter == null || state.TakeMatching.DataOf(take.Filter) == 0;
        var matching = take.Filter == null ? inHand : Math.Min(inHand, state.TakeMatching[take.Filter]);
        if (take.Filter != null && !known && take.EventIfNot == null)
        {
            // Cards joined the hand whose kind wasn't read: how many it takes isn't known.
            state.Unmodelled ??= "it takes cards of a kind from your hand, and cards joined it whose kind wasn't read";
            return new[] { state };
        }
        var pool = take.EventIfNot != null ? inHand : matching;
        count = take.Way == HandTakeWay.Each ? pool : Math.Min(take.Most, pool);
        if (take.EventIfNot != null)
        {
            // A pick its test sends one way or the other: the event is certain only when every card it could take goes the same way.
            cardEvent = !known ? null : matching >= inHand ? take.Event : matching == 0 ? take.EventIfNot : null;
            if (take.Filter != null && count > 0)
                state.TakeMatching = cardEvent == take.Event ? state.TakeMatching.Add(take.Filter, -count)
                    : cardEvent == take.EventIfNot ? state.TakeMatching : state.TakeMatching.With(take.Filter, matching, data: 1);
        }
        else if (take.Filter != null && count > 0) state.TakeMatching = state.TakeMatching.Add(take.Filter, -count);
        if (take.Leaves && count > 0)
        {
            var wholeHand = take.Way == HandTakeWay.Each && take.Filter == null;
            RunLeaveHand(state, new LeaveHandAction(count) { Whole = wholeHand });
            // A take of some cards of the hand: the other filtered takes' cards may be among them (only a take of one kind follows its own).
            if (!wholeHand && take.Filter == null && state.Player(owner) is { } after)
                foreach (var entry in state.TakeMatching.Items)
                {
                    var most = Math.Min(entry.Amount, Math.Max(0, after.HandCount - after.PlayingFromHand));
                    // A condition's count (decision #190) is exact or not known: the cards taken may or may not have been the ones it counts.
                    var exact = !IsConditionFilter(entry.Name) || most == Math.Max(0, entry.Amount - count);
                    state.TakeMatching = state.TakeMatching.With(entry.Name, most, exact ? entry.Data : 1);
                }
            if (wholeHand) state.TakeMatching = ZeroedMatching(state.TakeMatching);
        }
        state.PlayTaken = count;
        return TakenEvents(new[] { state }, take, cardEvent, count);
    }

    /// <summary>Each card taken in turn: its event, then the take's per-card actions.</summary>
    private static IEnumerable<SimState> TakenEvents(IEnumerable<SimState> states, TakeFromHandAction take, CardEvent? cardEvent, int count)
    {
        if (count == 0 || cardEvent == null && take.EachCard.Count == 0) return states;
        var result = new List<SimState>();
        foreach (var state in states)
        {
            var sub = new BranchSet(state, nested: true);
            for (var i = 0; i < count; i++)
            {
                if (cardEvent is { } each) RunCardEvent(sub, new CardEventAction(each, Count: 1) { Indirect = true, Card = each == CardEvent.Generated ? take.Made : null });
                foreach (var action in take.EachCard) Run(sub, action);
            }
            result.AddRange(sub.States);
        }
        return result;
    }

    /// <summary>The count of cards a take from your draw pile took, kept with no pile read.</summary>
    private static string TakenKey(TakeFromHandAction take) => "taken:" + (take.Filter ?? "draw pile");

    /// <summary>The whole hand gone: no filtered take has a card left there, and what joins after is counted from none.</summary>
    private static NamedCounts ZeroedMatching(NamedCounts matching)
    {
        foreach (var entry in matching.Items) matching = matching.With(entry.Name, amount: 0, data: 0);
        return matching;
    }

    /// <summary>
    /// A card joins its owner's hand during a card play: each filtered take it would be taken by counts it (SimDrawCard.TakenBy); with no
    /// profile read (null), whether it would isn't known.
    /// </summary>
    internal static void JoinsTakes(SimState state, IReadOnlyList<string>? takenBy, bool known, bool intoHand, bool made)
    {
        if (state.TakeMatching.Count == 0) return;
        foreach (var entry in state.TakeMatching.Items)
        {
            if (entry.Name.StartsWith("taken:", StringComparison.Ordinal)) continue;
            // A take of every pile counts what's made anywhere (a card moving from a pile into the hand was counted already); a take of
            // the hand what joins the hand.
            var everyPile = entry.Name.StartsWith("all:", StringComparison.Ordinal);
            if (everyPile ? !made : !intoHand) continue;
            if (!known) state.TakeMatching = state.TakeMatching.With(entry.Name, entry.Amount, data: 1);
            else if (takenBy?.Contains(entry.Name) == true) state.TakeMatching = state.TakeMatching.With(entry.Name, entry.Amount + 1, entry.Data);
        }
    }

    /// <summary>
    /// Cards leaving the playing player's hand mid-play (what it discards and exhausts from there): the hand is that much smaller from
    /// now, so a draw after it has their room and what reads the hand at the end of the turn counts what's left (Cloak Clasp, Regret).
    /// The whole hand: what was in it is gone too (its Burns, its Ethereal cards, what Stampede could pick, what acts when exhausted).
    /// Which cards go when it isn't the whole hand isn't kept, but how many is.
    /// </summary>
    private static void RunLeaveHand(SimState state, LeaveHandAction leave)
    {
        var owner = state.CurrentPlay?.Owner ?? Who.You;
        if (state.Player(owner) == null) return;
        var player = state.MutablePlayer(owner);
        var inHand = Math.Max(0, player.HandCount - player.PlayingFromHand);
        player.HandCount -= leave.Whole ? inHand : Math.Min(leave.Count, inHand);
        if (!leave.Whole)
        {
            // The cards it takes are the ones that change nothing the simulation follows (decision #167: a choice inside the play is
            // your later decision): there are enough of them unless the cards it follows in your hand (acting at the end of your turn,
            // acting when exhausted, Ethereal, what Stampede or Simpleton could pick) may be all that's left to take.
            var picks = player.AutoHand == null ? 0 : player.Simpleton > 0 ? player.AutoHand.Count : player.AutoHand.Count(handCard => handCard.IsAttack);
            var followed = player.TurnEndCards.Count + player.ExhaustingInHand.Count + player.Fading + picks;
            if (inHand - followed < Math.Min(leave.Count, inHand))
                state.Unmodelled ??= "it takes cards from your hand, and the cards the simulation follows there may be all it can take";
            return;
        }
        player.TurnEndCards = new();
        player.Fading = player.HexFading = 0;
        player.ExhaustingInHand = new();
        if (player.AutoHand != null) player.AutoHand = new();
    }

    /// <summary>
    /// A play exhausting others: the cards that act when exhausted among the ones it can take (ExhaustFromHandAction) give their
    /// energy (Drum of Battle's AfterCardExhausted: PlayerCmd.GainEnergy). All of them; one card at random, a branch per such card by
    /// its share of the cards in the hand and one where another card went. When you choose (decision #167: a choice inside the play
    /// is your later decision), your pick is a card, never a particular one: the play does what any pick would do. Only when every
    /// card you could take acts alike when exhausted is that what any pick does; otherwise nothing that depends on which card is
    /// followed, and nothing is marked unknown. Cleanse's pick from your draw pile the same way.
    /// </summary>
    private static IEnumerable<SimState> RunExhaustFromHand(SimState state, ExhaustFromHandAction exhaust)
    {
        var owner = state.CurrentPlay?.Owner ?? Who.You;
        if (state.Player(owner) is not { } player) return new[] { state };
        if (exhaust.FromDrawPile) return ExhaustChosenFromDrawPile(state, owner);
        bool Takes(ExhaustingCard card) => (exhaust.Only == SimCardType.None || card.Type == exhaust.Only) && (exhaust.Except == SimCardType.None || card.Type != exhaust.Except);
        var taken = player.ExhaustingInHand.Where(Takes).ToList();
        if (taken.Count == 0) return new[] { state };
        switch (exhaust.Pick)
        {
            case ExhaustPick.Chosen:
            {
                // Your pick is a card (decision #92: never which one): what any pick would do, nothing that depends on which.
                var inHand = player.HandCount - player.PlayingFromHand;
                var filtered = exhaust.Only != SimCardType.None || exhaust.Except != SimCardType.None;
                if (filtered || inHand > taken.Count || taken.Select(card => (card.Energy, card.Type)).Distinct().Count() > 1) return new[] { state };
                // Only cards that act when exhausted, all alike: one of them.
                var mine = state.MutablePlayer(owner);
                mine.ExhaustingInHand = mine.ExhaustingInHand.Where(other => other.HandCard != taken[0].HandCard).ToList();
                return ProcCardEvent(state, CardEvent.EnergyGained, taken[0].Energy, owner, indirect: false);
            }
            case ExhaustPick.All:
            {
                state.MutablePlayer(owner).ExhaustingInHand = player.ExhaustingInHand.Where(card => !Takes(card)).ToList();
                IEnumerable<SimState> states = new[] { state };
                foreach (var card in taken) states = Then(states, branch => ProcCardEvent(branch, CardEvent.EnergyGained, card.Energy, owner, indirect: false));
                return states;
            }
            default:
            {
                // One of the cards in the hand, each equally likely (CombatCardSelection.NextItem); of a kind (Thrash), the count of
                // that kind isn't held: unknown.
                if (exhaust.Only != SimCardType.None || exhaust.Except != SimCardType.None)
                {
                    state.Unmodelled ??= "it exhausts a random card of a kind from your hand, and a card that acts when exhausted (Drum of Battle) is one";
                    return new[] { state };
                }
                var inHand = Math.Max(taken.Count, player.HandCount - player.PlayingFromHand);
                var result = new List<SimState>();
                foreach (var card in taken)
                {
                    var branch = state.Clone();
                    branch.Weight = state.Weight / inHand;
                    var mine = branch.MutablePlayer(owner);
                    mine.ExhaustingInHand = mine.ExhaustingInHand.Where(other => other.HandCard != card.HandCard).ToList();
                    result.AddRange(ProcCardEvent(branch, CardEvent.EnergyGained, card.Energy, owner, indirect: false));
                }
                if (inHand > taken.Count)
                {
                    state.Weight = state.Weight * (inHand - taken.Count) / inHand;
                    result.Add(state); // another card went
                }
                return result;
            }
        }
    }

    /// <summary>
    /// Cleanse: the card you choose from your draw pile is exhausted (decision #167: your later decision): a plain card when the pile
    /// has one, else the same whichever you take (its energy when it acts when exhausted). When they differ, the pick is a card and
    /// never a particular one (decisions #92, #188): the pile holds one card fewer (SimDrawPile.Picked), and nothing a particular card
    /// would do is followed. With no pile read, only its count is kept.
    /// </summary>
    private static IEnumerable<SimState> ExhaustChosenFromDrawPile(SimState state, int owner)
    {
        if (state.Player(owner)?.Draw is not { Count: > 0 } pile) return new[] { state };
        var profiles = pile.Top.Concat(pile.Rest.Select(group => group.Card)).DistinctBy(card => card.Key).ToList();
        var card = profiles.FirstOrDefault(profile => profile.Plain) ?? (profiles.Count == 1 ? profiles[0] : null);
        if (card == null)
        {
            state.MutablePlayer(owner).Draw = pile.WithOnePicked();
            return new[] { state };
        }
        var simPlayer = state.MutablePlayer(owner);
        simPlayer.Draw = pile.Rest.FindIndex(group => group.Card.Key == card.Key) is var restIndex and >= 0
            ? pile.WithoutOneOf(restIndex)
            : pile.WithoutTopAt(pile.Top.FindIndex(top => top.Key == card.Key));
        return card.Exhausting is { } acting ? ProcCardEvent(state, CardEvent.EnergyGained, acting.Energy, owner, indirect: false) : new[] { state };
    }

    /// <summary>The play's turn counters on its owner (SimPlayer.Turn): all, by type, by tag.</summary>
    private static void Count(SimState state, SimCardPlay play, PlayStage stage)
    {
        if (state.Player(play.Owner) == null) return;
        // A payout a play decides by the fight's log of plays (Ripple Basin's: no Attack this turn; Tune's: a Ki this turn): the play counted here
        // can turn it off, giving the bonus up there (decision #239), or on, setting it up (decision #244).
        List<(ReadModel Model, int Owner, bool WasOn)>? watching = null;
        foreach (var (model, owner) in Listeners(state))
            if (model is ReadModel { PayoutReadsThePlayLog: true } read) (watching ??= new()).Add((read, owner, read.PayoutOn(state, owner)));
        var player = state.MutablePlayer(play.Owner);
        // What it's the first of this turn, as it starts (the game's log knows the play itself: MelodyCmd.IsFirstPlayThisTurn).
        if (stage == PlayStage.Started && ReferenceEquals(state.CurrentPlay, play)) state.CurrentPlay = play with { FirstThisTurn = SimKeys.FirstThisTurn(player.Turn, play.Card) };
        player.Turn = SimKeys.CountPlay(player.Turn, stage, play.Card.Type, play.Card.Tags);
        if (state.History.Count > 0) RecordPlay(state, play, stage);
        if (watching != null)
            foreach (var (read, owner, wasOn) in watching) read.Decided(state, owner, wasOn);
    }

    // ------------------------------------------------------------------ attacks

    private static void RunAttack(BranchSet branches, AttackAction at)
    {
        // A shot with no arrow does nothing: no attack, so no hooks either.
        if (at.NeedsArrow && branches.States.All(branch => branch.ArrowAwayAt < 0)) return;
        // Its hits, each branch its own when it reads them as it runs (decision #189: Fiend Fire's cards taken); none at all: no attack.
        // A count of the fight's log adds hits as the simulation goes (Finisher: decision #191).
        int HitsOf(SimState state) => CountOf(state, at.HitsFrom, at.Hits) + (at.HitsLogged is { } logged ? (int)logged.More(state, state.CurrentPlay?.Owner ?? Who.You) : 0);
        var mostHits = branches.States.Count == 0 ? 0 : branches.States.Max(HitsOf);
        if (mostHits <= 0) return;
        bool Hits(SimState state, int hit) => hit < HitsOf(state);
        var dealer = at.ByOsty ? Who.YourOsty : Who.You;
        branches.Do(branch =>
        {
            branch.AttackSerial++;
            branch.CurrentAttack = branch.AttackSerial;
            branch.AttackDealt = 0;
            branch.AttackKilled = false;
        });
        AttackInfo Info(SimState state) => new(state.CurrentAttack, state.CurrentPlay?.Owner is { } playOwner && !at.ByOsty ? playOwner : dealer, at.Damage.Props, state.CurrentPlay?.Card);
        HookStep(branches, nameof(SimModel.BeforeAttack), (model, owner, branch) => model.BeforeAttack(branch, owner, Info(branch)));
        for (var i = 0; i < mostHits; i++)
        {
            var hit = i;
            switch (at.Mode)
            {
                case SimPlay.Target.Single:
                    branches.Each(branch =>
                    {
                        var target = at.TargetId >= 0 ? at.TargetId : branch.CurrentPlay?.Target ?? Who.None;
                        if (!Hits(branch, hit) || branch.Enemy(target) is not { Alive: true }) return new[] { branch };
                        return HitOnce(branch, Info(branch).Dealer, target, at);
                    });
                    break;
                case SimPlay.Target.All:
                    branches.Each(branch => Hits(branch, hit) ? HitAllRound(branch, rounds: 1) : new[] { branch });
                    break;
                case SimPlay.Target.Random:
                    branches.Each(branch => Hits(branch, hit) ? RandomTarget(branch, (copy, id) => HitOnce(copy, Info(copy).Dealer, id, at)) : new[] { branch });
                    break;
            }
        }
        // The game logs the attack (CreatureAttackedEntry, its attacker) before AfterAttack (decision #191: Rattle and Flatten count Osty's).
        branches.Do(branch => { if (branch.History.Count > 0) Record(branch, new HistoryEvent(HistoryKind.CreatureAttacked, Info(branch).Dealer)); });
        HookStep(branches, nameof(SimModel.AfterAttack), (model, owner, branch) => model.AfterAttack(branch, owner, Info(branch)));
        // Blossom Shot: Stretch for the damage it dealt (PowerCmd.Apply<StretchPower> after the attack, inside the shot).
        if (at.StretchFromDamage)
            branches.Each(branch =>
            {
                var owner = branch.CurrentPlay?.Owner ?? Who.You;
                return branch.AttackDealt > 0 ? ApplyAndTell(branch, owner, owner, PowerId.Stretch, branch.AttackDealt, debuff: false) : new[] { branch };
            });
        branches.Do(branch => branch.CurrentAttack = 0);

        // One hit on every hittable enemy; with RepeatPerKill (Echoing Slash), one more round per enemy it killed.
        IEnumerable<SimState> HitAllRound(SimState state, int rounds)
        {
            if (rounds <= 0) return new[] { state };
            var ids = state.Targetable.Select(enemy => enemy.Id).ToList();
            IEnumerable<SimState> states = new[] { state };
            foreach (var id in ids)
            {
                var target = id;
                states = Then(states, branch => branch.Enemy(target) is { Alive: true } ? HitOnce(branch, Info(branch).Dealer, target, at) : new[] { branch });
            }
            return Then(states, branch => HitAllRound(branch, rounds - 1 + (at.RepeatPerKill ? ids.Count(id => branch.Enemy(id) is { Alive: false }) : 0)));
        }
    }

    /// <summary>One random living enemy: a branch per possible target.</summary>
    private static IEnumerable<SimState> RandomTarget(SimState state, Func<SimState, int, IEnumerable<SimState>> then)
    {
        var targets = state.Targetable.Select(enemy => enemy.Id).ToList();
        if (targets.Count == 0) return new[] { state };
        if (targets.Count == 1) return then(state, targets[0]);
        var all = new List<SimState>();
        foreach (var id in targets)
        {
            var branch = state.Clone();
            branch.Weight = state.Weight / targets.Count;
            all.AddRange(then(branch, id));
        }
        return all;
    }

    /// <summary>
    /// One hit of an attack on an enemy: its damage from the pipeline now (the modelled sources' terms and the captured ones), then
    /// the damage itself (SimEngine.Damage: Thorns, block, HP loss, the hooks).
    /// </summary>
    private static IEnumerable<SimState> HitOnce(SimState state, int dealer, int target, AttackAction at)
    {
        if (at.NeedsArrow && state.ArrowAwayAt < 0) return new[] { state };
        var spec = at.Damage;
        var card = state.CurrentPlay?.Card;
        var ctx = new DamageCtx(dealer, target, spec.Props, card, state.CurrentPlay);
        // A replay's damage has what the card gained from its plays before it (decision #160).
        var baseDamage = spec.BaseFor(target) + spec.GrowsEachPlay * (state.CurrentPlay?.PlayIndex ?? 0)
            + (spec.Logged?.More(state, state.CurrentPlay?.Owner ?? Who.You) ?? 0m); // a calculated value on the fight's log (decision #191)
        var terms = spec.TermsFor(target);
        if (spec.EnchantFirstPlayOnly && state.CurrentPlay?.PlayIndex > 0) terms = WithoutEnchantment(terms);
        var amount = spec.Fixed ? Math.Max(0, (int)baseDamage) : ModifiedDamage(state, baseDamage, ctx, terms);
        // The first hit on each enemy, for the damage-formula audit (not after Mighty Wings gave Strength mid-attack: the preview
        // can't know that).
        if (card != null && !at.ByOsty && !state.FirstHits.ContainsKey(target) && !state.Fired.Has(RelicId.MightyWings.Name)) state.FirstHits = state.FirstHits.Add(target, amount);
        if (amount <= 0) return new[] { state };
        if (state.Enemy(target) is { Alive: true } enemy)
        {
            Damage(state, enemy, amount, dealer, spec.Props, card);
            // The hit killed its target (its DamageResult's WasTargetKilled, a revive after it included): SimTest.AttackKilled.
            if (enemy.Hp <= 0) state.AttackKilled = true;
        }
        // Rain Shot, Omnislice: the target's damage to every other hittable enemy. Beta: a powered hit, only caps apply. Main (MainRule
        // .SplashIsUnpowered): unpowered (Unpowered | Move), through every damage modifier.
        if (at.Splash)
        {
            var unpowered = state.MainRules.HasFlag(MainRule.SplashIsUnpowered);
            var props = unpowered ? SimProps.Unpowered | SimProps.Move : spec.Props;
            foreach (var other in state.Targetable.Where(simEnemy => simEnemy.Id != target).ToList())
            {
                var splash = unpowered ? ModifiedDamage(state, amount, new DamageCtx(dealer, other.Id, props, card, state.CurrentPlay), captured: null) : amount;
                if (splash > 0) Damage(state, other, splash, dealer, props, card);
            }
        }
        AllyDeaths(state); // a death runs its hooks as it happens (AfterDeath: Impaled lets its arrows go, Crab Rage, Explosive)
        return DrainPending(state); // what the hit set off where hooks can't branch (an arrow knocked loose: Yah, Twine)
    }

    /// <summary>The terms without the card's enchantment: it turned itself off after the play before (Vigorous).</summary>
    private static CapturedTerms WithoutEnchantment(CapturedTerms terms) => terms with { EnchantAdd = 0m, EnchantMul = 1m };

    // ------------------------------------------------------------------ block, powers, HP

    private static void RunBlock(BranchSet branches, GainBlockAction gainBlockAction)
    {
        // Its gains, each branch its own when it reads them as it runs (decision #189).
        for (var i = 0; i < MostOf(branches, gainBlockAction.TimesFrom, gainBlockAction.Times); i++)
        {
            var gain = i;
            branches.Each(branch => gain < CountOf(branch, gainBlockAction.TimesFrom, gainBlockAction.Times) ? BlockOnce(branch, gainBlockAction) : new[] { branch });
        }
    }

    /// <summary>
    /// One block gain of the action (CreatureCmd.GainBlock, its hooks in SimPipelines): the block pipeline (ModifyBlock), then
    /// AfterModifyingBlockAmount for the sources that changed the amount, the gain (Juggernaut, Beacon of Hope), then AfterBlockGained.
    /// </summary>
    internal static IEnumerable<SimState> BlockOnce(SimState state, GainBlockAction gainBlockAction)
    {
        if (!state.TakesPart(gainBlockAction.To)) return new[] { state };
        var mods = gainBlockAction.Fixed ? null : new List<(SimModel Model, int Owner)>();
        // A replay's block has what the card gained from its plays before it (decision #160).
        var baseBlock = gainBlockAction.Base + BlockBaseNow(state, gainBlockAction) + gainBlockAction.GrowsEachPlay * (state.CurrentPlay?.PlayIndex ?? 0);
        var blockTerms = gainBlockAction.Captured;
        if (blockTerms != null && gainBlockAction.EnchantFirstPlayOnly && state.CurrentPlay?.PlayIndex > 0) blockTerms = WithoutEnchantment(blockTerms);
        var amount = gainBlockAction.Fixed ? Math.Max(0, (int)baseBlock)
            : CalcBlock(state, baseBlock, new BlockCtx(gainBlockAction.To, gainBlockAction.Props, state.CurrentPlay?.Card, state.CurrentPlay), blockTerms, mods);
        // Before the gain (CreatureCmd.GainBlock): what changed the amount hears it first, then the block lands.
        if (mods != null && amount > 0) foreach (var (model, o) in mods) model.AfterModifyingBlockAmount(state, o, amount, state.CurrentPlay);
        var states = GainBlock(state, amount, state.EndOfTurnNow && gainBlockAction.To == Who.You && state.Log ? state.CurrentPlay?.Card.Name ?? "block" : null, gainBlockAction.To);
        // Block from a card play this turn: the turn counts each play that gained any once (Unmovable counts the earlier plays, v0.114.0: DistinctBy
        // their card play).
        if (amount > 0 && state.CurrentPlay != null && gainBlockAction.Props.IsCardOrMonsterMove())
            states = states.Select(branch => { if (branch.PlayCardBlockGains++ == 0 && branch.CurrentPlay!.Owner == Who.You) branch.You.Turn = branch.You.Turn.Add(SimKeys.CardBlock, delta: 1); return branch; }).ToList();
        return Then(states, branch => Hook(branch, nameof(SimModel.AfterBlockGained), (model, owner, copy) => model.AfterBlockGained(copy, owner, gainBlockAction.To, amount, copy.CurrentPlay?.Card)));
    }

    /// <summary>
    /// What a block gain's amount reads as it runs (SimBlockBase): the play's last attack's dealt damage (Fisticuffs), the gainer's block
    /// (Entrench), the play's target's block times its multiplier (Mimic); 0 for an amount given whole.
    /// </summary>
    private static decimal BlockBaseNow(SimState state, GainBlockAction gainBlockAction)
    {
        switch (gainBlockAction.BaseFrom)
        {
            case SimBlockBase.AttackDealt:
                return state.AttackDealt;
            case SimBlockBase.OwnBlock:
                return state.Player(gainBlockAction.To)?.Block ?? 0;
            case SimBlockBase.TargetBlock:
                var target = state.CurrentPlay?.Target ?? Who.None;
                var block = state.Player(target)?.Block ?? (target >= 0 ? state.Enemy(target)?.Block ?? 0 : 0);
                return gainBlockAction.PerBlock * block;
            default:
                return 0;
        }
    }

    /// <summary>
    /// PowerCmd.Apply: the amount through ModifyPowerAmountGiven (Snecko Skull...), then Artifact takes a debuff (a charge
    /// each), else the power goes on (poison into the enemy's poison).
    /// </summary>
    internal static bool ApplyPower(SimState state, int applier, int target, PowerId power, int amount, bool debuff) =>
        ApplyPower(state, applier, target, power, amount, debuff, out _);

    /// <summary>
    /// Hook.ModifyPowerAmountGiven (PowerCmd.Apply): every listener's additive term on the amount so far (Snecko Skull's Poison), then every
    /// listener's multiplicative one (Unsettling Lamp's doubling), then Hook.AfterModifyingPowerAmountGiven told to each that changed it, in the
    /// listeners' order (their flash). The amount given.
    /// </summary>
    internal static int AmountGiven(SimState state, int applier, int target, PowerId power, int amount, bool debuff)
    {
        var listeners = Listeners(state);
        HashSet<(SimModel Model, int Owner)>? modifiers = null;
        foreach (var (model, owner) in listeners)
        {
            var term = model.ModifyPowerAmountGivenAdditive(state, owner, power, amount, applier, target, debuff);
            if (term == 0) continue;
            amount += term;
            (modifiers ??= new()).Add((model, owner));
        }
        foreach (var (model, owner) in listeners)
        {
            var factor = model.ModifyPowerAmountGivenMultiplicative(state, owner, power, amount, applier, target, debuff);
            if (factor != 1m)
            {
                amount = (int)(amount * factor);
                (modifiers ??= new()).Add((model, owner));
            }
            amount = model.ModifyPowerAmountGiven(state, owner, power, amount, applier, target, debuff);
        }
        if (modifiers != null)
            foreach (var (model, owner) in listeners)
                if (modifiers.Contains((model, owner))) model.AfterModifyingPowerAmountGiven(state, owner, power);
        return amount;
    }

    /// <summary>
    /// PowerCmd.Apply, as <see cref="ApplyPower(SimState, int, int, PowerId, int, bool)"/>, giving the amount that landed (<paramref name="landed"/>:
    /// after ModifyPowerAmountGiven and the players' scaling, what Hook.AfterPowerAmountChanged is told). A new one on an enemy in a fight of more
    /// than one player is scaled for the players (PowerModel.GetScaledAmountForMultiplayer, read from its code: SimModels.ScaledForPlayers); more
    /// of one already there isn't (PowerCmd.ModifyAmount). One already there that the change leaves at 0 (or below, for a power that can't go below
    /// 0) goes (PowerModel.ShouldRemoveDueToAmount, its AllowNegative read from its code).
    /// </summary>
    internal static bool ApplyPower(SimState state, int applier, int target, PowerId power, int amount, bool debuff, out int landed)
    {
        landed = 0;
        if (amount == 0) return false;
        // Creature.CanReceivePowers: every listener's ShouldAllowHitting of the target (Die For You and Wuppy's Loyalty, read from their code: a
        // dead creature receives none, as a pet killed by the hit Monarch's Gaze is told of).
        if (!CanReceivePowers(state, target)) return false;
        amount = AmountGiven(state, applier, target, power, amount, debuff);
        if (amount == 0) return false;
        var powers = PowersOf(state, target);
        if (debuff && powers[PowerId.Artifact] > 0)
        {
            SetPowers(state, target, powers[PowerId.Artifact] > 1 ? powers.Add(PowerId.Artifact, delta: -1) : powers.Without(PowerId.Artifact));
            return false;
        }
        if (Who.IsEnemy(target) && state.Teammates.Count > 0 && !powers.Has(power))
        {
            amount = SimModels.ScaledForPlayers(state, power, amount);
            if (amount == 0) return false;
        }
        landed = amount;
        // The game's log has it now (PowerReceivedEntry: the power, who applied it), before its hooks (decision #191).
        if (state.History.Count > 0) Record(state, new HistoryEvent(HistoryKind.PowerReceived, applier) { Power = power });
        // The game's preview of an enemy's next poison tick (PoisonPower.CalculateTotalDamageNextTurn, read by the adapter: the play check
        // compares it) follows the stacks landing; the stacks are the power's entry, landing below as any power's.
        if (target >= 0 && power == PowerId.Poison) state.Enemy(target)?.RaisePoisonPreview(amount);
        // Accelerant on any player: every enemy's poison preview ticks that many more times (PoisonPower.TriggerCount sums every living
        // player's; the tick itself reads it from the board as it runs).
        if (Who.IsPlayer(target) && power == PowerId.Accelerant)
            foreach (var enemy in state.Enemies)
            {
                var more = enemy.PoisonTriggers + amount;
                enemy.Poison += PoisonTick(enemy.PoisonStacks, more) - PoisonTick(enemy.PoisonStacks, enemy.PoisonTriggers);
                enemy.PoisonTriggers = more;
            }
        var had = powers.Has(power);
        var powerModel = SimModels.Get(power.Name);
        var after = powers.Add(power, amount);
        // More of one already there (PowerCmd.ModifyAmount; a power kept in copies changes a copy, never the whole): gone once
        // ShouldRemoveDueToAmount says so.
        var gone = had && powerModel is not ReadModel { HasCopies: true } && powerModel?.Instanced != true && (SimModels.AllowsNegative(power) ? after[power] == 0 : after[power] <= 0);
        var left = after[power];
        if (gone) after = after.Without(power);
        SetPowers(state, target, after);
        // Gone, it's PowerCmd.Remove's: its AfterRemoved, told as it goes (the game tells Hook.AfterPowerAmountChanged's listeners first; no read
        // power's AfterRemoved changes what one of them reads: TRACKER, the power removals' order).
        if (gone) TellRemoved(state, target, power, left);
        // A power whose hooks a game update made unreadable lands where nothing follows what it does: unknown (decision #92).
        if (powerModel == null && SimModels.UnreadReason(power.Name) is { } unread) state.Unmodelled ??= unread;
        // A read power whose code asks who applied it (base.Applier) keeps its applier as its Data, as the adapter reads it (decision #195).
        if (!had && powerModel is ReadModel { UsesApplier: true }) SetPowers(state, target, PowersOf(state, target).With(power, PowersOf(state, target)[power], DataCreature.Of(applier)));
        // A read power lands as the game's PowerCmd.Apply lands it: a new instance starts its own state afresh, a power kept in copies gets a copy
        // of its own or more of its applier's (decision #195), and a new instance (or copy) runs its AfterApplied.
        if (powerModel is ReadModel read)
        {
            var copy = read.Land(state, target, applier, amount, had);
            if (!had || copy > 0) read.AfterApplied(state, target, applier, amount, copy);
            return true;
        }
        // Each copy of an instanced power is applied on its own (a second Intercept on a covered teammate: a Covered of its own).
        if (!had || powerModel is { Instanced: true }) powerModel?.AfterApplied(state, target, applier, amount);
        return true;
    }

    /// <summary>Creature.CanReceivePowers: no listener's Hook.ShouldAllowHitting says no for the creature.</summary>
    internal static bool CanReceivePowers(SimState state, int creature)
    {
        foreach (var (model, owner) in Listeners(state))
            if (model.Overrides(nameof(SimModel.ShouldAllowHitting)) && !model.ShouldAllowHitting(state, owner, creature)) return false;
        return true;
    }

    /// <summary>
    /// An application's amount as it lands: its own, the applier's amount of the power (Cho Ki), the target's (Molten Fist), or the play's
    /// target's amount of another power (Dominate: its Vulnerable), or what the play's last attack dealt (Blight Strike).
    /// </summary>
    private static int AmountOf(SimState state, ApplyPowerAction apply, int applier, int target) =>
        apply.FromAttackDealt ? state.AttackDealt
        : apply.CopiesOwn ? PowerOn(state, applier, apply.Power)
        : apply.CopiesTarget ? PowerOn(state, target, apply.Power)
        : apply.TargetsAmountOf is { } other ? (state.CurrentPlay?.Target ?? Who.None) is var aimedAt && state.Enemy(aimedAt) is { Alive: true } aimedEnemy
            ? PowerOn(state, aimedEnemy.Id, other) : 0
        : apply.Amount;

    /// <summary>
    /// PowerCmd.Apply as a power's or relic's hook runs it: the power lands (ApplyPower), then the listeners hear it (AfterPowerApplied: the
    /// game's Hook.AfterPowerAmountChanged), as a card's application is heard (Shroud's block on Doom you apply, Sleight of Flesh's damage on a
    /// debuff you land).
    /// </summary>
    internal static IEnumerable<SimState> ApplyAndTell(SimState state, int applier, int target, PowerId power, int amount, bool debuff) =>
        ApplyPower(state, applier, target, power, amount, debuff, out var landed) ? Tell(state, power, landed, applier, target, debuff) : new[] { state };

    /// <summary>
    /// PowerCmd.Apply where hooks can't branch (a hook inside a hit or a death: Hand Drill's Vulnerable as it breaks block): the power lands now,
    /// and its listeners hear it once the hit or the action is over (DrainPending), as a card event raised there runs then (Raise): a listener's
    /// draw (Vicious) can't branch inside the hit.
    /// </summary>
    internal static void ApplyInHit(SimState state, int applier, int target, PowerId power, int amount, bool debuff)
    {
        if (ApplyPower(state, applier, target, power, amount, debuff, out var landed)) state.PendingPowers = state.PendingPowers.Add(new PendingPower(power, landed, applier, target, debuff));
    }

    /// <summary>A power that landed, told to every AfterPowerApplied listener (the game's Hook.AfterPowerAmountChanged).</summary>
    internal static IEnumerable<SimState> Tell(SimState state, PowerId power, int amount, int applier, int target, bool debuff) =>
        Hook(state, nameof(SimModel.AfterPowerApplied), (model, owner, branch) => model.AfterPowerApplied(branch, owner, power, amount, applier, target, debuff));

    /// <summary>The power on each creature it reaches, then AfterPowerApplied for each that landed (Artifact took none of it).</summary>
    private static void RunApplyPower(BranchSet branches, ApplyPowerAction applyPowerAction)
    {
        IEnumerable<SimState> Apply(SimState state, int applier, int target) =>
            ApplyPower(state, applier, target, applyPowerAction.Power, AmountOf(state, applyPowerAction, applier, target), applyPowerAction.Debuff, out var landed)
                ? Hook(state, nameof(SimModel.AfterPowerApplied), (model, owner, branch) => model.AfterPowerApplied(branch, owner, applyPowerAction.Power, landed, applier, target, applyPowerAction.Debuff)) : new[] { state };
        if (applyPowerAction.To == ApplyPowerAction.Where.RandomEnemy)
        {
            // Each application to a random hittable enemy: a branch per target, one step each (the branch budget holds).
            for (var i = 0; i < applyPowerAction.Times; i++) branches.Each(branch => RandomTarget(branch, (copy, id) => Apply(copy, branch.CurrentPlay?.Owner ?? Who.You, id)));
            return;
        }
        branches.Each(branch => ApplyPowerOf(branch, applyPowerAction));
    }

    /// <summary>
    /// A card play's application (an ApplyPowerAction that isn't on a random enemy) on one branch: the power on each creature it reaches, then
    /// AfterPowerApplied for each that landed. A read enchantment's PowerCmd.Apply runs it too (ReadModel: its card's application).
    /// </summary>
    internal static IEnumerable<SimState> ApplyPowerOf(SimState branch, ApplyPowerAction applyPowerAction)
    {
        IEnumerable<SimState> Apply(SimState state, int applier, int target) =>
            ApplyPower(state, applier, target, applyPowerAction.Power, AmountOf(state, applyPowerAction, applier, target), applyPowerAction.Debuff, out var landed)
                ? Hook(state, nameof(SimModel.AfterPowerApplied), (model, owner, copy) => model.AfterPowerApplied(copy, owner, applyPowerAction.Power, landed, applier, target, applyPowerAction.Debuff)) : new[] { state };
        var applier = branch.CurrentPlay?.Owner ?? Who.You;
        var targets = applyPowerAction.To switch
        {
            // The play's target: a living enemy, or a teammate (Ignition, Covered); aimed at no one, no one (never you: Who.None).
            ApplyPowerAction.Where.Target => branch.CurrentPlay?.Target is { } aimed && (Who.KindOf(aimed) == CreatureKind.Teammate || branch.Enemy(aimed) is { Alive: true }) ? new List<int> { aimed } : new List<int>(),
            ApplyPowerAction.Where.AllEnemies => branch.Targetable.Select(enemy => enemy.Id).ToList(),
            ApplyPowerAction.Where.You => new List<int> { applier },
            _ => new List<int> { applyPowerAction.Creature },
        };
        IEnumerable<SimState> states = new[] { branch };
        foreach (var target in targets)
        {
            var aimedAt = target;
            states = Then(states, copy => Apply(copy, applier, aimedAt));
        }
        return states;
    }

    /// <summary>Where a play keeps a debuff it took from its target (SimState.PlayTests: the power's name after it, its amount).</summary>
    private const string KeptDebuffKey = "kept debuff:";

    /// <summary>
    /// KeepTargetDebuffsAction: each power on the play's target whose amount is a debuff (SimModels.IsDebuffAt: Misery's TypeForCurrentAmount
    /// test), in the target's order, with its amount, kept for the play. None with no enemy targeted.
    /// </summary>
    private static void KeepTargetDebuffs(SimState state)
    {
        if (state.CurrentPlay?.Target is not { } target || !Who.IsEnemy(target) || state.Enemy(target) is null) return;
        foreach (var entry in PowersOf(state, target).Items)
            if (entry.Amount != 0 && SimModels.IsDebuffAt(entry.Name, entry.Amount))
                state.PlayTests = state.PlayTests.With(KeptDebuffKey + entry.Name.Name, entry.Amount);
    }

    /// <summary>
    /// SpreadKeptDebuffsAction: the debuffs the play kept onto every hittable enemy but its target (CombatState.HittableEnemies as the loop starts),
    /// enemy by enemy, each power in turn, applied by the play's owner as PowerCmd applies it (ApplyPower: the amount hooks, Artifact taking a
    /// debuff, AfterPowerApplied for one that landed).
    /// </summary>
    private static IEnumerable<SimState> SpreadKeptDebuffs(SimState branch)
    {
        var kept = branch.PlayTests.Items.Where(entry => entry.Name.StartsWith(KeptDebuffKey, StringComparison.Ordinal))
            .Select(entry => (Power: new PowerId(entry.Name[KeptDebuffKey.Length..]), entry.Amount)).ToList();
        if (kept.Count == 0) return new[] { branch };
        var applier = branch.CurrentPlay?.Owner ?? Who.You;
        var target = branch.CurrentPlay?.Target;
        IEnumerable<SimState> states = new[] { branch };
        foreach (var enemy in branch.Targetable.Select(enemy => enemy.Id).Where(id => id != target).ToList())
            foreach (var (power, amount) in kept)
                states = Then(states, copy => ApplyPower(copy, applier, enemy, power, amount, debuff: true, out var landed)
                    ? Hook(copy, nameof(SimModel.AfterPowerApplied), (model, owner, state) => model.AfterPowerApplied(state, owner, power, landed, applier, enemy, true))
                    : new[] { copy });
        return states;
    }

    /// <summary>A card play's card events on one branch (CardEvents), for a read enchantment's draws and energy (ReadModel: its card's).</summary>
    internal static IEnumerable<SimState> CardEventsOf(SimState state, CardEventAction cardEventAction) => CardEvents(state, cardEventAction);

    /// <summary>
    /// The HP a card's play costs its owner (a LoseHpAction's; a read enchantment's damage to its card's owner, Corrupted's): CreatureCmd.Damage of its
    /// owner, dealt by them (the command's dealer is its cardSource's owner) from <paramref name="card"/>, through the one path for damage to a player
    /// (DamagePlayer, decision #219), named by the card.
    /// </summary>
    internal static void LoseHp(SimState state, string name, int amount, int player, SimCard? card, SimProps props = HpCostProps) =>
        DamagePlayer(state, name, player, amount, props, dealer: player, card);

    /// <summary>Card events, one card at a time, each through every listener's AfterCardEvent (indirect ones marked in the log).</summary>
    private static void RunCardEvent(BranchSet branches, CardEventAction cardEventAction)
    {
        if (cardEventAction.FirstPlayOnly && branches.States.Count > 0 && branches.States[0].CurrentPlay is { PlayIndex: > 0 }) return;
        var made = cardEventAction.Event == CardEvent.Generated;
        if (cardEventAction.CountFrom != SimCount.Given || cardEventAction.JoinsHand || made && branches.States.Any(branch => branch.TakeMatching.Count > 0))
        {
            branches.Each(branch => CardEvents(branch, cardEventAction));
            return;
        }
        for (var i = 0; i < cardEventAction.Count; i++)
        {
            // A draw from your draw pile (decision #90): a branch per card that matters, with its reactions.
            if (cardEventAction.Event == CardEvent.Drawn && branches.States.Count > 0 && DrawsFromPile(branches.States[0], cardEventAction.Player ?? branches.States[0].CurrentPlay?.Owner ?? Who.You))
            {
                branches.Each(branch => DrawCards(branch, cardEventAction.Player ?? branch.CurrentPlay?.Owner ?? Who.You, count: 1, cardEventAction.Indirect));
                continue;
            }
            branches.Do(branch => { branch.EventCard = cardEventAction.Card; CountEvent(branch, cardEventAction.Event, cardEventAction.Player ?? branch.CurrentPlay?.Owner ?? Who.You); });
            HookStep(branches, nameof(SimModel.AfterCardEvent), (model, owner, branch) =>
            {
                branch.IndirectNow = cardEventAction.Indirect;
                var states = model.AfterCardEvent(branch, owner, cardEventAction.Event, cardEventAction.Player ?? branch.CurrentPlay?.Owner ?? Who.You).ToList();
                foreach (var state in states) state.IndirectNow = false;
                return states;
            });
            branches.Do(branch => branch.EventCard = null);
        }
    }

    /// <summary>
    /// A play's card events on one branch, as many as it reads there as they run (decision #189: CountOf), each with its listeners. Cards
    /// that join the hand (JoinsHand) take a slot as they come: made cards (into a full hand they go to the discard pile, still made),
    /// draws with no draw pile read (none past No Draw or a full hand).
    /// </summary>
    private static IEnumerable<SimState> CardEvents(SimState state, CardEventAction cardEventAction)
    {
        var player = cardEventAction.Player ?? state.CurrentPlay?.Owner ?? Who.You;
        var count = CountOf(state, cardEventAction.CountFrom, cardEventAction.Count);
        IEnumerable<SimState> states = new[] { state };
        for (var i = 0; i < count; i++) states = Then(states, branch => OneCardEvent(branch, cardEventAction, player));
        return states;
    }

    private static IEnumerable<SimState> OneCardEvent(SimState state, CardEventAction cardEventAction, int player)
    {
        var cardEvent = cardEventAction.Event;
        if (cardEvent == CardEvent.Drawn && DrawsFromPile(state, player)) return DrawCards(state, player, count: 1, cardEventAction.Indirect);
        // Whose hand it joins: the event's player's, or a made card's owner's when someone else made it (IntoHandOf).
        var handOf = cardEvent == CardEvent.Drawn ? player : cardEventAction.IntoHandOf ?? player;
        if (cardEventAction.JoinsHand && state.Player(handOf) is { } simPlayer)
        {
            if (cardEvent == CardEvent.Drawn)
            {
                if (DrawsStopped(state, player)) return DrawPrevented(state, player);
                if (RoomIn(simPlayer) == 0) return new[] { state };
                // Drawing the whole pile shuffles the discard pile in: I Am Invincible's top isn't known any more.
                var drawer = state.MutablePlayer(player);
                if (drawer.InvincibleBlock > 0 && --drawer.InvincibleDrawPile <= 0) drawer.InvincibleBlock = 0;
            }
            var made = cardEvent != CardEvent.Drawn;
            if (RoomIn(simPlayer) > 0)
            {
                AddToHand(state, handOf, count: 1, takenBy: made ? cardEventAction.TakenBy : null, known: made && cardEventAction.TakenBy != null);
                return FireCardEvent(state, cardEvent, player, cardEventAction.Indirect, cardEventAction.Card);
            }
            // Made into a full hand: it goes to the discard pile instead (CardPileCmd.Add), for a shuffle later in the turn.
            if (made && cardEventAction.MadeCard is { } madeName && simPlayer.Draw != null && simPlayer.MadeCards?.TryGetValue(madeName, out var madeProfile) == true)
                state.MutablePlayer(handOf).Draw = simPlayer.Draw.WithDiscarded(madeProfile);
        }
        // A made card outside the hand (or into a full one: the discard pile) still counts for a take of every pile.
        if (cardEvent == CardEvent.Generated)
            JoinsTakes(state, cardEventAction.TakenBy, known: cardEventAction.TakenBy != null, intoHand: false, made: true);
        return FireCardEvent(state, cardEvent, player, cardEventAction.Indirect, cardEventAction.Card);
    }

    /// <summary>A relic's or power's value (its DynamicVar) as the adapter read it (SimState.Vars), or the fallback.</summary>
    public static int Var(SimState state, int owner, string model, string var, int fallback) =>
        state.Vars.TryGetValue($"{owner}:{model}:{var}", out var value) ? value : fallback;

    /// <summary>A value as the game has it, a fraction included (SimState.Fractions), else the whole one (Var), else <paramref name="fallback"/>.</summary>
    public static decimal VarValue(SimState state, int owner, string model, string var, decimal fallback) => VarValue(state, $"{owner}:{model}:{var}", fallback);

    /// <summary>The same, by its key ("owner:Model:Var") made once by the caller.</summary>
    public static decimal VarValue(SimState state, string key, decimal fallback) =>
        state.Fractions.TryGetValue(key, out var fraction) ? fraction : state.Vars.TryGetValue(key, out var value) ? value : fallback;

    /// <summary>What the adapter adds to a value's key for its total over an instanced power's copies (SimReaders.AddVars).</summary>
    public const string TotalSuffix = ".Total";

    /// <summary>
    /// A value summed over every copy of an instanced power on its owner (PowerInstanceType.Instanced: each Monologue played is a
    /// copy with its own Strength, and each gives its own), as the adapter read it; one copy's (Var) when no total was read.
    /// </summary>
    public static int VarTotal(SimState state, int owner, string model, string var, int fallback) =>
        state.Vars.TryGetValue($"{owner}:{model}:{var}{TotalSuffix}", out var total) ? total : Var(state, owner, model, var, fallback);
}

public static partial class SimEngine
{
    /// <summary>An enemy's hits this turn: its intent's, or the final numbers.</summary>
    internal static int HitCount(SimEnemy enemy) => enemy.Intent?.Count ?? enemy.Hits.Count;

    /// <summary>
    /// One hit of an enemy's intent on a player, through the damage pipeline as it lands (a Move from the enemy: its Strength
    /// and Weak, the player's Vulnerable, Intangible...), with the captured terms of unmodelled sources on that player.
    /// </summary>
    internal static int IntentDamage(SimState state, SimEnemy enemy, int hit, int player)
    {
        var otherHit = enemy.Intent![hit];
        return CalcDamage(state, otherHit.Base, new DamageCtx(enemy.Id, player, SimProps.Move, Card: null, Play: null), otherHit.TermsOn(player));
    }
}
