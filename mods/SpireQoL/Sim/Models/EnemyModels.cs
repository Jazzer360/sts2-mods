namespace SpireQoL.Sim.Models;

// Enemy powers that change their attack during your turn (decompile v0.113.1). Their intent is worked out when it lands, so a
// Strength change here is what the attack deals.

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
