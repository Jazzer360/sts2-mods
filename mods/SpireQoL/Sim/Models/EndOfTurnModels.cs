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
    public override IEnumerable<SimState> BeforeSideTurnEndEarly(SimState s, int owner) =>
        s.TakesPart(owner) && Amount(s, owner) > 0 ? Block(s, owner, Amount(s, owner)) : One(s);
}

/// <summary>HailstormPower.BeforeSideTurnEnd: its amount to every hittable enemy when its owner has its FrostOrbs (Data) Frost orbs.</summary>
public sealed class HailstormPower : EndOfTurnModel
{
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState s, int owner)
    {
        if (!s.TakesPart(owner) || s.Player(owner) is not { } p || Amount(s, owner) <= 0) return One(s);
        return p.Orbs.Count(o => o.Kind == OrbKind.Frost) >= Data(s, owner) ? DamageAll(s, owner, Amount(s, owner)) : One(s);
    }
}

/// <summary>TheBombPower.BeforeSideTurnEnd: ticks down (amount: turns left); on its last its Damage (Data) to every hittable enemy, then it goes.</summary>
public sealed class TheBombPower : EndOfTurnModel
{
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState s, int owner)
    {
        if (!s.TakesPart(owner)) return One(s);
        var turns = Amount(s, owner);
        var damage = Data(s, owner);
        if (turns > 1) { Set(s, owner, turns - 1, damage); return One(s); }
        SimEngine.SetPowers(s, owner, SimEngine.PowersOf(s, owner).Without(Name));
        return damage > 0 ? DamageAll(s, owner, damage) : One(s);
    }
}

/// <summary>ConsumingShadowPower.AfterSideTurnEnd: its owner's last orb is evoked, its amount of times (OrbCmd.EvokeLast).</summary>
public sealed class ConsumingShadowPower : EndOfTurnModel
{
    public override IEnumerable<SimState> AfterSideTurnEnd(SimState s, int owner)
    {
        if (!s.TakesPart(owner)) return One(s);
        IEnumerable<SimState> r = One(s);
        for (var i = 0; i < Amount(s, owner); i++) r = r.SelectMany(x => SimEngine.EvokeLast(x, owner)).ToList();
        return r;
    }
}

/// <summary>
/// Orichalcum (and its fake): BeforeSideTurnEndVeryEarly arms it when its owner has no block (Data 1); BeforeSideTurnEnd then
/// gives its block (amount, unpowered).
/// </summary>
public abstract class OrichalcumBase : EndOfTurnRelic
{
    public override IEnumerable<SimState> BeforeSideTurnEndVeryEarly(SimState s, int owner)
    {
        if (s.TakesPart(owner) && s.Player(owner) is { Block: <= 0 }) Set(s, owner, Amount(s, owner), 1);
        return One(s);
    }
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState s, int owner)
    {
        if (Data(s, owner) != 1) return One(s);
        Set(s, owner, Amount(s, owner), 0);
        return Block(s, owner, Amount(s, owner));
    }
}

public sealed class Orichalcum : OrichalcumBase { }
public sealed class FakeOrichalcum : OrichalcumBase { }

/// <summary>CloakClasp.BeforeSideTurnEnd: its block (amount) per card in its owner's hand (unpowered).</summary>
public sealed class CloakClasp : EndOfTurnRelic
{
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState s, int owner)
    {
        if (!s.TakesPart(owner) || s.Player(owner) is not { HandCount: > 0 } p) return One(s);
        return SimEngine.ProcBlock(s, owner, Name, p.HandCount * Amount(s, owner), Unpowered, label: $"{SimEngine.Title(s, owner, Name)} ×{p.HandCount}");
    }
}

/// <summary>ScreamingFlagon.BeforeSideTurnEnd: its damage (amount) to every hittable enemy when its owner's hand is empty.</summary>
public sealed class ScreamingFlagon : EndOfTurnRelic
{
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState s, int owner) =>
        s.TakesPart(owner) && s.Player(owner) is { HandCount: 0 } ? DamageAll(s, owner, Amount(s, owner)) : One(s);
}

/// <summary>StoneCalendar.BeforeSideTurnEnd: its damage (amount) to every hittable enemy on its DamageTurn (Data: turns until then).</summary>
public sealed class StoneCalendar : EndOfTurnRelic
{
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState s, int owner) =>
        s.TakesPart(owner) && Data(s, owner) == 0 && Amount(s, owner) > 0 ? DamageAll(s, owner, Amount(s, owner)) : One(s);
}

/// <summary>ObsidianVice.BeforeSideTurnEnd: its damage (amount) per card in its owner's hand, to one random hittable enemy (unpowered).</summary>
public sealed class ObsidianVice : EndOfTurnRelic
{
    public override IEnumerable<SimState> BeforeSideTurnEnd(SimState s, int owner)
    {
        if (!s.TakesPart(owner) || s.Player(owner) is not { HandCount: > 0 } p || Amount(s, owner) <= 0) return One(s);
        return SimEngine.ProcDamageRandom(s, owner, Name, Amount(s, owner) * p.HandCount, Unpowered, label: $"{SimEngine.Title(s, owner, Name)} ×{p.HandCount}");
    }
}

