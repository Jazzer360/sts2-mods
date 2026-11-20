namespace SpireQoL.Sim;

/// <summary>
/// The Fool's arrows (ArrowCmd, decompile v0.113.1). A shot spends one of your available arrows before its attack
/// (TryShootArrow: AfterArrowShot runs even with none, Covering Fire), then impales its target after the attack (ImpaleTarget:
/// your Impaled on it, the arrow added, AfterArrowImpaled), and a target the attack killed lets the arrow go at once. Your
/// Impaled on an enemy ("ImpaledPower": Amount = your arrows in it, Data = its HitCounter) loses an arrow every third powered hit
/// of yours (ImpaledPower.AfterDamageReceived) and all of them when it dies (AfterDeath); Recall, Uproot and Wrest pull every one
/// out. Each arrow that comes loose is yours to shoot again (RetrieveArrow) and sets off AfterArrowDislodged (Yah, Twine).
/// Teammates' arrows in an enemy are "ImpaledPowerOther": they make it Impaled and go back to them when it dies (their hit counter
/// isn't kept).
/// </summary>
public sealed record ShootAction : SimAction;

/// <summary>The shot's arrow impales the play's target, after the shot's attack (ArrowCmd.ImpaleTarget).</summary>
public sealed record ImpaleAction : SimAction;

/// <summary>Every arrow of yours in the play's target comes out (Recall, Uproot, Wrest: ArrowCmd.DislodgeArrow while impaled).</summary>
public sealed record DislodgeAllAction : SimAction;

public static partial class SimEngine
{
    public const string Impaled = "ImpaledPower";

    /// <summary>The enemy is impaled by anyone's arrow (ArrowCmd.CreatureIsImpaledByArrow).</summary>
    public static bool IsImpaled(SimState s, int enemy)
    {
        var p = PowersOf(s, enemy);
        return p.Has(Impaled) || p.Has(Impaled + "Other");
    }

    private static void RunShoot(BranchSet b)
    {
        b.Each(s =>
        {
            var target = s.CurrentPlay?.Target ?? -1;
            s.ArrowAwayAt = -1;
            if (s.P.Arrows > 0 && s.Enemy(target) is { Alive: true })
            {
                s.P.Arrows--;
                s.ArrowAwayAt = target;
            }
            return Hook(s, nameof(SimModel.AfterArrowShot), (m, o, x) => m.AfterArrowShot(x, o, Who.You));
        });
    }

    private static void RunImpale(BranchSet b)
    {
        b.Each(s =>
        {
            var target = s.ArrowAwayAt;
            s.ArrowAwayAt = -1;
            if (s.Enemy(target) is not { } e) return new[] { s };
            var p = e.Powers;
            e.Powers = p.Has(Impaled) ? p.With(Impaled, p[Impaled] + 1) : p.With(Impaled, 1, 3);
            var r = Hook(s, nameof(SimModel.AfterArrowImpaled), (m, o, x) => m.AfterArrowImpaled(x, o, target, Who.You));
            // Killed by the shot's own attack: the arrow comes straight out.
            return Then(r.ToList(), x => x.Enemy(target) is { Alive: false } ? Dislodge(x, target, 1) : new[] { x });
        });
    }

    private static void RunDislodgeAll(BranchSet b) =>
        b.Each(s => s.CurrentPlay?.Target is { } t && t >= 0 ? Dislodge(s, t, PowerOn(s, t, Impaled)) : new[] { s });

    /// <summary>Arrows of yours out of an enemy (ArrowCmd.DislodgeArrow): back to you, each through AfterArrowDislodged.</summary>
    internal static IEnumerable<SimState> Dislodge(SimState s, int enemy, int count)
    {
        count = Math.Min(count, PowerOn(s, enemy, Impaled));
        if (count <= 0) return new[] { s };
        TakeArrows(s, enemy, count);
        return ProcCardEvent(s, CardEvent.ArrowDislodged, count, Who.You, indirect: false);
    }

    /// <summary>
    /// How many of a card's plays, from the first, start with the target still Impaled while each play hits it
    /// <paramref name="hitsPerPlay"/> times (Ki Hop: KiHop.OnPlay checks wasImpaled before its hit). Your powered hits count your
    /// Impaled down from <paramref name="counter"/>; at 0 one of your arrows comes loose and it starts again at 3, and the power
    /// goes with your last arrow (ImpaledPower.AfterDamageReceived). A teammate's arrow keeps it Impaled whatever you hit.
    /// </summary>
    public static int PlaysStartingImpaled(bool teammateArrow, int yourArrows, int counter, int hitsPerPlay, int plays)
    {
        if (teammateArrow) return plays;
        if (yourArrows <= 0) return 0;
        counter = Math.Max(1, counter);
        var arrows = yourArrows;
        for (var k = 0; k < plays; k++)
        {
            if (arrows <= 0) return k;
            for (var h = 0; h < hitsPerPlay && arrows > 0; h++)
            {
                if (--counter > 0) continue;
                counter = 3;
                arrows--;
            }
        }
        return plays;
    }

    /// <summary>Takes arrows out of an enemy's Impaled (gone with the last) and gives them back to you.</summary>
    internal static void TakeArrows(SimState s, int enemy, int count)
    {
        var p = PowersOf(s, enemy);
        var left = p[Impaled] - count;
        SetPowers(s, enemy, left > 0 ? p.With(Impaled, left) : p.Without(Impaled));
        s.P.Arrows += count;
    }

    // ------------------------------------------------------------------ events raised inside damage

    /// <summary>
    /// A card event raised where hooks can't branch (inside a hit or a death: an arrow knocked loose), run once the hit or action
    /// is over (DrainPending).
    /// </summary>
    internal static void Raise(SimState s, CardEvent ev, int player, int count = 1) =>
        s.Pending = s.Pending.Add($"{(int)ev}|{player}", count);

    /// <summary>The events raised during a hit or an action, each through every listener.</summary>
    internal static IEnumerable<SimState> DrainPending(SimState s)
    {
        if (s.Pending.Count == 0) return new[] { s };
        var pending = s.Pending.Items;
        s.Pending = PowerSet.Empty;
        IEnumerable<SimState> r = new[] { s };
        foreach (var e in pending)
        {
            var parts = e.Name.Split('|');
            var ev = (CardEvent)int.Parse(parts[0]);
            var player = int.Parse(parts[1]);
            r = Then(r.ToList(), x => ProcCardEvent(x, ev, e.Amount, player, indirect: false));
            // Epic Retrieval: its own energy for each of its player's arrows its play knocks loose (the game passes the card as the source).
            if (ev == CardEvent.ArrowDislodged)
                r = Then(r.ToList(), x => x.CurrentPlay is { ArrowEnergy: > 0 } cp && cp.Owner == player
                    ? ProcCardEvent(x, CardEvent.EnergyGained, cp.ArrowEnergy * e.Amount, player, indirect: false) : new[] { x });
        }
        return Then(r.ToList(), DrainPending);
    }
}
