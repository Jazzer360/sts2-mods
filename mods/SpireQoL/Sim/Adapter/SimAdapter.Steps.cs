using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using SpireQoL.Procs;
using SpireQoL.Procs.Programs;
using static SpireQoL.Procs.Programs.StepValues;
using Sts2Mods.Core.Game;

namespace SpireQoL.Sim;

/// <summary>
/// A play's actions built from its program's steps (decision #193, docs/CARD_PROGRAMS.md A2): each command step of the card's code is
/// translated into the simulator's action for that command, once for every card that calls it, its values read from the step's own
/// arguments (on the board where only the game's own math gives them: damage and block terms, B1), in the order the code runs them. A
/// step on a condition is its action under that condition (decision #190): asked by the simulator as it runs when it's translated, decided
/// on the board when nothing in the play can change it, and (a gap the card scan lists) unknown where it runs when it isn't translated yet and
/// the play can change it (decision #311; the board's for a first play that reads it before any command).
/// Nothing decides on the board whether a step's action exists: its condition does, where the code puts it. It's the one translator of what
/// you play or use (decision #335): a card's OnPlay, or a potion's OnUse read as a play (SimAdapter.Potions).
/// </summary>
public static partial class SimAdapter
{
    /// <summary>
    /// The game commands a card's step is translated from (Translate), each to the simulator action standing for it. The card scan holds
    /// every command step of every card on both branches to this set or <see cref="CommandsNotTranslated"/> (StepTranslationTests).
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> CommandsTranslated = new Dictionary<string, string>
    {
        ["DamageCmd.Attack"] = "AttackAction: its damage value, hits (WithHitCount's argument), targeting and dealer from its builder",
        ["CreatureCmd.GainBlock"] = "GainBlockAction: its block value through the block pipeline, to the creature its first argument names",
        ["CreatureCmd.Damage"] = "LoseHpAction on its owner (an HP cost); DamageAction on the target (no attack); a shot's damage on the target",
        ["CreatureCmd.Kill"] = "KillOstyAction (your Osty); KillEachAction (each hittable enemy a power's own question picks: End of Days' DoomPower.DoomKill)",
        ["Hook.AfterDiedToDoom"] = "KillEachAction's tell: the listeners told the list its kill went over (DoomPower.DoomKill, after its foreach)",
        ["OstyCmd.Summon"] = "SummonAction: its amount for the player its second argument names (you, or each player its loop goes over), as OstyCmd.Summon runs (SimEngine.Summon)",
        ["CreatureCmd.Heal"] = "HealAction: its amount on you, your Osty or the player it's aimed at, up to max HP",
        ["CreatureCmd.GainMaxHp"] = "GainMaxHpAction: its amount on you or the player it's aimed at, their max HP raised, then healed by it",
        ["CreatureCmd.LoseMaxHp"] = "LoseMaxHpAction: its amount off its owner's max HP (the card's), HP above the new max lost first as the card's damage",
        ["CreatureCmd.SetCurrentHp"] = "ExecuteAction (the target, on its condition)",
        ["CreatureCmd.Stun"] = "StunTargetAction (the target: its next move the stunned move)",
        ["CreatureCmd.LoseBlock"] = "StripBlockAction (the target's block, and its Artifact when the play removes it)",
        ["PowerCmd.Apply"] = "ApplyPowerAction (or FocusAction) to whom its second argument names, its amount its third; a new copy aimed at a player as the code sets its aim on what it gave (decision #322)",
        ["PowerCmd.ModifyAmount"] = "ModifyAimedPowerAction: the holder's copy of a power aimed at a player, found as its code finds it (decision #322), by its offset; any other instance unknown where it runs",
        ["PowerCmd.Remove"] = "the target's Artifact, folded into StripBlockAction; a power of the card's owner taken off them (RemoveOwnPowerAction)",
        ["CardPileCmd.Draw"] = "CardEventAction(Drawn), or a teammate's draws, its count its own argument",
        ["CardPileCmd.DrawWithoutBlockingOnOtherPlayers"] = "the same as Draw, for each player its loop goes over",
        ["CardPileCmd.Add"] = "TakeToHandAction into your hand, TakeFromHandAction back onto your draw pile or with your whole hand",
        ["CardPileCmd.Shuffle"] = "ShuffleDrawPileAction (with the hand the play put into your draw pile)",
        ["CardPileCmd.ShuffleIfNecessary"] = "ShuffleDrawPileAction when the draw pile is empty",
        ["CardPileCmd.AutoPlayFromDrawPile"] = "AutoPlayFromDrawAction: its count, exhaust and upgrading callback",
        ["PotionCmd.TryToProcure"] = "ProcurePotionAction: a potion into the card's owner's belt while it has room, then AfterPotionProcured, when Hook.ShouldProcurePotion lets every potion it could be through on the board and none is a Fairy in a Bottle (ProcureFrom)",
        ["PlayerCmd.GainGold"] = "GainGoldAction: AfterGoldGained for the card's owner, when Hook.ModifyGoldGained leaves some of its amount on the board (GoldFrom)",
        ["CardCmd.AutoPlay"] = "AutoPlayCardsAction: the cards of your pile its card argument names (each of a list, one at random, a shuffled list's first so many), filtered as its code filters them, at a random enemy or the play's target (CardAutoPlayOf); a card you pick, then played, is unknown (decision #188)",
        ["CardPileCmd.AddGeneratedCardToCombat"] = "CardEventAction(Generated), about the card it makes when its code names it (its class and type), for its creator, one, into the pile it names (MadeCards)",
        ["CardPileCmd.AddGeneratedCardsToCombat"] = "the same, as many as its list holds: a Create's count, a card factory's count, the rounds of the loop that filled it",
        ["CardPileCmd.AddToCombatAndPreview"] = "the same, its count argument of the card it names, for the target's player",
        ["ForgeCmd.Forge"] = "the Sovereign Blade it makes when you have none (the first play's)",
        ["CardCmd.Exhaust"] = "TakeFromHandAction(Exhausted), after ExhaustFromHandAction",
        ["CardCmd.Discard"] = "TakeFromHandAction(Discarded), or the cards it looked at on your draw pile",
        ["CardCmd.Upgrade"] = "UpgradeCardsAction of your cards in the piles it goes over (an upgrade of a card the play makes, of a pick, or in an auto-play's callback: no action, UpgradeTargetOf)",
        ["CardCmd.ApplyKeyword"] = "EtherealInHandAction for a picked card made Ethereal; Retain, Sly, Exhaust or Innate given to a pick or a drawn card: no action (KeywordGivenOf)",
        ["CardCmd.ApplySingleTurnSly"] = "no action: Sly for the turn on a picked card (KeywordGivenOf)",
        ["CardCmd.ApplySingleTurnRetain"] = "no action: Retain for the turn on a picked or drawn card (KeywordGivenOf)",
        ["CardCmd.Enchant"] = "no action: an enchantment on a card the play makes, whose class acts only on its card's play (EnchantsMadeCard, EnchantmentActsOnlyOnItsPlay)",
        ["CardCmd.DiscardAndDraw"] = "TakeFromHandAction(Discarded), then the draws",
        ["CardCmd.Transform"] = "TakeFromHandAction(Generated) in your hand, or a made card in its pile",
        ["CardCmd.TransformTo"] = "the same as Transform",
        ["PlayerCmd.GainEnergy"] = "CardEventAction(EnergyGained) to the player its second argument names; GainEnergyAsHeldAction for your own energy",
        ["PlayerCmd.GainStars"] = "CardEventAction(StarsGained), for the card's owner or the player it's aimed at",
        ["OrbCmd.Channel"] = "OrbAction(Channel) of its orb, into the orbs of the player its argument names",
        ["OrbCmd.EvokeNext"] = "OrbAction(EvokeFront), keeping the orb when its dequeue argument says so; every orb twice (EvokeAllTwice)",
        ["OrbCmd.AddSlots"] = "OrbAction(Slots)",
        ["OrbCmd.Passive"] = "OrbPassiveAction: each of your orbs of the class its loop goes over, its passive with no target or at the play's target (Tesla Coil's), the other loops' rounds times (PassivesOrbOf)",
        ["OrbCmd.RemoveSlots"] = "OrbAction(Slots), negative",
        ["ArrowCmd.ShootArrowAtTarget"] = "ShootAction, the attack it's given (needing the arrow), ImpaleAction",
        ["ArrowCmd.TryShootArrow"] = "ShootAction",
        ["ArrowCmd.ImpaleTarget"] = "what its delegate runs, then ImpaleAction",
        ["ArrowCmd.GainArrowSlot"] = "GainArrowSlotsAction: an arrow slot for the card's owner, with its arrow there, each round of its loop",
        ["ArrowCmd.DislodgeArrow"] = "DislodgeAllAction (the loop around it pulls every arrow of yours out)",
        [nameof(PoisonPower) + ".Trigger"] = "ModelCommandAction: the power's own command on each enemy's instance of it, as its code runs it (ModelSpec.Commands)",
    };

    /// <summary>
    /// The game commands a card's step can call that the simulator can't follow yet, each with what isn't followed: where one runs, the play's
    /// outcome is unknown (a NotModelledAction with its reason, decision #292: `?` and a range, counted by the audits), never left out. Each is
    /// an open docs/TRACKER.md item naming it (StepTranslationTests).
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> CommandsNotTranslated = new Dictionary<string, string>
    {
    };

    /// <summary>
    /// The game commands a card's step can call that change nothing the simulator predicts, each with why (reviewed from the code they run, decision
    /// #292): no action, and no unknown.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> CommandsOutsideTheFight = new Dictionary<string, string>
    {
        ["MonsterModel.OnDieToDoom"] = "a monster's own reaction to dying of Doom (End of Days' kills): every override is its looks only (Torch Head Amalgam's torches)",
        ["CardPileCmd.RemoveFromDeck"] = "a card taken out of your deck (its DeckVersion): the deck outside the fight, not its piles",
        ["PlayerCmd.EndTurn"] = "the turn ended by the card: what follows is the turn's end, which the forecast simulates from the state after the play",
    };

    /// <summary>
    /// One play being translated (decision #335): the model whose code it is (the card played, or the potion used: its OnUse read as a play), its
    /// board, its program and what the steps have decided so far. What only a card has (its type, its preview's numbers, its replays, its own
    /// place in your piles) is absent for a potion, answered as the game answers it with no card.
    /// </summary>
    private sealed class StepPlay
    {
        /// <summary>The card played or the potion used.</summary>
        public required AbstractModel Model { get; init; }
        public required ModelProgram Program { get; init; }
        public required ProgramBoard Board { get; init; }
        public required Player Me { get; init; }
        public required ICombatState Combat { get; init; }
        public required IReadOnlyDictionary<Creature, int> Ids { get; init; }
        public required SimPlay Play { get; init; }
        /// <summary>The enemy the card or potion is aimed at (null: none).</summary>
        public Creature? Aimed { get; init; }
        /// <summary>Whose card it is (Who.You, or a teammate's auto-play); a potion's is yours.</summary>
        public int Owner { get; init; }
        /// <summary>The player it's aimed at (null, Who.None: none): a card's teammate, or the player a potion is used on (you, or a teammate it's thrown at).</summary>
        public Creature? Ally { get; init; }
        public int AllyId { get; init; } = Who.None;
        /// <summary>The creature the board's play target is (cardPlay.Target): the enemy a card is aimed at, or whomever a potion is used on.</summary>
        public Creature? BoardTarget { get; init; }
        /// <summary>The card played; null for a potion's use.</summary>
        public CardModel? Card => Model as CardModel;
        /// <summary>Its values (DynamicVars): the card's, or the potion's.</summary>
        public DynamicVarSet Vars => Card?.DynamicVars ?? ((PotionModel)Model).DynamicVars;
        /// <summary>Whom it can be aimed at: the card's targeting, or the potion's.</summary>
        public TargetType TargetType => Card?.TargetType ?? ((PotionModel)Model).TargetType;
        /// <summary>Its target is a player (a card aimed at a teammate; a potion used on you or a teammate): what lands on its target goes to them.</summary>
        public bool AimsAtPlayer => TargetType == TargetType.AnyAlly || Card == null && Who.IsPlayer(AllyId);
        /// <summary>The plays the adapter describes (decision #189: one, repeated by the simulator).</summary>
        public int Plays { get; init; } = 1;
        /// <summary>Each command step with everything it runs on (CardEffects.Find).</summary>
        public required Dictionary<CommandStep, CardEffects.Found> Found { get; init; }
        /// <summary>Steps another step's action stands for (the attack a shot is given, the Discard sharing The Hermit's pick).</summary>
        public HashSet<CommandStep> Consumed { get; } = new(ReferenceEqualityComparer.Instance);
        /// <summary>A shot and the attack it's given (ShootArrowAtTarget's builder argument).</summary>
        public Dictionary<CommandStep, CommandStep> ShotAttacks { get; } = new(ReferenceEqualityComparer.Instance);
        /// <summary>The take actions of each take's command (TakesOf), in TakeFiltersOf's order.</summary>
        public Dictionary<CommandStep, List<SimAction>> Takes { get; set; } = new(ReferenceEqualityComparer.Instance);
        /// <summary>The tests the code stores before its first command: asked at the play's start (AskAction).</summary>
        public List<SimCondition> Asks { get; } = new();
        /// <summary>A spread of the target's debuffs onto the other enemies its code makes (Misery), or null.</summary>
        public DebuffSpread? Spread { get; set; }
        public Type Type => Model.GetType();
        public string Name => Model.GetType().Name;
        /// <summary>The board as the play's target sees it (cardPlay.Target: BoardTarget).</summary>
        public ProgramBoard Aiming => Board with { Target = BoardTarget };
    }

    /// <summary>The play's body from its program's steps, in its code's order: the asks first, then each step's actions.</summary>
    private static List<SimAction> BodyFromSteps(StepPlay play)
    {
        var body = new List<SimAction>();
        Walk(play.Program.Steps, play, body, inner: false);
        var first = play.Asks.Where(condition => condition.At != null).DistinctBy(condition => condition.At).Select(condition => (SimAction)new AskAction(condition));
        // The target's debuffs a spread takes, where its code takes them: before its first command (Misery's, before its attack).
        if (play.Spread is { } spread)
            first = first.Append(play.Program.Commands.All(command => command.Offset > spread.TakenAt)
                ? new KeepTargetDebuffsAction()
                : new NotModelledAction($"{play.Name} takes its target's debuffs after its first command"));
        return first.Concat(body).ToList();
    }

    /// <summary>
    /// Each step in order: a loop's body once (its rounds are what its steps' actions count: hits, gains, channels, draws), a command through
    /// its translation under its condition. <paramref name="inner"/>: the steps of a command's delegate, guarded by their own condition only
    /// (the command's is around them).
    /// </summary>
    private static void Walk(IEnumerable<ProgramStep> steps, StepPlay play, List<SimAction> into, bool inner)
    {
        foreach (var step in steps)
            switch (step)
            {
                // A spread of the target's debuffs (Misery's loops): one action, with what it can't follow on this board.
                case LoopStep loop when play.Spread is { } spread && ReferenceEquals(loop, spread.Over):
                    into.AddRange(SpreadFrom(play));
                    break;
                // A kill of each creature of a list its code works out (End of Days' DoomPower.DoomKill: each doomed enemy), then the hook it tells.
                case LoopStep loop when KillsEachItem(loop):
                    into.Add(KillEachFrom(loop, play));
                    break;
                case LoopStep loop:
                    Walk(loop.Body, play, into, inner);
                    break;
                case CommandStep command when !play.Consumed.Contains(command):
                    var (actions, guard) = Translate(command, play);
                    if (actions.Count == 0) break;
                    Guard(command, inner ? command.Condition ?? ProgramCondition.Always : guard ?? play.Found[command].Condition, actions, play, into);
                    break;
                // Code the reader couldn't follow: what it does is unknown where it runs, never left out.
                case Procs.Programs.UnknownStep unknown:
                    into.Add(new NotModelledAction($"{play.Name}: {unknown.Reason}"));
                    break;
            }
    }

    /// <summary>
    /// The step's actions under its condition (decision #190): a WhenAction when the simulator asks it; decided on the board when it reads
    /// only what the play can't change (the card's own values, a pick, the fight's room: decision #311), left out when that's false. One with
    /// no translation yet (a gap the card scan lists) that reads what the play can change (ConditionReader.ChangeableInput) holds as the board
    /// says on the first play when the play runs no command before it's read; anywhere else the step is unknown where it runs (a
    /// NotModelledAction with its reason, then the board's answer), never left out.
    /// </summary>
    private static void Guard(CommandStep command, ProgramCondition condition, List<SimAction> actions, StepPlay play, List<SimAction> into)
    {
        var decision = Decide(play.Model, command, condition, play.Aiming, play.Found.GetValueOrDefault(command));
        switch (decision.Way)
        {
            case StepDecision.Always:
                into.AddRange(actions);
                return;
            case StepDecision.Asked:
                play.Asks.AddRange(decision.Reading.AskFirst);
                into.Add(new WhenAction(decision.Reading.Condition!, actions));
                return;
        }
        var onTheBoard = decision.Holds == false ? new List<SimAction>() : actions;
        if (decision.Way == StepDecision.Fixed)
        {
            into.AddRange(onTheBoard);
            return;
        }
        // A step that's unknown wherever it runs (a command not followed) stays unknown where the board can't tell, whatever it says.
        var unknown = actions.All(action => action is NotModelledAction) ? actions
            : onTheBoard.Prepend(new NotModelledAction($"{play.Name}: {command.Command.FullName} on a test the simulator doesn't ask, which the play can change ({decision.Input})")).ToList();
        if (decision.Way == StepDecision.FirstPlayOnTheBoard)
            into.Add(new FirstPlayOnly(onTheBoard) { LaterPlays = unknown });
        else
            into.AddRange(unknown);
    }

    /// <summary>How a step's condition is taken for the play (Decide, decision #311).</summary>
    internal enum StepDecision
    {
        /// <summary>No condition: the step runs.</summary>
        Always,
        /// <summary>The simulator asks it as the step runs (a WhenAction).</summary>
        Asked,
        /// <summary>Decided on the board: it reads only what the play can't change.</summary>
        Fixed,
        /// <summary>Decided on the board for the first play, read before the play runs any command; unknown on a replay's later plays.</summary>
        FirstPlayOnTheBoard,
        /// <summary>Unknown where it runs: the play can change what it reads before it's read.</summary>
        Unknown,
    }

    /// <summary>A step's condition as the play takes it (its own tests of a roll or a take it acts on left out): how, whether it holds on the board, and the input the play can change.</summary>
    internal sealed record StepDecisionReading(StepDecision Way, ProgramCondition Condition, ConditionReading Reading, bool? Holds = null, string? Input = null);

    /// <summary>Whom a card or a potion can be aimed at (its TargetType).</summary>
    private static TargetType TargetTypeOf(AbstractModel model) => model switch
    {
        CardModel card => card.TargetType,
        PotionModel potion => potion.TargetType,
        _ => TargetType.None,
    };

    /// <summary>
    /// How the step's condition is taken (decision #311): asked by the simulator when it's translated; otherwise decided on the board, which only
    /// a test of what the play can't change may be (ConditionReader.ChangeableInput: the card's own values and upgrade, a pick, the fight's
    /// room...). One the play can change is decided on the board for the first play when it's read before the play runs any command, and
    /// unknown anywhere else. Before that, where the board would decide it, a test that the roll the step acts on found something, or that the
    /// cards a take takes from are there, is the step's action's own: it rolls, or takes the cards there are, as it runs; so is (decision #316)
    /// a test that a count the action reads as it runs is above none (TestsOwnCount: your hand's room, the cards a take took), or that there's
    /// an enemy to hit for an attack that finds its targets among them as it runs (TestsOwnTargets).
    /// </summary>
    internal static StepDecisionReading Decide(AbstractModel model, CommandStep command, ProgramCondition condition, ProgramBoard board, CardEffects.Found? found)
    {
        var type = model.GetType();
        var reading = PlayConditions.Reading(model, condition);
        if (reading.Way is ConditionWay.Decided or ConditionWay.NotRead)
        {
            var take = CardEffects.ExhaustsOfOthers(type).Concat(CardEffects.Discards(type)).Concat(CardEffects.Transforms(type))
                .Concat(CardEffects.TakesIntoHandOf(type)).Any(taken => ReferenceEquals(taken.Command, command));
            var takenFrom = command.Values.Concat(found?.Loops.Select(loop => loop.Items).OfType<ProgramValue>() ?? Array.Empty<ProgramValue>()).ToList();
            var counts = CountsReadAsItRuns(type, command);
            var withoutOwn = Without(condition, literal => TestsOwnRoll(literal, command) || TestsOwnTake(literal, takenFrom, take)
                                                           || TestsOwnCount(literal, counts) || TestsOwnTargets(literal, command, TargetTypeOf(model)));
            if (!ReferenceEquals(withoutOwn, condition)) reading = PlayConditions.Reading(model, condition = withoutOwn);
        }
        var holds = reading.Way switch
        {
            ConditionWay.Simulated => (bool?)null,
            ConditionWay.Decided => reading.Holds,
            ConditionWay.NotRead => ProgramEvaluator.Holds(condition, board, CardPrograms.Of(type)),
            _ => true,
        };
        if (reading.Way == ConditionWay.Always) return new StepDecisionReading(StepDecision.Always, condition, reading);
        if (reading.Way == ConditionWay.Simulated) return new StepDecisionReading(StepDecision.Asked, condition, reading);
        if (PlayConditions.ChangeableInput(model, command, condition, board) is not { } changeable) return new StepDecisionReading(StepDecision.Fixed, condition, reading, holds);
        return new StepDecisionReading(changeable.BeforeAnyCommand ? StepDecision.FirstPlayOnTheBoard : StepDecision.Unknown, condition, reading, holds, changeable.Input);
    }

