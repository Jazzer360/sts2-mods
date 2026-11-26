namespace SpireQoL.Sim;

// What a power's or relic's Data holds where models share a meaning (decision #143). The adapter writes these and the models read
// them; a meaning only one model has is a constant in that model. Their numbers are the ones reports and replay cases hold.

/// <summary>
/// A relic used once a turn or once a combat (RelicProc.Armed, UsedThisTurn): armed until it's used (as RelicCountersFeature.IsArmed
/// reads it), used this turn.
/// </summary>
[Flags]
public enum RelicUse { None = 0, Armed = 1, UsedThisTurn = 2 }

/// <summary>A power that acts once a turn (ProcModel.UsedThisTurn: Pale Blue Dot, Lullaby, Yah!).</summary>
[Flags]
public enum PowerUse { None = 0, UsedThisTurn = 1 }

/// <summary>
/// A creature a power's Data names (its applier): its id + 100, so 0 is no one. Osty's id (-100) would be
/// 0 too: he applies none of these powers.
/// </summary>
public static class DataCreature
{
    public static int Of(int? id) => id is { } creature && creature != Who.None ? creature + 100 : 0;
    public static int In(int data) => data == 0 ? Who.None : data - 100;
}
