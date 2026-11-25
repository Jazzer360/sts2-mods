using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// A step's condition decided on the board before the play (decision #311): only a test of what the play can't change may be (the card's own
/// values and upgrade, a pick, the fight's room); one the play can change holds as the board says on the first play when it's read before
/// the play runs any command, and is unknown anywhere else, never left out (End of Days' kill was, until it was read).
/// </summary>
[Collection(GameFightCollection.Name)]
public class BoardDecidedConditionTests
{
    /// <summary>
    /// What a play can change, by the game's members that read it (an oracle apart from ConditionReader.FixedQueries): a pile's cards, a
    /// creature's HP, block or powers, the enemies there are, your orbs.
    /// </summary>
    private static readonly HashSet<string> ChangedByPlays = new(StringComparer.Ordinal)
    {
        "GetPile", "GetCards", "Cards", "Powers", "GetPower", "GetPowerAmount", "HasPower", "CurrentHp", "Block", "IsDead", "IsAlive",
        "HittableEnemies", "Enemies", "Orbs", "OrbQueue", "Energy", "Stars",
    };

    /// <summary>Every step of every card's play whose condition the board decides, with the decision.</summary>
    private static List<(string Card, CommandStep Step, SimAdapter.StepDecisionReading Decision)> Decisions()
    {
        Assert.True(GameModels.EnsureLoaded() > 500, "the game's cards didn't load");
        var decisions = new List<(string, CommandStep, SimAdapter.StepDecisionReading)>();
        foreach (var card in ModelDb.AllCards.OrderBy(card => card.GetType().Name, StringComparer.Ordinal))
            foreach (var found in CardEffects.Find(CardPrograms.Of(card.GetType()), _ => true))
            {
                if (found.Condition.IsAlways) continue;
                var decision = SimAdapter.Decide(card, found.Command, found.Condition, new ProgramBoard(card), found);
                if (decision.Way is SimAdapter.StepDecision.Fixed or SimAdapter.StepDecision.FirstPlayOnTheBoard or SimAdapter.StepDecision.Unknown)
                    decisions.Add((card.GetType().Name, found.Command, decision));
            }
        return decisions;
    }

    [Fact]
    public void NoTestThePlayCanChangeIsDecidedOnTheBoard()
    {
        var decided = Decisions().Where(entry => entry.Decision.Way == SimAdapter.StepDecision.Fixed).ToList();
        Assert.NotEmpty(decided);
        static bool Changeable(ConditionLiteral literal) => literal.Test
            .Map(part => part is ResultValue result && result.Command.StartsWith("CardSelectCmd.", StringComparison.Ordinal) ? new ConstantValue(1) : null)
            .Parts().Any(part => part is QueryValue query && ChangedByPlays.Contains(query.Name) || part is ResultValue or UnknownValue);
        // A way a test of what's fixed closes on the board (Charge's upgrade) is closed whatever else it reads; every open way reads only what's fixed.
        var wrong = decided
            .Where(entry => entry.Decision.Condition.Terms.Any(way =>
                !way.Any(literal => !Changeable(literal) && ProgramEvaluator.Holds(ProgramCondition.Of(literal), new ProgramBoard(ModelDb.AllCards.First(card => card.GetType().Name == entry.Card)), CardPrograms.Of(GameType(entry.Card))) == false)
                && way.Any(Changeable)))
            .Select(entry => $"{entry.Card}: {entry.Step.Command.FullName} if {entry.Decision.Condition}").ToList();
        Assert.True(wrong.Count == 0, "Steps decided on the board whose test the play can change:\n" + string.Join("\n", wrong));
    }

    [Fact]
    public void ATestThePlayCanChangeIsNeverDecidedForLaterPlays()
    {
        // Each step the board decides that reads what the play can change: decided for the first play at most, never for a later one.
        foreach (var (card, step, decision) in Decisions().Where(entry => entry.Decision.Way != SimAdapter.StepDecision.Fixed))
        {
            Assert.False(string.IsNullOrEmpty(decision.Input), $"{card}: {step.Command.FullName} names no input the play can change");
            if (decision.Way == SimAdapter.StepDecision.FirstPlayOnTheBoard)
                Assert.True(ConditionReader.ReadBeforeAnyCommand(decision.Condition.Literals.First(literal =>
                        ConditionReader.ChangeableInput(literal.Test, new ConditionReader.Context(GameType(card), RuntimeCode.Read, Board: null, CardPrograms.Of(GameType(card)))) != null).Test,
                    step, new ConditionReader.Context(GameType(card), RuntimeCode.Read, Board: null, CardPrograms.Of(GameType(card)))), $"{card}: {step.Command.FullName}");
        }
    }

    private static Type GameType(string card) => ModelDb.AllCards.First(model => model.GetType().Name == card).GetType();

