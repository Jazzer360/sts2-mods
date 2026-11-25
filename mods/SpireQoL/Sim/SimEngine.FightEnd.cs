namespace SpireQoL.Sim;

/// <summary>
/// The fight's end inside a turn (decision #301): once no primary enemy is alive and nothing stops the fight from ending (FightEnding:
/// CombatManager.IsEnding), the game's commands return at their start (each one's IsEnding or IsOverOrEnding guard), its combat hooks reach no one
/// (Hook.IterateCombatHookListeners: CombatManager.ShouldSkipCombatEffect), a card's later plays don't happen (OnPlayWrapper's loop breaks) and no
/// end of turn or enemy turn follows. A branch whose fight is ending is set aside untouched from then on; the death sequence that ended it still
/// runs (AfterDeath, AfterDamageReceived and the rest Hook dispatches to every listener), and so does AfterCardPlayed.
/// </summary>
public static partial class SimEngine
{
    /// <summary>
    /// The hook dispatches the simulator runs as steps that still reach every listener while the fight is ending, each with why: the game's Hook
    /// method loops over IterateHookListeners itself, not IterateCombatHookListeners (FightEndTests read it on both branches). Every other
    /// dispatch the simulator runs as a step (HookDispatches) reaches no one then.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> HooksAfterTheFightEnds = new Dictionary<string, string>
    {
        [nameof(SimModel.AfterCardPlayed)] = "Hook.AfterCardPlayed loops over every listener (and its Late) whatever the fight's state",
        [nameof(SimModel.AfterPlayResolved)] = "not a game hook: the simulator's own, for a tab judged on what the play leaves",
    };

    /// <summary>
    /// The game's Hook methods behind each dispatch the simulator runs as a step (HookStep), for FightEndTests to read their loop: a simulator hook
    /// standing for several (AfterCardEvent: a card drawn, exhausted, discarded or made, stars gained, a shuffle) names each.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string[]> HookDispatches = new Dictionary<string, string[]>
    {
        [nameof(SimModel.BeforeCardPlayed)] = new[] { "BeforeCardPlayed" },
        [nameof(SimModel.AfterCardPlayed)] = new[] { "AfterCardPlayed" },
        [nameof(SimModel.BeforeAttack)] = new[] { "BeforeAttack" },
        [nameof(SimModel.AfterAttack)] = new[] { "AfterAttack" },
        [nameof(SimModel.AfterCardEvent)] = new[] { "AfterCardDrawn", "AfterCardExhausted", "AfterCardDiscarded", "AfterCardGeneratedForCombat", "AfterStarsGained", "AfterShuffle" },
        [nameof(SimModel.BeforeSideTurnEndVeryEarly)] = new[] { "BeforeSideTurnEnd" },
        [nameof(SimModel.BeforeSideTurnEndEarly)] = new[] { "BeforeSideTurnEnd" },
        [nameof(SimModel.BeforeSideTurnEnd)] = new[] { "BeforeSideTurnEnd" },
        [nameof(SimModel.AfterSideTurnEnd)] = new[] { "AfterSideTurnEnd" },
        [nameof(SimModel.AfterSideTurnEndLate)] = new[] { "AfterSideTurnEnd" },
        [nameof(SimModel.BeforeSideTurnStart)] = new[] { "BeforeSideTurnStart" },
        [nameof(SimModel.AfterSideTurnStart)] = new[] { "AfterSideTurnStart" },
        [nameof(SimModel.AfterSideTurnStartLate)] = new[] { "AfterSideTurnStart" },
    };

    /// <summary>
    /// The fight in this branch is over: it had enemies, and FightEnding holds (no primary enemy alive, nothing stopping the end). A board with no
    /// enemy at all is no fight's (the simulator's own tests of a player alone), never one that ended.
    /// </summary>
    internal static bool FightOver(SimState state) => state.Enemies.Count > 0 && FightEnding(state);

    /// <summary>A step on the branches whose fight goes on (WhileFighting), or on all when no fight is ending.</summary>
    private static void Fighting(BranchSet branches, Action step)
    {
        if (AnyEnding(branches)) WhileFighting(branches, step);
        else step();
    }

    /// <summary>Some branch's fight is ending (asked before a step, so a fight going on everywhere costs no allocation).</summary>
    private static bool AnyEnding(BranchSet branches)
    {
        foreach (var branch in branches.States)
            if (FightOver(branch)) return true;
        return false;
    }

    /// <summary>
    /// Runs <paramref name="step"/> on the branches whose fight goes on: one whose fight is ending (FightEnding) is set aside untouched, as the
    /// game's commands and combat hooks do nothing then, and kept after the others.
    /// </summary>
    private static void WhileFighting(BranchSet branches, Action step)
    {
        List<SimState>? over = null;
        foreach (var branch in branches.States)
            if (FightOver(branch)) (over ??= new List<SimState>()).Add(branch);
        if (over == null)
        {
            step();
            return;
        }
        if (over.Count == branches.States.Count) return;
        var ending = new HashSet<SimState>(over, ReferenceEqualityComparer.Instance);
        branches.States = branches.States.Where(branch => !ending.Contains(branch)).ToList();
        branches.Aside += over.Count;
        try
        {
            step();
        }
        finally
        {
            // Stopped at a cap or not, the branches set aside are kept (as they are), after the ones the step ran on.
            branches.Aside -= over.Count;
            branches.States.AddRange(over);
        }
    }
}
