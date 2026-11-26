using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The Insatiable's Sandpit (SandpitPower, decision #321): one copy on it for each player, aimed at that player, each counting down on its own. Its
/// AfterSideTurnStartLate, as the enemies' side's turn starts (and only then), is PowerCmd.Decrement of each copy; a copy at 0 goes, and its
/// AfterRemoved kills the player it's aimed at and their Osty with force (no Fairy in a Bottle, no Lizard Tail), before any enemy moves, unless its
/// holder or that player is dead already.
/// </summary>
public class SandpitTests
{
    /// <summary>The Insatiable (enemy 1, an attack of 10) with a Sandpit aimed at each player at its amount.</summary>
    private static SimState Board(params (int Player, int Amount)[] copies)
    {
        var insatiable = Enemy(id: 1, hp: 300);
        insatiable.Hits.Add(10);
        foreach (var (player, amount) in copies)
        {
            insatiable.Powers = insatiable.Powers.With(PowerId.Sandpit, insatiable.Powers[PowerId.Sandpit] + amount);
            insatiable.Fields = insatiable.Fields.With(SimKeys.Aimed(PowerId.Sandpit.Name, player), amount);
        }
        var state = new SimState { Enemies = { insatiable } };
        state.You.Hp = 50;
        return state;
    }

    [Fact]
    public void ItCountsDownAsTheEnemiesTurnStarts()
    {
        var after = Assert.Single(SimEngine.Run(Board((Who.You, 3))).Branches);
        Assert.Equal(2, SimEngine.AimedAmount(after, holder: 1, PowerId.Sandpit, Who.You));
        Assert.Equal(2, after.Enemy(id: 1)!.Powers[PowerId.Sandpit]);
        Assert.False(after.Died);
        Assert.Equal(40, after.You.Hp); // the attack still comes
    }

    [Fact]
    public void ItsLastCountKillsYouBeforeAnyEnemyMoves()
    {
        var state = Board((Who.You, 1));
        state.You.Fairies = 1;
        state.You.FairyHeal = 15;
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.YouDieChance().Certain);
        var after = Assert.Single(outcome.Branches);
        Assert.Equal(0, after.You.FairiesUsed); // killed with force: nothing holds it back
        Assert.False(after.Enemy(id: 1)!.Powers.Has(PowerId.Sandpit));
        Assert.Equal(0, after.You.Hp); // the attack alone (10) leaves you standing
    }

    [Fact]
    public void ItKillsYourOstyWithYou()
    {
        var state = Board((Who.You, 1));
        state.You.OstyHp = 12;
        state.You.OstyMaxHp = 12;
        var after = Assert.Single(SimEngine.Run(state).Branches);
        Assert.True(after.Died);
        Assert.Equal(0, after.You.OstyHp);
    }

    [Fact]
    public void ADeadHolderKillsNoOne()
    {
        // if (oldOwner.IsDead || base.Target.IsDead) return: The Insatiable dead before its turn starts takes no one with it.
        var state = Board((Who.You, 1));
        state.Enemies[0].Hp = 0;
        var outcome = SimEngine.Run(state);
        Assert.False(outcome.YouDieChance().Possible);
    }

    [Fact]
    public void EachPlayersCopyCountsDownOnItsOwn()
    {
        // A copy each (Liquify), a Frantic Escape having raised yours: the teammate's runs out first and kills only them.
        var state = Board((Who.You, 2), (Who.Teammate(teammateIndex: 0), 1));
        state.Teammates.Add(new SimPlayer { Hp = 40 });
        var after = Assert.Single(SimEngine.Run(state).Branches);
        Assert.False(after.Died);
        Assert.True(after.Teammates[0].Died);
        Assert.Equal(1, SimEngine.AimedAmount(after, holder: 1, PowerId.Sandpit, Who.You));
        Assert.Equal(0, SimEngine.AimedAmount(after, holder: 1, PowerId.Sandpit, Who.Teammate(teammateIndex: 0)));
        Assert.Equal(1, after.Enemy(id: 1)!.Powers[PowerId.Sandpit]);
    }

    [Fact]
    public void ItDoesNothingAsYourTurnStarts()
    {
        // if (side == CombatSide.Enemy): the players' turn start in a projection leaves it as it is.
        var state = Board((Who.You, 3));
        var model = SimModels.Get(PowerId.Sandpit.Name)!;
        var after = Assert.Single(model.AfterSideTurnStartLate(state, owner: 1));
        Assert.Equal(3, SimEngine.AimedAmount(after, holder: 1, PowerId.Sandpit, Who.You));
    }
}
