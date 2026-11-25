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

/// <summary>PlatingPower.BeforeSideTurnEndEarly: its amount of block (unpowered).</summary>
public sealed class PlatingPower : EndOfTurnModel
{
    public override IEnumerable<SimState> BeforeSideTurnEndEarly(SimState state, int owner) =>
        state.TakesPart(owner) && Amount(state, owner) > 0 ? Block(state, owner, Amount(state, owner)) : One(state);
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

// Magic Bomb is read from its code (ReadModel, decision #279).

/// <summary>InsatiableDominionPower.AfterSideTurnEndLate: its Damage (2 + its owner's turn number, unpowered, no dealer) to its owner.</summary>
public sealed class InsatiableDominionPower : TurnEndDebuff
{
    public override IEnumerable<SimState> AfterSideTurnEndLate(SimState state, int owner) =>
        state.TakesPart(owner) ? Hurt(state, owner, SimEngine.Var(state, owner, Name, "Damage", fallback: 0), Unpowered, selfDealt: false) : One(state);
}
