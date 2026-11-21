using Xunit;

namespace SpireQoL.Sim.Tests;

/// <summary>
/// Cards that act on what their own draw gives (decision #118, decompile: the result of CardPileCmd.Draw): Escape Plan gains its
/// block only when it draws a Skill; Pillage draws again while it draws an Attack and your hand has room; Scrape discards what it
/// drew unless it costs 0 (an X cost isn't 0). Each draw is a branch per profile, which carries the type or the cost while such a
/// card is in your hand, so the chance of each outcome is exact.
/// </summary>
public class SimDrawnCardTests
{
    private static readonly SimDrawCard Skill = new("skill") { Type = SimCardType.Skill };
    private static readonly SimDrawCard Attack = new("attack") { Type = SimCardType.Attack };
    private static readonly SimDrawCard FreeCard = new("free") { Free = true };
    private static readonly SimDrawCard Burn = new("burn") { Free = false, TurnEnd = new TurnEndCard("Burn", "Burn", Amount: 2) };
    private static readonly SimDrawCard SlyCard = new("sly") { Free = false, Sly = true };

    private static SimDrawPile Pile(params (SimDrawCard Card, int Count)[] rest) => new() { Rest = rest.Select(cardCount => new SimDrawGroup(cardCount.Card, cardCount.Count)).ToList() };

    private static SimState You(SimDrawPile pile, int hand = 5)
    {
        var state = new SimState { Enemies = { new SimEnemy { Id = 1, Name = "E1", Hp = 50, Hits = new() { 10 } } } };
        state.You.HandCount = hand;
        state.You.Hp = 50;
        state.You.Draw = pile;
        return state;
    }

    /// <summary>A card played from your hand whose draw it acts on.</summary>
    private static SimPlay Playing(string name, DrawnCardAction action) => new() { Name = name, HandCard = 7, HandDelta = -1 + action.Count, Body = { action } };

    private static DrawnCardAction EscapePlan => new(Count: 1) { BlockIf = SimCardType.Skill, Block = new GainBlockAction(Times: 1, Base: 3, SimProps.Move) };

    [Fact]
    public void EscapePlanBlocksOnlyWhenItDrawsASkill()
    {
        // One Skill in four: a quarter of the outcomes have its 3 block, so the enemy's 10 takes 7 there and 10 in the rest.
        var outcome = SimEngine.Run(You(Pile((Skill, 1), (Attack, 3))), Playing("EscapePlan", EscapePlan));
        Assert.Equal(0.25, outcome.Branches.Where(branch => branch.Taken == 7).Sum(branch => branch.Weight), 9);
        Assert.Equal(0.75, outcome.Branches.Where(branch => branch.Taken == 10).Sum(branch => branch.Weight), 9);
    }

    [Fact]
    public void EscapePlanIntoAFullHandDrawsNothingAndGainsNothing()
    {
        var outcome = SimEngine.Run(You(Pile((Skill, 4)), hand: 11), Playing("EscapePlan", EscapePlan));
        Assert.All(outcome.Branches, branch => Assert.Equal(10, branch.Taken));
    }

    [Fact]
    public void PillageDrawsAgainWhileItDrawsAnAttack()
    {
        // Two Attacks and two Skills: it stops at the first Skill. 1 card: 1/2; 2: 2/4 * 2/3 = 1/3; 3: 2/4 * 1/3 * 2/2 = 1/6.
        var outcome = SimEngine.Run(You(Pile((Attack, 2), (Skill, 2)), hand: 5), Playing("Pillage", new DrawnCardAction(Count: 1) { DrawAgainIf = SimCardType.Attack }));
        double Drew(int drawn) => outcome.Branches.Where(branch => branch.You.Draw!.RestCount == 4 - drawn).Sum(branch => branch.Weight);
        Assert.Equal(0.5, Drew(drawn: 1), 9);
        Assert.Equal(1.0 / 3, Drew(drawn: 2), 9);
        Assert.Equal(1.0 / 6, Drew(drawn: 3), 9);
    }

