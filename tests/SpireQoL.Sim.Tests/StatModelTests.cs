using SpireQoL.Sim.Models;
using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The damage and block sources that change during a turn (Sim/Models/StatModels.cs), each as the game resolves it
/// (decompile v0.113.1): the damage of every hit is worked out from the state when it lands.
/// </summary>
public class StatModelTests
{
    private static SimState WithPowers(params (PowerId Name, int Amount)[] mine)
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 100) } };
        foreach (var (power, a) in mine) state.You.Powers = state.You.Powers.With(power, a);
        return state;
    }

    [Fact]
    public void StrengthAddsToPoweredHitsOnly()
    {
        var state = WithPowers((PowerId.Strength, 3));
        Assert.Equal(91, After(state, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 6))).Single().Enemy(id: 0)!.Hp);
        // Unpowered (Thorns-like) damage doesn't get Strength.
        Assert.Equal(94, After(state, Play(Card("X"), target: 0, plays: 1, Attack(baseDamage: 6, props: SimProps.Move | SimProps.Unpowered))).Single().Enemy(id: 0)!.Hp);
    }

    [Fact]
    public void VulnerableAndPaperPhrogAndDebilitate()
    {
        var state = WithPowers();
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Vulnerable, amount: 2);
        Assert.Equal(85, After(state, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 10))).Single().Enemy(id: 0)!.Hp); // 15
        state.You.Relics = RelicSet.Empty.With(RelicId.PaperPhrog, amount: 0);
        Assert.Equal(83, After(state, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 10))).Single().Enemy(id: 0)!.Hp); // 1.75 → 17
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.Debilitate, amount: 1);
        Assert.Equal(75, After(state, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 10))).Single().Enemy(id: 0)!.Hp); // 1.75 + 0.75 → 25
    }

    [Fact]
    public void TenderReplayHitsOneLessOnItsSecondPlay()
    {
        // A report from 0.45.0: Wah Sweep+ played twice into a 23-HP Hunter Killer dealt 9, then 8.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 23) } };
        state.You.Powers = PowerSet.Empty.With(PowerId.Tender, amount: 1).With(PowerId.Strength, amount: -2);
        var after = After(state, Play(Card("WahSweep"), target: 0, plays: 2, Attack(baseDamage: 11))).Single();
        Assert.Equal(6, after.Enemy(id: 0)!.Hp);
        Assert.Equal(-4, after.You.Powers[PowerId.Strength]);
        Assert.Equal(-2, after.You.Powers[PowerId.Dexterity]);
    }

    [Fact]
    public void ArtifactTakesTendersStrengthLoss()
    {
        var state = WithPowers((PowerId.Tender, 1), (PowerId.Artifact, 1));
        var after = After(state, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 6))).Single();
        Assert.Equal(0, after.You.Powers[PowerId.Strength]); // the first debuff was taken
        Assert.Equal(-1, after.You.Powers[PowerId.Dexterity]);
        Assert.False(after.You.Powers.Has(PowerId.Artifact));
    }

    [Fact]
    public void PenNibDoublesOnlyThePlayThatReachesTen()
    {
        // A report from 0.46.2: Ki Whip with Pen Nib ready (9), played twice into a 56-HP Magi Knight: 14, then 7.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 56) } };
        state.You.Relics = RelicSet.Empty.With(RelicId.PenNib, amount: 9);
        var after = After(state, Play(Card("KiWhip"), target: 0, plays: 2, Attack(baseDamage: 7))).Single();
        Assert.Equal(35, after.Enemy(id: 0)!.Hp);
        Assert.Equal(1, after.You.Relics[RelicId.PenNib]);
        // At 8, the replay's second play is the doubled one.
        state.You.Relics = RelicSet.Empty.With(RelicId.PenNib, amount: 8);
        Assert.Equal(35, After(state, Play(Card("KiWhip"), target: 0, plays: 2, Attack(baseDamage: 7))).Single().Enemy(id: 0)!.Hp);
        Assert.Equal(1, After(state, Play(Card("KiWhip"), target: 0, plays: 2, Attack(baseDamage: 7))).Single().Fired["PenNib"]);
    }

    [Fact]
    public void PenNibBoundToTheCardMidPlayDoublesNoCardInHand()
    {
        // Reports from 1.11.4 (Regent): Jackpot was the 10th attack and mid-play when the hand was simulated; the game
        // doubles only Jackpot (AttackToDouble), so Sovereign Blade dealt 60, not 120. Its next attack counts 1.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 200) } };
        state.You.Relics = RelicSet.Empty.With(RelicId.PenNib, amount: 0, data: 1);
        var after = After(state, Play(Card("SovereignBlade"), target: 0, plays: 1, Attack(baseDamage: 60))).Single();
        Assert.Equal(140, after.Enemy(id: 0)!.Hp);
        Assert.Equal(1, after.You.Relics[RelicId.PenNib]);
        Assert.False(after.Fired.Has("PenNib"));
    }

    [Fact]
    public void MightyWingsStrengthLandsBeforeTheNextHit()
    {
        // A report from 0.46.2: Repeat Shot+ into a 70-HP Soul Nexus dealt 33 (30 or more: +2 Strength), then 36.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 70) } };
        state.You.Relics = RelicSet.Empty.With(RelicId.MightyWings, amount: 2, data: 30);
        var after = After(state, Play(Card("RepeatShot"), target: 0, plays: 1, Attack(baseDamage: 22, hits: 2, terms: new CapturedTerms { Mul = 1.5m }))).Single();
        Assert.Equal(1, after.Enemy(id: 0)!.Hp);
        Assert.Equal(4, after.You.Powers[PowerId.Strength]);
        Assert.Equal(2, after.Fired["MightyWings"]);
    }

    [Fact]
    public void VigorIsSpentByTheFirstAttackOnly()
    {
        var state = WithPowers((PowerId.Vigor, 5));
        var after = After(state, Play(Card("Strike"), target: 0, plays: 2, Attack(baseDamage: 6))).Single();
        Assert.Equal(83, after.Enemy(id: 0)!.Hp); // 11, then 6
        Assert.False(after.You.Powers.Has(PowerId.Vigor));
    }

    [Fact]
    public void VigorReservedForAnAttackUnderWayAddsNothing()
    {
        // VigorPower.BeforeAttack binds the first attack (commandToModify); until that attack spends it, ModifyDamageAdditive gives
        // every other card's damage 0. The adapter reads that as bound outside the simulation (a card mid-play).
        var state = WithPowers();
        state.You.Powers = state.You.Powers.With(PowerId.Vigor, amount: 6);
        KeptOutside(state, PowerId.Vigor);
        var after = After(state, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 9))).Single();
        Assert.Equal(91, after.Enemy(id: 0)!.Hp); // 9, not 15
        Assert.Equal(6, after.You.Powers[PowerId.Vigor]); // the attack under way spends it, not this one
    }

    /// <summary>A read model's kept attack one under way as the state was read (the adapter's SimReaders.KeptOutside: no simulated attack's id).</summary>
    private static void KeptOutside(SimState state, PowerId power) =>
        SimEngine.SetField(state, Who.You, Assert.IsType<ReadModel>(SimModels.Get(power.Name)).FieldKey("Data.commandToModify"), int.MaxValue);

    [Fact]
    public void AimQuietusAndGigantificationReservedForAnAttackUnderWayAddNothing()
    {
        var arrow = SimProps.Move | SimProps.FromArrow;
        foreach (var name in new[] { PowerId.Aim, PowerId.Quietus, PowerId.Gigantification })
        {
            var state = WithPowers();
            // A read model keeps the attack in its own data (decision #228); a hand-written one in its Data.
            state.You.Powers = state.You.Powers.With(name, amount: 3, SimModels.Get(name.Name) is ReadModel ? 0 : AttackBoundPower.BoundOutside);
            if (SimModels.Get(name.Name) is ReadModel) KeptOutside(state, name);
            var after = After(state, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 10, props: arrow))).Single();
            Assert.Equal(90, after.Enemy(id: 0)!.Hp);
            Assert.True(after.You.Powers.Has(name), name.Name);
        }
    }

    [Fact]
    public void FlutterHalvesHitsOnlyUntilItsLastStackGoes()
    {
        // Wah Ki (2 × 2) on a Thieving Hopper with Flutter 1: the first hit is halved to 1 and takes the last stack
        // (FlutterPower.AfterDamageReceived stuns it), so the second hit deals its full 2.
        var state = WithPowers();
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Flutter, amount: 1);
        var after = After(state, Play(Card("WahKi"), target: 0, plays: 1, Attack(baseDamage: 2, hits: 2))).Single();
        Assert.Equal(97, after.Enemy(id: 0)!.Hp);
        Assert.True(after.Enemy(id: 0)!.Stunned);
        Assert.False(after.Enemy(id: 0)!.Powers.Has(PowerId.Flutter));
    }

    [Fact]
    public void GigantificationTriplesOneAttackPerStack()
    {
        var state = WithPowers((PowerId.Gigantification, 1));
        var after = After(state, Play(Card("Strike"), target: 0, plays: 2, Attack(baseDamage: 5))).Single();
        Assert.Equal(80, after.Enemy(id: 0)!.Hp); // 15, then 5
        Assert.False(after.You.Powers.Has(PowerId.Gigantification));
    }

    [Fact]
    public void SlowGrowsWithEveryCardPlayed()
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 100) } };
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Slow, amount: 1, data: 2); // 2 cards played this turn: +20%
        var after = After(state, Play(Card("Strike"), target: 0, plays: 2, Attack(baseDamage: 10))).Single();
        Assert.Equal(75, after.Enemy(id: 0)!.Hp); // 12, then 13
    }

    [Fact]
    public void LethalityOnTheFirstAttackPlayOfTheTurnOnly()
    {
        var state = WithPowers((PowerId.Lethality, 50));
        var after = After(state, Play(Card("Strike"), target: 0, plays: 2, Attack(baseDamage: 10))).Single();
        Assert.Equal(75, after.Enemy(id: 0)!.Hp); // 15, then 10
    }

    [Fact]
    public void DexterityAndFrailOnBlock()
    {
        var state = WithPowers((PowerId.Dexterity, 2), (PowerId.Frail, 1));
        Assert.Equal(5, After(state, Play(Card("Defend", SimCardType.Skill), target: Who.None, plays: 1, Block(baseBlock: 5))).Single().You.Block); // (5 + 2) × 0.75
    }

    [Fact]
    public void FisticuffsBlockFromDamageGetsDexterity()
    {
        // Fisticuffs.OnPlay: GainBlock(the damage dealt, ValueProp.Move), a powered block: 7 dealt with 1 Dexterity is 8.
        var state = WithPowers((PowerId.Dexterity, 1));
        var after = After(state, Play(Card("Fisticuffs"), target: 0, plays: 1, Attack(baseDamage: 7), Block(baseBlock: 7))).Single();
        Assert.Equal(93, after.Enemy(id: 0)!.Hp);
        Assert.Equal(8, after.You.Block);
        state.You.Powers = state.You.Powers.With(PowerId.Frail, amount: 1);
        Assert.Equal(6, After(state, Play(Card("Fisticuffs"), target: 0, plays: 1, Attack(baseDamage: 7), Block(baseBlock: 7))).Single().You.Block); // (7 + 1) × 0.75
    }

    [Fact]
    public void WeakAppliedByTheCardLowersTheIntent()
    {
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 50) } };
        state.Enemies[0].Intent = new List<IntentHit> { new(Base: 12m) };
        var script = Play(Card("Neutralize"), target: 0, plays: 1, Attack(baseDamage: 3), new ApplyPowerAction(PowerId.Weak, Amount: 1) { Debuff = true });
        var outcome = SimEngine.Run(state, script);
        Assert.Equal(9, outcome.TakenMax); // 12 × 0.75
    }
}

