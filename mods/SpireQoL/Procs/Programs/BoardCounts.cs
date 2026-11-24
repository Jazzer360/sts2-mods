using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Sim;
using Sts2Mods.Core.Game;

namespace SpireQoL.Procs.Programs;

/// <summary>
/// What a card's program gives on the board before its play (decision #194): its effects' counts read from its steps (CardEffects) and
/// worked out on the board the way the game would (ProgramEvaluator): its draws, the cards its takes take, its block gains, its hits, its
/// orbs, its star gains. The adapter's translators read a step's own count here where only the board gives it (a take's cards in your hand
/// now, Cleanse's draw pile), the card scan reads whether a card's effect is counted at all, and the Shuffle Tag a card's own draws before
/// its simulation is in. No card is named; what a play does as it runs is the simulator's (decision #189).
/// </summary>
public static class BoardCounts
{

    /// <summary>
    /// The card's owner; null for the game's template card (ModelDb, read by the coverage scan), whose Owner
    /// throws (CardModel.Owner asserts a mutable model).
    /// </summary>
    private static Player? OwnerOf(CardModel card) => card.IsCanonical ? null : card.Owner;

    /// <summary>
    /// The play shuffles your discard pile into your draw pile before its draws, read from its program (decision #182): on every play
    /// (CardEffects.ShufflesDiscardPileIn: Reboot), or when the draw pile is short (CardEffects.ShufflesWhenShort: Ki Chant).
    /// </summary>
    internal static bool ShufflesFirst(CardModel card)
    {
        if (Programs.CardEffects.ShufflesDiscardPileIn(card.GetType())) return true;
        if (!Programs.CardEffects.ShufflesWhenShort(card.GetType()) || OwnerOf(card) is not { } me) return false;
        // KiChant.OnPlay: an empty draw pile shuffles if it must; a short one shuffles when the discard pile has cards.
        var draw = Sts2State.DrawPileCount(me);
        return draw < Sts2State.Var(card, "Cards", fallback: 3) && Sts2State.DiscardPileCount(me) > 0;
    }

    /// <summary>
    /// The cards a play puts back on your draw pile from your hand: the most its pick from your hand takes (CardPileCmd.Add to the draw pile
    /// after CardSelectCmd.FromHand, its selector's most: Glimmer's and Photon Cut's PutBack, Thinking Ahead's one), read on the board.
    /// </summary>
    internal static int PutBackCount(CardModel card)
    {
        var program = CardPrograms.Of(card.GetType());
        foreach (var found in CardEffects.Find(program, command => command.Command is { DeclaringType: "CardPileCmd", Name: "Add" }))
            if (found.Pick is { Command.Name: "FromHand" } pick && found.Command.Values.Count > 1 && found.Command.Values[1] is ConstantValue { Value: (int)PileType.Draw })
                return ProgramEvaluator.Number(new CardEffects.CardsTaken(found.Command, found.Condition, CardEffects.TakeWay.Picked, Items: null, pick).PerPick, new ProgramBoard(card), program) is { } most
                    ? (int)most : 1;
        return 0;
    }

    // ------------------------------------------------------------------ draws

    /// <summary>
    /// How many cards one play of this card draws right now (0 if it doesn't draw), read from its program (decision #182): each draw
    /// for you its code makes (CardPileCmd.Draw to its owner, CardCmd.DiscardAndDraw), on the condition its code puts it on, evaluated
    /// against the board (ProgramEvaluator: Impatience's hand, Restlessness alone in your hand, FTL's plays this turn, Fetch's Osty,
    /// Mad Science's rider), its count from its own argument (Cards, Scrawl's room in your hand, Calculated Gamble's hand, a count the
    /// game calculates) and its loops' rounds. A draw whose condition asks the target (Ki Hop, Wrest) counts what the worst target it
    /// could be aimed at would draw. <paramref name="handCount"/> isn't read: the hand is the board's.
    /// </summary>
    public static int CardsDrawn(CardModel card, int handCount) => DrawsOnPlay(card, target: null, playsBefore: 0);

    /// <summary>
    /// Draws of one play of <paramref name="card"/> aimed at <paramref name="target"/>, after <paramref name="playsBefore"/> of its replay's
    /// plays: each draw its condition lets through, its count times its loops' rounds (DrawCount). A draw whose condition asks the target
    /// with none given (Ki Hop, Wrest) counts what it would draw at the worst target it could be aimed at; with no fight to aim at (the
    /// game's template card), its condition counts as holding.
    /// </summary>
    private static int DrawsOnPlay(CardModel card, Creature? target, int playsBefore)
    {
        var program = Programs.CardPrograms.Of(card.GetType());
        var total = 0;
        foreach (var draw in Programs.CardEffects.DrawsForYou(program))
        {
            if (target == null && Programs.ProgramEvaluator.AsksTarget(draw.Condition) && Targets(card) is { } targets)
            {
                total += targets.Count > 0 ? targets.Min(enemy => DrawOnPlay(card, draw, enemy, playsBefore)) : 0;
                continue;
            }
            total += DrawOnPlay(card, draw, target, playsBefore);
        }
        return total;
    }

    /// <summary>One draw of a play, on the board as that play starts: none when its condition fails there, else its count.</summary>
    private static int DrawOnPlay(CardModel card, Programs.CardEffects.DrawForYou draw, Creature? target, int playsBefore)
    {
        var program = Programs.CardPrograms.Of(card.GetType());
        return Programs.ProgramEvaluator.Holds(draw.Condition, new Programs.ProgramBoard(card, target, playsBefore), program) == false ? 0 : DrawCount(card, draw);
    }

