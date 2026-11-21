using SpireQoL.Sim.Models;
using Xunit;
using static SpireQoL.Sim.Tests.V2.V2Script;

namespace SpireQoL.Sim.Tests.V2;

/// <summary>
/// The damage and block sources that change during a turn (Sim/Models/StatModels.cs), each as the game resolves it
/// (decompile v0.113.1): the damage of every hit is worked out from the state when it lands.
/// </summary>
public class StatModelTests
{
    private static SimState WithPowers(params (PowerId Name, int Amount)[] mine)
    {
        var s = new SimState { Enemies = { Enemy(0, 100) } };
        foreach (var (n, a) in mine) s.P.Powers = s.P.Powers.With(n, a);
        return s;
    }

    [Fact]
    public void StrengthAddsToPoweredHitsOnly()
    {
        var s = WithPowers((PowerId.Strength, 3));
        Assert.Equal(91, After(s, Play(Card("Strike"), 0, 1, Attack(6))).Single().Enemy(0)!.Hp);
        // Unpowered (Thorns-like) damage doesn't get Strength.
        Assert.Equal(94, After(s, Play(Card("X"), 0, 1, Attack(6, props: SimProps.Move | SimProps.Unpowered))).Single().Enemy(0)!.Hp);
    }

    [Fact]
    public void VulnerableAndPaperPhrogAndDebilitate()
    {
        var s = WithPowers();
        s.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Vulnerable, 2);
        Assert.Equal(85, After(s, Play(Card("Strike"), 0, 1, Attack(10))).Single().Enemy(0)!.Hp); // 15
        s.P.Relics = RelicSet.Empty.With(RelicId.PaperPhrog, 0);
        Assert.Equal(83, After(s, Play(Card("Strike"), 0, 1, Attack(10))).Single().Enemy(0)!.Hp); // 1.75 → 17
        s.Enemies[0].Powers = s.Enemies[0].Powers.With(PowerId.Debilitate, 1);
        Assert.Equal(75, After(s, Play(Card("Strike"), 0, 1, Attack(10))).Single().Enemy(0)!.Hp); // 1.75 + 0.75 → 25
    }

    [Fact]
    public void TenderReplayHitsOneLessOnItsSecondPlay()
    {
        // A report from 0.45.0: Wah Sweep+ played twice into a 23-HP Hunter Killer dealt 9, then 8.
        var s = new SimState { Enemies = { Enemy(0, 23) } };
        s.P.Powers = PowerSet.Empty.With(PowerId.Tender, 1).With(PowerId.Strength, -2);
        var after = After(s, Play(Card("WahSweep"), 0, 2, Attack(11))).Single();
        Assert.Equal(6, after.Enemy(0)!.Hp);
        Assert.Equal(-4, after.P.Powers[PowerId.Strength]);
        Assert.Equal(-2, after.P.Powers[PowerId.Dexterity]);
    }

    [Fact]
    public void ArtifactTakesTendersStrengthLoss()
    {
        var s = WithPowers((PowerId.Tender, 1), (PowerId.Artifact, 1));
        var after = After(s, Play(Card("Strike"), 0, 1, Attack(6))).Single();
        Assert.Equal(0, after.P.Powers[PowerId.Strength]); // the first debuff was taken
        Assert.Equal(-1, after.P.Powers[PowerId.Dexterity]);
        Assert.False(after.P.Powers.Has(PowerId.Artifact));
    }

    [Fact]
    public void PenNibDoublesOnlyThePlayThatReachesTen()
    {
        // A report from 0.46.2: Ki Whip with Pen Nib ready (9), played twice into a 56-HP Magi Knight: 14, then 7.
        var s = new SimState { Enemies = { Enemy(0, 56) } };
        s.P.Relics = RelicSet.Empty.With(RelicId.PenNib, 9);
        var after = After(s, Play(Card("KiWhip"), 0, 2, Attack(7))).Single();
        Assert.Equal(35, after.Enemy(0)!.Hp);
        Assert.Equal(1, after.P.Relics[RelicId.PenNib]);
        // At 8, the replay's second play is the doubled one.
        s.P.Relics = RelicSet.Empty.With(RelicId.PenNib, 8);
        Assert.Equal(35, After(s, Play(Card("KiWhip"), 0, 2, Attack(7))).Single().Enemy(0)!.Hp);
        Assert.Equal(1, After(s, Play(Card("KiWhip"), 0, 2, Attack(7))).Single().Fired["PenNib"]);
    }

    [Fact]
    public void PenNibBoundToTheCardMidPlayDoublesNoCardInHand()
    {
        // Reports from 1.11.4 (Regent): Jackpot was the 10th attack and mid-play when the hand was simulated; the game
        // doubles only Jackpot (AttackToDouble), so Sovereign Blade dealt 60, not 120. Its next attack counts 1.
        var s = new SimState { Enemies = { Enemy(0, 200) } };
        s.P.Relics = RelicSet.Empty.With(RelicId.PenNib, 0, 1);
        var after = After(s, Play(Card("SovereignBlade"), 0, 1, Attack(60))).Single();
        Assert.Equal(140, after.Enemy(0)!.Hp);
        Assert.Equal(1, after.P.Relics[RelicId.PenNib]);
        Assert.False(after.Fired.Has("PenNib"));
    }

    [Fact]
    public void MightyWingsStrengthLandsBeforeTheNextHit()
    {
        // A report from 0.46.2: Repeat Shot+ into a 70-HP Soul Nexus dealt 33 (30 or more: +2 Strength), then 36.
        var s = new SimState { Enemies = { Enemy(0, 70) } };
        s.P.Relics = RelicSet.Empty.With(RelicId.MightyWings, 2, 30);
        var after = After(s, Play(Card("RepeatShot"), 0, 1, Attack(22, hits: 2, terms: new CapturedTerms { Mul = 1.5m }))).Single();
        Assert.Equal(1, after.Enemy(0)!.Hp);
        Assert.Equal(4, after.P.Powers[PowerId.Strength]);
        Assert.Equal(2, after.Fired["MightyWings"]);
    }

    [Fact]
    public void VigorIsSpentByTheFirstAttackOnly()
    {
        var s = WithPowers((PowerId.Vigor, 5));
        var after = After(s, Play(Card("Strike"), 0, 2, Attack(6))).Single();
        Assert.Equal(83, after.Enemy(0)!.Hp); // 11, then 6
        Assert.False(after.P.Powers.Has(PowerId.Vigor));
    }

    [Fact]
    public void VigorReservedForAnAttackUnderWayAddsNothing()
    {
        // VigorPower.BeforeAttack binds the first attack (commandToModify); until that attack spends it, ModifyDamageAdditive gives
        // every other card's damage 0. The adapter reads that as bound outside the simulation (a card mid-play).
        var s = WithPowers();
        s.P.Powers = s.P.Powers.With(PowerId.Vigor, 6, AttackBoundPower.BoundOutside);
        var after = After(s, Play(Card("Strike"), 0, 1, Attack(9))).Single();
        Assert.Equal(91, after.Enemy(0)!.Hp); // 9, not 15
        Assert.Equal(6, after.P.Powers[PowerId.Vigor]); // the attack under way spends it, not this one
    }

    [Fact]
    public void AimQuietusAndGigantificationReservedForAnAttackUnderWayAddNothing()
    {
        var arrow = SimProps.Move | SimProps.FromArrow;
        foreach (var name in new[] { PowerId.Aim, PowerId.Quietus, PowerId.Gigantification })
        {
            var s = WithPowers();
            s.P.Powers = s.P.Powers.With(name, 3, AttackBoundPower.BoundOutside);
            var after = After(s, Play(Card("Strike"), 0, 1, Attack(10, props: arrow))).Single();
            Assert.Equal(90, after.Enemy(0)!.Hp);
            Assert.True(after.P.Powers.Has(name), name.Name);
        }
    }

    [Fact]
    public void FlutterHalvesHitsOnlyUntilItsLastStackGoes()
    {
        // Wah Ki (2 × 2) on a Thieving Hopper with Flutter 1: the first hit is halved to 1 and takes the last stack
        // (FlutterPower.AfterDamageReceived stuns it), so the second hit deals its full 2.
        var s = WithPowers();
        s.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Flutter, 1);
        var after = After(s, Play(Card("WahKi"), 0, 1, Attack(2, hits: 2))).Single();
        Assert.Equal(97, after.Enemy(0)!.Hp);
        Assert.True(after.Enemy(0)!.Stunned);
        Assert.False(after.Enemy(0)!.Powers.Has(PowerId.Flutter));
    }

    [Fact]
    public void GigantificationTriplesOneAttackPerStack()
    {
        var s = WithPowers((PowerId.Gigantification, 1));
        var after = After(s, Play(Card("Strike"), 0, 2, Attack(5))).Single();
        Assert.Equal(80, after.Enemy(0)!.Hp); // 15, then 5
        Assert.False(after.P.Powers.Has(PowerId.Gigantification));
    }

    [Fact]
    public void SlowGrowsWithEveryCardPlayed()
    {
        var s = new SimState { Enemies = { Enemy(0, 100) } };
        s.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Slow, 1, 2); // 2 cards played this turn: +20%
        var after = After(s, Play(Card("Strike"), 0, 2, Attack(10))).Single();
        Assert.Equal(75, after.Enemy(0)!.Hp); // 12, then 13
    }

    [Fact]
    public void LethalityOnTheFirstAttackPlayOfTheTurnOnly()
    {
        var s = WithPowers((PowerId.Lethality, 50));
        var after = After(s, Play(Card("Strike"), 0, 2, Attack(10))).Single();
        Assert.Equal(75, after.Enemy(0)!.Hp); // 15, then 10
    }

    [Fact]
    public void DexterityAndFrailOnBlock()
    {
        var s = WithPowers((PowerId.Dexterity, 2), (PowerId.Frail, 1));
        Assert.Equal(5, After(s, Play(Card("Defend", SimCardType.Skill), -1, 1, Block(5))).Single().P.Block); // (5 + 2) × 0.75
    }

    [Fact]
    public void FisticuffsBlockFromDamageGetsDexterity()
    {
        // Fisticuffs.OnPlay: GainBlock(the damage dealt, ValueProp.Move), a powered block: 7 dealt with 1 Dexterity is 8.
        var s = WithPowers((PowerId.Dexterity, 1));
        var after = After(s, Play(Card("Fisticuffs"), 0, 1, Attack(7), Block(7))).Single();
        Assert.Equal(93, after.Enemy(0)!.Hp);
        Assert.Equal(8, after.P.Block);
        s.P.Powers = s.P.Powers.With(PowerId.Frail, 1);
        Assert.Equal(6, After(s, Play(Card("Fisticuffs"), 0, 1, Attack(7), Block(7))).Single().P.Block); // (7 + 1) × 0.75
    }

    [Fact]
    public void WeakAppliedByTheCardLowersTheIntent()
    {
        var s = new SimState { Enemies = { Enemy(0, 50) } };
        s.Enemies[0].Intent = new List<IntentHit> { new(12m) };
        var script = Play(Card("Neutralize"), 0, 1, Attack(3), new ApplyPowerAction(PowerId.Weak, 1) { Debuff = true });
        var o = SimEngine.Run(s, script);
        Assert.Equal(9, o.TakenMax); // 12 × 0.75
    }
}

