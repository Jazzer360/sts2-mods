namespace SpireQoL.Sim.Tests;

/// <summary>
/// A player's pets on a test board, as the adapter reads them: each a creature with its power (Die For You, Wuppy's Loyalty), which takes the
/// hits aimed at its player from its code (read from it: the read models).
/// </summary>
internal static class Pets
{
    /// <summary>An Osty with <paramref name="hp"/> (0: dead, kept in the fight with Die For You), as OstyCmd.Summon's first summon leaves him.</summary>
    public static SimPlayer WithOsty(this SimPlayer player, int hp)
    {
        player.OstyHp = hp;
        player.OstyShields = true;
        player.OstyPowers = player.OstyPowers.With(PowerId.DieForYou, 1);
        return player;
    }

    /// <summary>A Wuppy with <paramref name="hp"/> and its Wuppy's Loyalty, as the Wuppy relic's pet joins the fight.</summary>
    public static SimPlayer WithWuppy(this SimPlayer player, int hp)
    {
        player.WuppyHp = hp;
        player.WuppyPowers = player.WuppyPowers.With(PowerId.WuppysLoyalty, 1);
        return player;
    }
}
