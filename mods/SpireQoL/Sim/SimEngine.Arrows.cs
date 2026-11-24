using System.Collections.Immutable;
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
    /// <summary>Your arrows in an enemy (ImpaledPower), and a teammate's (the simulator's own entry: ImpaledPowerOther).</summary>
    public static readonly PowerId Impaled = PowerId.Impaled, ImpaledOther = new("ImpaledPowerOther");

    /// <summary>The enemy is impaled by anyone's arrow (ArrowCmd.CreatureIsImpaledByArrow).</summary>
    public static bool IsImpaled(SimState state, int enemy)
    {
        var powers = PowersOf(state, enemy);
        return powers.Has(Impaled) || powers.Has(ImpaledOther);
    }

    private static void RunShoot(BranchSet branches)
    {
        branches.Each(branch =>
        {
            var target = branch.CurrentPlay?.Target ?? Who.None;
            branch.ArrowAwayAt = Who.None;
            var hasArrow = branch.You.Arrows > 0 && branch.Enemy(target) is { Alive: true };
            if (hasArrow)
            {
                branch.You.Arrows--;
                branch.ArrowAwayAt = target;
            }
            // Its result, for what the card does only when the shot had an arrow (Hobbling Shot's debuffs: ShotHadArrowCondition).
            branch.PlayTests = branch.PlayTests.With(ShotKey, hasArrow ? 1 : 2);
            return Hook(branch, nameof(SimModel.AfterArrowShot), (model, owner, copy) => model.AfterArrowShot(copy, owner, Who.You));
        });
    }

    private static void RunImpale(BranchSet branches)
    {
        branches.Each(branch =>
        {
            var target = branch.ArrowAwayAt;
            branch.ArrowAwayAt = Who.None;
            if (branch.Enemy(target) is not { } enemy) return new[] { branch };
            var powers = enemy.Powers;
            enemy.Powers = powers.Has(Impaled) ? powers.With(Impaled, powers[Impaled] + 1) : powers.With(Impaled, amount: 1, data: 3);
            var states = Hook(branch, nameof(SimModel.AfterArrowImpaled), (model, owner, copy) => model.AfterArrowImpaled(copy, owner, target, Who.You));
            // Killed by the shot's own attack: the arrow comes straight out.
            return Then(states.ToList(), copy => copy.Enemy(target) is { Alive: false } ? Dislodge(copy, target, count: 1) : new[] { copy });
        });
    }

    private static void RunDislodgeAll(BranchSet branches) =>
        branches.Each(branch => branch.CurrentPlay?.Target is { } target && target >= 0 ? Dislodge(branch, target, PowerOn(branch, target, Impaled)) : new[] { branch });

    /// <summary>Arrows of yours out of an enemy (ArrowCmd.DislodgeArrow): back to you, each through AfterArrowDislodged.</summary>
    internal static IEnumerable<SimState> Dislodge(SimState state, int enemy, int count)
    {
        count = Math.Min(count, PowerOn(state, enemy, Impaled));
        if (count <= 0) return new[] { state };
        TakeArrows(state, enemy, count);
        return ProcCardEvent(state, CardEvent.ArrowDislodged, count, Who.You, indirect: false);
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
        for (var playIndex = 0; playIndex < plays; playIndex++)
        {
            if (arrows <= 0) return playIndex;
            for (var hit = 0; hit < hitsPerPlay && arrows > 0; hit++)
            {
                if (--counter > 0) continue;
                counter = 3;
                arrows--;
            }
        }
        return plays;
    }

    /// <summary>Takes arrows out of an enemy's Impaled (gone with the last) and gives them back to you.</summary>
    internal static void TakeArrows(SimState state, int enemy, int count)
    {
        var powers = PowersOf(state, enemy);
        var left = powers[Impaled] - count;
        SetPowers(state, enemy, left > 0 ? powers.With(Impaled, left) : powers.Without(Impaled));
        state.You.Arrows += count;
    }

    // ------------------------------------------------------------------ events raised inside damage

    /// <summary>
    /// A card event raised where hooks can't branch (inside a hit or a death: an arrow knocked loose), run once the hit or action
    /// is over (DrainPending).
    /// </summary>
    /// <param name="source">The model raising it, when its tab counts what it gives (a draw or energy tab: decision #132).</param>
    internal static void Raise(SimState state, CardEvent cardEvent, int player, int count = 1, string? source = null)
    {
        // The same event for the same player from the same source adds up (one entry, in the order first raised).
        for (var i = 0; i < state.Pending.Length; i++)
        {
            var pending = state.Pending[i];
            if (pending.Event != cardEvent || pending.Player != player || pending.Source != source) continue;
            state.Pending = state.Pending.SetItem(i, pending with { Count = pending.Count + count });
            return;
        }
        state.Pending = state.Pending.Add(new PendingEvent(cardEvent, player, source, count));
    }

    /// <summary>
    /// The creatures an enemy's death adds (SimEnemy.Spawns: Surprise's gremlins), one after another, each with the next id (the
    /// adapter numbers enemies in the game's order, so a creature added last is next), a branch per HP it can start with, each its
    /// share. They join doing nothing this turn (no hits), hittable at once: later hits in the same play and random targets count them.
    /// </summary>
    internal static IEnumerable<SimState> Spawn(SimState state, int dead)
    {
        IEnumerable<SimState> states = new[] { state };
        foreach (var spawn in state.Enemy(dead)?.Spawns ?? new List<SimSpawn>())
            states = Then(states.ToList(), branch => spawn.Hp.Select((hp, index) =>
            {
                var copy = index == spawn.Hp.Count - 1 ? branch : branch.Clone();
                copy.Weight = branch.Weight / spawn.Hp.Count;
                copy.Enemies.Add(new SimEnemy { Id = copy.Enemies.Max(enemy => enemy.Id) + 1, Name = spawn.Name, Hp = hp, Powers = spawn.Powers });
                return copy;
            }).ToList());
        return states;
    }

    /// <summary>The events raised during a hit or an action, each through every listener.</summary>
    internal static IEnumerable<SimState> DrainPending(SimState state)
    {
        if (state.Pending.IsEmpty && state.PendingIntoDraw.IsEmpty && state.PendingSpawns.IsEmpty) return new[] { state };
        var pending = state.Pending;
        var intoDraw = state.PendingIntoDraw;
        var spawns = state.PendingSpawns;
        state.Pending = ImmutableArray<PendingEvent>.Empty;
        state.PendingIntoDraw = ImmutableArray<(int Player, string Card)>.Empty;
        state.PendingSpawns = ImmutableArray<int>.Empty;
        IEnumerable<SimState> states = new[] { state };
        foreach (var dead in spawns) states = Then(states.ToList(), branch => Spawn(branch, dead));
        foreach (var (player, card) in intoDraw) states = Then(states.ToList(), branch => IntoDrawPile(branch, player, card));
        foreach (var (cardEvent, player, source, count) in pending)
        {
            var tally = source != null && SimModels.Get(source) is { } model ? TallyFor(model.Name, model.Tab, cardEvent, player) : null;
            states = Then(states.ToList(), branch => ProcCardEvent(branch, cardEvent, count, player, indirect: false, tally));
            // Epic Retrieval: its own energy for each of its player's arrows its play knocks loose (the game passes the card as the source).
            if (cardEvent == CardEvent.ArrowDislodged)
                states = Then(states.ToList(), branch => branch.CurrentPlay is { ArrowEnergy: > 0 } play && play.Owner == player
                    ? ProcCardEvent(branch, CardEvent.EnergyGained, play.ArrowEnergy * count, player, indirect: false) : new[] { branch });
        }
        return Then(states.ToList(), DrainPending);
    }
}
