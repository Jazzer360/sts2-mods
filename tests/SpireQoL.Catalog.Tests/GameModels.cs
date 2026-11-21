using MegaCrit.Sts2.Core.Models;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Loads the installed game's models (ModelDb) the way the game does at startup (OneTimeInitialization: ModelDb.Init), but from
/// an explicit list of every concrete model type in the game's assembly: the game's own list needs its mod manager. Ids aren't
/// assigned (ModelDb.InitIds needs the multiplayer id cache); nothing here reads them. Once per test run.
/// </summary>
internal static class GameModels
{
    private static readonly Lazy<int> Loaded = new(() =>
    {
        var types = typeof(AbstractModel).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && !type.ContainsGenericParameters && typeof(AbstractModel).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) != null)
            .ToArray();
        foreach (var type in types)
        {
            try { ModelDb.Init(new[] { type }); }
            catch { /* a model that can't be made outside the game: left out */ }
        }
        // No mods loaded (what the game's mod manager reports when mods are skipped), so lists that add mods' models work.
        typeof(MegaCrit.Sts2.Core.Modding.ModManager).GetProperty("State")!
            .SetValue(null, MegaCrit.Sts2.Core.Modding.ModManagerState.Skipped);
        return ModelDb.AllCards.Count();
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Loads the models (once) and returns how many cards the game has.</summary>
    public static int EnsureLoaded() => Loaded.Value;
}
