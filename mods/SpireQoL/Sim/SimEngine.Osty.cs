namespace SpireQoL.Sim;

/// <summary>Osty's HP, and card effects that aren't attacks: Capture Spirit's damage, Judgement's execution, Osty's death.</summary>
public static partial class SimEngine
{
    /// <summary>
    /// A player's Osty loses HP (a hit he takes for them, Thorns on his attack, his death): AfterCurrentHpChanged with a loss, for
    /// every listener (Necro Mastery). Returns the HP he lost.
    /// </summary>
    internal static int OstyLoses(SimState s, int player, int amount)
    {
        var p = s.Player(player);
        if (p == null || amount <= 0 || p.OstyHp <= 0) return 0;
        if (player != Who.You) p = s.MutableTeammate(Who.TeammateIndex(player));
        var lost = Math.Min(amount, p.OstyHp);
        p.OstyHp -= lost;
        foreach (var (m, o) in Listeners(s)) m.AfterOstyHpLost(s, o, player, lost);
        AllyDeaths(s);
        return lost;
    }

    private static void RunDamage(BranchSet b, DamageAction da) => b.Each(s =>
    {
        var target = s.CurrentPlay?.Target ?? -1;
        if (s.Enemy(target) is not { Alive: true } e) return new[] { s };
        var spec = da.Damage;
        var card = s.CurrentPlay?.Card;
        var amount = spec.Fixed ? Math.Max(0, (int)spec.BaseFor(target))
            : CalcDamage(s, spec.BaseFor(target), new DamageCtx(Who.You, target, spec.Props, card, s.CurrentPlay), spec.TermsFor(target));
        if (amount > 0) Damage(s, e, amount, Who.You, spec.Props, card);
        AllyDeaths(s);
        return DrainPending(s);
    });

    private static IEnumerable<SimState> TriggerPoison(SimState s)
    {
        foreach (var e in s.Targetable.ToList())
        {
            if (e.PoisonStacks <= 0) continue;
            var before = e.PoisonStacks;
            var ticks = Math.Min(e.PoisonStacks, Math.Max(1, e.PoisonTriggers));
            for (var i = 0; i < ticks && e.Alive; i++)
            {
                Damage(s, e, e.PoisonStacks, dealer: null, SimProps.Unblockable | SimProps.Unpowered);
                if (e.Alive) e.PoisonStacks--;
            }
            // The next tick's damage (Poison, the game's preview) drops by what the spent stacks dealt.
            e.Poison = Math.Max(0, e.Poison - (PoisonTick(before, e.PoisonTriggers) - PoisonTick(e.PoisonStacks, e.PoisonTriggers)));
            AllyDeaths(s);
        }
        return DrainPending(s);
    }

    /// <summary>A poison tick's damage before modifiers: the stacks, then one less, for min(stacks, triggers) hits.</summary>
    internal static int PoisonTick(int stacks, int triggers)
    {
        var sum = 0;
        for (var i = 0; i < Math.Min(stacks, Math.Max(1, triggers)); i++) sum += stacks - i;
        return sum;
    }

    private static void RunExecute(BranchSet b, ExecuteAction ex) => b.Do(s =>
    {
        if (s.Enemy(s.CurrentPlay?.Target ?? -1) is { Alive: true } e && e.Hp <= ex.Threshold) e.Hp = 0;
        AllyDeaths(s);
    });
}
