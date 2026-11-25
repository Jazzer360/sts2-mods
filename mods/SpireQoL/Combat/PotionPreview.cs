using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Runs;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Sts2Mods.Core.Game;

namespace SpireQoL.Combat;

/// <summary>
/// Play Preview for a potion (decision #314): while its Drink button is selected (hovered, or a controller's focus on it: NClickableControl.IsFocused)
/// or while you aim it (NTargetManager selecting, the potion the game marks as yours in hand, HoveredModelTracker's selected potion, at the node
/// the arrow is on), Incoming Damage, the Death Skull, Teammate Incoming Damage and the intents show the turn as it would go after using it there.
/// Its use and the turn after it are searched on a worker (SimJob, decision #107), the frame waiting out of its shared 8 ms at most, and nothing
/// shows until the search is done. One that needs a target shows only once aimed; a potion whose code only procures potions (Entropic Brew: its
/// random potions don't change the fight, the owner's decision) shows nothing. Reads the game's nodes only: no patches.
/// </summary>
internal static class PotionPreview
{
    /// <summary>NPotionContainer._holders: the potion slots (both branches).</summary>
    private static readonly AccessTools.FieldRef<NPotionContainer, List<NPotionHolder>>? HoldersOf = Field<NPotionContainer, List<NPotionHolder>>("_holders");
    /// <summary>NPotionHolder._popup: the Drink and Discard popup open on the slot, or null.</summary>
    private static readonly AccessTools.FieldRef<NPotionHolder, NPotionPopup?>? PopupOf = Field<NPotionHolder, NPotionPopup?>("_popup");
    /// <summary>NPotionPopup._useButton: its Drink (or Throw) button.</summary>
    private static readonly AccessTools.FieldRef<NPotionPopup, NPotionPopupButton>? UseButtonOf = Field<NPotionPopup, NPotionPopupButton>("_useButton");
    /// <summary>NClickableControl.IsFocused: hovered by the mouse, or a controller's focus on it (what the button's highlight shows).</summary>
    private static readonly Func<NClickableControl, bool>? IsFocused =
        AccessTools.PropertyGetter(typeof(NClickableControl), "IsFocused") is { } getter ? AccessTools.MethodDelegate<Func<NClickableControl, bool>>(getter) : null;
    /// <summary>HoveredModelTracker._localSelectedPotion: the potion of yours being aimed (NPotionHolder.UsePotion sets it while targeting).</summary>
    private static readonly AccessTools.FieldRef<HoveredModelTracker, PotionModel?>? SelectedPotion = Field<HoveredModelTracker, PotionModel?>("_localSelectedPotion");
    /// <summary>NTargetManager.HoveredNode: the node the targeting arrow is on (a creature's, a teammate's player state), or null.</summary>
    private static readonly Func<NTargetManager, Node?>? HoveredNode =
        AccessTools.PropertyGetter(typeof(NTargetManager), "HoveredNode") is { } hovered ? AccessTools.MethodDelegate<Func<NTargetManager, Node?>>(hovered) : null;

    private static AccessTools.FieldRef<TOwner, TField>? Field<TOwner, TField>(string name) where TOwner : class
    {
        try { return AccessTools.Field(typeof(TOwner), name) != null ? AccessTools.FieldRefAccess<TOwner, TField>(name) : null; }
        catch { return null; }
    }

    /// <summary>The search for the potion being previewed: what it's for (the potion, its aim, the board's key) and the job.</summary>
    private sealed record Search(PotionModel Potion, int Aim, long StateKey, SimJob Job);

    private static Search? _search;
    private static PlayPreview.Shown? _built;

