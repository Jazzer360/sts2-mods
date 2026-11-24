namespace SpireQoL.Sim;

/// <summary>Osty's HP, and card effects that aren't attacks: Capture Spirit's damage, Judgement's execution, Osty's death.</summary>
public static partial class SimEngine
{
    /// <summary>
    /// A player's Osty loses HP (a hit he takes for them, Thorns on his attack, his death): AfterCurrentHpChanged with a loss, for
    /// every listener (Necro Mastery). Returns the HP he lost.
    /// </summary>
    internal static int OstyLoses(SimState state, int player, int amount)
    {
        var simPlayer = state.Player(player);
        if (simPlayer == null || amount <= 0 || simPlayer.OstyHp <= 0) return 0;
        if (player != Who.You) simPlayer = state.MutableTeammate(Who.TeammateIndex(player));
        var lost = Math.Min(amount, simPlayer.OstyHp);
        simPlayer.OstyHp -= lost;
        foreach (var (model, o) in Listeners(state)) model.AfterOstyHpLost(state, o, player, lost);
        AllyDeaths(state);
        return lost;
    }

    private static void RunDamage(BranchSet branches, DamageAction damageAction) => branches.Each(branch =>
    {
        var target = branch.CurrentPlay?.Target ?? Who.None;
        if (branch.Enemy(target) is not { Alive: true } enemy) return new[] { branch };
        var spec = damageAction.Damage;
        var card = branch.CurrentPlay?.Card;
        var amount = spec.Fixed ? Math.Max(0, (int)spec.BaseFor(target))
            : ModifiedDamage(branch, spec.BaseFor(target), new DamageCtx(Who.You, target, spec.Props, card, branch.CurrentPlay), spec.TermsFor(target));
        if (amount > 0) Damage(branch, enemy, amount, Who.You, spec.Props, card);
        AllyDeaths(branch);
        return DrainPending(branch);
    });

    /// <summary>
    /// A card calling a power's own command on each hittable enemy that has it, one after another (Outbreak: PoisonPower.Trigger on each enemy's
    /// poison), as the power's code runs it (ReadModel.RunCommand, decision #210), the deaths it causes handled after each. A power whose command
    /// isn't read makes the simulation unknown. The game's preview of an enemy's next poison tick (PoisonPower.CalculateTotalDamageNextTurn, the
    /// play check's) follows the stacks the command spent.
    /// </summary>
    private static IEnumerable<SimState> RunModelCommand(SimState state, string power, string method)
    {
        if (SimModels.Get(power) is not ReadModel model || !model.HasCommand(method))
        {
            state.Unmodelled ??= $"{power}.{method} isn't read from its code: what it does isn't simulated";
            return new[] { state };
        }
        var id = new PowerId(power);
        IEnumerable<SimState> states = new[] { state };
        foreach (var enemyId in state.Targetable.Select(enemy => enemy.Id).ToList())
            states = Then(states.ToList(), branch =>
            {
                if (branch.Enemy(enemyId) is not { Alive: true } enemy || !PowersOf(branch, enemyId).Has(id)) return new[] { branch };
                var before = enemy.PoisonStacks;
                return Then(model.RunCommand(method, branch, enemyId).ToList(), after =>
                {
                    after.Enemy(enemyId)?.FollowPoisonPreview(before);
                    AllyDeaths(after);
                    return DrainPending(after);
                });
            });
        return states;
    }

    /// <summary>A poison tick's damage before modifiers: the stacks, then one less, for min(stacks, triggers) hits.</summary>
    internal static int PoisonTick(int stacks, int triggers)
    {
        var sum = 0;
        for (var i = 0; i < Math.Min(stacks, Math.Max(1, triggers)); i++) sum += stacks - i;
        return sum;
    }

    private static void RunExecute(BranchSet branches, ExecuteAction ex) => branches.Do(branch =>
    {
        if (branch.Enemy(branch.CurrentPlay?.Target ?? Who.None) is { Alive: true } enemy && enemy.Hp <= ex.Threshold) enemy.Hp = 0;
        AllyDeaths(branch);
    });
}
