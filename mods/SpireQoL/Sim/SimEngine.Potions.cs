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
        });
        HookStep(branches, nameof(SimModel.BeforePotionUsed), (model, owner, branch) => model.BeforePotionUsed(branch, owner, use));
        foreach (var action in potionUseAction.Body) Run(branches, action);
        // The wrapper goes on only while its owner lives: the log and AfterPotionUsed are skipped for a potion whose owner died during it.
        bool OwnerDead(SimState branch) => branch.Player(use.Owner) is not { Died: false, Hp: > 0 };
        branches.Do(branch => { if (!OwnerDead(branch) && branch.History.Count > 0) Record(branch, new HistoryEvent(HistoryKind.PotionUsed, use.Owner)); });
        if (!branches.States.Any(OwnerDead))
        {
            HookStep(branches, nameof(SimModel.AfterPotionUsed), (model, owner, branch) => model.AfterPotionUsed(branch, owner, use));
            return;
        }
        var dead = branches.States.Where(OwnerDead).ToList();
        branches.States = branches.States.Where(branch => !OwnerDead(branch)).ToList();
        branches.Aside += dead.Count;
        try
        {
            HookStep(branches, nameof(SimModel.AfterPotionUsed), (model, owner, branch) => model.AfterPotionUsed(branch, owner, use));
        }
        finally
        {
            branches.Aside -= dead.Count;
            branches.States.AddRange(dead);
        }
    }
}
