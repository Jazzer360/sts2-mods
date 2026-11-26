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
        MakeLists();
        // The relics' rules, read with the game's cards as the load's worker reads them once the game has made its models (decision #359).
        SpireQoL.Data.RelicReadings.ReadAll();
        return ModelDb.AllCards.Count();
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Every pool's list made here, on one thread, as the game's startup does (ModelDb.Preload): a pool makes its list the first time it's asked
    /// (CardPoolModel.AllCards, RelicPoolModel.AllRelics, PotionPoolModel.AllPotions), and each first time writes the mods' table of pool content
    /// (ModHelper.ConcatModelsFromMods: a plain Dictionary). Two tests asking for two pools' lists at once, on two threads, corrupted it ("Operations
    /// that change non-concurrent collections must have exclusive access"), and every test that read a pool after that failed: the catalog run's
    /// rare failure. ModelDb's own lists (AllRelics, AllPotions...) are made here too, so none is first made by two tests at once.
    /// </summary>
    private static void MakeLists()
    {
        foreach (var model in ModelDb.All.ToList())
        {
            try
            {
                switch (model)
                {
                    case CardPoolModel cards: _ = cards.AllCards; break;
                    case RelicPoolModel relics: _ = relics.AllRelics; break;
                    case PotionPoolModel potions: _ = potions.AllPotions; break;
                }
            }
            catch { /* a pool that can't make its list outside the game: asked again by the test that needs it */ }
        }
        foreach (var list in new Func<object>[]
                 {
                     () => ModelDb.AllCards.ToList(), () => ModelDb.AllCardPools.ToList(), () => ModelDb.AllRelicPools.ToList(), () => ModelDb.AllRelics.ToList(), () => ModelDb.AllPotions.ToList(),
                     () => ModelDb.AllPotionPools.ToList(), () => ModelDb.AllPowers.ToList(), () => ModelDb.AllEvents.ToList(), () => ModelDb.AllSharedEvents.ToList(),
                     () => ModelDb.AllEncounters.ToList(), () => ModelDb.AllCharacters.ToList(), () => ModelDb.AllAbstractModelSubtypes,
                 })
        {
            try { _ = list(); }
            catch { /* made by the test that needs it */ }
        }
    }

    /// <summary>Loads the models (once) and returns how many cards the game has.</summary>
    public static int EnsureLoaded() => Loaded.Value;
}
