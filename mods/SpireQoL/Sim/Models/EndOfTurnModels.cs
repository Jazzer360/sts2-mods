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

/// <summary>HailstormPower.BeforeSideTurnEnd: its amount to every hittable enemy when its owner has its FrostOrbs (Data) Frost orbs.</summary>
public sealed class HailstormPower : EndOfTurnModel
{
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState state, int owner)
    {
        if (!state.TakesPart(owner) || state.Player(owner) is not { } player || Amount(state, owner) <= 0) return One(state);
        return player.Orbs.Count(orb => orb.Kind == OrbKind.Frost) >= Data(state, owner) ? DamageAll(state, owner, Amount(state, owner)) : One(state);
    }
}

/// <summary>TheBombPower.BeforeSideTurnEnd: ticks down (amount: turns left); on its last its Damage (Data) to every hittable enemy, then it goes.</summary>
public sealed class TheBombPower : EndOfTurnModel
{
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState state, int owner)
    {
        if (!state.TakesPart(owner)) return One(state);
        var turns = Amount(state, owner);
        var damage = Data(state, owner);
        if (turns > 1) { Set(state, owner, turns - 1, damage); return One(state); }
        SimEngine.SetPowers(state, owner, SimEngine.PowersOf(state, owner).Without(ThisPower));
        return damage > 0 ? DamageAll(state, owner, damage) : One(state);
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

/// <summary>
/// Orichalcum (and its fake): BeforeSideTurnEndVeryEarly arms it when its owner has no block (Data 1); BeforeSideTurnEnd then
/// gives its block (amount, unpowered).
/// </summary>
public abstract class OrichalcumBase : EndOfTurnRelic
{
    public override IEnumerable<SimState> BeforeSideTurnEndVeryEarly(SimState state, int owner)
    {
        if (state.TakesPart(owner) && state.Player(owner) is { Block: <= 0 }) Set(state, owner, Amount(state, owner), 1);
        return One(state);
    }
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState state, int owner)
    {
        if (Data(state, owner) != 1) return One(state);
        Set(state, owner, Amount(state, owner), 0);
        return Block(state, owner, Amount(state, owner));
    }
}

public sealed class Orichalcum : OrichalcumBase { }
public sealed class FakeOrichalcum : OrichalcumBase { }

/// <summary>CloakClasp.BeforeSideTurnEnd: its block (amount) per card in its owner's hand (unpowered).</summary>
public sealed class CloakClasp : EndOfTurnRelic
{
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState state, int owner)
    {
        if (!state.TakesPart(owner) || state.Player(owner) is not { HandCount: > 0 } player) return One(state);
        return SimEngine.ProcBlock(state, owner, Name, player.HandCount * Amount(state, owner), Unpowered, label: $"{SimEngine.Title(state, owner, Name)} ×{player.HandCount}");
    }
}

/// <summary>ScreamingFlagon.BeforeSideTurnEnd: its damage (amount) to every hittable enemy when its owner's hand is empty.</summary>
public sealed class ScreamingFlagon : EndOfTurnRelic
{
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState state, int owner) =>
        state.TakesPart(owner) && state.Player(owner) is { HandCount: 0 } ? DamageAll(state, owner, Amount(state, owner)) : One(state);
}

/// <summary>StoneCalendar.BeforeSideTurnEnd: its damage (amount) to every hittable enemy on its DamageTurn (Data: turns until then).</summary>
public sealed class StoneCalendar : EndOfTurnRelic
{
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState state, int owner) =>
        state.TakesPart(owner) && Data(state, owner) == 0 && Amount(state, owner) > 0 ? DamageAll(state, owner, Amount(state, owner)) : One(state);
}

/// <summary>ObsidianVice.BeforeSideTurnEnd: its damage (amount) per card in its owner's hand, to one random hittable enemy (unpowered).</summary>
public sealed class ObsidianVice : EndOfTurnRelic
{
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState state, int owner)
    {
        if (!state.TakesPart(owner) || state.Player(owner) is not { HandCount: > 0 } player || Amount(state, owner) <= 0) return One(state);
        return SimEngine.ProcDamageRandom(state, owner, Name, Amount(state, owner) * player.HandCount, Unpowered, label: $"{SimEngine.Title(state, owner, Name)} ×{player.HandCount}");
    }
}

