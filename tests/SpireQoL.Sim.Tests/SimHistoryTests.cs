using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// The history ledger (decision #191): the counts of the game's log of the fight the board's cards, relics and powers ask about, seeded once
/// in the start state and counted forward by the simulator's own events as the chain unfolds; this turn's counts start over where the
/// players' turn ends.
/// </summary>
public class SimHistoryTests
{
    /// <summary>Forgotten Ritual's and Evil Eye's question (WasCardExhaustedThisTurn): a card its owner exhausted this turn.</summary>
    private static readonly HistoryQuery ExhaustedThisTurn = new(HistoryKind.CardExhausted, HistoryScope.ThisTurn, HistoryWhose.Owner);

    /// <summary>Midnight's: every card exhausted this fight, anyone's.</summary>
    private static readonly HistoryQuery ExhaustedThisFight = new(HistoryKind.CardExhausted, HistoryScope.ThisFight, HistoryWhose.Anyone);

    /// <summary>Spite's (LostHpThisTurn): damage its owner took this turn that got through (Result.UnblockedDamage &gt; 0).</summary>
    private static readonly HistoryQuery LostHpThisTurn = new(HistoryKind.DamageReceived, HistoryScope.ThisTurn, HistoryWhose.Owner) { Details = HistoryDetail.Unblocked };

