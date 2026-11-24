namespace SpireQoL.Sim.Models;

// What acts when your side's turn ends (decompile v0.113.1: Hook.BeforeSideTurnEnd runs every listener's VeryEarly, then Early,
// then BeforeSideTurnEnd; Hook.AfterSideTurnEnd after the hands are flushed). Each model acts for its own player, in the game's
// listener order (players in player order, their powers then their relics), so teammates' come in their place. No card tabs:
// these show in Incoming Damage's breakdown.

/// <summary>An end-of-turn power or relic: no tab on cards; it acts only while its player takes part (alive).</summary>
public abstract class EndOfTurnModel : ProcModel
{
    public override TabKind? Tab => null;
}

/// <inheritdoc cref="EndOfTurnModel"/>
public abstract class EndOfTurnRelic : RelicProc
{
    public override TabKind? Tab => null;
}

/// <summary>PlatingPower.BeforeSideTurnEndEarly: its amount of block (unpowered).</summary>
public sealed class PlatingPower : EndOfTurnModel
{
    public override IEnumerable<SimState> BeforeSideTurnEndEarly(SimState state, int owner) =>
        state.TakesPart(owner) && Amount(state, owner) > 0 ? Block(state, owner, Amount(state, owner)) : One(state);
}

/// <summary>
/// TheBombPower.BeforeSideTurnEnd: ticks down (amount: turns left); on its last its Damage to every hittable enemy, then it goes.
/// Each Bomb played is a copy of its own (instanced) with its own turns and Damage, each ticking on its own: the copies listed in
/// SimPlayer.Counters (Left: turns, Amount: Damage), or with none listed its Powers entry (amount: turns, Data: Damage).
/// </summary>
public sealed class TheBombPower : EndOfTurnModel
{
    public override bool Instanced => true;

    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState state, int owner)
    {
        if (!state.TakesPart(owner)) return One(state);
        var listed = CounterCopies.Listed(state, owner, ThisPower);
        var copies = listed ?? new List<CounterCopy> { new(ThisPower, Left: Amount(state, owner), Amount: Data(state, owner)) };
        var ticking = copies.Where(copy => copy.Left > 1).Select(copy => copy with { Left = copy.Left - 1 }).ToList();
        var powers = SimEngine.PowersOf(state, owner);
        SimEngine.SetPowers(state, owner, ticking.Count == 0 ? powers.Without(ThisPower) : powers.With(ThisPower, ticking.Sum(copy => copy.Left), ticking[^1].Amount));
        if (listed != null) CounterCopies.SetListed(state, owner, ThisPower, ticking);
        // Each copy on its last turn goes off in the order the game holds them.
        IEnumerable<SimState> states = One(state);
        foreach (var copy in copies.Where(copy => copy.Left <= 1 && copy.Amount > 0))
            states = states.SelectMany(branch => DamageAll(branch, owner, copy.Amount)).ToList();
        return states;
    }

    /// <summary>
    /// A Bomb played (TheBomb.OnPlay: its Turns, then SetDamage) lands beside the ones before it. Its Damage is set after it lands,
    /// outside what the play carries: 0 here, as it can't go off before its turns (3) run out, past the turn simulated.
    /// </summary>
    public override void AfterApplied(SimState state, int owner, int applier, int amount)
    {
        if (state.Player(owner) is null) return;
        var before = CounterCopies.Listed(state, owner, ThisPower)
                     ?? (Amount(state, owner) > amount ? new List<CounterCopy> { new(ThisPower, Left: Amount(state, owner) - amount, Amount: Data(state, owner)) } : null);
        if (before == null) return; // the first: its entry is the copy
        CounterCopies.SetListed(state, owner, ThisPower, before.Append(new CounterCopy(ThisPower, Left: amount, Amount: 0)));
    }
}

/// <summary>ConsumingShadowPower.AfterSideTurnEnd: its owner's last orb is evoked, its amount of times (OrbCmd.EvokeLast).</summary>
public sealed class ConsumingShadowPower : EndOfTurnModel
{
    public override IEnumerable<SimState> AfterSideTurnEnd(SimState state, int owner)
    {
        if (!state.TakesPart(owner)) return One(state);
        IEnumerable<SimState> states = One(state);
        for (var i = 0; i < Amount(state, owner); i++) states = states.SelectMany(branch => SimEngine.EvokeLast(branch, owner)).ToList();
        return states;
    }
}