    [Fact]
    public void PillageStopsAtAFullHand()
    {
        // Nine in hand with Pillage: it leaves (8), then draws Attacks until the hand holds 10.
        var outcome = SimEngine.Run(You(Pile((Attack, 6)), hand: 9), Playing("Pillage", new DrawnCardAction(Count: 1) { DrawAgainIf = SimCardType.Attack }));
        Assert.All(outcome.Branches, branch => Assert.Equal(4, branch.You.Draw!.RestCount));
    }

    [Fact]
    public void ScrapeDiscardsWhatItDrewUnlessItCostsZero()
    {
        // Draws 2 from a free card and a Burn: the Burn is discarded, so it doesn't burn you at the end of your turn.
        var outcome = SimEngine.Run(You(Pile((FreeCard, 1), (Burn, 1))), Playing("Scrape", new DrawnCardAction(Count: 2) { DiscardUnlessFree = true }));
        var state = Assert.Single(outcome.Branches);
        Assert.Equal(10, state.Taken);                 // the enemy's 10, no Burn
        Assert.Empty(state.You.TurnEndCards);
        Assert.Equal(1, state.PlayEvents[CardEvent.Discarded]);
    }

    [Fact]
    public void ScrapeDiscardingASlyCardIsNotModelled()
    {
        var outcome = SimEngine.Run(You(Pile((SlyCard, 1), (FreeCard, 1))), Playing("Scrape", new DrawnCardAction(Count: 1) { DiscardUnlessFree = true }));
        Assert.Contains(outcome.Branches, branch => branch.Unmodelled != null);
        Assert.Contains(outcome.Branches, branch => branch.Unmodelled == null);
    }

    // ---- against the textbook odds (decision #120): every pile makeup and hand size, not just ones worked out by hand

    /// <summary>
    /// Pillage's chance of drawing each number of Attacks, worked out the plain way: it has drawn t Attacks so far; the next card
    /// is a non-Attack with chance k / (cards left), which stops it, else an Attack, and it goes on, until the hand is full (room
    /// draws) or the piles are empty.
    /// </summary>
    private static Dictionary<int, double> PillageOdds(int attacks, int others, int room)
    {
        var distribution = new Dictionary<int, double>();
        var chanceStillDrawing = 1.0;
        for (var attacksDrawn = 0; chanceStillDrawing > 0; attacksDrawn++)
        {
            var left = attacks + others - attacksDrawn;
            if (attacksDrawn == room || left == 0) { distribution[attacksDrawn] = distribution.GetValueOrDefault(attacksDrawn) + chanceStillDrawing; break; }
            distribution[attacksDrawn] = distribution.GetValueOrDefault(attacksDrawn) + chanceStillDrawing * others / left;
            chanceStillDrawing *= (double)(attacks - attacksDrawn) / left;
        }
        return distribution.Where(entry => entry.Value > 0).ToDictionary(entry => entry.Key, entry => entry.Value);
    }

    private static double Choose(int total, int chosen) => chosen < 0 || chosen > total ? 0 : Enumerable.Range(1, chosen).Aggregate(1.0, (product, index) => product * (total - chosen + index) / index);

    private static void SameOdds(Dictionary<int, double> expected, SimOutcome outcome, string what)
    {
        var distribution = DrawnChance.Distribution(outcome)!;
        Assert.True(Math.Abs(distribution.Values.Sum() - 1) < 1e-9, $"{what}: the chances add to {distribution.Values.Sum()}");
        foreach (var drawn in expected.Keys.Union(distribution.Keys))
            Assert.True(Math.Abs(expected.GetValueOrDefault(drawn) - distribution.GetValueOrDefault(drawn)) < 1e-9,
                $"{what}: {drawn} drawn, expected {expected.GetValueOrDefault(drawn)}, simulated {distribution.GetValueOrDefault(drawn)}");
    }

    [Fact]
    public void PillageMatchesTheTextbookOddsForEveryPileAndHand()
    {
        // The hand counts Pillage; it leaves first, so the hand has room for 11 - hand draws.
        for (var attacks = 0; attacks <= 8; attacks++)
        for (var others = 0; others <= 6; others++)
        for (var hand = 1; hand <= 10; hand++)
        {
            if (attacks + others == 0) continue;
            var pile = new List<(SimDrawCard, int)>();
            if (attacks > 0) pile.Add((Attack, attacks));
            if (others > 0) pile.Add((Skill, others));
            var outcome = SimEngine.Run(You(Pile(pile.ToArray()), hand), Playing("Pillage", new DrawnCardAction(Count: 1) { DrawAgainIf = SimCardType.Attack }));
            SameOdds(PillageOdds(attacks, others, 11 - hand), outcome, $"{attacks} Attacks, {others} others, hand {hand}");
        }
    }

