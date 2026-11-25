namespace SpireQoL.Sim;

/// <summary>
/// Potion Tags (decision #41) from a potion's simulated use (decision #318), as a card's proc tabs come from its play's log (CardTabs): the use alone
/// (SimSearch.PlayOnly: it leaves the belt, Hook.BeforePotionUsed, its body, Hook.AfterPotionUsed), and of the relics of yours that listen to a
/// potion's hooks (read from their code: Reptile Trinket's and Belt Buckle's AfterPotionUsed), the ones its log shows acting. A read model by where
/// its code flashes (its flash log, as the game shows a relic acting), a hand-written one by its acting log; a plain tag where it acts in every
/// branch, a crossed one where its effect is against you and it acts in some. Pure: the tests run the same rule the game shows.
/// </summary>
public static class PotionTabs
{
    /// <summary>
    /// The model listens to a potion's use (Hook.BeforePotionUsed or AfterPotionUsed, read from its code or written by hand): what drinking a potion
    /// can set off, and so the relics a potion's tags are chosen from.
    /// </summary>
    public static bool ListensToPotions(SimModel model) =>
        model.Overrides(nameof(SimModel.BeforePotionUsed)) || model.Overrides(nameof(SimModel.AfterPotionUsed));

    /// <summary>
    /// Of <paramref name="models"/> (your relics' models, in your relics' order), the ones the use (<paramref name="use"/>, a play-only search's
    /// outcome) sets off, in that order, each crossed or not. One the simulator doesn't model sets off nothing it can tell.
    /// </summary>
    public static List<(string Model, bool Crossed)> SetOff(SimOutcome use, IReadOnlyList<string> models)
    {
        var tabs = new List<(string, bool)>();
        foreach (var name in models)
        {
            if (SimModels.Get(name) is not { } model || !ListensToPotions(model)) continue;
            var key = SimKeys.Logged(name, owner: null);
            var flashes = model is ReadModel;
            var crossed = model.HarmfulFor(Who.You);
            var log = (flashes, crossed) switch
            {
                (true, true) => use.PlayFlashedAny,
                (true, false) => use.PlayFlashedEvery,
                (false, true) => use.PlayFiredAny,
                _ => use.PlayFiredEvery,
            };
            if (log.Has(key)) tabs.Add((name, crossed));
        }
        return tabs;
    }
}
