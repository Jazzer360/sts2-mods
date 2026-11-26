using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Procs.Programs;
using Sts2Mods.Core.Game;

namespace SpireQoL.Sim;

/// <summary>
/// A potion's use built from its code (decisions #313, #335): its OnUse read as a play (PotionPrograms.AsPlay: the creature it's used on is the
/// play's target) and translated by the card translation (SimAdapter.Steps), the one translator of what you play or use: each command step into the
/// simulator's action for that command, in its code's order, its amounts the potion's own values. What only a card has (its cost, its replays, its
/// own place in your piles) is absent. A command, a whom, an amount or a condition it can't follow makes the use unknown where it runs (a
/// NotModelledAction with why: `?` and a range), never left out. The use's own frame is the simulator's (SimEngine.RunPotionUse: it leaves the belt,
/// then BeforePotionUsed, its body, the log, AfterPotionUsed). The commands a potion's code calls are held to the card translation's lists, on both
/// branches, by the potion scan (PotionScanTests).
/// </summary>
public static partial class SimAdapter
{
    /// <summary>
    /// The potions you can drink or throw in a fight (decision #313): CombatOnly and AnyTime, not one that acts on its own (Automatic: Fairy in a
    /// Bottle, whose revive the simulator already follows).
    /// </summary>
    public static bool Usable(PotionModel potion) => potion.Usage is MegaCrit.Sts2.Core.Entities.Potions.PotionUsage.CombatOnly or MegaCrit.Sts2.Core.Entities.Potions.PotionUsage.AnyTime;

    /// <summary>
    /// The use of <paramref name="potion"/> aimed at <paramref name="targetId"/> (an enemy's id, Who.You, a teammate it's thrown at, Who.None for
    /// none), as a SimPlay with its Potion set: its body from its code through the card translation, the listeners of its hooks the simulator can't
    /// follow made unknown.
    /// </summary>
    public static SimPlay Potion(PotionModel potion, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids, int targetId)
    {
        using var reads = Sts2State.TrackReads();
        var name = potion.GetType().Name;
        var play = new SimPlay
        {
            Name = name, TargetId = targetId, HandDelta = 0,
            Potion = new SimPotionUse(name, Who.You, targetId, Fairy: CountsAsFairy(potion)),
        };
        // Whom it's used on: an enemy (the play's aimed enemy), or a player (you, or the teammate it's thrown at: the play's player target).
        var target = CreatureOf(targetId, me, combat, ids);
        var enemy = target != null && ids.ContainsKey(target) ? target : null;
        var player = target != null && Who.IsPlayer(targetId) ? targetId : Who.None;
        var steps = StepPlayOf(potion, me, combat, ids, play, enemy, owner: Who.You, ally: player == Who.None ? null : target, allyId: player, plays: 1, boardTarget: target);
        if (steps.Program.Steps.Count == 0 && PotionRuns(potion))
            play.Body.Add(new NotModelledAction($"{name}'s OnUse isn't read"));
        else play.Body = BodyFromSteps(steps);
        // A listener whose class answers a potion hook the simulator doesn't run for it: what it does is unknown (a read model, or a hand-written one, runs).
        foreach (var hook in new[] { nameof(AbstractModel.BeforePotionUsed), nameof(AbstractModel.AfterPotionUsed) })
            if (UnfollowedListener(me, combat, hook) is { } listener) play.Body.Insert(0, new NotModelledAction($"{listener}'s {hook} isn't followed"));
        if (reads.Unread.Count > 0) play.Body.Insert(0, new UnreadAction(reads.Unread[0]));
        return play;
    }

    /// <summary>The potion's class overrides OnUse (a potion with none, the game's deprecated one, does nothing).</summary>
    private static bool PotionRuns(PotionModel potion) =>
        potion.GetType().GetMethod("OnUse", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public) is { } onUse
        && onUse.DeclaringType != typeof(PotionModel);

    /// <summary>The aim a creature is (an enemy's id, you, a teammate; Who.None for none or one the simulator doesn't name): a potion's target as the simulator keys it.</summary>
    public static int AimOf(Creature? target, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids)
    {
        if (target == null) return Who.None;
        if (ReferenceEquals(target, me.Creature)) return Who.You;
        if (ids.TryGetValue(target, out var enemyId)) return enemyId;
        var index = Teammates(me, combat).FindIndex(mate => ReferenceEquals(mate.Creature, target));
        return index >= 0 ? Who.Teammate(index) : Who.None;
    }

    /// <summary>The creature an aim names (an enemy by its id, you, a teammate), or null.</summary>
    private static Creature? CreatureOf(int id, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids)
    {
        if (id == Who.You) return me.Creature;
        if (Who.TeammateIndex(id) is var teammateIndex and >= 0)
            return Teammates(me, combat) is var mates && teammateIndex < mates.Count ? mates[teammateIndex].Creature : null;
        return ids.FirstOrDefault(entry => entry.Value == id).Key;
    }

    /// <summary>
    /// The card commands a potion thrown at a teammate can run on their cards (their hand, their piles, a card made or forged for them): a
    /// teammate's piles aren't simulated, so each is unknown where it runs (decision #335). Their draws are followed as a card's draws for a
    /// teammate are (TeammateDraws).
    /// </summary>
    private static bool ActsOnCards(CommandStep command) =>
        command.Command.DeclaringType is "CardCmd" or "ForgeCmd"
        || command.Command.DeclaringType == "CardPileCmd" && command.Command.Name is not ("Draw" or "DrawWithoutBlockingOnOtherPlayers");

    /// <summary>
    /// A listener of the run whose class answers <paramref name="hook"/> with no model the simulator runs for it (a read one or a hand-written one):
    /// its class's name, or null when every one that answers it is followed.
    /// </summary>
    private static string? UnfollowedListener(Player me, ICombatState combat, string hook)
    {
        try
        {
            foreach (var model in me.RunState.IterateHookListeners(combat))
            {
                if (model.GetType().GetMethod(hook, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public) is not { } method || method.DeclaringType == typeof(AbstractModel)) continue;
                if (SimModels.Get(SimReaders.ModelName(model)) is not { } simModel || !simModel.Overrides(hook)) return model.GetType().Name;
            }
            return null;
        }
        catch
        {
            return "a listener";
        }
    }
}
