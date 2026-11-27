using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// What the orbs' own code does that the engine's hand-coded orb rules left out (decision #214), read from the decompile (beta v0.113.1, main
/// v0.111.1): each a fix, outside the boards OrbParityTests holds equal.
/// </summary>
public class OrbCodeTests
{
    private static SimState Board()
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 40) } };
        state.Enemies[0].Hits = new() { 5 };
        state.You.OrbCapacity = 1;
        return state;
    }

    [Fact]
    public void PlasmasEvokeGivesItsEnergy()
    {
        // PlasmaOrb.Evoke: PlayerCmd.GainEnergy(EvokeVal, base.Owner), EvokeVal => 2m. The hand-coded rules gave none.
        var state = Board();
        state.You.Orbs.Add(SimEngine.FreshOrb(state, OrbKind.Plasma, Who.You));
        state.You.Energy = 1;
        var after = Assert.Single(After(state, Play(Card("Dualcast", SimCardType.Skill), target: Who.None, plays: 1, new OrbAction(OrbAction.Op.EvokeFront))));
        Assert.Equal(3, after.You.Energy);
        Assert.Empty(after.You.Orbs);
    }

    [Fact]
    public void FrostBlocksEveryOtherPlayerWithHibernate()
    {
        // FrostOrb.Passive and Evoke: GainBlock(base.Owner.Creature, value), then with HibernatePower on its owner the same block for every other
        // player (foreach Player in CombatState.Players, player != base.Owner). The hand-coded rules gave the others none.
        var state = Board();
        state.Teammates.Add(new SimPlayer { Hp = 30, MaxHp = 30 });
        state.You.Orbs.Add(SimEngine.FreshOrb(state, OrbKind.Frost, Who.You));
        state.You.Powers = state.You.Powers.With(PowerId.Hibernate, amount: 1);
        state.Enemies[0].Hits = new();
        var turnEnd = SimEngine.Run(state).Worst;
        Assert.Equal((2, 2), (turnEnd.You.Block, turnEnd.Teammates[0].Block));
        var evoked = Assert.Single(After(state, Play(Card("Dualcast", SimCardType.Skill), target: Who.None, plays: 1, new OrbAction(OrbAction.Op.EvokeFront))));
        Assert.Equal((5, 5), (evoked.You.Block, evoked.Teammates[0].Block));
    }

    [Fact]
    public void FrostWithoutHibernateBlocksOnlyItsOwner()
    {
        var state = Board();
        state.Teammates.Add(new SimPlayer { Hp = 30, MaxHp = 30 });
        state.You.Orbs.Add(SimEngine.FreshOrb(state, OrbKind.Frost, Who.You));
        var evoked = Assert.Single(After(state, Play(Card("Dualcast", SimCardType.Skill), target: Who.None, plays: 1, new OrbAction(OrbAction.Op.EvokeFront))));
        Assert.Equal((5, 0), (evoked.You.Block, evoked.Teammates[0].Block));
    }

    [Fact]
    public void ThunderAppliedThisPlayHitsALaterEvoke()
    {
        // ThunderPower is read from its code: a card's PowerCmd.Apply<ThunderPower> lands as the power (the hand-coded rules kept Thunder as a
        // number read once, so a Thunder played this turn did nothing to an evoke after it), and its AfterOrbEvoked hits again.
        var state = Board();
        state.You.Orbs.Add(SimEngine.FreshOrb(state, OrbKind.Lightning, Who.You));
        var after = Assert.Single(After(state, Play(Card("Probe", SimCardType.Skill), target: Who.None, plays: 1,
            new ApplyPowerAction(PowerId.Thunder, Amount: 6) { To = ApplyPowerAction.Where.You }, new OrbAction(OrbAction.Op.EvokeFront))));
        Assert.Equal(40 - 8 - 6, after.Enemy(id: 0)!.Hp);
    }

    [Fact]
    public void GlassDealsWhatItWasBeforeItsPassiveLowersIt()
    {
        // GlassOrb.Passive keeps passiveVal = PassiveVal, stores _passiveVal - 1, then damages every enemy by the kept value (a let: decision #214).
        var state = Board();
        state.You.Orbs.Add(SimEngine.FreshOrb(state, OrbKind.Glass, Who.You));
        var turnEnd = SimEngine.Run(state).Worst;
        Assert.Equal(36, turnEnd.Enemy(id: 0)!.Hp);
        Assert.Equal((3, 6, 3), (turnEnd.You.Orbs[0].Passive, turnEnd.You.Orbs[0].Evoke, turnEnd.You.Orbs[0].Fields["_passiveVal"]));
    }
    [Fact]
    public void AnEvokeOnceTheFightIsOverDoesNothingAndKeepsTheOrb()
    {
        // OrbCmd.Evoke returns at its start once CombatManager.IsOverOrEnding, before its dequeue: Multi-Cast+'s third evoke after its second killed the
        // last enemy does nothing, and its last (the one that removes the orb) never happens, so the Dark orb stays (the owner's Defect run).
        var state = Board();
        var dark = SimEngine.FreshOrb(state, OrbKind.Dark, Who.You);
        dark.Evoke = 30;
        state.You.Orbs.Add(dark);
        var after = Assert.Single(After(state, Play(Card("MultiCast", SimCardType.Skill), target: Who.None, plays: 1,
            new OrbAction(OrbAction.Op.EvokeFront) { Times = 3 })));
        Assert.False(after.Enemy(id: 0)!.Alive);
        Assert.Equal(new[] { (OrbKind.Dark, 30) }, after.You.Orbs.Select(orb => (orb.Kind, orb.Evoke)));
    }

    [Fact]
    public void AStockedAxebotsNextBodyTakesTheEvokesAfterTheKill()
    {
        // Axebot's Stock brings the next Axebot in as it dies (holding the fight's end back), and Dark's evoke hits the weakest enemy you can hit then:
        // Multi-Cast+'s second evoke of a 90 Dark orb lands on the new Axebot (the owner's run: 67 then 90).
        var state = Board();
        var axebot = state.Enemies[0];
        axebot.Hp = 67;
        axebot.Powers = PowerSet.Empty.With(PowerId.Stock, amount: 1);
        axebot.Spawns = new List<SimSpawn> { new("Axebot", new[] { 90 }) { Source = PowerId.Stock, Monster = "Axebot", UniqueFromEarlier = true } };
        var dark = SimEngine.FreshOrb(state, OrbKind.Dark, Who.You);
        dark.Evoke = 90;
        state.You.Orbs.Add(dark);
        var after = Assert.Single(After(state, Play(Card("MultiCast", SimCardType.Skill), target: Who.None, plays: 1,
            new OrbAction(OrbAction.Op.EvokeFront) { Times = 3 })));
        Assert.Equal(90, after.Enemy(id: 1)!.Hp + 90);
        Assert.False(after.Enemy(id: 1)!.Alive);
        // Its death ends the fight: the third evoke does nothing and the Dark orb stays.
        Assert.Single(after.You.Orbs);
    }
}
