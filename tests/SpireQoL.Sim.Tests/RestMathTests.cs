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
        var restOutcome = RestMath.Compute(30, 77, 77 * 0.3m, 0, None, false, NoBoss, rest: true);
        Assert.Equal(new[] { new RestLine(RestStep.Rest, 23) }, restOutcome.Lines);
        Assert.Equal(53, restOutcome.ResultHp);
        Assert.Equal(77, restOutcome.ResultMax);
        Assert.Equal(0, restOutcome.Over);
        Assert.Equal((53, 77), (restOutcome.RestedHp, restOutcome.RestedMax));
    }

    [Fact]
    public void Heal_past_max_is_capped_and_counted_over()
    {
        var restOutcome = RestMath.Compute(70, 77, 23.1m, 0, None, false, NoBoss, rest: true);
        Assert.Equal(new[] { new RestLine(RestStep.Rest, 7) }, restOutcome.Lines);
        Assert.Equal(77, restOutcome.ResultHp);
        Assert.Equal(16, restOutcome.Over);
    }

    [Fact]
    public void Regal_pillow_is_its_own_line_and_takes_the_cap_last()
    {
        var open = RestMath.Compute(30, 77, 38.1m, 15, None, false, NoBoss, rest: true);
        Assert.Equal(new[] { new RestLine(RestStep.Rest, 23), new RestLine(RestStep.RegalPillow, 15) }, open.Lines);
        Assert.Equal(68, open.ResultHp);

        var capped = RestMath.Compute(50, 77, 38.1m, 15, None, false, NoBoss, rest: true);
        Assert.Equal(new[] { new RestLine(RestStep.Rest, 23), new RestLine(RestStep.RegalPillow, 4) }, capped.Lines);
        Assert.Equal(77, capped.ResultHp);
        Assert.Equal(11, capped.Over);
    }

    [Fact]
    public void Stone_humidifier_raises_max_and_heals_what_it_adds()
    {
        var restOutcome = RestMath.Compute(70, 77, 23.1m, 0, new[] { 5 }, false, NoBoss, rest: true);
        Assert.Equal(new[] { new RestLine(RestStep.Rest, 7), new RestLine(RestStep.StoneHumidifier, 5) }, restOutcome.Lines);
        Assert.Equal((82, 82), (restOutcome.ResultHp, restOutcome.ResultMax));
        Assert.Equal(16, restOutcome.Over);
    }

    [Fact]
    public void Night_terrors_heals_to_full_then_takes_five_max()
    {
        // ModifyRestSiteHealAmount returns max HP (Regal Pillow's part doesn't count), then LoseMaxHp 5.
        var restOutcome = RestMath.Compute(30, 77, 77, 15, None, true, NoBoss, rest: true);
        Assert.Equal(new[] { new RestLine(RestStep.Rest, 47), new RestLine(RestStep.NightTerrors, -5) }, restOutcome.Lines);
        Assert.Equal((72, 72), (restOutcome.ResultHp, restOutcome.ResultMax));
        Assert.Equal(0, restOutcome.Over);
    }

    [Fact]
    public void Night_terrors_leaves_at_least_one_max_hp()
    {
        var restOutcome = RestMath.Compute(1, 3, 3, 0, None, true, NoBoss, rest: true);
        Assert.Equal((1, 1), (restOutcome.ResultHp, restOutcome.ResultMax));
    }

    [Fact]
    public void Pantograph_adds_a_line_and_what_it_would_heal_anyway_is_over()
    {
        var boss = new[] { (RestStep.Pantograph, 25) };
        var restOutcome = RestMath.Compute(40, 77, 23.1m, 0, None, false, boss, rest: true);
        Assert.Equal(new[] { new RestLine(RestStep.Rest, 23), new RestLine(RestStep.Pantograph, 14) }, restOutcome.Lines);
        Assert.Equal(77, restOutcome.ResultHp);
        Assert.Equal((63, 77), (restOutcome.RestedHp, restOutcome.RestedMax));
        // Without resting: 40 + 25 = 65; resting adds 12 of its 23.
        Assert.Equal(11, restOutcome.Over);
    }

    [Fact]
    public void Bath_robes_heals_to_full_so_the_whole_rest_is_over()
    {
        var boss = new[] { (RestStep.BathRobes, 0) };
        var restOutcome = RestMath.Compute(40, 77, 23.1m, 0, None, false, boss, rest: true);
        Assert.Equal(new[] { new RestLine(RestStep.Rest, 23), new RestLine(RestStep.BathRobes, 14) }, restOutcome.Lines);
        Assert.Equal(77, restOutcome.ResultHp);
        Assert.Equal(23, restOutcome.Over);
    }

    [Fact]
    public void Without_rest_only_the_boss_relics_show()
    {
        var boss = new[] { (RestStep.Pantograph, 25) };
        var restOutcome = RestMath.Compute(40, 77, 23.1m, 0, None, false, boss, rest: false);
        Assert.Equal(new[] { new RestLine(RestStep.Pantograph, 25) }, restOutcome.Lines);
        Assert.Equal(65, restOutcome.ResultHp);
        Assert.Equal(0, restOutcome.Over);
        Assert.Equal((40, 77), (restOutcome.RestedHp, restOutcome.RestedMax));
    }

    [Fact]
    public void Lines_always_sum_to_the_result()
    {
        var boss = new[] { (RestStep.Pantograph, 25), (RestStep.BathRobes, 0) };
        foreach (var hp in new[] { 1, 20, 50, 76 })
        foreach (var nt in new[] { false, true })
        {
            var restOutcome = RestMath.Compute(hp, 77, nt ? 77 : 38.1m, 15, new[] { 5 }, nt, boss, rest: true);
            Assert.Equal(restOutcome.ResultHp, restOutcome.Hp + restOutcome.Lines.Sum(restLine => restLine.Hp));
        }
    }
}
