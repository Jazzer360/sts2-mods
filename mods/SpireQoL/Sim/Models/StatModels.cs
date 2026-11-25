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

// Pen Nib is read from its code (ReadModel, decision #272): the 10th Attack kept as its card play's identity.
