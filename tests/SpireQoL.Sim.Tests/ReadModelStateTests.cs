using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A read model's own state (decision #195): the fields and properties its code stores and reads are the branch's own (SimPlayer.Fields), part of
/// its key, and its code's tests are asked once a hook run, where the game asks them.
/// </summary>
public class ReadModelStateTests
{
    private static SimState WithRelic(RelicId relic, params (string Var, int Value)[] vars)
    {
        var state = new SimState { You = new SimPlayer { Hp = 50, HandCount = 3, Energy = 3 } };
        state.Enemies.Add(new SimEnemy { Id = 0, Name = "E0", Hp = 40 });
        state.You.Relics = state.You.Relics.With(relic, amount: 0, data: (int)RelicUse.Armed);
        foreach (var (name, value) in vars) state.Vars[$"{Who.You}:{relic.Name}:{name}"] = value;
        return state;
    }

    private static SimCardPlay Played(SimCardType type) => new(new SimCard("Played", type), Owner: Who.You, Target: 0, PlayIndex: 0, PlayCount: 1, AutoPlay: false);

    [Fact]
    public void TwoBranchesThatDifferOnlyInAModelsFieldStayApart()
    {
        var state = WithRelic(RelicId.Kunai);
        var other = state.Clone();
        other.You.Fields = other.You.Fields.With("Kunai.AttacksPlayedThisTurn", amount: 1);
        Assert.NotEqual(state.Key(), other.Key());
        var enemy = state.Clone();
        enemy.Enemies[0].Fields = enemy.Enemies[0].Fields.With("SomePower.Count", amount: 1);
        Assert.NotEqual(state.Key(), enemy.Key());
    }

    [Fact]
    public void KunaiCountsItsOwnAttacksThisTurn()
    {
        // Kunai.AfterCardPlayed: AttacksPlayedThisTurn++, then Dexterity when it's a multiple of Cards.
        var kunai = Assert.IsType<ReadModel>(SimModels.Get(RelicId.Kunai.Name));
        IEnumerable<SimState> states = new[] { WithRelic(RelicId.Kunai, ("Cards", 3), ("DexterityPower", 1)) };
        foreach (var type in new[] { SimCardType.Attack, SimCardType.Skill, SimCardType.Attack, SimCardType.Attack })
            states = states.SelectMany(branch => kunai.AfterCardPlayed(branch, Who.You, Played(type))).ToList();
        var after = Assert.Single(states);
        Assert.Equal(3, after.You.Fields["Kunai.AttacksPlayedThisTurn"]);
        Assert.Equal(1, after.You.Powers[PowerId.Dexterity]);
        Assert.Equal(1, after.Fired["Kunai"]);
    }

    /// <summary>A board with a power of yours at an amount.</summary>
    private static SimState WithPower(PowerId power, int amount)
    {
        var state = new SimState { You = new SimPlayer { Hp = 50, HandCount = 3, Energy = 3 } };
        state.Enemies.Add(new SimEnemy { Id = 0, Name = "E0", Hp = 40 });
        state.You.Powers = state.You.Powers.With(power, amount);
        return state;
    }

    private static IEnumerable<SimState> BothHooks(SimModel model, SimState state, SimCardPlay play, Action<SimState>? between = null)
    {
        state.CurrentPlay = play;
        return model.BeforeCardPlayed(state, Who.You, play).SelectMany(branch =>
        {
            between?.Invoke(branch);
            return model.AfterCardPlayed(branch, Who.You, play);
        }).ToList();
    }

    [Fact]
    public void AfterimageBindsItsAmountToThePlayAsItBegins()
    {
        // AfterimagePower: BeforeCardPlayed amountsForPlayedCards.Add(card, Amount); AfterCardPlayed Remove(card, out value) and value > 0: block
        // of what was bound, not of what it is after the play (more Afterimage from the play itself gives none on that play).
        var afterimage = Assert.IsType<ReadModel>(SimModels.Get(PowerId.Afterimage.Name));
        var after = Assert.Single(BothHooks(afterimage, WithPower(PowerId.Afterimage, amount: 1), Played(SimCardType.Skill),
            between: branch => branch.You.Powers = branch.You.Powers.Add(PowerId.Afterimage, 2)));
        Assert.Equal(1, after.You.Block);
        Assert.False(after.Bound.Has(SimKeys.Owned(PowerId.Afterimage.Name, Who.You)));
        // Played before it was there: nothing bound, nothing taken.
        var none = Assert.Single(BothHooks(afterimage, new SimState { You = new SimPlayer { Hp = 50 } }, Played(SimCardType.Skill),
            between: branch => branch.You.Powers = branch.You.Powers.With(PowerId.Afterimage, 3)));
        Assert.Equal(0, none.You.Block);
    }