/// <summary>ParryingShield.AfterSideTurnEnd: its damage (amount) to a random hittable enemy when its owner has at least its Block (Data).</summary>
public sealed class ParryingShield : EndOfTurnRelic
{
    public override IEnumerable<SimState> AfterSideTurnEnd(SimState s, int owner)
    {
        if (!s.TakesPart(owner) || Amount(s, owner) <= 0 || s.Player(owner) is not { } p || p.Block < Data(s, owner)) return One(s);
        if (owner == Who.You) s.ParryingShieldFired = true;
        return DamageRandom(s, owner, Amount(s, owner));
    }
}

/// <summary>LunarPastry.AfterSideTurnEnd: its owner gains stars (Black Hole reacts: AfterStarsGained).</summary>
public sealed class LunarPastry : EndOfTurnRelic
{
    public override IEnumerable<SimState> AfterSideTurnEnd(SimState s, int owner) =>
        s.TakesPart(owner) ? SimEngine.ProcCardEvent(s, CardEvent.StarsGained, 1, owner, indirect: false) : One(s);
}

// ---- debuffs on a player that hurt them when their side's turn ends (AfterSideTurnEnd, AfterSideTurnEndLate), in listener order
// with that player's other powers.

/// <summary>A debuff that hurts its owner at the end of their turn: its damage through the pipeline on them, named as the game names it.</summary>
public abstract class TurnEndDebuff : EndOfTurnModel
{
    /// <summary>The applier is gone (Data: the applier's id + 100; 0: unknown, taken as alive).</summary>
    protected static bool ApplierDead(SimState s, int owner, string name)
    {
        var data = SimEngine.PowersOf(s, owner).DataOf(name);
        return data >= 100 && s.Enemy(data - 100) is not { Alive: true };
    }

    protected IEnumerable<SimState> Hurt(SimState s, int owner, int amount, SimProps props, bool selfDealt) =>
        amount <= 0 ? One(s) : SimEngine.ProcDamagePlayer(s, owner, Name, owner, amount, props, noDealer: !selfDealt, label: SimEngine.Title(s, owner, Name));
}

/// <summary>ConstrictPower.AfterSideTurnEnd: its amount to its owner (unpowered, dealt by its owner); it goes when its applier dies (AfterDeath).</summary>
public sealed class ConstrictPower : TurnEndDebuff
{
    public override IEnumerable<SimState> AfterSideTurnEnd(SimState s, int owner) =>
        s.TakesPart(owner) && !ApplierDead(s, owner, Name) ? Hurt(s, owner, Amount(s, owner), Unpowered, selfDealt: true) : One(s);
}

/// <summary>MagicBombPower.AfterSideTurnEnd: while its applier lives, its amount to its owner (unpowered, dealt by its owner), then it goes.</summary>
public sealed class MagicBombPower : TurnEndDebuff
{
    public override IEnumerable<SimState> AfterSideTurnEnd(SimState s, int owner)
    {
        if (!s.TakesPart(owner) || ApplierDead(s, owner, Name)) return One(s);
        var amount = Amount(s, owner);
        SimEngine.SetPowers(s, owner, SimEngine.PowersOf(s, owner).Without(Name));
        return Hurt(s, owner, amount, Unpowered, selfDealt: true);
    }
}

/// <summary>DemisePower.AfterSideTurnEnd: its amount to its owner, unblockable (no dealer).</summary>
public sealed class DemisePower : TurnEndDebuff
{
    public override IEnumerable<SimState> AfterSideTurnEnd(SimState s, int owner) =>
        s.TakesPart(owner) ? Hurt(s, owner, Amount(s, owner), SimProps.Unblockable | Unpowered, selfDealt: false) : One(s);
}

/// <summary>DisintegrationPower.AfterSideTurnEndLate: its amount to its owner (unpowered, dealt by its owner).</summary>
public sealed class DisintegrationPower : TurnEndDebuff
{
    public override IEnumerable<SimState> AfterSideTurnEndLate(SimState s, int owner) =>
        s.TakesPart(owner) ? Hurt(s, owner, Amount(s, owner), Unpowered, selfDealt: true) : One(s);
}

/// <summary>InsatiableDominionPower.AfterSideTurnEndLate: its Damage (2 + its owner's turn number, unpowered, no dealer) to its owner.</summary>
public sealed class InsatiableDominionPower : TurnEndDebuff
{
    public override IEnumerable<SimState> AfterSideTurnEndLate(SimState s, int owner) =>
        s.TakesPart(owner) ? Hurt(s, owner, SimEngine.Var(s, owner, Name, "Damage", 0), Unpowered, selfDealt: false) : One(s);
}