    [Fact]
    public void PillageWithAttacksOfSeveralKindsHasTheSameOddsAndStaysCheap()
    {
        // 12 Attacks of four kinds (a Burn-like one among them) and 8 others, an empty hand: up to 10 draws. Drawn in every order,
        // that's 4^10 paths; merged draw by draw, it's the sets drawn. The odds don't depend on the kinds.
        var burnAttack = new SimDrawCard("burn attack") { Type = SimCardType.Attack, TurnEnd = new TurnEndCard("Burn", "Burn", Amount: 2) };
        var kinds = new[] { Attack, new SimDrawCard("attack 2") { Type = SimCardType.Attack }, new SimDrawCard("attack 3") { Type = SimCardType.Attack }, burnAttack };
        var mixed = SimEngine.Run(You(Pile((kinds[0], 3), (kinds[1], 3), (kinds[2], 3), (kinds[3], 3), (Skill, 8)), hand: 1),
            Playing("Pillage", new DrawnCardAction(Count: 1) { DrawAgainIf = SimCardType.Attack }));
        SameOdds(PillageOdds(attacks: 12, others: 8, room: 10), mixed, "four kinds of Attack");
        Assert.True(mixed.Copies < 5000, $"{mixed.Copies} state copies");
    }

    [Fact]
    public void ScrapeMatchesTheTextbookOddsForEveryPile()
    {
        // Draws 4 (with an empty hand, all fit): f free cards among them with chance C(F, f) C(X, 4 - f) / C(F + X, 4), or all
        // of a smaller pile.
        for (var free = 0; free <= 7; free++)
        for (var others = 0; others <= 7; others++)
        {
            var total = free + others;
            if (total == 0) continue;
            var draws = Math.Min(4, total);
            var pile = new List<(SimDrawCard, int)>();
            if (free > 0) pile.Add((FreeCard, free));
            if (others > 0) pile.Add((Burn, others));
            var outcome = SimEngine.Run(You(Pile(pile.ToArray()), hand: 1), Playing("Scrape", new DrawnCardAction(Count: 4) { DiscardUnlessFree = true }));
            var expected = Enumerable.Range(0, draws + 1).ToDictionary(freeDrawn => freeDrawn, freeDrawn => Choose(free, freeDrawn) * Choose(others, draws - freeDrawn) / Choose(total, draws));
            SameOdds(expected.Where(entry => entry.Value > 0).ToDictionary(entry => entry.Key, entry => entry.Value), outcome, $"{free} free, {others} others");
        }
    }

    [Fact]
    public void EscapePlanMatchesTheShareOfSkills()
    {
        for (var skills = 0; skills <= 6; skills++)
        for (var others = 0; others <= 6; others++)
        {
            if (skills + others == 0) continue;
            var pile = new List<(SimDrawCard, int)>();
            if (skills > 0) pile.Add((Skill, skills));
            if (others > 0) pile.Add((Attack, others));
            var outcome = SimEngine.Run(You(Pile(pile.ToArray())), Playing("EscapePlan", EscapePlan));
            Assert.Equal((double)skills / (skills + others), DrawnChance.ChanceOfAny(DrawnChance.Distribution(outcome)!), 9);
        }
    }