/// <summary>ParryingShield.AfterSideTurnEnd: its damage (amount) to a random hittable enemy when its owner has at least its Block (Data).</summary>
public sealed class ParryingShield : EndOfTurnRelic
{
    /// <summary>Plain when this play makes it fire for sure and ending the turn now might not; crossed when it would for sure now and the play can stop it.</summary>
    public override OutcomeRule? OutcomeTab(SimState start, SimOutcome now, int owner) =>
        owner != Who.You ? null
        : !now.ParryingShieldInAll ? new(false, outcome => outcome.ParryingShieldInAll)
        : new(true, outcome => !outcome.ParryingShieldInAll);

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
        state.TakesPart(owner) ? SimEngine.ProcCardEvent(state, CardEvent.StarsGained, 1, owner, indirect: false) : One(state);
}

// ---- debuffs on a player that hurt them when their side's turn ends (AfterSideTurnEnd, AfterSideTurnEndLate), in listener order
// with that player's other powers.

/// <summary>A debuff that hurts its owner at the end of their turn: its damage through the pipeline on them, named as the game names it.</summary>
public abstract class TurnEndDebuff : EndOfTurnModel
{
    /// <summary>The applier is gone (Data: the applier's id + 100; 0: unknown, taken as alive).</summary>
    protected static bool ApplierDead(SimState state, int owner, PowerId name)
    {
        var data = SimEngine.PowersOf(state, owner).DataOf(name);
        return data >= 100 && state.Enemy(data - 100) is not { Alive: true };
    }

    protected IEnumerable<SimState> Hurt(SimState state, int owner, int amount, SimProps props, bool selfDealt) =>
        amount <= 0 ? One(state) : SimEngine.ProcDamagePlayer(state, owner, Name, owner, amount, props, noDealer: !selfDealt, label: SimEngine.Title(state, owner, Name));
}

/// <summary>ConstrictPower.AfterSideTurnEnd: its amount to its owner (unpowered, dealt by its owner); it goes when its applier dies (AfterDeath).</summary>
public sealed class ConstrictPower : TurnEndDebuff
{
    public override IEnumerable<SimState> AfterSideTurnEnd(SimState state, int owner) =>
        state.TakesPart(owner) && !ApplierDead(state, owner, ThisPower) ? Hurt(state, owner, Amount(state, owner), Unpowered, selfDealt: true) : One(state);
}

/// <summary>MagicBombPower.AfterSideTurnEnd: while its applier lives, its amount to its owner (unpowered, dealt by its owner), then it goes.</summary>
public sealed class MagicBombPower : TurnEndDebuff
{
    public override IEnumerable<SimState> AfterSideTurnEnd(SimState state, int owner)
    {
        if (!state.TakesPart(owner) || ApplierDead(state, owner, ThisPower)) return One(state);
        var amount = Amount(state, owner);
        SimEngine.SetPowers(state, owner, SimEngine.PowersOf(state, owner).Without(ThisPower));
        return Hurt(state, owner, amount, Unpowered, selfDealt: true);
    }
}

/// <summary>DemisePower.AfterSideTurnEnd: its amount to its owner, unblockable (no dealer).</summary>
public sealed class DemisePower : TurnEndDebuff
{
    public override IEnumerable<SimState> AfterSideTurnEnd(SimState state, int owner) =>
        state.TakesPart(owner) ? Hurt(state, owner, Amount(state, owner), SimProps.Unblockable | Unpowered, selfDealt: false) : One(state);
}

/// <summary>DisintegrationPower.AfterSideTurnEndLate: its amount to its owner (unpowered, dealt by its owner).</summary>
public sealed class DisintegrationPower : TurnEndDebuff
{
    public override IEnumerable<SimState> AfterSideTurnEndLate(SimState state, int owner) =>
        state.TakesPart(owner) ? Hurt(state, owner, Amount(state, owner), Unpowered, selfDealt: true) : One(state);
}

/// <summary>InsatiableDominionPower.AfterSideTurnEndLate: its Damage (2 + its owner's turn number, unpowered, no dealer) to its owner.</summary>
public sealed class InsatiableDominionPower : TurnEndDebuff
{
    public override IEnumerable<SimState> AfterSideTurnEndLate(SimState state, int owner) =>
        state.TakesPart(owner) ? Hurt(state, owner, SimEngine.Var(state, owner, Name, "Damage", 0), Unpowered, selfDealt: false) : One(state);
}
