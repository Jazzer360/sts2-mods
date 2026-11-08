namespace SpireQoL.Data;

public enum CounterKind
{
    /// <summary>Counts up to a threshold, then fires. Flash when the game marks it Active.</summary>
    Counter,
    /// <summary>A limit you shouldn't exceed (Velvet Choker). Warn as you approach it.</summary>
    Limit,
    /// <summary>Good while under a threshold (Pocketwatch). No flash; turns red when exceeded.</summary>
    StayUnder,
    /// <summary>Fires on a specific turn (Horn Cleat turn 2). Shown until that turn.</summary>
    OnTurn,
    /// <summary>Armed until first use this combat or turn.</summary>
    OneShot,
}

/// <param name="TypeName">The relic's class name in the game (e.g. "PenNib").</param>
/// <param name="ThresholdVar">DynamicVars key holding the threshold, or null to use Fixed.</param>
/// <param name="CrossTurn">Keeps counting across turns/combats: always shown, sorted first.</param>
/// <param name="InTray">Show a row in the Relic Counters tray. Off for one-shot relics whose
/// on-card icon (Relic Reminders) already says everything.</param>
public sealed record RelicCounterSpec(
    string TypeName,
    CounterKind Kind,
    string? ThresholdVar = null,
    int Fixed = 0,
    bool CrossTurn = false,
    bool InTray = true);

/// <summary>
/// Every relic the Relic Counters tray knows about. Thresholds are read live from the relic's
/// DynamicVars, so balance patches are picked up automatically. Source: docs/TRACKER.md §2–3.
/// </summary>
public static class RelicSpecs
{
    public static readonly IReadOnlyList<RelicCounterSpec> All = new[]
    {
        // Counters that carry across turns and combats.
        new RelicCounterSpec("PenNib", CounterKind.Counter, Fixed: 10, CrossTurn: true),
        new RelicCounterSpec("Nunchaku", CounterKind.Counter, "Cards", CrossTurn: true),
        new RelicCounterSpec("TuningFork", CounterKind.Counter, "Cards", CrossTurn: true),
        new RelicCounterSpec("IronClub", CounterKind.Counter, "Cards", CrossTurn: true),
        new RelicCounterSpec("GalacticDust", CounterKind.Counter, "Stars", CrossTurn: true),
        new RelicCounterSpec("JossPaper", CounterKind.Counter, "ExhaustAmount", CrossTurn: true),
        new RelicCounterSpec("StoneCalendar", CounterKind.Counter, "DamageTurn", CrossTurn: true),
        new RelicCounterSpec("HappyFlower", CounterKind.Counter, "Turns", CrossTurn: true),
        new RelicCounterSpec("FakeHappyFlower", CounterKind.Counter, "Turns", CrossTurn: true),
        new RelicCounterSpec("Pendulum", CounterKind.Counter, "Turns", CrossTurn: true),
        new RelicCounterSpec("PollinousCore", CounterKind.Counter, "Turns", CrossTurn: true),
        new RelicCounterSpec("Metronome", CounterKind.Counter, "OrbCount", CrossTurn: true),

        // Per-turn counters.
        new RelicCounterSpec("Kunai", CounterKind.Counter, "Cards"),
        new RelicCounterSpec("Shuriken", CounterKind.Counter, "Cards"),
        new RelicCounterSpec("OrnamentalFan", CounterKind.Counter, "Cards"),
        new RelicCounterSpec("Kusarigama", CounterKind.Counter, "Cards"),
        new RelicCounterSpec("LetterOpener", CounterKind.Counter, "Cards"),
        new RelicCounterSpec("BrilliantScarf", CounterKind.Counter, "Cards"),
        new RelicCounterSpec("VelvetChoker", CounterKind.Limit, "Cards"),
        new RelicCounterSpec("Pocketwatch", CounterKind.StayUnder, "CardThreshold"),

        // Fire on a specific turn.
        new RelicCounterSpec("HornCleat", CounterKind.OnTurn, Fixed: 2),
        new RelicCounterSpec("Candelabra", CounterKind.OnTurn, Fixed: 2),
        new RelicCounterSpec("CaptainsWheel", CounterKind.OnTurn, Fixed: 3),
        new RelicCounterSpec("Chandelier", CounterKind.OnTurn, Fixed: 3),
        new RelicCounterSpec("SparklingRouge", CounterKind.OnTurn, Fixed: 3),

        // One-shot: armed until first use. Only Pael's Eye gets a tray row (it has no card to
        // mark); the rest are shown on the cards that would use them (Relic Reminders).
        new RelicCounterSpec("Vambrace", CounterKind.OneShot, InTray: false),
        new RelicCounterSpec("ThrowingAxe", CounterKind.OneShot, InTray: false),
        new RelicCounterSpec("BurningSticks", CounterKind.OneShot, InTray: false),
        new RelicCounterSpec("UnsettlingLamp", CounterKind.OneShot, InTray: false),
        new RelicCounterSpec("PaelsEye", CounterKind.OneShot),
        new RelicCounterSpec("Permafrost", CounterKind.OneShot, InTray: false),
        new RelicCounterSpec("MusicBox", CounterKind.OneShot, InTray: false),
    };

    private static readonly Dictionary<string, RelicCounterSpec> ByName = All.ToDictionary(s => s.TypeName);

    public static RelicCounterSpec? For(string typeName) => ByName.TryGetValue(typeName, out var s) ? s : null;
}
