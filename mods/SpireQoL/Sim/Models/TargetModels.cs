namespace SpireQoL.Sim.Models;

// Damage sources keyed on the target or on positions that change during your turn (decompile v0.113.1).

/// <summary>TrackingPower.ModifyDamageMultiplicative: 1 + amount/100 on the owner's (or pet's) powered hits from a card on a Weak enemy.</summary>
public sealed class TrackingPower : SimModel
{
    public override decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage)
    {
        if (!damage.Props.IsPoweredAttack() || damage.Card == null) return 1m;
        if (!(damage.Dealer == owner || (owner == Who.You && damage.Dealer == Who.YourOsty))) return 1m;
        return SimEngine.PowersOf(state, damage.Target).Has(PowerId.Weak) ? 1m + SimEngine.PowerOn(state, owner, ThisPower) / 100m : 1m;
    }
}

/// <summary>ColossusPower.ModifyDamageMultiplicative: powered attacks on its owner from a Vulnerable dealer deal its DamageDecrease (0.5).</summary>
public sealed class ColossusPower : SimModel
{
    public override decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage) =>
        damage.Target == owner && damage.Props.IsPoweredAttack() && damage.Dealer is { } dealer && SimEngine.PowersOf(state, dealer).Has(PowerId.Vulnerable) ? 0.5m : 1m;
}

/// <summary>TaintedPower.ModifyDamageAdditive: its amount on powered attacks on its owner (Vital Spark gives it for Tainted cards).</summary>
public sealed class TaintedPower : SimModel
{
    public override decimal ModifyDamageAdditive(SimState state, int owner, DamageCtx damage) =>
        damage.Target == owner && damage.Props.IsPoweredAttack() ? SimEngine.PowerOn(state, owner, ThisPower) : 0m;
}

/// <summary>
/// FlutterPower: powered attacks on its owner deal its DamageDecrease% (Data; 50 by default); each that gets HP through takes a
/// stack (AfterDamageReceived), and the last stuns it.
/// </summary>
public sealed class FlutterPower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) => OutcomeRules.Stuns(now, owner);

    public override decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage)
    {
        if (damage.Target != owner || !damage.Props.IsPoweredAttack()) return 1m;
        var pct = SimEngine.PowersOf(state, owner).DataOf(ThisPower);
        return (pct == 0 ? 50 : pct) / 100m;
    }
    public override void AfterDamageReceived(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Target != owner || hit.Unblocked == 0 || !hit.Props.IsPoweredAttack() || owner < 0) return;
        var powers = SimEngine.PowersOf(state, owner);
        if (!powers.Has(ThisPower)) return;
        var left = powers[ThisPower] - 1;
        SimEngine.SetPowers(state, owner, left <= 0 ? powers.Without(ThisPower) : powers.With(ThisPower, left));
        if (left <= 0 && state.Enemy(owner) is { Alive: true } enemy)
        {
            enemy.Stunned = true;
            enemy.StunnedThisTurn = true;
            state.LogFired(Name, amount: 0, owner);
        }
    }
}

/// <summary>HardToKillPower.ModifyDamageCap: at most its amount per damage to its owner; it flashes when it caps.</summary>
public sealed class HardToKillPower : SimModel
{
    public override bool FlashesOnDamageModified => true;
    public override decimal ModifyDamageCap(SimState state, int owner, DamageCtx damage) => damage.Target == owner ? SimEngine.PowerOn(state, owner, ThisPower) : decimal.MaxValue;
}

/// <summary>IntangiblePower.ModifyDamageCap: at most 1 per damage to its owner (its HP-loss cap is in the HP-loss pipeline).</summary>
public sealed class IntangiblePower : SimModel
{
    public override bool FlashesOnDamageModified => true;
    public override decimal ModifyDamageCap(SimState state, int owner, DamageCtx damage) => damage.Target == owner ? 1m : decimal.MaxValue;
    /// <summary>ModifyHpLostAfterOsty: at most 1 HP per damage to its owner (it flashes when it changes one).</summary>
    public override decimal ModifyHpLostAfterOsty(SimState state, int owner, DamageCtx damage, decimal amount) =>
        damage.Target != owner || amount < 1m ? amount : 1m;
}

