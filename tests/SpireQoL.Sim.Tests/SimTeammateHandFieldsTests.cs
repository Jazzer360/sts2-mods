using System.Collections;
using System.Reflection;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// What's shown leaves out the cards in teammates' hands (decisions #25, #38: SimState.WithoutTeammateHands).
/// Every SimPlayer field is reviewed here as one or the other: read from the cards in their hand (cleared in what's
/// shown), or something you can see (kept). A new field fails until it's sorted, so a hand-dependent effect can't
/// reach what's shown by being forgotten (as their Ethereal cards nearly did).
/// </summary>
public class SimTeammateHandFieldsTests
{
    /// <summary>From the cards in their hand: you see how many they hold, not which.</summary>
    private static readonly HashSet<string> FromTheirHand = new()
    {
        nameof(SimPlayer.Fading),          // Ethereal cards that exhaust (Feel No Pain, Charon's Ashes, Forgotten Soul)
        nameof(SimPlayer.HexFading),       // of those, Ethereal only through Hex
        nameof(SimPlayer.TurnEndCards),    // Burn, Decay, Bad Luck, Regret...
        // End-of-turn auto-plays picked from the hand (Stampede, Simpleton; never filled for a teammate today, decision #80).
        nameof(SimPlayer.Stampede), nameof(SimPlayer.Simpleton), nameof(SimPlayer.AutoHand),
    };

    /// <summary>On screen (their powers, relics, orbs, block, HP, hand size, cards they played) or the simulation's own bookkeeping.</summary>
    private static readonly HashSet<string> Known = new()
    {
        nameof(SimPlayer.Block), nameof(SimPlayer.Reflect),
        nameof(SimPlayer.HexFrom),         // who cast Hex on them (its power is on screen)
        nameof(SimPlayer.HandCount),       // the size is on screen (Cloak Clasp, Screaming Flagon, Obsidian Vice, Regret's amount)
        nameof(SimPlayer.Energy),          // on screen for every player (Pael's Tears, Orbit)
        nameof(SimPlayer.Arrows),          // on screen (the Fool's arrows)
        nameof(SimPlayer.AttacksThisTurn), // you watch them play
        // Auto-plays from their piles: Howl from Beyond in their exhaust pile (you can view it), I Am Invincible when every card
        // in their draw pile is one (its count and contents are viewable; the order doesn't matter then).
        nameof(SimPlayer.ExhaustAutoPlays), nameof(SimPlayer.InvincibleBlock), nameof(SimPlayer.InvincibleDrawPile),
        nameof(SimPlayer.Orbs), nameof(SimPlayer.OrbCapacity), nameof(SimPlayer.SlotOnFirstChannel), nameof(SimPlayer.Thunder),
        nameof(SimPlayer.FrontOrbBonusTriggers),
        nameof(SimPlayer.BlockGains), 
        nameof(SimPlayer.Focus), 
        nameof(SimPlayer.OstyHp), nameof(SimPlayer.OstyShields), nameof(SimPlayer.WuppyHp), nameof(SimPlayer.Hp),
        nameof(SimPlayer.Fairies), nameof(SimPlayer.FairyHeal), nameof(SimPlayer.LizardTailHeal), // their potions and relics are on screen
        nameof(SimPlayer.Taken), nameof(SimPlayer.Through), nameof(SimPlayer.OstyTaken), nameof(SimPlayer.Incoming),
        nameof(SimPlayer.BlockGained), nameof(SimPlayer.BeaconOfHope), nameof(SimPlayer.BeaconGiving),
        nameof(SimPlayer.Powers), nameof(SimPlayer.Relics), // their powers and relics are on screen
        nameof(SimPlayer.Turn), // the cards they played this turn: you watch them play
        "_orbsShared",
    };

    private static FieldInfo[] Fields() =>
        typeof(SimPlayer).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    [Fact]
    public void EverySimPlayerFieldIsReviewedAsFromTheirHandOrKnown()
    {
        var unreviewed = Fields().Select(f => f.Name).Where(n => !FromTheirHand.Contains(n) && !Known.Contains(n)).ToList();
        Assert.True(unreviewed.Count == 0,
            $"New SimPlayer field(s) {string.Join(", ", unreviewed)}: add each to FromTheirHand (and clear it in " +
            "SimState.WithoutTeammateHands) or to Known, with why.");
        Assert.Empty(FromTheirHand.Intersect(Known));
        var stale = FromTheirHand.Concat(Known).Except(Fields().Select(f => f.Name)).ToList();
        Assert.True(stale.Count == 0, $"No longer on SimPlayer: {string.Join(", ", stale)}");
    }

    [Fact]
    public void WhatsShownClearsEveryFieldFromTheirHandAndKeepsTheRest()
    {
        var mate = new SimPlayer();
        foreach (var f in Fields().Where(f => f.Name != "_orbsShared")) Fill(f, mate);
        var s = new SimState();
        s.Teammates.Add(mate);

        var shown = s.WithoutTeammateHands().Teammates[0];
        var fresh = new SimPlayer();
        foreach (var f in Fields().Where(f => f.Name != "_orbsShared"))
        {
            if (FromTheirHand.Contains(f.Name))
                Assert.True(Describe(f.GetValue(shown)) == Describe(f.GetValue(fresh)), $"{f.Name} isn't cleared in what's shown");
            else
                Assert.True(Describe(f.GetValue(shown)) == Describe(f.GetValue(mate)), $"{f.Name} changed in what's shown");
        }
        Assert.NotEqual(Describe(new SimPlayer().Fading), Describe(mate.Fading)); // the original is untouched
    }

    /// <summary>Sets a field to a value unlike its default (lists get one entry).</summary>
    private static void Fill(FieldInfo f, SimPlayer p)
    {
        var t = f.FieldType;
        if (t == typeof(int)) f.SetValue(p, 7);
        else if (t == typeof(bool)) f.SetValue(p, true);
        else if (t == typeof(List<(string, int)>)) f.SetValue(p, new List<(string, int)> { ("X", 3) });
        else if (t == typeof(List<TurnEndCard>)) f.SetValue(p, new List<TurnEndCard> { new("Burn", "Burn", 2) });
        else if (t == typeof(List<SimHandCard>)) f.SetValue(p, new List<SimHandCard> { new() { Id = 3, Name = "Strike", IsAttack = true } });
        else if (t == typeof(List<SimPlay>)) f.SetValue(p, new List<SimPlay> { new() { Name = "HowlFromBeyond" } });
        else if (t == typeof(PowerSet)) f.SetValue(p, PowerSet.Empty.With("StrengthPower", 2));
        else if (t == typeof(List<SimOrb>)) f.SetValue(p, new List<SimOrb> { new() { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 } });
        else Assert.Fail($"SimPlayer.{f.Name}: type {t.Name} isn't handled by this test's Fill: add it");
    }

    private static string Describe(object? v) => v switch
    {
        null => "null",
        IEnumerable e and not string => $"[{e.Cast<object>().Count()}]",
        _ => v.ToString() ?? "",
    };
}