    /// <summary>
    /// The counts the step's own action reads as it runs (decision #316): a draw's, a make's (its own or the bound of the loop that fills
    /// it), a take into your hand's (a pick's most, a roll's count), each one that reads your hand's room or the cards a take of the play
    /// took (SimCount.HandRoom, SimCount.CardsTaken), which the action counts on the state as it runs.
    /// </summary>
    private static List<ProgramValue> CountsReadAsItRuns(Type type, CommandStep command)
    {
        var counts = new List<ProgramValue>();
        foreach (var draw in CardEffects.DrawsForYou(type))
            if (ReferenceEquals(draw.Command, command)) counts.Add(draw.Count);
        foreach (var make in MadeCards.Of(type))
            if (ReferenceEquals(make.Found?.Command, command) && make.CountValue is { } made) counts.Add(made);
        foreach (var intoHand in CardEffects.TakesIntoHandOf(type))
            if (ReferenceEquals(intoHand.Command, command) && (intoHand.Way == CardEffects.TakeWay.Picked ? intoHand.PerPick : intoHand.RollCount) is { } taken) counts.Add(taken);
        if (counts.Count == 0) return counts;
        var takes = CardEffects.ExhaustsOfOthers(type).Concat(CardEffects.Discards(type)).ToList();
        return counts.Where(count => CardEffects.ReadsHandRoom(count) || takes.Any(take => CardEffects.CountsTaken(count, take))).ToList();
    }

    /// <summary>
    /// The test is that a count the step's action reads as it runs is above none (The Moon's room, Storm of Steel's hand, Optimism's Sadness,
    /// Dredge's pick: <paramref name="counts"/>, CountsReadAsItRuns): the action counts it on the state as it runs, and with none it does
    /// nothing, as the command given none does (decision #316).
    /// </summary>
    private static bool TestsOwnCount(ConditionLiteral literal, IReadOnlyList<ProgramValue> counts)
    {
        if (counts.Count == 0) return false;
        var (tested, above) = literal.Test switch
        {
            CompareValue { Op: Comparison.NotEqual or Comparison.Greater, Right: ConstantValue { Value: 0 } } some => (some.Left, literal.Holds),
            CompareValue { Op: Comparison.Equal or Comparison.LessOrEqual, Right: ConstantValue { Value: 0 } } none => (none.Left, !literal.Holds),
            var truthy => (truthy, literal.Holds),
        };
        if (!above) return false;
        var text = Unstored(tested)!.ToString();
        return counts.Any(count => Unstored(count)!.ToString() == text);
    }

    /// <summary>
    /// The test is that there's an enemy to hit (HittableEnemies' Count, Any: Memento Mori's), on an attack whose targets its action finds
    /// among the hittable enemies as it runs (every one, or a random one: ModeOf), which with none hits no one (decision #316).
    /// </summary>
    private static bool TestsOwnTargets(ConditionLiteral literal, CommandStep command, TargetType targeting)
    {
        if (command.Command.FullName != "DamageCmd.Attack" || ModeOf(command, targeting) is not (SimPlay.Target.Random or SimPlay.Target.All or SimPlay.Target.LeastHp)) return false;
        var (counted, any) = literal.Test switch
        {
            CompareValue { Op: Comparison.NotEqual or Comparison.Greater, Right: ConstantValue { Value: 0 } } some => (some.Left, literal.Holds),
            CompareValue { Op: Comparison.Equal or Comparison.LessOrEqual, Right: ConstantValue { Value: 0 } } none => (none.Left, !literal.Holds),
            var truthy => (truthy, literal.Holds),
        };
        var collection = Unstored(counted) switch
        {
            QueryValue { Name: "Count", IsProperty: true, Of: { } of } => of,
            QueryValue { Type: "Enumerable", Name: "Count" or "Any", Arguments: [var source] } => source,
            _ => null,
        };
        return any && collection != null && Unstored(collection) is QueryValue { Name: "HittableEnemies", IsProperty: true };
    }

    /// <summary>
    /// A command step's actions (CommandsTranslated), and the condition they run on when it isn't the step's own (a take's: its tests on the
    /// card it takes are the take's filter; an evoke's test of your orbs is what the evoke does with none; a shuffle's test of your draw pile
    /// is its own).
    /// </summary>
    private static (List<SimAction> Actions, ProgramCondition? Guard) Translate(CommandStep command, StepPlay play)
    {
        var found = play.Found[command];
        var none = (new List<SimAction>(), (ProgramCondition?)null);
        // A shape of a translated command its translation doesn't follow (a creature other than the one its case reads): unknown where it runs.
        if (ShapeNotFollowed(command, play.Program) is { } shape) return (new List<SimAction> { new NotModelledAction($"{command.Command.FullName}: {shape}") }, null);
        // A potion thrown at a teammate acting on their cards: their piles aren't simulated (ActsOnCards).
        if (play.Card == null && Who.TeammateIndex(play.AllyId) >= 0 && ActsOnCards(command)) return (Unknown(play, $"{command.Command.FullName} on a teammate's cards: not followed"), null);
        switch (command.Command.DeclaringType, command.Command.Name)
        {
            case ("DamageCmd", "Attack"):
                return play.ShotAttacks.ContainsValue(command) ? none : (AttacksFrom(found, play, inShot: false), null);
            case ("ArrowCmd", "ShootArrowAtTarget"):
                return (ShotFrom(command, play), null);
            case ("ArrowCmd", "TryShootArrow"):
                return (new List<SimAction> { new ShootAction() }, null);
            case ("ArrowCmd", "ImpaleTarget"):
            {
                var actions = new List<SimAction>();
                Walk(command.Inner, play, actions, inner: true);
                actions.Add(new ImpaleAction());
                return (actions, null);
            }
            case ("ArrowCmd", "GainArrowSlot"):
                return RoundsOf(found, play) is var slots and > 0 ? (new List<SimAction> { new GainArrowSlotsAction(slots) }, null) : none;
            case ("ArrowCmd", "DislodgeArrow"):
                return (new List<SimAction> { new DislodgeAllAction() }, null);
            // A block only for the card its draw gave (Escape Plan's Skill): the draw's action asks it (UseDrawnCard moves the block into it).
            case ("CreatureCmd", "GainBlock") when DrawnCardReaderOf(play.Type) is { BlockIf: true }:
                return (BlocksFrom(found, play, times: null), Without(found.Condition, literal => literal.Test.Parts().Any(part => part is ResultValue { Command: "CardPileCmd.Draw" })));
            case ("CreatureCmd", "GainBlock"):
                return (BlocksFrom(found, play, times: null), null);
            case ("CreatureCmd", "Damage"):
                return (DamageFrom(found, play), null);
            case ("CreatureCmd", "Kill"):
                return (new List<SimAction> { new KillOstyAction() }, null);
            case ("OstyCmd", "Summon"):
                return (SummonsFrom(found, play), null);
            case ("CreatureCmd", "GainMaxHp"):
                return (MaxHpFrom(found, play), null);
            case ("CreatureCmd", "LoseMaxHp"):
                return (MaxHpLossFrom(found, play), null);
            case ("CreatureCmd", "Heal"):
                return (HealsFrom(found, play), null);
            case ("CreatureCmd", "SetCurrentHp"):
                return (new List<SimAction> { new ExecuteAction(ThresholdOf(found, play)) }, null);
            case ("CreatureCmd", "Stun"):
                return (new List<SimAction> { new StunTargetAction() }, null);
            case ("CreatureCmd", "LoseBlock"):
                return (new List<SimAction> { new StripBlockAction(RemoveArtifact: play.Program.Commands.Any(other => RemovesTargetsArtifact(other, play.Program))) }, null);
            // The target's Artifact after its block (Expose): folded into the strip above.
            case ("PowerCmd", "Remove") when RemovesTargetsArtifact(command, play.Program):
                return none;
            // A power of the card's owner taken off (Rain Shot's Aim and Quietus after its shot), its AfterRemoved told.
            case ("PowerCmd", "Remove"):
                return (new List<SimAction> { new RemoveOwnPowerAction(PowerId.Of(command.Command.GenericArguments[0])) }, null);
            case ("PowerCmd", "Apply"):
                return (PowersFrom(found, play), null);
            case ("PowerCmd", "ModifyAmount"):
                return (new List<SimAction> { AimedModifyFrom(found, play) }, null);
            // A power's own command the card calls on each enemy's instance of it (Outbreak: PoisonPower.Trigger), read from the power's code.
            // Its tests of the enemy its loop is at are the action's own: it goes over each enemy's instance of the power.
            case (var power, var method) when SimModels.Get(power) is ReadModel read && read.HasCommand(method):
                return (new List<SimAction> { new ModelCommandAction(power, method) }, Without(found.Condition, literal => literal.Test.Parts().Any(part => part is NamedValue { Name: "each" })));
            case ("CardPileCmd", "Draw" or "DrawWithoutBlockingOnOtherPlayers"):
                return (DrawsFrom(found, play), null);
            case ("PlayerCmd", "GainEnergy"):
                return (EnergyFrom(found, play), null);
            case ("PlayerCmd", "GainStars"):
                return (Argument(command, 1) != "cardPlay.Target.Player" ? new List<SimAction> { new CardEventAction(CardEvent.StarsGained, Count: 1) }
                    // The player it's aimed at (a potion used on you or thrown at a teammate: Star Potion's target.Player); none with no player to aim at.
                    : Who.IsPlayer(play.AllyId) ? new List<SimAction> { new CardEventAction(CardEvent.StarsGained, Count: 1) { Player = play.AllyId == Who.You ? null : play.AllyId } } : new List<SimAction>(), null);
            case ("OrbCmd", "Channel" or "EvokeNext" or "AddSlots" or "RemoveSlots"):
                return OrbsFrom(found, play);
            case ("CardCmd", "Exhaust" or "Discard" or "Transform" or "TransformTo"):
                return TakesFrom(found, play);
            case ("CardCmd", "DiscardAndDraw"):
            {
                var (taken, guard) = TakesFrom(found, play);
                return (taken.Concat(DrawsFrom(found, play)).ToList(), guard);
            }
            case ("CardPileCmd", "Add"):
                return AddsFrom(found, play);
            case ("CardPileCmd", "Shuffle" or "ShuffleIfNecessary"):
                return ShuffleFrom(found, play);
            case ("PotionCmd", "TryToProcure"):
                return ProcureFrom(found, play);
            case ("PlayerCmd", "GainGold"):
                return GoldFrom(found, play);
            case ("CardCmd", "AutoPlay"):
                return AutoPlayCardsFrom(found, play);
            case ("CardPileCmd", "AutoPlayFromDrawPile"):
                return (AutoPlayFrom(found, play), null);
            // The cards of yours in some piles upgraded: what the simulator reads of them later (UpgradeCardsAction). Its tests of the card its loop
            // is at (Apotheosis': not the card played, one that can be upgraded) are the command's own: it goes over every card there, and upgrades
            // only one that can be (its !IsUpgradable skip). A card the play makes, a pick, a card an auto-play's callback upgrades: no action.
            // A keyword given to a pick or to a card the play drew: Ethereal on a pick exhausts it as the turn ends; the rest change nothing this turn
            // (KeywordGivenOf). Its test that the pick found a card is the action's own.
            case ("CardCmd", "ApplyKeyword" or "ApplySingleTurnSly" or "ApplySingleTurnRetain"):
                return KeywordGivenOf(command, play.Program) == CardKeyword.Ethereal
                    ? (new List<SimAction> { new EtherealInHandAction() }, Without(found.Condition, literal => literal.Test.Parts().Any(part => part is ResultValue { Command: var picked } && picked.StartsWith("CardSelectCmd.", StringComparison.Ordinal))))
                    : none;
            // An enchantment on a card the play makes (Blade of Ink's Inky on its Shivs): the made card is followed by its class, type and tags, and an
            // enchantment that acts only on its card's play acts when it's played, a later decision. One that listens elsewhere (a draw, the flush) isn't followed.
            // Each of your orbs of a class set off (Darkness' Dark orbs): their passive, as many times as the loops round it go (its loop over the orbs is the
            // action's own: it goes over them as they are then).
            case ("OrbCmd", "Passive"):
            {
                if (PassivesOrbOf(command, play.Program) is not { } orbKind) return none;
                var times = RoundsOf(found, play, skip: OverOwnersOrbs);
                return times > 0 ? (new List<SimAction> { new OrbPassiveAction(orbKind, times) { AtTarget = Argument(command, 2) == "cardPlay.Target" } }, null) : none;
            }
            case ("CardCmd", "Enchant"):
                return command.Command.GenericArguments is [var enchantment] && EnchantmentActsOnlyOnItsPlay(enchantment)
                    ? none : (Unknown(play, $"an enchantment given to a card it makes that acts outside its card's play ({string.Join(", ", command.Command.GenericArguments)}): not followed"), null);
            case ("CardCmd", "Upgrade"):
                if (UpgradeTargetOf(command, play.Program) is not { Target: UpgradeTarget.Piles, Piles: var piles }) return none;
                // A card's upgrade of the piles of the teammate it's aimed at: their piles aren't simulated.
                if (play.Card != null && UpgradesTargetsPiles(command, play.Program)) return (Unknown(play, "CardCmd.Upgrade of a teammate's cards: not followed"), null);
                return (new List<SimAction> { new UpgradeCardsAction(piles, play.Name) { Random = UpgradesRandomCards(command, play.Program) } }, Without(found.Condition, literal => literal.Test.Parts().Any(part => part is NamedValue { Name: "each" })));
            case ("CardPileCmd", "AddGeneratedCardToCombat" or "AddGeneratedCardsToCombat" or "AddToCombatAndPreview"):
            case ("ForgeCmd", "Forge"):
                return (MadeFrom(found, play), null);
        }
        // A command the simulator can't follow yet: the play's outcome is unknown where it runs (decision #292), never left out.
        return Untranslated(command.Command.FullName) is { } unknown ? (new List<SimAction> { unknown }, null) : none;
    }

