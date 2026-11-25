namespace SpireQoL.Sim.Models;

// Block sources that change during your turn (decompile v0.113.1). Each is bound to one play the way the game binds it.

// Stretch and Pael's Legion are read from their code (ReadModel, decision #269): the card play each keeps (its identity and player), Stretch's bonus
// given as its block term works it out, Pael's Legion's play kept as its term changes the block (AfterModifyingBlockAmount).

/// <summary>
/// Vambrace (relic): Data bit 1 = a card is bound (its block was doubled), bit 2 = block gained this combat. Until then, a card's
/// block from its owner is doubled; the first time that lands it flashes and binds the card (AfterModifyingBlockAmount);
/// AfterCardPlayed of that card marks block gained this combat (so a replay's later plays aren't doubled).
/// </summary>
public sealed class Vambrace : SimModel
{
    /// <summary>Its Data: a card is bound (its block was doubled: the game's TriggeringCard), block gained this combat.</summary>
    public const int Bound = 1, GainedThisCombat = 2;
    public override TabKind? Tab => TabKind.Trigger;
    public override decimal ModifyBlockMultiplicative(SimState state, int owner, BlockCtx block)
    {
        if (!block.Props.IsCardOrMonsterMove() || block.Card == null || block.Play?.Owner != owner) return 1m;
        return (SimEngine.RelicsOf(state, owner).DataOf(ThisRelic) & GainedThisCombat) != 0 ? 1m : 2m;
    }
    public override void AfterModifyingBlockAmount(SimState state, int owner, int amount, SimCardPlay? play)
    {
        var relics = SimEngine.RelicsOf(state, owner);
        if (amount <= 0 || play == null) return;
        if ((relics.DataOf(ThisRelic) & Bound) == 0) state.LogFired(Name, amount: 0, owner);
        SimEngine.SetRelics(state, owner, relics.With(ThisRelic, relics[ThisRelic], relics.DataOf(ThisRelic) | Bound));
    }
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        var relics = SimEngine.RelicsOf(state, owner);
        var data = relics.DataOf(ThisRelic);
        if (play.Owner == owner && (data & Bound) != 0 && (data & GainedThisCombat) == 0) SimEngine.SetRelics(state, owner, relics.With(ThisRelic, relics[ThisRelic], data: GainedThisCombat));
        return One(state);
    }
}

/// <summary>
/// UnmovablePower.ModifyBlockMultiplicative: doubles its owner's block from cards (Move) while their card plays this turn that gained block, other
/// than the play in progress, are fewer than its amount (v0.114.0: DistinctBy the entries' CardPlay, so a play gaining block twice counts once).
/// </summary>
public sealed class UnmovablePower : SimModel
{
    public override decimal ModifyBlockMultiplicative(SimState state, int owner, BlockCtx block)
    {
        if (block.Target >= 0 || !block.Props.IsCardOrMonsterMove()) return 1m;
        if (block.Play != null && block.Play.Owner != owner) return 1m;
        var turn = SimEngine.TurnOf(state, owner);
        var earlier = turn[SimKeys.CardBlock] - (block.Play != null && state.PlayCardBlockGains > 0 ? 1 : 0);
        return earlier >= SimEngine.PowerOn(state, owner, ThisPower) ? 1m : 2m;
    }
}

/// <summary>ShadowmeldPower.ModifyBlockMultiplicative: 2^amount on its owner's block.</summary>
public sealed class ShadowmeldPower : SimModel
{
    public override decimal ModifyBlockMultiplicative(SimState state, int owner, BlockCtx block) =>
        block.Target == owner ? (decimal)Math.Pow(2.0, SimEngine.PowerOn(state, owner, ThisPower)) : 1m;
}
