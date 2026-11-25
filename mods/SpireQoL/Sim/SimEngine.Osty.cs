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

    /// <summary>
    /// OstyCmd.Summon of <paramref name="amount"/> for <paramref name="player"/>, as the game runs it: the amount through Hook.ModifySummonAmount (every
    /// listener's, in order; none at 0); then Osty alive gains that much max HP and is healed by it (CreatureCmd.GainMaxHp: SetMaxHp, then Heal), Osty
    /// dead is revived (his max HP set to it, healed by it: CreatureCmd.SetMaxHp, Heal) and Hook.AfterOstyRevived told, and a player with no Osty
    /// yet gets one (PlayerCmd.AddPet, Die For You on him, then the same max HP and heal); then the fight's log records the summon
    /// (CombatHistory.Summoned) and Hook.AfterSummon is told. A heal of Osty tells AfterCurrentHpChanged a gain, which nothing the simulator runs acts
    /// on (Necro Mastery acts on a loss). <paramref name="log"/>: a model's summon logs its tab's amount under its name (Devour Life, Sic 'Em).
    /// </summary>
    internal static IEnumerable<SimState> Summon(SimState state, int player, decimal amount, string? log = null, int? logOwner = null)
    {
        if (state.Player(player) == null) return new[] { state };
        foreach (var (model, owner) in Listeners(state))
            if (model.Overrides(nameof(SimModel.ModifySummonAmount))) amount = model.ModifySummonAmount(state, owner, player, amount);
        if (amount == 0m) return new[] { state };
        var summoned = (int)amount;
        var summoner = state.MutablePlayer(player);
        var revived = false;
        if (summoner.OstyHp > 0)
        {
            // GainMaxHp: SetMaxHp(MaxHp + amount), then Heal by the change.
            if (summoner.OstyMaxHp != int.MaxValue) summoner.OstyMaxHp += summoned;
            summoner.OstyHp = (int)Math.Min(summoner.OstyMaxHp, (long)summoner.OstyHp + summoned);
        }
        else
        {
            revived = summoner.OstyShields; // a dead Osty stays in the fight with his Die For You (ShouldPowerBeRemovedAfterOwnerDeath: false)
            summoner.OstyShields = true;
            summoner.OstyMaxHp = summoned;
            summoner.OstyHp = summoned;
        }
        if (log != null) state.LogFired(log, summoned, logOwner ?? player);
        IEnumerable<SimState> states = new[] { state };
        if (revived) states = Then(states.ToList(), branch => Hook(branch, nameof(SimModel.AfterOstyRevived), (model, owner, copy) => model.AfterOstyRevived(copy, owner, player)));
        return Then(states.ToList(), branch =>
        {
            Record(branch, new HistoryEvent(HistoryKind.Summoned, player));
            return Hook(branch, nameof(SimModel.AfterSummon), (model, owner, copy) => model.AfterSummon(copy, owner, player, summoned));
        });
    }

    /// <summary>A card's summon (SummonAction): its amount for its summoner, the play's player unless it names another.</summary>
    private static void RunSummon(BranchSet branches, SummonAction summon) =>
        branches.Each(branch => Summon(branch, summon.Player ?? branch.CurrentPlay?.Owner ?? Who.You, summon.Amount));

    /// <summary>
    /// CreatureCmd.Heal of a player's Osty: up to his max HP, from 0 too (Creature.HealInternal sets his HP; a dead Osty comes back). A player with no
    /// Osty has nothing to heal.
    /// </summary>
    internal static void HealOsty(SimState state, int player, int amount)
    {
        if (amount <= 0 || state.Player(player) is not { OstyShields: true }) return;
        var healed = state.MutablePlayer(player);
        healed.OstyHp = (int)Math.Min(healed.OstyMaxHp, (long)healed.OstyHp + amount);
    }

    private static void RunHeal(BranchSet branches, HealAction heal) => branches.Do(branch =>
    {
        var player = branch.CurrentPlay?.Owner ?? Who.You;
        if (heal.Osty) HealOsty(branch, player, heal.Amount);
        else Heal(branch, player, heal.Amount);
    });

    private static void RunDamage(BranchSet branches, DamageAction damageAction) => branches.Each(branch =>
    {
        var target = branch.CurrentPlay?.Target ?? Who.None;
        if (branch.Enemy(target) is not { Alive: true } enemy) return new[] { branch };
        var spec = damageAction.Damage;
        var card = branch.CurrentPlay?.Card;
        var amount = spec.Fixed ? Math.Max(0, (int)spec.BaseFor(target))
            : ModifiedDamage(branch, spec.BaseFor(target), new DamageCtx(Who.You, target, spec.Props, card, branch.CurrentPlay), spec.TermsFor(target));
        if (amount > 0) Damage(branch, enemy, amount, Who.You, spec.Props, card);
        // Its DamageResult's WasTargetKilled, as an attack's hit records it (SimTest.AttackKilled: the play's last damage killed).
        branch.AttackKilled = enemy.Hp <= 0;
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