    /// <summary>
    /// What a command step no case of Translate took stands for (decision #300): nothing only for a command reviewed as changing nothing the
    /// simulator predicts (CommandsOutsideTheFight); a named gap's reason for one not followed yet; for any other (a power's own command its
    /// model can't run, a command a game update added), an unknown with why. Never nothing for a command that can change the fight.
    /// </summary>
    internal static SimAction? Untranslated(string command) =>
        CommandsOutsideTheFight.ContainsKey(command) ? null
        : CommandsNotTranslated.TryGetValue(command, out var notFollowed) ? new NotModelledAction($"{command}: {notFollowed}")
        : new NotModelledAction($"{command}: {ShapesNotFollowed.GetValueOrDefault(command, "a use of it the simulator doesn't follow")}");

    /// <summary>
    /// The translated commands whose translation reads one shape of them (whom an argument names), each with what isn't followed in any other
    /// (decision #300): a step in another shape is unknown where it runs (ShapeNotFollowed), never left out. Each is an open docs/TRACKER.md item
    /// naming it (StepTranslationTests), and the card scan lists every card's step in a shape not followed on both branches.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> ShapesNotFollowed = new Dictionary<string, string>
    {
        ["ArrowCmd.DislodgeArrow"] = "an arrow pulled out of a creature other than the card's target: not followed",
        ["CardCmd.Upgrade"] = "a card upgraded that's neither one the play makes, a pick, nor one of yours in a pile: not followed",
        ["CardCmd.ApplyKeyword"] = "a keyword given to a card other than one you pick or the play drew, or one other than Ethereal, Retain, Sly, Exhaust or Innate: not followed",
        ["CardCmd.ApplySingleTurnSly"] = "Sly given for the turn to a card other than one you pick: not followed",
        ["CardCmd.ApplySingleTurnRetain"] = "Retain given for the turn to a card other than one you pick or the play drew: not followed",
        ["CardCmd.Enchant"] = "an enchantment given to a card other than one the play makes: not followed",
        ["ArrowCmd.GainArrowSlot"] = "an arrow slot gained by a player other than the card's owner: not followed",
        ["OrbCmd.Passive"] = "an orb's passive set off at a creature other than the play's target, or of orbs other than each of yours of a class its code names (`is` or OfType): not followed",
        ["CreatureCmd.Kill"] = "a creature other than your Osty killed: not followed",
        ["CreatureCmd.SetCurrentHp"] = "the HP of a creature other than the card's target set: not followed",
        ["CreatureCmd.Stun"] = "a creature other than the card's target stunned: not followed",
        ["CreatureCmd.LoseBlock"] = "block taken from a creature other than the card's target: not followed",
        ["PlayerCmd.GainStars"] = "stars gained by a player other than the card's owner or its target's player: not followed",
        ["PowerCmd.Remove"] = "a power taken off a creature other than the card's owner (bar the target's Artifact with its block): not followed",
        ["DamageCmd.Attack"] = "an attack aimed at a creature its code picks by an order other than the enemy you can hit with the least HP: not followed",
    };

    /// <summary>
    /// Why a translated command's step is in a shape its translation doesn't read (<see cref="ShapesNotFollowed"/>), or null when it reads it.
    /// Pure on the step's code, so the card scan asks it of every card on both branches (StepTranslationTests).
    /// </summary>
    internal static string? ShapeNotFollowed(CommandStep command, ModelProgram program)
    {
        var followed = command.Command.FullName switch
        {
            "ArrowCmd.DislodgeArrow" or "CreatureCmd.SetCurrentHp" or "CreatureCmd.Stun" => Argument(command, 0) == "cardPlay.Target",
            "CreatureCmd.Kill" => Argument(command, 0) == "Owner.Osty" || program.All().OfType<LoopStep>().Any(loop => KillsEachItem(loop) && loop.Body.Contains(command)),
            "CreatureCmd.LoseBlock" => Argument(command, 1) == "cardPlay.Target",
            "CardCmd.Upgrade" => UpgradeTargetOf(command, program) != null,
            "CardCmd.Enchant" => EnchantsMadeCard(command, program),
            "OrbCmd.Passive" => PassivesOrbOf(command, program) != null,
            "ArrowCmd.GainArrowSlot" => IsOwnerPlayer(ValueAt(command, 0)),
            "CardCmd.ApplyKeyword" or "CardCmd.ApplySingleTurnSly" or "CardCmd.ApplySingleTurnRetain" => KeywordGivenOf(command, program) != null,
            "PlayerCmd.GainStars" => Argument(command, 1) is "Owner" or "cardPlay.Target.Player",
            "PowerCmd.Remove" => RemovesTargetsArtifact(command, program) || command.Command.GenericArguments.Count == 1 && IsYou(ValueAt(command, 0)),
            // Its Targeting's creature: one its code picks by an order is read only as the least-HP enemy (SimPlay.Target.LeastHp).
            "DamageCmd.Attack" => command.TargetValue is not { } aimedAt || !PicksByOrder(aimedAt) || IsLeastHpEnemy(aimedAt),
            _ => true,
        };
        return followed ? null : ShapesNotFollowed[command.Command.FullName];
    }

    /// <summary>
    /// The keyword a CardCmd.ApplyKeyword, ApplySingleTurnSly or ApplySingleTurnRetain step gives, when the simulator follows it (decision #348): to a card you
    /// pick (a CardSelectCmd's result) or one the play drew (CardPileCmd.Draw's result: Expertise's). Ethereal on a pick makes it exhaust as the turn ends
    /// (EtherealInHandAction). The others change nothing this turn: Retain keeps a card from the flush as the turn ends, which only moves cards to the discard
    /// pile (CardPileCmd.Add: no discard hooks; an Ethereal card exhausts whether retained or not); Sly acts as its card is discarded (CardCmd.Discard), which
    /// the flush isn't, and which card is Sly is a pick (decision #188); Exhaust and Innate act when the card is played or drawn next. Null for any other
    /// keyword or card. Pure on the step's code.
    /// </summary>
    internal static CardKeyword? KeywordGivenOf(CommandStep command, ModelProgram program)
    {
        var keyword = command.Command.Name switch
        {
            "ApplySingleTurnSly" => CardKeyword.Sly,
            "ApplySingleTurnRetain" => CardKeyword.Retain,
            _ => ValueAt(command, 1) switch
            {
                ConstantValue { Value: var given } => (CardKeyword?)given,
                ArrayValue { Items: [ConstantValue { Value: var given }] } => (CardKeyword)given,
                _ => null,
            },
        };
        if (keyword is not { } known || UpgradedValue(command, program) is not { } card) return null;
        var parts = card.Parts().ToList();
        var picked = parts.Any(part => part is ResultValue { Command: var pick } && pick.StartsWith("CardSelectCmd.", StringComparison.Ordinal));
        var drawn = parts.Any(part => part is ResultValue { Command: "CardPileCmd.Draw" });
        return known switch
        {
            CardKeyword.Ethereal or CardKeyword.Sly when picked => known,
            CardKeyword.Retain or CardKeyword.Exhaust or CardKeyword.Innate when picked || drawn => known,
            _ => null,
        };
    }

    /// <summary>
    /// The class of orb an OrbCmd.Passive step sets off, when the simulator follows it (decisions #214, #367): with no target (its third argument null: the
    /// orb's passive as its turn-end trigger runs it) or at the play's target, on each of the owner's orbs of a class (a foreach over OrbQueue.Orbs filtered by
    /// `orb is X`, Darkness', or by OfType&lt;X&gt;, Tesla Coil's). Null for any other. Pure on the step's code.
    /// </summary>
    internal static OrbKind? PassivesOrbOf(CommandStep command, ModelProgram program)
    {
        if (Argument(command, 2) is not ("null" or "cardPlay.Target") || ValueAt(command, 1) is not NamedValue { Name: "each" }) return null;
        if (program.All().OfType<LoopStep>().LastOrDefault(loop => OverOwnersOrbs(loop) && new ModelProgram("", loop.Body).All().Contains(command)) is not { Items: { } items }) return null;
        var queries = StepValues.Unstored(items)!.Parts().OfType<QueryValue>().ToList();
        var classes = queries.Where(part => part.Name == "is").Select(part => part.Type)
            .Concat(queries.Where(part => part is { Type: "Enumerable", Name: "OfType", GenericArguments: [_] }).Select(part => part.GenericArguments[0]))
            .Distinct().ToList();
        return classes is [var orbClass] ? HookReader.OrbKindOf(orbClass) : null;
    }

    /// <summary>A foreach over the card's owner's orbs (base.Owner.PlayerCombatState.OrbQueue.Orbs, filtered or not).</summary>
    private static bool OverOwnersOrbs(LoopStep loop) =>
        loop.Over.StartsWith("each of", StringComparison.Ordinal) && loop.Items is { } items
        && StepValues.Unstored(items)!.Parts().Any(part => part is QueryValue { Name: "Orbs", Of: QueryValue { Name: "OrbQueue", Of: QueryValue { Name: "PlayerCombatState", Of: var whose } } } && IsOwner(whose));

    /// <summary>A CardCmd.Enchant step enchants a card the play makes (decision #349: MakesCards on its card argument). Pure on the step's code.</summary>
    internal static bool EnchantsMadeCard(CommandStep command, ModelProgram program) => UpgradedValue(command, program) is { } card && MakesCards(card.Parts().ToList());

    /// <summary>
    /// The members of an enchantment that act only on its own card (EnchantmentModel): its play (OnPlay, its card's damage, block and play count), its
    /// setting up as it's given (OnEnchant, RecalculateValues), and which cards it can go on.
    /// </summary>
    private static readonly HashSet<string> OnItsCardOnly = new(StringComparer.Ordinal)
    {
        "OnPlay", "OnEnchant", "RecalculateValues", "CanEnchant", "CanEnchantCardType", "EnchantBlockAdditive", "EnchantBlockMultiplicative",
        "EnchantDamageAdditive", "EnchantDamageMultiplicative", "EnchantPlayCount",
    };

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> OnlyOnItsPlay = new(StringComparer.Ordinal);

    /// <summary>
    /// The enchantment class acts only on its own card's play (decision #349): its code overrides no method of the game's models but the ones on its card
    /// (OnItsCardOnly) and its properties, so it does nothing while its card sits in a pile (Inky's Weak on its card's play). False for one that answers a
    /// hook elsewhere (a draw, the flush, the shuffle: Slither's AfterCardDrawn, Slumbering Essence's BeforeFlush), or a class it can't find.
    /// </summary>
    internal static bool EnchantmentActsOnlyOnItsPlay(string enchantment) => OnlyOnItsPlay.GetOrAdd(enchantment, name =>
    {
        var type = typeof(EnchantmentModel).Assembly.GetTypes().FirstOrDefault(candidate => candidate.Name == name && typeof(EnchantmentModel).IsAssignableFrom(candidate));
        if (type == null) return false;
        const System.Reflection.BindingFlags declared = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
        return type.GetMethods(declared).Where(method => method.IsVirtual && !method.IsSpecialName && method.GetBaseDefinition().DeclaringType is { } baseType && baseType != type && typeof(AbstractModel).IsAssignableFrom(baseType))
            .All(method => OnItsCardOnly.Contains(method.Name));
    });

    /// <summary>What a card's CardCmd.Upgrade upgrades (UpgradeTargetOf).</summary>
    internal enum UpgradeTarget
    {
        /// <summary>
        /// A card the play makes (a CreateCard, a card factory's, a Soul.Create's, Shiv.CreateInHand's, a transform's result): the simulator follows a
        /// made card by its class, type and tags (CardEventAction.Card), none of which an upgrade changes; its numbers are read when it's played.
        /// </summary>
        MadeCard,
        /// <summary>A card you pick (CardSelectCmd's result): a card, never a particular one (decision #188), so nothing that depends on which.</summary>
        Picked,
        /// <summary>The card an auto-play from your draw pile plays, upgraded first in its callback (The Devil+): AutoPlayFromDrawAction.Upgrade.</summary>
        AutoPlayed,
        /// <summary>Every card of yours in some piles (UpgradedPiles), or random ones there: UpgradeCardsAction.</summary>
        Piles,
    }

    /// <summary>
    /// The player the play is aimed at (cardPlay.Target.Player; a potion's OnUse target.Player, read as a play: Blessing of the Forge's hand): a card's
    /// is a teammate, whose piles aren't simulated; a potion's is you, or a teammate it's thrown at, unknown before any translation (ActsOnCards).
    /// </summary>
    private static bool IsTargetsPlayer(ProgramValue? whose) => StepValues.Unstored(whose)?.ToString() is "cardPlay.Target.Player" or "target.Player";

    /// <summary>The piles an upgrade goes over are the player its play is aimed at's (IsTargetsPlayer).</summary>
    private static bool UpgradesTargetsPiles(CommandStep command, ModelProgram program) =>
        UpgradedValue(command, program)?.Parts().Any(part => part switch
        {
            QueryValue { Name: "GetPile", Arguments: [_, var whose] } => IsTargetsPlayer(whose),
            QueryValue { Name: "AllCards", Of: QueryValue { Name: "PlayerCombatState", Of: var whose } } => IsTargetsPlayer(whose),
            _ => false,
        }) == true;

    /// <summary>The card argument of a command on a card (an upgrade, a keyword): a loop's item read as the collection its loop goes over.</summary>
    private static ProgramValue? UpgradedValue(CommandStep command, ModelProgram program)
    {
        var upgraded = ValueAt(command, 0);
        return upgraded is NamedValue { Name: "each" }
            ? program.All().OfType<LoopStep>().LastOrDefault(loop => loop.Items != null && new ModelProgram("", loop.Body).All().Contains(command))?.Items is { } items ? Unstored(items) : null
            : upgraded;
    }

    private static readonly HashSet<string> CardMakers = new(StringComparer.Ordinal) { "CreateCard", "CreateClone", "CreateCloneForPlayer", "GetForCombat", "GetDistinctForCombat" };

    private static readonly HashSet<string> MakingCommands = new(StringComparer.Ordinal) { "Shiv.CreateInHand", "CardCmd.Transform", "CardCmd.TransformTo" };

    /// <summary>
    /// What a CardCmd.Upgrade step upgrades, read from its card argument (a loop's item read as the collection the loop goes over): a pick, the card
    /// played off your draw pile in an auto-play's callback, your cards in your piles (GetPile of yours: your hand, draw, discard or exhaust pile; your
    /// PlayerCombatState.AllCards: every one), or a card the play makes. Null for anything else (ShapesNotFollowed). Pure on the step's code.
    /// </summary>
    internal static (UpgradeTarget Target, UpgradedPiles Piles)? UpgradeTargetOf(CommandStep command, ModelProgram program)
    {
        if (program.All().OfType<CommandStep>().Any(other => other.Command.FullName == "CardPileCmd.AutoPlayFromDrawPile" && other.Inner.Contains(command)))
            return (UpgradeTarget.AutoPlayed, UpgradedPiles.None);
        // The card its loop then auto-plays, upgraded first (Knife Trap+'s Shivs): AutoPlayCardsAction.Upgraded.
        if (UpgradedByAutoPlay(command, program) != null) return (UpgradeTarget.AutoPlayed, UpgradedPiles.None);
        if (UpgradedValue(command, program) is not { } upgraded) return null;
        var parts = upgraded.Parts().ToList();
        if (parts.Any(part => part is ResultValue { Command: var picked } && picked.StartsWith("CardSelectCmd.", StringComparison.Ordinal))) return (UpgradeTarget.Picked, UpgradedPiles.None);
        // A card the play makes, before the piles: a count its maker is given can read one (Stoke's cards for the hand it exhausted).
        if (MakesCards(parts)) return (UpgradeTarget.MadeCard, UpgradedPiles.None);
        var piles = UpgradedPiles.None;
        foreach (var part in parts)
            piles |= part switch
            {
                QueryValue { Name: "AllCards", Of: QueryValue { Name: "PlayerCombatState", Of: var whose } } when IsOwner(whose) || IsTargetsPlayer(whose) => UpgradedPiles.Hand | UpgradedPiles.Draw | UpgradedPiles.Discard | UpgradedPiles.Exhaust,
                QueryValue { Name: "GetPile", Arguments: [ConstantValue { Value: var pile }, var whose] } when IsOwner(whose) || IsTargetsPlayer(whose) => (PileType)pile switch
                {
                    PileType.Hand => UpgradedPiles.Hand,
                    PileType.Draw => UpgradedPiles.Draw,
                    PileType.Discard => UpgradedPiles.Discard,
                    PileType.Exhaust => UpgradedPiles.Exhaust,
                    _ => UpgradedPiles.None,
                },
                _ => UpgradedPiles.None,
            };
        if (piles != UpgradedPiles.None) return (UpgradeTarget.Piles, piles);
        return null;
    }

    // ---- cards in your piles a card plays (CardCmd.AutoPlay, decision #360)

    /// <summary>
    /// What a card's CardCmd.AutoPlay plays, read from its card argument (decision #360): the pile its code takes from (GetPile of yours, your
    /// PlayerCombatState.ExhaustPile), the filter its Where keeps (null: every card), the way it takes them (every card of the list a loop goes over, a
    /// random one as the step runs, or the list shuffled and its first so many), the filter it falls back on when the first finds none (Uproar's), and
    /// whom each plays at (a random enemy: no target, or a roll over the hittable enemies; or the play's target).
    /// </summary>
    internal sealed record CardAutoPlayRead(SimPile Pile, AutoPlayWay Way, LambdaValue? Filter, LambdaValue? Fallback, ProgramValue? Count, bool AtTarget);

    /// <summary>Why a card's CardCmd.AutoPlay isn't read (CardAutoPlayOf null): a card you pick, then played (decision #188 keeps its `?`), or an argument not read.</summary>
    internal static string AutoPlayNotRead(CommandStep command) =>
        ValueAt(command, 1)?.Parts().Any(part => part is ResultValue { Command: var picked } && picked.StartsWith("CardSelectCmd.", StringComparison.Ordinal)) == true
            ? "a card you pick, then played: what it does is your pick's (decision #188 keeps its ?)"
            : $"a card it plays that its code names in a way the simulator doesn't read ({ValueAt(command, 1)})";

    /// <summary>
    /// The CardCmd.AutoPlay of a card's code (CardAutoPlayRead), or null when its card argument isn't a card of your piles its code takes as the
    /// simulator follows: Each (a loop over ToList(Where(pile, filter))), Random (FirstOrDefault(StableShuffle(ToList(Where(pile, filter))))), or
    /// Shuffled (a loop over Take(StableShuffle(...), count)). Pure on the step's code.
    /// </summary>
    internal static CardAutoPlayRead? CardAutoPlayOf(CommandStep command, ModelProgram program)
    {
        if (command.Command.FullName != "CardCmd.AutoPlay" || ValueAt(command, 1) is not { } card) return null;
        var atTarget = ValueAt(command, 2) switch
        {
            NullValue => (bool?)false,
            QueryValue { Name: "NextItem", Arguments: [var among] } when Unstored(among) is QueryValue { Name: "HittableEnemies" } => false,
            var given when given?.ToString() == "cardPlay.Target" => true,
            _ => null,
        };
        if (atTarget is not { } aimed) return null;
        if (card is NamedValue { Name: "each" })
        {
            if (program.All().OfType<LoopStep>().LastOrDefault(loop => loop.Items != null && new ModelProgram("", loop.Body).All().Contains(command))?.Items is not { } items) return null;
            if (Listed(items) is not { } list) return null;
            if (Unlisted(items) is QueryValue { Name: "Take", Arguments: [var shuffled, var count] } && Shuffled(shuffled) is { } inner && PileOf(inner) is { } shuffledPile)
                return new CardAutoPlayRead(shuffledPile.Pile, AutoPlayWay.Shuffled, shuffledPile.Filter, Fallback: null, count, aimed);
            return PileOf(list) is { } eachPile ? new CardAutoPlayRead(eachPile.Pile, AutoPlayWay.Each, eachPile.Filter, Fallback: null, Count: null, aimed) : null;
        }
        // One at random, or its fallback's when it finds none (Uproar: {found: it; not found: FirstOrDefault(StableShuffle(any Attack))}).
        if (card is WaysValue { Ways: [(_, var first), (_, var second)] } && RandomOne(first) is { } main && RandomOne(second) is { } fallback && main.Pile == fallback.Pile)
            return new CardAutoPlayRead(main.Pile, AutoPlayWay.Random, main.Filter, fallback.Filter ?? Always, Count: null, aimed);
        return RandomOne(card) is { } random ? new CardAutoPlayRead(random.Pile, AutoPlayWay.Random, random.Filter, Fallback: null, Count: null, aimed) : null;
    }

    /// <summary>A filter every card passes (a fallback with no Where of its own).</summary>
    private static readonly LambdaValue Always = new("c", ProgramCondition.Always);

    /// <summary>The list a loop goes over, as it was made (a ToList of it, a local holding it).</summary>
    private static ProgramValue? Listed(ProgramValue? value) => Unlisted(value) is { } list && (list is QueryValue { Name: "Where" or "Cards" or "Take" or "ExhaustPile" }) ? list : null;

    private static ProgramValue? Unlisted(ProgramValue? value) => StepValues.Unstored(value) switch
    {
        QueryValue { Name: "ToList" or "ToArray" or "AsEnumerable", Arguments: [var inner] } => Unlisted(inner),
        var other => other,
    };

    /// <summary>A shuffle of a list (StableShuffle(list, rng)): the list.</summary>
    private static ProgramValue? Shuffled(ProgramValue? value) => Unlisted(value) is QueryValue { Name: "StableShuffle", Arguments: [var list, ..] } ? Unlisted(list) : null;

    /// <summary>One card at random of a pile (FirstOrDefault(StableShuffle(ToList(Where(pile, filter))))): its pile and filter.</summary>
    private static (SimPile Pile, LambdaValue? Filter)? RandomOne(ProgramValue value) =>
        Unstored(value) is QueryValue { Name: "FirstOrDefault", Arguments: [var shuffled] } && Shuffled(shuffled) is { } list ? PileOf(list) : null;

    /// <summary>The pile of yours a list holds the cards of (GetPile(pile, you).Cards, your PlayerCombatState.ExhaustPile.Cards), and its Where filter.</summary>
    private static (SimPile Pile, LambdaValue? Filter)? PileOf(ProgramValue list)
    {
        LambdaValue? filter = null;
        if (Unlisted(list) is QueryValue { Name: "Where", Arguments: [var source, LambdaValue lambda] })
        {
            filter = lambda;
            list = source;
        }
        // A pile kept in a local first (Beat Down's, Cho Chorus' `CardPile pile = PileType.X.GetPile(Owner)`) is the pile.
        var cards = Unlisted(list) is QueryValue { Name: "Cards", Of: var held } query ? query with { Of = StepValues.Unstored(held) } : Unlisted(list);
        SimPile? pile = cards switch
        {
            QueryValue { Name: "Cards", Of: QueryValue { Name: "GetPile", Arguments: [ConstantValue { Value: var type }, var whose] } } when IsOwner(whose) => (PileType)type switch
            {
                PileType.Hand => SimPile.Hand,
                PileType.Draw => SimPile.Draw,
                PileType.Discard => SimPile.Discard,
                PileType.Exhaust => SimPile.Exhaust,
                _ => null,
            },
            QueryValue { Name: "Cards", Of: QueryValue { Name: "ExhaustPile", Of: QueryValue { Name: "PlayerCombatState", Of: var whose } } } when IsOwner(whose) => SimPile.Exhaust,
            _ => null,
        };
        return pile is { } found ? (found, filter) : null;
    }

    /// <summary>
    /// The CardCmd.AutoPlay a CardCmd.Upgrade's card is upgraded for: one in the same loop playing the loop's card after it (Knife Trap+'s Shivs, upgraded
    /// before each plays). Null for any other upgrade.
    /// </summary>
    internal static CommandStep? UpgradedByAutoPlay(CommandStep upgrade, ModelProgram program)
    {
        if (upgrade.Command.FullName != "CardCmd.Upgrade" || ValueAt(upgrade, 0) is not NamedValue { Name: "each" }) return null;
        var loop = program.All().OfType<LoopStep>().LastOrDefault(candidate => candidate.Items != null && candidate.Body.Contains(upgrade));
        return loop?.Body.SkipWhile(step => !ReferenceEquals(step, upgrade)).OfType<CommandStep>()
            .FirstOrDefault(step => step.Command.FullName == "CardCmd.AutoPlay" && ValueAt(step, 1) is NamedValue { Name: "each" });
    }

    /// <summary>
    /// The card's auto-plays of cards in your piles, each read with its key (AutoPlayCardsAction.Key: the card's class and the command's offset), the ones
    /// a step plays the same cards at the same way as another's left out (Beat Down's two AutoPlays, one for a card aimed at an enemy and one for the
    /// rest: both at a random enemy), and whether its play upgrades each first (an upgrade step for it, on its own condition).
    /// </summary>
    internal static IReadOnlyList<(string Key, CardAutoPlayRead Read, CommandStep Command, CommandStep? Upgrade)> CardAutoPlaysOf(Type cardType) =>
        AutoPlayReads.GetOrAdd(cardType, type =>
        {
            var program = CardPrograms.Of(type);
            var reads = new List<(string, CardAutoPlayRead, CommandStep, CommandStep?)>();
            foreach (var command in program.All().OfType<CommandStep>().Where(step => step.Command.FullName == "CardCmd.AutoPlay"))
            {
                if (CardAutoPlayOf(command, program) is not { } read || reads.Any(other => other.Item2 == read && Equals(ValueAt(other.Item3, 1), ValueAt(command, 1)))) continue;
                var upgrade = program.All().OfType<CommandStep>().FirstOrDefault(step => ReferenceEquals(UpgradedByAutoPlay(step, program), command));
                reads.Add(($"{type.Name}@{command.Offset:X4}", read, command, upgrade));
            }
            return reads;
        });

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IReadOnlyList<(string, CardAutoPlayRead, CommandStep, CommandStep?)>> AutoPlayReads = new();

    /// <summary>
    /// CardCmd.AutoPlay of cards in your piles (decision #360): AutoPlayCardsAction by its read (CardAutoPlaysOf), its count the rounds of its loops for a
    /// random one, the most of a shuffled list on the board; its tests of the card its loop or its roll is at, of the fight's end and of whom the card
    /// aims at are the action's own. A step that plays the same cards as another already read (Beat Down's second) is that one. Unknown, with why, for a
    /// card you pick then played (decision #188), an argument not read, a potion's, or a teammate's.
    /// </summary>
    private static (List<SimAction> Actions, ProgramCondition? Guard) AutoPlayCardsFrom(CardEffects.Found found, StepPlay play)
    {
        var command = found.Command;
        if (play.Card == null || play.Owner != Who.You) return (Unknown(play, "CardCmd.AutoPlay of a card in a pile that isn't yours: not followed"), null);
        if (CardAutoPlayOf(command, play.Program) is not { } read) return (Unknown(play, "CardCmd.AutoPlay: " + AutoPlayNotRead(command)), null);
        var reads = CardAutoPlaysOf(play.Type);
        if (reads.FirstOrDefault(entry => ReferenceEquals(entry.Command, command)) is not { Key: { } key } entry)
            return (new List<SimAction>(), null); // the same cards as another step's, read there
        var count = read.Way switch
        {
            AutoPlayWay.Random => RoundsOf(found, play),
            AutoPlayWay.Shuffled => read.Count is { } most && ProgramEvaluator.Number(most, play.Board, play.Program) is { } number ? (int)number : -1,
            _ => 0,
        };
        if (count < 0) return (Unknown(play, $"CardCmd.AutoPlay: how many it plays ({read.Count}) isn't read on this board"), null);
        var upgraded = entry.Upgrade is { } upgrade && ProgramEvaluator.Holds(upgrade.Condition ?? ProgramCondition.Always, play.Board, play.Program) == true;
        var action = new AutoPlayCardsAction(key, read.Pile, read.Way, count) { AtTarget = read.AtTarget, Upgraded = upgraded, Fallback = read.Fallback != null ? key + "/else" : null };
        return (new List<SimAction> { action }, Without(found.Condition, literal => literal.Test.Parts().Any(part =>
            part is NamedValue { Name: "each" } or QueryValue { Name: "StableShuffle" or "IsOverOrEnding" or "TargetType" })));
    }

    /// <summary>
    /// A CardCmd.Upgrade of your piles' cards upgrades random ones (decision #363): its card argument is a roll's (TakeRandom, NextItem, a shuffle's: Drain
    /// Power's discard pile), not every card a loop goes over. Pure on the step's code.
    /// </summary>
    internal static bool UpgradesRandomCards(CommandStep command, ModelProgram program) =>
        UpgradedValue(command, program)?.Parts().Any(part => part is QueryValue { Name: "TakeRandom" or "NextItem" or "StableShuffle" or "UnstableShuffle" }) == true;

    /// <summary>The value names a card the play makes: a maker's query (CreateCard, a card factory, Soul.Create) or a making command's result (Shiv.CreateInHand, a transform).</summary>
    private static bool MakesCards(List<ProgramValue> parts) => parts.Any(part => part is QueryValue { Name: var maker, Type: var makerType } && (CardMakers.Contains(maker) || maker == "Create" && makerType != "CombatState")
                                 || part is ResultValue { Command: var making } && MakingCommands.Contains(making));

    /// <summary>
    /// A loop over a list its code works out whose body kills its item and does nothing else a fight follows (DoomPower.DoomKill's foreach:
    /// CreatureCmd.Kill(creature) of each one not dead yet, beside MonsterModel.OnDieToDoom's looks).
    /// </summary>
    private static bool KillsEachItem(LoopStep loop) =>
        loop.Items != null && loop.Body.Where(step => step is not CommandStep { Command.FullName: var name } || !CommandsOutsideTheFight.ContainsKey(name)).ToList()
            is [CommandStep { Command.FullName: "CreatureCmd.Kill" } kill] && IsEach(ValueAt(kill, 0));

    /// <summary>
    /// A card's kill of each creature of a list (KillsEachItem): KillEachAction where the list is the hittable enemies filtered on each one's own power's
    /// question (End of Days: DoomPower.GetDoomedCreatures(HittableEnemies), its Where(c => c.GetPower&lt;DoomPower&gt;()?.IsOwnerDoomed()), read from
    /// that power's code), telling Hook.AfterDiedToDoom where the code then tells it that list (that step taken here); unknown, with why, for any
    /// other list.
    /// </summary>
    private static SimAction KillEachFrom(LoopStep loop, StepPlay play)
    {
        var told = play.Program.Commands.FirstOrDefault(command => ProgramReader.IsHookTold(command.Command) && command.Values.Any(value => Equals(StepValues.Unstored(value), StepValues.Unstored(loop.Items!))));
        if (told != null) play.Consumed.Add(told);
        if (DoomedFilter(loop.Items!, play.Type.Assembly) is not { } filter) return new NotModelledAction($"CreatureCmd.Kill: each of {loop.Items}, a list the simulator doesn't read");
        if (told != null && told.Command.Name != nameof(HookName.AfterDiedToDoom)) return new NotModelledAction($"{told.Command.FullName}: told by the card, which the simulator doesn't run");
        return new KillEachAction(filter.Power, filter.Question) { TellsDiedToDoom = told != null };
    }

    /// <summary>
    /// The power and its own question a list of the hittable enemies is filtered on (Where(HittableEnemies, c => c.GetPower&lt;P&gt;()?.Q() ?? false)), the
    /// list as a power's static method gives it (DoomPower.GetDoomedCreatures(creatures), its code read with its parameters the call's arguments) or
    /// as the code makes it; null for any other list.
    /// </summary>
    private static (PowerId Power, string Question)? DoomedFilter(ProgramValue items, System.Reflection.Assembly game)
    {
        var list = StepValues.Unstored(items);
        if (list is QueryValue { Of: null, IsProperty: false, Name: var method, Type: var declaring, Arguments: var arguments }
            && game.GetType("MegaCrit.Sts2.Core.Models.Powers." + declaring) is { } owner
            && owner.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)
                .FirstOrDefault(candidate => candidate.Name == method && candidate.GetParameters().Length == arguments.Count) is { } found)
        {
            var parameters = found.GetParameters();
            ProgramValue? returned;
            try { returned = ProgramReader.ReadReturnedValue(owner, found, RuntimeCode.Read); }
            catch (InvalidOperationException) { return null; }
            list = returned?.Map(part => part is ParameterValue or NamedValue && Array.FindIndex(parameters, parameter => parameter.Name == (part as ParameterValue)?.Name || parameter.Name == (part as NamedValue)?.Name) is var index and >= 0
                ? arguments[index] : null);
            if (list == null) return null;
            list = StepValues.Unstored(list);
        }
        if (list is QueryValue { Name: "ToList", Arguments: [var listed] }) list = StepValues.Unstored(listed);
        if (list is not QueryValue { Type: "Enumerable", Name: "Where", Arguments: [var among, LambdaValue filter] } || !IsEveryEnemy(among)) return null;
        // The filter: the item has the power (the ?.'s test) and the power answers its question.
        string? power = null, question = null;
        if (filter.Body.Terms is not [var only]) return null;
        foreach (var literal in only)
        {
            if (!literal.Holds) return null;
            switch (literal.Test)
            {
                case QueryValue { Name: "GetPower", GenericArguments: [var has], Of: ParameterValue { Name: var tested } } when tested == filter.Parameter:
                    power ??= has;
                    if (power != has) return null;
                    break;
                case QueryValue { Name: var asked, Arguments.Count: 0, Of: QueryValue { Name: "GetPower", GenericArguments: [var askedOf], Of: ParameterValue { Name: var holder } } } when holder == filter.Parameter:
                    if (question != null || power != null && power != askedOf) return null;
                    (power, question) = (askedOf, asked);
                    break;
                default:
                    return null;
            }
        }
        return power != null && question != null ? (PowerId.Of(power), question) : null;
    }