    [Fact]
    public void PillageWithIterationDrawsItsExtraCardsWithoutCountingThem()
    {
        // Iteration draws 2 more on your first Status this turn: a Status stops Pillage (not an Attack), and the 2 it chains
        // aren't Pillage's (CardPileCmd.Draw returns only its own card), so they neither count nor carry it on. 1 Attack, 1 Status,
        // 2 Attacks: it draws the Status first half the time (0 Attacks counted), else the Attack, then the Status (1).
        var status = new SimDrawCard("status") { Type = SimCardType.Status, Status = true };
        var state = You(Pile((Attack, 1), (status, 1)), hand: 1);
        state.You.Powers = state.You.Powers.With(PowerId.Iteration, amount: 2, data: 0);
        state.You.Draw!.Rest.Add(new SimDrawGroup(new SimDrawCard("attack b") { Type = SimCardType.Attack }, Count: 2));
        var outcome = SimEngine.Run(state, Playing("Pillage", new DrawnCardAction(Count: 1) { DrawAgainIf = SimCardType.Attack }));
        // Attacks: 3 in 4. Draws Attacks until the Status: 0 with 1/4, 1 with 3/4 * 1/3, 2 with 3/4 * 2/3 * 1/2, 3 with 1/4.
        SameOdds(new() { [0] = 0.25, [1] = 0.25, [2] = 0.25, [3] = 0.25 }, outcome, "with Iteration");
        // Iteration's 2 follow the Status: drawn first, it leaves 1 card in the pile; any later, it empties it.
        Assert.Equal(0.25, outcome.Branches.Where(branch => branch.You.Draw!.RestCount == 1).Sum(branch => branch.Weight), 9);
        Assert.Equal(0.75, outcome.Branches.Where(branch => branch.You.Draw!.RestCount == 0).Sum(branch => branch.Weight), 9);
    }

    // ---- Hellraiser (decision #121): a Strike you draw plays itself at once

    /// <summary>A Strike as the adapter builds its auto-play: 6 damage at each enemy it could be aimed at.</summary>
    private static SimDrawCard HellStrike(params int[] targets) => new("strike") { Type = SimCardType.Attack, Strike = true, Auto = new SimHandCard
    {
        Name = "StrikeIronclad", IsAttack = true, Plays = targets.Select(target => new SimPlay
        {
            Name = "StrikeIronclad", IsAttack = true, AutoPlay = true, HandDelta = -1, TargetId = target,
            Card = new SimCardPlay(new SimCard("StrikeIronclad", SimCardType.Attack), Who.You, target, PlayIndex: 0, PlayCount: 1, AutoPlay: true),
            Body = { new AttackAction(Hits: 1, SimPlay.Target.Single, new DamageSpec(Base: 0m, SimProps.Move) { BaseOn = new Dictionary<int, decimal> { [target] = 6 }, Fixed = true }) },
        }).ToList(),
    } };

    private static SimState Hellraised(SimDrawPile pile, int hand = 1, int enemies = 1)
    {
        var state = You(pile, hand);
        for (var id = 2; id <= enemies; id++) state.Enemies.Add(new SimEnemy { Id = id, Name = "E" + id, Hp = 50, Hits = new() { 10 } });
        state.You.Powers = state.You.Powers.With(PowerId.Hellraiser, amount: 1, data: 0);
        return state;
    }

    [Fact]
    public void HellraiserPlaysADrawnStrikeAtARandomEnemy()
    {
        // A draw of 1 from a pile of only Strikes, two enemies: it plays itself at either, half each, and leaves your hand.
        var outcome = SimEngine.Run(Hellraised(Pile((HellStrike(targets: [1, 2]), 3)), hand: 3, enemies: 2),
            new SimPlay { Name = "Draw1", HandCard = 7, HandDelta = -1, Body = { new CardEventAction(CardEvent.Drawn, Count: 1) } });
        Assert.All(outcome.Branches, branch => Assert.Null(branch.Unmodelled));
        Assert.Equal(0.5, outcome.Branches.Where(branch => branch.Enemies[0].Hp == 44 && branch.Enemies[1].Hp == 50).Sum(branch => branch.Weight), 9);
        Assert.Equal(0.5, outcome.Branches.Where(branch => branch.Enemies[1].Hp == 44 && branch.Enemies[0].Hp == 50).Sum(branch => branch.Weight), 9);
        Assert.All(outcome.Branches, branch => Assert.Equal(1, branch.AutoPlayed));
    }

