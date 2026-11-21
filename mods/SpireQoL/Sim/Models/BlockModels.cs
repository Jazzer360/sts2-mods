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

    public override IEnumerable<SimState> BeforeCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        var p = SimEngine.PowersOf(s, owner);
        if (play.Owner != owner || !play.Card.GainsBlock || !p.Has(ThisPower) || (p.DataOf(ThisPower) & Bound) != 0) return One(s);
        SimEngine.SetPowers(s, owner, p.With(ThisPower, p[ThisPower], Bound | (p[ThisPower] << 4)));
        return One(s);
    }

    public override decimal ModifyBlockAdditive(SimState s, int owner, BlockCtx b)
    {
        if (b.Card == null || b.Play?.Owner != owner || !b.Props.IsPoweredBlock()) return 0m;
        var p = SimEngine.PowersOf(s, owner);
        return (p.DataOf(ThisPower) & Bound) != 0 ? p[ThisPower] : 0m;
    }

    public override void AfterModifyingBlockAmount(SimState s, int owner, int amount, SimCardPlay? play)
    {
        var p = SimEngine.PowersOf(s, owner);
        if ((p.DataOf(ThisPower) & Bound) != 0) SimEngine.SetPowers(s, owner, p.With(ThisPower, p[ThisPower], p.DataOf(ThisPower) | Gave));
    }

    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        var p = SimEngine.PowersOf(s, owner);
        var data = p.DataOf(ThisPower);
        if (play.Owner != owner || (data & Bound) == 0) return One(s);
        if ((data & Gave) == 0)
        {
            SimEngine.SetPowers(s, owner, p.With(ThisPower, p[ThisPower], 0));
            return One(s);
        }
        var left = p[ThisPower] - (data >> 4);
        SimEngine.SetPowers(s, owner, left <= 0 ? p.Without(ThisPower) : p.With(ThisPower, left, 0));
        s.LogFired(Name, 0, owner);
        return One(s);
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
    public override decimal ModifyBlockMultiplicative(SimState s, int owner, BlockCtx b)
    {
        if (!b.Props.IsCardOrMonsterMove() || b.Card == null || b.Play?.Owner != owner) return 1m;
        var r = SimEngine.RelicsOf(s, owner);
        return r[ThisRelic] > 0 ? 1m : 2m; // bound or not: within one play, the play in progress is the bound one
    }
    public override void AfterModifyingBlockAmount(SimState s, int owner, int amount, SimCardPlay? play)
    {
        var r = SimEngine.RelicsOf(s, owner);
        if (amount > 0 && play != null && r[ThisRelic] <= 0) SimEngine.SetRelics(s, owner, r.With(ThisRelic, r[ThisRelic], 1));
    }
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        var r = SimEngine.RelicsOf(s, owner);
        if (r.DataOf(ThisRelic) != 1) return One(s);
        s.LogFired(Name, 0, owner);
        SimEngine.SetRelics(s, owner, r.With(ThisRelic, 2, 0)); // Turns: 2 (PaelsLegion.CanonicalVars)
        return One(s);
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
    public override decimal ModifyBlockMultiplicative(SimState s, int owner, BlockCtx b)
    {
        if (!b.Props.IsCardOrMonsterMove() || b.Card == null || b.Play?.Owner != owner) return 1m;
        return (SimEngine.RelicsOf(s, owner).DataOf(ThisRelic) & 2) != 0 ? 1m : 2m;
    }
    public override void AfterModifyingBlockAmount(SimState s, int owner, int amount, SimCardPlay? play)
    {
        var r = SimEngine.RelicsOf(s, owner);
        if (amount <= 0 || play == null) return;
        if ((r.DataOf(ThisRelic) & 1) == 0) s.LogFired(Name, 0, owner);
        SimEngine.SetRelics(s, owner, r.With(ThisRelic, r[ThisRelic], r.DataOf(ThisRelic) | 1));
    }
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        var r = SimEngine.RelicsOf(s, owner);
        var data = r.DataOf(ThisRelic);
        if (play.Owner == owner && (data & 1) != 0 && (data & 2) == 0) SimEngine.SetRelics(s, owner, r.With(ThisRelic, r[ThisRelic], 2));
        return One(s);
    }
}

/// <summary>
/// UnmovablePower.ModifyBlockMultiplicative: doubles its owner's block from cards (Move) while their block gains from card
/// plays this turn, other than the play in progress, are fewer than its amount.
/// </summary>
public sealed class UnmovablePower : SimModel
{
    public override decimal ModifyBlockMultiplicative(SimState s, int owner, BlockCtx b)
    {
        if (b.Target >= 0 || !b.Props.IsCardOrMonsterMove()) return 1m;
        if (b.Play != null && b.Play.Owner != owner) return 1m;
        var turn = SimEngine.TurnOf(s, owner);
        var earlier = turn[SimKeys.CardBlock] - (b.Play != null ? s.PlayCardBlockGains : 0);
        return earlier >= SimEngine.PowerOn(s, owner, ThisPower) ? 1m : 2m;
    }
}

/// <summary>ShadowmeldPower.ModifyBlockMultiplicative: 2^amount on its owner's block.</summary>
public sealed class ShadowmeldPower : SimModel
{
    public override decimal ModifyBlockMultiplicative(SimState s, int owner, BlockCtx b) =>
        b.Target == owner ? (decimal)Math.Pow(2.0, SimEngine.PowerOn(s, owner, ThisPower)) : 1m;
}

/// <summary>NoBlockPower.ModifyBlockMultiplicative: its owner gains no block from cards (anything not Unpowered).</summary>
public sealed class NoBlockPower : SimModel
{
    public override decimal ModifyBlockMultiplicative(SimState s, int owner, BlockCtx b) =>
        b.Target == owner && !b.Props.HasFlag(SimProps.Unpowered) && b.Card != null ? 0m : 1m;
}
