using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The creatures a read model's code names (decision #195, round four: HookCreature): the dealer and target of the damage it's asked about, the
/// creature that died, its applier, a pet's player; and what its code asks of them (there, a player, a monster, a power or a relic, the same one,
/// the same side), each as the game's Creature answers it. A value that isn't a whole number (Colossus' 0.5) is the game's, not its whole part.
/// </summary>
public class ReadModelCreatureTests
{
    private static SimState Board()
    {
        var state = new SimState { You = new SimPlayer { Hp = 50, MaxHp = 70 } };
        state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 60 });
        state.Enemies.Add(new SimEnemy { Id = 0, Name = "E0", Hp = 30 });
        state.Enemies.Add(new SimEnemy { Id = 1, Name = "E1", Hp = 25 });
        return state;
    }

    private static HookFactValue Fact(HookFact fact, HookWho who, string? of = null, HookWho? other = null) =>
        new(fact) { Creature = new HookCreature(who), Of = of, Other = other is { } otherWho ? new HookCreature(otherWho) : null };

    /// <summary>A power whose ModifyDamageMultiplicative is 2 when <paramref name="test"/> holds, else 1.</summary>
    private static ReadModel Asking(HookValue test) => new(new ModelSpec("TestAskingPower", ModelKind.Power,
        new[] { new HookProgram(HookName.ModifyDamageMultiplicative, Array.Empty<HookStep>()) { Returns = new HookWays(new[] { new HookWay(test, new HookConstant(2m)), new HookWay(new HookConstant(1m), new HookConstant(1m)) }) } }));

    private static decimal Asked(HookValue test, SimState state, int owner, int? dealer, int target) =>
        Asking(test).ModifyDamageMultiplicative(state, owner, new DamageCtx(dealer, target, SimProps.Move, Card: null, Play: null));

    [Fact]
    public void ADealerIsThereUnlessNoOneDealsIt()
    {
        var there = Fact(HookFact.CreatureThere, HookWho.Dealer);
        Assert.Equal(2m, Asked(there, Board(), owner: 0, dealer: Who.You, target: 0));
        Assert.Equal(1m, Asked(there, Board(), owner: 0, dealer: null, target: 0));
    }

    [Fact]
    public void APowerOnTheDealerOrTheTargetIsAskedOfThatCreature()
    {
        var state = Board();
        state.Teammates[0].Powers = state.Teammates[0].Powers.With(PowerId.Vulnerable, amount: 1);
        var dealerVulnerable = Fact(HookFact.CreatureHasPower, HookWho.Dealer, PowerId.Vulnerable.Name);
        Assert.Equal(2m, Asked(dealerVulnerable, state, owner: 0, dealer: Who.Teammate(0), target: 0));
        Assert.Equal(1m, Asked(dealerVulnerable, state, owner: 0, dealer: Who.You, target: 0));
        Assert.Equal(1m, Asked(dealerVulnerable, state, owner: 0, dealer: null, target: 0));
        var targetVulnerable = Fact(HookFact.CreatureHasPower, HookWho.Target, PowerId.Vulnerable.Name);
        Assert.Equal(2m, Asked(targetVulnerable, state, owner: Who.You, dealer: Who.You, target: Who.Teammate(0)));
    }

    [Fact]
    public void ATeammatesArrowsMakeAnEnemyImpaled()
    {
        // The game has one ImpaledPower; the simulator keeps a teammate's arrows apart (ImpaledPowerOther): either one is Impaled.
        var state = Board();
        state.Enemies[1].Powers = state.Enemies[1].Powers.With(SimEngine.ImpaledOther, amount: 1);
        Assert.Equal(2m, Asked(Fact(HookFact.CreatureHasPower, HookWho.Target, PowerId.Impaled.Name), state, owner: Who.You, dealer: Who.You, target: 1));
    }

    [Fact]
    public void ARelicIsAPlayersAndOstyIsNoPlayer()
    {
        var state = Board();
        state.You.Relics = state.You.Relics.With(RelicId.PaperPhrog, amount: 0, data: 0);
        var phrog = Fact(HookFact.CreatureHasRelic, HookWho.Dealer, RelicId.PaperPhrog.Name);
        Assert.Equal(2m, Asked(phrog, state, owner: 0, dealer: Who.You, target: 0));
        // Osty's creature has no Player (Creature.Player is null for a pet): his hits have no Paper Phrog, though his player has it.
        Assert.Equal(1m, Asked(phrog, state, owner: 0, dealer: Who.YourOsty, target: 0));
        Assert.Equal(1m, Asked(Fact(HookFact.CreatureIsPlayer, HookWho.Dealer), state, owner: 0, dealer: Who.YourOsty, target: 0));
        Assert.Equal(2m, Asked(Fact(HookFact.CreatureIsMonster, HookWho.Dealer), state, owner: 0, dealer: Who.YourOsty, target: 0));
    }

    [Fact]
    public void APetsPlayerIsItsOwner()
    {
        // dealer.PetOwner?.Creature.GetPower<CrueltyPower>(): Osty's player's Cruelty; no one's for a creature that's no pet.
        var state = Board();
        state.You.Powers = state.You.Powers.With(PowerId.Cruelty, amount: 25);
        var cruelty = new HookFactValue(HookFact.CreaturePowerAmount) { Creature = new HookCreature(HookWho.Dealer) { PetOwner = true }, Of = PowerId.Cruelty.Name };
        var model = new ReadModel(new ModelSpec("TestAskingPower", ModelKind.Power, new[] { new HookProgram(HookName.ModifyDamageAdditive, Array.Empty<HookStep>()) { Returns = cruelty } }));
        Assert.Equal(25m, model.ModifyDamageAdditive(state, 0, new DamageCtx(Who.YourOsty, 0, SimProps.Move, Card: null, Play: null)));
        Assert.Equal(0m, model.ModifyDamageAdditive(state, 0, new DamageCtx(Who.You, 0, SimProps.Move, Card: null, Play: null)));
    }

    [Fact]
    public void SidesAreThePlayersOrTheEnemies()
    {
        var sameSide = Fact(HookFact.SameSide, HookWho.Owner, other: HookWho.Dealer);
        Assert.Equal(2m, Asked(sameSide, Board(), owner: 0, dealer: 1, target: Who.You));
        Assert.Equal(1m, Asked(sameSide, Board(), owner: 0, dealer: Who.You, target: 0));
        Assert.Equal(2m, Asked(sameSide, Board(), owner: Who.You, dealer: Who.YourOsty, target: 0));
        Assert.Equal(1m, Asked(sameSide, Board(), owner: 0, dealer: null, target: Who.You));
        var playersSide = Fact(HookFact.CreatureOnPlayersSide, HookWho.Owner);
        Assert.Equal(2m, Asked(playersSide, Board(), owner: Who.Teammate(0), dealer: null, target: 0));
        Assert.Equal(1m, Asked(playersSide, Board(), owner: 1, dealer: null, target: 0));
    }

    [Fact]
    public void AValueThatIsntWholeIsTheGames()
    {
        // Colossus' DamageDecrease is 0.5: its whole part (0) is what SimState.Vars keeps; the read model reads 0.5.
        var model = new ReadModel(new ModelSpec("TestFractionPower", ModelKind.Power,
            new[] { new HookProgram(HookName.ModifyDamageMultiplicative, Array.Empty<HookStep>()) { Returns = new HookVar("DamageDecrease") { Canonical = 0.5m } } }));
        var state = Board();
        var damage = new DamageCtx(Who.You, 0, SimProps.Move, Card: null, Play: null);
        Assert.Equal(0.5m, model.ModifyDamageMultiplicative(state, 0, damage));
        state.Vars["0:TestFractionPower:DamageDecrease"] = 0;
        state.Fractions["0:TestFractionPower:DamageDecrease"] = 0.25m;
        Assert.Equal(0.25m, model.ModifyDamageMultiplicative(state, 0, damage));
    }

    [Fact]
    public void LeadershipRaisesItsAlliesAttacksNotItsOwn()
    {
        // LeadershipPower.ModifyDamageAdditive, read from its code: base.Owner == dealer, base.Owner.Side != dealer.Side, then its amount.
        var leadership = Assert.IsType<ReadModel>(SimModels.Get(PowerId.Leadership.Name));
        var state = Board();
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.Leadership, amount: 3);
        decimal Added(int? dealer) => leadership.ModifyDamageAdditive(state, 0, new DamageCtx(dealer, Who.You, SimProps.Move, Card: null, Play: null));
        Assert.Equal(3m, Added(dealer: 1));
        Assert.Equal(0m, Added(dealer: 0));
        Assert.Equal(0m, Added(dealer: Who.Teammate(0)));
    }
}
