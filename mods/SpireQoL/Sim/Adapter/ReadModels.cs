using System.Reflection;
using SpireQoL.Procs.Programs;

namespace SpireQoL.Sim;

/// <summary>
/// The powers and relics the simulator models from their own code (decision #195): each one's hooks read from the running game (HookReader)
/// once, when the simulator first asks for a model (SimModels.Read), and run by ReadModel. A main player gets main's code.
/// </summary>
public static class ReadModels
{
    /// <summary>
    /// The classes modelled from their code: each joined once its read programs were proven equal to the hand-written model they replaced, on
    /// the same scenarios (ReadModelParityTests), the snapshots and the single-player golden, and that model was deleted. A class here whose
    /// hooks a game update makes unreadable is logged and left unmodelled, never guessed (HookProgramTests fails it first on the installed game).
    /// </summary>
    internal static readonly IReadOnlySet<string> Classes = new HashSet<string>(StringComparer.Ordinal)
    {
        // The damage and block math (Modify hooks): their returns as expressions.
        PowerId.Strength.Name, PowerId.Dexterity.Name, PowerId.Frail.Name, PowerId.DoubleDamage.Name,
        // What a card played, a card event or a power landing sets off: block, damage, draws, a power.
        PowerId.AllIsWell.Name, PowerId.Arsenal.Name, PowerId.CorrosiveWave.Name, PowerId.DanseMacabre.Name, PowerId.FeelNoPain.Name, PowerId.PillarOfCreation.Name,
        PowerId.Rage.Name, PowerId.Shroud.Name, PowerId.SleightOfFlesh.Name, PowerId.Smokestack.Name, PowerId.Speedster.Name, PowerId.SpiritOfAsh.Name,
        PowerId.Vicious.Name,
        RelicId.CharonsAshes.Name, RelicId.DaughterOfTheWind.Name, RelicId.Fertilizer.Name, RelicId.GamePiece.Name, RelicId.LostWisp.Name, RelicId.ToughBandages.Name,
    };

    /// <summary>The read models of the running game's classes, for SimModels.</summary>
    public static IEnumerable<ModelSpec> Specs() => Specs(Diagnostics.CoverageLedger.ModelTypes(), RuntimeCode.Read, warn: message => ModEntry.Log.Warn(message));

    /// <summary>The read models of <paramref name="types"/> (a branch's), each read with <paramref name="read"/>; one not read whole is left out.</summary>
    public static List<ModelSpec> Specs(IEnumerable<Type> types, Func<MethodBase, List<CodeOp>> read, Action<string>? warn = null)
    {
        var all = types.ToList();
        var specs = new List<ModelSpec>();
        foreach (var type in HookReader.Powers(all).Concat(HookReader.Relics(all)).Where(type => Classes.Contains(type.Name)).OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            var reading = HookReader.Read(type, read);
            if (reading.Whole) specs.Add(reading.Spec);
            else warn?.Invoke($"[sim] {type.Name} isn't modelled: its hooks aren't read whole ({string.Join("; ", reading.Gaps)})");
        }
        return specs;
    }
}
