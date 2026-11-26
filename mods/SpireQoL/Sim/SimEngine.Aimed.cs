namespace SpireQoL.Sim;

/// <summary>
/// Copies of a power aimed at a player (decision #321): the game keeps one instance per player some powers are aimed at (PowerModel.Target set by
/// whoever makes it: The Insatiable's Sandpit, one per player; a Player its class keeps: Imitation Learning's PlayerTarget), each with its own amount.
/// The holder's power entry keeps every copy's amount together, as before; each copy's own amount is in the holder's fields under the player it's
/// aimed at (SimKeys.Aimed), read by the adapter (SimReaders.Fields), so a card or a model can ask for the copy aimed at one player and change it alone.
/// </summary>
public static partial class SimEngine
{
    /// <summary>The amount of the holder's copy of the power aimed at the player; 0 when it has none.</summary>
    public static int AimedAmount(SimState state, int holder, PowerId power, int player) =>
        PowersOf(state, holder).Has(power) ? FieldsOf(state, holder)[SimKeys.Aimed(power.Name, player)] : 0;

    /// <summary>The players the holder's copies of the power are aimed at, in player order.</summary>
    public static List<int> AimedAt(SimState state, int holder, PowerId power)
    {
        var aimedAt = new List<int>();
        if (!PowersOf(state, holder).Has(power)) return aimedAt;
        var fields = FieldsOf(state, holder);
        foreach (var player in state.PlayerIds)
            if (fields[SimKeys.Aimed(power.Name, player)] != 0) aimedAt.Add(player);
        return aimedAt;
    }

    /// <summary>
    /// A new copy of the power on the holder aimed at the player, as it landed (PowerCmd.Apply, then its aim set: Imitation Learning's PlayerTarget): its
    /// amount, the entry's already counted by the application.
    /// </summary>
    internal static void AimNewCopy(SimState state, int holder, PowerId power, int player, int amount)
    {
        if (amount == 0 || !PowersOf(state, holder).Has(power)) return;
        var key = SimKeys.Aimed(power.Name, player);
        SetField(state, holder, key, FieldsOf(state, holder)[key] + amount);
    }

    /// <summary>
    /// PowerCmd.ModifyAmount of the holder's copy of the power aimed at the player (decision #321): the offset through Hook.ModifyPowerAmountGiven when
    /// there's an applier (AmountGiven), the copy's amount and the holder's entry (every copy's together) changed, the fight's log told
    /// (PowerReceivedEntry), Hook.AfterPowerAmountChanged's listeners told (AfterPowerApplied) when it changed; then the copy goes once
    /// ShouldRemoveDueToAmount says so (PowerCmd.Remove: that copy's AfterRemoved, SimModel.AfterAimedCopyRemoved), the entry with the last copy.
    /// Nothing without such a copy.
    /// </summary>
    internal static IEnumerable<SimState> ModifyAimedCopy(SimState state, int holder, PowerId power, int player, int offset, int? applier)
    {
        var had = AimedAmount(state, holder, power, player);
        if (had == 0) return new[] { state };
        var debuff = SimModels.IsDebuffAt(power, had);
        var amount = applier is { } giver ? AmountGiven(state, giver, holder, power, offset, debuff) : offset;
        if (state.History.Count > 0) Record(state, new HistoryEvent(HistoryKind.PowerReceived, applier ?? Who.None) { Power = power });
        var left = had + amount;
        var key = SimKeys.Aimed(power.Name, player);
        SetField(state, holder, key, left);
        var powers = PowersOf(state, holder);
        SetPowers(state, holder, powers.With(power, powers[power] + amount));
        var told = amount != 0 ? Tell(state, power, amount, applier ?? Who.None, holder, debuff).ToList() : new List<SimState> { state };
        var gone = SimModels.AllowsNegative(power) ? left == 0 : left <= 0;
        if (!gone) return told;
        foreach (var branch in told) RemoveAimedCopy(branch, holder, power, player, left);
        return told;
    }

    /// <summary>
    /// The creature a play's aimed copies are on (decision #322): the play's owner, or the first enemy with the power (CombatState.Enemies'
    /// FirstOrDefault of HasPower: a dead one has none); Who.None when there's none.
    /// </summary>
    internal static int AimHolderOf(SimState state, AimHolder holder, PowerId? holderPower)
    {
        if (holder == AimHolder.PlayOwner) return state.CurrentPlay?.Owner ?? Who.You;
        if (holderPower is not { } power) return Who.None;
        foreach (var enemy in state.Enemies)
            if (enemy.Alive && enemy.Powers.Has(power)) return enemy.Id;
        return Who.None;
    }

    /// <summary>The player a play names as the one a copy is aimed at (decision #322): its owner, or its target when that's a player; Who.None else.</summary>
    internal static int AimedPlayerOf(SimState state, AimedPlayer aim)
    {
        if (aim == AimedPlayer.PlayOwner) return state.CurrentPlay?.Owner ?? Who.You;
        return state.CurrentPlay?.Target is { } target && Who.IsPlayer(target) ? target : Who.None;
    }

    /// <summary>A ModifyAimedPowerAction: the holder's copy aimed at the player changed, its applier the holder or the play's owner (ModifyAimedCopy).</summary>
    private static IEnumerable<SimState> RunModifyAimed(SimState state, ModifyAimedPowerAction modify)
    {
        var holder = AimHolderOf(state, modify.Holder, modify.HolderPower);
        var player = AimedPlayerOf(state, modify.Aim);
        if (holder == Who.None || player == Who.None) return new[] { state };
        return ModifyAimedCopy(state, holder, modify.Power, player, modify.Amount, modify.AppliedByHolder ? holder : state.CurrentPlay?.Owner ?? Who.You);
    }

    /// <summary>
    /// PowerCmd.Remove of the holder's copy aimed at the player: off the holder (RemoveInternal: the entry loses its amount, gone with the last copy),
    /// then that copy's AfterRemoved told to it alone (SimModel.AfterAimedCopyRemoved), its amount as it had it.
    /// </summary>
    private static void RemoveAimedCopy(SimState state, int holder, PowerId power, int player, int amount)
    {
        var key = SimKeys.Aimed(power.Name, player);
        if (!FieldsOf(state, holder).Has(key)) return;
        SetFields(state, holder, FieldsOf(state, holder).Without(key));
        var powers = PowersOf(state, holder);
        var rest = powers[power] - amount;
        SetPowers(state, holder, AimedAt(state, holder, power).Count == 0 ? powers.Without(power) : powers.With(power, rest));
        if (SimModels.Get(power.Name) is { } model) model.AfterAimedCopyRemoved(state, holder, player, amount);
    }
}
