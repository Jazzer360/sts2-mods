namespace SpireQoL.Sim.Models;

// The damage and block sources that can change during your turn (docs/SIMULATOR.md "Coverage"), each from its
// decompiled body (v0.113.1). Amounts live on the owner's PowerSet; Data holds what a model keeps between hooks.

// Strength, Dexterity, Frail and Double Damage are read from their code (ReadModel, decision #195).

// Vigor and Gigantification are read from their code (ReadModel, decision #228).

// Aim is read from its code (ReadModel, decision #232).

// Quietus is read from its code (ReadModel, decision #242).

// Slow is read from its code (ReadModel, decision #229).

// Lethality is read from its code (ReadModel, decision #231).

/// <summary>PhantomBladesPower.ModifyDamageAdditive: its amount on the owner's first Shiv each turn (no Shiv play finished yet).</summary>
public sealed class PhantomBladesPower : SimModel
{
    public override decimal ModifyDamageAdditive(SimState state, int owner, DamageCtx damage)
    {
        if (!damage.Props.IsPoweredAttack() || damage.Card == null || !damage.Card.Is(SimCardTag.Shiv) || damage.Dealer != owner) return 0m;
        var player = state.Player(owner);
        return player != null && player.Turn[SimKeys.Plays(PlayStage.Finished, SimCardTag.Shiv)] > 0 ? 0m : SimEngine.PowerOn(state, owner, ThisPower);
    }
}

/// <summary>
/// Pen Nib (relic): Amount is AttacksPlayed (0-9). Data 1 while a card the game is playing right now is bound to be doubled
/// (AttackToDouble, read mid-play: no card simulated here is that card, so none is doubled), 2 while a play simulated here is.
/// BeforeCardPlayed counts your Attack plays (each play of a replay); the one that brings it to 0 binds itself (and flashes);
/// its powered hits from that play deal double (ModifyDamageMultiplicative); AfterCardPlayed of that play unbinds.
/// </summary>
public sealed class PenNib : SimModel
{
    /// <summary>Its Data: the card the game is playing right now is the doubled one (AttackToDouble), or a play simulated here is.</summary>
    public const int GameCardBound = 1, SimulatedPlayBound = 2;
    public override TabKind? Tab => TabKind.Trigger;
    public override decimal ModifyDamageMultiplicative(SimState state, int owner, DamageCtx damage)
    {
        if (!damage.Props.IsPoweredAttack() || damage.Card == null) return 1m;
        if (!(damage.Dealer == owner || (owner == Who.You && damage.Dealer == Who.YourOsty))) return 1m;
        var relics = SimEngine.RelicsOf(state, owner);
        if (relics.DataOf(ThisRelic) == SimulatedPlayBound) return damage.Play != null ? 2m : 1m;
        if (relics.DataOf(ThisRelic) == GameCardBound) return 1m; // the game's card mid-play is the doubled one
        return damage.Play == null && relics[ThisRelic] == 9 ? 2m : 1m; // not being played (a preview): the next attack is the 10th
    }
    public override IEnumerable<SimState> BeforeCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        if (play.Card.Type != SimCardType.Attack || play.Owner != owner) return One(state);
        var relics = SimEngine.RelicsOf(state, owner);
        var count = (relics[ThisRelic] + 1) % 10;
        SimEngine.SetRelics(state, owner, relics.With(ThisRelic, count, count == 0 ? SimulatedPlayBound : relics.DataOf(ThisRelic)));
        if (count == 0) state.LogFired(Name, amount: 0, owner);
        return One(state);
    }
    public override IEnumerable<SimState> AfterCardPlayed(SimState state, int owner, SimCardPlay play)
    {
        var relics = SimEngine.RelicsOf(state, owner);
        if (relics.DataOf(ThisRelic) == SimulatedPlayBound && play.Owner == owner) SimEngine.SetRelics(state, owner, relics.With(ThisRelic, relics[ThisRelic], data: 0));
        return One(state);
    }
}