    /// <summary>PowerCmd.Remove&lt;ArtifactPower&gt; of the card's target, in a play that takes the target's block (Expose): StripBlockAction's.</summary>
    private static bool RemovesTargetsArtifact(CommandStep command, ModelProgram program) =>
        command.Command is { DeclaringType: "PowerCmd", Name: "Remove", GenericArguments: [nameof(ArtifactPower)] } && Argument(command, 0) == "cardPlay.Target"
        && program.Commands.Any(other => other.Command is { DeclaringType: "CreatureCmd", Name: "LoseBlock" } && Argument(other, 1) == "cardPlay.Target");

    /// <summary>
    /// PotionCmd.TryToProcure (decision #362): ProcurePotionAction for a potion of the card's owner's (its player argument the owner) that can't be a Fairy in
    /// a Bottle (a random one CreateRandomPotionInCombat makes: none of those is; or one of a class that isn't), when the game's Hook.ShouldProcurePotion lets
    /// you procure it on the board (Sozu doesn't): asked of every potion it could be, nothing when none is let through. Unknown, with why, for any other
    /// shape (another player's, a potion that could be a Fairy in a Bottle, Sozu's answer depending on which potion).
    /// </summary>
    private static (List<SimAction> Actions, ProgramCondition? Guard) ProcureFrom(CardEffects.Found found, StepPlay play)
    {
        var command = found.Command;
        var none = (new List<SimAction>(), (ProgramCondition?)null);
        // A shape not read (another player's belt, Entropic Brew's target; a potion its code names otherwise, its out-of-combat random ones): as before
        // (decision #313), nothing where no listener's class overrides AfterPotionProcured, unknown where one does.
        (List<SimAction>, ProgramCondition?) NotRead(string why) => AnyListenerOverrides(play, nameof(AbstractModel.AfterPotionProcured)) ? (Unknown(play, why), null) : none;
        if (!StepValues.IsOwner(ValueAt(command, 1))) return NotRead("PotionCmd.TryToProcure for a player other than the card's owner: not followed");
        List<PotionModel> could;
        if (ValueAt(command, 0)?.Parts().Any(part => part is QueryValue { Name: "CreateRandomPotionInCombat" }) == true)
            could = ModelDb.AllPotions.Where(potion => potion.CanBeGeneratedInCombat).ToList();
        else if (command.Command.GenericArguments is [var potionClass] && ModelDb.AllPotions.FirstOrDefault(potion => potion.GetType().Name == potionClass) is { } named)
            could = new List<PotionModel> { named };
        else return NotRead($"PotionCmd.TryToProcure of a potion its code names in a way the simulator doesn't read ({ValueAt(command, 0)})");
        if (could.Any(CountsAsFairy)) return (Unknown(play, "PotionCmd.TryToProcure of a potion that could be a Fairy in a Bottle: what it saves you from isn't followed"), null);
        bool? allowed;
        try
        {
            var answers = could.Select(potion => MegaCrit.Sts2.Core.Hooks.Hook.ShouldProcurePotion(play.Me.RunState, play.Combat, potion, play.Me)).Distinct().ToList();
            allowed = answers.Count == 1 ? answers[0] : null;
        }
        catch { allowed = null; }
        return allowed switch
        {
            true => (new List<SimAction> { new ProcurePotionAction() }, null),
            false => none,
            null => (Unknown(play, "PotionCmd.TryToProcure: whether you may procure it depends on which potion it is (Hook.ShouldProcurePotion)"), null),
        };
    }