    [Fact]
    public void PillageWithHellraiserKeepsDrawingPastAStrikeThatPlayedItself()
    {
        // Pillage's draw is still the Strike (an Attack) after it plays itself, and your hand has the room it left: it draws on.
        // One Strike, one Skill: the Strike first (half): 1 Attack, 6 damage; the Skill first: none.
        var outcome = SimEngine.Run(Hellraised(Pile((HellStrike(targets: 1), 1), (Skill, 1))),
            Playing("Pillage", new DrawnCardAction(Count: 1) { DrawAgainIf = SimCardType.Attack }));
        Assert.All(outcome.Branches, branch => Assert.Null(branch.Unmodelled));
        SameOdds(new() { [0] = 0.5, [1] = 0.5 }, outcome, "with Hellraiser");
        Assert.Equal(0.5, outcome.Branches.Where(branch => branch.Enemies[0].Hp == 44).Sum(branch => branch.Weight), 9);
        // The Strike played: whichever came first, both cards were drawn in the Strike-first branch, one in the other.
        Assert.Equal(0.5, outcome.Branches.Where(branch => branch.You.Draw!.RestCount == 0).Sum(branch => branch.Weight), 9);
    }

    [Fact]
    public void HellraiserWithAStrikeWhosePlayIsntBuiltIsNotModelled()
    {
        var bare = new SimDrawCard("bare strike") { Type = SimCardType.Attack, Strike = true };
        var outcome = SimEngine.Run(Hellraised(Pile((bare, 2))),
            new SimPlay { Name = "Draw1", HandCard = 7, HandDelta = -1, Body = { new CardEventAction(CardEvent.Drawn, Count: 1) } });
        Assert.All(outcome.Branches, branch => Assert.NotNull(branch.Unmodelled));
    }

    // ---- the chance tab (decision #119)

    [Fact]
    public void EscapePlansTabIsTheChanceItDrawsASkill()
    {
        var outcome = SimEngine.Run(You(Pile((Skill, 1), (Attack, 3))), Playing("EscapePlan", EscapePlan));
        var distribution = DrawnChance.Distribution(outcome)!;
        Assert.Equal(0.25, DrawnChance.ChanceOfAny(distribution), 9);
    }

    [Fact]
    public void PillagesTabIsTheMostLikelyNumberOfAttacks()
    {
        // Two Attacks and two Skills: 0 Attacks 1/2, 1 Attack 1/3, 2 Attacks 1/6.
        var outcome = SimEngine.Run(You(Pile((Attack, 2), (Skill, 2))), Playing("Pillage", new DrawnCardAction(Count: 1) { DrawAgainIf = SimCardType.Attack }));
        var distribution = DrawnChance.Distribution(outcome)!;
        Assert.Equal(0.5, distribution[0], 9);
        Assert.Equal(1.0 / 3, distribution[1], 9);
        Assert.Equal(1.0 / 6, distribution[2], 9);
        Assert.Equal((0, 0.5), (DrawnChance.MostLikely(distribution).Tally, Math.Round(DrawnChance.MostLikely(distribution).Chance, 9)));
    }

    [Fact]
    public void ScrapesTabCountsTheCardsThatCostZeroAndATieGoesHigher()
    {
        // Draws 1 from one free card and one that isn't: 0 or 1 free, half each: the tie goes to 1.
        var outcome = SimEngine.Run(You(Pile((FreeCard, 1), (Burn, 1))), Playing("Scrape", new DrawnCardAction(Count: 1) { DiscardUnlessFree = true }));
        var (n, chance) = DrawnChance.MostLikely(DrawnChance.Distribution(outcome)!);
        Assert.Equal(1, n);
        Assert.Equal(0.5, chance, 9);
    }

    [Fact]
    public void APlayWithoutSuchADrawHasNoTab()
    {
        var outcome = SimEngine.Run(You(Pile((Skill, 2))), new SimPlay { Name = "Strike", HandCard = 7, HandDelta = -1 });
        Assert.Null(DrawnChance.Distribution(outcome));
    }

    [Fact]
    public void TheOddsTallyFlagsChancesFarFromWhatHappened()
    {
        var fair = new OddsTally();
        for (var i = 0; i < 100; i++) fair.Add(new SimChance(Low: 0.5, High: 0.5, Seen: true), i % 2 == 0);
        Assert.False(fair.Drifted);
        var off = new OddsTally();
        for (var i = 0; i < 100; i++) off.Add(new SimChance(Low: 0.1, High: 0.1, Seen: true), happened: true);
        Assert.True(off.Drifted);
    }
}
