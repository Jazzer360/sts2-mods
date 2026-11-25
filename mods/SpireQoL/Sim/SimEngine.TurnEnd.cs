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
        /// <summary>
        /// The cards in its owner's hand as their side's turn ends (Unblockable, Unpowered, Move: Regret counts them at
        /// BeforeSideTurnEnd, its model RegretCard, into its Amount).
        /// </summary>
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
        foreach (var card in simPlayer.TurnEndCards)
        {
            if (!HandAtTurnEnd.TryGetValue(card.Card, out var kind)) continue;
            var props = kind == InHand.Damage ? SimProps.Unpowered | SimProps.Move : SimProps.Unblockable | SimProps.Unpowered | SimProps.Move;
            // CreatureCmd.Damage of its owner (Burn's, Regret's): the one path for damage to a player (decision #219). Its dealer is the card's
            // owner (the command's cardSource.Owner.Creature), and its card the card itself (the command's cardSource: this), as the damage math,
            // the HP-loss passes and the hit's reactions see it (Buffer asks whether it's Brightest Flame).
            DamagePlayer(state, card.Title, player, card.Amount, props, dealer: player, card: card.Source ?? new SimCard(card.Card, SimCardType.None));
        }
    }

    /// <summary>
    /// Hook.ShouldTakeExtraTurn of each player as the players' turn is over (CombatManager.SwitchFromPlayerToEnemySide, decision #319): every
    /// listener's Early pass, then its own, in the game's listener order; the first that says yes gives that player another turn before the
    /// enemies act, and is kept as its source (SimPlayer.ExtraTurnFrom). Not asked in a branch whose fight is over (the turn loop ends the combat
    /// instead: decision #301), nor of a player who died. The enemy turn is still simulated after it: what happens before the enemies act (an
    /// enemy's death, the lethal tags) is unchanged, and what the enemies would do to that player isn't shown.
    /// </summary>
    private static void ExtraTurns(SimState state)
    {
        if (FightOver(state)) return;
        foreach (var player in state.PlayerIds)
        {
            if (state.Player(player) is not { Died: false } simPlayer) continue;
            var source = ExtraTurnSource(state, player);
            if (source != simPlayer.ExtraTurnFrom) state.MutablePlayer(player).ExtraTurnFrom = source;
        }
    }

    /// <summary>Hook.ShouldTakeExtraTurn's answer for <paramref name="player"/>: the name of the first listener that says yes (Early first), or null.</summary>
    private static string? ExtraTurnSource(SimState state, int player)
    {
        var listeners = Listeners(state);
        for (var i = 0; i < listeners.Count; i++)
            if (listeners[i].Model.ShouldTakeExtraTurnEarly(state, listeners[i].Owner, player)) return listeners[i].Model.Name;
        for (var i = 0; i < listeners.Count; i++)
            if (listeners[i].Model.ShouldTakeExtraTurn(state, listeners[i].Owner, player)) return listeners[i].Model.Name;
        return null;
    }
}
