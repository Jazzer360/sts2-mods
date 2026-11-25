using Xunit;
using static SpireQoL.Sim.Tests.ScriptBuilder;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Powers whose every application is a copy of its own (PowerInstanceType.Instanced), each with its own amount and values, which the
/// simulator holds as one Powers entry: each model says how its copies act (SimModel.Instanced). Monologue's copies each give their
/// own Strength (the total over them: SimEngine.VarTotal), The Bomb's each tick and go off on their own (its copies in your fields), and
/// each Covered gives its applier Intercept (AfterApplied on every copy).
/// </summary>
public class InstancedCopyTests
{
    private static SimEnemy Attacker(int id, int hp, int hit = 0) => new() { Id = id, Name = $"E{id}", Hp = hp, Hits = hit > 0 ? new() { hit } : new() };

    /// <summary>Your Intercept right after the play: the enemies' turn's end takes it off (InterceptPower.AfterSideTurnEnd, side == CombatSide.Enemy).</summary>
    private static int InterceptAfterThePlay(SimState state, SimScript play)
    {
        SimEngine.RunScriptTo(state, play, out var branches, out _);
        return Assert.Single(branches).You.Powers[PowerId.Intercept];
    }

    // ---- Monologue: each copy binds and gives its own Strength

    [Fact]
    public void TwoMonologuesGiveTwoStrengthACard()
    {
        // MonologuePower.BeforeCardPlayed binds each copy's DynamicVars.Strength, AfterCardPlayed applies it: two copies of 1 each
        // give 2 a card. Read from its code (decision #196): each copy's Strength is its own state, as the adapter reads each instance.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 100) } };
        state.You.Powers = state.You.Powers.With(PowerId.Monologue, amount: 2);
        foreach (var copy in new[] { 1, 2 })
            state.You.Fields = state.You.Fields.With($"{PowerId.Monologue.Name}@{copy}", amount: 1).With($"{PowerId.Monologue.Name}@{copy}.Var.StrengthPower", amount: 1);
        var after = Assert.Single(After(state, Play(Card("Strike"), target: 0, plays: 2, Attack(baseDamage: 5))));
        Assert.Equal(4, after.You.Powers[PowerId.Strength]);
        Assert.Equal(100 - 5 - 7, after.Enemy(id: 0)!.Hp); // the second play has the first's 2
    }

    [Fact]
    public void TwoMonologuesEachGiveTheirOwnStrength()
    {
        // An upgraded Monologue's copy (2) beside one of 1: 3 a card, each its own.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 100) } };
        state.You.Powers = state.You.Powers.With(PowerId.Monologue, amount: 2);
        foreach (var (copy, strength) in new[] { (1, 1), (2, 2) })
            state.You.Fields = state.You.Fields.With($"{PowerId.Monologue.Name}@{copy}", amount: 1).With($"{PowerId.Monologue.Name}@{copy}.Var.StrengthPower", amount: strength);
        var after = Assert.Single(After(state, Play(Card("Defend", SimCardType.Skill), target: Who.None, plays: 1)));
        Assert.Equal(3, after.You.Powers[PowerId.Strength]);
    }

    [Fact]
    public void OneMonologueKeptAsOneGivesItsCanonicalStrength()
    {
        // A state with the power's entry alone (no copies listed): one copy, its Strength the class's canonical 1.
        var state = new SimState { Enemies = { Enemy(id: 0, hp: 100) } };
        state.You.Powers = state.You.Powers.With(PowerId.Monologue, amount: 1);
        var after = Assert.Single(After(state, Play(Card("Defend", SimCardType.Skill), target: Who.None, plays: 1)));
        Assert.Equal(1, after.You.Powers[PowerId.Strength]);
    }

    // ---- The Bomb: each copy ticks and goes off on its own (read from its code: its copies in your fields, decision #196)

    /// <summary>Your Bombs as the adapter lists them: each copy's turns left (its amount) and its own Damage (its DynamicVars, set as it landed).</summary>
    private static void Bombs(SimState state, params (int Turns, int Damage)[] bombs)
    {
        state.You.Powers = state.You.Powers.With(PowerId.TheBomb, amount: bombs.Sum(bomb => bomb.Turns));
        for (var copy = 1; copy <= bombs.Length; copy++)
            state.You.Fields = state.You.Fields.With($"{PowerId.TheBomb.Name}@{copy}", bombs[copy - 1].Turns).With($"{PowerId.TheBomb.Name}@{copy}.Var.Damage", bombs[copy - 1].Damage);
    }

    private static IEnumerable<int> TurnsLeft(SimState state) =>
        state.You.Fields.Items.Where(entry => entry.Name.StartsWith(PowerId.TheBomb.Name + "@", StringComparison.Ordinal) && !entry.Name.Contains('.', StringComparison.Ordinal)).Select(entry => entry.Amount);

    [Fact]
    public void ABombOnItsLastTurnGoesOffBesideOneThatIsNot()
    {
        // TheBombPower.BeforeSideTurnEnd, per copy: one on its last turn (40) goes off, one with 3 turns ticks to 2. Merged (4 turns)
        // neither would.
        var state = new SimState { Enemies = { Attacker(id: 0, hp: 50) } };
        Bombs(state, (1, 40), (3, 50));
        var after = Assert.Single(SimEngine.Run(state).Branches);
        Assert.Equal(10, after.Enemy(id: 0)!.Hp);
        Assert.Equal(new[] { 2 }, TurnsLeft(after));
        Assert.Equal(2, after.You.Powers[PowerId.TheBomb]);
    }