/// <summary>The first hit of a card's attack on each enemy (the damage-formula audit compares it with the game's preview).</summary>
public class FirstHitTests
{
    [Fact]
    public void FirstHitIsRecordedPerEnemyAndOnlyWhenEveryBranchAgrees()
    {
        var s = new SimState { Enemies = { V2Script.Enemy(0, 100), V2Script.Enemy(1, 100) } };
        s.P.Powers = s.P.Powers.With(PowerId.Strength, 2);
        var all = SimEngine.Run(s, V2Script.Play(V2Script.Card("Cleave"), -1, 1, V2Script.Attack(8, hits: 2, mode: SimPlay.Target.All)));
        Assert.Equal(10, all.PlayFirstHits[0]);
        Assert.Equal(10, all.PlayFirstHits[1]);
        // Random targets: the branches disagree on who was hit first, so nothing is recorded.
        var random = SimEngine.Run(s, V2Script.Play(V2Script.Card("SwordBoomerang"), -1, 1, V2Script.Attack(3, hits: 1, mode: SimPlay.Target.Random)));
        Assert.Empty(random.PlayFirstHits);
    }
}

/// <summary>Cho Ki puts your Stretch back (ChoKi.OnPlay: PowerCmd.Apply of the Stretch you have), so playing it keeps it.</summary>
public class ChoKiTests
{
    [Fact]
    public void ChoKiKeepsYourStretch()
    {
        var s = new SimState { Enemies = { V2Script.Enemy(0, 100) } };
        s.P.Powers = s.P.Powers.With(PowerId.Stretch, 3);
        var choKi = new SimCard("ChoKi", SimCardType.Skill) { GainsBlock = true };
        var after = Assert.Single(V2Script.After(s, V2Script.Play(choKi, -1, 1, V2Script.Block(5),
            new ApplyPowerAction(PowerId.Stretch, 0) { To = ApplyPowerAction.Where.You, CopiesOwn = true })));
        Assert.Equal(8, after.P.Block);
        Assert.Equal(3, after.P.Powers[PowerId.Stretch]);
        // Any other block card spends it.
        var other = Assert.Single(V2Script.After(s, V2Script.Play(new SimCard("Defend", SimCardType.Skill) { GainsBlock = true }, -1, 1, V2Script.Block(5))));
        Assert.False(other.P.Powers.Has(PowerId.Stretch));
    }
}
