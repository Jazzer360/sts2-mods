namespace SpireQoL.Sim.Models;

// Enemy powers that change their attack during your turn (decompile v0.113.1). Their intent is worked out when it lands, so a
// Strength change here is what the attack deals.

/// <summary>
/// MonarchsGazePower.AfterDamageGiven: each powered hit its owner deals takes its amount of Strength from the target for the turn
/// (MonarchsGazeStrengthDownPower: a temporary Strength loss; Artifact can take it).
/// </summary>
public sealed class MonarchsGazePower : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override void AfterDamageGiven(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Dealer != owner || !hit.Props.IsPoweredAttack() || hit.Target < 0) return;
        state.LogFired(Name, amount: 0, owner);
        SimEngine.ApplyPower(state, owner, hit.Target, PowerId.Strength, -SimEngine.PowerOn(state, owner, ThisPower), debuff: true);
    }
}

/// <summary>VitalSparkPower.AfterCardPlayed: playing a Tainted card gives its player Tainted (its amount: more damage from powered attacks).</summary>
public sealed class VitalSparkPower : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override bool Harmful => true;
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (play.Card.Affliction != SimAffliction.Tainted) return One(state);
        state.LogFired(Name, amount: 0, owner);
        SimEngine.ApplyPower(state, owner, play.Owner, PowerId.Tainted, SimEngine.PowerOn(state, owner, ThisPower), debuff: false);
        return One(state);
    }
}

/// <summary>RavenousPower (AfterDeath of an ally): it's stunned.</summary>
public sealed class RavenousPower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) => Who.IsEnemy(owner) ? OutcomeRules.KillsAnother(owner) : null;

    public override void AfterDeath(SimState state, int owner, int dead)
    {
        if (dead == owner || dead < 0 || state.Enemy(owner) is not { } enemy || enemy.Stunned) return;
        state.LogFired(Name, amount: 0, owner);
        SimEngine.Stun(enemy);
    }
}
