using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// A card play's shape, decided once before its first play as CardModel.OnPlayWrapper does: where the card goes after
/// (Hook.ModifyCardPlayResultLocation: Feral returns a 0-cost Attack to your hand) and how many times it plays (Hook.ModifyCardPlayCount:
/// Echo Form on the fight's log). A power's life as PowerCmd.Apply runs it: a new one runs AfterApplied (Feral counts the 0-cost Attacks
/// already started this turn), more of one only changes its amount.
/// </summary>
public class SimPlayShapeTests
{
    /// <summary>FeralPower.AfterApplied's question: 0-energy Attacks its owner started this turn.</summary>
    private static readonly HistoryQuery FreeAttacksStarted =
        new(HistoryKind.CardPlayStarted, HistoryScope.ThisTurn, HistoryWhose.Owner) { Details = HistoryDetail.ZeroEnergy, CardType = SimCardType.Attack };

    /// <summary>EchoFormPower.ModifyCardPlayCount's question: plays its owner started this turn, first of their series.</summary>
    private static readonly HistoryQuery FirstPlaysStarted =
        new(HistoryKind.CardPlayStarted, HistoryScope.ThisTurn, HistoryWhose.Owner) { Details = HistoryDetail.FirstInSeries };

    private static SimState Board(int hand = 5)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = 500 } }, History = new[] { FreeAttacksStarted, FirstPlaysStarted } };
        state.You.HandCount = hand;
        return state;
    }

    /// <summary>A card played from your hand (it leaves it as it resolves): an Attack for <paramref name="energy"/>, or the Feral card.</summary>
    private static SimPlay FromHand(string name, SimCardType type, int energy, int plays = 1, params SimAction[] body)
    {
        var play = new SimPlay { Name = name, TargetId = 0, HandCard = 1, HandDelta = -1, Plays = plays };
        play.Card = new SimCardPlay(new SimCard(name, type) { Energy = energy }, Who.You, Target: 0, PlayIndex: 0, PlayCount: plays, AutoPlay: false);
        play.Body.AddRange(body);
        return play;
    }

    private static SimPlay FreeAttack(int plays = 1) => FromHand("Claw", SimCardType.Attack, energy: 0, plays, new AttackAction(Hits: 1, SimPlay.Target.Single, new DamageSpec(Base: 3, SimProps.Move)));

    private static SimPlay FeralCard() => FromHand("Feral", SimCardType.Power, energy: 2, plays: 1, new ApplyPowerAction(PowerId.Feral, Amount: 1) { To = ApplyPowerAction.Where.You });

    private static SimState After(SimState start, params SimPlay[] plays)
    {
        var script = new SimScript { Name = "chain" };
        foreach (var play in plays) script.Actions.AddRange(SimScript.Of(play).Actions);
        SimEngine.RunScriptTo(start, script, out var after, out _);
        return Assert.Single(after);
    }

    [Fact]
    public void FeralReturnsAFreeAttackWhileItHasCharges()
    {
        // Feral 1, none returned yet: the 0-cost Attack goes back to your hand (5 cards still), and the next one doesn't (4).
        var state = Board();
        state.You.Powers = state.You.Powers.With(PowerId.Feral, amount: 1, data: 0);
        Assert.Equal(5, After(state, FreeAttack()).You.HandCount);
        Assert.Equal(4, After(state, FreeAttack(), FreeAttack()).You.HandCount);
    }

    [Fact]
    public void ANewFeralCountsTheFreeAttacksAlreadyStartedThisTurn()
    {
        // Play a 0-cost Attack, then Feral (a new power: AfterApplied counts the 0-cost Attacks started this turn, 1): the next 0-cost
        // Attack doesn't come back. The Feral card leaves your hand too: 5 - 1 (the first Attack) - 1 (Feral) - 1 (the second Attack).
        Assert.Equal(2, After(Board(), FreeAttack(), FeralCard(), FreeAttack()).You.HandCount);
        // With no 0-cost Attack before it, the Attack after it comes back.
        Assert.Equal(4, After(Board(), FeralCard(), FreeAttack()).You.HandCount);
    }

    [Fact]
    public void MoreFeralOnAnExistingOneOnlyRaisesItsAmount()
    {
        // Feral 1: a 0-cost Attack comes back (its counter 1). A second Feral stacks (amount 2, counter still 1, no AfterApplied): the next
        // 0-cost Attack comes back too.
        var state = Board();
        state.You.Powers = state.You.Powers.With(PowerId.Feral, amount: 1, data: 0);
        var after = After(state, FreeAttack(), FeralCard(), FreeAttack());
        Assert.Equal(4, after.You.HandCount); // 5, the Attack back, Feral gone, the Attack back
        Assert.Equal(2, after.You.Powers[PowerId.Feral]);
        Assert.Equal(2, after.You.Powers.DataOf(PowerId.Feral));
    }

    [Fact]
    public void AReplayedFreeAttackComesBackOnce()
    {
        // Decided once before the plays (OnPlayWrapper): a 0-cost Attack played twice uses one charge and comes back once.
        var state = Board();
        state.You.Powers = state.You.Powers.With(PowerId.Feral, amount: 2, data: 0);
        var after = After(state, FreeAttack(plays: 2));
        Assert.Equal(5, after.You.HandCount);
        Assert.Equal(1, after.You.Powers.DataOf(PowerId.Feral));
        // The second charge is still there for the next one.
        Assert.Equal(5, After(state, FreeAttack(plays: 2), FreeAttack()).You.HandCount);
    }

    [Fact]
    public void EchoFormDoublesOnlyTheFirstCardsPlayedThisTurn()
    {
        // Echo Form 1: the first card play this turn plays twice; a card after it once. Cards already played this turn (the log's first
        // plays of a series): none doubled.
        var state = Board();
        state.You.Powers = state.You.Powers.With(PowerId.EchoForm, amount: 1);
        SimScript Shaped(params SimPlay[] plays)
        {
            var script = new SimScript { Name = "chain" };
            foreach (var play in plays)
                script.Actions.AddRange(SimScript.Of(play).Actions.Select(action => action is CardPlayAction cardPlay ? cardPlay with { CountByModels = true } : action));
            return script;
        }
        SimEngine.RunScriptTo(state, Shaped(FreeAttack(), FreeAttack()), out var after, out _);
        Assert.Equal(500 - 3 * 3, Assert.Single(after).Enemies[0].Hp);
        var played = Board();
        played.You.Powers = played.You.Powers.With(PowerId.EchoForm, amount: 1);
        played.You.Turn = played.You.Turn.With(FirstPlaysStarted.Key, amount: 1);
        SimEngine.RunScriptTo(played, Shaped(FreeAttack()), out var later, out _);
        Assert.Equal(500 - 3, Assert.Single(later).Enemies[0].Hp);
    }
}
