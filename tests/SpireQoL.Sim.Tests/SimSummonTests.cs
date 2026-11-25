using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// OstyCmd.Summon as the game runs it (decompile v0.114.0, the same on v0.111.1): Hook.ModifySummonAmount, then Osty alive gains max HP and is
/// healed by it (CreatureCmd.GainMaxHp), dead he's revived (CreatureCmd.SetMaxHp, Heal, Hook.AfterOstyRevived), missing he's added with Die For
/// You (PlayerCmd.AddPet); then CombatHistory.Summoned and Hook.AfterSummon. And CreatureCmd.Heal of Osty from a card (Spur).
/// </summary>
public class SimSummonTests
{
    private static SimScript Summoning(int amount, params SimAction[] more) =>
        Play(Card("Bodyguard", SimCardType.Skill), target: Who.None, plays: 1, new SimAction[] { new SummonAction(amount) }.Concat(more).ToArray());

    [Fact]
    public void BodyguardsSummonTakesTheEnemysHitForYou()
    {
        // Report 20261125-025845-b055ab: Osty at 2/2, Bodyguard summons 5 (Osty 7/7), the Shrinker Beetle's 8 lands: Osty takes 7, you 1. The play
        // preview showed 6 (the summon not simulated: Osty took 2).
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "ShrinkerBeetle", Hp = 28, Hits = { 8 } } } };
        state.You.Hp = 52;
        state.You.MaxHp = 66;
        state.You.OstyHp = 2;
        state.You.OstyMaxHp = 2;
        var outcome = SimEngine.Run(state, Summoning(amount: 5));
        Assert.Equal(1, outcome.TakenMax);
        Assert.Equal(7, outcome.OstyTakenMax);
        var after = Assert.Single(After(state, Summoning(amount: 5)));
        Assert.Equal(7, after.You.OstyHp);
        Assert.Equal(7, after.You.OstyMaxHp);
    }

    [Fact]
    public void ASummonOfADeadOstyRevivesHimAtTheAmount()
    {
        // OstyCmd.Summon with Osty dead: SetMaxHp(amount), Heal(amount): he comes back at the amount, whatever his max HP was.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50) } };
        state.You.OstyHp = 0;
        state.You.OstyMaxHp = 12;
        state.You.OstyShields = true;
        var after = Assert.Single(After(state, Summoning(amount: 5)));
        Assert.Equal(5, after.You.OstyHp);
        Assert.Equal(5, after.You.OstyMaxHp);
        Assert.True(after.You.OstyShields);
    }

    [Fact]
    public void TheFirstSummonAddsOstyWithDieForYou()
    {
        // No Osty yet (PlayerCmd.AddPet, then PowerCmd.Apply<DieForYouPower>): he takes your powered hits from then on.
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 50, Hits = { 4 } } } };
        state.You.OstyShields = false;
        state.You.OstyHp = 0;
        Assert.Equal(4, SimEngine.Run(state).TakenMax);
        var outcome = SimEngine.Run(state, Summoning(amount: 6));
        Assert.Equal(0, outcome.TakenMax);
        Assert.Equal(4, outcome.OstyTakenMax);
    }

    [Fact]
    public void ASummonIsInTheFightsLog()
    {
        // CombatHistory.Summoned: a question counting the turn's summons of its owner counts the play's.
        var query = new HistoryQuery(HistoryKind.Summoned, HistoryScope.ThisTurn, HistoryWhose.Owner);
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50) }, History = new[] { query } };
        state.You.OstyHp = 3;
        var after = Assert.Single(After(state, Summoning(amount: 2)));
        Assert.Equal(1, SimEngine.HistoryCount(after, Who.You, query));
    }

    [Fact]
    public void SpursHealOfOstyStopsAtHisMaxHp()
    {
        // Spur.OnPlay: OstyCmd.Summon, then CreatureCmd.Heal(base.Owner.Osty, Heal): up to his max HP (Creature.SetCurrentHpInternal).
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50) } };
        state.You.OstyHp = 3;
        state.You.OstyMaxHp = 10;
        var spur = Summoning(amount: 2, new HealAction(Amount: 4) { Osty = true }); // 3/10 → 5/12 → 9/12
        Assert.Equal(9, Assert.Single(After(state, spur)).You.OstyHp);
        var full = Summoning(amount: 2, new HealAction(Amount: 20) { Osty = true });
        Assert.Equal(12, Assert.Single(After(state, full)).You.OstyHp);
    }

    [Fact]
    public void NotYetsHealOfYouStopsAtYourMaxHp()
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50) } };
        state.You.Hp = 60;
        state.You.MaxHp = 66;
        var notYet = Play(Card("NotYet", SimCardType.Skill), target: Who.None, plays: 1, new HealAction(Amount: 10));
        Assert.Equal(66, Assert.Single(After(state, notYet)).You.Hp);
    }

    [Fact]
    public void ASummonForATeammateRaisesTheirOsty()
    {
        // Legion of Bone: OstyCmd.Summon for each living player of your side, each their own Osty.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50) } };
        state.You.OstyHp = 1;
        state.Teammates.Add(new SimPlayer { OstyHp = 2 });
        var legion = Play(Card("LegionOfBone", SimCardType.Skill), target: Who.None, plays: 1, new SummonAction(Amount: 4), new SummonAction(Amount: 4) { Player = Who.Teammate(0) });
        var after = Assert.Single(After(state, legion));
        Assert.Equal(5, after.You.OstyHp);
        Assert.Equal(6, after.Teammates[0].OstyHp);
    }
}
