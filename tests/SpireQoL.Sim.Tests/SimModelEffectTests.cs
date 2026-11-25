using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Decision #156: models that fired their tab without doing what their game class does (ModelEffectCheck found them), each now doing
/// it, from its hook in the decompile.
/// </summary>
public class SimModelEffectTests
{
    private static SimState State(int enemyHp = 100, int enemyBlock = 0)
    {
        var state = new SimState { Enemies = { Enemy(id: 0, enemyHp, enemyBlock) } };
        state.You.Hp = 50;
        state.You.MaxHp = 80;
        return state;
    }

    private static SimState Power(SimState state, PowerId power, int amount)
    {
        state.You.Powers = state.You.Powers.With(power, amount);
        return state;
    }

    private static SimState Relic(SimState state, RelicId relic, params (string Var, int Value)[] vars)
    {
        state.You.Relics = state.You.Relics.With(relic, amount: 0, data: 1);
        foreach (var (name, value) in vars) state.Vars[$"{Who.You}:{relic}:{name}"] = value;
        return state;
    }

    private static SimScript Events(params SimAction[] body) => Play(Card("X", SimCardType.Skill), target: Who.None, plays: 1, body);

    private static SimState After(SimState state, SimScript script) => Assert.Single(ScriptBuilder.After(state, script));

    [Fact]
    public void CorrosiveWavePoisonsEveryEnemyForEachCardDrawn()
    {
        // CorrosiveWavePower.AfterCardDrawn: its amount of poison on every hittable enemy, each draw.
        var after = After(Power(State(), PowerId.CorrosiveWave, amount: 2), Events(new CardEventAction(CardEvent.Drawn, Count: 3)));
        Assert.Equal(6, after.Enemies[0].PoisonStacks);
    }

    [Fact]
    public void ArsenalAndFertilizerGiveStrength()
    {
        // ArsenalPower: its amount for each card made; Fertilizer: its StrengthPower when a Sadness is made.
        Assert.Equal(2, After(Power(State(), PowerId.Arsenal, amount: 1), Events(new CardEventAction(CardEvent.Generated, Count: 2))).You.Powers[PowerId.Strength]);
        Assert.Equal(1, After(Relic(State(), RelicId.Fertilizer, ("StrengthPower", 1)), Events(new CardEventAction(CardEvent.Generated, Count: 1) { Card = new SimCard(CardId.Sadness.Name, SimCardType.Status) })).You.Powers[PowerId.Strength]);
    }

    [Fact]
    public void HelicalDartGivesDexterityForAShiv()
    {
        var after = After(Relic(State(), RelicId.HelicalDart, ("DexterityPower", 1)), Play(Card("Shiv", SimCardType.Attack, SimCardTag.Shiv), target: 0, plays: 1, Attack(baseDamage: 4)));
        Assert.Equal(1, after.You.Powers[PowerId.Dexterity]);
    }

    [Fact]
    public void WhittlingKnifeGivesAimOnAShuffle()
    {
        var after = After(Relic(State(), RelicId.WhittlingKnife, ("AimPower", 5)), Events(new CardEventAction(CardEvent.Shuffled, Count: 1)));
        Assert.Equal(5, after.You.Powers[PowerId.Aim]);
    }

    [Fact]
    public void RainbowRingGivesStrengthAndDexterityOnTheThirdKind()
    {
        // An Attack and a Skill played already: the Power completes the three, once a turn.
        // Its own counts of this turn's kinds, as the game keeps them (RainbowRing.AttacksPlayedThisTurn, SkillsPlayedThisTurn: decision #195).
        var state = Relic(State(), RelicId.RainbowRing, ("StrengthPower", 1), ("DexterityPower", 1));
        state.You.Relics = state.You.Relics.With(RelicId.RainbowRing, amount: 0, data: 0);
        state.You.Fields = state.You.Fields.With("RainbowRing.AttacksPlayedThisTurn", amount: 1).With("RainbowRing.SkillsPlayedThisTurn", amount: 1);
        var after = After(state, Play(Card("Inflame", SimCardType.Power), target: Who.None, plays: 1));
        Assert.Equal(1, after.You.Powers[PowerId.Strength]);
        Assert.Equal(1, after.You.Powers[PowerId.Dexterity]);
    }