    /// <summary>The enemies the card could be aimed at; null when it isn't in a fight (the game's template card).</summary>
    private static List<Creature>? Targets(CardModel card)
    {
        try { return card.CombatState?.HittableEnemies.Where(creature => creature.IsAlive).ToList(); }
        catch { return null; }
    }

    /// <summary>
    /// Draws of all <paramref name="plays"/> plays aimed at <paramref name="target"/>, when one of the card's draws asks its target; null
    /// for other cards. Each play's condition is evaluated after the plays before it: Ki Hop draws on each play that starts with the target
    /// Impaled, its own hit loosening the last arrow (SimEngine.PlaysStartingImpaled); Wrest pulls every arrow of yours out on its first
    /// play, so only that one draws. The audit takes it with the play's actual target as the play starts.
    /// </summary>
    public static int? TargetDraws(CardModel card, Creature target, int plays)
    {
        if (!Programs.CardEffects.DrawsForYou(card.GetType()).Any(draw => Programs.ProgramEvaluator.AsksTarget(draw.Condition))) return null;
        var total = 0;
        for (var playIndex = 0; playIndex < Math.Max(1, plays); playIndex++) total += DrawsOnPlay(card, target, playIndex);
        return total;
    }

    /// <summary>
    /// How many cards all <paramref name="plays"/> plays of this card draw right now, each play's conditions after the plays before it
    /// (FTL draws while fewer than PlayMax plays have finished this turn, a replay's earlier plays included; Fetch only on its first),
    /// a draw that asks the target at the worst target it could be aimed at.
    /// </summary>
    public static int CardsDrawnTotal(CardModel card, int handCount, int plays)
    {
        var total = 0;
        for (var playIndex = 0; playIndex < Math.Max(1, plays); playIndex++) total += DrawsOnPlay(card, target: null, playIndex);
        return total;
    }

    /// <summary>
    /// What one draw of the play (a DrawForYou) draws when it runs: its count its own argument on the board, times its loops' rounds. Its
    /// condition isn't asked here: the simulator asks it of each play as it runs (decision #190: a WhenAction).
    /// </summary>
    internal static int DrawCount(CardModel card, Programs.CardEffects.DrawForYou draw)
    {
        var program = Programs.CardPrograms.Of(card.GetType());
        var board = new Programs.ProgramBoard(card);
        var count = Programs.ProgramEvaluator.Number(draw.Count, board, program) is { } read ? (int)read
            : card.DynamicVars.ContainsKey("Cards") ? Sts2State.Var(card, "Cards") : 1;
        foreach (var loop in draw.Loops) count *= card.DynamicVars.ContainsKey(loop.Over) ? Sts2State.Var(card, loop.Over) : 1;
        return Math.Max(0, count);
    }

    /// <summary>How many of <paramref name="plays"/> plays in a row start with fewer than <paramref name="max"/> plays finished, <paramref name="finishedBefore"/> before the first.</summary>
    internal static int PlaysUnder(int finishedBefore, int max, int plays) => Math.Clamp(max - finishedBefore, 0, Math.Max(1, plays));

    // ------------------------------------------------------------------ cards taken: exhausted, discarded, transformed, moved into your hand

    /// <summary>
    /// Other cards the play exhausts, read from its program (decision #182: CardEffects.ExhaustsOfOthers), counted on the board
    /// (Taken): a pick takes one card a play (a pick's first: Brand, Burning Pact) or the selector's most (Purity), from your hand or your
    /// draw pile (Cleanse), of the cards its test on the picked card lets through (The Hermit: a Curse); a roll takes one card a play from
    /// what it rolls over (Thrash: your Attacks); a loop takes each card of what it goes over once (Fiend Fire your hand, Second Wind its
    /// non-Attacks, Growth its Sadness, Flak Cannon your statuses: a later play finds them gone). Only the exhausts whose condition holds
    /// (True Grit's pick when upgraded, its roll when not). A collection that can't be told on the board (the game's template card) is
    /// <paramref name="others"/>.
    /// </summary>
    internal static int ExhaustOthers(CardModel card, List<CardModel> others, int drawPile) =>
        Taken(card, Programs.CardEffects.ExhaustsOfOthers(card.GetType()), others, drawPile, draws: 0, fromHandOnly: false);

    /// <summary>Of those, the ones that leave your hand: not Cleanse's (your draw pile); of a collection from every pile, the ones in your hand.</summary>
    private static int ExhaustedFromHand(CardModel card, List<CardModel> others, int drawPile) =>
        Taken(card, Programs.CardEffects.ExhaustsOfOthers(card.GetType()), others, drawPile, draws: 0, fromHandOnly: true);

    /// <summary>
    /// Cards the play discards, read from its program (CardEffects.Discards: CardCmd.Discard and DiscardAndDraw), counted on the board
    /// (Taken): a pick from your hand, after the draws its code makes first (Acrobatics, Prepared; Survivor, Hidden Daggers before any),
    /// of the cards its test lets through (The Hermit: not a Curse); your whole hand (Storm of Steel, Calculated Gamble); the cards it drew
    /// that its filter keeps (Scrape: at most <paramref name="draws"/>); the cards it looked at on top of your draw pile, less the one taken
    /// (Ki Chant, each play). A pick's later plays aren't counted yet (TRACKER).
    /// </summary>
    internal static int DiscardCount(CardModel card, List<CardModel> others, int draws) =>
        Taken(card, Programs.CardEffects.Discards(card.GetType()), others, DrawPileOf(card), draws, fromHandOnly: false);

