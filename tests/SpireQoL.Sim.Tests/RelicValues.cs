namespace SpireQoL.Sim.Tests;

/// <summary>
/// A relic modelled from its code on a player, with one of its values as the adapter reads it from the game (SimState.Vars "id:Relic:Key",
/// SimReaders.AddVars): Orichalcum's Block, Screaming Flagon's and Charon's Ashes' Damage. Its own code reads the value, not a counter.
/// </summary>
internal static class RelicValues
{
    public static SimState WithRelic(this SimState state, int owner, RelicId relic, string key, int value)
    {
        var player = state.MutablePlayer(owner);
        player.Relics = player.Relics.With(relic, amount: 0);
        state.Vars[$"{owner}:{relic.Name}:{key}"] = value;
        return state;
    }
}
