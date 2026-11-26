namespace SpireQoL.Sim;

/// <summary>A card in a player's hand changed by a power's or relic's code, and what it costs (decompile v0.114.0).</summary>
public static partial class SimEngine
{
    /// <summary>
    /// CardCmd.Upgrade of a player's cards in <paramref name="piles"/> (UpgradeCardsAction; <paramref name="source"/> names what upgrades them). The command
    /// runs no hook (UpgradeInternal, FinalizeUpgradeInternal on each card), so what it changes is what the simulator reads of an upgraded card later:
    /// in the hand (UpgradeInHand); in the draw and discard piles, a card drawn or taken from there, or played off the top, whose profile an upgrade
    /// could change (SimDrawCard.Upgradable: SimPlayer.PilesUpgraded makes that unknown); in the exhaust pile, a card that plays itself from there as the
    /// turn ends (Howl from Beyond: SimPlayer.ExhaustAutoPlays) that can be upgraded. I Am Invincible's block on top of the draw pile is read at the
    /// board, so an upgrade of that pile with it there is unknown.
    /// </summary>
    internal static void UpgradeCards(SimState state, int player, UpgradedPiles piles, string source)
    {
        if (state.Player(player) is not { } simPlayer) return;
        if (piles.HasFlag(UpgradedPiles.Hand)) UpgradeInHand(state, player, source);
        if ((piles & (UpgradedPiles.Draw | UpgradedPiles.Discard)) != 0)
        {
            if (piles.HasFlag(UpgradedPiles.Draw) && simPlayer.InvincibleBlock > 0)
                state.Unmodelled ??= $"{source} upgrades your draw pile, and I Am Invincible plays from its top as the turn ends";
            if (simPlayer.Draw != null && !simPlayer.PilesUpgraded) state.MutablePlayer(player).PilesUpgraded = true;
        }
        if (piles.HasFlag(UpgradedPiles.Exhaust) && simPlayer.ExhaustAutoPlays.Any(play => play.Card?.Card.Upgradable != false))
            state.Unmodelled ??= $"{source} upgrades your exhaust pile, and a card there that can be upgraded plays itself as the turn ends";
    }

    /// <summary>
    /// CardCmd.Upgrade of cards in a player's hand (every one, or random ones: TunePower's; a card you pick is a card, decision #188): followed while
    /// no card in that hand that can be upgraded acts later in a way the simulator reads from the board (SimPlayer.UpgradableAtTurnEnd: Ethereal, or
    /// acting in hand as the turn ends: Burn), and no card Stampede or Simpleton could auto-play from it can be (their plays are built at the
    /// board: SimState.AutoPlaysUnknown then). Anything else of a hand card's (its numbers) is read when it's played, a later decision.
    /// </summary>
    internal static void UpgradeInHand(SimState state, int player, string source)
    {
        if (state.Player(player) is not { } simPlayer) return;
        if (simPlayer.UpgradableAtTurnEnd > 0)
            state.Unmodelled ??= $"{source} upgrades cards in hand, and one that acts as the turn ends could be one";
        if (simPlayer.AutoHand?.Any(card => card.Card?.Upgradable != false) == true) state.AutoPlaysUnknown = true;
    }

    /// <summary>
    /// A hand card's live energy cost (CardEnergyCost.GetWithModifiers(CostModifiers.All), decision #334): its cost before global modifiers
    /// (SimHandCard.LocalCost), then Hook.ModifyEnergyCostInCombat on it: every listener's TryModifyEnergyCostInCombat in the game's order on the
    /// cost so far, then every listener's Late pass (Curious' Powers 1 less, Borrowed Time's 1 more, Corruption's Skills 0), at least 0. A cost
    /// below 0 stays (no cost: the hooks are skipped). An X cost is <see cref="XCost"/> (SimpletonPower orders it last). Null where it isn't known:
    /// a cost pass on the board the simulator doesn't run (SimState.CostsUnknown). A card read before the rule (no LocalCost: an old replay
    /// case) costs what the game showed then. <paramref name="pile"/>: the card's pile (your hand; the play pile while it's played).
    /// </summary>
    internal static int? HandCost(SimState state, int player, SimHandCard card, SimCardPile pile = SimCardPile.Hand)
    {
        if (card.Cost == XCost) return XCost;
        if (card.LocalCost is not { } cost || card.Card is not { } facts) return card.Cost;
        if (cost < 0) return cost;
        if (state.CostsUnknown != null) return null;
        var costed = new SimCostedCard(facts, player, pile);
        var listeners = Listeners(state);
        // The out value is the cost from here on, whatever the answer (the game's loop passes modifiedCost on).
        foreach (var (model, owner) in listeners) model.TryModifyEnergyCostInCombat(state, owner, costed, cost, out cost);
        foreach (var (model, owner) in listeners) model.TryModifyEnergyCostInCombatLate(state, owner, costed, cost, out cost);
        return Math.Max(0, cost);
    }

    /// <summary>
    /// CardModel.SetToFreeThisTurn on the game's random pick among the cards in <paramref name="player"/>'s hand that cost energy or stars
    /// (CostsEnergyOrStars: an X cost doesn't), each at its live cost (HandCost), as Mummified Hand picks (FreeInHandStep). What reads a hand
    /// card's cost in a simulation is the pick of the cheapest at the turn's end (SimpletonPower), from the hand as AutoHand holds it, which is then
    /// every playable card: a branch per card there that costs energy, each equally likely, that card free (its local cost 0, the listeners' passes
    /// still on it). Otherwise nothing a simulation reads changes. A cost that isn't known makes the branch unknown (SimState.CostsUnknown).
    /// <paramref name="played"/> is the card in play (SimCard.HandId), no longer in the hand.
    /// </summary>
    internal static IEnumerable<SimState> FreeInHand(SimState state, int player, int played)
    {
        if (state.AutoPlaysUnknown || state.Player(player) is not { Simpleton: > 0, AutoHand: { } hand }) return new[] { state };
        var costing = new List<SimHandCard>();
        foreach (var card in hand)
        {
            if (card.Id == played) continue;
            if (HandCost(state, player, card) is not { } cost)
            {
                state.Unmodelled ??= state.CostsUnknown;
                return new[] { state };
            }
            if (cost is > 0 and < XCost) costing.Add(card);
        }
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

    /// <summary>
    /// SimpletonPower's pick: the playable cards in <paramref name="player"/>'s hand at the lowest live cost (GetResolved, an X cost last), the
    /// game's random pick among them. Unknown where a cost isn't (SimState.CostsUnknown): the branch is marked, and the costs the game showed when
    /// the hand was read stand for the pick.
    /// </summary>
    private static List<SimHandCard> Cheapest(SimState state, int player, List<SimHandCard> hand)
    {
        if (hand.Count == 0) return hand;
        var costs = new int[hand.Count];
        for (var i = 0; i < hand.Count; i++)
        {
            if (HandCost(state, player, hand[i]) is { } cost) costs[i] = cost;
            else
            {
                state.Unmodelled ??= state.CostsUnknown;
                costs[i] = hand[i].Cost;
            }
        }
        var lowest = costs.Min();
        var cheapest = new List<SimHandCard>();
        for (var i = 0; i < hand.Count; i++)
            if (costs[i] == lowest) cheapest.Add(hand[i]);
        return cheapest;
    }

    /// <summary>An X cost in a hand card's Cost (SimHandCard.Cost: SimpletonPower orders X costs last).</summary>
    private const int XCost = 999999999;
}