/// <summary>The first hit of a card's attack on each enemy (the damage-formula audit compares it with the game's preview).</summary>
public class FirstHitTests
{
    [Fact]
    public void FirstHitIsRecordedPerEnemyAndOnlyWhenEveryBranchAgrees()
    {
        var state = new SimState { Enemies = { ScriptBuilder.Enemy(id: 0, hp: 100), ScriptBuilder.Enemy(id: 1, hp: 100) } };
        state.You.Powers = state.You.Powers.With(PowerId.Strength, amount: 2);
        var all = SimEngine.Run(state, ScriptBuilder.Play(ScriptBuilder.Card("Cleave"), target: Who.None, plays: 1, ScriptBuilder.Attack(baseDamage: 8, hits: 2, mode: SimPlay.Target.All)));
        Assert.Equal(10, all.PlayFirstHits[0]);
        Assert.Equal(10, all.PlayFirstHits[1]);
        // Random targets: the branches disagree on who was hit first, so nothing is recorded.
        var random = SimEngine.Run(state, ScriptBuilder.Play(ScriptBuilder.Card("SwordBoomerang"), target: Who.None, plays: 1, ScriptBuilder.Attack(baseDamage: 3, hits: 1, mode: SimPlay.Target.Random)));
        Assert.Empty(random.PlayFirstHits);
    }
}

