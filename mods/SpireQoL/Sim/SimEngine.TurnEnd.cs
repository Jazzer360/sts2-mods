namespace SpireQoL.Sim;

/// <summary>
/// Cards that act in their owner's hand when the turn ends (CardModel.OnTurnEndInHand, run by CombatManager's DoTurnEnd for each
/// card in hand, in hand order, decompile v0.113.1). Each hits its owner through the damage pipeline with no dealer.
/// </summary>
public static partial class SimEngine
{
    /// <summary>How a card in hand hurts its owner at the end of the turn.</summary>
    public enum InHand
    {
        /// <summary>Its Damage (Unpowered, Move): blocked.</summary>
        Damage,
        /// <summary>Its HpLoss (Unblockable, Unpowered, Move).</summary>
        HpLoss,
        /// <summary>The cards in its owner's hand (Unblockable, Unpowered, Move: Regret counts them at BeforeSideTurnEnd).</summary>
        HandSize,
    }

    /// <summary>Every card that acts in hand at the end of the turn, by class name: the adapter reads exactly these.</summary>
    public static readonly IReadOnlyDictionary<string, InHand> HandAtTurnEnd = new Dictionary<string, InHand>
    {
        [CardId.Burn.Name] = InHand.Damage, [CardId.Decay.Name] = InHand.Damage, [CardId.Infection.Name] = InHand.Damage, [CardId.Toxic.Name] = InHand.Damage, [CardId.Wither.Name] = InHand.Damage,
        [CardId.BadLuck.Name] = InHand.HpLoss, [CardId.Beckon.Name] = InHand.HpLoss,
        [CardId.Regret.Name] = InHand.HandSize,
    };

    /// <summary>A player's cards in hand act, in hand order (DoTurnEnd: OnTurnEndInHand).</summary>
    private static void CardsActInHand(SimState state, int player)
    {
        if (!state.TakesPart(player) || state.Player(player) is not { TurnEndCards.Count: > 0 } simPlayer) return;
        var handSize = Math.Max(0, simPlayer.HandCount);
        foreach (var card in simPlayer.TurnEndCards)
        {
            if (!HandAtTurnEnd.TryGetValue(card.Card, out var kind)) continue;
            var props = kind == InHand.Damage ? SimProps.Unpowered | SimProps.Move : SimProps.Unblockable | SimProps.Unpowered | SimProps.Move;
            var baseAmount = kind == InHand.HandSize ? handSize : card.Amount;
            var amount = CalcDamage(state, baseAmount, new DamageCtx(Dealer: null, player, props, Card: null, Play: null));
            if (kind == InHand.Damage) LandBlockable(state, card.Title, amount, player, dealer: null, props);
            else LandUnblockable(state, card.Title, amount, player, dealer: null, props);
        }
    }
}
