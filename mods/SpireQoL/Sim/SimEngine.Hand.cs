namespace SpireQoL.Sim;

/// <summary>A card in a player's hand changed by a power's or relic's code (decompile v0.113.1).</summary>
public static partial class SimEngine
{
    /// <summary>
    /// CardModel.SetToFreeThisTurn on the game's random pick among the cards in <paramref name="player"/>'s hand that cost energy or stars
    /// (CostsEnergyOrStars: an X cost doesn't), as Mummified Hand picks (FreeInHandStep). What reads a hand card's cost in a simulation is the
    /// pick of the cheapest at the turn's end (SimpletonPower), from the hand as AutoHand holds it, which is then every playable card: a branch per
    /// card there that costs energy, each equally likely, that card free. Otherwise nothing a simulation reads changes. <paramref name="played"/>
    /// is the card in play (SimCard.HandId), no longer in the hand.
    /// </summary>
    internal static IEnumerable<SimState> FreeInHand(SimState state, int player, int played)
    {
        if (state.AutoPlaysUnknown || state.Player(player) is not { Simpleton: > 0, AutoHand: { } hand }) return new[] { state };
        var costing = hand.Where(card => card.Id != played && card.Cost is > 0 and < XCost).ToList();
        if (costing.Count == 0) return new[] { state };
        var result = new List<SimState>(costing.Count);
        foreach (var free in costing)
        {
            var branch = costing.Count == 1 ? state : state.Clone();
            branch.Weight = state.Weight / costing.Count;
            branch.MutablePlayer(player).AutoHand = hand.Select(card => card.Id == free.Id ? card.FreeCopy() : card).ToList();
            result.Add(branch);
        }
        return result;
    }

    /// <summary>An X cost in a hand card's Cost (SimHandCard.Cost: SimpletonPower orders X costs last).</summary>
    private const int XCost = 999999999;
}
