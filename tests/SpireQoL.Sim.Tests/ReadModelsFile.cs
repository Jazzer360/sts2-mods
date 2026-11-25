using System.Runtime.CompilerServices;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The simulator tests' models read from the game's code (decision #195): the game isn't here, so they come from ReadModels/read-models.json,
/// which the catalog tests write from the installed game's code and hold to it (HookProgramTests), as the game reads them in play.
/// </summary>
internal static class ReadModelsFile
{
    public static string PathOf => Path.Combine(AppContext.BaseDirectory, "ReadModels", "read-models.json");

    /// <summary>The read models in the file, as the simulator registers them.</summary>
    public static List<ModelSpec> Specs() => File.Exists(PathOf) ? HookProgramJson.Read(File.ReadAllText(PathOf)) : new List<ModelSpec>();

    /// <summary>
    /// A class whose hooks a game update made unreadable, as the reader gives it (ModelSpec.Unread): the simulator's failure path is tested on
    /// it (ReadModelFailureTests). No game class has its name.
    /// </summary>
    public static readonly ModelSpec Unreadable = new("UnreadableForTestsPower", ModelKind.Power, Array.Empty<HookProgram>())
    {
        Unread = "UnreadableForTestsPower's hooks couldn't be read from the game's code (a game update?): what it does isn't simulated",
    };

    /// <summary>A class read whole whose program the simulator can't compile (damage to a hit's target, which no read model deals): unread too.</summary>
    public static readonly ModelSpec Uncompilable = new("UncompilableForTestsPower", ModelKind.Power,
        new[] { new HookProgram(HookName.AfterCardPlayed, new HookStep[] { new DamageStep(HookTarget.HitTarget, new HookConstant(3m), new HookConstant(4m)) }) });

    /// <summary>
    /// A power that stops the HP its owner would lose to damage from a Burn (`cardSource is Burn`): what a hit's card is, as the HP-loss passes are
    /// told it (EndOfTurnModelTests). No game class has its name.
    /// </summary>
    public static readonly ModelSpec StopsBurns = new("StopsBurnsForTestsPower", ModelKind.Power,
        new[] { new HookProgram(HookName.ModifyHpLostAfterOstyLate, Array.Empty<HookStep>()) { Returns = new HookWays(new[] {
            new HookWay(new HookFactValue(HookFact.SourceCardIs) { Of = CardId.Burn.Name }, new HookConstant(0m)),
            new HookWay(new HookConstant(1m), new HookFactValue(HookFact.HpLostSoFar)) }) } });

    /// <summary>Every power class's PowerCmd rules (decision #234), from ReadModels/power-rules.json, which the catalog tests write from the installed game.</summary>
    public static List<PowerRule> Rules()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ReadModels", "power-rules.json");
        return File.Exists(path) ? HookProgramJson.ReadRules(File.ReadAllText(path)) : new List<PowerRule>();
    }

    /// <summary>
    /// The turn's-end listeners the simulator doesn't follow (decision #303), from ReadModels/turn-end-gaps.json, which the catalog tests write from
    /// the installed game.
    /// </summary>
    public static List<TurnEndGap> TurnEndGaps()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ReadModels", "turn-end-gaps.json");
        return File.Exists(path) ? HookProgramJson.ReadTurnEndGaps(File.ReadAllText(path)) : new List<TurnEndGap>();
    }

    [ModuleInitializer]
    internal static void Use()
    {
        SimModels.Read = () => Specs().Append(Unreadable).Append(Uncompilable).Append(StopsBurns);
        SimModels.ReadRules = Rules;
        SimModels.ReadTurnEndGaps = TurnEndGaps;
    }
}
