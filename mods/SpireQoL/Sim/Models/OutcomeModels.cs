namespace SpireQoL.Sim.Models;

// Tabs judged on how the turn ends (SimModel.OutcomeTab): the rules they share, and the sources whose only part in the
// simulation is that tab (what they do happens in the engine, or after the turn).

/// <summary>The rules tabs judged on how the turn ends share (SimModel.OutcomeTab).</summary>
internal static class OutcomeRules
{
    /// <summary>The play kills this enemy in some branch: a warning, as its death sets something off.</summary>
    public static OutcomeRule KillsIt(int enemy) => new(Harmful: true, outcome => outcome.KilledByPlayInAny.Contains(enemy), branch => branch.Enemy(enemy) is not { Died: false });

    /// <summary>The play kills another enemy in some branch: a warning, as that makes this one stronger.</summary>
    public static OutcomeRule KillsAnother(int enemy) => new(Harmful: true, outcome => outcome.KilledByPlayInAny.Any(killed => killed != enemy));

    /// <summary>The play kills some enemy in every branch of an aim (not always the same one): a bonus.</summary>
    public static readonly OutcomeRule KillsOne = new(Harmful: false, outcome => outcome.PlayKillsInAll, InSome: outcome => outcome.KilledByPlayInAny.Count > 0);

    /// <summary>
    /// Stunned during your turn and alive when its turn comes, in every branch of an aim, when it isn't already without the play: a
    /// bonus. Null for anything but an enemy.
    /// </summary>
    public static OutcomeRule? Stuns(SimOutcome now, int enemy) =>
        Who.IsEnemy(enemy) && !now.StunnedInAll(enemy) ? new(Harmful: false, outcome => outcome.StunnedInAll(enemy)) : null;
}

/// <summary>An enemy's power whose death sets something off: a warning on a play that can kill it.</summary>
public abstract class DeathSetsOff : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) => Who.IsEnemy(owner) ? OutcomeRules.KillsIt(owner) : null;
}

// Infested, Stock and Steam Eruption are read from their code (ReadModel, decision #276).

/// <summary>
/// SurprisePower: its death springs the surprise (AfterDeath: CreatureCmd.Add of a Sneaky Gremlin and a Fat Gremlin, whose HP is a
/// roll): their creatures (SimEnemy.Spawns) join once the hit is over (SimEngine.Spawn), so later hits and random targets count them.
/// </summary>
public sealed class SurprisePower : DeathSetsOff
{
    public override void AfterDeath(SimState state, int owner, int dead)
    {
        if (dead != owner || state.Enemy(owner)?.Spawns is not { } spawns) return;
        var ordinal = 0;
        foreach (var spawn in spawns)
            if (spawn.Source == ThisPower) state.PendingSpawns = state.PendingSpawns.Add(new PendingSpawn(owner, ThisPower, ordinal++));
    }

    /// <summary>SurprisePower.ShouldStopCombatFromEnding: true (its gremlins come in).</summary>
    public override bool ShouldStopCombatFromEnding(SimState state, int owner) => true;
}

// Imbalanced is read from its code (ReadModel, decision #271).

// Hand Drill is read from its code (ReadModel: AfterBlockBroken, decision #285).

// Compost is read from its code (ReadModel, decision #277).

// Rupture is read from its code (ReadModel, decision #243).

/// <summary>
/// RedSkull: Strength once your HP is at or below its threshold (AfterCurrentHpChanged). The adapter's amount is the HP at the
/// threshold; a bonus when every branch of an aim takes you there from above it.
/// </summary>
public sealed class RedSkull : SimModel
{
    /// <summary>Its Data once its Strength is given (RedSkull.StrengthApplied); 0 before.</summary>
    public const int StrengthGiven = 1;

    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner)
    {
        if (owner != Who.You) return null;
        var hp = start.You.Hp;
        var threshold = SimEngine.RelicsOf(start, owner)[ThisRelic];
        return hp > threshold ? new(Harmful: false, outcome => outcome.PlayHpLostMin > 0 && hp - outcome.PlayHpLostMin <= threshold) : null;
    }

    /// <summary>
    /// RedSkull.AfterCurrentHpChanged (ModifyStrengthIfNecessary): HP lost to its threshold or below (amount) gives its Strength at once,
    /// once (StrengthApplied: Data), so a hit after it in the same play has it (Hemokinesis' own HP cost, then its attack). A dev review
    /// report: Hemokinesis at 42/80 hit for 28, predicted 24.
    /// </summary>
    public override void AfterDamageReceived(SimState state, int owner, DamageEvent hit)
    {
        if (hit.Target != owner || hit.Unblocked <= 0 || !Who.IsPlayer(owner) || state.Player(owner) is not { } player || player.Hp == int.MaxValue) return;
        var relics = SimEngine.RelicsOf(state, owner);
        if (relics.DataOf(ThisRelic) != 0 || player.Hp > relics[ThisRelic]) return;
        SimEngine.SetRelics(state, owner, relics.With(ThisRelic, relics[ThisRelic], StrengthGiven));
        SimEngine.ApplyInHit(state, owner, owner, PowerId.Strength, SimEngine.Var(state, owner, Name, "StrengthPower", fallback: 3), debuff: false);
    }
}
