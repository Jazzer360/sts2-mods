using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The bonus tabs as each source's settings row shows them (decision #250): the simulator works out both directions of a payout from the
/// code (decision #239), the row picks which are tabs (TabShows, from SettingsCatalog through ReadModels/tab-rows.json). Each source's tab
/// here is the one a card's play shows (CardTabs, the rules the game shows), on boards built from the game's own code's conditions.
/// </summary>
public class BonusTabRowTests
{
    /// <summary>
    /// A card's tabs as CardPredictions reads them: one simulation per aim (each enemy for a card aimed at one, else one), against ending the
    /// turn now, every source on the board with what its row shows.
    /// </summary>
    internal static CardTabs TabsOf(SimState state, SimPlay play, bool aimed = false)
    {
        var aims = aimed ? state.Enemies.Select(enemy => enemy.Id).ToList() : new List<int> { play.TargetId };
        var outcomes = aims.Select(aim =>
        {
            play.TargetId = aim;
            return SimEngine.Run(state, play);
        }).ToArray();
        var baseline = SimEngine.Run(state);
        var sources = new List<TabSource>();
        foreach (var power in state.You.Powers.Items) sources.Add(Source(power.Name.Name, owner: null, pass: 2));
        foreach (var relic in state.You.Relics.Items) sources.Add(Source(relic.Name.Name, owner: null, pass: 1));
        foreach (var enemy in state.Enemies)
            foreach (var power in enemy.Powers.Items) sources.Add(Source(power.Name.Name, enemy.Id, pass: 0));
        var tabs = new CardTabs { Chances = true, ProcNumbers = true };
        tabs.Add(sources, state, baseline, outcomes);
        return tabs;
    }

    private static TabSource Source(string name, int? owner, int pass) => new(name, name, owner, pass, Shows: TabRowsFile.ShowsOf(name));

    private static SourceTab? Tab(CardTabs tabs, string sourceClass) => tabs.Tabs.GetValueOrDefault("proc:" + sourceClass);

    // ---- Parrying Shield (ParryingShield.AfterSideTurnEnd: 6 damage to a random enemy when your block is at least its Block): its row shows the
    // play that sets it up, never one that stops it (the owner's call).

    private static SimState Parrying(int block)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50, Hits = { 3 } } } };
        state.You.Block = block;
        state.WithRelic(Who.You, RelicId.ParryingShield, "Block", value: 10);
        state.WithRelic(Who.You, RelicId.ParryingShield, "Damage", value: 6);
        return state;
    }

    [Fact]
    public void ParryingShieldShowsThePlayThatSetsItUp()
    {
        var tabs = TabsOf(Parrying(block: 5), new SimPlay { Name = "Defend", BlockGains = 1, BlockAmount = 5 });
        Assert.Equal(new SourceTab("proc:ParryingShield", Crossed: false), Tab(tabs, "ParryingShield"));
    }

    [Fact]
    public void ParryingShieldShowsNoTabOnAPlayThatStopsIt()
    {
        // 12 block: it fires ending the turn now. An attack on a 5-Thorns enemy takes 5 of it (7 left): it doesn't, and its row shows no warning.
        var state = Parrying(block: 12);
        state.Enemies[0].Thorns = 5;
        var play = new SimPlay { Name = "Strike", IsAttack = true, HitTarget = SimPlay.Target.Single, Hits = 1, TargetId = 1 };
        play.HitDamage[1] = 6;
        Assert.False(SimEngine.Run(state, play).PaidInSome(RelicId.ParryingShield.Name, owner: null));
        Assert.Null(Tab(TabsOf(state, play), "ParryingShield"));
    }
}
