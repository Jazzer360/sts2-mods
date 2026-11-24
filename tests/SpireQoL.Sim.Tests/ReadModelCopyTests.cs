using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A read power kept in copies (decision #195, round five: PowerInstanceType, ModelSpec.Copies): each application of an Instanced power is a copy
/// of its own, and an InstancedPerApplier power's application stacks onto its applier's copy or starts one (PowerCmd.FindExistingInstanceForStacking).
/// Each copy keeps its amount, its applier and its own state (SimPlayer.Fields "Name@id"), runs its hooks on its own in the order they landed, and
/// goes on its own; the power's entry holds their total.
/// </summary>
public class ReadModelCopyTests
{
    private static SimState Board()
    {
        var state = new SimState { You = new SimPlayer { Hp = 50, MaxHp = 70, HandCount = 3, Energy = 3 } };
        state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 60, HandCount = 2, Energy = 2 });
        state.Enemies.Add(new SimEnemy { Id = 0, Name = "E0", Hp = 40, Hits = new List<int> { 6 } });
        return state;
    }

    private static SimCardPlay Played(int owner) => new(new SimCard("Played", SimCardType.Skill), Owner: owner, Target: 0, PlayIndex: 0, PlayCount: 1, AutoPlay: false);

    private static SimState Play(SimModel model, SimState state, int owner, SimCardPlay play)
    {
        state.CurrentPlay = play;
        var before = Assert.Single(model.BeforeCardPlayed(state, owner, play));
        return Assert.Single(model.AfterCardPlayed(before, owner, play));
    }

    [Fact]
    public void EachPlayersStrangleReactsToItsOwnApplier()
    {
        // StranglePower is one instance per applier: your Strangle and your teammate's on one enemy are two, each dealing its own amount on its
        // applier's plays. Kept as one (its applier the Data of the power's entry), the teammate's plays dealt nothing.
        var strangle = Assert.IsType<ReadModel>(SimModels.Get(PowerId.Strangle.Name));
        Assert.Equal(PowerCopies.PerApplier, strangle.Spec.Copies);
        var state = Board();
        Assert.True(SimEngine.ApplyPower(state, Who.You, 0, PowerId.Strangle, amount: 2, debuff: true));
        Assert.True(SimEngine.ApplyPower(state, Who.Teammate(0), 0, PowerId.Strangle, amount: 3, debuff: true));
        Assert.True(SimEngine.ApplyPower(state, Who.You, 0, PowerId.Strangle, amount: 2, debuff: true));
        Assert.Equal(7, state.Enemies[0].Powers[PowerId.Strangle]);
        Assert.Equal(new[] { 1, 2 }, strangle.CopiesOrOne(state, 0));
        var afterYours = Play(strangle, state.Clone(), 0, Played(Who.You));
        Assert.Equal(40 - 4, afterYours.Enemies[0].Hp);
        var afterTheirs = Play(strangle, state.Clone(), 0, Played(Who.Teammate(0)));
        Assert.Equal(40 - 3, afterTheirs.Enemies[0].Hp);
    }

    [Fact]
    public void AStrangleKeptAsOneIsItsOnlyCopy()
    {
        // The adapter lists every copy; a state built with the power's entry alone (its applier as its Data) is the one copy.
        var strangle = Assert.IsType<ReadModel>(SimModels.Get(PowerId.Strangle.Name));
        var state = Board();
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.Strangle, amount: 4, data: DataCreature.Of(Who.You));
        Assert.Equal(new[] { 1 }, strangle.CopiesOrOne(state, 0));
        Assert.Equal(40 - 4, Play(strangle, state, 0, Played(Who.You)).Enemies[0].Hp);
    }

    [Fact]
    public void EachKnockdownMultipliesOnItsOwnAndSparesItsApplier()
    {
        // KnockdownPower is Instanced: each copy multiplies powered attacks on its owner by its own amount, except its own applier's.
        var knockdown = Assert.IsType<ReadModel>(SimModels.Get(PowerId.Knockdown.Name));
        Assert.Equal(PowerCopies.EachApplication, knockdown.Spec.Copies);
        var state = Board();
        Assert.True(SimEngine.ApplyPower(state, Who.You, 0, PowerId.Knockdown, amount: 2, debuff: true));
        Assert.True(SimEngine.ApplyPower(state, Who.Teammate(0), 0, PowerId.Knockdown, amount: 3, debuff: true));
        Assert.Equal(new[] { 1, 2 }, knockdown.CopiesOrOne(state, 0));
        var card = new SimCard("Strike", SimCardType.Attack);
        decimal Multiplier(int dealer) => knockdown.ModifyDamageMultiplicative(state, 0, new DamageCtx(dealer, 0, SimProps.Move, card, Play: null));
        Assert.Equal(3m, Multiplier(Who.You));
        Assert.Equal(2m, Multiplier(Who.Teammate(0)));
        Assert.Equal(6m, Multiplier(Who.YourOsty));
        // Each goes at its owner's turn's end on its own; an unpowered hit isn't changed.
        Assert.Equal(1m, knockdown.ModifyDamageMultiplicative(state, 0, new DamageCtx(Who.You, 0, SimProps.Unpowered, Card: null, Play: null)));
    }

    [Fact]
    public void ACopyThatGoesLeavesTheOthers()
    {
        // GuardedPower's AfterDeath removes the copy whose applier died: the other copy stays, with its own amount in the total.
        var guarded = Assert.IsType<ReadModel>(SimModels.Get(PowerId.Guarded.Name));
        var state = Board();
        state.Enemies.Add(new SimEnemy { Id = 1, Name = "E1", Hp = 20 });
        Assert.True(SimEngine.ApplyPower(state, 1, 0, PowerId.Guarded, amount: 1, debuff: false));
        Assert.True(SimEngine.ApplyPower(state, 0, 0, PowerId.Guarded, amount: 1, debuff: false));
        Assert.Equal(2, state.Enemies[0].Powers[PowerId.Guarded]);
        state.Enemies[1].Hp = 0;
        guarded.AfterDeath(state, 0, dead: 1);
        Assert.Equal(new[] { 2 }, guarded.CopiesOrOne(state, 0));
        Assert.Equal(1, state.Enemies[0].Powers[PowerId.Guarded]);
        guarded.AfterDeath(state, 0, dead: 0);
        Assert.False(state.Enemies[0].Powers.Has(PowerId.Guarded));
        Assert.Empty(guarded.CopiesOrOne(state, 0));
    }

    [Fact]
    public void ANewInstanceStartsItsOwnStateAfresh()
    {
        // A power applied where none is starts as the game's new instance does: what an earlier one kept is gone with it.
        var strangle = Assert.IsType<ReadModel>(SimModels.Get(PowerId.Strangle.Name));
        var state = Board();
        state.Enemies[0].Fields = state.Enemies[0].Fields.With(PowerId.Strangle.Name + "@1", amount: 9, data: DataCreature.Of(Who.Teammate(0)));
        Assert.True(SimEngine.ApplyPower(state, Who.You, 0, PowerId.Strangle, amount: 2, debuff: true));
        Assert.Equal(new[] { 1 }, strangle.CopiesOrOne(state, 0));
        Assert.Equal(2, state.Enemies[0].Fields[PowerId.Strangle.Name + "@1"]);
        Assert.Equal(DataCreature.Of(Who.You), state.Enemies[0].Fields.DataOf(PowerId.Strangle.Name + "@1"));
    }
}