/// <summary>
/// SurroundedPower (you, Kaiser Crab): Data is the way you face (0 right, 1 left). A dealer behind you (BackAttackLeftPower
/// while you face right, BackAttackRightPower while you face left) deals 1.5×. A card of yours aimed at the crab behind you
/// turns you to face it (BeforeCardPlayed), and so does a death that leaves every enemy on one side (AfterDeath).
/// </summary>
public sealed class SurroundedPower : SimModel
{
    /// <summary>Its Data: which way you face (the game's Facing).</summary>
    public const int FacingRight = 0, FacingLeft = 1;
    private static PowerId BehindPower(int facing) => facing == FacingRight ? PowerId.BackAttackLeft : PowerId.BackAttackRight;

    public override decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage)
    {
        if (damage.Dealer is not { } dealer || damage.Target != owner) return 1m;
        return SimEngine.PowersOf(state, dealer).Has(BehindPower(SimEngine.PowersOf(state, owner).DataOf(ThisPower))) ? 1.5m : 1m;
    }

    public override IEnumerable<SimState> BeforeCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (play.Owner != owner || play.Target < 0) return One(state);
        var powers = SimEngine.PowersOf(state, owner);
        var facing = powers.DataOf(ThisPower);
        if (SimEngine.PowersOf(state, play.Target).Has(BehindPower(facing))) SimEngine.SetPowers(state, owner, powers.With(ThisPower, powers[ThisPower], facing == FacingRight ? FacingLeft : FacingRight));
        return One(state);
    }

    /// <summary>
    /// AfterDeath of an enemy: when every hittable enemy left is on one side (all BackAttackLeftPower or all
    /// BackAttackRightPower), you turn to face the first of them if it's behind you.
    /// </summary>
    public override void AfterDeath(SimState state, int owner, int dead)
    {
        if (state.Enemy(dead) == null || !SimEngine.PowersOf(state, owner).Has(ThisPower)) return;
        var left = state.Targetable.ToList();
        if (left.Count == 0) return;
        if (!left.All(enemy => enemy.Powers.Has(PowerId.BackAttackLeft)) && !left.All(enemy => enemy.Powers.Has(PowerId.BackAttackRight))) return;
        var powers = SimEngine.PowersOf(state, owner);
        var facing = powers.DataOf(ThisPower);
        if (left[0].Powers.Has(BehindPower(facing))) SimEngine.SetPowers(state, owner, powers.With(ThisPower, powers[ThisPower], 1 - facing));
    }
}

/// <summary>
/// CoveredPower (on a teammate covered with Intercept): powered attacks on its owner deal nothing. Each Intercept played on them
/// is a copy of its own (instanced), each zeroing the same attacks; Data holds the players covering them, a bit each (CovererBit).
/// Each copy gives its applier Intercept counting its owner once (CoveredPower.AfterApplied: InterceptPower.AddCoveredCreature
/// skips a creature it already covers), so a second player's Intercept on them gives that player Intercept too.
/// </summary>
public sealed class CoveredPower : SimModel
{
    public override bool Instanced => true;

    /// <summary>A covering player's bit in Data (you: 1, teammate i: 2 &lt;&lt; i, from their ids -1, -2 - i); 0 for anyone else.</summary>
    public static int CovererBit(int player) => Who.IsPlayer(player) && -1 - player is var bit and < 30 ? 1 << bit : 0;

    public override decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage) =>
        damage.Target == owner && damage.Props.IsPoweredAttack() ? 0m : 1m;
    public override void AfterApplied(SimState state, int owner, int applier, int amount)
    {
        var powers = SimEngine.PowersOf(state, owner);
        var bit = CovererBit(applier);
        if ((powers.DataOf(ThisPower) & bit) != 0) return;
        SimEngine.SetPowers(state, owner, powers.With(ThisPower, powers[ThisPower], powers.DataOf(ThisPower) | bit));
        var otherPowers = SimEngine.PowersOf(state, applier);
        SimEngine.SetPowers(state, applier, otherPowers.With(PowerId.Intercept, otherPowers[PowerId.Intercept] + 1));
    }
}

/// <summary>InterceptPower.ModifyDamageMultiplicative: powered attacks on its owner deal × (players covered + 1): its amount is the count.</summary>
public sealed class InterceptPower : SimModel
{
    public override decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage) =>
        damage.Target == owner && damage.Props.IsPoweredAttack() ? SimEngine.PowerOn(state, owner, ThisPower) + 1 : 1m;
}
