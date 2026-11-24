using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using Sts2Mods.Core.Game;

namespace SpireQoL.Procs;

/// <summary>
/// Cards that come back to your hand when you play something (decompile v0.113.1). Each moves itself with
/// CardPileCmd.Add(this, Hand), which sends it to your discard pile instead when your hand is full (10).
///  - Make It So (MakeItSo.AfterCardPlayedLate): when a Skill of yours finishes and your Skills played this turn
///    (CardPlaysFinished: each play of a replay, auto-plays too) are a multiple of its Cards value (3), from any pile but
///    your hand, the exhaust pile included.
///  - Right Hand Hand (RightHandHand.AfterCardPlayedLate): any card of yours played whose energy value (its cost, even when
///    it's free or auto-played: CardPlay.Resources.EnergyValue) is at least its Energy value (2), from your discard pile.
///  - Whistling Arrows (WhistlingArrows.AfterArrowShot): you shoot an arrow (an arrow, not an empty shot), from your
///    discard pile.
/// Bolas, Bounce and Thrumming Hatchet come back as your next turn starts after you play them (their own text says so):
/// nothing to show. CardCoverage flags any other card whose hooks move it to your hand.
/// </summary>
internal static class ReturnToHand
{
    public const string MakeItSo = nameof(MakeItSo);
    public const string RightHandHand = nameof(RightHandHand);
    public const string WhistlingArrows = nameof(MegaCrit.Sts2.Core.Models.Cards.WhistlingArrows);

    /// <summary>The cards this class models (CardCoverage's scan accepts them).</summary>
    public static readonly HashSet<string> Modelled = new() { MakeItSo, RightHandHand, WhistlingArrows };

    /// <summary>Cards that come back next turn by their own text (CardCoverage's scan accepts them too).</summary>
    public static readonly HashSet<string> NextTurn = new() { nameof(Bolas), nameof(Bounce), nameof(ThrummingHatchet) };

    private static readonly PileType[] FightPiles = { PileType.Hand, PileType.Draw, PileType.Discard, PileType.Exhaust };

    /// <summary>Every copy of a card in your fight's piles (an Attack Potion's copy included), with its pile.</summary>
    public static IEnumerable<(CardModel Card, PileType Pile)> Copies(Player me, string name)
    {
        foreach (var pile in FightPiles)
        {
            IEnumerable<CardModel> cards;
            try { cards = pile.GetPile(me).Cards.ToList(); } catch { continue; }
            foreach (var card in cards)
                if (card.GetType().Name == name) yield return (card, pile);
        }
    }

    /// <summary>Make It So's count: every this many Skills (its Cards value).</summary>
    public static int Every(CardModel makeItSo) => Math.Max(1, Sts2State.Var(makeItSo, "Cards", fallback: 3));

    /// <summary>Your Skills played this turn (each play of a replay counts, as the game counts them).</summary>
    public static int SkillsThisTurn(Player me) => Sts2State.CountPlayedThisTurn(me, CardType.Skill);

    /// <summary>
    /// The cards that come back to your hand when <paramref name="card"/> is played now, its hand after the play read from its program on the
    /// board: your hand without it, the cards its makes put there (MadeCards) and its own draws and its enchantment's (Swift).
    /// </summary>
    public static List<CardModel> Back(CardModel card, Player me, int plays)
    {
        var hand = Sts2State.Hand(me).Count;
        var made = Programs.MadeCards.Summary(card).IntoYourHand;
        var draws = Programs.BoardCounts.CardsDrawn(card, hand) + SpireQoL.Sim.SimAdapter.SwiftDraws(card);
        return Returning(card, me, plays, Math.Min(MegaCrit.Sts2.Core.Entities.Cards.CardPile.MaxCardsInHand, hand - 1 + made) + draws, Fool.Shoots(card, me) ? 1 : 0);
    }

    /// <summary>
    /// The cards that come back to your hand when <paramref name="card"/> is played now (not counting itself), for the
    /// tabs and your hand size after. <paramref name="handAfter"/>: cards in your hand once it resolves, before these come
    /// back; a full hand sends them to your discard pile instead. <paramref name="shots"/>: arrows it shoots.
    /// </summary>
    public static List<CardModel> Returning(CardModel card, Player me, int plays, int handAfter, int shots)
    {
        var back = new List<CardModel>();
        var hand = handAfter;
        void Add(CardModel otherCard)
        {
            if (hand >= 10) return; // CardPileCmd.Add: a full hand sends it to the discard pile
            back.Add(otherCard);
            hand++;
        }
        plays = Math.Max(1, plays);
        // Whistling Arrows: shooting comes during the play, before the card's own AfterCardPlayed hooks.
        if (shots > 0)
            foreach (var (nextCard, pile) in Copies(me, WhistlingArrows))
                if (pile == PileType.Discard && !ReferenceEquals(nextCard, card)) Add(nextCard);
        if (card.Type == CardType.Skill)
        {
            var skills = SkillsThisTurn(me);
            foreach (var (otherCard, pile) in Copies(me, MakeItSo))
            {
                if (pile == PileType.Hand || ReferenceEquals(otherCard, card)) continue;
                var every = Every(otherCard);
                if (Enumerable.Range(1, plays).Any(nextPlay => (skills + nextPlay) % every == 0)) Add(otherCard);
            }
        }
        int energy;
        // The play's energy value: what it spends (PlayCardAction: SpendResources; X costs spend all your energy), or its
        // cost when auto-played (CardCmd.AutoPlay: GetAmountToSpend). Both are GetAmountToSpend.
        try { energy = card.EnergyCost.GetAmountToSpend(); } catch { energy = 0; }
        foreach (var (c, pile) in Copies(me, RightHandHand))
        {
            if (pile != PileType.Discard || ReferenceEquals(c, card)) continue;
            if (energy >= Sts2State.Var(c, "Energy", fallback: 2)) Add(c);
        }
        return back;
    }
}