    private static SimState Board(int enemyHp = 50, params HistoryQuery[] queries)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 0, Name = "E0", Hp = enemyHp } }, History = queries };
        state.You.HandCount = 4;
        return state;
    }

    private static AttackAction Hit(int damage, int hits = 1) =>
        new(hits, SimPlay.Target.Single, new DamageSpec(Base: 0m, SimProps.Move) { BaseOn = new Dictionary<int, decimal> { [0] = damage }, Fixed = true });

    private static SimPlay Card(string name, params SimAction[] body)
    {
        var play = new SimPlay { Name = name, TargetId = 0, HandCard = 0, HandDelta = 0 };
        play.Body.AddRange(body);
        return play;
    }

    private static SimScript Chain(params SimPlay[] plays)
    {
        var script = new SimScript { Name = string.Join(" then ", plays.Select(play => play.Name)) };
        foreach (var play in plays) script.Actions.AddRange(SimScript.Of(play).Actions);
        return script;
    }

    private static CountCondition Logged(HistoryQuery query, SimCompare compare = SimCompare.Greater, int than = 0) =>
        new(SimCounted.History, compare, than) { Query = query };

    private static SimPlay ForgottenRitual() =>
        Card("ForgottenRitual", new WhenAction(Logged(ExhaustedThisTurn), new SimAction[] { new CardEventAction(CardEvent.EnergyGained, Count: 3) }));

    private static SimPlay Exhausting() => Card("BurningPact", new CardEventAction(CardEvent.Exhausted, Count: 1) { Indirect = true });

    [Fact]
    public void AnExhaustEarlierInTheChainMakesForgottenRitualsEnergy()
    {
        // Nothing exhausted yet this turn: no energy. A card exhausted by an earlier play of the chain: the ledger counted it, so 3.
        Assert.Equal(0, Assert.Single(SimEngine.Run(Board(queries: ExhaustedThisTurn), ForgottenRitual()).Branches).You.Energy);
        Assert.Equal(3, Assert.Single(SimEngine.Run(Board(queries: ExhaustedThisTurn), Chain(Exhausting(), ForgottenRitual())).Branches).You.Energy);
        // One the game's log already has (seeded from the start state): 3 without an exhaust in the chain.
        var seeded = Board(queries: ExhaustedThisTurn);
        seeded.You.Turn = seeded.You.Turn.With(ExhaustedThisTurn.Key, amount: 1);
        Assert.Equal(3, Assert.Single(SimEngine.Run(seeded, ForgottenRitual()).Branches).You.Energy);
    }

    [Fact]
    public void ATeammatesExhaustIsntYours()
    {
        // Whose: the entry's actor is the owner (e.Actor == base.Owner.Creature). A teammate's exhaust counts in their ledger, not yours.
        var state = Board(queries: ExhaustedThisTurn);
        state.Teammates.Add(new SimPlayer { HandCount = 3 });
        var teammatesExhaust = Card("TheirPact", new CardEventAction(CardEvent.Exhausted, Count: 1) { Indirect = true, Player = Who.Teammate(0) });
        SimEngine.RunScriptTo(state, Chain(teammatesExhaust, ForgottenRitual()), out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(0, branch.You.Energy);
        Assert.Equal(1, branch.Teammates[0].Turn[ExhaustedThisTurn.Key]);
    }

    [Fact]
    public void HpLostEarlierInTheTurnMakesSpiteHitMore()
    {
        // Spite: two hits when you lost HP this turn, else one (its hit count's ways, each on the ledger's count).
        SimPlay Spite() => Card("Spite",
            new WhenAction(Logged(LostHpThisTurn), new SimAction[] { Hit(damage: 5, hits: 2) }),
            new WhenAction(new NotCondition(Logged(LostHpThisTurn)), new SimAction[] { Hit(damage: 5) }));
        Assert.Equal(45, Assert.Single(SimEngine.Run(Board(queries: LostHpThisTurn), Spite()).Branches).Enemies[0].Hp);
        // Offering's HP cost first: the ledger counts the HP lost, and Spite hits twice.
        var offering = Card("Offering", new LoseHpAction(Amount: 6));
        var branch = Assert.Single(SimEngine.Run(Board(queries: LostHpThisTurn), Chain(offering, Spite())).Branches);
        Assert.Equal(40, branch.Enemies[0].Hp);
    }

    [Fact]
    public void DamageYourBlockStoppedIsntHpLost()
    {
        // The entry is logged either way, but its UnblockedDamage is 0 when your block took it all: Spite's test doesn't count it. Damage
        // through the block does.
        var anyDamage = new HistoryQuery(HistoryKind.DamageReceived, HistoryScope.ThisTurn, HistoryWhose.Owner);
        var state = Board(queries: new[] { LostHpThisTurn, anyDamage });
        state.You.Block = 10;
        var blocked = Card("Galvanized", new HitYouAction("Galvanic", Amount: 4));
        SimEngine.RunScriptTo(state, SimScript.Of(blocked), out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(0, branch.You.Turn[LostHpThisTurn.Key]);
        Assert.Equal(1, branch.You.Turn[anyDamage.Key]);
        var through = Card("Galvanized", new HitYouAction("Galvanic", Amount: 14));
        SimEngine.RunScriptTo(state, SimScript.Of(through), out after, out _);
        Assert.Equal(1, Assert.Single(after).You.Turn[LostHpThisTurn.Key]);
    }

    [Fact]
    public void TheEnemiesTurnStartsTheTurnsCountsOverAndKeepsTheFights()
    {
        // At the end of the simulated turn the players' turn is over: "this turn" is the enemies' now (HappenedThisTurn), so this turn's
        // counts (the ledger's and the plays') start over; the fight's keep what the turn added.
        var state = Board(queries: new[] { ExhaustedThisTurn, ExhaustedThisFight });
        state.You.Turn = state.You.Turn.With(ExhaustedThisTurn.Key, amount: 2).With(SimKeys.Plays(PlayStage.Finished), amount: 3);
        state.You.Fight = state.You.Fight.With(ExhaustedThisFight.Key, amount: 5);
        var outcome = SimEngine.Run(state, Exhausting());
        var branch = Assert.Single(outcome.Branches);
        Assert.Equal(0, branch.You.Turn[ExhaustedThisTurn.Key]);
        Assert.Equal(0, branch.You.Turn[SimKeys.Plays(PlayStage.Finished)]);
        Assert.Equal(6, branch.You.Fight[ExhaustedThisFight.Key]);
    }

    [Fact]
    public void AnyonesQueryCountsEveryPlayersEvent()
    {
        // Midnight: CardExhaustedEntry.Count(), no filter: a teammate's exhaust counts in every player's ledger.
        var state = Board(queries: ExhaustedThisFight);
        state.Teammates.Add(new SimPlayer { HandCount = 3 });
        var teammatesExhaust = Card("TheirPact", new CardEventAction(CardEvent.Exhausted, Count: 2) { Indirect = true, Player = Who.Teammate(0) });
        var branch = Assert.Single(SimEngine.Run(state, teammatesExhaust).Branches);
        Assert.Equal(2, branch.You.Fight[ExhaustedThisFight.Key]);
        Assert.Equal(2, branch.Teammates[0].Fight[ExhaustedThisFight.Key]);
    }

    [Fact]
    public void APlaysDetailIsTestedAsTheGameLogsIt()
    {
        // Echo Form's question: plays its owner started this turn that are the first of their series (not a replay's later plays). Feral's:
        // Attacks started for no energy. A replayed 0-cost Attack: two plays, one first in its series, both for no energy.
        var firstInSeries = new HistoryQuery(HistoryKind.CardPlayStarted, HistoryScope.ThisTurn, HistoryWhose.Owner) { Details = HistoryDetail.FirstInSeries };
        var freeAttacks = new HistoryQuery(HistoryKind.CardPlayStarted, HistoryScope.ThisTurn, HistoryWhose.Owner) { Details = HistoryDetail.ZeroEnergy, CardType = SimCardType.Attack };
        var state = Board(queries: new[] { firstInSeries, freeAttacks });
        var play = Card("Strike", Hit(damage: 1));
        play.Card = new SimCardPlay(new SimCard("Strike", SimCardType.Attack) { Energy = 0 }, Who.You, Target: 0, PlayIndex: 0, PlayCount: 2, AutoPlay: false);
        // The ledger as the play leaves it, before the enemies' turn: the script alone.
        SimEngine.RunScriptTo(state, SimScript.Of(play), out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(1, branch.You.Turn[firstInSeries.Key]);
        Assert.Equal(2, branch.You.Turn[freeAttacks.Key]);
        Assert.Equal(2, branch.You.Turn[SimKeys.Plays(PlayStage.Started, SimCardType.Attack)]);
    }

    [Fact]
    public void OstysAttacksAreHis()
    {
        // Rattle's and Flatten's question: attacks Osty made this turn (CreatureAttackedEntry, e.Actor == Owner.Osty). Yours aren't his.
        var ostysAttacks = new HistoryQuery(HistoryKind.CreatureAttacked, HistoryScope.ThisTurn, HistoryWhose.OwnersOsty);
        var yourAttacks = new HistoryQuery(HistoryKind.CreatureAttacked, HistoryScope.ThisTurn, HistoryWhose.Owner);
        var state = Board(queries: new[] { ostysAttacks, yourAttacks });
        state.You.OstyHp = 10;
        var ostyHits = Card("Poke", Hit(damage: 3) with { ByOsty = true }, Hit(damage: 2));
        SimEngine.RunScriptTo(state, SimScript.Of(ostyHits), out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(1, branch.You.Turn[ostysAttacks.Key]);
        Assert.Equal(1, branch.You.Turn[yourAttacks.Key]);
    }

    [Fact]
    public void RattleHitsOnceMoreForEachAttackOstyMadeThisTurn()
    {
        // Rattle: Osty attacks with WithHitCount(CalculatedHits on its target): 1 + the attacks Osty made this turn (the log's
        // CreatureAttackedEntry, e.Actor == Owner.Osty). Replayed, its second play counts the first's attack: two hits.
        var ostysAttacks = new HistoryQuery(HistoryKind.CreatureAttacked, HistoryScope.ThisTurn, HistoryWhose.OwnersOsty);
        var state = Board(queries: new[] { ostysAttacks });
        state.You.OstyHp = 10;
        var rattle = Card("Rattle", Hit(damage: 3, hits: 1) with { ByOsty = true, HitsLogged = new LoggedAmount(ostysAttacks, Per: 1, AtStart: 0) });
        rattle.Plays = 2;
        rattle.Card = new SimCardPlay(new SimCard("Rattle", SimCardType.Attack), Who.You, Target: 0, PlayIndex: 0, PlayCount: 2, AutoPlay: false);
        SimEngine.RunScriptTo(state, SimScript.Of(rattle), out var after, out _);
        var branch = Assert.Single(after);
        Assert.Equal(2, branch.You.Turn[ostysAttacks.Key]);
        Assert.Equal(50 - 3 * (1 + 2), branch.Enemies[0].Hp);
    }

    [Fact]
    public void VoltaicChannelsALightningForEachLightningChanneledThisFight()
    {
        // Voltaic.OnPlay: lightningChanneledCount = CalculatedChannels (the Lightning its owner channeled this fight), stored once, then a
        // channel each round. One on the board; replayed, its second play counts the first one's too: two more, three in all.
        var lightning = new HistoryQuery(HistoryKind.OrbChanneled, HistoryScope.ThisFight, HistoryWhose.Owner) { Orb = OrbKind.Lightning };
        var state = Board(queries: new[] { lightning });
        state.You.OrbCapacity = 5;
        var voltaic = Card("Voltaic", new OrbAction(OrbAction.Op.Channel) { Orb = OrbKind.Lightning, Times = 1, TimesLogged = new LoggedAmount(lightning, Per: 1, AtStart: 0) });
        voltaic.Plays = 2;
        voltaic.Card = new SimCardPlay(new SimCard("Voltaic", SimCardType.Skill), Who.You, Target: Who.None, PlayIndex: 0, PlayCount: 2, AutoPlay: false);
        SimEngine.RunScriptTo(state, SimScript.Of(voltaic), out var after, out _);
        Assert.Equal(3, Assert.Single(after).You.Orbs.Count(orb => orb.Kind == OrbKind.Lightning));
    }

    [Fact]
    public void ACountWhoseEventsCardWasntReadIsntKnown()
    {
        // Iteration's question: Status cards drawn this turn. A draw with no draw pile read can't say what it drew: the count isn't known,
        // and a condition on it makes the play unknown (never a guess).
        var statusDrawn = new HistoryQuery(HistoryKind.CardDrawn, HistoryScope.ThisTurn, HistoryWhose.Owner) { CardType = SimCardType.Status };
        var state = Board(queries: statusDrawn);
        var draws = Card("Draws", new CardEventAction(CardEvent.Drawn, Count: 1) { Indirect = true },
            new WhenAction(Logged(statusDrawn), new SimAction[] { new CardEventAction(CardEvent.EnergyGained, Count: 1) }));
        SimEngine.RunScriptTo(state, SimScript.Of(draws), out var after, out _);
        var branch = Assert.Single(after);
        Assert.Null(SimEngine.HistoryCount(branch, Who.You, statusDrawn));
        Assert.Equal(SimEngine.HistoryUnknown, branch.Unmodelled);
    }

    [Fact]
    public void AQuestionTheStartStateDidntSeedIsUnknownNeverZero()
    {
        // Forgotten Ritual's condition on a board whose start state has no seed for it: the play is unknown, its energy not decided either way.
        SimEngine.RunScriptTo(Board(), SimScript.Of(ForgottenRitual()), out var after, out _);
        Assert.Equal(SimEngine.HistoryUnknown, Assert.Single(after).Unmodelled);
    }

    [Fact]
    public void ArrowsAreCountedAsShotsSpendThem()
    {
        // Arrow Stab's test (ArrowCmd.PlayerHasAnyArrows): the arrows you have as the step runs.
        var anyArrows = new CountCondition(SimCounted.ArrowsAvailable, SimCompare.Greater, Than: 0);
        var stab = Card("ArrowStab", new WhenAction(anyArrows, new SimAction[] { new CardEventAction(CardEvent.EnergyGained, Count: 1) }));
        var armed = Board();
        armed.You.Arrows = 1;
        Assert.Equal(1, Assert.Single(SimEngine.Run(armed, stab).Branches).You.Energy);
        Assert.Equal(0, Assert.Single(SimEngine.Run(Board(), stab).Branches).You.Energy);
    }

    [Fact]
    public void ACalculatedValueOnTheLogFollowsTheChain()
    {
        // Finisher: a hit per Attack finished this turn (CalculatedHits). None on the board; replayed, its second play finds the first one
        // finished: one hit.
        var attacksFinished = new HistoryQuery(HistoryKind.CardPlayFinished, HistoryScope.ThisTurn, HistoryWhose.Owner) { CardType = SimCardType.Attack };
        var finisher = Card("Finisher", Hit(damage: 6, hits: 0) with { HitsLogged = new LoggedAmount(attacksFinished, Per: 1, AtStart: 0) });
        finisher.Plays = 2;
        finisher.Card = new SimCardPlay(new SimCard("Finisher", SimCardType.Attack), Who.You, Target: 0, PlayIndex: 0, PlayCount: 2, AutoPlay: false);
        Assert.Equal(44, Assert.Single(SimEngine.Run(Board(), finisher).Branches).Enemies[0].Hp);
        // Murder: 1 damage, one more for each card drawn this fight (CalculatedDamage): a draw earlier in the play adds one.
        var drawn = new HistoryQuery(HistoryKind.CardDrawn, HistoryScope.ThisFight, HistoryWhose.Owner);
        var murderHit = new AttackAction(Hits: 1, SimPlay.Target.Single, new DamageSpec(Base: 0m, SimProps.Move)
            { BaseOn = new Dictionary<int, decimal> { [0] = 1 }, Fixed = true, Logged = new LoggedAmount(drawn, Per: 1, AtStart: 0) });
        var murder = Card("Murder", new CardEventAction(CardEvent.Drawn, Count: 2) { Indirect = true }, murderHit);
        Assert.Equal(47, Assert.Single(SimEngine.Run(Board(queries: drawn), murder).Branches).Enemies[0].Hp);
    }

    [Fact]
    public void TheLedgerKeepsThePlaysNamesAndReportsHoldIt()
    {
        // The plays of the turn by type keep the names replay cases already hold; the ledger and its queries go through a report's JSON.
        Assert.Equal("Finished:Attack", new HistoryQuery(HistoryKind.CardPlayFinished, HistoryScope.ThisTurn, HistoryWhose.Owner) { CardType = SimCardType.Attack }.Key);
        Assert.Equal("Started#Wah", new HistoryQuery(HistoryKind.CardPlayStarted, HistoryScope.ThisTurn, HistoryWhose.Owner) { CardTag = SimCardTag.Wah }.Key);
        var state = Board(queries: new[] { ExhaustedThisTurn, LostHpThisTurn, ExhaustedThisFight });
        state.You.Fight = state.You.Fight.With(ExhaustedThisFight.Key, amount: 4);
        var back = SimState.FromJson(state.ToJson());
        Assert.Equal(state.History, back.History);
        Assert.Equal(4, back.You.Fight[ExhaustedThisFight.Key]);
    }
}
