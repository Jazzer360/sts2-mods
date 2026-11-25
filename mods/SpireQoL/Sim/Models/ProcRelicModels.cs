namespace SpireQoL.Sim.Models;

// Relics that act on your plays and card events (decompile v0.113.1). Amount: the relic's counter; Data bit 1: armed (unused
// this combat or turn, RelicCountersFeature.IsArmed), unless a model says otherwise.

// ---- giving up a relic's bonus (crossed tabs; Art of War's and Ripple Basin's are read from their code: decision #239)

/// <summary>
/// PaelsTears (BeforeSideTurnEnd: any energy left gives energy next turn): a play that spends energy and leaves you with none gives
/// that up (bad for you). Judged once the play has resolved (AfterPlayResolved), so energy the play gets back first (Orbit, Ivory
/// Tile, Subroutine, the card's own) keeps the bonus and shows no warning. Data: energy was spent during this play (1), not the armed bit.
/// </summary>
public sealed class PaelsTears : RelicProc
{
    /// <summary>Its Data: energy was spent during the play under way (none: 0).</summary>
    private const int SpentThisPlay = 1;
    public override bool Harmful => true;
    public override ActedSeen Seen => ActedSeen.NoEnergyLeft;
    public override IEnumerable<SimState> AfterCardEvent(SimState state, int owner, CardEvent cardEvent, int player)
    {
        if (cardEvent == CardEvent.EnergySpent && player == owner && Data(state, owner) == 0) Set(state, owner, Amount(state, owner), data: SpentThisPlay);
        return One(state);
    }
    public override IEnumerable<SimState> AfterPlayResolved(SimState state, int owner, int player)
    {
        if (player != owner || Data(state, owner) == 0) return One(state);
        Set(state, owner, Amount(state, owner), data: 0);
        return state.Player(owner) is { Energy: <= 0 } ? Fire(state, owner) : One(state);
    }
}

/// <summary>
/// Pocketwatch (ModifyHandDraw: 3 more cards at the start of your next turn when you played no more than its CardThreshold this
/// turn; AfterCardPlayed counts each card its owner plays, auto-plays and each play of a replay included): the play that takes the
/// count past the threshold gives that up (bad for you). Amount: cards played this turn (its counter); Data: the threshold.
/// </summary>
public sealed class Pocketwatch : RelicProc
{
    public override bool Harmful => true;
    /// <summary>It doesn't flash: its counter (cards this turn) goes past the threshold.</summary>
    public override ActedSeen Seen => ActedSeen.CounterPassedLimit;
    public override string? LimitVar => "CardThreshold";
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner)) return One(state);
        var count = Amount(state, owner) + 1;
        Set(state, owner, count, Data(state, owner));
        return count == Data(state, owner) + 1 ? Fire(state, owner) : One(state);
    }
}

/// <summary>PaelsEye.BeforeCardPlayed: while armed, the turn's first card you play (not an auto-play) gives up its extra turn (bad for you).</summary>
public sealed class PaelsEye : RelicProc
{
    public override bool Harmful => true;
    /// <summary>The turn's first card clears its Active status (BeforeCardPlayed: Status = Normal); it doesn't flash.</summary>
    public override ActedSeen Seen => ActedSeen.StatusCleared;
    /// <summary>Its BeforeSideTurnEndEarly waits on its owner, a player, taking part: never as the enemies' turn ends.</summary>
    public override bool MayActOnTheEnemiesSide(string hook, int owner) => false;

    /// <summary>
    /// BeforeSideTurnEndEarly: still armed (no card played this turn: SimPlayer.PaelsEyeExhaust), its owner's whole hand is
    /// exhausted (Feel No Pain, Charon's Ashes... react to each card); nothing is left to act in hand or exhaust as Ethereal.
    /// </summary>
    public override IEnumerable<SimState> BeforeSideTurnEndEarly(SimState state, int owner)
    {
        if (!state.EndsTheirTurn(owner) || state.Player(owner) is not { PaelsEyeExhaust: true, HandCount: > 0 } player) return One(state);
        var cards = player.HandCount;
        return SimEngine.ProcCardEvent(state, CardEvent.Exhausted, cards, owner, indirect: false).Select(branch =>
        {
            var simPlayer = branch.MutablePlayer(owner);
            simPlayer.HandCount = 0;
            simPlayer.Fading = 0;
            simPlayer.TurnEndCards = new();
            return branch;
        }).ToList();
    }
    public override IEnumerable<SimState> BeforeCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || play.AutoPlay || !Armed(state, owner) || SimEngine.TurnOf(state, owner)[SimKeys.Plays(PlayStage.Started)] != 0) return One(state);
        Disarm(state, owner);
        return Fire(state, owner);
    }
}

// Biiig Hug is read from its code (ReadModel, decision #257).

// ---- used up by a play

// Burning Sticks is read from its code (ReadModel, decision #252).

/// <summary>
/// UnsettlingLamp: while armed, the first card of yours that applies a debuff to an enemy without Artifact (BeforePowerAmountChanged)
/// has its debuffs doubled (ModifyPowerAmountGivenMultiplicative); AfterCardPlayed of that card uses it up. Data bit 2: bound.
/// </summary>
public sealed class UnsettlingLamp : RelicProc
{
    private const int BoundBit = 2;
    public override int ModifyPowerAmountGiven(SimState state, int owner, PowerId power, int amount, int? applier, int target, bool debuff)
    {
        if (!debuff || applier != owner || target < 0 || state.CurrentPlay is not { } play || play.Owner != owner) return amount;
        var data = Data(state, owner);
        if ((data & BoundBit) == 0)
        {
            if (!Armed(state, owner) || SimEngine.PowerOn(state, target, PowerId.Artifact) > 0) return amount;
            Set(state, owner, Amount(state, owner), data | BoundBit);
        }
        return amount * 2;
    }
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (!Mine(play, owner) || (Data(state, owner) & BoundBit) == 0) return One(state);
        Set(state, owner, Amount(state, owner), data: 0);
        return Fire(state, owner);
    }
}

// ---- on play

/// <summary>ChemicalX.ModifyXValue: X-cost cards (energy or stars) get more.</summary>
public sealed class ChemicalX : RelicProc
{
    public override IEnumerable<SimState> BeforeCardPlayed(SimState state, int owner, SimCardPlay play) =>
        Mine(play, owner) && play.PlayIndex == 0 && (play.Card.CostsX || play.Card.StarCostX) ? Fire(state, owner) : One(state);
}

/// <summary>
/// UnceasingTop.AfterHandEmptied: a draw (CardPileCmd.Draw) when a play leaves your hand empty. The game checks once the card has
/// resolved (CardModel: CombatManager.CheckForEmptyHand after it reaches its pile) and only while no other card's effect is running,
/// in your turn's play and auto-play phases (IsValidPhase: Stampede's plays at the end of your turn count): your hand holds nothing and
/// no card of yours is still being played. The card it draws is drawn, with what reacts to draws.
/// </summary>
public sealed class UnceasingTop : RelicProc
{
    public override IEnumerable<SimState> AfterPlayResolved(SimState state, int owner, int player) =>
        player == owner && !state.EnemyTurnNow && state.Player(owner) is { HandCount: 0, PlayingFromHand: 0 }
            ? Cause(Fire(state, owner), CardEvent.Drawn, count: 1, owner)
            : One(state);
}

// ---- counters

// ---- card events


