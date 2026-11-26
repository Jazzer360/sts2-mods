namespace SpireQoL.Sim.Models;

// Enemy powers that change their attack during your turn (decompile v0.113.1). Their intent is worked out when it lands, so a
// Strength change here is what the attack deals.

// Ravenous is read from its code (ReadModel, decision #251).

/// <summary>
/// PossessStrengthPower and PossessSpeedPower (The Lost's, decision #326): AfterPowerAmountChanged counts what its owner took of the power from each
/// player (an application by its owner, on a player, of less than 0: stolen[player] += amount), and AfterDeath, as its owner dies, gives each player
/// back what was taken (PowerCmd.Apply of -stolen, no applier). Hand-written: the reader doesn't read a dictionary of its own data keyed by creature,
/// nor a loop over it; the adapter reads each player's entry into the fields (CreatureKeyedData).
/// </summary>
public abstract class PossessPowerModel : SimModel
{
    /// <summary>The power it takes and gives back (Strength, Dexterity).</summary>
    protected abstract PowerId Taken { get; }

    /// <summary>Its own data's dictionary of what it took from each player.</summary>
    protected abstract string StolenData { get; }

    public override IReadOnlyList<string> CreatureKeyedData => new[] { StolenData };

    public override IEnumerable<SimState> AfterPowerApplied(SimState state, int owner, PowerId power, int amount, int applier, int target, bool debuff)
    {
        // applier == base.Owner, power.Owner.IsPlayer, power is the one it takes, amount < 0: StolenX[power.Owner] += amount.
        if (applier != owner || !Who.IsPlayer(target) || power != Taken || amount >= 0) return One(state);
        var key = SimKeys.PerCreature(Name, StolenData, target);
        SimEngine.SetField(state, owner, key, SimEngine.FieldsOf(state, owner)[key] + amount);
        return One(state);
    }

    public override void AfterDeath(SimState state, int owner, int dead)
    {
        // creature != base.Owner returns; then PowerCmd.Apply(item.Key, -item.Value, applier: null) for each player it took from, in the fight's order.
        if (dead != owner) return;
        foreach (var player in state.PlayerIds)
        {
            var stolen = SimEngine.FieldsOf(state, owner)[SimKeys.PerCreature(Name, StolenData, player)];
            if (stolen != 0) SimEngine.ApplyInHit(state, Who.None, player, Taken, -stolen, debuff: -stolen < 0);
        }
    }
}

/// <summary>PossessStrengthPower: the Strength The Lost takes from each player, back as it dies.</summary>
public sealed class PossessStrengthPower : PossessPowerModel
{
    protected override PowerId Taken => PowerId.Strength;
    protected override string StolenData => "stolenStrength";
}

/// <summary>PossessSpeedPower: the Dexterity its owner takes from each player, back as it dies.</summary>
public sealed class PossessSpeedPower : PossessPowerModel
{
    protected override PowerId Taken => PowerId.Dexterity;
    protected override string StolenData => "stolenDexterity";
}

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
