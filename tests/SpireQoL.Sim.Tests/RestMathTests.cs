using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Rest Preview's sum (RestMath, decompile v0.113.1): HealRestSiteOption / MendRestSiteOption heal 30% of max HP through
/// ModifyRestSiteHealAmount, truncated and capped at max; AfterRestSiteHeal (Stone Humidifier, Night Terrors); at the boss
/// fight's start Pantograph and an unused Bath Robes.
/// </summary>
public class RestMathTests
{
    private static readonly int[] None = Array.Empty<int>();
    private static readonly (RestStep, int)[] NoBoss = Array.Empty<(RestStep, int)>();

    [Fact]
    public void Rest_heals_the_whole_part_of_thirty_percent()
    {
        var restOutcome = RestMath.Compute(currentHp: 30, max: 77, 77 * 0.3m, pillow: 0, None, nightTerrors: false, NoBoss, rest: true);
        Assert.Equal(new[] { new RestLine(RestStep.Rest, Hp: 23) }, restOutcome.Lines);
        Assert.Equal(53, restOutcome.ResultHp);
        Assert.Equal(77, restOutcome.ResultMax);
        Assert.Equal(0, restOutcome.Over);
        Assert.Equal((53, 77), (restOutcome.RestedHp, restOutcome.RestedMax));
    }

    [Fact]
    public void Heal_past_max_is_capped_and_counted_over()
    {
        var restOutcome = RestMath.Compute(currentHp: 70, max: 77, heal: 23.1m, pillow: 0, None, nightTerrors: false, NoBoss, rest: true);
        Assert.Equal(new[] { new RestLine(RestStep.Rest, Hp: 7) }, restOutcome.Lines);
        Assert.Equal(77, restOutcome.ResultHp);
        Assert.Equal(16, restOutcome.Over);
    }

    [Fact]
    public void Regal_pillow_is_its_own_line_and_takes_the_cap_last()
    {
        var open = RestMath.Compute(currentHp: 30, max: 77, heal: 38.1m, pillow: 15, None, nightTerrors: false, NoBoss, rest: true);
        Assert.Equal(new[] { new RestLine(RestStep.Rest, Hp: 23), new RestLine(RestStep.RegalPillow, Hp: 15) }, open.Lines);
        Assert.Equal(68, open.ResultHp);

        var capped = RestMath.Compute(currentHp: 50, max: 77, heal: 38.1m, pillow: 15, None, nightTerrors: false, NoBoss, rest: true);
        Assert.Equal(new[] { new RestLine(RestStep.Rest, Hp: 23), new RestLine(RestStep.RegalPillow, Hp: 4) }, capped.Lines);
        Assert.Equal(77, capped.ResultHp);
        Assert.Equal(11, capped.Over);
    }

    [Fact]
    public void Stone_humidifier_raises_max_and_heals_what_it_adds()
    {
        var restOutcome = RestMath.Compute(currentHp: 70, max: 77, heal: 23.1m, pillow: 0, new[] { 5 }, nightTerrors: false, NoBoss, rest: true);
        Assert.Equal(new[] { new RestLine(RestStep.Rest, Hp: 7), new RestLine(RestStep.StoneHumidifier, Hp: 5) }, restOutcome.Lines);
        Assert.Equal((82, 82), (restOutcome.ResultHp, restOutcome.ResultMax));
        Assert.Equal(16, restOutcome.Over);
    }

    [Fact]
    public void Night_terrors_heals_to_full_then_takes_five_max()
    {
        // ModifyRestSiteHealAmount returns max HP (Regal Pillow's part doesn't count), then LoseMaxHp 5.
        var restOutcome = RestMath.Compute(currentHp: 30, max: 77, heal: 77, pillow: 15, None, nightTerrors: true, NoBoss, rest: true);
        Assert.Equal(new[] { new RestLine(RestStep.Rest, Hp: 47), new RestLine(RestStep.NightTerrors, Hp: -5) }, restOutcome.Lines);
        Assert.Equal((72, 72), (restOutcome.ResultHp, restOutcome.ResultMax));
        Assert.Equal(0, restOutcome.Over);
    }

    [Fact]
    public void Night_terrors_leaves_at_least_one_max_hp()
    {
        var restOutcome = RestMath.Compute(currentHp: 1, max: 3, heal: 3, pillow: 0, None, nightTerrors: true, NoBoss, rest: true);
        Assert.Equal((1, 1), (restOutcome.ResultHp, restOutcome.ResultMax));
    }

    [Fact]
    public void Pantograph_adds_a_line_and_what_it_would_heal_anyway_is_over()
    {
        var boss = new[] { (RestStep.Pantograph, 25) };
        var restOutcome = RestMath.Compute(currentHp: 40, max: 77, heal: 23.1m, pillow: 0, None, nightTerrors: false, boss, rest: true);
        Assert.Equal(new[] { new RestLine(RestStep.Rest, Hp: 23), new RestLine(RestStep.Pantograph, Hp: 14) }, restOutcome.Lines);
        Assert.Equal(77, restOutcome.ResultHp);
        Assert.Equal((63, 77), (restOutcome.RestedHp, restOutcome.RestedMax));
        // Without resting: 40 + 25 = 65; resting adds 12 of its 23.
        Assert.Equal(11, restOutcome.Over);
    }

    [Fact]
    public void Bath_robes_heals_to_full_so_the_whole_rest_is_over()
    {
        var boss = new[] { (RestStep.BathRobes, 0) };
        var restOutcome = RestMath.Compute(currentHp: 40, max: 77, heal: 23.1m, pillow: 0, None, nightTerrors: false, boss, rest: true);
        Assert.Equal(new[] { new RestLine(RestStep.Rest, Hp: 23), new RestLine(RestStep.BathRobes, Hp: 14) }, restOutcome.Lines);
        Assert.Equal(77, restOutcome.ResultHp);
        Assert.Equal(23, restOutcome.Over);
    }

    [Fact]
    public void Without_rest_only_the_boss_relics_show()
    {
        var boss = new[] { (RestStep.Pantograph, 25) };
        var restOutcome = RestMath.Compute(currentHp: 40, max: 77, heal: 23.1m, pillow: 0, None, nightTerrors: false, boss, rest: false);
        Assert.Equal(new[] { new RestLine(RestStep.Pantograph, Hp: 25) }, restOutcome.Lines);
        Assert.Equal(65, restOutcome.ResultHp);
        Assert.Equal(0, restOutcome.Over);
        Assert.Equal((40, 77), (restOutcome.RestedHp, restOutcome.RestedMax));
    }

    [Fact]
    public void Lines_always_sum_to_the_result()
    {
        var boss = new[] { (RestStep.Pantograph, 25), (RestStep.BathRobes, 0) };
        foreach (var hp in new[] { 1, 20, 50, 76 })
        foreach (var nightTerrors in new[] { false, true })
        {
            var restOutcome = RestMath.Compute(hp, max: 77, nightTerrors ? 77 : 38.1m, pillow: 15, new[] { 5 }, nightTerrors, boss, rest: true);
            Assert.Equal(restOutcome.ResultHp, restOutcome.Hp + restOutcome.Lines.Sum(restLine => restLine.Hp));
        }
    }
}