    /// <summary>Of those, the ones from your hand: not the cards it drew (Scrape's: the simulator's), nor the ones it looked at (Ki Chant's).</summary>
    internal static int DiscardedFromHand(CardModel card, List<CardModel> others, int draws) =>
        Taken(card, Programs.CardEffects.Discards(card.GetType()), others, DrawPileOf(card), draws, fromHandOnly: true);

    /// <summary>
    /// It discards from your hand without a pick (its whole hand: Storm of Steel, Calculated Gamble) while a Sly card is there
    /// (CardModel.IsSlyThisTurn): the Sly card plays itself (CardCmd.Discard). A discard you pick is a card, never a particular one (decision #188).
    /// </summary>
    internal static bool DiscardsSlyWithoutPick(CardModel card)
    {
        if (!Programs.CardEffects.Discards(card.GetType()).Any(discard => discard.Way == Programs.CardEffects.TakeWay.Each) || OwnerOf(card) is not { } me) return false;
        var others = Sts2State.Hand(me).Where(other => !ReferenceEquals(other, card)).ToList();
        return DiscardedFromHand(card, others, CardsDrawn(card, others.Count + 1)) > 0 && others.Any(other => other.IsSlyThisTurn);
    }

    /// <summary>
    /// Most cards a play transforms, read from its program (CardEffects.Transforms: CardCmd.Transform, TransformTo), counted on the board
    /// (Taken) among the cards CardCmd.Transform can take (IsTransformable): a pick from your hand (Begone one, Guards as many as you
    /// pick) or your draw pile (Charge, Seance), each of a collection (Compact your statuses, Primal Force your Attacks), the first of one
    /// (Punch of Joy's Sadness).
    /// </summary>
    internal static int TransformCount(CardModel card, List<CardModel> others, int drawPile) =>
        Taken(card, Programs.CardEffects.Transforms(card.GetType()), others, drawPile, draws: 0, fromHandOnly: false, Transformable);

    /// <summary>The play transforms only cards you choose how many of (Guards: 0 or more): none certainly.</summary>
    internal static bool TransformsChosen(CardModel card) =>
        Programs.CardEffects.Transforms(card.GetType()) is { Count: > 0 } transforms && transforms.All(transform => transform.ChoosesHowMany);

    private static bool Transformable(CardModel card)
    {
        try { return card.IsTransformable; } catch { return true; }
    }

    /// <summary>
    /// How many cards the play moves into your hand from your piles (CardEffects.TakesIntoHandOf: CardPileCmd.Add to the hand), counted
    /// on the board (Taken: a pick a play, a roll's count, each card of a collection), one play's (decision #189: the simulator applies your hand's room as
    /// each card comes, a full hand sending it to the discard pile: CardPileCmd.Add). None when you choose how many (Neow's Fury:
    /// HandTakeChosen). Which cards is a pick or a roll; how many is in the piles now.
    /// </summary>
    internal static int TakenToHandCount(CardModel card)
    {
        var takes = Programs.CardEffects.TakesIntoHandOf(card.GetType());
        if (takes.Count == 0 || takes.Any(take => take.ChoosesHowMany)) return 0;
        return Taken(card, takes, new List<CardModel>(), DrawPileOf(card), draws: 0, fromHandOnly: false);
    }

    /// <summary>
    /// The cards in your hand, other than the card played, a take from your hand lets through as the play starts (its collection's filter,
    /// or its test on the picked card: The Hermit's Curse), for the simulator to follow from there (TakeFromHandAction.Matching). A collection
    /// over more than your hand (Flak Cannon's statuses in every pile) counts every card of it.
    /// </summary>
    internal static int MatchingAtStart(CardModel card, Programs.CardEffects.CardsTaken take, bool everyPile)
    {
        var program = Programs.CardPrograms.Of(card.GetType());
        var board = new Programs.ProgramBoard(card);
        var others = OwnerOf(card) is { } me ? Sts2State.Hand(me).Where(other => !ReferenceEquals(other, card)).ToList() : new List<CardModel>();
        if (take.Way == Programs.CardEffects.TakeWay.Picked)
            return others.Count(other => Programs.ProgramEvaluator.HoldsFor(take.ItemTest, other, board, program) != false);
        return Collection(take, others, board, program, fromHandOnly: !everyPile, _ => true);
    }

    /// <summary>The take's collection holds cards outside your hand (Flak Cannon's, Florescence's: every pile), on the board.</summary>
    internal static bool TakesFromEveryPile(CardModel card, Programs.CardEffects.CardsTaken take)
    {
        if (take.Items == null || Programs.ProgramEvaluator.Items(take.Items, new Programs.ProgramBoard(card), Programs.CardPrograms.Of(card.GetType())) is not { } items) return false;
        return take.Items.Parts().Any(part => part is Programs.QueryValue { Name: "AllCards" }) || items.OfType<CardModel>().Any(item => item.Pile?.Type is not (null or PileType.Hand));
    }