/// <summary>StoneCalendar.BeforeSideTurnEnd: its damage (amount) to every hittable enemy on its DamageTurn (Data: turns until then).</summary>
public sealed class StoneCalendar : EndOfTurnRelic
{
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState state, int owner) =>
        state.TakesPart(owner) && Data(state, owner) == 0 && Amount(state, owner) > 0 ? DamageAll(state, owner, Amount(state, owner)) : One(state);
}

/// <summary>ParryingShield.AfterSideTurnEnd: its damage (amount) to a random hittable enemy when its owner has at least its Block (Data).</summary>
public sealed class ParryingShield : EndOfTurnRelic
{
    /// <summary>Plain when this play makes it fire for sure and ending the turn now might not; crossed when it would for sure now and the play can stop it.</summary>
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) =>
        owner != Who.You ? null
        : !now.ParryingShieldInAll ? new(Harmful: false, outcome => outcome.ParryingShieldInAll, InPlay: false)
        : new(Harmful: true, outcome => !outcome.ParryingShieldInAll, branch => !branch.ParryingShieldFired, InPlay: false);

    public override IEnumerable<SimState> AfterSideTurnEnd(SimState state, int owner)
    {
        if (!state.TakesPart(owner) || Amount(state, owner) <= 0 || state.Player(owner) is not { } player || player.Block < Data(state, owner)) return One(state);
        if (owner == Who.You) state.ParryingShieldFired = true;
        return DamageRandom(state, owner, Amount(state, owner));
    }
}

/// <summary>LunarPastry.AfterSideTurnEnd: its owner gains stars (Black Hole reacts: AfterStarsGained).</summary>
public sealed class LunarPastry : EndOfTurnRelic
{
    public override IEnumerable<SimState> AfterSideTurnEnd(SimState state, int owner) =>
        state.TakesPart(owner) ? SimEngine.ProcCardEvent(state, CardEvent.StarsGained, count: 1, owner, indirect: false) : One(state);
}

// ---- debuffs on a player that hurt them when their side's turn ends (AfterSideTurnEnd, AfterSideTurnEndLate), in listener order
// with that player's other powers.

/// <summary>A debuff that hurts its owner at the end of their turn: its damage through the pipeline on them, named as the game names it.</summary>
public abstract class TurnEndDebuff : EndOfTurnModel
{
    /// <summary>The applier is gone (Data: the applier, DataCreature; no one: unknown, taken as alive).</summary>
    protected static bool ApplierDead(SimState state, int owner, PowerId name) =>
        DataCreature.In(SimEngine.PowersOf(state, owner).DataOf(name)) is var applier && Who.IsEnemy(applier) && state.Enemy(applier) is not { Alive: true };

    protected IEnumerable<SimState> Hurt(SimState state, int owner, int amount, SimProps props, bool selfDealt) =>
        amount <= 0 ? One(state) : SimEngine.ProcDamagePlayer(state, owner, Name, owner, amount, props, noDealer: !selfDealt, label: SimEngine.Title(state, owner, Name));
}

/// <summary>MagicBombPower.AfterSideTurnEnd: while its applier lives, its amount to its owner (unpowered, dealt by its owner), then it goes.</summary>
public sealed class MagicBombPower : TurnEndDebuff
{
    /// <summary>Instanced, but nothing in the game applies it (Magi Knight's MAGIC_BOMB is an attack): it's never on anyone twice.</summary>
    public override bool Instanced => true;
    public override IEnumerable<SimState> AfterSideTurnEnd(SimState state, int owner)
    {
        if (!state.TakesPart(owner) || ApplierDead(state, owner, ThisPower)) return One(state);
        var amount = Amount(state, owner);
        SimEngine.SetPowers(state, owner, SimEngine.PowersOf(state, owner).Without(ThisPower));
        return Hurt(state, owner, amount, Unpowered, selfDealt: true);
    }
}

/// <summary>InsatiableDominionPower.AfterSideTurnEndLate: its Damage (2 + its owner's turn number, unpowered, no dealer) to its owner.</summary>
public sealed class InsatiableDominionPower : TurnEndDebuff
{
    public override IEnumerable<SimState> AfterSideTurnEndLate(SimState state, int owner) =>
        state.TakesPart(owner) ? Hurt(state, owner, SimEngine.Var(state, owner, Name, "Damage", fallback: 0), Unpowered, selfDealt: false) : One(state);
}
