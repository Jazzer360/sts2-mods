namespace SpireQoL.Sim.Models;

// Enemy powers that change their attack during your turn (decompile v0.113.1). Their intent is worked out when it lands, so a
// Strength change here is what the attack deals.

// Ravenous is read from its code (ReadModel, decision #251).

/// <summary>
/// SandpitPower (The Insatiable's: one copy on it for each player, aimed at that player, each with its own countdown: decision #321). Its
/// AfterSideTurnStartLate, as the enemies' side's turn starts: PowerCmd.Decrement of each copy. A copy at 0 goes (ShouldRemoveDueToAmount), and its
/// AfterRemoved, unless its holder or the player it's aimed at is dead already, kills that player and their Osty (CreatureCmd.Kill with force: no
/// Fairy in a Bottle or Lizard Tail) before any enemy moves. Its other hooks only move creatures on screen and change the music. Hand-written
/// (decision #321): the reader reads its countdown but not its AfterRemoved (a loop over its target's creatures, a property of its own) or its
/// AfterApplied (a field only its looks read).
/// </summary>
public sealed class SandpitPower : SimModel
{
    /// <summary>One copy per player it's aimed at (PowerInstanceType.Instanced), each counting down on its own (SimEngine.AimedAt).</summary>
    public override bool Instanced => true;

    public override IEnumerable<SimState> AfterSideTurnStartLate(SimState state, int owner)
    {
        // if (side == CombatSide.Enemy) await PowerCmd.Decrement(this): each copy, the copies applied one per player in player order.
        if (!state.EnemiesSideNow) return One(state);
        IEnumerable<SimState> branches = One(state);
        foreach (var player in SimEngine.AimedAt(state, owner, ThisPower))
            branches = branches.SelectMany(branch => SimEngine.ModifyAimedCopy(branch, owner, ThisPower, player, offset: -1, applier: null)).ToList();
        return branches;
    }

    public override void AfterAimedCopyRemoved(SimState state, int owner, int aimedAt, int amount)
    {
        // if (oldOwner.IsDead || base.Target.IsDead) return; then each of its target's creatures that's a player or an Osty, killed with force.
        if (state.Enemy(owner) is not { Alive: true } || state.Player(aimedAt) is not { Hp: > 0, Died: false } player) return;
        SimEngine.KillPlayer(state, aimedAt, forced: true);
        if (player.OstyHp > 0) SimEngine.OstyLoses(state, aimedAt, player.OstyHp);
    }
}
