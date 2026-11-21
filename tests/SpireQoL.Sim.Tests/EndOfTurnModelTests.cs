using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The end of your side's turn as models (Sim/Models/EndOfTurnModels.cs, decompile v0.113.1): Hook.BeforeSideTurnEnd's passes in
/// listener order (players in player order), each player's DoTurnEnd, then Hook.AfterSideTurnEnd.
/// </summary>
public class EndOfTurnModelTests
{
    private static SimEnemy Attacker(int id, int hp, int hit = 0) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hit > 0 ? new() { hit } : new() };

    [Fact]
    public void EtherealExhaustsSetOffEveryExhaustModelAndTheirChains()
    {
        // Two Ethereal cards exhaust: Feel No Pain blocks for each, Dark Embrace draws for each, and each draw sets off Speedster.
        var state = new SimState { Enemies = { Attacker(0, 50, 10) } };
        state.You.Fading = 2;
        state.You.Powers = state.You.Powers.With(PowerId.FeelNoPain, 3).With(PowerId.DarkEmbrace, 1).With(PowerId.Speedster, 2);
        var outcome = SimEngine.Run(state);
        Assert.Equal(4, outcome.TakenMax);
        Assert.All(outcome.Branches, branch => Assert.Equal(46, branch.Enemy(0)!.HpAtTurnEnd));
    }

    [Fact]
    public void ATeammateBeforeYouActsFirst()
    {
        // Your teammate comes before you in player order: their Screaming Flagon (empty hand) kills E0 before your Obsidian
        // Vice picks its random target, so the Vice can only hit E1: no branch on E0.
        var state = new SimState { Enemies = { Attacker(0, 5, 3), Attacker(1, 30, 3) }, TeammatesBeforeMe = 1 };
        var mate = new SimPlayer { Hp = 30, HandCount = 0 };
        mate.ScreamingFlagonDamage = 5;
        state.Teammates.Add(mate);
        state.You.HandCount = 2;
        state.You.ObsidianViceDamage = 4;
        var outcome = SimEngine.Run(state);
        Assert.True(outcome.DeadInAll(0));
        Assert.All(outcome.Branches, branch => Assert.Equal(17, branch.Enemy(1)!.HpAtTurnEnd)); // 30 - 5 (Flagon hits every enemy) - 8
    }

    [Fact]
    public void TheBombTicksDownUntilItsLastTurn()
    {
        var state = new SimState { Enemies = { Attacker(0, 50) } };
        state.You.Powers = state.You.Powers.With(PowerId.TheBomb, 2, 40);
        var after = Assert.Single(SimEngine.Run(state).Branches);
        Assert.Equal(50, after.Enemy(0)!.Hp);
        Assert.Equal(1, after.You.Powers[PowerId.TheBomb]);
        state.You.Powers = PowerSet.Empty.With(PowerId.TheBomb, 1, 40);
        Assert.Equal(10, Assert.Single(SimEngine.Run(state).Branches).Enemy(0)!.Hp);
    }

    [Fact]
    public void StoneCalendarWaitsForItsTurn()
    {
        var state = new SimState { Enemies = { Attacker(0, 70) } };
        state.You.Relics = state.You.Relics.With(RelicId.StoneCalendar, 52, 1);
        Assert.Equal(70, Assert.Single(SimEngine.Run(state).Branches).Enemy(0)!.Hp);
        state.You.Relics = RelicSet.Empty.With(RelicId.StoneCalendar, 52, 0);
        Assert.Equal(18, Assert.Single(SimEngine.Run(state).Branches).Enemy(0)!.Hp);
    }

    [Fact]
    public void ConsumingShadowEvokesTheLastOrb()
    {
        var state = new SimState { Enemies = { Attacker(0, 50, 10) } };
        state.You.OrbCapacity = 3;
        state.You.Orbs = new() { new SimOrb { Kind = OrbKind.Dark, Passive = 6, Evoke = 20 }, new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 } };
        state.You.ConsumingShadow = 1;
        var outcome = SimEngine.Run(state);
        // Frost's passive (2) at turn end, then Consuming Shadow evokes it (5): 7 block. Dark's passive stays in it.
        Assert.Equal(3, outcome.TakenMax);
        Assert.All(outcome.Branches, branch => Assert.Equal(OrbKind.Dark, Assert.Single(branch.You.Orbs).Kind));
    }

    [Fact]
    public void ConstrictAndMagicBombGoWithTheirApplier()
    {
        // ConstrictPower.AfterDeath and MagicBombPower.AfterSideTurnEnd: once the enemy that applied them is dead (here Screaming
        // Flagon kills it at BeforeSideTurnEnd), neither hurts you. Data: the applier's id + 100.
        var state = new SimState { Enemies = { Attacker(0, 5), Attacker(1, 50) } };
        state.You.HandCount = 0;
        state.You.ScreamingFlagonDamage = 5;
        state.You.Powers = state.You.Powers.With(PowerId.Constrict, 4, 100).With(PowerId.MagicBomb, 6, 100);
        Assert.Equal(0, SimEngine.Run(state).TakenMax);

        // Applied by the enemy that lives: 4 + 6.
        state.You.Powers = state.You.Powers.With(PowerId.Constrict, 4, 101).With(PowerId.MagicBomb, 6, 101);
        var outcome = SimEngine.Run(state);
        Assert.Equal(10, outcome.TakenMax);
        Assert.False(outcome.Worst.You.Powers.Has(PowerId.MagicBomb)); // it goes once it has gone off
        Assert.True(outcome.Worst.You.Powers.Has(PowerId.Constrict));
    }

    [Fact]
    public void DemiseIgnoresYourBlockAndConstrictUsesIt()
    {
        var state = new SimState { Enemies = { Attacker(0, 50) } };
        state.You.Block = 10;
        state.You.Powers = state.You.Powers.With(PowerId.Demise, 3).With(PowerId.Constrict, 4);
        var outcome = SimEngine.Run(state);
        Assert.Equal(3, outcome.TakenMax);
        Assert.Equal(6, outcome.Worst.BlockAtEnemyTurn);
    }

    [Fact]
    public void CardsInHandHitThroughTheDamagePipeline()
    {
        // Burn's 5 under Intangible is 1 (IntangiblePower.ModifyDamageCap), so it takes 1 block, not 5; Regret counts the hand.
        var state = new SimState { Enemies = { Attacker(0, 50) } };
        state.You.Block = 10;
        state.You.HandCount = 3;
        state.You.Powers = state.You.Powers.With(PowerId.Intangible, 1);
        state.You.TurnEndCards.Add(new TurnEndCard("Burn", "Burn+", 5));
        state.You.TurnEndCards.Add(new TurnEndCard("Regret", "Regret", 0));
        var outcome = SimEngine.Run(state);
        Assert.Equal(9, outcome.Worst.BlockAtEnemyTurn);
        Assert.Equal(1, outcome.TakenMax); // Regret's 3, capped at 1 HP too

        state.You.Powers = PowerSet.Empty;
        outcome = SimEngine.Run(state);
        Assert.Equal(5, outcome.Worst.BlockAtEnemyTurn);
        Assert.Equal(3, outcome.TakenMax);
    }
}
