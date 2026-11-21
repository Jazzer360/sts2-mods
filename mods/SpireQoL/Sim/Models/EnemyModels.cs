namespace SpireQoL.Sim.Models;

// Enemy powers that change their attack during your turn (decompile v0.113.1). Their intent is worked out when it lands, so a
// Strength change here is what the attack deals.

/// <summary>EnragePower.AfterCardPlayed: every Skill played gives its owner its amount of Strength.</summary>
public sealed class EnragePower : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override bool Harmful => true;
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (play.Card.Type != SimCardType.Skill || s.Enemy(owner) is not { Alive: true }) return One(s);
        s.LogFired(Name, 0, owner);
        SimEngine.ApplyPower(s, owner, owner, "StrengthPower", SimEngine.PowerOn(s, owner, Name), debuff: false);
        return One(s);
    }
}

/// <summary>
/// MonarchsGazePower.AfterDamageGiven: each powered hit its owner deals takes its amount of Strength from the target for the turn
/// (MonarchsGazeStrengthDownPower: a temporary Strength loss; Artifact can take it).
/// </summary>
public sealed class MonarchsGazePower : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override void AfterDamageGiven(SimState s, int owner, DamageEvent e)
    {
        if (e.Dealer != owner || !e.Props.IsPoweredAttack() || e.Target < 0) return;
        s.LogFired(Name, 0, owner);
        SimEngine.ApplyPower(s, owner, e.Target, "StrengthPower", -SimEngine.PowerOn(s, owner, Name), debuff: true);
    }
}

/// <summary>VitalSparkPower.AfterCardPlayed: playing a Tainted card gives its player Tainted (its amount: more damage from powered attacks).</summary>
public sealed class VitalSparkPower : SimModel
{
    public override TabKind? Tab => TabKind.Trigger;
    public override bool Harmful => true;
    public override IEnumerable<SimState> AfterCardPlayed(SimState s, int owner, SimCardPlay play)
    {
        if (!play.Card.Is("Tainted")) return One(s);
        s.LogFired(Name, 0, owner);
        SimEngine.ApplyPower(s, owner, play.Owner, "TaintedPower", SimEngine.PowerOn(s, owner, Name), debuff: false);
        return One(s);
    }
}

/// <summary>
/// CrabRagePower (AfterDeath of an ally on its side): it gains Strength (its amount here) and block (SimEnemy.CrabRageBlock), once.
/// </summary>
public sealed class CrabRagePower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) => Who.IsEnemy(owner) ? OutcomeRules.KillsAnother(owner) : null;

    public override void AfterDeath(SimState s, int owner, int dead)
    {
        if (dead == owner || dead < 0 || s.Enemy(owner) is not { Alive: true } e || e.Intent == null) return;
        s.LogFired(Name, 0, owner);
        var strength = e.Powers[Name];
        e.Powers = e.Powers.Without(Name).Add("StrengthPower", strength);
        e.Block += e.CrabRageBlock;
    }
}

/// <summary>RavenousPower (AfterDeath of an ally): it's stunned.</summary>
public sealed class RavenousPower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) => Who.IsEnemy(owner) ? OutcomeRules.KillsAnother(owner) : null;

    public override void AfterDeath(SimState s, int owner, int dead)
    {
        if (dead == owner || dead < 0 || s.Enemy(owner) is not { Alive: true } e || e.Stunned) return;
        s.LogFired(Name, 0, owner);
        SimEngine.Stun(e);
    }
}
