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

    [ModuleInitializer]
    internal static void Use() => SimModels.Read = Specs;
}