    [Fact]
    public void CompostGivesEnergyWhenAnEnemyDies()
    {
        // Another enemy keeps the fight going: the last one's death ends it, and PlayerCmd.GainEnergy gives nothing then (decision #301).
        var state = WithBystander(Power(State(enemyHp: 5), PowerId.Compost, amount: 1));
        state.You.Energy = 0;
        Assert.Equal(1, After(state, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 10))).You.Energy);
        var last = Power(State(enemyHp: 5), PowerId.Compost, amount: 1);
        last.You.Energy = 0;
        Assert.Equal(0, After(last, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 10))).You.Energy);
    }

    [Fact]
    public void ATeammatesCompostGivesYouEnergyToo()
    {
        // CompostPower.AfterDeath (read from its code, decision #277): its amount for each living player on its owner's side, you included.
        var state = WithBystander(State(enemyHp: 5)); // the fight goes on (decision #301)
        state.You.Energy = 0;
        state.Teammates.Add(new SimPlayer { Hp = 40, MaxHp = 60, Powers = PowerSet.Empty.With(PowerId.Compost, amount: 2) });
        Assert.Equal(2, After(state, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 10))).You.Energy);
    }

    [Fact]
    public void DemonTongueHealsTheFirstHpLostOnYourTurn()
    {
        // 5 lost, healed; then 3 lost: once a turn.
        var state = Relic(State(), RelicId.DemonTongue);
        state.You.Relics = state.You.Relics.With(RelicId.DemonTongue, amount: 0, data: 0);
        Assert.Equal(47, After(state, Events(new LoseHpAction(Amount: 5), new LoseHpAction(Amount: 3))).You.Hp);
    }

    [Fact]
    public void HandDrillMakesTheRestOfTheAttackHitHarder()
    {
        // Two hits of 10 on 5 block: the first breaks it (5 through) and applies Vulnerable 2, so the second deals 15.
        var state = Relic(State(enemyBlock: 5), RelicId.HandDrill, ("VulnerablePower", 2));
        var after = After(state, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 10, hits: 2)));
        Assert.Equal(80, after.Enemies[0].Hp);
        Assert.Equal(2, after.Enemies[0].Powers[PowerId.Vulnerable]);
    }

    [Fact]
    public void HandDrillsVulnerableIsHeardByTheListeners()
    {
        // HandDrill.AfterBlockBroken: PowerCmd.Apply<VulnerablePower>(target, ..., base.Owner.Creature): the game's AfterPowerAmountChanged tells
        // every listener. SleightOfFleshPower deals its amount to an enemy its owner lands a debuff on; ViciousPower draws for a Vulnerable its owner
        // lands. Heard once the hit is over (a draw can't branch inside it).
        var state = Power(Relic(State(enemyBlock: 5), RelicId.HandDrill, ("VulnerablePower", 2)), PowerId.SleightOfFlesh, amount: 3);
        var after = After(state, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 10)));
        Assert.Equal(100 - 5 - 3, after.Enemies[0].Hp);
        Assert.True(after.Fired.Has(PowerId.SleightOfFlesh.Name));

        var drawing = Power(Relic(State(enemyBlock: 5), RelicId.HandDrill, ("VulnerablePower", 2)), PowerId.Vicious, amount: 1);
        drawing.You.HandCount = 4;
        drawing.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(new SimDrawCard("plain"), Count: 10) } };
        var drew = After(drawing, Play(Card("Strike"), target: 0, plays: 1, Attack(baseDamage: 10)));
        Assert.Equal(5, drew.You.HandCount);
        Assert.True(drew.Fired.Has(PowerId.Vicious.Name));
    }

    [Fact]
    public void KnockoutBlowsStarsWaitOnItsKill()
    {
        // KnockoutBlow.OnPlay gains stars only when its attack kills the target (decision #190: a WhenAction); Black Hole then hits every
        // enemy. It used to count the stars on every play.
        SimState WithBlackHole(int targetHp)
        {
            var state = new SimState { Enemies = { Enemy(id: 0, hp: targetHp), Enemy(id: 1, hp: 50) } };
            state.You.Powers = state.You.Powers.With(PowerId.BlackHole, amount: 3);
            return state;
        }
        var knockout = Play(Card("KnockoutBlow"), target: 0, plays: 1, Attack(baseDamage: 10), new WhenAction(new AttackKilledCondition(), new SimAction[] { new CardEventAction(CardEvent.StarsGained, Count: 1) }));
        Assert.Equal(47, After(WithBlackHole(targetHp: 5), knockout).Enemies[1].Hp);
        Assert.Equal(50, After(WithBlackHole(targetHp: 30), knockout).Enemies[1].Hp);
    }

    [Fact]
    public void AWarningOnlySomeOutcomesSetOffHasItsChance()
    {
        // Decision #159: a random hit (one of two enemies) wakes the sleeping one half the time: the Asleep warning's chance is 50%,
        // from its rule's per-branch test (decision #307: its next move an attack where it woke, still asleep on its 2 stacks where it didn't).
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 40), Enemy(id: 1, hp: 40) } };
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Asleep, amount: 2);
        WakeTabParityTests.WithSleeperRules(state.Enemies[0]);
        var proc = new SimPlay { Name = "Defend" };
        proc.Procs.Add(new ProcHit { Spread = SimPlay.Target.Random, Damage = { [0] = 5, [1] = 5 }, Times = 1 });
        var rule = SimModels.Get("AsleepPower")!.OutcomeTab(state, SimEngine.Run(state), owner: 0)!;
        var outcome = SimEngine.Run(state, proc);
        Assert.True(rule.SetOff(outcome));
        Assert.Equal("50%", outcome.Chance(rule.InBranch!).Text());
    }
}
