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
    /// <summary>Pael's Tears: good while you still have energy to carry over to next turn.</summary>
    KeepEnergy,
}

/// <param name="TypeName">The power's class name (e.g. "PanachePower").</param>
/// <param name="Threshold">Steps per cycle (from the decompile; the game shows "left" via DisplayAmount).</param>
/// <param name="Unit">Short noun for the label ("cards", "energy"...).</param>
/// <param name="OnEnemy">Lives on an enemy but counts your plays (Withering Presence).</param>
public sealed record PowerCounterSpec(string TypeName, int Threshold, string Unit, bool CrossTurn = false, bool OnEnemy = false);

/// <summary>
/// Powers that count down to an effect. The game exposes "steps left" as PowerModel.DisplayAmount.
/// Thresholds are the reset values in each class (v0.111.0).
/// </summary>
public static class PowerSpecs
{
    public static readonly IReadOnlyList<PowerCounterSpec> All = new[]
    {
        new PowerCounterSpec("OrbitPower", 4, "energy", CrossTurn: true),       // every 4 energy spent: gain energy
        new PowerCounterSpec("AutomationPower", 10, "draws", CrossTurn: true),  // every 10 cards drawn: gain energy
        new PowerCounterSpec("CacophonyPower", 33, "draws", CrossTurn: true),   // every 33 cards drawn: damage an enemy
        new PowerCounterSpec("PanachePower", 5, "cards"),                       // every 5 cards this turn: damage all enemies
        // Counts every card played this turn, despite the game's property being named "AttacksPlayedThisTurn".
        new PowerCounterSpec("PaleBlueDotPower", 5, "cards"),                   // 5 cards this turn: draw next turn (once)
        new PowerCounterSpec("WitheringPresencePower", 6, "cards", CrossTurn: true, OnEnemy: true), // every 6 cards: Wither in hand
    };

    private static readonly Dictionary<string, PowerCounterSpec> ByName = All.ToDictionary(s => s.TypeName);

    public static PowerCounterSpec? For(string typeName) => ByName.TryGetValue(typeName, out var s) ? s : null;
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
        new RelicCounterSpec("PaelsTears", CounterKind.KeepEnergy, "Energy"),
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
