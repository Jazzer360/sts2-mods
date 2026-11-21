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
            : CalcDamage(branch, spec.BaseFor(target), new DamageCtx(Who.You, target, spec.Props, card, branch.CurrentPlay), spec.TermsFor(target));
        if (amount > 0) Damage(branch, enemy, amount, Who.You, spec.Props, card);
        AllyDeaths(branch);
        return DrainPending(branch);
    });

    private static IEnumerable<SimState> TriggerPoison(SimState state)
    {
        foreach (var enemy in state.Targetable.ToList())
        {
            if (enemy.PoisonStacks <= 0) continue;
            var before = enemy.PoisonStacks;
            var ticks = Math.Min(enemy.PoisonStacks, Math.Max(1, enemy.PoisonTriggers));
            for (var i = 0; i < ticks && enemy.Alive; i++)
            {
                Damage(state, enemy, enemy.PoisonStacks, dealer: null, SimProps.Unblockable | SimProps.Unpowered);
                if (enemy.Alive) enemy.PoisonStacks--;
            }
            // The next tick's damage (Poison, the game's preview) drops by what the spent stacks dealt.
            enemy.Poison = Math.Max(0, enemy.Poison - (PoisonTick(before, enemy.PoisonTriggers) - PoisonTick(enemy.PoisonStacks, enemy.PoisonTriggers)));
            AllyDeaths(state);
        }
        return DrainPending(state);
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