    /// <summary>A hook listener of the run (RunState.IterateHookListeners, the hooks' own loop) whose class overrides <paramref name="hook"/>.</summary>
    private static bool AnyListenerOverrides(StepPlay play, string hook)
    {
        try
        {
            return play.Me.RunState.IterateHookListeners(play.Combat).Any(model =>
                model.GetType().GetMethod(hook, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public) is { } method && method.DeclaringType != typeof(AbstractModel));
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// PlayerCmd.GainGold (decision #362): GainGoldAction for the card's owner's gold, when the game's Hook.ModifyGoldGained leaves some of its amount on the
    /// board (Ectoplasm leaves none: nothing then). Unknown, with why, for another player's, or an amount not read.
    /// </summary>
    private static (List<SimAction> Actions, ProgramCondition? Guard) GoldFrom(CardEffects.Found found, StepPlay play)
    {
        var command = found.Command;
        // A shape not read: as before (decision #313), nothing where no listener's class overrides AfterGoldGained, unknown where one does.
        (List<SimAction>, ProgramCondition?) NotRead(string why) =>
            AnyListenerOverrides(play, nameof(AbstractModel.AfterGoldGained)) ? (Unknown(play, why), null) : (new List<SimAction>(), null);
        if (!StepValues.IsOwner(ValueAt(command, 1))) return NotRead("PlayerCmd.GainGold for a player other than the card's owner: not followed");
        if (ValueAt(command, 0) is not { } amount || ProgramEvaluator.Number(amount, play.Board, play.Program) is not { } gold)
            return NotRead($"PlayerCmd.GainGold: its amount ({ValueAt(command, 0)}) isn't read on this board");
        decimal gained;
        try { gained = MegaCrit.Sts2.Core.Hooks.Hook.ModifyGoldGained(play.Me.RunState, play.Combat, gold, play.Me, out _); }
        catch { return NotRead("PlayerCmd.GainGold: Hook.ModifyGoldGained couldn't be asked on this board"); }
        return gained > 0 ? (new List<SimAction> { new GainGoldAction() }, null) : (new List<SimAction>(), null);
    }

    // ---------------------------------------------------------------- the play's context

    /// <summary>
    /// The play's steps found once, the shots' attacks, the takes and the steps another stands for: of a card played, or of a potion used (its
    /// OnUse read as a play, decision #335). <paramref name="boardTarget"/>: the creature its code's target is on the board (a card's enemy; whom a
    /// potion is used on).
    /// </summary>
    private static StepPlay StepPlayOf(AbstractModel model, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids, SimPlay play,
        Creature? aimed, int owner, Creature? ally, int allyId, int plays, Creature? boardTarget)
    {
        var program = CardPrograms.Of(model.GetType());
        var stepPlay = new StepPlay
        {
            Model = model, Program = program, Board = ProgramBoard.For(model), Me = me, Combat = combat, Ids = ids, Play = play,
            Aimed = aimed, Owner = owner, Ally = ally, AllyId = allyId, Plays = plays, BoardTarget = boardTarget,
            Found = CardEffects.Find(program, _ => true).ToDictionary(found => found.Command, found => found, ReferenceEqualityComparer.Instance as IEqualityComparer<CommandStep>),
        };
        // A shot given an attack (ShootArrowAtTarget(target, owner, the attack's builder, ...)): the attack runs inside it.
        CommandStep? lastAttack = null;
        foreach (var command in program.Commands)
        {
            if (command.Command is { DeclaringType: "DamageCmd", Name: "Attack" }) lastAttack = command;
            if (command.Command is { DeclaringType: "ArrowCmd", Name: "ShootArrowAtTarget" } && lastAttack != null
                && command.Values.Any(value => value.Parts().Any(part => part is QueryValue { Name: "Attack" })))
                stepPlay.ShotAttacks[command] = lastAttack;
        }
        // A damage worked out from another's result (Rain Shot's to every other enemy): its first damage's splash (AttackAction.Splash).
        foreach (var command in program.Commands)
            if (command.Command is { DeclaringType: "CreatureCmd", Name: "Damage" } && command.Values.Any(value => value.Parts().Any(part => part is ResultValue { Command: "CreatureCmd.Damage" })))
                stepPlay.Consumed.Add(command);
        // Stretch from an attack's results (Blossom Shot): the attack's own (AttackAction.StretchFromDamage).
        foreach (var command in program.Commands)
            if (command.Command is { DeclaringType: "PowerCmd", Name: "Apply", GenericArguments: [nameof(StretchPower)] } && command.Values.Count > 2 && ReadsResults(command.Values[2]))
                stepPlay.Consumed.Add(command);
        stepPlay.Takes = TakesOf(model, stepPlay);
        stepPlay.Spread = DebuffSpreadOf(program);
        return stepPlay;
    }

    /// <summary>How many rounds the step's loops go on the board, each but the ones it acts on each item of (Rally's players, Piercing Wail's enemies).</summary>
    private static int RoundsOf(CardEffects.Found found, StepPlay play, Func<LoopStep, bool>? skip = null) =>
        found.Loops.Where(loop => skip == null || !skip(loop)).Aggregate(1, (times, loop) => times * ProgramEvaluator.Rounds(loop, play.Board, play.Program));

    /// <summary>The loop goes over the players on your side (GetTeammatesOf: you and your teammates).</summary>
    private static bool OverPlayers(LoopStep loop) => CardEffects.OverYourSide(loop);

    /// <summary>
    /// The players a collection of your side holds on the board (a loop over GetTeammatesOf, or one given whole: Yoga's), each as the
    /// simulator names them (Who.You, Who.Teammate(k)), its own filter applied (Self-Deprecate's leaves you out); you and each living teammate
    /// when it can't be read.
    /// </summary>
    private static List<int> PlayersIn(ProgramValue? items, StepPlay play)
    {
        var mates = Teammates(play.Me, play.Combat);
        var players = new List<int>();
        if (items != null && ProgramEvaluator.Items(items, play.Board, play.Program) is { } creatures)
        {
            foreach (var creature in creatures.OfType<Creature>())
            {
                if (ReferenceEquals(creature, play.Me.Creature)) players.Add(Who.You);
                else if (mates.FindIndex(mate => ReferenceEquals(mate.Creature, creature)) is var teammateIndex and >= 0) players.Add(Who.Teammate(teammateIndex));
            }
            return players;
        }
        players.Add(Who.You);
        for (var teammateIndex = 0; teammateIndex < mates.Count; teammateIndex++)
            if (mates[teammateIndex].Creature is { IsAlive: true }) players.Add(Who.Teammate(teammateIndex));
        return players;
    }

    /// <summary>The players of your side the step's loop goes over (PlayersIn of its innermost loop over them).</summary>
    private static List<int> PlayersOf(CardEffects.Found found, StepPlay play) => PlayersIn(found.Loops.LastOrDefault(OverPlayers)?.Items, play);

    private static int Number(StepPlay play, ProgramValue? value, int fallback, Creature? target = null) =>
        value != null && ProgramEvaluator.Number(value, play.Board with { Target = target ?? play.BoardTarget }, play.Program) is { } read ? (int)read : fallback;

    // ---------------------------------------------------------------- attacks and damage

    /// <summary>
    /// The step's attack (DamageCmd.Attack...Execute): its targeting and dealer from its builder (TargetingAllOpponents, TargetingRandomOpponents,
    /// Targeting; FromOsty), its hits WithHitCount's argument on the board at the target it's aimed at (a count of what a take of the play
    /// took, or of the fight's log, read as it runs; a value two ways leave on tests the simulator asks, one attack per way), times its loops'
    /// rounds, through the game's hit-count hooks; its damage the card value its first argument names, through the pipeline on each enemy.
    /// </summary>
    private static List<SimAction> AttacksFrom(CardEffects.Found found, StepPlay play, bool inShot)
    {
        var command = found.Command;
        // A card's attack (DamageCmd.Attack builds from a card): a model that isn't one attacks with none of it to read.
        if (play.Card is not { } card) return new List<SimAction> { new NotModelledAction($"{play.Name} attacks with no card to attack from ({command.Builder})") };
        var mode = ModeOf(command, card.TargetType);
        if (mode == SimPlay.Target.None) return new List<SimAction> { new NotModelledAction($"{card.GetType().Name} attacks by a targeting its translation doesn't read ({command.Builder})") };
        var byOsty = command.Builder.Contains("FromOsty");
        var aimed = mode == SimPlay.Target.Single ? play.Aimed : null;
        var hits = BoardCounts.HitsOf(command, play.Board with { Target = aimed }, play.Program) * RoundsOf(found, play);
        hits = WithHitCountHooks(card, play.Combat, hits);
        var takes = CardEffects.ExhaustsOfOthers(play.Type).Concat(CardEffects.Discards(play.Type)).ToList();
        var hitsTaken = takes.Any(take => CardEffects.CountsTaken(command.HitCountValue, take) || BoardCounts.CountsTakenByItsValue(card, command.HitCountValue, take));
        var hitsLogged = command.HitCountValue?.Parts().Any(part => part is CardValue { Key: "CalculatedHits" }) == true
            ? HistoryLedger.LoggedOf(card, "CalculatedHits", play.Combat) : null;
        if (hits <= 0 && !hitsTaken && hitsLogged == null) return new List<SimAction>();
        var attack = AttackAction(card, ValueKey(ValueAt(command, 0)), mode, hits, byOsty, play) with
        {
            HitsLogged = hitsLogged,
            HitsFrom = hitsTaken ? SimCount.CardsTaken : SimCount.Given,
            NeedsArrow = inShot,
            // Stretch from what its hits dealt (Blossom Shot: PowerCmd.Apply<StretchPower> of the sum of its results' damage).
            StretchFromDamage = play.Program.Commands.Any(other => other.Command is { DeclaringType: "PowerCmd", Name: "Apply", GenericArguments: [nameof(StretchPower)] }
                                                                   && other.Values.Count > 2 && ReadsResults(other.Values[2])),
        };
        // A hit count the code works out on a test the simulator asks (Spite: two hits when you lost HP this turn, else one): the attack with
        // each way's hits, on that way's condition, asked as it runs (decisions #190, #191).
        if (attack.HitsFrom == SimCount.Given && HitWaysOf(card, command) is { } ways)
            return ways.Select(way => (SimAction)new WhenAction(way.Condition, new SimAction[] { attack with { Hits = WithHitCountHooks(card, play.Combat, way.Hits) } })).ToList();
        return new List<SimAction> { attack };
    }

    /// <summary>
    /// Whom an attack hits, from its builder: every enemy (TargetingAllOpponents), random ones (TargetingRandomOpponents), the one it targets
    /// (Targeting: the enemy you can hit with the least HP when its argument is that, read from its shape (StepValues.IsLeastHpEnemy: Memento
    /// Mori's MinBy); another creature its code picks by an order isn't read; else a roll's target on a card aimed at a random enemy, or the card's
    /// target); with none of those, or several on different ways (Shiv's every enemy with Fan of Knives), the card's own targeting.
    /// </summary>
    private static SimPlay.Target ModeOf(CommandStep command, TargetType targeting)
    {
        if (command.Builder.Contains("Targeting") && command.TargetValue is { } aimedAt)
        {
            if (IsLeastHpEnemy(aimedAt)) return SimPlay.Target.LeastHp;
            if (PicksByOrder(aimedAt)) return SimPlay.Target.None;
        }
        var byCard = targeting switch
        {
            TargetType.AnyEnemy => SimPlay.Target.Single,
            TargetType.AllEnemies => SimPlay.Target.All,
            TargetType.RandomEnemy => SimPlay.Target.Random,
            _ => SimPlay.Target.None,
        };
        var ways = new List<SimPlay.Target>();
        if (command.Builder.Contains("TargetingAllOpponents")) ways.Add(SimPlay.Target.All);
        if (command.Builder.Contains("TargetingRandomOpponents")) ways.Add(SimPlay.Target.Random);
        if (command.Builder.Contains("Targeting")) ways.Add(byCard == SimPlay.Target.Random ? SimPlay.Target.Random : SimPlay.Target.Single);
        return ways.Count == 1 ? ways[0] : byCard;
    }

    /// <summary>The game's hit-count modifiers on an attack card's hits (Thumbscrew), the way its card preview applies them.</summary>
    private static int WithHitCountHooks(CardModel card, ICombatState combat, int hits)
    {
        if (hits <= 0 || card.Type != CardType.Attack) return hits;
        try
        {
            return Math.Max(0, (int)MegaCrit.Sts2.Core.Hooks.Hook.ModifyAttackHitCount(combat, MegaCrit.Sts2.Core.Commands.DamageCmd.Attack(0m).FromCard(card, null), hits));
        }
        catch
        {
            return hits;
        }
    }

    /// <summary>
    /// An attack of the card's value <paramref name="key"/> (its damage value: calculated on each target when it's a calculated value),
    /// with the captured terms on each enemy you can hit; Osty's and a value-less attack keep the game's final numbers.
    /// </summary>
    private static AttackAction AttackAction(CardModel card, string? key, SimPlay.Target mode, int hits, bool byOsty, StepPlay play)
    {
        var baseOn = new Dictionary<int, decimal>();
        var captured = new Dictionary<int, CapturedTerms>();
        decimal baseDamage = 0m;
        var props = ValueProp.Move;
        CalculatedVar? calc = null;
        DynamicVar? value = null;
        if (key != null) card.DynamicVars.TryGetValue(key, out value);
        if (value == null && !card.DynamicVars.TryGetValue("CalculatedDamage", out value) && !card.DynamicVars.TryGetValue("Damage", out value)) card.DynamicVars.TryGetValue("OstyDamage", out value);
        switch (value)
        {
            case CalculatedVar calculatedVar:
                calc = calculatedVar;
                if (value is CalculatedDamageVar calculatedDamage) props = calculatedDamage.Props;
                break;
            case DamageVar damageVar:
                baseDamage = damageVar.BaseValue;
                props = damageVar.Props;
                break;
            // Osty's own damage value isn't a DamageVar (OstyDamageVar : DynamicVar): Bone Shards, Snap, Right Hand Hand and the rest
            // attack with its BaseValue, which Osty's ModifyDamage hooks then scale.
            case OstyDamageVar ostyDamage:
                baseDamage = ostyDamage.BaseValue;
                props = ostyDamage.Props;
                break;
        }
        var fixedNumbers = byOsty || (calc == null && baseDamage == 0m);
        var me = play.Me;
        foreach (var (creature, id) in play.Ids)
        {
            if (!creature.IsAlive || !creature.IsHittable) continue;
            var baseValue = calc != null ? SafeDecimal(() => calc.Calculate(creature)) : baseDamage;
            if (fixedNumbers)
            {
                baseOn[id] = FinalHitDamage(card, me, play.Combat, creature, baseValue, props, byOsty);
                continue;
            }
            baseOn[id] = baseValue;
            captured[id] = SimReaders.Damage(me.RunState, play.Combat, card, me.Creature, creature, baseValue, props);
        }
        // Each later play's growth (decision #160), whatever the play count read now: a replay the simulator decides as the play starts (Echo Form's) has it too.
        var growth = !fixedNumbers ? PlayGrowth.Of(card) : PlayGrowth.Growth.None;
        return new AttackAction(hits, mode, new DamageSpec(baseDamage, SimReaders.Props(props))
        {
            BaseOn = baseOn, Captured = captured, Fixed = fixedNumbers, GrowsEachPlay = growth.Damage, EnchantFirstPlayOnly = growth.EnchantFirstPlayOnly,
            Logged = calc != null && !fixedNumbers ? HistoryLedger.LoggedOf(card, key ?? "CalculatedDamage", play.Combat) : null,
        })
        {
            ByOsty = byOsty,
            Splash = CardEffects.SplashesDamage(card.GetType()), // the target's damage to every other enemy (its code's next CreatureCmd.Damage)
            RepeatPerKill = CardEffects.RepeatsPerKill(card.GetType()),
        };
    }

    /// <summary>The game's damage for one hit on <paramref name="enemy"/> (Hook.ModifyDamage with the card as its source), Osty dealing it for his attacks.</summary>
    private static int FinalHitDamage(CardModel card, Player me, ICombatState combat, Creature enemy, decimal raw, ValueProp props, bool byOsty)
    {
        try
        {
            var dealer = byOsty && me.Osty is { } osty ? osty : me.Creature;
            return Math.Max(0, (int)MegaCrit.Sts2.Core.Hooks.Hook.ModifyDamage(me.RunState, combat, enemy, dealer, raw, props, card, null,
                MegaCrit.Sts2.Core.Hooks.ModifyDamageHookType.All, CardPreviewMode.None, out _));
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// A shot (ArrowCmd.ShootArrowAtTarget): its arrow leaves (ShootAction), the attack it's given or its delegate's steps run only with an
    /// arrow (NeedsArrow), then the arrow impales the target (ImpaleAction).
    /// </summary>
    private static List<SimAction> ShotFrom(CommandStep shot, StepPlay play)
    {
        var actions = new List<SimAction> { new ShootAction() };
        if (play.ShotAttacks.TryGetValue(shot, out var given)) actions.AddRange(AttacksFrom(play.Found[given], play, inShot: true));
        foreach (var inner in shot.Inner.OfType<CommandStep>().Where(inner => inner.Command is { DeclaringType: "DamageCmd", Name: "Attack" }))
        {
            play.Consumed.Add(inner);
            actions.AddRange(AttacksFrom(play.Found[inner], play, inShot: true));
        }
        Walk(shot.Inner, play, actions, inner: true);
        actions.Add(new ImpaleAction());
        return actions;
    }

    /// <summary>
    /// CreatureCmd.Damage: on its owner, the HP the card costs (LoseHpAction, its amount its third argument); on the target, inside a shot,
    /// the shot's hit (Rain Shot: with the splash its next damage gives every other enemy); on the target otherwise, damage that isn't an
    /// attack (DamageAction: Capture Spirit). With no card (a potion's), the damage as CreatureCmd.Damage deals it with none (DamageWithoutCard).
    /// </summary>
    private static List<SimAction> DamageFrom(CardEffects.Found found, StepPlay play)
    {
        var command = found.Command;
        if (play.Card is not { } card) return DamageWithoutCard(command, play);
        var whom = ValueAt(command, 1);
        // Its HP cost: the engine lands it with the props every card's code passes (SimEngine.HpCostProps); one that passes others isn't that.
        if (IsYou(whom))
            return ValueAt(command, 3) is ConstantValue { Value: var flags } && SimReaders.Props((ValueProp)(int)flags) == SimEngine.HpCostProps
                ? new List<SimAction> { new LoseHpAction(Number(play, ValueAt(command, 2), Sts2State.Var(card, "HpLoss"))) }
                : new List<SimAction> { new NotModelledAction($"{card.GetType().Name} damages its owner with props its code doesn't give an HP cost") };
        var key = ValueKey(ValueAt(command, 2));
        // An attack card's damage to every enemy (Echoing Slash), or to its target (Omnislice, a shot's: Rain Shot) is simulated as its attack
        // (with the splash or the repeats its table names: docs/CARD_PROGRAMS.md A5), as the adapter has; CreatureCmd.Damage runs no
        // BeforeAttack or AfterAttack in the game (TRACKER).
        if (card.Type == CardType.Attack && IsEveryEnemy(whom))
            return new List<SimAction> { AttackAction(card, key, SimPlay.Target.All, hits: 1, byOsty: false, play) };
        // Damage to anyone else (every enemy from a card that isn't an attack, a creature its code picks): unknown where it runs, never left out.
        if (!IsTarget(whom)) return new List<SimAction> { new NotModelledAction($"{card.GetType().Name} damages a creature its translation doesn't name ({whom})") };
        if (play.Aimed is not { } target || !play.Ids.TryGetValue(target, out var targetId)) return new List<SimAction>();
        if (card.Type == CardType.Attack || InsideShot(command, play))
            return new List<SimAction> { AttackAction(card, key, SimPlay.Target.Single, hits: 1, byOsty: false, play) with { NeedsArrow = InsideShot(command, play) } };
        // Its amount and props as a potion's are read (decision #343): a damage value given whole (its BaseValue and its own Props), or an amount and
        // the props its code passes after it.
        var (amount, props) = DamageOf(command, play);
        if (amount == null || props == null)
            return new List<SimAction> { new NotModelledAction($"{card.GetType().Name} damages its target by a value its translation doesn't read ({ValueAt(command, 2)})") };
        var valueProp = (ValueProp)(int)props.Value;
        return new List<SimAction>
        {
            new DamageAction(new DamageSpec(amount.Value, SimReaders.Props(valueProp))
                { Captured = new Dictionary<int, CapturedTerms> { [targetId] = SimReaders.Damage(play.Me.RunState, play.Combat, card, play.Me.Creature, target, amount.Value, valueProp) } }),
        };
    }

    /// <summary>
    /// CreatureCmd.Damage with no card (a potion's use: decision #313): to the enemy it names, or to every hittable enemy (a list given whole), its
    /// amount and props the damage value given whole or the amount and props its code passes, dealt by you, through the game's damage math with no
    /// card. Anything else (another dealer, a creature it doesn't name) is unknown where it runs.
    /// </summary>
    private static List<SimAction> DamageWithoutCard(CommandStep command, StepPlay play)
    {
        var whom = ValueAt(command, 1);
        var (amount, props) = DamageOf(command, play);
        if (amount == null || props == null) return Unknown(play, "damage by an amount the simulator doesn't read");
        // The dealer: the potion's owner (its creature), as every potion's code gives it.
        if (DealerOf(command, play) != Who.You) return Unknown(play, "damage dealt by someone other than you");
        var valueProp = (ValueProp)(int)props.Value;
        // Every creature but the pets (Foul Potion's): the players in the fight's order, then the enemies, each in turn (decision #340).
        if (StepValues.IsEveryCreatureButPets(whom))
        {
            // An enemy there that can't be hit is still in its list (CombatState.Creatures), which the simulator's hits don't reach.
            if (play.Combat.Enemies.Any(enemy => enemy.IsAlive && !enemy.IsHittable)) return Unknown(play, "damage to every creature with an enemy that can't be hit");
            var everyone = new Dictionary<int, CapturedTerms>();
            foreach (var (enemy, id) in play.Ids)
                if (enemy.IsAlive) everyone[id] = SimReaders.Damage(play.Me.RunState, play.Combat, card: null, play.Me.Creature, enemy, amount.Value, valueProp);
            var players = new List<int>();
            foreach (var player in play.Combat.Players)
                if (AimOf(player.Creature, play.Me, play.Combat, play.Ids) is var aim && Who.IsPlayer(aim)) players.Add(aim);
            return new List<SimAction> { new DamageAction(new DamageSpec(amount.Value, SimReaders.Props(valueProp)) { Captured = everyone }) { EveryEnemy = true, Players = players, Source = play.Name } };
        }
        if (IsEveryEnemy(whom))
        {
            var captured = new Dictionary<int, CapturedTerms>();
            foreach (var (enemy, id) in play.Ids)
                if (enemy.IsAlive) captured[id] = SimReaders.Damage(play.Me.RunState, play.Combat, card: null, play.Me.Creature, enemy, amount.Value, valueProp);
            return new List<SimAction> { new DamageAction(new DamageSpec(amount.Value, SimReaders.Props(valueProp)) { Captured = captured }) { EveryEnemy = true } };
        }
        if (OnBoard(play, whom) is not Creature target || IdOf(target, play) is not { } targetId || !Who.IsEnemy(targetId))
            return Unknown(play, $"damage to a creature the simulator doesn't name ({whom})");
        return new List<SimAction>
        {
            new DamageAction(new DamageSpec(amount.Value, SimReaders.Props(valueProp))
                { Captured = new Dictionary<int, CapturedTerms> { [targetId] = SimReaders.Damage(play.Me.RunState, play.Combat, card: null, play.Me.Creature, target, amount.Value, valueProp) } }) { TargetId = targetId },
        };
    }

    /// <summary>A damage command's amount and props: a damage value given whole (its BaseValue and Props), or an amount and the props after it.</summary>
    private static (decimal? Amount, decimal? Props) DamageOf(CommandStep command, StepPlay play)
    {
        // A calculated value given whole is worked out on its target, which its base value isn't: not read here.
        if (OnBoard(play, ValueAt(command, 2)) is CalculatedVar) return (null, null);
        if (OnBoard(play, ValueAt(command, 2)) is DynamicVar dynamicVar) return (dynamicVar.BaseValue, (int)PropsOf(dynamicVar));
        var amount = NumberOnBoard(play, ValueAt(command, 2));
        var props = OnBoard(play, ValueAt(command, 3)) switch
        {
            ValueProp valueProp => (decimal?)(int)valueProp,
            decimal number => number,
            _ => null,
        };
        return (amount, props);
    }

    /// <summary>A damage or block value's props (DamageVar, BlockVar: their Props), Move for any other.</summary>
    private static ValueProp PropsOf(DynamicVar dynamicVar) => dynamicVar switch
    {
        DamageVar damage => damage.Props,
        OstyDamageVar ostyDamage => ostyDamage.Props,
        BlockVar block => block.Props,
        _ => ValueProp.Move,
    };

    /// <summary>The damage's dealer (the creature argument after its amount and props, or after a value given whole), as the simulator names it.</summary>
    private static int? DealerOf(CommandStep command, StepPlay play) =>
        IdOf(OnBoard(play, ValueAt(command, OnBoard(play, ValueAt(command, 2)) is DynamicVar ? 3 : 4)), play);

    /// <summary>A value on the board as the play's target sees it (a number as a decimal, a creature, a value object); null when it can't be told.</summary>
    private static object? OnBoard(StepPlay play, ProgramValue? value) => value == null ? null : ProgramEvaluator.Value(value, play.Aiming, play.Program);

    /// <summary>A number on the board as the play's target sees it (a value object's BaseValue); null when it can't be told.</summary>
    private static decimal? NumberOnBoard(StepPlay play, ProgramValue? value) => OnBoard(play, value) switch
    {
        decimal number => number,
        int number => number,
        DynamicVar dynamicVar => dynamicVar.BaseValue,
        _ => null,
    };

    /// <summary>The simulator's id for a creature or a player (an enemy's id, you, a teammate); null for any other.</summary>
    private static int? IdOf(object? who, StepPlay play)
    {
        var creature = who switch
        {
            Creature direct => direct,
            Player player => player.Creature,
            _ => null,
        };
        if (creature == null) return null;
        if (ReferenceEquals(creature, play.Me.Creature)) return Who.You;
        if (play.Ids.TryGetValue(creature, out var enemyId)) return enemyId;
        var index = Teammates(play.Me, play.Combat).FindIndex(mate => ReferenceEquals(mate.Creature, creature));
        return index >= 0 ? Who.Teammate(index) : null;
    }

    private static List<SimAction> Unknown(StepPlay play, string why) => new() { new NotModelledAction($"{play.Name}: {why}") };

    /// <summary>The step runs inside a shot's delegate (ArrowCmd.ImpaleTarget's or ShootArrowAtTarget's), only with an arrow.</summary>
    private static bool InsideShot(CommandStep command, StepPlay play) =>
        play.Program.Commands.Any(other => other.Command.DeclaringType == "ArrowCmd" && other.Inner.Contains(command));

    /// <summary>The HP at or below which the target is set to 0 (its condition's CurrentHp comparison); any HP with none.</summary>
    private static int ThresholdOf(CardEffects.Found found, StepPlay play)
    {
        foreach (var literal in found.Condition.Literals)
            if (literal is { Holds: true, Test: CompareValue { Op: Comparison.LessOrEqual, Left: QueryValue { Name: "CurrentHp" }, Right: var threshold } })
                return Number(play, threshold, Sts2State.Var(play.Model, "Threshold"));
        return int.MaxValue;
    }

    // ---------------------------------------------------------------- block

    /// <summary>
    /// CreatureCmd.GainBlock: its block (the card value its second argument names, through the block pipeline on the gainer) to the creature
    /// its first argument names: you, the teammate it's aimed at, or each player its loop goes over (Rally: you, then each living teammate);
    /// its gains its loops' rounds. Fisticuffs' block is the damage it dealt; Entrench's your block; Mimic's the teammate's (A5).
    /// </summary>
    private static List<SimAction> BlocksFrom(CardEffects.Found found, StepPlay play, int? times)
    {
        var command = found.Command;
        var gainer = ValueAt(command, 0);
        var gains = times ?? RoundsOf(found, play, loop => IsEach(gainer) && OverPlayers(loop));
        if (gains <= 0) return new List<SimAction>();
        if (IsEach(gainer) && found.Loops.Any(OverPlayers))
        {
            var actions = new List<SimAction>();
            var mates = Teammates(play.Me, play.Combat);
            foreach (var player in PlayersOf(found, play))
            {
                var creature = player == Who.You ? play.Me.Creature : mates[Who.TeammateIndex(player)].Creature;
                if (creature != null && BlockFor(found, play, gains, player, creature) is { } block) actions.Add(block);
            }
            return actions;
        }
        if (IsYou(gainer)) return BlockFor(found, play, gains, Who.You, play.Me.Creature) is { } block ? new List<SimAction> { block } : new List<SimAction>();
        // The teammate it's aimed at (Lift, Demonic Shield; Constellation's target's player): none with no teammate to aim at.
        var toTarget = IsTarget(gainer) || gainer is QueryValue { Name: "Creature" } && IsTarget(((QueryValue)gainer).Of);
        if (toTarget && play.AllyId != Who.None && play.Ally != null)
            return BlockFor(found, play, gains, play.AllyId, play.Ally) is { } allyBlock ? new List<SimAction> { allyBlock } : new List<SimAction>();
        // Block to anyone else (an enemy, a creature its code picks): unknown where it runs, never left out.
        return toTarget ? new List<SimAction>() : new List<SimAction> { new NotModelledAction($"{play.Name} gives block to a creature its translation doesn't name ({gainer})") };
    }

    private static SimAction? BlockFor(CardEffects.Found found, StepPlay play, int gains, int to, Creature gainer)
    {
        if (play.Card is not { } card) return BlockWithoutCard(found.Command, play, gains, to, gainer);
        var combat = play.Combat;
        var me = play.Me;
        // What its amount reads as it runs, from its code (CardEffects.BlockBaseOf), through the block pipeline with the props its code gives.
        var props = BlockPropsOf(found.Command, card);
        switch (CardEffects.BlockBaseOf(found.Command, play.Type))
        {
            // Fisticuffs: the damage its attack dealt (its results' TotalDamage + OverkillDamage), so Dexterity and Frail apply.
            case SimBlockBase.AttackDealt:
                return new GainBlockAction(gains, Base: 0, SimReaders.Props(props))
                    { BaseFrom = SimBlockBase.AttackDealt, Captured = SimReaders.Block(combat, card, gainer, Sts2State.Var(card, "Damage"), props), To = to };
            // Entrench: the gainer's block as the gain runs.
            case SimBlockBase.OwnBlock:
                return new GainBlockAction(gains, Base: 0, SimReaders.Props(props)) { BaseFrom = SimBlockBase.OwnBlock, Captured = SimReaders.Block(combat, card, gainer, gainer.Block, props), To = to };
            // Mimic: its calculated block on its target (CalculationBase + CalculationExtra × the target's block), as the gain runs.
            case SimBlockBase.TargetBlock when play.Ally != null || play.Aimed != null:
            {
                var target = play.Ally ?? play.Aimed!;
                var perBlock = (decimal)Sts2State.Var(card, "CalculationExtra", fallback: 1);
                var atStart = Sts2State.Var(card, "CalculationBase") + perBlock * target.Block;
                return new GainBlockAction(gains, Sts2State.Var(card, "CalculationBase"), SimReaders.Props(props))
                    { BaseFrom = SimBlockBase.TargetBlock, PerBlock = perBlock, Captured = SimReaders.Block(combat, card, gainer, atStart, props), To = to };
            }
            case SimBlockBase.TargetBlock:
                return null;
        }
        var toAlly = to != Who.You && to != play.Owner;
        if (toAlly && card.DynamicVars.ContainsKey("CalculatedBlock")) return new GainBlockAction(gains, CardBlock(card, combat, me, gainer, gainer), SimProps.Move) { Fixed = true, To = to };
        decimal baseValue;
        var key = ValueKey(ValueAt(found.Command, 1));
        // Its props as a potion's are read (decision #343): a block value given whole has its own (BlockVar.Props, CalculatedBlockVar.Props); an
        // amount, the props its code passes after it (BlockPropsOf).
        var valueProps = props;
        if (key != null && card.DynamicVars.TryGetValue(key, out var named) && named is not CalculatedVar) (baseValue, valueProps) = (named.BaseValue, OnBoard(play, ValueAt(found.Command, 1)) is DynamicVar ? PropsOf(named) : props);
        else if (card.DynamicVars.TryGetValue("CalculatedBlock", out var dynamicVar) && dynamicVar is CalculatedVar calc)
            (baseValue, valueProps) = (SafeDecimal(() => calc.Calculate(null)), dynamicVar is CalculatedBlockVar calculatedBlock && OnBoard(play, ValueAt(found.Command, 1)) is DynamicVar ? calculatedBlock.Props : props);
        else if (card.DynamicVars.TryGetValue("Block", out var otherDynamicVar)) (baseValue, valueProps) = (otherDynamicVar.BaseValue, OnBoard(play, ValueAt(found.Command, 1)) is DynamicVar ? PropsOf(otherDynamicVar) : props);
        else return new GainBlockAction(gains, CardBlock(card, combat, me), SimProps.Move) { Fixed = true, To = to };
        var growth = PlayGrowth.Of(card); // each later play's, Echo Form's replay included
        return new GainBlockAction(gains, baseValue, SimReaders.Props(valueProps))
        {
            Captured = SimReaders.Block(combat, card, gainer, baseValue, valueProps), To = to, GrowsEachPlay = growth.Block,
            EnchantFirstPlayOnly = growth.EnchantFirstPlayOnly,
        };
    }

    /// <summary>
    /// A block gain with no card (a potion's use: decision #313): its amount a block value given whole (its BaseValue and Props) or a number on the
    /// board with the props after it, through the game's block math with no card; unknown where it runs when the amount can't be read.
    /// </summary>
    private static SimAction BlockWithoutCard(CommandStep command, StepPlay play, int gains, int to, Creature gainer)
    {
        decimal amount;
        ValueProp props;
        if (OnBoard(play, ValueAt(command, 1)) is DynamicVar dynamicVar) (amount, props) = (dynamicVar.BaseValue, PropsOf(dynamicVar));
        else if (NumberOnBoard(play, ValueAt(command, 1)) is { } number && OnBoard(play, ValueAt(command, 2)) is ValueProp or decimal)
            (amount, props) = (number, OnBoard(play, ValueAt(command, 2)) is ValueProp given ? given : (ValueProp)(int)(decimal)OnBoard(play, ValueAt(command, 2))!);
        else return new NotModelledAction($"{play.Name}: block by an amount the simulator doesn't read");
        return new GainBlockAction(gains, amount, SimReaders.Props(props)) { To = to, Captured = SimReaders.Block(play.Combat, card: null, gainer, amount, props) };
    }

    /// <summary>The props a block gain's code gives it (its third argument: a constant, or a card value's Props); Move when it can't be read.</summary>
    private static ValueProp BlockPropsOf(CommandStep gain, CardModel card) => ValueAt(gain, 2) switch
    {
        ConstantValue { Value: var flags } => (ValueProp)(int)flags,
        QueryValue { Name: "Props", Of: QueryValue { Name: var key } } when card.DynamicVars.TryGetValue(key, out var named) && named is CalculatedBlockVar calculated => calculated.Props,
        _ => ValueProp.Move,
    };

    // ---------------------------------------------------------------- powers

    /// <summary>
    /// PowerCmd.Apply&lt;T&gt;: the power to whom its second argument names (you; the card's target, or the teammate it's aimed at; every
    /// enemy you can hit, given whole or as each of a loop over them; a random enemy, a roll, each of its loop's rounds), its amount its third
    /// argument on the board (the target's own amount of it as it lands: Molten Fist; your own: Cho Ki's Stretch kept for replays; what the
    /// play's attack dealt: Blight Strike), an unknown when it can't be read (decision #282: never a guess). Only
    /// powers the simulator reads (modelled ones, the ones it keeps as numbers, poison, Artifact, a debuff on enemies); Strength and a
    /// temporary Strength on enemies as Strength (a debuff's sign); Focus and a temporary Focus as Focus.
    /// </summary>
    private static List<SimAction> PowersFrom(CardEffects.Found found, StepPlay play)
    {
        var command = found.Command;
        if (command.Command.GenericArguments is not [var powerName] || PowerType(play.Type, powerName) is not { } type)
            return new List<SimAction> { new NotModelledAction($"{play.Name} applies a power its translation can't name ({(command.Command.GenericArguments.Count > 0 ? command.Command.GenericArguments[0] : ValueAt(command, 1)?.ToString())})") };
        var whom = ValueAt(command, 1);
        var amount = ValueAt(command, 2) is { } given && ProgramEvaluator.Number(given, play.Aiming, play.Program) is { } read ? (int?)read : null;
        // Who applies it (its fourth argument): you, or the player it lands on applying it to themselves (Duplicator, Focus Potion thrown at a
        // teammate: the target its own applier); anyone else isn't followed.
        int? appliedBy = null;
        if (ValueAt(command, 3) is var applier && !IsYou(applier))
        {
            if (!(IsTarget(applier) && IsTarget(whom) && play.AimsAtPlayer))
                return new List<SimAction> { new NotModelledAction($"{play.Name} applies {powerName} by a creature its translation doesn't name ({applier})") };
            if (play.AllyId != Who.You) appliedBy = play.AllyId;
        }
        // Whom it lands on: (where, which creature) for each.
        var targets = new List<(ApplyPowerAction.Where To, int Creature)>();
        var times = 1;
        if (IsYou(whom)) targets.Add((ApplyPowerAction.Where.You, 0));
        else if (IsTarget(whom))
        {
            // A potion's target (no card play to aim at): the enemy, you or the teammate it's used on, by name.
            if (play.Card == null && play.Aimed != null && play.Ids.TryGetValue(play.Aimed, out var enemyId)) targets.Add((ApplyPowerAction.Where.Creature, enemyId));
            else if (play.Card == null && play.AllyId == Who.You) targets.Add((ApplyPowerAction.Where.You, 0));
            else if (!play.AimsAtPlayer) targets.Add((ApplyPowerAction.Where.Target, 0));
            else if (play.AllyId != Who.None) targets.Add((ApplyPowerAction.Where.Creature, play.AllyId));
        }
        else if (IsEveryEnemy(whom) || IsEach(whom) && found.Loops.Any(OverEnemies)) targets.Add((ApplyPowerAction.Where.AllEnemies, 0));
        else if (whom is QueryValue { Name: "NextItem", Arguments: [var rolled] } && rolled.Parts().Any(part => part is QueryValue { Name: "HittableEnemies" }))
        {
            targets.Add((ApplyPowerAction.Where.RandomEnemy, 0));
            times = Math.Max(1, RoundsOf(found, play));
        }
        // Every living player on your side (GetTeammatesOf: you and your teammates, given whole or as each of a loop over them: Yoga).
        else if (whom != null && (whom.Parts().Any(part => part is QueryValue { Name: "GetTeammatesOf" }) || IsEach(whom) && found.Loops.Any(OverPlayers)))
            foreach (var player in IsEach(whom) ? PlayersOf(found, play) : PlayersIn(whom, play))
                targets.Add(player == Who.You ? (ApplyPowerAction.Where.You, 0) : (ApplyPowerAction.Where.Creature, player));
        // On anyone else (a creature its code picks other than these): unknown where it runs, never left out, when the power is one the
        // simulator reads (one it doesn't changes nothing it predicts on anyone: PowerTo's rule).
        else if (Simulated(SimPowerName(type)) || SimReaders.IsDebuffType(type) || type == typeof(StrengthPower) || typeof(TemporaryStrengthPower).IsAssignableFrom(type) || FocusSignOf(type) != 0)
            return new List<SimAction> { new NotModelledAction($"{play.Name} applies {powerName} to a creature its translation doesn't name ({whom})") };
        var actions = new List<SimAction>();
        var aimedAt = AimSetOn(command, play, type);
        foreach (var (to, creature) in targets)
        {
            if (PowerTo(found, play, type, powerName, amount, to, creature, times) is not { } action) continue;
            if (action is ApplyPowerAction applied)
                action = applied with { AimedAt = aimedAt ?? applied.AimedAt, Applier = appliedBy ?? applied.Applier };
            actions.Add(action);
        }
        return actions;
    }

    /// <summary>
    /// The player the power a PowerCmd.Apply gave is aimed at, as the code sets its aim on it after (decision #322: Imitation Learning's
    /// imitationLearningPower.PlayerTarget = cardPlay.Target.Player): a store on that command's result (ProgramReader.ResultPrefix) to the power's
    /// Target or a Player its class keeps, of the play's owner or its target's player. Null when the code aims it at no one, or at someone else.
    /// </summary>
    private static AimedPlayer? AimSetOn(CommandStep command, StepPlay play, Type power)
    {
        foreach (var store in play.Program.All().OfType<StoreStep>())
        {
            if (store.Of is not { } result || result.Offset != command.Offset || !store.Field.StartsWith(ProgramReader.ResultPrefix, StringComparison.Ordinal)) continue;
            var property = store.Field[ProgramReader.ResultPrefix.Length..];
            if (property != "Target" && power.GetProperty(property)?.PropertyType != typeof(Player)) continue;
            var aimed = Unstored(store.Value);
            if (IsYou(aimed) || aimed is QueryValue { Name: "Owner", Of: NamedValue { Name: "this" } }) return AimedPlayer.PlayOwner;
            if (IsTarget(aimed) || aimed is QueryValue { Name: "Player", IsProperty: true, Of: { } targeted } && IsTarget(Unstored(targeted))) return AimedPlayer.PlayTarget;
        }
        return null;
    }

    /// <summary>
    /// PowerCmd.ModifyAmount of the holder's copy of a power aimed at a player (decision #322), found as the code finds it (the same reading as its test:
    /// ConditionReader.AimedCopyOf), by its offset (Imitation Learning's card value, Frantic Escape's 1), applied by the play's owner or by the holder.
    /// Any other instance (a power its code finds another way), an amount or an applier the translation can't read: unknown where it runs.
    /// </summary>
    private static SimAction AimedModifyFrom(CardEffects.Found found, StepPlay play)
    {
        var command = found.Command;
        var notFollowed = new NotModelledAction($"{play.Name}: PowerCmd.ModifyAmount of a power its translation can't find ({ValueAt(command, 1)})");
        if (ValueAt(command, 1) is not { } instance || ConditionReader.AimedCopyOf(instance) is not { } copy) return notFollowed;
        if (ValueAt(command, 2) is not { } offset || ProgramEvaluator.Number(offset, play.Aiming, play.Program) is not { } amount)
            return new NotModelledAction($"{PowerAmountUnread} ({copy.Power.Name}: {ValueAt(command, 2)})");
        var applier = ValueAt(command, 3);
        var byHolder = copy.Holder == AimHolder.FirstEnemyWithPower && applier != null && ConditionReader.FirstEnemyWith(applier) == copy.HolderPower?.Name;
        if (!byHolder && !IsYou(applier)) return new NotModelledAction($"{play.Name}: PowerCmd.ModifyAmount by an applier its translation can't name ({applier})");
        return new ModifyAimedPowerAction(copy.Power, copy.Holder, copy.Aim, (int)amount) { HolderPower = copy.HolderPower, AppliedByHolder = byHolder };
    }

    /// <summary>
    /// The step's power to one creature (PowersFrom): Strength and a temporary Strength on enemies as Strength (a debuff's sign: issue #371,
    /// one Artifact charge takes all of it), Focus and a temporary Focus on you as Focus, any other power the simulator reads (modelled ones,
    /// the ones it keeps as numbers, poison, Artifact, a debuff on enemies). Null for one it doesn't read.
    /// </summary>
    private static SimAction? PowerTo(CardEffects.Found found, StepPlay play, Type type, string powerName, int? read, ApplyPowerAction.Where to, int creature, int times)
    {
        var command = found.Command;
        var unread = new NotModelledAction($"{PowerAmountUnread} ({powerName}: {ValueAt(command, 2)})");
        var amount = read ?? 0;
        var simPowerName = SimPowerName(type);
        var toEnemies = to is ApplyPowerAction.Where.Target or ApplyPowerAction.Where.AllEnemies or ApplyPowerAction.Where.RandomEnemy || to == ApplyPowerAction.Where.Creature && Who.IsEnemy(creature);
        if (toEnemies && (type == typeof(StrengthPower) || typeof(TemporaryStrengthPower).IsAssignableFrom(type)))
        {
            if (read == null) return unread;
            var sign = typeof(TemporaryStrengthPower).IsAssignableFrom(type) && SimReaders.IsDebuffType(type) ? -1 : 1;
            return new ApplyPowerAction(PowerId.Strength, sign * amount) { To = to, Creature = creature, Debuff = sign * amount < 0 };
        }
        if (to == ApplyPowerAction.Where.You && FocusSignOf(type) is var focusSign && focusSign != 0)
            return read == null ? unread : amount != 0 ? new FocusAction(focusSign * amount) : null;
        // A temporary Strength or Dexterity power has a read model for its turn's end (decision #400) but never lands as itself here: what it gives as it
        // lands is its BeforeApplied's (on enemies, Strength above), so it lands as before, as a power the simulator doesn't read.
        var simulated = Simulated(simPowerName) && !typeof(TemporaryStrengthPower).IsAssignableFrom(type) && !typeof(TemporaryDexterityPower).IsAssignableFrom(type);
        var debuff = SimReaders.IsDebuffType(type);
        if (debuff ? !toEnemies && !simulated : !simulated) return null;
        if (simPowerName == nameof(StretchPower) && to == ApplyPowerAction.Where.You && CardEffects.KeepsStretchForReplays(play.Type))
            return new ApplyPowerAction(new PowerId(simPowerName), Amount: 0) { To = to, CopiesOwn = true };
        var amountOf = command.Values.Count > 2 ? ConditionReader.TargetPowerAmountOf(command.Values[2]) : null;
        var fromAttack = AttackDealtTo(command, play, to);
        if (read == null && amountOf == null && !fromAttack) return unread;
        return new ApplyPowerAction(new PowerId(simPowerName), amount)
        {
            To = to, Creature = creature, Times = times, Debuff = debuff && toEnemies, FromAttackDealt = fromAttack,
            CopiesTarget = toEnemies && amountOf == powerName,
            TargetsAmountOf = amountOf != null && !(toEnemies && amountOf == powerName) ? PowerId.Of(amountOf) : null,
        };
    }
    /// <summary>
    /// A spread of the play's target's debuffs onto the other enemies, as the card's code makes it (Misery, decision #293): a loop over the
    /// enemies you can hit (<paramref name="Over"/>) around a loop over a collection taken from the target's powers whose amount is a debuff
    /// (TypeForCurrentAmount == Debuff) at <paramref name="TakenAt"/>, whose every command applies one of them (PowerCmd.Apply of a clone, not
    /// generic, or PowerCmd.ModifyAmount of the one already there).
    /// </summary>
    internal sealed record DebuffSpread(LoopStep Over, int TakenAt);

    /// <summary>The program's spread of its target's debuffs (<see cref="DebuffSpread"/>), or null.</summary>
    internal static DebuffSpread? DebuffSpreadOf(ModelProgram program)
    {
        foreach (var over in program.All().OfType<LoopStep>())
        {
            if (!IsEveryEnemy(over.Items)) continue;
            foreach (var each in over.Body.OfType<LoopStep>())
            {
                if (each.Items is not { } items || !TakesTargetDebuffs(items)) continue;
                var commands = new ModelProgram("", each.Body).Commands.ToList();
                if (commands.Count == 0 || !commands.All(command => IsCloneApplied(command) || command.Command is { DeclaringType: "PowerCmd", Name: "ModifyAmount" })
                    || !commands.Any(IsCloneApplied)) continue;
                return new DebuffSpread(over, items is StoredValue stored ? stored.Offset : int.MaxValue);
            }
        }
        return null;
    }

    /// <summary>The collection is taken from the target's powers, those whose amount is a debuff (Where(cardPlay.Target.Powers, p => p.TypeForCurrentAmount == Debuff)).</summary>
    private static bool TakesTargetDebuffs(ProgramValue items) =>
        items.Parts().Any(part => part is QueryValue { Name: "Powers" } powers && IsTarget(powers.Of))
        && items.Parts().Any(part => part is CompareValue { Op: Comparison.Equal, Left: QueryValue { Name: "TypeForCurrentAmount" }, Right: ConstantValue { Value: var type } }
                                     && type == (long)MegaCrit.Sts2.Core.Entities.Powers.PowerType.Debuff);

    /// <summary>PowerCmd.Apply, not generic, of a clone of the loop's item (each.Key.ClonePreservingMutability()), its amount the item's.</summary>
    private static bool IsCloneApplied(CommandStep command) =>
        command.Command is { DeclaringType: "PowerCmd", Name: "Apply", GenericArguments.Count: 0 }
        && ValueAt(command, 1) is { } power && power.Parts().Any(part => part is QueryValue { Name: "ClonePreservingMutability" }) && IsEach(power);

    /// <summary>
    /// The spread's actions: the kept debuffs onto every other enemy you can hit (SpreadKeptDebuffsAction), and an unknown for a debuff on the
    /// target the simulator can't spread as the game does: a temporary one (ITemporaryPower: its amount is added to the power it applies before
    /// the spread), one instanced (its stacking finds no one already there), or one another applied (the spread's applier is the debuff's own).
    /// </summary>
    private static List<SimAction> SpreadFrom(StepPlay play)
    {
        var actions = new List<SimAction>();
        foreach (var power in play.Aimed?.Powers ?? Enumerable.Empty<PowerModel>())
        {
            if (power.TypeForCurrentAmount != MegaCrit.Sts2.Core.Entities.Powers.PowerType.Debuff) continue;
            var why = power is ITemporaryPower ? "a temporary debuff, added to the power it applies"
                : power.InstanceType != MegaCrit.Sts2.Core.Entities.Powers.PowerInstanceType.None ? "an instanced debuff"
                : play.Owner != Who.You || !ReferenceEquals(power.Applier, play.Me.Creature) ? "a debuff another applied, spread as theirs"
                : null;
            if (why == null) continue;
            actions.Add(new NotModelledAction($"{play.Name} spreads {why} ({power.GetType().Name})"));
            break;
        }
        actions.Add(new SpreadKeptDebuffsAction());
        return actions;
    }

    /// <summary>The power type a step names, among the ones the card's code applies.</summary>
    private static Type? PowerType(Type cardType, string name) =>
        Diagnostics.CardCoverage.PowersApplied(cardType).FirstOrDefault(type => type.Name == name)
        ?? typeof(PowerModel).Assembly.GetTypes().FirstOrDefault(type => type.Name == name && typeof(PowerModel).IsAssignableFrom(type));

    /// <summary>
    /// The simulator runs the power a card applies (decisions #284, #312): a model of it (hand-written, or read from its code now), a class read from
    /// its code at the reviewed builds (ReadModels.ReadWholeAtReview: Poison) that a game update left unread, whose application makes the
    /// simulation unknown (SimEngine.ApplyPower) rather than being dropped, a power it keeps as a number (SelfPowers), or a class another class's
    /// code checks for by type (ReadModels.CheckedByOtherCode, decision #312: Cruelty,
    /// which Vulnerable's damage reads), kept as a power with no behaviour of its own where nothing models it, so that code finds it.
    /// </summary>
    private static bool Simulated(string simPowerName) =>
        SimModels.Has(simPowerName) || ReadModels.ReadWholeAtReview.Contains(simPowerName) || SelfPowers.Contains(simPowerName)
        || ReadModels.CheckedByOtherCode.Contains(simPowerName);

    /// <summary>What an unknown power amount says: the step's amount isn't one the adapter reads (the card scan and PowerAmountTests hold every card to none).</summary>
    internal const string PowerAmountUnread = "a power's amount its code gives that isn't read";

    /// <summary>
    /// The card's power applications whose amount the adapter wouldn't read (decision #282), for the card scan: a power the simulator acts on
    /// (PowerTo), its amount none of a target's amount of a power, what the play's attack dealt, your own Stretch kept for replays, or a value
    /// whose every part the board reads (no part the reader couldn't follow, no other command's result, a card value only by a key the card has:
    /// DynamicVars.Doom's "DoomPower"). What a board gives each part is held by PowerAmountTests on a fight. Each as "Power: amount".
    /// </summary>
    internal static IEnumerable<string> PowerAmountsUnread(CardModel card) =>
        PowerAmountsUnread(CardPrograms.Of(card.GetType()), card.DynamicVars.ContainsKey);

    /// <summary>
    /// The same for a card's program on either branch (main's read from its assembly in the tests), its values' keys tested by
    /// <paramref name="hasKey"/>; a power the running game has no class of counts as one the simulator acts on.
    /// </summary>
    internal static IEnumerable<string> PowerAmountsUnread(ModelProgram program, Func<string, bool> hasKey)
    {
        foreach (var command in program.Commands)
        {
            if (command.Command is not { DeclaringType: "PowerCmd", Name: "Apply", GenericArguments: [var powerName] }) continue;
            var type = typeof(PowerModel).Assembly.GetTypes().FirstOrDefault(each => each.Name == powerName && typeof(PowerModel).IsAssignableFrom(each));
            var simPowerName = type == null ? powerName : SimPowerName(type);
            var actedOn = type == null || type == typeof(StrengthPower) || typeof(TemporaryStrengthPower).IsAssignableFrom(type) || FocusSignOf(type) != 0 || SimReaders.IsDebuffType(type)
                          || Simulated(simPowerName);
            if (!actedOn || simPowerName == nameof(StretchPower) && CardEffects.KeepsStretchForReplays(program)) continue;
            var amount = ValueAt(command, 2);
            if (amount != null && (ConditionReader.TargetPowerAmountOf(amount) != null
                                   || CardEffects.ReadsAttackResults(amount) && (amount.Parts().Any(part => part is QueryValue { Name: "OverkillDamage" }) || IsTarget(ValueAt(command, 1)))
                                   || amount.Parts().All(part => part is not (UnknownValue or ResultValue) && (part is not CardValue { Key: var key } || hasKey(key))))) continue;
            yield return $"{powerName}: {amount?.ToString() ?? "none"}";
        }
    }

    /// <summary>
    /// The step's amount is what the play's attack dealt (Blight Strike: the sum of its results' TotalDamage; with their OverkillDamage too, any
    /// target): AttackAction's own count, block and overkill included, which is TotalDamage alone when the power goes to the target of a
    /// single-target attack, since a target the hit killed takes no power (PowerCmd.Apply: CanReceivePowers) and one alive has no overkill.
    /// </summary>
    private static bool AttackDealtTo(CommandStep command, StepPlay play, ApplyPowerAction.Where to)
    {
        if (ValueAt(command, 2) is not { } amount || !CardEffects.ReadsAttackResults(amount)) return false;
        if (amount.Parts().Any(part => part is QueryValue { Name: "OverkillDamage" })) return true;
        var attack = play.Program.Commands.TakeWhile(step => !ReferenceEquals(step, command)).LastOrDefault(step => step.Command is { DeclaringType: "DamageCmd", Name: "Attack" });
        return to == ApplyPowerAction.Where.Target && attack != null && ModeOf(attack, play.TargetType) == SimPlay.Target.Single;
    }

    /// <summary>Focus a power gives (+1) or takes (-1): FocusPower, or a temporary Focus power (its IsPositive: Hyperbeam's is negative); 0 for any other.</summary>
    private static int FocusSignOf(Type type)
    {
        if (type == typeof(FocusPower)) return 1;
        if (!typeof(TemporaryFocusPower).IsAssignableFrom(type)) return 0;
        try
        {
            var instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
            var positive = type.GetProperty("IsPositive", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.GetValue(instance);
            return positive is false ? -1 : 1;
        }
        catch
        {
            return 1;
        }
    }

    // ---------------------------------------------------------------- draws, energy, stars

    /// <summary>
    /// A draw (CardPileCmd.Draw, CardCmd.DiscardAndDraw's draws): for you, its count its own argument (what a take of the play took, or your
    /// hand's room, read as it runs), times its loops' rounds; for the teammate it's aimed at or each player its loop goes over, their draws
    /// (their piles on the board: a teammate's aren't simulated), yours as for you.
    /// </summary>
    private static List<SimAction> DrawsFrom(CardEffects.Found found, StepPlay play)
    {
        var command = found.Command;
        var actions = new List<SimAction>();
        var forEach = command.Values.Any(IsEach) && found.Loops.Any(OverPlayers);
        var players = forEach ? PlayersOf(found, play) : null;
        var yours = CardEffects.DrawsForYou(play.Type).FirstOrDefault(draw => ReferenceEquals(draw.Command, command))
            // A draw for the play's target's player when that's you (a potion you drink: Swift Potion's target.Player): yours.
            ?? (play.AllyId == Who.You && IsTarget(ValueAt(command, 2)) ? new CardEffects.DrawForYou(command, found.Condition, found.Loops.Where(loop => !OverPlayers(loop)).ToList()) : null);
        if (yours != null && (players == null || players.Contains(Who.You))) actions.AddRange(YourDraws(yours, play));
        var cards = Number(play, ValueAt(command, 1), Sts2State.Var(play.Model, "Cards"));
        var mates = Teammates(play.Me, play.Combat);
        for (var teammateIndex = 0; teammateIndex < mates.Count; teammateIndex++)
        {
            if (mates[teammateIndex].Creature is not { IsAlive: true } mate) continue;
            var who = Who.Teammate(teammateIndex);
            if (players != null ? !players.Contains(who) : yours != null || mate != play.Ally) continue;
            actions.AddRange(TeammateDraws(mates[teammateIndex], who, cards));
        }
        return actions;
    }

    /// <summary>
    /// A teammate's draws (their piles on the board: a teammate's aren't simulated): as many as their hand has room for and their piles hold; a
    /// shuffle when the draw pile runs out.
    /// </summary>
    private static IEnumerable<SimAction> TeammateDraws(Player mate, int who, int cards)
    {
        var hand = PileType.Hand.GetPile(mate).Cards.Count;
        var drawPile = PileType.Draw.GetPile(mate).Cards.Count;
        var discard = PileType.Discard.GetPile(mate).Cards.Count;
        var drawn = Math.Min(cards, Math.Min(Math.Max(0, CardPile.MaxCardsInHand - hand), drawPile + discard));
        if (drawn > drawPile) yield return new CardEventAction(CardEvent.Shuffled, Count: 1) { Player = who, Indirect = true };
        if (drawn > 0) yield return new CardEventAction(CardEvent.Drawn, drawn) { Player = who, Indirect = true };
    }

    /// <summary>
    /// The draws of a CardCmd.DiscardAndDraw count the cards it discards (its count the Count of its own cards argument: Gambler's Brew's
    /// DiscardAndDraw(list, list.Count)): what its take took, read as it runs.
    /// </summary>
    private static bool CountsItsOwnCards(CardEffects.DrawForYou draw) =>
        draw.Command.Command.Name == "DiscardAndDraw" && ValueAt(draw.Command, 1) is { } discarded
        && Unstored(draw.Count) is QueryValue { Name: "Count" } count && (count.Of ?? (count.Arguments.Count > 0 ? count.Arguments[0] : null)) is { } counted && Unstored(counted).ToString() == discarded.ToString();

    /// <summary>One draw of yours: a count of the cards the play took (Calculated Gamble) or of your hand's room (Scrawl) read as it runs; else its own count.</summary>
    private static IEnumerable<SimAction> YourDraws(CardEffects.DrawForYou draw, StepPlay play)
    {
        var takes = CardEffects.ExhaustsOfOthers(play.Type).Concat(CardEffects.Discards(play.Type)).ToList();
        // Or the cards it discards itself (CardCmd.DiscardAndDraw(cards, cards.Count): Gambler's Brew's pick).
        if (takes.Any(take => CardEffects.CountsTaken(draw.Count, take)) || CountsItsOwnCards(draw))
        {
            yield return new CardEventAction(CardEvent.Drawn, Count: 0) { Indirect = true, JoinsHand = true, CountFrom = SimCount.CardsTaken };
            yield break;
        }
        if (CardEffects.ReadsHandRoom(draw.Count))
        {
            yield return new CardEventAction(CardEvent.Drawn, int.MaxValue) { Indirect = true, JoinsHand = true, CountFrom = SimCount.HandRoom };
            yield break;
        }
        var drawn = BoardCounts.DrawCount(play.Model, draw);
        if (drawn > 0) yield return new CardEventAction(CardEvent.Drawn, drawn) { Indirect = true, JoinsHand = true };
    }

    /// <summary>
    /// PlayerCmd.GainEnergy: its amount its first argument (your own energy: GainEnergyAsHeldAction, as much as you have once its cost is
    /// spent), to the player its second names: you, the teammate it's aimed at (none with none to aim at), or each player its loop goes over.
    /// </summary>
    private static List<SimAction> EnergyFrom(CardEffects.Found found, StepPlay play)
    {
        var command = found.Command;
        var amountValue = ValueAt(command, 0);
        if (amountValue is QueryValue { Name: "Energy", Of: QueryValue { Name: "PlayerCombatState" } }) return new List<SimAction> { new GainEnergyAsHeldAction() };
        var amount = Number(play, amountValue, Sts2State.Var(play.Model, "Energy")) * RoundsOf(found, play, OverPlayers);
        if (amount <= 0) return new List<SimAction>();
        var whom = ValueAt(command, 1);
        CardEventAction Gain(int to) => new(CardEvent.EnergyGained, amount) { Player = to };
        if (IsOwnerPlayer(whom)) return new List<SimAction> { Gain(play.Owner) };
        if (IsTarget(whom)) return Who.IsPlayer(play.AllyId) ? new List<SimAction> { Gain(play.AllyId) } : new List<SimAction>();
        if (!IsEach(whom) || !found.Loops.Any(OverPlayers)) return new List<SimAction> { new NotModelledAction($"{play.Name} gives energy to a player its translation doesn't name ({whom})") };
        // Teammates are numbered from your side: a teammate's own card (Howl from Beyond) giving you energy isn't followed (it'd need their
        // numbering of the players).
        var players = PlayersOf(found, play);
        return players.Where(player => player == Who.You || play.Owner == Who.You).Select(player => (SimAction)Gain(player == Who.You ? play.Owner : player)).ToList();
    }

    // ---------------------------------------------------------------- Osty and heals

    /// <summary>
    /// OstyCmd.Summon(choiceContext, summoner, amount, source): its amount (the card value its third argument names) for the player its second
    /// argument names: you (base.Owner), or each player of your side its loop goes over (Legion of Bone: each, a teammate's own card only for
    /// itself, as a teammate's numbering isn't followed). Its loops' other rounds repeat it.
    /// </summary>
    private static List<SimAction> SummonsFrom(CardEffects.Found found, StepPlay play)
    {
        var command = found.Command;
        var amount = Number(play, ValueAt(command, 2), Sts2State.Var(play.Model, "Summon"));
        var summoner = ValueAt(command, 1);
        var overPlayers = IsEach(summoner) ? found.Loops.LastOrDefault(loop => OverPlayers(loop) || OverPlayerCreatures(loop)) : null;
        var rounds = RoundsOf(found, play, loop => ReferenceEquals(loop, overPlayers));
        var actions = new List<SimAction>();
        if (IsOwnerPlayer(summoner))
            for (var round = 0; round < rounds; round++) actions.Add(new SummonAction(amount));
        // The player it's aimed at (a potion used on you or thrown at a teammate: Bone Brew's target.Player); none with no player to aim at.
        else if (IsTarget(summoner))
        {
            if (Who.IsPlayer(play.AllyId))
                for (var round = 0; round < rounds; round++) actions.Add(new SummonAction(amount) { Player = play.AllyId });
        }
        else if (overPlayers != null)
            foreach (var player in PlayersIn(overPlayers.Items, play).Where(player => player == Who.You || play.Owner == Who.You))
                for (var round = 0; round < rounds; round++) actions.Add(new SummonAction(amount) { Player = player == Who.You ? play.Owner : player });
        else actions.Add(new NotModelledAction($"{play.Name} summons for a player its translation doesn't name ({summoner})"));
        return actions;
    }

    /// <summary>The loop goes over the fight's players (CombatState.PlayerCreatures, its own filter applied: Legion of Bone's living ones).</summary>
    private static bool OverPlayerCreatures(LoopStep loop) => loop.Items?.Parts().Any(part => part is QueryValue { Name: "PlayerCreatures" }) == true;

    /// <summary>
    /// CreatureCmd.Heal(creature, amount): its amount on you (base.Owner.Creature: Not Yet), your Osty (base.Owner.Osty: Spur), or the player it's
    /// aimed at (a potion used on you or thrown at a teammate: Blood Potion's share of their max HP).
    /// </summary>
    private static List<SimAction> HealsFrom(CardEffects.Found found, StepPlay play)
    {
        var command = found.Command;
        var amount = Number(play, ValueAt(command, 1), Sts2State.Var(play.Model, "Heal"));
        if (IsYou(ValueAt(command, 0))) return new List<SimAction> { new HealAction(amount) };
        if (Argument(command, 0) == "Owner.Osty") return new List<SimAction> { new HealAction(amount) { Osty = true } };
        if (IsTarget(ValueAt(command, 0)) && play.AimsAtPlayer)
            return Who.IsPlayer(play.AllyId) ? new List<SimAction> { new HealAction(amount) { Player = play.AllyId == Who.You ? null : play.AllyId } } : new List<SimAction>();
        return new List<SimAction> { new NotModelledAction($"{play.Name} heals a creature its translation doesn't name ({ValueAt(command, 0)})") };
    }

    /// <summary>
    /// CreatureCmd.GainMaxHp(creature, amount): its amount (its second argument on the board) on you (Feed), or the player it's aimed at (Fruit Juice
    /// used on you or thrown at a teammate); anyone else's max HP isn't kept (unknown where it runs).
    /// </summary>
    private static List<SimAction> MaxHpFrom(CardEffects.Found found, StepPlay play)
    {
        var command = found.Command;
        var amount = Number(play, ValueAt(command, 1), Sts2State.Var(play.Model, "MaxHp"));
        if (IsYou(ValueAt(command, 0))) return new List<SimAction> { new GainMaxHpAction(amount) };
        if (IsTarget(ValueAt(command, 0)) && play.AimsAtPlayer)
            return Who.IsPlayer(play.AllyId) ? new List<SimAction> { new GainMaxHpAction(amount) { Player = play.AllyId == Who.You ? null : play.AllyId } } : new List<SimAction>();
        return new List<SimAction> { new NotModelledAction($"{play.Name} raises the max HP of a creature its translation doesn't name ({ValueAt(command, 0)})") };
    }

    /// <summary>
    /// CreatureCmd.LoseMaxHp(choiceContext, creature, amount, cardSource) of you from a card's play (Brightest Flame, decision #341): its amount (its third
    /// argument on the board) off your max HP, as decision #327's engine runs it, the HP above the new max lost as the card's damage (its cardSource the card).
    /// Anyone else's, or one with no card as its source (a potion's), is unknown where it runs.
    /// </summary>
    private static List<SimAction> MaxHpLossFrom(CardEffects.Found found, StepPlay play)
    {
        var command = found.Command;
        if (!IsYou(ValueAt(command, 1)) || play.Card == null || ValueAt(command, 3) is not NamedValue { Name: "this" })
            return new List<SimAction> { new NotModelledAction($"{play.Name} takes max HP from a creature, or with a source, its translation doesn't name ({ValueAt(command, 1)})") };
        return new List<SimAction> { new LoseMaxHpAction(Number(play, ValueAt(command, 2), Sts2State.Var(play.Model, "MaxHp"))) };
    }

    // ---------------------------------------------------------------- orbs

    /// <summary>
    /// OrbCmd: a channel of its orb (its class, or a random one) into the orbs of the player it names (yours, or the teammate it's aimed at:
    /// Ignition), its loops' rounds; an evoke of your front orb, kept when its dequeue argument is false, its loops' rounds (Quadcast's and
    /// Multicast's last round dequeues); in a loop over your orbs, every orb that many times (Shatter: twice); slots added or removed. An
    /// evoke's test of your orbs is what it does with none (OrbCmd.EvokeNext evokes nothing), so it isn't asked.
    /// </summary>
    private static (List<SimAction> Actions, ProgramCondition? Guard) OrbsFrom(CardEffects.Found found, StepPlay play)
    {
        var command = found.Command;
        var none = (new List<SimAction>(), (ProgramCondition?)null);
        switch (command.Command.Name)
        {
            case "Channel":
            {
                if (CardEffects.OrbOf(command) is not { } kind) return (new List<SimAction> { new NotModelledAction($"{play.Name} channels an orb its translation can't name") }, null);
                var to = command.Arguments.Contains("cardPlay.Target.Player") ? play.AllyId : play.Owner;
                if (to == Who.None) return none;
                var times = RoundsOf(found, play);
                // A loop whose bound is a calculated value on the fight's log (Voltaic's Lightning channeled this fight) follows the ledger.
                var logged = found.Loops.Select(LoopBoundKey).OfType<string>().Select(key => (play.Card is { } card ? HistoryLedger.LoggedOf(card, key, play.Combat) : null)).FirstOrDefault(amount => amount != null);
                return times > 0 || logged != null ? (new List<SimAction> { new OrbAction(OrbAction.Op.Channel) { Orb = kind, Times = times, To = to, TimesLogged = logged } }, null) : none;
            }
            case "EvokeNext":
            {
                var guard = WithoutOrbTests(found.Condition);
                if (found.Loops.FirstOrDefault(loop => loop.While?.Literals.Any(literal => literal.Test.Parts().Any(part => part is QueryValue { Name: "Orbs" })) == true) is { } overOrbs)
                {
                    var perOrb = new ModelProgram("", overOrbs.Body).Commands.Count(other => other.Command is { DeclaringType: "OrbCmd", Name: "EvokeNext" });
                    // The loop's evokes are one action: every orb, that many times.
                    foreach (var other in new ModelProgram("", overOrbs.Body).Commands.Where(other => !ReferenceEquals(other, command))) play.Consumed.Add(other);
                    return (new List<SimAction> { perOrb == 2 ? new OrbAction(OrbAction.Op.EvokeAllTwice) : new NotModelledAction($"every orb evoked {perOrb} times") }, guard);
                }
                var keeps = ValueAt(command, 2) is ConstantValue { Value: 0 };
                var rounds = RoundsOf(found, play);
                return rounds > 0 ? (new List<SimAction> { new OrbAction(OrbAction.Op.EvokeFront) { Times = rounds, Keeps = keeps } }, guard) : none;
            }
            case "AddSlots" or "RemoveSlots":
            {
                // Slots for the player it's aimed at (Potion of Capacity's target.Player): yours when that's you; a teammate's orb slots aren't followed.
                if (IsTarget(ValueAt(command, 0)) && play.AllyId != Who.You)
                    return Who.IsPlayer(play.AllyId) ? (Unknown(play, "orb slots for a teammate: not followed"), null) : none;
                var slots = (command.Command.Name == "RemoveSlots" ? -1 : 1) * Number(play, ValueAt(command, 1), fallback: 1) * RoundsOf(found, play);
                return slots != 0 ? (new List<SimAction> { new OrbAction(OrbAction.Op.Slots) { Slots = slots } }, null) : none;
            }
        }
        return none;
    }

    // ---------------------------------------------------------------- cards taken, moved, shuffled, auto-played

    /// <summary>The take actions of the step (TakesOf), under the take's own tests of the play (its tests of the card it takes are its filter).</summary>
    private static (List<SimAction> Actions, ProgramCondition? Guard) TakesFrom(CardEffects.Found found, StepPlay play)
    {
        var command = found.Command;
        var actions = play.Takes.TryGetValue(command, out var taken) ? new List<SimAction>(taken) : new List<SimAction>();
        var take = CardEffects.ExhaustsOfOthers(play.Type).Concat(CardEffects.Discards(play.Type)).Concat(CardEffects.Transforms(play.Type))
            .FirstOrDefault(candidate => ReferenceEquals(candidate.Command, command));
        // The cards it discards that aren't in your hand: the ones it looked at on top of your draw pile (Ki Chant), on the board.
        if (take is { Way: CardEffects.TakeWay.LookedAt } && command.Command.Name == "Discard" && BoardCounts.DiscardedOutsideHand(play.Model) is > 0 and var looked)
            actions.Add(new CardEventAction(CardEvent.Discarded, looked) { Indirect = true });
        // Transformed outside your hand (Charge, Seance: your draw pile): a created card each, in place; none certain when you choose how many.
        if (take is { PickPile: not (null or PileType.Hand) } && command.Command.Name.StartsWith("Transform", StringComparison.Ordinal) && !take.ChoosesHowMany
            && BoardCounts.TransformsOutsideHand(play.Model) is > 0 and var transformed)
            actions.Add(new CardEventAction(CardEvent.Generated, transformed) { Indirect = true, Card = MadeSimCard(MadeCards.TransformedInto(command, play.Program)) });
        // A Sly card it discards without a choice (the whole hand: CardCmd.Discard's AutoPlay) plays itself: not simulated yet (tracker). One
        // you pick is a card, never a particular one (decision #188).
        if (take is { Way: CardEffects.TakeWay.Each } && command.Command.Name is "Discard" or "DiscardAndDraw" && BoardCounts.DiscardsSlyWithoutPick(play.Model))
            actions.Add(new NotModelledAction("a Sly card in your hand it discards plays itself"));
        return (actions, take?.PlayTest ?? found.Condition);
    }

    /// <summary>
    /// The take actions of every take of the play (decision #189: each a TakeFromHandAction the simulator decides as it runs), by command,
    /// in TakeFiltersOf's order (their filters' names): the exhausts of other cards (a pick, a roll, each card of a collection; a pick from
    /// your draw pile: Cleanse), the discards from your hand (a pick shared with an exhaust by a test on the card: The Hermit), the transforms
    /// in your hand. A take whose own test of the play fails on the board (True Grit's pick when it isn't upgraded) isn't one. A filtered take
    /// carries its cards in your hand now; one over every pile (Flak Cannon's statuses) takes its hand's share out silently, then counts
    /// every pile's. A loop's other commands run after each card (Second Wind's block). The exhausts' first is after the pick of what acts
    /// when exhausted (ExhaustFromHandAction: Drum of Battle).
    /// </summary>
    private static Dictionary<CommandStep, List<SimAction>> TakesOf(AbstractModel model, StepPlay play)
    {
        var type = model.GetType();
        var program = play.Program;
        var board = play.Board;
        var result = new Dictionary<CommandStep, List<SimAction>>(ReferenceEqualityComparer.Instance);
        int Count(ProgramValue? value) => value != null && ProgramEvaluator.Number(value, board, program) is { } read ? Math.Max(0, (int)read) : 1;
        var exhausts = CardEffects.ExhaustsOfOthers(type);
        foreach (var (take, key, cardEvent, eventIfNot, leaves) in BoardCounts.TakesInOrder(model))
        {
            var actions = result.TryGetValue(take.Command, out var already) ? already : result[take.Command] = new List<SimAction>();
            // A transform's made card, when its code names it: the created-card event's card.
            var made = cardEvent == CardEvent.Generated ? MadeSimCard(MadeCards.TransformedInto(take.Command, program)) : null;
            if (eventIfNot != null && CardEffects.Discards(type).FirstOrDefault(discard => ReferenceEquals(discard.Pick, take.Pick)) is { } shared) play.Consumed.Add(shared.Command);
            switch (take.Way)
            {
                case CardEffects.TakeWay.Picked when take.PickPile is PileType.Draw:
                    if (cardEvent == CardEvent.Exhausted)
                        actions.Add(new TakeFromHandAction(HandTakeWay.Pick) { Event = cardEvent, FromDrawPile = true, Most = Count(take.PerPick), ChoosesHowMany = take.ChoosesHowMany, Matching = BoardCounts.ExhaustOthers(model, new List<CardModel>(), DrawPileOf(model)) });
                    break;
                case CardEffects.TakeWay.Picked when take.PickPile is null or PileType.Hand:
                {
                    var filtered = !take.ItemTest.IsAlways || eventIfNot != null;
                    actions.Add(new TakeFromHandAction(HandTakeWay.Pick)
                    {
                        Event = cardEvent, Made = made, EventIfNot = eventIfNot, Leaves = leaves, Most = Count(take.PerPick), ChoosesHowMany = take.ChoosesHowMany,
                        Filter = filtered ? key : null, Matching = filtered ? BoardCounts.MatchingAtStart(model, take, everyPile: false) : 0,
                    });
                    break;
                }
                case CardEffects.TakeWay.Random or CardEffects.TakeWay.Each:
                {
                    var way = take.Way == CardEffects.TakeWay.Random ? HandTakeWay.Random : take.Single ? HandTakeWay.Pick : HandTakeWay.Each;
                    var most = take.Way == CardEffects.TakeWay.Random ? Count(take.RollCount) : 1;
                    if (CardEffects.IsYourWholeHand(take.Items) && take.ItemTest.IsAlways)
                        actions.Add(new TakeFromHandAction(way) { Event = cardEvent, Made = made, Leaves = leaves, Most = most, EachCard = EachCardOf(take, play) });
                    else if (BoardCounts.TakesFromEveryPile(model, take))
                    {
                        // Every pile's (Flak Cannon): its hand's share leaves first, then each card of every pile sets off its event.
                        actions.Add(new TakeFromHandAction(way) { Leaves = leaves, Most = most, Filter = key, Matching = BoardCounts.MatchingAtStart(model, take, everyPile: false) });
                        actions.Add(new TakeFromHandAction(way) { Event = cardEvent, Made = made, Leaves = false, Most = most, Filter = "all:" + key, Matching = BoardCounts.MatchingAtStart(model, take, everyPile: true), EachCard = EachCardOf(take, play) });
                    }
                    else
                        actions.Add(new TakeFromHandAction(way)
                        {
                            Event = cardEvent, Made = made, Leaves = leaves, Most = most, Filter = key, Matching = BoardCounts.MatchingAtStart(model, take, everyPile: false), EachCard = EachCardOf(take, play),
                            // Each card it lets through: the ones that act in your hand as the turn ends go with them (decision #402: Compact's Wither).
                            TurnEndTaken = way == HandTakeWay.Each && model is CardModel
                                && BoardCounts.MatchingCardsAtStart(model, take).Select(TurnEndOf).OfType<TurnEndCard>().ToList() is { Count: > 0 } turnEnds ? turnEnds : null,
                        });
                    break;
                }
            }
        }
        // What acts when exhausted among the cards the play's exhausts take from your hand (Drum of Battle), before the first.
        if (exhausts.Select(exhaust => result.GetValueOrDefault(exhaust.Command)).FirstOrDefault(actions => actions is { Count: > 0 }) is { } firstActions && ExhaustPickOf(model) is { FromDrawPile: false } exhaustPick)
            firstActions.Insert(0, exhaustPick);
        return result;
    }

    /// <summary>
    /// What a take's loop does after each card besides taking it (Second Wind: block for each card exhausted): its other commands' actions,
    /// once each, taken out of the play's own steps.
    /// </summary>
    private static IReadOnlyList<SimAction> EachCardOf(CardEffects.CardsTaken take, StepPlay play)
    {
        var loop = play.Program.All().OfType<LoopStep>()
            .FirstOrDefault(candidate => new ModelProgram("", candidate.Body).Commands.Any(command => ReferenceEquals(command, take.Command)));
        if (loop == null) return Array.Empty<SimAction>();
        var actions = new List<SimAction>();
        foreach (var other in new ModelProgram("", loop.Body).Commands.Where(command => command.Command is { DeclaringType: "CreatureCmd", Name: "GainBlock" }))
        {
            play.Consumed.Add(other);
            actions.AddRange(BlocksFrom(play.Found[other], play, times: 1));
        }
        return actions;
    }

    /// <summary>
    /// CardPileCmd.Add: into your hand from your piles (HandTakeOf: TakeToHandAction per pile, a choice of how many is unknown); back onto your
    /// draw pile after a pick from your hand (Glimmer, Thinking Ahead: the pick's most); a pick from your discard pile onto your draw pile's top (Wah Ki,
    /// Cosmic Indifference: PickDiscardOntoDrawTopAction, so a draw after it draws that card); your whole hand moved (Reboot: it leaves, setting
    /// off nothing). Anything else (into a teammate's hand) changes nothing simulated, except a pick
    /// onto your draw pile's top from a pile holding a card that acts from there as your turn ends (I Am Invincible: Cosmic Indifference from your
    /// discard pile, Photon Cut from your hand): which card goes there is your later decision, so what follows is unknown (PickOntoTopUnknown).
    /// </summary>
    private static (List<SimAction> Actions, ProgramCondition? Guard) AddsFrom(CardEffects.Found found, StepPlay play)
    {
        var command = found.Command;
        var actions = new List<SimAction>();
        var intoHand = CardEffects.TakesIntoHandOf(play.Type).FirstOrDefault(take => ReferenceEquals(take.Command, command));
        if (intoHand != null)
        {
            if (intoHand.ChoosesHowMany)
            {
                if (BoardCounts.HandTakeChosenOf(play.Model, Sts2State.Hand(play.Me).Count)) actions.Add(new NotModelledAction("you choose how many cards it takes into your hand"));
                return (actions, intoHand.PlayTest);
            }
            if (ReferenceEquals(CardEffects.TakesIntoHandOf(play.Type)[0].Command, command)) actions.AddRange(TakesToHand(play.Model, play.Me, play.Ids));
            return (actions, intoHand.PlayTest);
        }
        var pile = Argument(command, 1);
        var drawPile = ((int)PileType.Draw).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (pile == drawPile && found.Pick != null && PickMayTopWithACardActingThere(command, found, play)) actions.Add(new NotModelledAction(PickOntoTopUnknown));
        if (pile == drawPile && found.Pick != null && OntoTop(command)
            && new CardEffects.CardsTaken(command, found.Condition, CardEffects.TakeWay.Picked, Items: null, found.Pick).PickPile == PileType.Discard)
        {
            // Its tests are the pick's (a card picked: the discard pile held one; Cosmic Indifference's, that it's in your draw or discard pile, which a
            // card just picked from your discard pile is), and the action asks the first itself (an empty discard pile moves nothing).
            actions.Add(new PickDiscardOntoDrawTopAction());
            return (actions, ProgramCondition.Always);
        }
        if (pile == drawPile && found.Pick is { Command.Name: "FromHand" } pick)
        {
            var take = new CardEffects.CardsTaken(command, found.Condition, CardEffects.TakeWay.Picked, Items: null, pick);
            actions.Add(new TakeFromHandAction(HandTakeWay.Pick) { Most = Number(play, take.PerPick, fallback: 1) });
            return (actions, ProgramCondition.Always);
        }
        if (MovesYourWholeHand(command, play))
        {
            actions.Add(new TakeFromHandAction(HandTakeWay.Each));
            return (actions, null);
        }
        return (actions, null);
    }

    /// <summary>CardPileCmd.Add's position is the top (CardPilePosition.Top), read.</summary>
    private static bool OntoTop(CommandStep add) =>
        Argument(add, 2) == ((int)CardPilePosition.Top).ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal const string PickOntoTopUnknown = "you choose which card goes on top of your draw pile, and one you could pick plays itself from there as your turn ends";

    /// <summary>
    /// A pick put on your draw pile's top (CardPileCmd.Add's position Top, or one not read) could be a card that acts from there as your turn ends
    /// (ActsFromTopOfDrawPile): one in the pile it picks from (your hand: the cards in it but the one played; a pile not read: hand, draw and discard).
    /// A card the play draws before a pick from your hand could be one too (Photon Cut): your draw pile's cards count then.
    /// </summary>
    private static bool PickMayTopWithACardActingThere(CommandStep command, CardEffects.Found found, StepPlay play)
    {
        var top = ((int)CardPilePosition.Top).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (Argument(command, 2) is { } position && position != top && int.TryParse(position, out _)) return false;
        var from = new CardEffects.CardsTaken(command, found.Condition, CardEffects.TakeWay.Picked, Items: null, found.Pick).PickPile;
        var piles = from switch
        {
            PileType.Hand => play.Card != null && CardEffects.DrawsForYou(play.Card.GetType()).Count > 0 ? new[] { PileType.Hand, PileType.Draw } : new[] { PileType.Hand },
            { } pileType => new[] { pileType },
            null => new[] { PileType.Hand, PileType.Draw, PileType.Discard },
        };
        return piles.Any(pileType => pileType.GetPile(play.Me).Cards.Any(card => card != play.Card && ActsFromTopOfDrawPile(card)));
    }

    /// <summary>
    /// The CardPileCmd.Add moves your whole hand: each card of a loop over it (Reboot), or its cards given whole (Bottled Potential's
    /// Add(hand.Cards, Draw)).
    /// </summary>
    private static bool MovesYourWholeHand(CommandStep add, StepPlay play) =>
        IsEach(ValueAt(add, 0)) && play.Found[add].Loops is [.., var loop] && CardEffects.IsYourWholeHand(loop.Items) || CardEffects.IsYourWholeHand(ValueAt(add, 0));

    /// <summary>
    /// CardPileCmd.Shuffle: your discard pile into your draw pile, with the hand the play put into your draw pile first (Reboot); on its test
    /// of your draw pile being short of a number (Ki Chant's), only then, asked as it runs. ShuffleIfNecessary: only when your draw pile is
    /// empty.
    /// </summary>
    private static (List<SimAction> Actions, ProgramCondition? Guard) ShuffleFrom(CardEffects.Found found, StepPlay play)
    {
        var command = found.Command;
        if (command.Command.Name == "ShuffleIfNecessary")
            return (new List<SimAction> { new ShuffleDrawPileAction(Array.Empty<SimDrawGroup>()) { WhenShorterThan = 1 } }, ProgramCondition.Always);
        // Its condition's test of your draw pile's count: what the action asks itself.
        foreach (var literal in found.Condition.Literals)
            if (literal.Test is CompareValue compare && (DrawPileCount(compare.Left) ? compare.Right : DrawPileCount(compare.Right) ? compare.Left : null) is { } bound)
                return (new List<SimAction> { new ShuffleDrawPileAction(Array.Empty<SimDrawGroup>()) { WhenShorterThan = Number(play, bound, fallback: 1) } }, ProgramCondition.Always);
        // The hand the play moved into your draw pile before it (Reboot), by profile (the board read's own: unknown in a play built before its piles are).
        var movesHand = play.Program.Commands.TakeWhile(other => !ReferenceEquals(other, command))
            .Any(other => other.Command is { DeclaringType: "CardPileCmd", Name: "Add" } && MovesYourWholeHand(other, play));
        var profiles = DrawProfilesOf(play.Ids);
        if (movesHand && profiles == null)
            return (new List<SimAction> { new NotModelledAction("your hand shuffled in by a play built before the board's piles are read") }, null);
        var handIn = movesHand
            ? PileType.Hand.GetPile(play.Me).Cards.Where(other => other != play.Card).Select(other => profiles!.GetValueOrDefault(other)).OfType<SimDrawCard>()
                .GroupBy(profile => profile.Key).Select(sameProfile => new SimDrawGroup(sameProfile.First(), sameProfile.Count())).ToList()
            : new List<SimDrawGroup>();
        return (new List<SimAction> { new ShuffleDrawPileAction(handIn) }, null);
    }

    /// <summary>
    /// CardPileCmd.AutoPlayFromDrawPile: how many (its count argument on the board, or what a take of the play took: The Devil), whether they
    /// exhaust (its exhaust argument: Havoc's), whether they're upgraded first (the callback it's given upgrading them, on its condition).
    /// </summary>
    private static List<SimAction> AutoPlayFrom(CardEffects.Found found, StepPlay play)
    {
        var (count, exhausts, upgrades) = BoardCounts.AutoPlayOf(found.Command, play.Model);
        var countsTaken = found.Command.Values.Count > 2 && CardEffects.ExhaustsOfOthers(play.Type).Any(take => CardEffects.CountsTaken(found.Command.Values[2], take));
        return new List<SimAction> { new AutoPlayFromDrawAction(count) { ForceExhaust = exhausts, Upgrade = upgrades, CountFrom = countsTaken ? SimCount.CardsTaken : SimCount.Given } };
    }

    /// <summary>
    /// How a card that exhausts others takes them (ExhaustFromHandAction: what a card that acts when exhausted, Drum of Battle, does
    /// then), read from its program: a pick is Chosen (from your draw pile for Cleanse), a roll Random, a loop over a collection All;
    /// a filter by type (Second Wind's non-Attacks, Thrash's Attacks, The Hermit's Curse) is its Only or Except. A filter that isn't a
    /// type (Optimism's Sadness, Flak Cannon's statuses outside the exhaust pile) takes no card in your hand
    /// that acts when exhausted unless one passes it, evaluated on the board: then none is followed. Null for a card that exhausts no
    /// other card.
    /// </summary>
    internal static ExhaustFromHandAction? ExhaustPickOf(AbstractModel model)
    {
        var program = CardPrograms.Of(model.GetType());
        var board = ProgramBoard.For(model);
        foreach (var exhaust in CardEffects.ExhaustsOfOthers(model.GetType()))
        {
            if (ProgramEvaluator.Holds(exhaust.PlayTest, board, program) == false) continue;
            var pick = exhaust.Way switch
            {
                CardEffects.TakeWay.Picked => ExhaustPick.Chosen,
                CardEffects.TakeWay.Random => ExhaustPick.Random,
                _ => ExhaustPick.All,
            };
            var filter = exhaust.Way == CardEffects.TakeWay.Picked ? exhaust.ItemTest : exhaust.Filter?.Body ?? ProgramCondition.Always;
            if (filter.IsAlways) return new ExhaustFromHandAction(pick) { FromDrawPile = exhaust.FromDrawPile };
            if (BoardCounts.TypeTest(filter) is var (type, holds))
                return holds ? new ExhaustFromHandAction(pick) { Only = type, FromDrawPile = exhaust.FromDrawPile } : new ExhaustFromHandAction(pick) { Except = type, FromDrawPile = exhaust.FromDrawPile };
            return null;
        }
        return null;
    }

    // ---------------------------------------------------------------- made cards

    /// <summary>
    /// A make (MadeCards: CardPileCmd.AddGeneratedCardToCombat, AddGeneratedCardsToCombat, AddToCombatAndPreview, ForgeCmd.Forge): its cards'
    /// created-card events for their creator (Hook.AfterCardGeneratedForCombat's: you, or the player a helper made them for: Blade Symphony's
    /// Shivs), as many as its count on the board (or read as it runs: your hand's room, the cards a take took), each player's of your side a
    /// loop over them goes round, into the pile it names (a hand: they join it as they come, into a full one the discard pile). A forge makes
    /// the Sovereign Blade when you have none, on the first play only (a later play finds it).
    /// </summary>
    private static List<SimAction> MadeFrom(CardEffects.Found found, StepPlay play)
    {
        if (MadeCards.Of(play.Type).FirstOrDefault(make => ReferenceEquals(make.Found?.Command, found.Command)) is not { } make)
            return new List<SimAction> { new NotModelledAction($"{play.Name} makes cards its translation can't read ({found.Command.Command.FullName})") };
        if (make.By == MadeCards.Way.Forged)
            return MadeCards.HasBlade(play.Model) ? new List<SimAction>()
                : new List<SimAction> { new FirstPlayOnly(new SimAction[] { new CardEventAction(CardEvent.Generated, Count: 1) { Indirect = true, JoinsHand = true, Card = MadeSimCard(make.Card) } }) };
        // A list of cards whose count its code doesn't give (filled by calls that aren't steps: Orobic Acid's AddRange of three factories' cards):
        // unknown where it runs, never one.
        if (found.Command.Command.Name == "AddGeneratedCardsToCombat" && make.PerCall == null && make.FilledBy == null)
            return Unknown(play, $"{found.Command.Command.FullName} of a list whose count its code doesn't give");
        var (count, from) = MadeCount(make, play);
        // A count its code gives that the board can't work out (a value the evaluator can't read): unknown where it runs, never one.
        if (count < 0) return Unknown(play, $"{found.Command.Command.FullName} of a count this board can't work out ({make.CountValue})");
        if (count <= 0 && from == SimCount.Given) return new List<SimAction>();
        var intoHand = MadeCards.IntoHand(make, play.Type);
        var takenBy = MadeTakenBy(play.Model);
        var madeCard = MadeCardOf(play.Model)?.GetType().Name;
        var actions = new List<SimAction>();
        foreach (var player in MadeFor(make, found, play))
        {
            var creator = MadeBy(make, play, player);
            var yours = player == play.Owner;
            actions.Add(new CardEventAction(CardEvent.Generated, count)
            {
                Card = MadeSimCard(make.Card), Indirect = true, CountFrom = from, Player = creator == play.Owner ? null : creator, JoinsHand = intoHand,
                YouMaySkip = intoHand && MadeCards.YouMaySkip(make),
                IntoHandOf = intoHand && player != creator ? player : null,
                TakenBy = yours ? takenBy : Array.Empty<string>(), MadeCard = intoHand && yours ? madeCard : null,
            });
        }
        return actions;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SimCard?> MadeSimCards = new();

    /// <summary>
    /// The card a make makes, as the hooks that hear it see it (Hook.AfterCardGeneratedForCombat's card): its class and its canonical type and
    /// tags, when the code names it (MadeCards.Make.Card, MadeCards.TransformedInto); null for a roll or a pick (its class isn't known). One per
    /// class, shared.
    /// </summary>
    private static SimCard? MadeSimCard(string? cardClass) => cardClass is not { } name ? null : MadeSimCards.GetOrAdd(name, cardName =>
        MadeCards.CanonicalOf(cardName) is { } canonical ? new SimCard(cardName, SimReaders.TypeOf(canonical)) { Tags = SimReaders.TagsOf(canonical).ToHashSet() } : null);

    /// <summary>
    /// How many cards one round of a make makes for each player: read as it runs when its count is your hand's room (The Moon, The Lovers, Crash
    /// Landing) or what a take of the play took (Storm of Steel, Stoke); else its count on the board (MadeCards.CountOn), one when it can't be read.
    /// </summary>
    private static (int Count, SimCount From) MadeCount(MadeCards.Make make, StepPlay play)
    {
        if (make.CountValue is { } value)
        {
            if (CardEffects.ReadsHandRoom(value)) return (int.MaxValue, SimCount.HandRoom);
            var takes = CardEffects.ExhaustsOfOthers(play.Type).Concat(CardEffects.Discards(play.Type));
            if (play.Takes.Count > 0 && takes.Any(take => CardEffects.CountsTaken(value, take))) return (0, SimCount.CardsTaken);
        }
        return (MadeCards.CountOn(make, play.Board, play.Program) ?? -1, SimCount.Given);
    }

    /// <summary>
    /// The players a make's cards are for (their pile's): each player of your side a loop over them goes round (Glimpse Beyond, Blade
    /// Symphony), the teammate the card is aimed at (Largesse: none with none to aim at), or the card's owner.
    /// </summary>
    private static IEnumerable<int> MadeFor(MadeCards.Make make, CardEffects.Found found, StepPlay play)
    {
        if (IsEach(make.For) && found.Loops.Any(OverPlayers)) return PlayersOf(found, play);
        if (IsTarget(make.For)) return Who.IsPlayer(play.AllyId) ? new[] { play.AllyId } : Array.Empty<int>();
        return new[] { play.Owner };
    }

    /// <summary>Who makes a make's cards for <paramref name="player"/> (its creator argument): the card's owner when it names them, else the player itself.</summary>
    private static int MadeBy(MadeCards.Make make, StepPlay play, int player) => IsOwner(make.Creator) ? play.Owner : player;

    /// <summary>
    /// The cards a card turns into after its last play (MadeCards.Way.AfterPlay: Mourning Blow's Sadness, CardModel.TransformIntoAfterPlay):
    /// made once the card play is over (CardCmd.Transform, where the card went: your hand for Mourning Blow, decision #412), never a card more.
    /// </summary>
    private static IEnumerable<SimAction> MadeAfterPlay(CardModel card) =>
        MadeCards.Of(card.GetType()).Where(make => make.By == MadeCards.Way.AfterPlay)
            .Select(make => (SimAction)new CardEventAction(CardEvent.Generated, Count: 1) { Card = MadeSimCard(make.Card), Indirect = true, TakenBy = MadeTakenBy(card) });
}