    /// <summary>The cards one play transforms outside your hand (Charge, Seance: a pick from your draw pile), on the board: they stay in place.</summary>
    internal static int TransformsOutsideHand(CardModel card) =>
        Taken(card, Programs.CardEffects.Transforms(card.GetType()).Where(transform => transform.PickPile is not (null or PileType.Hand)).ToList(),
            new List<CardModel>(), DrawPileOf(card), draws: 0, fromHandOnly: false, Transformable);

    /// <summary>The play discards cards it looked at on top of your draw pile, less the one it took (Ki Chant), on the board: one play's.</summary>
    internal static int DiscardedOutsideHand(CardModel card) =>
        Taken(card, Programs.CardEffects.Discards(card.GetType()).Where(discard => discard.Way == Programs.CardEffects.TakeWay.LookedAt).ToList(),
            new List<CardModel>(), DrawPileOf(card), draws: 0, fromHandOnly: false);

    /// <summary>
    /// The play takes cards into your hand and you choose how many (a selector whose least and most differ: Neow's Fury), with cards to
    /// take and room in your hand (<paramref name="handCount"/> with the card played): your hand after it is your later decision.
    /// </summary>
    internal static bool HandTakeChosenOf(CardModel card, int handCount)
    {
        var chosen = Programs.CardEffects.TakesIntoHandOf(card.GetType()).Where(take => take.ChoosesHowMany).ToList();
        if (chosen.Count == 0 || handCount - 1 >= CardPile.MaxCardsInHand) return false;
        return Taken(card, chosen, new List<CardModel>(), DrawPileOf(card), draws: 0, fromHandOnly: false) > 0;
    }

    /// <summary>Cards in your draw pile; 0 for the game's template card.</summary>
    private static int DrawPileOf(CardModel card) => OwnerOf(card) is { } me ? Sts2State.DrawPileCount(me) : 0;

    /// <summary>
    /// How many cards the play's <paramref name="takes"/> take on the board, each whose condition holds:
    /// <list type="bullet">
    /// <item>a pick: the selector's most (its first card: one) of the cards in its pile that its filter and its test on the card let
    /// through, your hand with the draws its code makes before it, a draw pile with the discard pile its play shuffles in first;
    /// one play's (decision #189);</item>
    /// <item>a roll: its count (one, or TakeRandom's) a play, of its collection;</item>
    /// <item>each card of a collection once (a later play finds them gone), one for its first card;</item>
    /// <item>the cards the play drew (<paramref name="draws"/>), and the cards looked at on top of your draw pile less the ones picked, a play.</item>
    /// </list>
    /// <paramref name="fromHandOnly"/>: only the cards that leave your hand. <paramref name="eligible"/>: the cards the command itself can
    /// take. A collection that can't be told on the board (the game's template card) is <paramref name="others"/>.
    /// </summary>
    private static int Taken(CardModel card, IReadOnlyList<Programs.CardEffects.CardsTaken> takes, List<CardModel> others, int drawPile, int draws,
        bool fromHandOnly, Func<CardModel, bool>? eligible = null)
    {
        if (takes.Count == 0) return 0;
        var program = Programs.CardPrograms.Of(card.GetType());
        var board = new Programs.ProgramBoard(card);
        bool Eligible(object? item) => eligible == null || item is not CardModel cardItem || eligible(cardItem);
        int Number(Programs.ProgramValue? value, int fallback) => value != null && Programs.ProgramEvaluator.Number(value, board, program) is { } read ? (int)read : fallback;
        var total = 0;
        foreach (var take in takes)
        {
            if (Programs.ProgramEvaluator.Holds(take.PlayTest, board, program) == false) continue;
            switch (take.Way)
            {
                case Programs.CardEffects.TakeWay.Picked:
                {
                    var pile = take.PickPile ?? PileType.Hand;
                    if (fromHandOnly && pile != PileType.Hand) break;
                    var pool = pile == PileType.Hand
                        ? others.Count(other => Eligible(other) && Programs.ProgramEvaluator.HoldsFor(take.ItemTest, other, board, program) != false) + DrawsBefore(card, program, take, draws)
                        : PileCount(card, pile, take, board, program, Eligible, otherwise: pile == PileType.Draw ? drawPile : others.Count);
                    total += Math.Min(pool, Number(take.PerPick, fallback: 1));
                    break;
                }
                case Programs.CardEffects.TakeWay.Random:
                    total += Math.Min(Number(take.RollCount, fallback: 1), Collection(take, others, board, program, fromHandOnly, Eligible));
                    break;
                case Programs.CardEffects.TakeWay.Drawn:
                    if (!fromHandOnly) total += draws;
                    break;
                case Programs.CardEffects.TakeWay.LookedAt:
                    if (!fromHandOnly) total += Math.Max(0, LooksAt(card) - Number(take.PerPick, fallback: 1));
                    break;
                default:
                    var each = Collection(take, others, board, program, fromHandOnly, Eligible);
                    total += take.Single ? Math.Min(1, each) : each;
                    break;
            }
        }
        return total;
    }

    /// <summary>The cards a pick from your hand also has to pick from: the ones the play draws before it (Acrobatics), when it draws first.</summary>
    private static int DrawsBefore(CardModel card, Programs.CardProgram program, Programs.CardEffects.CardsTaken take, int draws)
    {
        if (draws <= 0 || take.Pick == null) return 0;
        foreach (var step in program.All())
        {
            if (ReferenceEquals(step, take.Pick)) return 0;
            if (step is Programs.CommandStep command && Programs.CardEffects.DrawsForYou(card.GetType()).Any(draw => ReferenceEquals(draw.Command, command))) return draws;
        }
        return 0;
    }

