using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A card's own energy in the simulator (decision #117): what it gains reaches your energy, so Pael's Tears warns only when the play
/// really leaves you with none, and the play check can hold your energy after the play to the prediction. Rules from the decompile:
/// Sunder (PlayerCmd.GainEnergy when its attack's results show a kill), Double Energy (GainEnergy(PlayerCombatState.Energy) after
/// its cost is spent), Epic Retrieval (AfterArrowDislodged, source this card: energy per arrow its play knocks loose).
/// </summary>
public class CardEnergyTests
{
    private static SimState State(int hp = 100, int energy = 3)
    {
        var s = new SimState { Enemies = { Enemy(0, hp) } };
        s.You.Energy = energy;
        return s;
    }

    private static SimState WithTears(SimState s)
    {
        s.You.Relics = s.You.Relics.With(RelicId.PaelsTears, 0, 0);
        return s;
    }

    /// <summary>A play as the adapter builds it: its cost spent first, the card's actions, then the card resolved.</summary>
    private static SimScript Spending(SimCard card, int cost, params SimAction[] body)
    {
        var script = Play(card, 0, 1, body);
        if (cost > 0) script.Actions.Insert(0, new CardEventAction(CardEvent.EnergySpent, cost));
        script.Actions.Add(new CardResolvedAction(0, 0, false, 0, 0, false));
        return script;
    }

    private static SimState One(SimState s, SimScript script) => Assert.Single(After(s, script));

    [Fact]
    public void APlainGainReachesYourEnergyAndKeepsPaelsTearsBonus()
    {
        // Your last energy on a 1-cost card that gains 2 (Production): 2 left, so Pael's Tears keeps its bonus.
        var s = WithTears(State(energy: 1));
        var after = One(s, Spending(new SimCard("Production", SimCardType.Skill) { Energy = 1 }, 1, new CardEventAction(CardEvent.EnergyGained, 2)));
        Assert.Equal(2, after.You.Energy);
        Assert.False(after.Fired.Has("PaelsTears"));
    }

    [Fact]
    public void SundersEnergyComesOnlyWithItsKill()
    {
        // Your last 3 energy on Sunder: the kill gives 3 back (no Pael's Tears warning); no kill, none back (the warning stands).
        var sunder = new SimCard("Sunder", SimCardType.Attack) { Energy = 3 };
        var kills = One(WithTears(State(hp: 10)), Spending(sunder, 3, Attack(24) with { OnKillEnergy = 3 }));
        Assert.False(kills.Enemy(0)!.Alive);
        Assert.Equal(3, kills.You.Energy);
        Assert.False(kills.Fired.Has("PaelsTears"));
        var survives = One(WithTears(State(hp: 50)), Spending(sunder, 3, Attack(24) with { OnKillEnergy = 3 }));
        Assert.Equal(0, survives.You.Energy);
        Assert.True(survives.Fired.Has("PaelsTears"));
    }

    [Fact]
    public void DoubleEnergyGainsWhatYouHaveOnceItsCostIsSpent()
    {
        var after = One(State(energy: 4), Spending(new SimCard("DoubleEnergy", SimCardType.Skill) { Energy = 1 }, 1, new GainEnergyAsHeldAction()));
        Assert.Equal(6, after.You.Energy);
        // With nothing left after its cost, there's nothing to double.
        var empty = One(WithTears(State(energy: 1)), Spending(new SimCard("DoubleEnergy", SimCardType.Skill) { Energy = 1 }, 1, new GainEnergyAsHeldAction()));
        Assert.Equal(0, empty.You.Energy);
        Assert.True(empty.Fired.Has("PaelsTears"));
    }

    [Fact]
    public void EpicRetrievalGainsPerArrowItsPlayKnocksLoose()
    {
        // Two of your arrows in an enemy it kills: both come loose (ImpaledPower.AfterDeath), 3 energy each.
        var s = State(hp: 5, energy: 2);
        s.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Impaled, 2, 3);
        var card = new SimCard("EpicRetrieval", SimCardType.Attack) { Energy = 2 };
        var script = new SimScript
        {
            Name = card.Name,
            Actions =
            {
                new CardEventAction(CardEvent.EnergySpent, 2),
                new CardPlayAction(new SimCardPlay(card, Who.You, 0, 0, 1, false) { ArrowEnergy = 3 }, new SimAction[] { Attack(9) }),
            },
        };
        var after = One(s, script);
        Assert.Equal(2, after.You.Arrows);
        Assert.Equal(6, after.You.Energy);
        // Another card knocking them loose gives nothing.
        var other = One(s, Spending(new SimCard("Strike", SimCardType.Attack) { Energy = 2 }, 2, Attack(9)));
        Assert.Equal(0, other.You.Energy);
    }
}
