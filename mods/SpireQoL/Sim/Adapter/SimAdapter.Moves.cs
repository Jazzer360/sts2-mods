namespace SpireQoL.Sim;

/// <summary>
/// A monster's code as the simulator's (decision #195): each command a monster's move or hook calls, read into its program
/// (ModelPrograms, MonsterCode), is translated into the enemy turn's action for it or is a named gap with why. The scan holds every
/// command of every monster's moves and hooks on both branches to one or the other (MonsterProgramTests).
/// </summary>
public static partial class SimAdapter
{
    /// <summary>The commands a monster's move is translated from, each to the enemy turn's action standing for it.</summary>
    internal static readonly IReadOnlyDictionary<string, string> MoveCommandsTranslated = new Dictionary<string, string>();

    /// <summary>The commands a monster's moves and hooks call that the enemy turn doesn't follow, and why.</summary>
    internal static readonly IReadOnlyDictionary<string, string> MoveCommandsNotTranslated = new Dictionary<string, string>
    {
        ["DamageCmd.Attack"] = "the move's attack: the enemy turn runs its intent's hits (SimEnemy.Intent), not the move's program",
        ["PowerCmd.Apply"] = "a power a move or a hook applies: not followed in the enemy turn (gap)",
        ["CreatureCmd.GainBlock"] = "block a move gives: not followed in the enemy turn (gap)",
        ["PowerCmd.Remove"] = "a power a move takes away (its own: Soar, Steam Eruption, Plating, its revive's): after its attack, nothing later this turn reads it",
        ["CardPileCmd.AddToCombatAndPreview"] = "status cards put into your piles (Dazed, Slimed, Burn...): your next turn's, not this enemy turn's",
        ["CardPileCmd.AddGeneratedCardToCombat"] = "cards put into your piles (Dazed, Beckon, Frantic Escape): your next turn's, not this enemy turn's",
        ["CardPileCmd.RemoveFromCombat"] = "cards a move steals from your piles (Thieving Hopper): your next turn's, not this enemy turn's",
        ["CreatureCmd.Add"] = "a creature a move summons: it joins with a move for its next turn (gap: the summon isn't in the enemy turn's state)",
        ["CreatureCmd.Heal"] = "HP a move heals: not followed in the enemy turn (gap)",
        ["CreatureCmd.SetMaxHp"] = "a move's new max HP (Test Subject's respawn): not followed in the enemy turn (gap)",
        ["CreatureCmd.SetMaxAndCurrentHp"] = "HP set as a monster joins or hatches (Decimillipede, Tough Egg): not followed (gap)",
        ["CreatureCmd.Kill"] = "a monster killing itself after its attack (Gas Bomb, Waterfall Giant): its death isn't followed in the enemy turn (gap)",
        ["CreatureCmd.Escape"] = "a monster fleeing (Fat Gremlin, Thieving Hopper): it leaves after its move (gap)",
        ["CreatureCmd.Stun"] = "a monster stunning itself after its attack (Bowlbug Rock off balance): its next turn's",
        ["ThieveryPower.Steal"] = "gold stolen (Gremlin Merc): not a fight's number",
        ["SwipePower.Steal"] = "a card stolen (Thieving Hopper): your next turn's, not this enemy turn's",
        ["ReattachPower.DoReattach"] = "a Decimillipede segment coming back: its next turn's (gap)",
        ["MonsterModel.SetMoveImmediate"] = "a monster's move changed by a hook (Queen's Enrage when her Amalgam dies): not followed (gap)",
    };
}
