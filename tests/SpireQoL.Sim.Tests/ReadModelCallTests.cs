using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Vulnerable and Weak, read from their code (decision #195, round four): each calls into another model's own method on a model it gets from a
/// creature (the dealer's Paper Phrog and Cruelty, the target's Paper Krane, Debilitate on its owner), read from that method's code with its
/// base.Owner the creature it was got from. Cruelty is the dealer's own, or its player's for a pet (dealer.GetPower ?? dealer.PetOwner's); a pet
/// has no Paper Phrog (its creature has no Player).
/// </summary>
public class ReadModelCallTests
{
    private static SimState Board()
    {
        var state = new SimState { You = new SimPlayer { Hp = 50, MaxHp = 70 } };
        state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 60 });
        state.Enemies.Add(new SimEnemy { Id = 0, Name = "E0", Hp = 30 });
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.Vulnerable, amount: 2).With(PowerId.Weak, amount: 2);
        return state;
    }

    private static decimal Vulnerable(SimState state, int dealer) =>
        Assert.IsType<ReadModel>(SimModels.Get(PowerId.Vulnerable.Name)).ModifyDamageMultiplicative(state, 0, new DamageCtx(dealer, 0, SimProps.Move, Card: null, Play: null));

    private static decimal Weak(SimState state, int target) =>
        Assert.IsType<ReadModel>(SimModels.Get(PowerId.Weak.Name)).ModifyDamageMultiplicative(state, 0, new DamageCtx(0, target, SimProps.Move, Card: null, Play: null));

    [Fact]
    public void VulnerableIsOneAndAHalf()
    {
        Assert.Equal(1.5m, Vulnerable(Board(), dealer: Who.You));
        var unpowered = Assert.IsType<ReadModel>(SimModels.Get(PowerId.Vulnerable.Name)).ModifyDamageMultiplicative(Board(), 0, new DamageCtx(Who.You, 0, SimProps.Move | SimProps.Unpowered, Card: null, Play: null));
        Assert.Equal(1m, unpowered);
    }

    [Fact]
    public void TheDealersCrueltyAddsItsAmount()
    {
        // dealer.GetPower<CrueltyPower>() ?? dealer.PetOwner?.Creature.GetPower<CrueltyPower>(): the first is the dealer's own.
        var state = Board();
        state.You.Powers = state.You.Powers.With(PowerId.Cruelty, amount: 50);
        Assert.Equal(2.0m, Vulnerable(state, dealer: Who.You));
        // Osty's hits: his player's Cruelty (the ?? 's second way).
        Assert.Equal(2.0m, Vulnerable(state, dealer: Who.YourOsty));
        Assert.Equal(1.5m, Vulnerable(state, dealer: Who.Teammate(0)));
    }

    [Fact]
    public void PaperPhrogIsTheDealingPlayersNotTheirPets()
    {
        var state = Board();
        state.You.Relics = state.You.Relics.With(RelicId.PaperPhrog, amount: 0, data: 0);
        Assert.Equal(1.75m, Vulnerable(state, dealer: Who.You));
        // dealer.Player?.GetRelic<PaperPhrog>(): Osty's creature has no Player.
        Assert.Equal(1.5m, Vulnerable(state, dealer: Who.YourOsty));
    }

    [Fact]
    public void DebilitateDoublesTheBonus()
    {
        var state = Board();
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.Debilitate, amount: 1);
        state.You.Relics = state.You.Relics.With(RelicId.PaperPhrog, amount: 0, data: 0);
        // 1.75 with Phrog, then Debilitate on the owner: amount + (amount - 1).
        Assert.Equal(2.5m, Vulnerable(state, dealer: Who.You));
    }

    [Fact]
    public void WeakIsThreeQuartersLessPaperKraneOnTheTarget()
    {
        var state = Board();
        Assert.Equal(0.75m, Weak(state, target: Who.You));
        state.You.Relics = state.You.Relics.With(RelicId.PaperKrane, amount: 0, data: 0);
        Assert.Equal(0.6m, Weak(state, target: Who.You));
        Assert.Equal(0.75m, Weak(state, target: Who.Teammate(0)));
        // Debilitate on the dealer: amount - (1 - amount).
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.Debilitate, amount: 1);
        Assert.Equal(0.2m, Weak(state, target: Who.You));
    }
}