    /// <summary>
    /// The potion of yours about to be used and where (its target: an enemy, you, a teammate; null for a potion with none), or null: none selected,
    /// or one that needs a target not aimed at one yet.
    /// </summary>
    internal static (PotionModel Potion, Creature? Target)? Selected(Player me)
    {
        // Aimed: the game's targeting with a potion of yours selected, the arrow on a creature or a teammate.
        if (NRun.Instance?.GlobalUi?.TargetManager is { } manager && GodotObject.IsInstanceValid(manager) && manager.IsInSelection)
        {
            if (RunManager.Instance?.HoveredModelTracker is not { } tracker || SelectedPotion?.Invoke(tracker) is not { } aimed || aimed.Owner != me) return null;
            var node = HoveredNode?.Invoke(manager);
            Creature? target = node switch
            {
                NCreature creature when GodotObject.IsInstanceValid(creature) => creature.Entity,
                NMultiplayerPlayerState playerState when GodotObject.IsInstanceValid(playerState) => playerState.Player?.Creature,
                _ => null,
            };
            return target != null && aimed.IsValidTarget(target) ? (aimed, target) : null;
        }
        // Its Drink button selected: one that doesn't need a target (its target as EnqueueManualUse gives it: you, when you're a valid one).
        if (NRun.Instance?.GlobalUi?.TopBar?.PotionContainer is not { } container || !GodotObject.IsInstanceValid(container) || HoldersOf?.Invoke(container) is not { } holders) return null;
        for (var i = 0; i < holders.Count; i++)
        {
            var holder = holders[i];
            if (!GodotObject.IsInstanceValid(holder) || PopupOf?.Invoke(holder) is not { } popup || !GodotObject.IsInstanceValid(popup) || popup.IsMarkedForRemoval || !popup.IsUsable) continue;
            if (UseButtonOf?.Invoke(popup) is not { } use || !GodotObject.IsInstanceValid(use) || IsFocused?.Invoke(use) != true) continue;
            if (holder.Potion?.Model is not { } potion || potion.Owner != me) return null;
            if (potion.TargetType is TargetType.AnyEnemy or TargetType.TargetedNoCreature || potion.CanThrowAtAlly()) return null; // aimed first
            return (potion, potion.IsValidTarget(me.Creature) ? me.Creature : null);
        }
        return null;
    }

    /// <summary>
    /// The turn after using the selected potion at its aim, for the board now: its search started (once for the potion, its aim and the board),
    /// waited for out of this frame's budget the first frame it's asked for, and null until it's done.
    /// </summary>
    internal static PlayPreview.Shown? Current(Player me)
    {
        if (Selected(me) is not var (potion, target))
        {
            Stop();
            return null;
        }
        if (TurnEndModel.Begin() is not { } start) return null;
        if (!SimAdapter.Usable(potion) || OnlyProcuresPotions(potion) || Sts2State.Combat is not { } combat || !SimWait.Models()) return null;
        var aim = SimAdapter.AimOf(target, me, combat, start.Ids);
        if (target != null && aim == Who.None) return null;
        if (_search is not { } search || !ReferenceEquals(search.Potion, potion) || search.Aim != aim || search.StateKey != start.StateKey)
        {
            _search?.Job.Cancel();
            var play = SimAdapter.Potion(potion, me, combat, start.Ids, aim);
            search = _search = new Search(potion, aim, start.StateKey, SimJob.Start(SimSearch.Of(start.State, play), $"{potion.GetType().Name} (potion at {aim})"));
        }
        // Waited for on the first frame it's asked for only (SimWait): a later frame reads what it has.
        if (!search.Job.Waited) SimWait.Ready(search.Job);
        if (search.Job.Latest is not { } outcome) return null;
        if (_built is { } built && ReferenceEquals(built.Outcome, outcome) && ReferenceEquals(built.Source, potion)) return built;
        return _built = new PlayPreview.Shown(potion, outcome, TurnEndModel.Preview(start, outcome), start.Ids);
    }

    /// <summary>No potion selected: its search (if still going) is no longer wanted.</summary>
    private static void Stop()
    {
        if (_search == null) return;
        _search.Job.Cancel();
        _search = null;
        _built = null;
    }

    /// <summary>
    /// The potion's code only procures potions (every command of its OnUse is PotionCmd's: Entropic Brew), which change nothing in the fight: no
    /// preview (the owner's decision, decision #314).
    /// </summary>
    private static bool OnlyProcuresPotions(PotionModel potion)
    {
        var type = potion.GetType();
        if (OnlyProcures.TryGetValue(type, out var only)) return only;
        var commands = PotionPrograms.Of(type).Commands.ToList();
        return OnlyProcures[type] = commands.Count > 0 && commands.All(command => command.Command.DeclaringType == "PotionCmd");
    }

    /// <summary>OnlyProcuresPotions by potion class, worked out once (the game's thread only).</summary>
    private static readonly Dictionary<Type, bool> OnlyProcures = new();
}
