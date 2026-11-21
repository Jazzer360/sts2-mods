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
        ["Burn"] = InHand.Damage, ["Decay"] = InHand.Damage, ["Infection"] = InHand.Damage, ["Toxic"] = InHand.Damage, ["Wither"] = InHand.Damage,
        ["BadLuck"] = InHand.HpLoss, ["Beckon"] = InHand.HpLoss,
        ["Regret"] = InHand.HandSize,
    };

    /// <summary>A player's cards in hand act, in hand order (DoTurnEnd: OnTurnEndInHand).</summary>
    private static void CardsActInHand(SimState s, int player)
    {
        if (!s.TakesPart(player) || s.Player(player) is not { TurnEndCards.Count: > 0 } p) return;
        var handSize = Math.Max(0, p.HandCount);
        foreach (var card in p.TurnEndCards)
        {
            if (!HandAtTurnEnd.TryGetValue(card.Card, out var kind)) continue;
            var props = kind == InHand.Damage ? SimProps.Unpowered | SimProps.Move : SimProps.Unblockable | SimProps.Unpowered | SimProps.Move;
            var baseAmount = kind == InHand.HandSize ? handSize : card.Amount;
            var amount = CalcDamage(s, baseAmount, new DamageCtx(null, player, props, null, null));
            if (kind == InHand.Damage) LandBlockable(s, card.Title, amount, player, null, props);
            else LandUnblockable(s, card.Title, amount, player, null, props);
        }
    }
}
