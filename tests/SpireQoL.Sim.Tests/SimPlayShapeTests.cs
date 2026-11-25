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

    /// <summary>A Skill from your hand that gains 5 block a play.</summary>
    private static SimPlay BlockSkill(bool exhausts = false)
    {
        var play = FromHand("Defend", SimCardType.Skill, energy: 1, plays: 1, new GainBlockAction(Times: 1, Base: 5, SimProps.Move));
        play.Card = play.Card! with { Card = play.Card.Card with { ExhaustsSelf = exhausts } };
        return play;
    }

    [Fact]
    public void BurstPlaysYourNextSkillsAgainAStackEach()
    {
        // Burst 1: the first Skill plays twice (10 block) and uses its stack; an Attack doesn't touch it; the next Skill plays once.
        var state = Board();
        state.You.Powers = state.You.Powers.With(PowerId.Burst, amount: 1);
        var after = After(state, FreeAttack(), BlockSkill(), BlockSkill());
        Assert.Equal(15, after.You.Block);
        Assert.False(after.You.Powers.Has(PowerId.Burst));
    }

    [Fact]
    public void DuplicationOneTwoPunchSignalBoostAndKiVerseEachReplayTheirKind()
    {
        // Duplication: any card. One-Two Punch: Attacks. Signal Boost: Powers. Ki Verse: Wah or Cho cards (v0.114.0: no longer Ki). Each a stack a play.
        var duplication = Board();
        duplication.You.Powers = duplication.You.Powers.With(PowerId.Duplication, amount: 2);
        Assert.Equal(20, After(duplication, BlockSkill(), BlockSkill()).You.Block);
        var punch = Board();
        punch.You.Powers = punch.You.Powers.With(PowerId.OneTwoPunch, amount: 1);
        var punched = After(punch, BlockSkill(), FreeAttack(), FreeAttack());
        Assert.Equal(5, punched.You.Block);
        Assert.Equal(500 - 3 * 3, punched.Enemies[0].Hp);
        var boost = Board();
        boost.You.Powers = boost.You.Powers.With(PowerId.SignalBoost, amount: 1);
        Assert.Equal(2, After(boost, FeralCard()).You.Powers[PowerId.Feral]);
        var verse = Board();
        verse.You.Powers = verse.You.Powers.With(PowerId.KiVerse, amount: 1);
        var melody = BlockSkill();
        melody.Card = melody.Card! with { Card = melody.Card.Card with { Tags = new HashSet<SimCardTag> { SimCardTag.Cho } } };
        Assert.Equal(15, After(verse, BlockSkill(), melody).You.Block);
        var kiCard = BlockSkill();
        kiCard.Card = kiCard.Card! with { Card = kiCard.Card.Card with { Tags = new HashSet<SimCardTag> { SimCardTag.Ki } } };
        Assert.Equal(10, After(verse, BlockSkill(), kiCard).You.Block);
    }

    [Fact]
    public void TagTeamReplaysAnotherPlayersAttackAtItsEnemy()
    {
        // Tag Team 1 on the enemy, applied by your teammate: your Attack at it plays once more and Tag Team goes; applied by you, it doesn't.
        var state = Board();
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.TagTeam, amount: 1, data: Models.TagTeamPower.DataFor(Who.Teammate(teammateIndex: 0), amount: 1));
        var attack = FreeAttack();
        attack.Card = attack.Card! with { Card = attack.Card.Card with { Targets = SimPlay.Target.Single } };
        var after = After(state, attack, attack);
        Assert.Equal(500 - 3 * 3, after.Enemies[0].Hp);
        Assert.False(after.Enemies[0].Powers.Has(PowerId.TagTeam));
        var yours = Board();
        yours.Enemies[0].Powers = yours.Enemies[0].Powers.With(PowerId.TagTeam, amount: 1, data: Models.TagTeamPower.DataFor(Who.You, amount: 1));
        Assert.Equal(500 - 3, After(yours, attack).Enemies[0].Hp);
    }

    [Fact]
    public void EachTagTeamIsItsAppliersOwn()
    {
        // TagTeamPower is instanced (PowerInstanceType.Instanced): each copy keeps its applier (card.Owner.Creature == base.Applier: no replay)
        // and only a copy that replayed a card goes (AfterModifyingCardPlayCount: PowerCmd.Remove(this)). Yours and a teammate's on one enemy:
        // your Attack plays once more for theirs, which goes; yours stays.
        var state = Board();
        state.Enemies[0].Powers = state.Enemies[0].Powers.With(PowerId.TagTeam, amount: 2,
            data: Models.TagTeamPower.DataFor(Who.You, amount: 1) | Models.TagTeamPower.DataFor(Who.Teammate(teammateIndex: 0), amount: 1));
        var attack = FreeAttack();
        attack.Card = attack.Card! with { Card = attack.Card.Card with { Targets = SimPlay.Target.Single } };
        var after = After(state, attack);
        Assert.Equal(500 - 3 * 2, after.Enemies[0].Hp);
        Assert.Equal(1, after.Enemies[0].Powers[PowerId.TagTeam]);
        Assert.Equal(500 - 3 * 3, After(state, attack, attack).Enemies[0].Hp);
    }

    [Fact]
    public void YourOwnTagTeamDoesntReplayYourAttacks()
    {
        // The Tag Team card applies TagTeamPower with you as its applier: your next Attack at the enemy plays once, as in the game.
        var state = Board();
        var tagTeam = FreeAttack();
        tagTeam.Card = tagTeam.Card! with { Card = tagTeam.Card.Card with { Targets = SimPlay.Target.Single } };
        tagTeam.Body.Add(new ApplyPowerAction(PowerId.TagTeam, Amount: 1) { Debuff = true });
        var attack = FreeAttack();
        attack.Card = attack.Card! with { Card = attack.Card.Card with { Targets = SimPlay.Target.Single } };
        var after = After(state, tagTeam, attack);
        Assert.Equal(500 - 3 * 2, after.Enemies[0].Hp);
        Assert.Equal(1, after.Enemies[0].Powers[PowerId.TagTeam]);
    }

    [Fact]
    public void ThrowingAxePlaysTheFightsFirstCardTwice()
    {
        var state = Board();
        state.You.Relics = state.You.Relics.With(RelicId.ThrowingAxe, amount: 0, data: (int)RelicUse.Armed);
        Assert.Equal(15, After(state, BlockSkill(), BlockSkill()).You.Block);
        var used = Board();
        used.You.Relics = used.You.Relics.With(RelicId.ThrowingAxe, amount: 0, data: 0);
        used.You.Fields = used.You.Fields.With("ThrowingAxe.UsedThisCombat", amount: 1); // its own UsedThisCombat, as the adapter reads it
        Assert.Equal(10, After(used, BlockSkill(), BlockSkill()).You.Block);
    }

    [Fact]
    public void CorruptionExhaustsYourSkillsOnceTheirPlaysAreOver()
    {
        // Corruption: a played Skill goes to the exhaust pile (its exhaust sets off Feel No Pain), once however many times it plays. A Skill
        // that exhausts itself has its own exhaust already (the play's After): Corruption adds none.
        var state = Board();
        state.You.Powers = state.You.Powers.With(PowerId.Corruption, amount: 1).With(PowerId.FeelNoPain, amount: 3);
        var once = After(state, BlockSkill());
        Assert.Equal(5 + 3, once.You.Block);
        Assert.Equal(1, once.You.ExhaustPile);
        var twice = BlockSkill();
        twice.Plays = 2;
        twice.Card = twice.Card! with { PlayCount = 2 };
        Assert.Equal(10 + 3, After(state, twice).You.Block);
        Assert.Equal(0, After(state, FreeAttack()).You.ExhaustPile);
    }

    [Fact]
    public void ReboundSendsYourNextCardToYourDrawPileAStackEach()
    {
        // Rebound 1: a card bound for your discard pile goes on top of your draw pile and uses the stack; one that exhausts itself isn't.
        var state = Board();
        state.You.Powers = state.You.Powers.With(PowerId.Rebound, amount: 1);
        Assert.True(After(state, BlockSkill(exhausts: true)).You.Powers.Has(PowerId.Rebound));
        Assert.False(After(state, BlockSkill()).You.Powers.Has(PowerId.Rebound));
    }
}