    [Fact]
    public void TheInputsAreReadFromTheTest()
    {
        var decisions = Decisions();
        SimAdapter.StepDecisionReading Of(string card, string command) => decisions.First(entry => entry.Card == card && entry.Step.Command.FullName == command).Decision;
        // Imitation Learning's test of your power aimed at that player is read before any command: the board's for the first play.
        Assert.Equal(SimAdapter.StepDecision.FirstPlayOnTheBoard, Of(nameof(ImitationLearning), "PowerCmd.Apply").Way);
        Assert.Equal("Powers", Of(nameof(ImitationLearning), "PowerCmd.Apply").Input);
        // A test that a count the step's action reads as it runs is above none, or that there's an enemy to hit for an attack that finds its
        // targets as it runs, is the action's own (decision #316): what's left of these steps' tests is fixed for the play (Storm of Steel's fight
        // going on), never decided on the board for a test the play can change.
        Assert.DoesNotContain(decisions, entry => entry.Card is nameof(MementoMori) or nameof(Dredge) or nameof(Optimism) or nameof(StormOfSteel) or nameof(TheMoon)
                                                  && entry.Step.Command.FullName is "DamageCmd.Attack" or "CardPileCmd.Add" or "CardPileCmd.Draw" or "CardPileCmd.AddGeneratedCardsToCombat"
                                                  && entry.Decision.Way != SimAdapter.StepDecision.Fixed);
        // The Hunt's room (a combat room for every play) is fixed for the play; its reward's test of the target's Fatal and its kill is asked as it runs.
        Assert.Equal(SimAdapter.StepDecision.Fixed, Of(nameof(TheHunt), "DamageCmd.Attack").Way);
        Assert.DoesNotContain(decisions, entry => entry.Card == nameof(TheHunt) && entry.Step.Command.FullName == "PowerCmd.Apply"); // asked as it runs: its target's Fatal and its kill
        // A pick is a card (decision #188); Mad Science's rider is stored on the card: fixed.
        Assert.Equal(SimAdapter.StepDecision.Fixed, Of(nameof(Acrobatics), "CardCmd.Discard").Way);
        Assert.Equal(SimAdapter.StepDecision.Fixed, Of(nameof(MadScience), "DamageCmd.Attack").Way);
        // Cinder's roll of a card in your hand, Ki Chant's top cards: the step's action rolls or takes them as it runs.
        Assert.DoesNotContain(decisions, entry => entry.Card is nameof(Cinder) or nameof(KiChant) && entry.Step.Command.FullName is "CardCmd.Exhaust" or "CardCmd.Discard"
                                                  && entry.Decision.Way != SimAdapter.StepDecision.Fixed);
    }

    [Fact]
    public void MementoMorisLaterPlayAttacksWithNoQuestion()
    {
        // MementoMori.OnPlay: if (HittableEnemies.Count != 0) an attack on one of them. Played twice, its second test is read after its first attack,
        // but it's the attack's own (decision #316): the attack finds its target among the hittable enemies as it runs, none with none.
        var gameFight = new GameFight();
        var enemy = gameFight.Combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, slot: null);
        gameFight.Combat.AddCreature(enemy);
        enemy.SetMaxHpInternal(100);
        enemy.SetCurrentHpInternal(100);
        for (var i = 0; i < 5; i++) gameFight.Add<DefendIronclad>(PileType.Draw);
        var card = gameFight.Add<MementoMori>();
        SimAdapter.Build(gameFight.Me, gameFight.Combat, out var ids);
        var play = SimAdapter.Play(card, gameFight.Me, gameFight.Combat, ids, ids[enemy], plays: 2);
        Assert.Empty(play.Body.OfType<FirstPlayOnly>());
        Assert.Empty(play.Body.OfType<NotModelledAction>());
        Assert.Equal(SimPlay.Target.Random, Assert.IsType<AttackAction>(Assert.Single(play.Body)).Mode);
    }

    [Fact]
    public void DredgesPickIsCappedByYourHandsRoomAsItRuns()
    {
        // Dredge.OnPlay: num = Min(Cards, MaxCardsInHand - your hand); if (num > 0) a pick of num from your discard pile into your hand. The pick's most
        // reads your hand's room as it runs (SimCount.HandRoom, its Cards the most), so its test of num is the pick's own (decision #316).
        var gameFight = new GameFight();
        var enemy = gameFight.Combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, slot: null);
        gameFight.Combat.AddCreature(enemy);
        for (var i = 0; i < 5; i++) gameFight.Add<DefendIronclad>(PileType.Discard);
        var card = gameFight.Add<Dredge>();
        SimAdapter.Build(gameFight.Me, gameFight.Combat, out var ids);
        var play = SimAdapter.Play(card, gameFight.Me, gameFight.Combat, ids, ids[enemy], plays: 2);
        var take = Assert.IsType<TakeToHandAction>(Assert.Single(play.Body));
        Assert.Equal(SimCount.HandRoom, take.CountFrom);
        Assert.Equal(card.DynamicVars.Cards.IntValue, take.Count);
    }

    [Fact]
    public void SovereignBladesBlockIsAskedOfYourParryAsItRuns()
    {
        // SovereignBlade.OnPlay: if (GetOwnerParryAmount(this) > 0m) GainBlock, after its attack; the helper returns its owner's Parry for a card in play.
        var gameFight = new GameFight();
        var enemy = gameFight.Combat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, slot: null);
        gameFight.Combat.AddCreature(enemy);
        enemy.SetMaxHpInternal(100);
        enemy.SetCurrentHpInternal(100);
        var card = gameFight.Add<SovereignBlade>();
        SimAdapter.Build(gameFight.Me, gameFight.Combat, out var ids);
        var play = SimAdapter.Play(card, gameFight.Me, gameFight.Combat, ids, ids[enemy], plays: 1);
        var guarded = Assert.Single(play.Body.OfType<WhenAction>());
        Assert.Equal(new CountCondition(SimCounted.OwnerPower, SimCompare.Greater, Than: 0) { Power = PowerId.Parry }, guarded.Condition with { At = null });
        Assert.IsType<GainBlockAction>(Assert.Single(guarded.Then));
    }
}
