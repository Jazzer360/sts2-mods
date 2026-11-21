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
        var s = new SimState { Enemies = { Attacker(0, 50, 10) } };
        s.You.Fading = 2;
        s.You.Powers = s.You.Powers.With(PowerId.FeelNoPain, 3).With(PowerId.DarkEmbrace, 1).With(PowerId.Speedster, 2);
        var o = SimEngine.Run(s);
        Assert.Equal(4, o.TakenMax);
        Assert.All(o.Branches, b => Assert.Equal(46, b.Enemy(0)!.HpAtTurnEnd));
    }

    [Fact]
    public void ATeammateBeforeYouActsFirst()
    {
        // Your teammate comes before you in player order: their Screaming Flagon (empty hand) kills E0 before your Obsidian
        // Vice picks its random target, so the Vice can only hit E1: no branch on E0.
        var s = new SimState { Enemies = { Attacker(0, 5, 3), Attacker(1, 30, 3) }, TeammatesBeforeMe = 1 };
        var mate = new SimPlayer { Hp = 30, HandCount = 0 };
        mate.ScreamingFlagonDamage = 5;
        s.Teammates.Add(mate);
        s.You.HandCount = 2;
        s.You.ObsidianViceDamage = 4;
        var o = SimEngine.Run(s);
        Assert.True(o.DeadInAll(0));
        Assert.All(o.Branches, b => Assert.Equal(17, b.Enemy(1)!.HpAtTurnEnd)); // 30 - 5 (Flagon hits every enemy) - 8
    }

    [Fact]
    public void TheBombTicksDownUntilItsLastTurn()
    {
        var s = new SimState { Enemies = { Attacker(0, 50) } };
        s.You.Powers = s.You.Powers.With(PowerId.TheBomb, 2, 40);
        var after = Assert.Single(SimEngine.Run(s).Branches);
        Assert.Equal(50, after.Enemy(0)!.Hp);
        Assert.Equal(1, after.You.Powers[PowerId.TheBomb]);
        s.You.Powers = PowerSet.Empty.With(PowerId.TheBomb, 1, 40);
        Assert.Equal(10, Assert.Single(SimEngine.Run(s).Branches).Enemy(0)!.Hp);
    }

    [Fact]
    public void StoneCalendarWaitsForItsTurn()
    {
        var s = new SimState { Enemies = { Attacker(0, 70) } };
        s.You.Relics = s.You.Relics.With(RelicId.StoneCalendar, 52, 1);
        Assert.Equal(70, Assert.Single(SimEngine.Run(s).Branches).Enemy(0)!.Hp);
        s.You.Relics = RelicSet.Empty.With(RelicId.StoneCalendar, 52, 0);
        Assert.Equal(18, Assert.Single(SimEngine.Run(s).Branches).Enemy(0)!.Hp);
    }

    [Fact]
    public void ConsumingShadowEvokesTheLastOrb()
    {
        var s = new SimState { Enemies = { Attacker(0, 50, 10) } };
        s.You.OrbCapacity = 3;
        s.You.Orbs = new() { new SimOrb { Kind = OrbKind.Dark, Passive = 6, Evoke = 20 }, new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 } };
        s.You.ConsumingShadow = 1;
        var o = SimEngine.Run(s);
        // Frost's passive (2) at turn end, then Consuming Shadow evokes it (5): 7 block. Dark's passive stays in it.
        Assert.Equal(3, o.TakenMax);
        Assert.All(o.Branches, b => Assert.Equal(OrbKind.Dark, Assert.Single(b.You.Orbs).Kind));
    }

    [Fact]
    public void ConstrictAndMagicBombGoWithTheirApplier()
    {
        // ConstrictPower.AfterDeath and MagicBombPower.AfterSideTurnEnd: once the enemy that applied them is dead (here Screaming
        // Flagon kills it at BeforeSideTurnEnd), neither hurts you. Data: the applier's id + 100.
        var s = new SimState { Enemies = { Attacker(0, 5), Attacker(1, 50) } };
        s.You.HandCount = 0;
        s.You.ScreamingFlagonDamage = 5;
        s.You.Powers = s.You.Powers.With(PowerId.Constrict, 4, 100).With(PowerId.MagicBomb, 6, 100);
        Assert.Equal(0, SimEngine.Run(s).TakenMax);

        // Applied by the enemy that lives: 4 + 6.
        s.You.Powers = s.You.Powers.With(PowerId.Constrict, 4, 101).With(PowerId.MagicBomb, 6, 101);
        var o = SimEngine.Run(s);
        Assert.Equal(10, o.TakenMax);
        Assert.False(o.Worst.You.Powers.Has(PowerId.MagicBomb)); // it goes once it has gone off
        Assert.True(o.Worst.You.Powers.Has(PowerId.Constrict));
    }

    [Fact]
    public void DemiseIgnoresYourBlockAndConstrictUsesIt()
    {
        var s = new SimState { Enemies = { Attacker(0, 50) } };
        s.You.Block = 10;
        s.You.Powers = s.You.Powers.With(PowerId.Demise, 3).With(PowerId.Constrict, 4);
        var o = SimEngine.Run(s);
        Assert.Equal(3, o.TakenMax);
        Assert.Equal(6, o.Worst.BlockAtEnemyTurn);
    }

    [Fact]
    public void CardsInHandHitThroughTheDamagePipeline()
    {
        // Burn's 5 under Intangible is 1 (IntangiblePower.ModifyDamageCap), so it takes 1 block, not 5; Regret counts the hand.
        var s = new SimState { Enemies = { Attacker(0, 50) } };
        s.You.Block = 10;
        s.You.HandCount = 3;
        s.You.Powers = s.You.Powers.With(PowerId.Intangible, 1);
        s.You.TurnEndCards.Add(new TurnEndCard("Burn", "Burn+", 5));
        s.You.TurnEndCards.Add(new TurnEndCard("Regret", "Regret", 0));
        var o = SimEngine.Run(s);
        Assert.Equal(9, o.Worst.BlockAtEnemyTurn);
        Assert.Equal(1, o.TakenMax); // Regret's 3, capped at 1 HP too

        s.You.Powers = PowerSet.Empty;
        o = SimEngine.Run(s);
        Assert.Equal(5, o.Worst.BlockAtEnemyTurn);
        Assert.Equal(3, o.TakenMax);
    }
}
