namespace SpireQoL.Sim.Models;

// Block sources that change during your turn (decompile v0.113.1). Each is bound to one play the way the game binds it.

/// <summary>
/// StretchPower: BeforeCardPlayed binds the first play of its owner's that gains block (Data 1: bound to the play in progress);
/// that play's powered block gets its amount (ModifyBlockAdditive); AfterCardPlayed of that play spends the amount it had when
/// the play started, if the bonus was given (Data 2: bound and given). The amount at the start lives in the high bits.
/// </summary>
public sealed class StretchPower : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    private const int Bound = 1, Gave = 2;

    public override IEnumerable<SimState> BeforeCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        var powers = SimEngine.PowersOf(state, owner);
        if (play.Owner != owner || !play.Card.GainsBlock || !powers.Has(ThisPower) || (powers.DataOf(ThisPower) & Bound) != 0) return One(state);
        SimEngine.SetPowers(state, owner, powers.With(ThisPower, powers[ThisPower], Bound | (powers[ThisPower] << 4)));
        return One(state);
    }

    public override decimal ModifyBlockAdditive(SimState state, int owner, BlockCtx block)
    {
        if (block.Card == null || block.Play?.Owner != owner || !block.Props.IsPoweredBlock()) return 0m;
        var powers = SimEngine.PowersOf(state, owner);
        return (powers.DataOf(ThisPower) & Bound) != 0 ? powers[ThisPower] : 0m;
    }

    public override void AfterModifyingBlockAmount(SimState state, int owner, int amount, SimCardPlay? play)
    {
        var powers = SimEngine.PowersOf(state, owner);
        if ((powers.DataOf(ThisPower) & Bound) != 0) SimEngine.SetPowers(state, owner, powers.With(ThisPower, powers[ThisPower], powers.DataOf(ThisPower) | Gave));
    }

    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        var powers = SimEngine.PowersOf(state, owner);
        var data = powers.DataOf(ThisPower);
        if (play.Owner != owner || (data & Bound) == 0) return One(state);
        if ((data & Gave) == 0)
        {
            SimEngine.SetPowers(state, owner, powers.With(ThisPower, powers[ThisPower], data: 0));
            return One(state);
        }
        var left = powers[ThisPower] - (data >> 4);
        SimEngine.SetPowers(state, owner, left <= 0 ? powers.Without(ThisPower) : powers.With(ThisPower, left, data: 0));
        state.LogFired(Name, amount: 0, owner);
        return One(state);
    }
}

/// <summary>
/// Pael's Legion (relic): Amount is its cooldown (turns), Data 1 while a play is bound. Off cooldown, a card's block from its
/// owner is doubled (ModifyBlockMultiplicative) for the first play whose block lands (AfterModifyingBlockAmount binds it);
/// AfterCardPlayed of that play flashes and starts the cooldown (its Turns var: the counter's own value when read).
/// </summary>
public sealed class PaelsLegion : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override decimal ModifyBlockMultiplicative(SimState state, int owner, BlockCtx block)
    {
        if (!block.Props.IsCardOrMonsterMove() || block.Card == null || block.Play?.Owner != owner) return 1m;
        var relics = SimEngine.RelicsOf(state, owner);
        return relics[ThisRelic] > 0 ? 1m : 2m; // bound or not: within one play, the play in progress is the bound one
    }
    public override void AfterModifyingBlockAmount(SimState state, int owner, int amount, SimCardPlay? play)
    {
        var relics = SimEngine.RelicsOf(state, owner);
        if (amount > 0 && play != null && relics[ThisRelic] <= 0) SimEngine.SetRelics(state, owner, relics.With(ThisRelic, relics[ThisRelic], data: 1));
    }
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        var relics = SimEngine.RelicsOf(state, owner);
        if (relics.DataOf(ThisRelic) != 1) return One(state);
        state.LogFired(Name, amount: 0, owner);
        SimEngine.SetRelics(state, owner, relics.With(ThisRelic, amount: 2, data: 0)); // Turns: 2 (PaelsLegion.CanonicalVars)
        return One(state);
    }
}

/// <summary>
/// Vambrace (relic): Data bit 1 = a card is bound (its block was doubled), bit 2 = block gained this combat. Until then, a card's
/// block from its owner is doubled; the first time that lands it flashes and binds the card (AfterModifyingBlockAmount);
/// AfterCardPlayed of that card marks block gained this combat (so a replay's later plays aren't doubled).
/// </summary>
public sealed class Vambrace : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override decimal ModifyBlockMultiplicative(SimState state, int owner, BlockCtx block)
    {
        if (!block.Props.IsCardOrMonsterMove() || block.Card == null || block.Play?.Owner != owner) return 1m;
        return (SimEngine.RelicsOf(state, owner).DataOf(ThisRelic) & 2) != 0 ? 1m : 2m;
    }
    public override void AfterModifyingBlockAmount(SimState state, int owner, int amount, SimCardPlay? play)
    {
        var relics = SimEngine.RelicsOf(state, owner);
        if (amount <= 0 || play == null) return;
        if ((relics.DataOf(ThisRelic) & 1) == 0) state.LogFired(Name, amount: 0, owner);
        SimEngine.SetRelics(state, owner, relics.With(ThisRelic, relics[ThisRelic], relics.DataOf(ThisRelic) | 1));
    }
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        var relics = SimEngine.RelicsOf(state, owner);
        var data = relics.DataOf(ThisRelic);
        if (play.Owner == owner && (data & 1) != 0 && (data & 2) == 0) SimEngine.SetRelics(state, owner, relics.With(ThisRelic, relics[ThisRelic], data: 2));
        return One(state);
    }
}

/// <summary>
/// UnmovablePower.ModifyBlockMultiplicative: doubles its owner's block from cards (Move) while their block gains from card
/// plays this turn, other than the play in progress, are fewer than its amount.
/// </summary>
public sealed class UnmovablePower : SimModel
{
    public override decimal ModifyBlockMultiplicative(SimState state, int owner, BlockCtx block)
    {
        if (block.Target >= 0 || !block.Props.IsCardOrMonsterMove()) return 1m;
        if (block.Play != null && block.Play.Owner != owner) return 1m;
        var turn = SimEngine.TurnOf(state, owner);
        var earlier = turn[SimKeys.CardBlock] - (block.Play != null ? state.PlayCardBlockGains : 0);
        return earlier >= SimEngine.PowerOn(state, owner, ThisPower) ? 1m : 2m;
    }
}

/// <summary>ShadowmeldPower.ModifyBlockMultiplicative: 2^amount on its owner's block.</summary>
public sealed class ShadowmeldPower : SimModel
{
    public override decimal ModifyBlockMultiplicative(SimState state, int owner, BlockCtx block) =>
        block.Target == owner ? (decimal)Math.Pow(2.0, SimEngine.PowerOn(state, owner, ThisPower)) : 1m;
}

/// <summary>NoBlockPower.ModifyBlockMultiplicative: its owner gains no block from cards (anything not Unpowered).</summary>
public sealed class NoBlockPower : SimModel
{
    public override decimal ModifyBlockMultiplicative(SimState state, int owner, BlockCtx block) =>
        block.Target == owner && !block.Props.HasFlag(SimProps.Unpowered) && block.Card != null ? 0m : 1m;
}
