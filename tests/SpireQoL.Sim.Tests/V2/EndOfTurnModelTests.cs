using Xunit;

namespace SpireQoL.Sim.Tests.V2;

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
        s.P.Fading = 2;
        s.P.Powers = s.P.Powers.With("FeelNoPainPower", 3).With("DarkEmbracePower", 1).With("SpeedsterPower", 2);
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
        s.P.HandCount = 2;
        s.P.ObsidianViceDamage = 4;
        var o = SimEngine.Run(s);
        Assert.True(o.DeadInAll(0));
        Assert.All(o.Branches, b => Assert.Equal(17, b.Enemy(1)!.HpAtTurnEnd)); // 30 - 5 (Flagon hits every enemy) - 8
    }

    [Fact]
    public void TheBombTicksDownUntilItsLastTurn()
    {
        var s = new SimState { Enemies = { Attacker(0, 50) } };
        s.P.Powers = s.P.Powers.With("TheBombPower", 2, 40);
        var after = Assert.Single(SimEngine.Run(s).Branches);
        Assert.Equal(50, after.Enemy(0)!.Hp);
        Assert.Equal(1, after.P.Powers["TheBombPower"]);
        s.P.Powers = PowerSet.Empty.With("TheBombPower", 1, 40);
        Assert.Equal(10, Assert.Single(SimEngine.Run(s).Branches).Enemy(0)!.Hp);
    }

    [Fact]
    public void StoneCalendarWaitsForItsTurn()
    {
        var s = new SimState { Enemies = { Attacker(0, 70) } };
        s.P.Relics = s.P.Relics.With("StoneCalendar", 52, 1);
        Assert.Equal(70, Assert.Single(SimEngine.Run(s).Branches).Enemy(0)!.Hp);
        s.P.Relics = PowerSet.Empty.With("StoneCalendar", 52, 0);
        Assert.Equal(18, Assert.Single(SimEngine.Run(s).Branches).Enemy(0)!.Hp);
    }

    [Fact]
    public void ConsumingShadowEvokesTheLastOrb()
    {
        var s = new SimState { Enemies = { Attacker(0, 50, 10) } };
        s.P.OrbCapacity = 3;
        s.P.Orbs = new() { new SimOrb { Kind = OrbKind.Dark, Passive = 6, Evoke = 20 }, new SimOrb { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 } };
        s.P.ConsumingShadow = 1;
        var o = SimEngine.Run(s);
        // Frost's passive (2) at turn end, then Consuming Shadow evokes it (5): 7 block. Dark's passive stays in it.
        Assert.Equal(3, o.TakenMax);
        Assert.All(o.Branches, b => Assert.Equal(OrbKind.Dark, Assert.Single(b.P.Orbs).Kind));
    }

    [Fact]
    public void ConstrictAndMagicBombGoWithTheirApplier()
    {
        // ConstrictPower.AfterDeath and MagicBombPower.AfterSideTurnEnd: once the enemy that applied them is dead (here Screaming
        // Flagon kills it at BeforeSideTurnEnd), neither hurts you. Data: the applier's id + 100.
        var s = new SimState { Enemies = { Attacker(0, 5), Attacker(1, 50) } };
        s.P.HandCount = 0;
        s.P.ScreamingFlagonDamage = 5;
        s.P.Powers = s.P.Powers.With("ConstrictPower", 4, 100).With("MagicBombPower", 6, 100);
        Assert.Equal(0, SimEngine.Run(s).TakenMax);

        // Applied by the enemy that lives: 4 + 6.
        s.P.Powers = s.P.Powers.With("ConstrictPower", 4, 101).With("MagicBombPower", 6, 101);
        var o = SimEngine.Run(s);
        Assert.Equal(10, o.TakenMax);
        Assert.False(o.Worst.P.Powers.Has("MagicBombPower")); // it goes once it has gone off
        Assert.True(o.Worst.P.Powers.Has("ConstrictPower"));
    }

    [Fact]
    public void DemiseIgnoresYourBlockAndConstrictUsesIt()
    {
        var s = new SimState { Enemies = { Attacker(0, 50) } };
        s.P.Block = 10;
        s.P.Powers = s.P.Powers.With("DemisePower", 3).With("ConstrictPower", 4);
        var o = SimEngine.Run(s);
        Assert.Equal(3, o.TakenMax);
        Assert.Equal(6, o.Worst.BlockAtEnemyTurn);
    }

    [Fact]
    public void CardsInHandHitThroughTheDamagePipeline()
    {
        // Burn's 5 under Intangible is 1 (IntangiblePower.ModifyDamageCap), so it takes 1 block, not 5; Regret counts the hand.
        var s = new SimState { Enemies = { Attacker(0, 50) } };
        s.P.Block = 10;
        s.P.HandCount = 3;
        s.P.Powers = s.P.Powers.With("IntangiblePower", 1);
        s.P.TurnEndCards.Add(new TurnEndCard("Burn", "Burn+", 5));
        s.P.TurnEndCards.Add(new TurnEndCard("Regret", "Regret", 0));
        var o = SimEngine.Run(s);
        Assert.Equal(9, o.Worst.BlockAtEnemyTurn);
        Assert.Equal(1, o.TakenMax); // Regret's 3, capped at 1 HP too

        s.P.Powers = PowerSet.Empty;
        o = SimEngine.Run(s);
        Assert.Equal(5, o.Worst.BlockAtEnemyTurn);
        Assert.Equal(3, o.TakenMax);
    }
}
