namespace SpireQoL.Sim.Models;

// Block sources that change during your turn (decompile v0.113.1). Each is bound to one play the way the game binds it.

// Stretch and Pael's Legion are read from their code (ReadModel, decision #269): the card play each keeps (its identity and player), Stretch's bonus
// given as its block term works it out, Pael's Legion's play kept as its term changes the block (AfterModifyingBlockAmount).

// Vambrace is read from its code (ReadModel, decision #272): the card whose block it doubled kept as its card play's identity.

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