    /// <summary>
    /// The cards of yours in <paramref name="pile"/> a pick can take: its filter's and its test's, read on the board; a draw pile with the
    /// discard pile the play shuffles in first when it's short (Ki Chant). <paramref name="otherwise"/> when the pile can't be read.
    /// </summary>
    private static int PileCount(CardModel card, PileType pile, Programs.CardEffects.CardsTaken take, Programs.ProgramBoard board, Programs.CardProgram program,
        Func<object?, bool> eligible, int otherwise)
    {
        if (OwnerOf(card) is not { } me) return otherwise;
        var cards = PileCards(me, pile);
        if (pile == PileType.Draw && ShufflesFirst(card)) cards.AddRange(PileCards(me, PileType.Discard));
        return cards.Count(other => eligible(other)
                                    && (take.PickFilter is not { } filter || Programs.ProgramEvaluator.Passes(filter, other, board, program) != false)
                                    && Programs.ProgramEvaluator.HoldsFor(take.ItemTest, other, board, program) != false);
    }

    /// <summary>
    /// How many cards the take's collection holds on the board (only your hand's, when asked), those its test on each card lets through;
    /// <paramref name="others"/> when it can't be told.
    /// </summary>
    private static int Collection(Programs.CardEffects.CardsTaken take, List<CardModel> others, Programs.ProgramBoard board, Programs.CardProgram program, bool fromHandOnly,
        Func<object?, bool> eligible)
    {
        if (take.Items == null || Programs.ProgramEvaluator.Items(take.Items, board, program) is not { } items) return others.Count;
        return items.Count(item => !ReferenceEquals(item, board.Card) && (!fromHandOnly || item is CardModel { Pile.Type: PileType.Hand }) && eligible(item)
                                   && (take.ItemTest.IsAlways || Programs.ProgramEvaluator.HoldsFor(take.ItemTest, item, board, program) != false));
    }

    /// <summary>
    /// The top cards of your draw pile the play looks at (a LookedAt take's count: Take(drawPile, Cards), Ki Chant), as many as the pile
    /// holds after the shuffle its play makes first when it's short.
    /// </summary>
    private static int LooksAt(CardModel card)
    {
        var take = Programs.CardEffects.Discards(card.GetType()).FirstOrDefault(discard => discard.Way == Programs.CardEffects.TakeWay.LookedAt);
        var count = take?.Looks is { } looks && Programs.ProgramEvaluator.Number(looks, new Programs.ProgramBoard(card), Programs.CardPrograms.Of(card.GetType())) is { } read
            ? (int)read : Sts2State.Var(card, "Cards", fallback: 3);
        if (OwnerOf(card) is not { } me) return count; // the game's template card (the scan)
        var available = Sts2State.DrawPileCount(me) + (ShufflesFirst(card) ? Sts2State.DiscardPileCount(me) : 0);
        return Math.Min(count, available);
    }
    /// <summary>
    /// The card exhausts others with a filter that isn't a type (ExhaustPickOf: none followed) and one of <paramref name="candidates"/>
    /// that acts when exhausted (CardEnergy.WhenExhausted: Drum of Battle) passes it, or might: the card scan's check that such a filter
    /// takes no card whose exhaust does something.
    /// </summary>
    internal static bool ExhaustFilterTakesActingCard(CardModel card, IEnumerable<CardModel> candidates)
    {
        var program = Programs.CardPrograms.Of(card.GetType());
        var board = new Programs.ProgramBoard(card);
        var acting = candidates.Where(candidate => SpireQoL.Sim.CardEnergy.WhenExhausted(candidate) > 0).ToList();
        foreach (var exhaust in Programs.CardEffects.ExhaustsOfOthers(card.GetType()))
        {
            var filter = exhaust.Way == Programs.CardEffects.TakeWay.Picked ? exhaust.ItemTest : exhaust.Filter?.Body ?? Programs.ProgramCondition.Always;
            if (filter.IsAlways || TypeTest(filter) != null) continue;
            if (exhaust.Way == Programs.CardEffects.TakeWay.Picked
                    ? acting.Any(candidate => Programs.ProgramEvaluator.HoldsFor(filter, candidate, board, program) != false)
                    : acting.Any(candidate => Programs.ProgramEvaluator.Passes(exhaust.Filter!, candidate, board, program) != false))
                return true;
        }
        return false;
    }

    /// <summary>A filter that's one test of a card's type (c.Type == Attack, each.Type != Curse): the type, and whether it's kept or left.</summary>
    internal static (SimCardType Type, bool Kept)? TypeTest(Programs.ProgramCondition filter)
    {
        if (filter.Terms is not [[{ Test: Programs.CompareValue { Op: Programs.Comparison.Equal or Programs.Comparison.NotEqual, Left: Programs.QueryValue { Name: "Type" }, Right: Programs.ConstantValue constant } compare } literal]])
            return null;
        if (!Enum.TryParse<SimCardType>(((CardType)constant.Value).ToString(), out var type) || type == SimCardType.None) return null;
        return (type, (compare.Op == Programs.Comparison.Equal) == literal.Holds);
    }

    private static List<CardModel> PileCards(Player me, PileType pile)
    {
        try { return pile.GetPile(me).Cards.ToList(); } catch { return new List<CardModel>(); }
    }

