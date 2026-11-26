using System.Text;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using SpireQoL.Procs;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The free-card tags (`free:`, Procs/FreeCards: Free Attack, Free Skill and Free Power on a card in your hand, the energy it saves, `?` when you
/// couldn't know the cost, crossed when wasted) on boards built from the game's own objects (GameFight): each card's tags recorded with the
/// free powers' charge followed by hand (FreeCards.Applies) and their saving worked out from the game's own passes (FreeCards.CostWithoutFree),
/// in Snapshots/free-card-tab-parity.txt (SPIREQOL_RECORD_FREE_TABS=path writes it). Reading the powers whole and working the saving out through
/// the simulator's cost rule (SimEngine.HandCost) must leave every tag as it was.
/// </summary>
[Collection(GameFightCollection.Name)]
public class FreeCardTabParityTests
{
    private static string RecordingPath => Path.Combine(CardPlaySnapshotTests.SourceFolder(), "Snapshots", "free-card-tab-parity.txt");

    /// <summary>The powers on you for one board: the free powers and the other cost listeners, by class and amount.</summary>
    private static readonly (string Board, (Type Power, int Amount)[] Powers)[] Boards =
    {
        ("free attack", new[] { (typeof(FreeAttackPower), 1) }),
        ("free attack 2", new[] { (typeof(FreeAttackPower), 2) }),
        ("free skill", new[] { (typeof(FreeSkillPower), 1) }),
        ("free power", new[] { (typeof(FreePowerPower), 1) }),
        ("all three", new[] { (typeof(FreeAttackPower), 1), (typeof(FreeSkillPower), 1), (typeof(FreePowerPower), 1) }),
        ("free attack, borrowed time", new[] { (typeof(FreeAttackPower), 1), (typeof(BorrowedTimePower), 1) }),
        ("free power, curious", new[] { (typeof(FreePowerPower), 1), (typeof(CuriousPower), 1) }),
        ("free power, curious 3", new[] { (typeof(FreePowerPower), 1), (typeof(CuriousPower), 3) }),
        ("free skill, corruption", new[] { (typeof(FreeSkillPower), 1), (typeof(CorruptionPower), 1) }),
        ("free attack, veilpiercer", new[] { (typeof(FreeAttackPower), 1), (typeof(VeilpiercerPower), 1) }),
        ("none", Array.Empty<(Type, int)>()),
    };

    /// <summary>How each card's cost is changed before the board's powers land: none, this turn, for the rest of the fight.</summary>
    private enum CostChange { None, ThisTurn, ThisCombat }

    private static List<string> Record()
    {
        var lines = new List<string>();
        foreach (var (board, powers) in Boards)
            foreach (var change in Enum.GetValues<CostChange>())
                foreach (var seen in new[] { true, false })
                    lines.AddRange(Tags(board, powers, change, seen));
        return lines;
    }

    private static IEnumerable<string> Tags(string board, (Type Power, int Amount)[] powers, CostChange change, bool seen)
    {
        var gameFight = new GameFight();
        var hand = new List<CardModel>
        {
            gameFight.Add<Bash>(), gameFight.Add<Anger>(), gameFight.Add<Whirlwind>(), gameFight.Add<ShrugItOff>(), gameFight.Add<DefendIronclad>(),
            gameFight.Add<Inflame>(), gameFight.Add<Corruption>(), gameFight.Add<Bash>(upgraded: true),
        };
        gameFight.Add<Bash>(PileType.Draw);
        foreach (var card in hand)
        {
            if (card.EnergyCost.CostsX) continue;
            switch (change)
            {
                case CostChange.ThisTurn: card.EnergyCost.SetThisTurn(cost: 1); break;
                case CostChange.ThisCombat: card.EnergyCost.SetThisCombat(cost: 1); break;
            }
        }
        // What you saw in your hand before the free powers came (FreeCards.Observe, every frame), or nothing seen.
        FreeCards.Observe(gameFight.Me, seen ? hand : new List<CardModel>());
        foreach (var (power, amount) in powers)
        {
            var mutable = (PowerModel)ModelDb.AllPowers.First(canon => canon.GetType() == power).ToMutable();
            mutable.ApplyInternal(gameFight.Me.Creature, amount);
        }
        // The board's start state, as the card tags have it (TurnEndModel.ForecastStart.State): the saving through the simulator's cost rule (decision #381).
        var start = SpireQoL.Sim.SimAdapter.Build(gameFight.Me, gameFight.Combat, out _);
        Assert.Null(start.CostsUnknown);
        foreach (var card in hand)
        {
            var text = new StringBuilder($"{board} | {change} | {(seen ? "seen" : "unseen")} | {card.GetType().Name}{(card.IsUpgraded ? "+" : "")}:");
            var uses = FreeCards.Uses(card, gameFight.Me, start);
            // The game's own passes (no start state, or a pass the simulator doesn't run) give the same.
            Assert.Equal(FreeCards.Uses(card, gameFight.Me).Select(use => (use.Power.GetType(), use.Saved, use.Hidden, use.Wasted)),
                uses.Select(use => (use.Power.GetType(), use.Saved, use.Hidden, use.Wasted)));
            if (uses.Count == 0) text.Append(" none");
            foreach (var use in uses)
                text.Append($" {FreeCards.Prefix}{use.Power.GetType().Name}={(use.Wasted ? "crossed" : use.Hidden ? "?" : use.Saved?.ToString() ?? "-")}");
            yield return text.ToString();
        }
        FreeCards.Observe(gameFight.Me, new List<CardModel>());
    }

    [Fact]
    public void TheFreeCardTagsAreAsRecorded()
    {
        GameModels.EnsureLoaded();
        var actual = Record();
        if (Environment.GetEnvironmentVariable("SPIREQOL_RECORD_FREE_TABS") is { Length: > 0 } recordTo)
        {
            File.WriteAllLines(recordTo, actual);
            return;
        }
        var expected = File.ReadAllLines(RecordingPath).ToList();
        var changed = actual.Zip(expected).Where(pair => pair.First != pair.Second).Take(12).Select(pair => $"now:    {pair.First}\nbefore: {pair.Second}");
        Assert.True(actual.SequenceEqual(expected), $"{actual.Count} tags now, {expected.Count} recorded:\n{string.Join("\n", changed)}");
    }
}
