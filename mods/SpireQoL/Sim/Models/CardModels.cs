namespace SpireQoL.Sim.Models;

// Cards' own hooks (CardListener): each listens from its owner's piles (SimPlayer.Cards), after its owner's powers and relics
// (decompile v0.113.1).

/// <summary>
/// Void.AfterCardDrawn: the card drawn is this one, so its owner loses its Energy (PlayerCmd.LoseEnergy: no hooks, never below 0),
/// after their powers' and relics' draw reactions (Automation's energy, Iteration's and Pagestorm's draws). Any card whose own draw
/// loses energy listens as this model (CardEnergy.LostWhenDrawn: SimDrawCard.EnergyLostOnDraw).
/// </summary>
public sealed class VoidCard : CardListener
{
    public override string Name => CardId.Void.Name;

    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player)
    {
        if (cardEvent != CardEvent.Drawn || player != owner || state.DrawnCard is not { EnergyLostOnDraw: > 0 } drawn) return One(state);
        var simPlayer = state.MutablePlayer(owner);
        simPlayer.Energy = Math.Max(0, simPlayer.Energy - drawn.EnergyLostOnDraw);
        return One(state);
    }
}

/// <summary>
/// Regret.BeforeSideTurnEnd: a Regret in its owner's hand counts the cards there as their side's turn ends (after their powers' and
/// relics' BeforeSideTurnEnd), and its OnTurnEndInHand deals that many later (SimEngine.CardsActInHand), whatever the hand holds by
/// then (its Ethereal cards exhausted, what their exhausts drew).
/// </summary>
public sealed class RegretCard : CardListener
{
    public override string Name => CardId.Regret.Name;

    /// <summary>Its BeforeSideTurnEnd waits on its owner, a player, taking part: never as the enemies' turn ends.</summary>
    public override bool MayActOnTheEnemiesSide(string hook, int owner) => false;

    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState state, int owner)
    {
        if (!state.EndsTheirTurn(owner) || state.Player(owner) is not { } player || !player.TurnEndCards.Any(card => card.Card == Name)) return One(state);
        var hand = Math.Max(0, player.HandCount);
        state.MutablePlayer(owner).TurnEndCards = player.TurnEndCards.Select(card => card.Card == Name ? card with { Amount = hand } : card).ToList();
        return One(state);
    }
}
