using System.Collections;
using System.Reflection;
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
        nameof(SimPlayer.Draw),            // their draw pile: never filled for a teammate (decision #90), cleared all the same
        nameof(SimPlayer.MadeCards),       // what goes into their draw pile: only with it (never filled for a teammate), cleared all the same
        nameof(SimPlayer.TurnEndCards),    // Burn, Decay, Bad Luck, Regret...
        nameof(SimPlayer.ExhaustingInHand), // Drum of Battle: which of their cards a play could exhaust
        // End-of-turn auto-plays picked from the hand (Stampede, Simpleton; never filled for a teammate today, decision #80).
        nameof(SimPlayer.Stampede), nameof(SimPlayer.Simpleton), nameof(SimPlayer.AutoHand),
    };

    /// <summary>On screen (their powers, relics, orbs, block, HP, hand size, cards they played) or the simulation's own bookkeeping.</summary>
    private static readonly HashSet<string> Known = new()
    {
        nameof(SimPlayer.Block), nameof(SimPlayer.Reflect),
        nameof(SimPlayer.HexFrom),         // who cast Hex on them (its power is on screen)
        nameof(SimPlayer.Counters),        // their counter powers' copies: each on screen with its count
        nameof(SimPlayer.Drawn), nameof(SimPlayer.StatusDrawn), // cards drawn: you watch them draw (the simulation's own count)
        nameof(SimPlayer.TakenToHand),     // cards taken from their piles into their hand in the simulation: its own count
        nameof(SimPlayer.HandCount),       // the size is on screen (Cloak Clasp, Screaming Flagon, Obsidian Vice, Regret's amount)
        nameof(SimPlayer.PlayingFromHand), // their cards being played: on screen as they play (the simulation's own count)
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
        nameof(SimPlayer.OstyHp), nameof(SimPlayer.OstyShields), nameof(SimPlayer.WuppyHp), nameof(SimPlayer.Hp), nameof(SimPlayer.MaxHp),
        nameof(SimPlayer.Fairies), nameof(SimPlayer.FairyHeal), nameof(SimPlayer.LizardTailHeal), // their potions and relics are on screen
        nameof(SimPlayer.Taken), nameof(SimPlayer.Through), nameof(SimPlayer.OstyTaken), nameof(SimPlayer.Incoming),
        nameof(SimPlayer.UnblockableTaken), nameof(SimPlayer.WuppyTaken), nameof(SimPlayer.Died), nameof(SimPlayer.LizardTailUsed), nameof(SimPlayer.FairiesUsed), // results
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
        var unreviewed = Fields().Select(field => field.Name).Where(name => !FromTheirHand.Contains(name) && !Known.Contains(name)).ToList();
        Assert.True(unreviewed.Count == 0,
            $"New SimPlayer field(s) {string.Join(", ", unreviewed)}: add each to FromTheirHand (and clear it in " +
            "SimState.WithoutTeammateHands) or to Known, with why.");
        Assert.Empty(FromTheirHand.Intersect(Known));
        var stale = FromTheirHand.Concat(Known).Except(Fields().Select(field => field.Name)).ToList();
        Assert.True(stale.Count == 0, $"No longer on SimPlayer: {string.Join(", ", stale)}");
    }

    [Fact]
    public void WhatsShownClearsEveryFieldFromTheirHandAndKeepsTheRest()
    {
        var mate = new SimPlayer();
        foreach (var field in Fields().Where(field => field.Name != "_orbsShared")) Fill(field, mate);
        var state = new SimState();
        state.Teammates.Add(mate);

        var shown = state.WithoutTeammateHands().Teammates[0];
        var fresh = new SimPlayer();
        foreach (var field in Fields().Where(field => field.Name != "_orbsShared"))
        {
            if (FromTheirHand.Contains(field.Name))
                Assert.True(Describe(field.GetValue(shown)) == Describe(field.GetValue(fresh)), $"{field.Name} isn't cleared in what's shown");
            else
                Assert.True(Describe(field.GetValue(shown)) == Describe(field.GetValue(mate)), $"{field.Name} changed in what's shown");
        }
        Assert.NotEqual(Describe(new SimPlayer().Fading), Describe(mate.Fading)); // the original is untouched
    }

    /// <summary>Sets a field to a value unlike its default (lists get one entry).</summary>
    private static void Fill(FieldInfo field, SimPlayer player)
    {
        var type = field.FieldType;
        if (type == typeof(int)) field.SetValue(player, 7);
        else if (type == typeof(bool)) field.SetValue(player, true);
        else if (type == typeof(List<(string, int)>)) field.SetValue(player, new List<(string, int)> { ("X", 3) });
        else if (type == typeof(List<TurnEndCard>)) field.SetValue(player, new List<TurnEndCard> { new("Burn", "Burn", Amount: 2) });
        else if (type == typeof(List<CounterCopy>)) field.SetValue(player, new List<CounterCopy> { new(PowerId.Orbit, Left: 2, Amount: 1) });
        else if (type == typeof(List<ExhaustingCard>)) field.SetValue(player, new List<ExhaustingCard> { new(HandCard: 7, Energy: 2, SimCardType.Skill) });
        else if (type == typeof(List<SimHandCard>)) field.SetValue(player, new List<SimHandCard> { new() { Id = 3, Name = "Strike", IsAttack = true } });
        else if (type == typeof(List<SimPlay>)) field.SetValue(player, new List<SimPlay> { new() { Name = "HowlFromBeyond" } });
        else if (type == typeof(PowerSet)) field.SetValue(player, PowerSet.Empty.With(PowerId.Strength, amount: 2));
        else if (type == typeof(RelicSet)) field.SetValue(player, RelicSet.Empty.With(RelicId.PenNib, amount: 2));
        else if (type == typeof(NamedCounts)) field.SetValue(player, NamedCounts.Empty.Add(SimKeys.Plays(PlayStage.Started), delta: 2));
        else if (type == typeof(List<SimOrb>)) field.SetValue(player, new List<SimOrb> { new() { Kind = OrbKind.Frost, Passive = 2, Evoke = 5 } });
        else if (type == typeof(SimDrawPile)) field.SetValue(player, new SimDrawPile { Rest = { new SimDrawGroup(new SimDrawCard("burn"), Count: 2) } });
        else if (type == typeof(IReadOnlyDictionary<string, SimDrawCard>)) field.SetValue(player, new Dictionary<string, SimDrawCard> { ["Dazed"] = new("dazed") });
        else Assert.Fail($"SimPlayer.{field.Name}: type {type.Name} isn't handled by this test's Fill: add it");
    }

    private static string Describe(object? v) => v switch
    {
        null => "null",
        IEnumerable e and not string => $"[{e.Cast<object>().Count()}]",
        _ => v.ToString() ?? "",
    };
}
