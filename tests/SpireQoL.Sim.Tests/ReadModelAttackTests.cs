using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// An attack's hooks read from the code (decision #228): BeforeAttack and AfterAttack are told of the attack (its id, attacker, props, card), an
/// attack its own data keeps is its id there (with its card and attacker beside it), a power an attack spends acts on that play, and its own amount
/// changed (PowerCmd.ModifyAmount) goes at 0.
/// </summary>
public class ReadModelAttackTests
{
    private static SimState Board()
    {
        var state = new SimState { You = new SimPlayer { Hp = 50, MaxHp = 70, OstyHp = 10 } };
        state.Enemies.Add(new SimEnemy { Id = 0, Name = "E0", Hp = 30 });
        return state;
    }

    private static readonly SimCard Strike = new("Strike", SimCardType.Attack);
    private static readonly SimCard Bash = new("Bash", SimCardType.Attack);

    private static SimState During(SimState state, SimCard card, int attack)
    {
        state.CurrentPlay = new SimCardPlay(card, Who.You, Target: 0, PlayIndex: 0, PlayCount: 1, AutoPlay: false);
        state.CurrentAttack = attack;
        return state;
    }

    [Fact]
    public void GigantificationTriplesTheAttackItKeepsThenLetsItGo()
    {
        // GigantificationPower.BeforeAttack keeps its owner's first powered attack from an Attack card (commandToModify = command);
        // ModifyDamageMultiplicative is 3 for that attack's card (or any while none is kept); AfterAttack lets it go and takes a stack.
        var giant = Assert.IsType<ReadModel>(SimModels.Get(PowerId.Gigantification.Name));
        var state = Board();
        state.You.Powers = state.You.Powers.With(PowerId.Gigantification, amount: 2);
        During(state, Strike, attack: 1);
        var kept = Assert.Single(giant.BeforeAttack(state, Who.You, new AttackInfo(Id: 1, Who.You, SimProps.Move, Strike)));
        Assert.Equal(1, kept.You.Fields[giant.FieldKey("Data.commandToModify")]);
        var play = kept.CurrentPlay!;
        Assert.Equal(3m, giant.ModifyDamageMultiplicative(kept, Who.You, new DamageCtx(Who.You, Target: 0, SimProps.Move, Strike, play)));
        // Another card's damage while this attack is kept: no more.
        kept.CurrentAttack = 0;
        var other = new SimCardPlay(Bash, Who.You, Target: 0, PlayIndex: 0, PlayCount: 1, AutoPlay: false);
        Assert.Equal(1m, giant.ModifyDamageMultiplicative(kept, Who.You, new DamageCtx(Who.You, Target: 0, SimProps.Move, Bash, other)));
        kept.CurrentAttack = 1;
        var spent = Assert.Single(giant.AfterAttack(kept, Who.You, new AttackInfo(Id: 1, Who.You, SimProps.Move, Strike)));
        Assert.Equal(0, spent.You.Fields[giant.FieldKey("Data.commandToModify")]);
        Assert.Equal(1, spent.You.Powers[PowerId.Gigantification]);
        Assert.True(spent.Fired.Has(PowerId.Gigantification.Name));
    }

    [Fact]
    public void VigorTakesBackWhatItStartedWithAndGoesAtZero()
    {
        // VigorPower.BeforeAttack keeps the attack and its amount then (amountWhenAttackStarted); AfterAttack: PowerCmd.ModifyAmount(this, -that):
        // what was added since stays, and at 0 it goes.
        var vigor = Assert.IsType<ReadModel>(SimModels.Get(PowerId.Vigor.Name));
        var state = Board();
        state.You.Powers = state.You.Powers.With(PowerId.Vigor, amount: 5);
        During(state, Strike, attack: 1);
        var attack = new AttackInfo(Id: 1, Who.You, SimProps.Move, Strike);
        var kept = Assert.Single(vigor.BeforeAttack(state, Who.You, attack));
        Assert.Equal(5m, vigor.ModifyDamageAdditive(kept, Who.You, new DamageCtx(Who.You, Target: 0, SimProps.Move, Strike, kept.CurrentPlay)));
        var gone = vigor.AfterAttack(kept.Clone(), Who.You, attack).Single();
        Assert.False(gone.You.Powers.Has(PowerId.Vigor));
        // Vigor gained during the attack stays once the attack takes back its 5.
        kept.You.Powers = kept.You.Powers.With(PowerId.Vigor, amount: 8);
        var left = vigor.AfterAttack(kept, Who.You, attack).Single();
        Assert.Equal(3, left.You.Powers[PowerId.Vigor]);
    }

    [Fact]
    public void BoneFluteBlocksForAnAttackItsOwnersOstyMakes()
    {
        // BoneFlute.AfterAttack: command.Attacker?.Monster is Osty (the type Player.Osty gets: the simulator's YourOsty) and its PetOwner is the owner.
        var flute = Assert.IsType<ReadModel>(SimModels.Get(RelicId.BoneFlute.Name));
        var state = Board();
        state.You.Relics = state.You.Relics.With(RelicId.BoneFlute, amount: 0, data: 0);
        During(state, Strike, attack: 1);
        var byOsty = flute.AfterAttack(state.Clone(), Who.You, new AttackInfo(Id: 1, Who.YourOsty, SimProps.Move, Strike)).Single();
        Assert.True(byOsty.You.Block > 0);
        var byYou = flute.AfterAttack(state.Clone(), Who.You, new AttackInfo(Id: 1, Who.You, SimProps.Move, Strike)).Single();
        Assert.Equal(0, byYou.You.Block);
    }
}