/// <summary>Cho Ki puts your Stretch back (ChoKi.OnPlay: PowerCmd.Apply of the Stretch you have), so playing it keeps it.</summary>
public class ChoKiTests
{
    [Fact]
    public void ChoKiKeepsYourStretch()
    {
        var state = new SimState { Enemies = { ScriptBuilder.Enemy(id: 0, hp: 100) } };
        state.You.Powers = state.You.Powers.With(PowerId.Stretch, amount: 3);
        var choKi = new SimCard("ChoKi", SimCardType.Skill) { GainsBlock = true };
        var after = Assert.Single(ScriptBuilder.After(state, ScriptBuilder.Play(choKi, target: Who.None, plays: 1, ScriptBuilder.Block(baseBlock: 5),
            new ApplyPowerAction(PowerId.Stretch, Amount: 0) { To = ApplyPowerAction.Where.You, CopiesOwn = true })));
        Assert.Equal(8, after.You.Block);
        Assert.Equal(3, after.You.Powers[PowerId.Stretch]);
        // Any other block card spends it.
        var other = Assert.Single(ScriptBuilder.After(state, ScriptBuilder.Play(new SimCard("Defend", SimCardType.Skill) { GainsBlock = true }, target: Who.None, plays: 1, ScriptBuilder.Block(baseBlock: 5))));
        Assert.False(other.You.Powers.Has(PowerId.Stretch));
    }
}