    [Fact]
    public void TwoBombsOnTheirLastTurnBothGoOff()
    {
        var state = new SimState { Enemies = { Attacker(id: 0, hp: 100) } };
        Bombs(state, (1, 40), (1, 50));
        var after = Assert.Single(SimEngine.Run(state).Branches);
        Assert.Equal(10, after.Enemy(id: 0)!.Hp);
        Assert.False(after.You.Powers.Has(PowerId.TheBomb));
        Assert.Empty(TurnsLeft(after));
    }

    [Fact]
    public void ABombPlayedNowLandsBesideOneAboutToGoOff()
    {
        // The Bomb played this turn (3 turns) beside one on its last: a copy of its own, so the old one still goes off.
        var state = new SimState { Enemies = { Attacker(id: 0, hp: 50) } };
        Bombs(state, (1, 40));
        var play = Play(Card("TheBomb", SimCardType.Skill), target: Who.None, plays: 1, new ApplyPowerAction(PowerId.TheBomb, Amount: 3) { To = ApplyPowerAction.Where.You });
        var outcome = SimEngine.Run(state, play);
        var after = Assert.Single(outcome.Branches);
        Assert.Equal(10, after.Enemy(id: 0)!.Hp);
        Assert.Equal(new[] { 2 }, TurnsLeft(after));
    }

    [Fact]
    public void ABombKeptAsOneIsOneCopyWithItsCanonicalDamage()
    {
        // A state with the power's entry alone (no copies listed): one copy, its Damage the class's canonical 40 until something sets it.
        var state = new SimState { Enemies = { Attacker(id: 0, hp: 50) } };
        state.You.Powers = state.You.Powers.With(PowerId.TheBomb, amount: 1);
        var after = Assert.Single(SimEngine.Run(state).Branches);
        Assert.Equal(10, after.Enemy(id: 0)!.Hp);
        Assert.False(after.You.Powers.Has(PowerId.TheBomb));
    }

    // ---- Covered: each Intercept played on a teammate is a copy, each giving its applier Intercept

    [Fact]
    public void YourInterceptOnATeammateSomeoneElseCoversGivesYouIntercept()
    {
        // Teammate 0 is covered by teammate 1 already. Your Intercept on them is a new Covered (instanced), whose AfterApplied gives
        // you Intercept: the enemy's 10 on you is doubled (covered players + 1).
        var state = new SimState { Enemies = { Attacker(id: 1, hp: 50, hit: 10) } };
        state.Enemies[0].TeammateHits = new() { new() { 10 }, new() { 10 } };
        state.Enemies[0].Intent = new List<IntentHit> { new(Base: 10) };
        var covered = new SimPlayer { Hp = 50, HandCount = 0 };
        covered.Powers = covered.Powers.With(PowerId.Covered, amount: 1, data: Models.CoveredPower.CovererBit(Who.Teammate(teammateIndex: 1)));
        var coverer = new SimPlayer { Hp = 50, HandCount = 0 };
        coverer.Powers = coverer.Powers.With(PowerId.Intercept, amount: 1);
        coverer.Fields = coverer.Fields.With(Models.CoveredPower.CoveredCount, 1);
        state.Teammates.Add(covered);
        state.Teammates.Add(coverer);
        var play = Play(Card("Intercept", SimCardType.Skill), Who.Teammate(teammateIndex: 0), plays: 1,
            new ApplyPowerAction(PowerId.Covered, Amount: 1) { To = ApplyPowerAction.Where.Creature, Creature = Who.Teammate(teammateIndex: 0) });
        var outcome = SimEngine.Run(state, play);
        Assert.Equal(20, outcome.TakenMax);
        Assert.Equal(0, outcome.TeammateTakenMax(teammateIndex: 0));
        Assert.Equal(1, InterceptAfterThePlay(state, play));
    }

    [Fact]
    public void YourSecondInterceptOnTheSameTeammateCoversThemOnce()
    {
        // You cover teammate 0 already: InterceptPower.AddCoveredCreature skips a creature it covers, so your Intercept stays at one
        // covered player (× 2, not × 3).
        var state = new SimState { Enemies = { Attacker(id: 1, hp: 50, hit: 10) } };
        state.Enemies[0].TeammateHits = new() { new() { 10 } };
        state.Enemies[0].Intent = new List<IntentHit> { new(Base: 10) };
        var covered = new SimPlayer { Hp = 50, HandCount = 0 };
        covered.Powers = covered.Powers.With(PowerId.Covered, amount: 1, data: Models.CoveredPower.CovererBit(Who.You));
        state.Teammates.Add(covered);
        state.You.Powers = state.You.Powers.With(PowerId.Intercept, amount: 1);
        // Read from its code (decision #270): the creatures it covers are its own data's count.
        SimEngine.SetField(state, Who.You, Models.CoveredPower.CoveredCount, 1);
        var play = Play(Card("Intercept", SimCardType.Skill), Who.Teammate(teammateIndex: 0), plays: 1,
            new ApplyPowerAction(PowerId.Covered, Amount: 1) { To = ApplyPowerAction.Where.Creature, Creature = Who.Teammate(teammateIndex: 0) });
        var outcome = SimEngine.Run(state, play);
        Assert.Equal(20, outcome.TakenMax);
        Assert.Equal(1, InterceptAfterThePlay(state, play));
    }
}
