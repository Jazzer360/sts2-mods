namespace SpireQoL.Sim;

/// <summary>A step of a rest-site heal, in the order the game runs it (one line of the Rest Preview sum).</summary>
public enum RestStep
{
    /// <summary>The Rest (or Mend) heal: 30% of max HP, or to full with Night Terrors.</summary>
    Rest,
    /// <summary>Regal Pillow's +15 to the rest heal (ModifyRestSiteHealAmount).</summary>
    RegalPillow,
    /// <summary>Stone Humidifier after the heal (AfterRestSiteHeal): +5 max HP, healing what it adds.</summary>
    StoneHumidifier,
    /// <summary>Night Terrors after the heal: −5 max HP (at least 1 left), HP capped.</summary>
    NightTerrors,
    /// <summary>Pantograph at the boss fight's start (BeforeCombatStart): heal 25.</summary>
    Pantograph,
    /// <summary>An unused Bath Robes at the boss fight's start: heal to full.</summary>
    BathRobes,
}

/// <summary>One line of the sum: a step and the HP it changes (after caps).</summary>
public readonly record struct RestLine(RestStep Step, int Hp);

/// <summary>
/// What a rest (or a Mend on a teammate) leaves a player with, as a sum: HP now, a line per step, and the result.
/// <see cref="Over"/> is the part of the rest heal that never reaches the result (over max HP, or healed anyway by
/// Pantograph or Bath Robes before the boss). <see cref="RestedHp"/>/<see cref="RestedMax"/> are right after resting,
/// before any boss relic (what the rest check compares).
/// </summary>
public sealed record RestOutcome(int Hp, int Max, IReadOnlyList<RestLine> Lines, int ResultHp, int ResultMax, int Over,
    int RestedHp, int RestedMax);

/// <summary>
/// The rest site's heal, from the game's rules (decompile v0.113.1), with no game types:
///  - HealRestSiteOption / MendRestSiteOption heal GetHealAmount (30% of max HP through ModifyRestSiteHealAmount); HP
///    becomes the whole part of HP + heal, capped at max (Creature.SetCurrentHpInternal).
///  - AfterRestSiteHeal, in listener order: Stone Humidifier GainMaxHp (max and HP up by the same), Night Terrors
///    LoseMaxHp 5 (at least 1 max left; HP capped). Mend runs it on the target with isMimicked false, so it's the same.
///  - At the boss fight's start (BeforeCombatStart): Pantograph heals 25, an unused Bath Robes heals to full.
/// </summary>
public static class RestMath
{
    /// <param name="heal">The game's heal amount (GetHealAmount), Regal Pillow included.</param>
    /// <param name="pillow">Regal Pillow's part of <paramref name="heal"/> (0 without it, or when Night Terrors sets the heal).</param>
    /// <param name="humidifiers">Stone Humidifier max HP gains, one per copy.</param>
    /// <param name="boss">Pantograph's heal or Bath Robes (amount ignored), in relic order; empty when not before the boss.</param>
    /// <param name="rest">Rest (or Mend) is taken; false gives only the boss relics' lines.</param>
    public static RestOutcome Compute(int hp, int max, decimal heal, decimal pillow, IReadOnlyList<int> humidifiers, bool nightTerrors,
        IReadOnlyList<(RestStep Step, int Amount)> boss, bool rest)
    {
        var lines = new List<RestLine>();
        int h = hp, m = max;
        var nominal = 0;
        if (rest)
        {
            // CreatureCmd.Heal: SetCurrentHpInternal((int)min(hp + heal, max)).
            var gained = (int)Math.Min(hp + heal, max) - hp;
            var pillowPart = nightTerrors ? 0 : (int)Math.Max(0, Math.Floor(pillow));
            var restGain = Math.Min(gained, Math.Max(0, (int)Math.Floor(heal) - pillowPart));
            lines.Add(new RestLine(RestStep.Rest, restGain));
            if (pillowPart > 0) lines.Add(new RestLine(RestStep.RegalPillow, gained - restGain));
            // Night Terrors heals to full: nothing of it is "wasted", the heal is whatever reaches max.
            nominal = nightTerrors ? gained : (int)Math.Floor(heal);
            h += gained;
            foreach (var g in humidifiers)
            {
                m += g;
                var before = h;
                h = Math.Min(h + g, m);
                lines.Add(new RestLine(RestStep.StoneHumidifier, h - before));
            }
            if (nightTerrors)
            {
                m -= Math.Min(5, m - 1);
                var before = h;
                h = Math.Min(h, m);
                lines.Add(new RestLine(RestStep.NightTerrors, h - before));
            }
        }
        int restedHp = h, restedMax = m;

        int Boss(ref int at, int top, List<RestLine>? into)
        {
            foreach (var (step, amount) in boss)
            {
                var before = at;
                at = step == RestStep.BathRobes ? top : Math.Min(top, at + amount);
                into?.Add(new RestLine(step, at - before));
            }
            return at;
        }
        var result = Boss(ref h, m, lines);
        var over = 0;
        if (rest)
        {
            // The rest heal's worth is what it adds to the result over not resting at all, apart from what the relics after
            // it change (Stone Humidifier's HP, Night Terrors' loss).
            var skip = hp;
            Boss(ref skip, max, null);
            var after = lines.Where(l => l.Step is RestStep.StoneHumidifier or RestStep.NightTerrors).Sum(l => l.Hp);
            over = Math.Max(0, nominal - (result - skip - after));
        }
        return new RestOutcome(hp, max, lines, result, m, over, restedHp, restedMax);
    }
}
