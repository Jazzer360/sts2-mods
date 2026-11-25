namespace SpireQoL.Sim.Models;

// Damage sources keyed on the target or on positions that change during your turn (decompile v0.113.1).

// Flutter is read from its code (ReadModel, decision #245).

// Hard to Kill and Intangible are read from their code (ReadModel, decision #229).

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
