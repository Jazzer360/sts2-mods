using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Conqueror read from its code (decision #247): ConquerorPower.ModifyDamageMultiplicative is 2 on its owner for a Sovereign Blade's powered
/// attack (`cardSource is SovereignBlade`). A read damage term that changes this play's damage acts on it (the game tells it so:
/// Hook.AfterModifyingDamageAmount on the modifiers), so its tab shows where the Blade hits an enemy with Conqueror, with the aim marker when
/// some target has none.
/// </summary>
public class ConquerorTabTests
{
    private static SimState Enemies(params bool[] conquered)
    {
        var state = new SimState();
        for (var i = 0; i < conquered.Length; i++)
        {
            var enemy = new SimEnemy { Id = i + 1, Name = $"E{i + 1}", Hp = 80, Hits = { 3 } };
            if (conquered[i]) enemy.Powers = enemy.Powers.With(PowerId.Conqueror, amount: 1);
            state.Enemies.Add(enemy);
        }
        return state;
    }

    private static SimPlay Blade(string name, SimPlay.Target mode = SimPlay.Target.Single) => new()
    {
        Name = name,
        IsAttack = true,
        TargetId = 1,
        Card = new SimCardPlay(new SimCard(name, SimCardType.Attack), Who.You, Who.None, PlayIndex: 0, PlayCount: 1, AutoPlay: false),
        Body = { ScriptBuilder.Attack(baseDamage: 10, mode: mode) },
    };

    private static SourceTab? Conqueror(SimState state, SimPlay play, bool aimed = true) =>
        BonusTabRowTests.TabsOf(state, play, aimed).Tabs.GetValueOrDefault("proc:ConquerorPower");

    [Fact]
    public void ItsTabShowsWhereTheBladeHitsAnEnemyWithConquerorWithTheAimMarker()
    {
        Assert.Equal(new SourceTab("proc:ConquerorPower", Crossed: false, Aimed: true), Conqueror(Enemies(true, false), Blade(CardId.SovereignBlade.Name)));
        // Every enemy has it: every target gives it, no marker.
        Assert.Equal(new SourceTab("proc:ConquerorPower", Crossed: false), Conqueror(Enemies(true, true), Blade(CardId.SovereignBlade.Name)));
        // One enemy: no marker either.
        Assert.Equal(new SourceTab("proc:ConquerorPower", Crossed: false), Conqueror(Enemies(true), Blade(CardId.SovereignBlade.Name)));
    }

    [Fact]
    public void ItsTabIsOnlyOnTheBladesHitsOnAnEnemyWithIt()
    {
        // Another attack: its code returns 1, nothing changes.
        Assert.Null(Conqueror(Enemies(true, false), Blade("Strike")));
        // The Blade at every enemy (Seeking Edge's): it hits the Conqueror one whatever you aim at.
        Assert.Equal(new SourceTab("proc:ConquerorPower", Crossed: false), Conqueror(Enemies(true, false), Blade(CardId.SovereignBlade.Name, SimPlay.Target.All), aimed: false));
        // No enemy with it: no tab.
        Assert.Null(Conqueror(Enemies(false, false), Blade(CardId.SovereignBlade.Name)));
    }

    [Fact]
    public void OnGeneratedBoardsItsTabIsWhereItsCodeDoublesTheHit()
    {
        // From its code alone: a tab where some target's play has the Blade hit an enemy with Conqueror (its term 2), with the marker where some
        // target's play doesn't; plain (it helps you).
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var scenario = 0; scenario < 150; scenario++)
        {
            var random = new Random(9700 + scenario);
            var conquered = Enumerable.Range(0, random.Next(1, 4)).Select(_ => random.Next(2) == 0).ToArray();
            var blade = random.Next(4) != 0;
            var all = random.Next(3) == 0;
            var state = Enemies(conquered);
            var tab = Conqueror(state, Blade(blade ? CardId.SovereignBlade.Name : "Strike", all ? SimPlay.Target.All : SimPlay.Target.Single), aimed: !all);
            var hitsOne = blade && conquered.Any(has => has);
            var everyAim = blade && (all ? conquered.Any(has => has) : conquered.All(has => has));
            SourceTab? expected = hitsOne ? new SourceTab("proc:ConquerorPower", Crossed: false, Aimed: !everyAim) : null;
            Assert.True(expected == tab, $"scenario {scenario}: expected {expected}, shown {tab}");
            seen.Add(tab == null ? "none" : tab.Aimed ? "aimed" : "every");
        }
        Assert.Equal(3, seen.Count);
    }
}
