using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Tabs judged on how the turn ends (SimModel.OutcomeTab): each source's rule against ending the turn now, on its own model. The
/// feature, the settings check and the play check all find these sources through the models (JudgedOnOutcome).
/// </summary>
public class OutcomeTabTests
{
    private static SimModel Model(string name) => SimModels.Get(name) ?? throw new InvalidOperationException(name + " isn't modelled");

    private static SimState You(int hp, RelicId? relic = null, int amount = 0, int data = 0) =>
        new() { You = { Hp = hp, Relics = relic is { } otherRelic ? RelicSet.Empty.With(otherRelic, amount, data) : RelicSet.Empty } };

    private static SimOutcome Ended(params SimState[] branches) => new() { Branches = branches.ToList() };

    [Fact]
    public void EverySourceJudgedOnTheTurnsOutcomeHasItsRuleOnItsModel()
    {
        var expected = new[]
        {
            "AsleepPower", "BurrowedPower", "CompostPower", "CrabRagePower", "DemonTongue", "EmotionChip", "ExplosivePower", "FlutterPower",
            "GremlinHorn", "HandDrill", "ImbalancedPower", "InfestedPower", "ParryingShield", "PlowPower", "RavenousPower", "RedSkull",
            "RupturePower", "SelfFormingClay", "ShriekPower", "SlipperyPower", "SlumberPower", "SteamEruptionPower", "StockPower",
            "SurprisePower", "TheGambitPower",
        };
        Assert.Equal(expected, SimModels.Names.Where(name => Model(name).JudgedOnOutcome).Order());
    }

    [Fact]
    public void RedSkullShowsWhenThePlayTakesYouToItsThresholdFromAbove()
    {
        // At 30 HP with the threshold at 20: losing 10 or more in every branch of an aim gives the Strength.
        var rule = Model("RedSkull").OutcomeTab(You(hp: 30, RelicId.RedSkull, amount: 20), Ended(), Who.You)!;
        Assert.False(rule.Harmful);
        Assert.True(rule.SetOff(new SimOutcome { PlayHpLostMin = 10 }));
        Assert.False(rule.SetOff(new SimOutcome { PlayHpLostMin = 9 }));
        // Already at the threshold: no tab (nothing changes), and never a teammate's.
        Assert.Null(Model("RedSkull").OutcomeTab(You(hp: 20, RelicId.RedSkull, amount: 20), Ended(), Who.You));
        Assert.Null(Model("RedSkull").OutcomeTab(You(hp: 30, RelicId.RedSkull, amount: 20), Ended(), Who.Teammate(teammateIndex: 0)));
    }

    [Fact]
    public void DemonTongueShowsOnlyWhileUnusedThisTurn()
    {
        Assert.NotNull(Model("DemonTongue").OutcomeTab(You(hp: 30, RelicId.DemonTongue), Ended(), Who.You));
        Assert.Null(Model("DemonTongue").OutcomeTab(You(hp: 30, RelicId.DemonTongue, data: 2), Ended(), Who.You));
    }

    [Fact]
    public void ImbalancedShowsOnlyWhenEndingTheTurnNowWouldntBlockItsFirstHit()
    {
        var blocked = new SimState { Enemies = { new SimEnemy { Id = 0, Hp = 10, FirstHitFullyBlocked = true } } };
        var hit = new SimState { Enemies = { new SimEnemy { Id = 0, Hp = 10 } } };
        Assert.Null(Model("ImbalancedPower").OutcomeTab(hit, Ended(blocked), owner: 0));
        var rule = Model("ImbalancedPower").OutcomeTab(hit, Ended(hit), owner: 0)!;
        Assert.True(rule.SetOff(Ended(blocked)));
        Assert.False(rule.SetOff(Ended(blocked, hit))); // one branch not blocked: no guarantee
    }

    [Fact]
    public void ADeathThatSetsSomethingOffWarnsOnAPlayThatCanKill()
    {
        var rule = Model("ExplosivePower").OutcomeTab(new SimState(), Ended(), owner: 1)!;
        Assert.True(rule.Harmful);
        Assert.True(rule.SetOff(new SimOutcome { KilledByPlayInAny = { 1 } }));
        Assert.False(rule.SetOff(new SimOutcome { KilledByPlayInAny = { 0 } }));
        // Stock: only while stock is left.
        var stocked = new SimState { Enemies = { new SimEnemy { Id = 1, Hp = 5, Powers = PowerSet.Empty.With(PowerId.Stock, amount: 2) } } };
        Assert.NotNull(Model("StockPower").OutcomeTab(stocked, Ended(), owner: 1));
        Assert.Null(Model("StockPower").OutcomeTab(new SimState { Enemies = { new SimEnemy { Id = 1, Hp = 5 } } }, Ended(), owner: 1));
    }

    [Fact]
    public void YourRelicsRulesAreYoursAndCompostIsAnyPlayers()
    {
        Assert.Null(Model("HandDrill").OutcomeTab(new SimState(), Ended(), Who.Teammate(teammateIndex: 0)));
        Assert.NotNull(Model("HandDrill").OutcomeTab(new SimState(), Ended(), Who.You));
        Assert.NotNull(Model("CompostPower").OutcomeTab(new SimState(), Ended(), Who.Teammate(teammateIndex: 0)));
        Assert.Null(Model("CompostPower").OutcomeTab(new SimState(), Ended(), owner: 0)); // never an enemy's
    }
}
