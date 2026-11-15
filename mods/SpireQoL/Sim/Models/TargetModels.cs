namespace SpireQoL.Sim.Models;

// Damage sources keyed on the target or on positions that change during your turn (decompile v0.113.1).

/// <summary>TrackingPower.ModifyDamageMultiplicative: 1 + amount/100 on the owner's (or pet's) powered hits from a card on a Weak enemy.</summary>
public sealed class TrackingPower : SimModel
{
    public override string Name => "TrackingPower";
    public override decimal ModifyDamageMultiplicative(SimState s, int owner, DamageCtx d)
    {
        if (!d.Props.IsPoweredAttack() || d.Card == null) return 1m;
        if (!(d.Dealer == owner || (owner == Who.You && d.Dealer == Who.YourOsty))) return 1m;
        return SimEngine.PowersOf(s, d.Target).Has("WeakPower") ? 1m + SimEngine.PowerOn(s, owner, Name) / 100m : 1m;
    }
}

/// <summary>ColossusPower.ModifyDamageMultiplicative: powered attacks on its owner from a Vulnerable dealer deal its DamageDecrease (0.5).</summary>
public sealed class ColossusPower : SimModel
{
    public override string Name => "ColossusPower";
    public override decimal ModifyDamageMultiplicative(SimState s, int owner, DamageCtx d) =>
        d.Target == owner && d.Props.IsPoweredAttack() && d.Dealer is { } dealer && SimEngine.PowersOf(s, dealer).Has("VulnerablePower") ? 0.5m : 1m;
}

/// <summary>TaintedPower.ModifyDamageAdditive: its amount on powered attacks on its owner (Vital Spark gives it for Tainted cards).</summary>
public sealed class TaintedPower : SimModel
{
    public override string Name => "TaintedPower";
    public override decimal ModifyDamageAdditive(SimState s, int owner, DamageCtx d) =>
        d.Target == owner && d.Props.IsPoweredAttack() ? SimEngine.PowerOn(s, owner, Name) : 0m;
}

/// <summary>
/// FlutterPower: powered attacks on its owner deal its DamageDecrease% (Data; 50 by default); each that gets HP through takes a
/// stack (AfterDamageReceived), and the last stuns it.
/// </summary>
public sealed class FlutterPower : SimModel
{
    public override string Name => "FlutterPower";
    public override decimal ModifyDamageMultiplicative(SimState s, int owner, DamageCtx d)
    {
        if (d.Target != owner || !d.Props.IsPoweredAttack()) return 1m;
        var pct = SimEngine.PowersOf(s, owner).DataOf(Name);
        return (pct == 0 ? 50 : pct) / 100m;
    }
    public override void AfterDamageReceived(SimState s, int owner, DamageEvent e)
    {
        if (e.Target != owner || e.Unblocked == 0 || !e.Props.IsPoweredAttack() || owner < 0) return;
        var p = SimEngine.PowersOf(s, owner);
        if (!p.Has(Name)) return;
        var left = p[Name] - 1;
        SimEngine.SetPowers(s, owner, left <= 0 ? p.Without(Name) : p.With(Name, left));
        if (left <= 0 && s.Enemy(owner) is { Alive: true } en)
        {
            en.Stunned = true;
            en.StunnedThisTurn = true;
            s.LogFired(Name, 0, owner);
        }
    }
}

/// <summary>HardToKillPower.ModifyDamageCap: at most its amount per damage to its owner; it flashes when it caps.</summary>
public sealed class HardToKillPower : SimModel
{
    public override string Name => "HardToKillPower";
    public override bool FlashesOnDamageModified => true;
    public override decimal ModifyDamageCap(SimState s, int owner, DamageCtx d) => d.Target == owner ? SimEngine.PowerOn(s, owner, Name) : decimal.MaxValue;
}

/// <summary>IntangiblePower.ModifyDamageCap: at most 1 per damage to its owner (its HP-loss cap is in the HP-loss pipeline).</summary>
public sealed class IntangiblePower : SimModel
{
    public override string Name => "IntangiblePower";
    public override bool FlashesOnDamageModified => true;
    public override decimal ModifyDamageCap(SimState s, int owner, DamageCtx d) => d.Target == owner ? 1m : decimal.MaxValue;
    /// <summary>ModifyHpLostAfterOsty: at most 1 HP per damage to its owner (it flashes when it changes one).</summary>
    public override decimal ModifyHpLostAfterOsty(SimState s, int owner, DamageCtx d, decimal amount) =>
        d.Target != owner || amount < 1m ? amount : 1m;
}

/// <summary>
/// SurroundedPower (you, Kaiser Crab): Data is the way you face (0 right, 1 left). A dealer behind you (BackAttackLeftPower
/// while you face right, BackAttackRightPower while you face left) deals 1.5×. A card of yours aimed at the crab behind you
/// turns you to face it (BeforeCardPlayed).
/// </summary>
public sealed class SurroundedPower : SimModel
{
    public override string Name => "SurroundedPower";
    private static string BehindPower(int facing) => facing == 0 ? "BackAttackLeftPower" : "BackAttackRightPower";

    public override decimal ModifyDamageMultiplicative(SimState s, int owner, DamageCtx d)
    {
        if (d.Dealer is not { } dealer || d.Target != owner) return 1m;
        return SimEngine.PowersOf(s, dealer).Has(BehindPower(SimEngine.PowersOf(s, owner).DataOf(Name))) ? 1.5m : 1m;
    }

    public override IEnumerable<SimState> BeforeCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (play.Owner != owner || play.Target < 0) return One(s);
        var p = SimEngine.PowersOf(s, owner);
        var facing = p.DataOf(Name);
        if (SimEngine.PowersOf(s, play.Target).Has(BehindPower(facing))) SimEngine.SetPowers(s, owner, p.With(Name, p[Name], 1 - facing));
        return One(s);
    }
}

/// <summary>
/// CoveredPower (on a teammate you cover with Intercept): powered attacks on its owner deal nothing; Data is its applier's
/// id + 100. Applying it gives the applier Intercept, counting one more covered player (CoveredPower.AfterApplied).
/// </summary>
public sealed class CoveredPower : SimModel
{
    public override string Name => "CoveredPower";
    public override decimal ModifyDamageMultiplicative(SimState s, int owner, DamageCtx d) =>
        d.Target == owner && d.Props.IsPoweredAttack() ? 0m : 1m;
    public override void AfterApplied(SimState s, int owner, int applier, int amount)
    {
        var p = SimEngine.PowersOf(s, owner);
        SimEngine.SetPowers(s, owner, p.With(Name, p[Name], applier + 100));
        var a = SimEngine.PowersOf(s, applier);
        SimEngine.SetPowers(s, applier, a.With("InterceptPower", a["InterceptPower"] + 1));
    }
}

/// <summary>InterceptPower.ModifyDamageMultiplicative: powered attacks on its owner deal × (players covered + 1): its amount is the count.</summary>
public sealed class InterceptPower : SimModel
{
    public override string Name => "InterceptPower";
    public override decimal ModifyDamageMultiplicative(SimState s, int owner, DamageCtx d) =>
        d.Target == owner && d.Props.IsPoweredAttack() ? SimEngine.PowerOn(s, owner, Name) + 1 : 1m;
}
