namespace SpireQoL.Sim.Models;

// Tabs judged on how the turn ends (SimModel.OutcomeTab): the rules they share, and the sources whose only part in the
// simulation is that tab (what they do happens in the engine, or after the turn).

/// <summary>The rules tabs judged on how the turn ends share (SimModel.OutcomeTab).</summary>
internal static class OutcomeRules
{
    /// <summary>The play kills this enemy in some branch: a warning, as its death sets something off.</summary>
    public static OutcomeRule KillsIt(int enemy) => new(true, o => o.KilledByPlayInAny.Contains(enemy));

    /// <summary>The play kills another enemy in some branch: a warning, as that makes this one stronger.</summary>
    public static OutcomeRule KillsAnother(int enemy) => new(true, o => o.KilledByPlayInAny.Any(k => k != enemy));

    /// <summary>The play kills some enemy in every branch of an aim (not always the same one): a bonus.</summary>
    public static readonly OutcomeRule KillsOne = new(false, o => o.PlayKillsInAll);

    /// <summary>You lose HP during the play (its own HP cost, Thorns hitting back), in every branch of an aim: a bonus.</summary>
    public static readonly OutcomeRule YouLoseHp = new(false, o => o.PlayHpLossesMin > 0);

    /// <summary>
    /// Stunned during your turn and alive when its turn comes, in every branch of an aim, when it isn't already without the play: a
    /// bonus. Null for anything but an enemy.
    /// </summary>
    public static OutcomeRule? Stuns(SimOutcome now, int enemy) =>
        Who.IsEnemy(enemy) && !now.StunnedInAll(enemy) ? new(false, o => o.StunnedInAll(enemy)) : null;
}

/// <summary>An enemy's power whose death sets something off: a warning on a play that can kill it.</summary>
public abstract class DeathSetsOff : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) => Who.IsEnemy(owner) ? OutcomeRules.KillsIt(owner) : null;
}

/// <summary>InfestedPower: its death releases what infests it.</summary>
public sealed class InfestedPower : DeathSetsOff { }

/// <summary>SteamEruptionPower: its death erupts.</summary>
public sealed class SteamEruptionPower : DeathSetsOff { }

/// <summary>SurprisePower: its death springs the surprise.</summary>
public sealed class SurprisePower : DeathSetsOff { }

/// <summary>ExplosivePower: its death damages every living player (the engine's: SimEnemy.Explosive).</summary>
public sealed class ExplosivePower : DeathSetsOff { }

/// <summary>StockPower: while stock is left, a new Axebot takes its place when it dies (and attacks this enemy turn).</summary>
public sealed class StockPower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) =>
        Who.IsEnemy(owner) && SimEngine.PowerOn(start, owner, ThisPower) > 0 ? OutcomeRules.KillsIt(owner) : null;
}

/// <summary>
/// ImbalancedPower: stunned when its first hit on you is fully blocked (the engine: SimEnemy.FirstHitFullyBlocked). A bonus when the
/// play makes every branch of an aim block it, and ending the turn now wouldn't.
/// </summary>
public sealed class ImbalancedPower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) =>
        Who.IsEnemy(owner) && !now.FirstHitBlockedInAll(owner) ? new(false, o => o.FirstHitBlockedInAll(owner)) : null;
}

/// <summary>HandDrill: Vulnerable on an enemy whose block you break. A bonus when every branch of an aim breaks one.</summary>
public sealed class HandDrill : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) =>
        owner == Who.You ? new(false, o => o.PlayBreaksBlockInAll) : null;
}

/// <summary>CompostPower (yours or a teammate's): energy for every player on your side when an enemy dies.</summary>
public sealed class CompostPower : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) => Who.IsPlayer(owner) ? OutcomeRules.KillsOne : null;
}

/// <summary>A relic or power of yours that reacts to HP you lose (AfterDamageReceived with UnblockedDamage &gt; 0, after Tungsten Rod).</summary>
public abstract class ReactsToYourHpLoss : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) => owner == Who.You ? OutcomeRules.YouLoseHp : null;
}

/// <summary>SelfFormingClay: block next turn.</summary>
public sealed class SelfFormingClay : ReactsToYourHpLoss { }

/// <summary>EmotionChip: the front orb's passive next turn.</summary>
public sealed class EmotionChip : ReactsToYourHpLoss { }

/// <summary>RupturePower: Strength when you lose HP on your turn.</summary>
public sealed class RupturePower : ReactsToYourHpLoss { }

/// <summary>DemonTongue: heals the HP you lose, once a turn (the adapter's Data bit 2: done this turn).</summary>
public sealed class DemonTongue : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) =>
        owner == Who.You && (SimEngine.RelicsOf(start, owner).DataOf(ThisRelic) & 2) == 0 ? OutcomeRules.YouLoseHp : null;
}

/// <summary>
/// RedSkull: Strength once your HP is at or below its threshold (AfterCurrentHpChanged). The adapter's amount is the HP at the
/// threshold; a bonus when every branch of an aim takes you there from above it.
/// </summary>
public sealed class RedSkull : SimModel
{
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner)
    {
        if (owner != Who.You) return null;
        var hp = start.You.Hp;
        var threshold = SimEngine.RelicsOf(start, owner)[ThisRelic];
        return hp > threshold ? new(false, o => o.PlayHpLostMin > 0 && hp - o.PlayHpLostMin <= threshold) : null;
    }
}
