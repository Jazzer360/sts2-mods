using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Conditions apart from effects (decision #190): a SimCondition is asked of each branch's own state when the WhenAction it guards runs,
/// so any condition guards any effect, a replay's later play asks it on the board the earlier plays left, and a test the card's code asks
/// once (its At) keeps its first answer for the rest of the play.
/// </summary>
public class SimConditionTests
{
    private static readonly SimDrawCard Plain = new("plain");

    /// <summary>One enemy; your hand holds the played card and <paramref name="hand"/> - 1 others.</summary>
    private static SimState Board(int enemyHp = 50, int hand = 1, int energy = 0)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = enemyHp } } };
        state.You.HandCount = hand;
        state.You.Energy = energy;
        return state;
    }

    private static AttackAction Hit(int damage, SimPlay.Target target = SimPlay.Target.Single) =>
        new(Hits: 1, target, new DamageSpec(Base: 0m, SimProps.Move) { BaseOn = new Dictionary<int, decimal> { [0] = damage, [1] = damage }, Fixed = true });

    private static SimPlay Card(string name, int plays, params SimAction[] body)
    {
        var play = new SimPlay { Name = name, TargetId = 0, HandCard = 7, HandDelta = -1, Plays = plays };
        play.Body.AddRange(body);
        return play;
    }

    private static CardEventAction Energy(int amount) => new(CardEvent.EnergyGained, amount);

    private static CardEventAction Draw(int cards) => new(CardEvent.Drawn, cards) { Indirect = true, JoinsHand = true };

    private static readonly SimCondition Killed = new AttackKilledCondition();

    // ---- one condition, any effect

    [Fact]
    public void OneConditionGuardsEnergyOrAnAttackOnEveryEnemy()
    {
        // The attack's kill (DamageResult.WasTargetKilled) guards Sunder's energy and Timber's second attack alike: one condition, two effects.
        var sunder = Card("Sunder", plays: 1, Hit(damage: 24), new WhenAction(Killed, new SimAction[] { Energy(amount: 3) }));
        Assert.Equal(3, Assert.Single(SimEngine.Run(Board(enemyHp: 20), sunder).Branches).You.Energy);
        Assert.Equal(0, Assert.Single(SimEngine.Run(Board(enemyHp: 30), sunder).Branches).You.Energy);

        var timber = Card("Timber", plays: 1, Hit(damage: 24), new WhenAction(Killed, new SimAction[] { Hit(damage: 10, SimPlay.Target.All) }));
        var two = Board(enemyHp: 20);
        two.Enemies.Add(new SimEnemy { Id = 1, Name = "E1", Hp = 30 });
        Assert.Equal(20, Assert.Single(SimEngine.Run(two, timber).Branches).Enemies[1].Hp);
        var survives = Board(enemyHp: 25);
        survives.Enemies.Add(new SimEnemy { Id = 1, Name = "E1", Hp = 30 });
        Assert.Equal(30, Assert.Single(SimEngine.Run(survives, timber).Branches).Enemies[1].Hp);
    }

    [Fact]
    public void TwoConditionsGuardTheSameEffect()
    {
        // A draw on a kill, and the same draw while your hand is empty (Restlessness' test): each asks its own test of the state.
        var pile = new SimDrawPile { Rest = { new SimDrawGroup(Plain, 10) } };
        var onKill = Card("OnKill", plays: 1, Hit(damage: 24), new WhenAction(Killed, new SimAction[] { Draw(cards: 2) }));
        var killed = Board(enemyHp: 20);
        killed.You.Draw = pile;
        Assert.Equal(2, Assert.Single(SimEngine.Run(killed, onKill).Branches).You.HandCount);

        var alone = new CountCondition(SimCounted.HandCards, SimCompare.Equal, Than: 0);
        var onEmptyHand = Card("OnEmptyHand", plays: 1, new WhenAction(alone, new SimAction[] { Draw(cards: 2) }));
        var empty = Board(hand: 1);
        empty.You.Draw = pile;
        Assert.Equal(2, Assert.Single(SimEngine.Run(empty, onEmptyHand).Branches).You.HandCount);
        var full = Board(hand: 3);
        full.You.Draw = pile;
        Assert.Equal(2, Assert.Single(SimEngine.Run(full, onEmptyHand).Branches).You.HandCount);
    }

    [Fact]
    public void AConditionTheAttackDidntMeetGuardsNothingOnThatBranch()
    {
        // A random hit at two enemies, one at 10 HP: the energy only on the branch whose hit killed (half the weight).
        var state = Board(enemyHp: 10);
        state.Enemies.Add(new SimEnemy { Id = 1, Name = "E1", Hp = 50 });
        var play = Card("RandomKill", plays: 1, Hit(damage: 12, SimPlay.Target.Random), new WhenAction(Killed, new SimAction[] { Energy(amount: 2) }));
        var branches = SimEngine.Run(state, play).Branches;
        Assert.Equal(2, branches.Count);
        Assert.Equal(2, branches.Single(branch => branch.Enemies[0].Hp <= 0).You.Energy);
        Assert.Equal(0, branches.Single(branch => branch.Enemies[0].Hp > 0).You.Energy);
    }

    // ---- replays: each play asks on the board the one before it left

    [Fact]
    public void ASacrificeReplayedFindsOstyGoneOnItsSecondPlay()
    {
        // Sacrifice: if Osty isn't missing, he dies and you gain block. Its second play finds him dead: no block (decided before the first
        // play, it gained the block twice).
        var ostyHere = new NotCondition(new OstyMissingCondition { At = "Sacrifice:0010" });
        var sacrifice = Card("Sacrifice", plays: 2, new WhenAction(ostyHere, new SimAction[]
        {
            new KillOstyAction(),
            new GainBlockAction(Times: 1, Base: 12m, SimProps.Move) { Fixed = true },
        }));
        var state = Board();
        state.You.WithOsty(hp: 10);
        var branch = Assert.Single(SimEngine.Run(state, sacrifice).Branches);
        Assert.Equal(0, branch.You.OstyHp);
        Assert.Equal(12, branch.You.Block);
    }

    [Fact]
    public void AnImpatienceReplayedDrawsAgainOnlyWhenItDrewNoAttack()
    {
        // Impatience: draw 2 if no Attack is in your hand. Its filter (an Attack) is followed through the play: a drawn Attack counts.
        const string filter = SimEngine.ConditionFilterPrefix + "Impatience#0";
        var attack = new SimDrawCard("attack") { TakenBy = new[] { filter } };
        var state = Board(hand: 1); // Impatience itself
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(attack, 2), new SimDrawGroup(Plain, 2) } };
        var noAttack = new CountCondition(SimCounted.HandMatching, SimCompare.Equal, Than: 0) { Filter = filter, Matching = 0, At = "Impatience:0004" };
        var impatience = Card("Impatience", plays: 2, new WhenAction(noAttack, new SimAction[] { Draw(cards: 2) }));
        var outcome = SimEngine.Run(state, impatience);
        // The first play draws 2; only the branch that drew 2 plain cards (1/6 of the weight) draws 2 more on the second.
        Assert.All(outcome.Branches, branch => Assert.Null(branch.Unmodelled));
        Assert.Equal(1.0 / 6, outcome.Branches.Where(branch => branch.You.HandCount == 4).Sum(branch => branch.Weight), precision: 6);
        Assert.Equal(5.0 / 6, outcome.Branches.Where(branch => branch.You.HandCount == 2).Sum(branch => branch.Weight), precision: 6);
    }

    [Fact]
    public void AConditionOnTheHandsKindAfterAnUnreadCardJoinedIsUnknown()
    {
        // A card made into your hand with no profile read: whether it's an Attack isn't known, so the second play's test can't be told.
        const string filter = SimEngine.ConditionFilterPrefix + "Impatience#0";
        var noAttack = new CountCondition(SimCounted.HandMatching, SimCompare.Equal, Than: 0) { Filter = filter, Matching = 0, At = "Impatience:0004" };
        var play = Card("Impatience", plays: 2,
            new WhenAction(noAttack, new SimAction[] { new CardEventAction(CardEvent.Generated, Count: 1) { Indirect = true, JoinsHand = true } }));
        var branch = Assert.Single(SimEngine.Run(Board(hand: 1), play).Branches);
        Assert.Equal(SimEngine.HandMatchingUnknown, branch.Unmodelled);
    }

    [Fact]
    public void ACardYouPickMayBeTheKindTheTestCountsSoItsCountIsntKnown()
    {
        // One Attack among 3 other cards; you discard one you pick (a card, never a particular one: decision #188), then a test asks
        // whether an Attack is left: it may or may not be, so it isn't known. With the Attack the only card, it's known to be gone.
        const string filter = SimEngine.ConditionFilterPrefix + "Probe#0";
        var attackLeft = new CountCondition(SimCounted.HandMatching, SimCompare.Greater, Than: 0) { Filter = filter, Matching = 1 };
        SimPlay Play() => Card("Probe", plays: 1,
            new TakeFromHandAction(HandTakeWay.Pick) { Event = CardEvent.Discarded },
            new WhenAction(attackLeft, new SimAction[] { Energy(amount: 1) }));
        Assert.Equal(SimEngine.HandMatchingUnknown, Assert.Single(SimEngine.Run(Board(hand: 4), Play()).Branches).Unmodelled);
        var alone = Assert.Single(SimEngine.Run(Board(hand: 2), Play()).Branches);
        Assert.Null(alone.Unmodelled);
        Assert.Equal(0, alone.You.Energy);
    }

    [Fact]
    public void FtlDrawsWhileYourPlaysThisTurnAreUnderItsMost()
    {
        // FTL: draw if the cards you finished playing this turn are fewer than PlayMax (3). With 2 finished before it, replayed: the first
        // play draws (2 < 3), the second doesn't (the first is the third finished).
        var underMost = new CountCondition(SimCounted.PlaysFinishedThisTurn, SimCompare.Less, Than: 3) { At = "Ftl:0020" };
        var ftl = Card("Ftl", plays: 2, Hit(damage: 5), new WhenAction(underMost, new SimAction[] { Draw(cards: 1) }));
        var state = Board();
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(Plain, 10) } };
        state.You.Turn = SimKeys.CountPlay(SimKeys.CountPlay(state.You.Turn, PlayStage.Finished, SimCardType.Skill, Array.Empty<SimCardTag>()),
            PlayStage.Finished, SimCardType.Skill, Array.Empty<SimCardTag>());
        Assert.Equal(1, Assert.Single(SimEngine.Run(state, ftl).Branches).You.HandCount);
    }

    [Fact]
    public void FetchDrawsOnlyOnItsFirstPlayThisTurn()
    {
        // Fetch: draw if this card hasn't finished a play this turn. Replayed, the second play finds the first finished.
        var notPlayed = new NotCondition(new CountCondition(SimCounted.CardPlaysFinished, SimCompare.Greater, Than: 0) { Before = 0, At = "Fetch:0030" });
        var fetch = Card("Fetch", plays: 2, new WhenAction(notPlayed, new SimAction[] { Draw(cards: 1) }));
        var state = Board();
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(Plain, 10) } };
        Assert.Equal(1, Assert.Single(SimEngine.Run(state, fetch).Branches).You.HandCount);
        // Played earlier this turn: no draw at all.
        var again = fetch.Body[0] is WhenAction when ? Card("Fetch", plays: 1, when with { Condition = new NotCondition(new CountCondition(SimCounted.CardPlaysFinished, SimCompare.Greater, Than: 0) { Before = 1 }) }) : fetch;
        Assert.Equal(0, Assert.Single(SimEngine.Run(state, again).Branches).You.HandCount);
    }

    // ---- a test asked once, where the code asks it

    [Fact]
    public void ATestAskedOnceKeepsItsAnswerForTheRestOfThePlay()
    {
        // Restlessness: if it's alone in your hand, draw 2, then gain 2 energy. The energy's test is the draws' (one if): asked once, before
        // the draws filled the hand. Asked again after them, it would fail.
        var alone = new CountCondition(SimCounted.HandCards, SimCompare.Equal, Than: 0) { At = "Restlessness:0008" };
        var restlessness = Card("Restlessness", plays: 1,
            new WhenAction(alone, new SimAction[] { Draw(cards: 2) }),
            new WhenAction(alone, new SimAction[] { Energy(amount: 2) }));
        var state = Board(hand: 1);
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(Plain, 10) } };
        var branch = Assert.Single(SimEngine.Run(state, restlessness).Branches);
        Assert.Equal(2, branch.You.HandCount);
        Assert.Equal(2, branch.You.Energy);
    }

    [Fact]
    public void AnElseIsAskedWhereItsIfWas()
    {
        // Wheel of Fortune: if your hand has cards, discard them and draw; else draw. With an empty hand only the else draws: its test was
        // asked before the draw filled the hand.
        var hasCards = new CountCondition(SimCounted.HandCards, SimCompare.Greater, Than: 0) { At = "WheelOfFortune:0010" };
        var wheel = Card("WheelOfFortune", plays: 1,
            new WhenAction(hasCards, new SimAction[] { new TakeFromHandAction(HandTakeWay.Each) { Event = CardEvent.Discarded }, Draw(cards: 3) }),
            new WhenAction(new NotCondition(hasCards), new SimAction[] { Draw(cards: 3) }));
        var state = Board(hand: 1);
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(Plain, 10) } };
        var branch = Assert.Single(SimEngine.Run(state, wheel).Branches);
        Assert.Equal(3, branch.You.HandCount);
        Assert.Equal(0, branch.PlayEvents[CardEvent.Discarded]);
    }

    [Fact]
    public void AStoredTestIsAskedBeforeTheAttackThatChangesIt()
    {
        // Ki Hop: wasImpaled is stored before its attack, and the draw waits on it. The hit knocks the target's last arrow loose, yet the draw
        // happens: the test was asked before it (AskAction). Replayed, the second play asks it again: no arrow, no draw.
        var impaled = new TargetImpaledCondition(ByOwner: false) { At = "KiHop:0040" };
        var kiHop = Card("KiHop", plays: 2, new AskAction(impaled), Hit(damage: 5), new WhenAction(impaled, new SimAction[] { Draw(cards: 1) }));
        var state = Board();
        state.Enemies[0].Powers = PowerSet.Empty.With(PowerId.Impaled, amount: 1, data: 1);
        state.You.Draw = new SimDrawPile { Rest = { new SimDrawGroup(Plain, 10) } };
        var branch = Assert.Single(SimEngine.Run(state, kiHop).Branches);
        Assert.False(branch.Enemies[0].Powers.Has(PowerId.Impaled));
        Assert.Equal(1, branch.You.HandCount);
    }
}