    /// <summary>
    /// Cards of yours, other than this one, its play takes out of your hand: what it discards from there (not the cards it looked at on
    /// your draw pile, Ki Chant's; nor the ones it drew, Scrape's, the simulator's) and what it exhausts from there (not Cleanse's, from
    /// the draw pile; of Florescence's and Flak Cannon's, from every pile, the ones in your hand).
    /// </summary>
    internal static int LeavingHand(CardModel card, List<CardModel> others, int drawPile, int draws)
    {
        var exhausts = ExhaustedFromHand(card, others, drawPile);
        var discards = DiscardedFromHand(card, others, draws);
        // Put back on your draw pile after the draws, from the cards in hand then (you choose which).
        var putBack = Programs.CardEffects.PutsBackFromHand(card.GetType())
            ? Math.Min(PutBackCount(card), others.Count + draws) : 0;
        return exhausts + discards + putBack;
    }

    // ------------------------------------------------------------------ block

    /// <summary>
    /// Separate times one play gains you block, read from its program (decision #182: each CreatureCmd.GainBlock, CardEffects.Find), on
    /// the board: each gain on its condition (Mad Science's rider), times its loops' rounds (Second Wind your non-Attacks, Cho Limbs 2,
    /// Evil Eye twice after an exhaust this turn, Death's Door one more Repeat after Doom, Cho Firm twice with an arrow to shoot); a loop
    /// over every player (Rally) gains yours once. None when the game says the card gains no block now (CardModel.GainsBlock: Sovereign
    /// Blade without Parry, Mad Science not a Skill, a Power whose Block is for later); one when it does and its program shows no gain.
    /// The game's template card (no owner: the coverage scan) counts every gain its code makes.
    /// </summary>
    internal static int BlockGainCount(CardModel card)
    {
        if (OwnerOf(card) != null && !card.GainsBlock) return 0;
        var type = card.GetType();
        var program = Programs.CardPrograms.Of(type);
        var board = new Programs.ProgramBoard(card);
        var gains = Programs.CardEffects.BlockGains(type);
        if (gains.Count == 0) return OwnerOf(card) != null ? 1 : 0;
        var count = 0;
        foreach (var gain in gains)
        {
            if (Programs.ProgramEvaluator.Holds(gain.Condition, board, program) == false) continue;
            // The loop over the players it gains each one's block in: yours once.
            count += gain.Loops.Aggregate(1, (times, loop) => times * (Programs.CardEffects.GainsForEachOfLoop(gain.Command) ? 1 : Programs.ProgramEvaluator.Rounds(loop, board, program)));
        }
        return count;
    }

    /// <summary>
    /// Hits per play of an attack card, read from its program (decision #182): its attack (the play's first DamageCmd.Attack) on its
    /// condition (Pact's End's exhausts, Nail's Impaled target, Fetch's and Flatten's Osty, Mad Science's type), its hit count's value
    /// (WithHitCount's argument: a card value, a constant on main, a card's X, Heavenly Drill's doubled X, Dismantle's Vulnerable target,
    /// Spite's HP lost this turn, Wah Blast's melodies, Fiend Fire's hand, a calculated count; one with none) times its loops' rounds,
    /// then the game's hit-count modifiers. With no target (null) a test of the target holds when it does for some enemy you could aim
    /// at, and a count that asks the target is its least.
    /// </summary>
    internal static int HitsPerPlay(CardModel card, ICombatState combat, Creature? target)
    {
        var hits = BaseHitsPerPlay(card, target);
        // Then the game's hit-count modifiers (Thumbscrew: +1 on your multi-hit attacks), the way its card
        // preview applies them (HitCountVar.GetPreviewValue; the attack itself in AttackCommand.Execute).
        if (hits <= 0 || card.Type != CardType.Attack) return hits;
        try
        {
            return Math.Max(0, (int)MegaCrit.Sts2.Core.Hooks.Hook.ModifyAttackHitCount(combat,
                MegaCrit.Sts2.Core.Commands.DamageCmd.Attack(0m).FromCard(card, null), hits));
        }
        catch
        {
            return hits;
        }
    }

    private static int BaseHitsPerPlay(CardModel card, Creature? target)
    {
        // A Shoot card with no arrow to shoot does nothing at all (ArrowCmd.ShootArrowAtTarget returns before its attack; the card
        // glows red).
        if (Fool.IsShot(card) && OwnerOf(card) is { } owner && !Fool.Shoots(card, owner)) return 0;
        var program = Programs.CardPrograms.Of(card.GetType());
        if (Programs.CardEffects.Attacks(card.GetType()) is not [var attack, ..]) return Math.Max(1, Sts2State.Var(card, "Repeat", fallback: 1));
        var board = new Programs.ProgramBoard(card, target);
        if (target == null && Programs.ProgramEvaluator.AsksTarget(attack.Condition))
        {
            if (Targets(card) is { } enemies && !enemies.Any(enemy => Programs.ProgramEvaluator.Holds(attack.Condition, board with { Target = enemy }, program) != false)) return 0;
        }
        else if (Programs.ProgramEvaluator.Holds(attack.Condition, board, program) == false) return 0;
        var hits = attack.Command.HitCountValue is not { } count ? 1
            : Programs.ProgramEvaluator.Number(count, board, program) is { } read ? (int)read
            : Least(count, board, program) ?? Math.Max(1, Sts2State.Var(card, "HitCount", Sts2State.Var(card, "Repeat", fallback: 1)));
        return Math.Max(0, hits * attack.Loops.Aggregate(1, (times, loop) => times * Programs.ProgramEvaluator.Rounds(loop, board, program)));
    }

