namespace SpireQoL.Sim;

/// <summary>
/// A power counting its amount down to a payout (decision #254: The Bomb), as its Relic Counters row says it: the turns left, this one counted,
/// from its amount and its last tick read from its code (ReadModel.CountdownAt). Pure, so its tests run what the tray shows.
/// </summary>
public static class PowerCountdown
{
    /// <summary>The turns until it pays, this one counted: it pays as the turn ends where its amount is at its last tick.</summary>
    public static int TurnsLeft(int amount, int lastTick) => Math.Max(1, amount - lastTick + 1);

    /// <summary>The row's label: "this turn" on its last, else the turns left; what it deals.</summary>
    public static string Label(int turnsLeft, int damage) => turnsLeft <= 1 ? $"{damage} to all this turn" : $"{damage} to all in {turnsLeft} turns";
}
