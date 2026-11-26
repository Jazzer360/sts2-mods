namespace SpireQoL.Sim;

/// <summary>A potion's use (decision #313): PotionModel.OnUseWrapper's steps, in its order.</summary>
public static partial class SimEngine
{
    /// <summary>
    /// A potion drunk or thrown (PotionModel.OnUseWrapper): it leaves its owner's belt (RemoveBeforeUse: one potion fewer, a Fairy in a Bottle
    /// fewer to hold back a death), Hook.BeforePotionUsed, its OnUse (the body), the game's log of it (History.PotionUsed), then
    /// Hook.AfterPotionUsed, only while its owner lives (the wrapper's !Owner.Creature.IsDead). No card play is in progress: what a card play
    /// counts or reacts to doesn't run.
    /// </summary>
    private static void RunPotionUse(BranchSet branches, PotionUseAction potionUseAction)
    {
        var use = potionUseAction.Use;
        branches.Do(branch =>
        {
            if (branch.Player(use.Owner) == null) return;
            var player = branch.MutablePlayer(use.Owner);
            player.Potions = Math.Max(0, player.Potions - 1);
            if (use.Fairy) player.Fairies = Math.Max(0, player.Fairies - 1);
            // Its own takes and tests (decision #335: its body is the card translation's), as a play's start clears a card play's.
            branch.PlayTaken = 0; branch.TakeMatching = NamedCounts.Empty; branch.AttackKilled = false; branch.PlayTests = NamedCounts.Empty;
        });
        HookStep(branches, nameof(SimModel.BeforePotionUsed), (model, owner, branch) => model.BeforePotionUsed(branch, owner, use));
        foreach (var action in potionUseAction.Body) Run(branches, action);
        // The wrapper goes on only while its owner lives: the log and AfterPotionUsed are skipped for a potion whose owner died during it.
        bool OwnerDead(SimState branch) => branch.Player(use.Owner) is not { Died: false, Hp: > 0 };
        branches.Do(branch => { if (!OwnerDead(branch) && branch.History.Count > 0) Record(branch, new HistoryEvent(HistoryKind.PotionUsed, use.Owner)); });
        RunWithout(branches, OwnerDead,() => HookStep(branches, nameof(SimModel.AfterPotionUsed), (model, owner, branch) => model.AfterPotionUsed(branch, owner, use)));
    }

    /// <summary>
    /// PotionCmd.TryToProcure for the play's owner (ProcurePotionAction, decision #362): a potion into their belt where it has room (AddPotionInternal fails
    /// on a full one), then Hook.AfterPotionProcured there; a branch with a full belt has nothing happen.
    /// </summary>
    private static void RunProcurePotion(BranchSet branches)
    {
        static int Owner(SimState branch) => branch.CurrentPlay?.Owner ?? Who.You;
        var procured = new HashSet<SimState>(ReferenceEqualityComparer.Instance);
        branches.Do(branch =>
        {
            if (branch.Player(Owner(branch)) is not { } player || player.Potions >= player.PotionSlots) return;
            branch.MutablePlayer(Owner(branch)).Potions++;
            procured.Add(branch);
        });
        // The hook runs for the branches that procured; the others are set aside meanwhile.
        RunWithout(branches, branch => !procured.Contains(branch), () => HookStep(branches, nameof(SimModel.AfterPotionProcured), (model, owner, branch) => model.AfterPotionProcured(branch, owner, Owner(branch))));
    }

    /// <summary>Runs <paramref name="step"/> on the branches <paramref name="aside"/> doesn't hold, the others set aside meanwhile (their share still counted).</summary>
    private static void RunWithout(BranchSet branches, Func<SimState, bool> aside, Action step)
    {
        if (!branches.States.Any(aside))
        {
            step();
            return;
        }
        var setAside = branches.States.Where(aside).ToList();
        branches.States = branches.States.Where(branch => !aside(branch)).ToList();
        branches.Aside += setAside.Count;
        try
        {
            step();
        }
        finally
        {
            branches.Aside -= setAside.Count;
            branches.States.AddRange(setAside);
        }
    }
}