    /// <summary>The least a value two ways leave can be (Dismantle with no target: one hit), when each way's value can be told.</summary>
    private static int? Least(Programs.ProgramValue value, Programs.ProgramBoard board, Programs.CardProgram program)
    {
        while (value is Programs.StoredValue stored) value = stored.Value;
        if (value is not Programs.WaysValue ways) return null;
        var each = ways.Ways.Select(way => Programs.ProgramEvaluator.Number(way.Value, board, program)).ToList();
        return each.All(number => number != null) && each.Count > 0 ? (int)each.Min()!.Value : null;
    }
    /// <summary>An attack card aimed at enemies (Osty's attacks use OstyDamage or CalculatedDamage).</summary>
    internal static bool IsAttackOnEnemies(CardModel card)
        => card.Type == CardType.Attack && card.TargetType is TargetType.AnyEnemy or TargetType.AllEnemies or TargetType.RandomEnemy;

    internal static bool IsPlayerAttack(CardModel card)
        => card.Type == CardType.Attack
           && (card.DynamicVars.ContainsKey("Damage") || card.DynamicVars.ContainsKey("CalculatedDamage"))
           && card.TargetType is TargetType.AnyEnemy or TargetType.AllEnemies or TargetType.RandomEnemy;

    // ------------------------------------------------------------------ orbs, stars, Osty

    /// <summary>Orbs one play channels (Metronome), read from its program (OrbsOf).</summary>
    internal static int ChannelCount(CardModel card) => OrbsOf(card).Channels.Sum(channel => channel.Times);

    /// <summary>
    /// What one play does to your orbs, read from its program (decision #182: CardEffects.OrbChannels, OrbEvokes, OrbSlotChanges) and
    /// worked out on the board: the slots it adds or removes (their count argument: Capacitor's Repeat, Bulk Up's OrbSlots), how many
    /// times it evokes your front orb (each OrbCmd.EvokeNext times its loops' rounds: Dualcast twice, Quadcast its Repeat, Multicast its
    /// X, one more upgraded) or every orb (a loop over your orbs: Shatter twice each), and each channel, in order, with its orb (its
    /// class, or random: Chaos) and how many times (its loops' rounds: Glacier 2, Chill your enemies, Tempest its X, Voltaic its count),
    /// on its condition (Spinner+ only). An evoke with no orb to evoke does nothing (as OrbCmd.EvokeNext), so its test isn't asked.
    /// </summary>
    internal static OrbPlan OrbsOf(CardModel card)
    {
        var type = card.GetType();
        var program = Programs.CardPrograms.Of(type);
        var board = new Programs.ProgramBoard(card);
        int Number(Programs.ProgramValue value, int fallback) => Programs.ProgramEvaluator.Number(value, board, program) is { } read ? (int)read : fallback;
        int Times(Programs.CardEffects.Found found) => found.Loops.Aggregate(1, (times, loop) => times * Programs.ProgramEvaluator.Rounds(loop, board, program));
        var slots = 0;
        foreach (var change in Programs.CardEffects.OrbSlotChanges(type))
            if (Programs.ProgramEvaluator.Holds(change.Condition, board, program) != false && change.Command.Values.Count > 1)
                slots += (change.Command.Command.Name == "RemoveSlots" ? -1 : 1) * Number(change.Command.Values[1], fallback: 1) * Times(change);
        var eachOrb = Programs.CardEffects.EvokesPerOrbOfAll(type);
        var front = eachOrb > 0 ? 0 : Programs.CardEffects.OrbEvokes(type).Sum(Times);
        var channels = new List<(SpireQoL.Sim.OrbKind Kind, int Times)>();
        foreach (var channel in Programs.CardEffects.OrbChannels(type))
        {
            if (Programs.CardEffects.OrbOf(channel.Command) is not { } kind || Programs.ProgramEvaluator.Holds(channel.Condition, board, program) == false) continue;
            var times = Times(channel);
            if (times > 0) channels.Add((kind, times));
        }
        return new OrbPlan(slots, front, eachOrb, channels);
    }

    /// <summary>One play's orbs (OrbsOf).</summary>
    internal sealed record OrbPlan(int Slots, int EvokesFront, int EvokesEachOrb, IReadOnlyList<(SpireQoL.Sim.OrbKind Kind, int Times)> Channels)
    {
        /// <summary>The kind every channel makes, when it's one kind (null: mixed, random or none).</summary>
        public SpireQoL.Sim.OrbKind? ChannelKind => Channels.Select(channel => channel.Kind).Distinct().ToList() is [var only] && only != SpireQoL.Sim.OrbKind.Random ? only : null;
    }

    /// <summary>
    /// The star gains of one play of the card: each PlayerCmd.GainStars in its play, on every way through it, to its owner, read from
    /// its own code (CardEnergy.CallsOf). A gain on a condition is guarded by it in the simulation instead (decision #190: Knockout Blow's kill).
    /// </summary>
    internal static int StarGainsOf(CardModel card) =>
        SpireQoL.Sim.CardEnergy.CallsOf(card.GetType(), "GainStars").Count(call => call is { Method: "OnPlay", Certain: true, ToOwner: true });