    [Fact]
    public void StormChannelsWhatItBoundOneOrbARoundAndFiresOnce()
    {
        // StormPower.AfterCardPlayed: Flash(); for (i < lightning) OrbCmd.Channel<LightningOrb>: its bound amount of Lightning, one firing.
        var storm = Assert.IsType<ReadModel>(SimModels.Get(PowerId.Storm.Name));
        var state = WithPower(PowerId.Storm, amount: 2);
        state.You.OrbCapacity = 3;
        var after = Assert.Single(BothHooks(storm, state, Played(SimCardType.Power)));
        Assert.Equal(2, after.You.Orbs.Count(orb => orb.Kind == OrbKind.Lightning));
        Assert.Equal(1, after.Fired["StormPower"]);
        var skill = Assert.Single(BothHooks(storm, WithPower(PowerId.Storm, amount: 2), Played(SimCardType.Skill)));
        Assert.Empty(skill.You.Orbs);
    }

    [Fact]
    public void EnrageHelpsItsEnemy()
    {
        // EnragePower.AfterCardPlayed: a Skill gives its owner its amount of Strength: a crossed tab (your play helps it). Once its owner is
        // dead its powers went with it: the engine's listeners leave them out (DeathListenerTests), as for every model.
        var enrage = Assert.IsType<ReadModel>(SimModels.Get(PowerId.Enrage.Name));
        Assert.True(enrage.HarmfulFor(owner: 0));
        Assert.Equal(TabKind.Trigger, enrage.Tab);
        var state = new SimState { You = new SimPlayer { Hp = 50 } };
        state.Enemies.Add(new SimEnemy { Id = 0, Name = "E0", Hp = 40, Powers = PowerSet.Empty.With(PowerId.Enrage, amount: 2) });
        var after = Assert.Single(enrage.AfterCardPlayed(state, owner: 0, Played(SimCardType.Skill)));
        Assert.Equal(2, after.Enemies[0].Powers[PowerId.Strength]);
    }

    /// <summary>
    /// A tab is crossed by whose side its effect helps, from the player whose card plays (CLAUDE.md "Plain vs crossed": it helps the enemy or
    /// hurts you). SneakyPower.AfterCardPlayed gives its owner block when another creature plays an Attack, and the multiplayer card Sneaky
    /// applies it to its own player (Sneaky.OnPlay: base.Owner.Creature): a teammate's Sneaky set off by your Attack helps your side, a plain tab.
    /// The same code on an enemy (Enrage's Strength on your Skill) helps the other side: crossed.
    /// </summary>
    [Fact]
    public void ATabIsCrossedOnlyWhenItHelpsTheOtherSide()
    {
        var sneaky = Assert.IsType<ReadModel>(SimModels.Get(PowerId.Sneaky.Name));
        Assert.False(sneaky.HarmfulFor(Who.Teammate(0)));
        Assert.False(sneaky.HarmfulFor(Who.You));
        Assert.True(sneaky.HarmfulFor(owner: 0));
        var enrage = Assert.IsType<ReadModel>(SimModels.Get(PowerId.Enrage.Name));
        Assert.True(enrage.HarmfulFor(owner: 2));
        Assert.False(enrage.HarmfulFor(Who.Teammate(1)));
        // A hand-written model's crossing is its own (Tender hurts its own player).
        Assert.True(SimModels.Get("TenderPower")!.HarmfulFor(Who.You));
    }

    [Fact]
    public void MiniRegentAsksItsFlagOnceAsTheGameDoes()
    {
        // MiniRegent.AfterStarsSpent: if (!UsedThisTurn) { UsedThisTurn = true; Apply<StrengthPower> }: the Strength after the store still
        // goes on (the test was asked before it), and only once a turn.
        var regent = Assert.IsType<ReadModel>(SimModels.Get(RelicId.MiniRegent.Name));
        var state = WithRelic(RelicId.MiniRegent, ("StrengthPower", 1));
        var first = Assert.Single(regent.AfterCardEvent(state, Who.You, CardEvent.StarsSpent, Who.You));
        Assert.Equal(1, first.You.Powers[PowerId.Strength]);
        Assert.Equal(1, first.You.Fields["MiniRegent.UsedThisTurn"]);
        var second = Assert.Single(regent.AfterCardEvent(first, Who.You, CardEvent.StarsSpent, Who.You));
        Assert.Equal(1, second.You.Powers[PowerId.Strength]);
    }
}