    /// <summary>
    /// A card that makes Osty attack: the game's own tag on it (CardTag.OstyAttack, on every card whose OnPlay attacks FromOsty). A list
    /// of names missed Fetch, so its hit, Bone Flute's block and the enemy HP after it weren't predicted (a dev-mode report, 1.21.2).
    /// </summary>
    internal static bool IsOstyAttack(CardModel card)
    {
        try { return card.Tags.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardTag.OstyAttack); }
        catch { return false; }
    }

    // ------------------------------------------------------------------ hits, auto-plays and takes into your hand, as the step translators read them

    /// <summary>
    /// The value is a card value calculated as the count of a take's collection and nothing more (Flak Cannon's CalculatedHits: its
    /// multiplier counts the statuses its loop exhausts, its base 0 and extra 1): what that take took, read as it runs (SimCount.CardsTaken).
    /// </summary>
    internal static bool CountsTakenByItsValue(CardModel card, ProgramValue? value, CardEffects.CardsTaken take)
    {
        if (value == null || take.Items == null || value.Parts().OfType<CardValue>().FirstOrDefault() is not { } cardValue) return false;
        if (Sts2State.Var(card, "CalculationBase", fallback: -1) != 0 || Sts2State.Var(card, "CalculationExtra", fallback: -1) != 1) return false;
        var multipliers = CardProgramReader.CalculatedMultipliers(card.GetType(), RuntimeCode.Read);
        if (!multipliers.TryGetValue(cardValue.Key, out var multiplier) || multiplier is not QueryValue { Name: "Count", Arguments: [var counted] }) return false;
        // The multiplier's lambda names the card "card" where its play names it "this".
        static string Collection(ProgramValue items) => (items is QueryValue { Name: "ToList" or "ToArray", Arguments: [var inner] } ? inner : items).ToString()!;
        return Collection(counted).Replace("card.", "this.", StringComparison.Ordinal) == Collection(take.Items is StoredValue stored ? stored.Value : take.Items);
    }
    /// <summary>The attack's hits on the board (WithHitCount's argument at the target; with none aimed at, its least): one with no hit count.</summary>
    internal static int HitsOf(CommandStep command, ProgramBoard board, CardProgram program)
    {
        if (command.HitCountValue is not { } count) return 1;
        if (ProgramEvaluator.Number(count, board, program) is { } read) return Math.Max(0, (int)read);
        var value = count;
        while (value is StoredValue stored) value = stored.Value;
        if (value is WaysValue ways && ways.Ways.Select(way => ProgramEvaluator.Number(way.Value, board, program)).ToList() is var each && each.Count > 0 && each.All(number => number != null))
            return (int)each.Min()!.Value;
        return Math.Max(1, Sts2State.Var(board.Card, "HitCount", Sts2State.Var(board.Card, "Repeat", fallback: 1)));
    }

    /// <summary>An auto-play step's count on the board, its exhaust argument and whether its callback upgrades the cards (on its condition).</summary>
    internal static (int Count, bool ForceExhaust, bool Upgrade) AutoPlayOf(CommandStep command, CardModel card)
    {
        var program = CardPrograms.Of(card.GetType());
        var board = new ProgramBoard(card);
        var count = command.Values.Count > 2 && ProgramEvaluator.Number(command.Values[2], board, program) is { } read ? (int)read : 1;
        var exhausts = (command.Values.Count > 4 && command.Values[4] is ConstantValue { Value: not 0 });
        var upgrades = command.Inner.OfType<CommandStep>().Any(inner => inner.Command is { DeclaringType: "CardCmd", Name: "Upgrade" }
                                                                         && ProgramEvaluator.Holds(inner.Condition ?? ProgramCondition.Always, board, program) != false);
        return (Math.Max(0, count), exhausts, upgrades);
    }

    /// <summary>The first auto-play of the card's play (AutoPlayOf); null for a card that auto-plays none.</summary>
    internal static (int Count, bool ForceExhaust, bool Upgrade)? AutoPlaysOf(CardModel card) =>
        CardPrograms.Of(card.GetType()).Commands.FirstOrDefault(command => command.Command is { DeclaringType: "CardPileCmd", Name: "AutoPlayFromDrawPile" }) is { } autoPlay
            ? AutoPlayOf(autoPlay, card) : null;

    /// <summary>How many cards one play takes into your hand (its take's pick, roll or collection), and whether the simulator reads it as it runs.</summary>
    internal static (int Count, SimCount From) HandTakeCount(CardModel card)
    {
        var take = CardEffects.TakesIntoHandOf(card.GetType())[0];
        var program = CardPrograms.Of(card.GetType());
        var board = new ProgramBoard(card);
        int Number(ProgramValue value) => ProgramEvaluator.Number(value, board, program) is { } read ? Math.Max(0, (int)read) : 1;
        switch (take.Way)
        {
            case CardEffects.TakeWay.Picked:
                return (Number(take.PerPick), SimCount.Given);
            case CardEffects.TakeWay.Random when take.RollCount is { } roll:
                if (!CardEffects.ReadsHandRoom(roll)) return (Number(roll), SimCount.Given);
                return (CardEffects.WithoutRoom(roll) is { } capped ? Number(capped) : int.MaxValue, SimCount.HandRoom);
            case CardEffects.TakeWay.Random:
                return (1, SimCount.Given);
            default:
                return (int.MaxValue, SimCount.Given);
        }
    }
}
