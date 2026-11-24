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
/// on the board when nothing in the play can change it, and (a gap the card scan lists) decided on the board when it isn't translated yet.
/// Nothing decides on the board whether a step's action exists: its condition does, where the code puts it.
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
        ["CreatureCmd.Kill"] = "KillOstyAction (your Osty)",
        ["CreatureCmd.SetCurrentHp"] = "ExecuteAction (the target, on its condition)",
        ["CreatureCmd.LoseBlock"] = "StripBlockAction (the target's block, and its Artifact when the play removes it)",
        ["PowerCmd.Apply"] = "ApplyPowerAction (or FocusAction) to whom its second argument names, its amount its third",
        ["PowerCmd.Remove"] = "the target's Artifact, folded into StripBlockAction; a shot's own powers removed: not modelled",
        ["CardPileCmd.Draw"] = "CardEventAction(Drawn), or a teammate's draws, its count its own argument",
        ["CardPileCmd.DrawWithoutBlockingOnOtherPlayers"] = "the same as Draw, for each player its loop goes over",
        ["CardPileCmd.Add"] = "TakeToHandAction into your hand, TakeFromHandAction back onto your draw pile or with your whole hand",
        ["CardPileCmd.Shuffle"] = "ShuffleDrawPileAction (with the hand the play put into your draw pile)",
        ["CardPileCmd.ShuffleIfNecessary"] = "ShuffleDrawPileAction when the draw pile is empty",
        ["CardPileCmd.AutoPlayFromDrawPile"] = "AutoPlayFromDrawAction: its count, exhaust and upgrading callback",
        ["CardPileCmd.AddGeneratedCardToCombat"] = "CardEventAction(Generated, GeneratedStatus or GeneratedSadness) for its creator, one, into the pile it names (MadeCards)",
        ["CardPileCmd.AddGeneratedCardsToCombat"] = "the same, as many as its list holds: a Create's count, a card factory's count, the rounds of the loop that filled it",
        ["CardPileCmd.AddToCombatAndPreview"] = "the same, its count argument of the card it names, for the target's player",
        ["ForgeCmd.Forge"] = "the Sovereign Blade it makes when you have none (the first play's)",
        ["CardCmd.Exhaust"] = "TakeFromHandAction(Exhausted), after ExhaustFromHandAction",
        ["CardCmd.Discard"] = "TakeFromHandAction(Discarded), or the cards it looked at on your draw pile",
        ["CardCmd.DiscardAndDraw"] = "TakeFromHandAction(Discarded), then the draws",
        ["CardCmd.Transform"] = "TakeFromHandAction(Generated) in your hand, or a made card in its pile",
        ["CardCmd.TransformTo"] = "the same as Transform",
        ["PlayerCmd.GainEnergy"] = "CardEventAction(EnergyGained) to the player its second argument names; GainEnergyAsHeldAction for your own energy",
        ["PlayerCmd.GainStars"] = "CardEventAction(StarsGained)",
        ["OrbCmd.Channel"] = "OrbAction(Channel) of its orb, into the orbs of the player its argument names",
        ["OrbCmd.EvokeNext"] = "OrbAction(EvokeFront), keeping the orb when its dequeue argument says so; every orb twice (EvokeAllTwice)",
        ["OrbCmd.AddSlots"] = "OrbAction(Slots)",
        ["OrbCmd.RemoveSlots"] = "OrbAction(Slots), negative",
        ["ArrowCmd.ShootArrowAtTarget"] = "ShootAction, the attack it's given (needing the arrow), ImpaleAction",
        ["ArrowCmd.TryShootArrow"] = "ShootAction",
        ["ArrowCmd.ImpaleTarget"] = "what its delegate runs, then ImpaleAction",
        ["ArrowCmd.DislodgeArrow"] = "DislodgeAllAction (the loop around it pulls every arrow of yours out)",
        [nameof(PoisonPower) + ".Trigger"] = "TriggerPoisonAction (every enemy's poison)",
    };

    /// <summary>The game commands a card's step can call that have no simulator action yet, each with why (TRACKER).</summary>
    internal static readonly IReadOnlyDictionary<string, string> CommandsNotTranslated = new Dictionary<string, string>
    {
        ["CardCmd.Upgrade"] = "a card upgraded mid-play: its numbers aren't followed by the simulator (gap)",
        ["CardCmd.AutoPlay"] = "a card in hand or a pile played by the card (Beat Down, Knife Trap): not simulated (gap)",
        ["CardCmd.ApplyKeyword"] = "a keyword given to a card: not simulated (gap)",
        ["CardCmd.ApplySingleTurnSly"] = "Sly given to a card this turn: not simulated (gap)",
        ["CardCmd.ApplySingleTurnRetain"] = "Retain given to a card this turn (main): the turn's end reads your hand from the game (gap)",
        ["CardCmd.Enchant"] = "an enchantment given to a card: not simulated (gap)",
        ["CardPileCmd.RemoveFromDeck"] = "a card taken out of your deck: outside the fight",
        ["OstyCmd.Summon"] = "Osty summoned or given HP: not simulated (gap)",
        ["OrbCmd.Passive"] = "an orb's passive set off by a card: not simulated (gap)",
        ["PowerCmd.ModifyAmount"] = "a power's amount changed in place: not simulated (gap)",
        ["CreatureCmd.Heal"] = "HP healed: not simulated (gap)",
        ["CreatureCmd.Stun"] = "an enemy stunned by a card: not simulated (gap)",
        ["CreatureCmd.GainMaxHp"] = "max HP gained: not simulated (gap)",
        ["CreatureCmd.LoseMaxHp"] = "max HP lost: not simulated (gap)",
        ["ArrowCmd.GainArrowSlot"] = "an arrow slot gained: the simulator reads your arrows from the board (gap)",
        ["PotionCmd.TryToProcure"] = "a potion: outside the simulated fight",
        ["PlayerCmd.EndTurn"] = "the turn ended by the card: the turn's end is the forecast's",
        ["PlayerCmd.GainGold"] = "gold: outside the fight",
    };

    /// <summary>One play being translated: the card, its board, its program and what the steps have decided so far.</summary>
    private sealed class StepPlay
    {
        public required CardModel Card { get; init; }
        public required CardProgram Program { get; init; }
        public required ProgramBoard Board { get; init; }
        public required Player Me { get; init; }
        public required ICombatState Combat { get; init; }
        public required IReadOnlyDictionary<Creature, int> Ids { get; init; }
        public required SimPlay Play { get; init; }
        /// <summary>The enemy the card is aimed at (null: none).</summary>
        public Creature? Aimed { get; init; }
        /// <summary>Whose card it is (Who.You, or a teammate's auto-play).</summary>
        public int Owner { get; init; }
        /// <summary>The teammate it's aimed at (null, Who.None: none).</summary>
        public Creature? Ally { get; init; }
        public int AllyId { get; init; } = Who.None;
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
        public Type Type => Card.GetType();
        public string Name => Card.GetType().Name;
    }

    /// <summary>The play's body from its program's steps, in its code's order: the asks first, then each step's actions.</summary>
    private static List<SimAction> BodyFromSteps(StepPlay play)
    {
        var body = new List<SimAction>();
        Walk(play.Program.Steps, play, body, inner: false);
        var first = play.Asks.Where(condition => condition.At != null).DistinctBy(condition => condition.At).Select(condition => (SimAction)new AskAction(condition));
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
                case LoopStep loop:
                    Walk(loop.Body, play, into, inner);
                    break;
                case CommandStep command when !play.Consumed.Contains(command):
                    var (actions, guard) = Translate(command, play);
                    if (actions.Count == 0) break;
                    Guard(inner ? command.Condition ?? ProgramCondition.Always : guard ?? play.Found[command].Condition, actions, play, into);
                    break;
            }
    }

    /// <summary>
    /// The step's actions under its condition (decision #190): a WhenAction when the simulator asks it, left out when it's decided false on
    /// the board (the card's own values, a pick), and decided on the board when it has no translation yet (a gap the card scan lists).
    /// </summary>
    private static void Guard(ProgramCondition condition, List<SimAction> actions, StepPlay play, List<SimAction> into)
    {
        var reading = PlayConditions.Reading(play.Card, condition);
        switch (reading.Way)
        {
            case ConditionWay.Decided when reading.Holds == false:
                return;
            case ConditionWay.NotRead when ProgramEvaluator.Holds(condition, play.Board with { Target = play.Aimed }, play.Program) == false:
                return;
            case ConditionWay.Simulated:
                play.Asks.AddRange(reading.AskFirst);
                into.Add(new WhenAction(reading.Condition!, actions));
                return;
        }
        into.AddRange(actions);
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
            case ("ArrowCmd", "DislodgeArrow") when Argument(command, 0) == "cardPlay.Target":
                return (new List<SimAction> { new DislodgeAllAction() }, null);
            case ("CreatureCmd", "GainBlock"):
                return (BlocksFrom(found, play, times: null), null);
            case ("CreatureCmd", "Damage"):
                return (DamageFrom(found, play), null);
            case ("CreatureCmd", "Kill") when Argument(command, 0) == "Owner.Osty":
                return (new List<SimAction> { new KillOstyAction() }, null);
            case ("CreatureCmd", "SetCurrentHp") when Argument(command, 0) == "cardPlay.Target":
                return (new List<SimAction> { new ExecuteAction(ThresholdOf(found, play)) }, null);
            case ("CreatureCmd", "LoseBlock") when Argument(command, 1) == "cardPlay.Target":
                return (new List<SimAction> { new StripBlockAction(RemoveArtifact: play.Program.Commands.Any(other => other.Command is { DeclaringType: "PowerCmd", Name: "Remove" } && Argument(other, 0) == "cardPlay.Target")) }, null);
            case ("PowerCmd", "Apply"):
                return (PowersFrom(found, play), null);
            case (nameof(PoisonPower), "Trigger"):
                return (new List<SimAction> { new TriggerPoisonAction() }, null);
            case ("CardPileCmd", "Draw" or "DrawWithoutBlockingOnOtherPlayers"):
                return (DrawsFrom(found, play), null);
            case ("PlayerCmd", "GainEnergy"):
                return (EnergyFrom(found, play), null);
            case ("PlayerCmd", "GainStars") when Argument(command, 1) == "Owner":
                return (new List<SimAction> { new CardEventAction(CardEvent.StarsGained, Count: 1) }, null);
            case ("OrbCmd", _):
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
            case ("CardPileCmd", "AutoPlayFromDrawPile"):
                return (AutoPlayFrom(found, play), null);
            case ("CardPileCmd", "AddGeneratedCardToCombat" or "AddGeneratedCardsToCombat" or "AddToCombatAndPreview"):
            case ("ForgeCmd", "Forge"):
                return (MadeFrom(found, play), null);
        }
        return none;
    }

    // ---------------------------------------------------------------- the play's context

    /// <summary>The play's steps found once, the shots' attacks, the takes and the steps another stands for.</summary>
    private static StepPlay StepPlayOf(CardModel card, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids, SimPlay play,
        Creature? aimed, int owner, Creature? ally, int allyId, int plays)
    {
        var program = CardPrograms.Of(card.GetType());
        var stepPlay = new StepPlay
        {
            Card = card, Program = program, Board = new ProgramBoard(card), Me = me, Combat = combat, Ids = ids, Play = play,
            Aimed = aimed, Owner = owner, Ally = ally, AllyId = allyId, Plays = plays,
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
        stepPlay.Takes = TakesOf(card, stepPlay);
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
        value != null && ProgramEvaluator.Number(value, play.Board with { Target = target ?? play.Aimed }, play.Program) is { } read ? (int)read : fallback;

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
        var card = play.Card;
        var mode = ModeOf(command, card);
        if (mode == SimPlay.Target.None) return new List<SimAction>();
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
    /// (Targeting: a roll's target on a card aimed at a random enemy, Memento Mori's); with none of those, or several on different ways
    /// (Shiv's every enemy with Fan of Knives), the card's own targeting.
    /// </summary>
    private static SimPlay.Target ModeOf(CommandStep command, CardModel card)
    {
        var byCard = card.TargetType switch
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
        var growth = play.Plays > 1 && !fixedNumbers ? PlayGrowth.Of(card) : PlayGrowth.Growth.None;
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
    /// attack (DamageAction: Capture Spirit).
    /// </summary>
    private static List<SimAction> DamageFrom(CardEffects.Found found, StepPlay play)
    {
        var command = found.Command;
        var card = play.Card;
        var whom = ValueAt(command, 1);
        if (IsYou(whom)) return new List<SimAction> { new LoseHpAction(Number(play, ValueAt(command, 2), Sts2State.Var(card, "HpLoss"))) };
        var key = ValueKey(ValueAt(command, 2));
        // An attack card's damage to every enemy (Echoing Slash), or to its target (Omnislice, a shot's: Rain Shot) is simulated as its attack
        // (with the splash or the repeats its table names: docs/CARD_PROGRAMS.md A5), as the adapter has; CreatureCmd.Damage runs no
        // BeforeAttack or AfterAttack in the game (TRACKER).
        if (card.Type == CardType.Attack && IsEveryEnemy(whom))
            return new List<SimAction> { AttackAction(card, key, SimPlay.Target.All, hits: 1, byOsty: false, play) };
        if (!IsTarget(whom) || play.Aimed is not { } target || !play.Ids.TryGetValue(target, out var targetId)) return new List<SimAction>();
        if (card.Type == CardType.Attack || InsideShot(command, play))
            return new List<SimAction> { AttackAction(card, key, SimPlay.Target.Single, hits: 1, byOsty: false, play) with { NeedsArrow = InsideShot(command, play) } };
        if (key == null || !card.DynamicVars.TryGetValue(key, out var value) || value is not DamageVar damage) return new List<SimAction>();
        return new List<SimAction>
        {
            new DamageAction(new DamageSpec(damage.BaseValue, SimReaders.Props(damage.Props))
                { Captured = new Dictionary<int, CapturedTerms> { [targetId] = SimReaders.Damage(play.Me.RunState, play.Combat, card, play.Me.Creature, target, damage.BaseValue, damage.Props) } }),
        };
    }

    /// <summary>The step runs inside a shot's delegate (ArrowCmd.ImpaleTarget's or ShootArrowAtTarget's), only with an arrow.</summary>
    private static bool InsideShot(CommandStep command, StepPlay play) =>
        play.Program.Commands.Any(other => other.Command.DeclaringType == "ArrowCmd" && other.Inner.Contains(command));

    /// <summary>The HP at or below which the target is set to 0 (its condition's CurrentHp comparison); any HP with none.</summary>
    private static int ThresholdOf(CardEffects.Found found, StepPlay play)
    {
        foreach (var literal in found.Condition.Literals)
            if (literal is { Holds: true, Test: CompareValue { Op: Comparison.LessOrEqual, Left: QueryValue { Name: "CurrentHp" }, Right: var threshold } })
                return Number(play, threshold, Sts2State.Var(play.Card, "Threshold"));
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
        var card = play.Card;
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
        if ((IsTarget(gainer) || gainer is QueryValue { Name: "Creature" } && IsTarget(((QueryValue)gainer).Of)) && play.AllyId != Who.None && play.Ally != null)
            return BlockFor(found, play, gains, play.AllyId, play.Ally) is { } allyBlock ? new List<SimAction> { allyBlock } : new List<SimAction>();
        return new List<SimAction>();
    }

    private static GainBlockAction? BlockFor(CardEffects.Found found, StepPlay play, int gains, int to, Creature gainer)
    {
        var card = play.Card;
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
        if (key != null && card.DynamicVars.TryGetValue(key, out var named) && named is not CalculatedVar) baseValue = named.BaseValue;
        else if (card.DynamicVars.TryGetValue("CalculatedBlock", out var dynamicVar) && dynamicVar is CalculatedVar calc) baseValue = SafeDecimal(() => calc.Calculate(null));
        else if (card.DynamicVars.TryGetValue("Block", out var otherDynamicVar)) baseValue = otherDynamicVar.BaseValue;
        else return new GainBlockAction(gains, CardBlock(card, combat, me), SimProps.Move) { Fixed = true, To = to };
        var growth = play.Plays > 1 ? PlayGrowth.Of(card) : PlayGrowth.Growth.None;
        return new GainBlockAction(gains, baseValue, SimProps.Move)
        {
            Captured = SimReaders.Block(combat, card, gainer, baseValue, ValueProp.Move), To = to, GrowsEachPlay = growth.Block,
            EnchantFirstPlayOnly = growth.EnchantFirstPlayOnly,
        };
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
    /// argument on the board (the target's own amount of it as it lands: Molten Fist; your own: Cho Ki's Stretch kept for replays). Only
    /// powers the simulator reads (modelled ones, the ones it keeps as numbers, poison, Artifact, a debuff on enemies); Strength and a
    /// temporary Strength on enemies as Strength (a debuff's sign); Focus and a temporary Focus as Focus.
    /// </summary>
    private static List<SimAction> PowersFrom(CardEffects.Found found, StepPlay play)
    {
        var command = found.Command;
        var card = play.Card;
        if (command.Command.GenericArguments is not [var powerName] || PowerType(card.GetType(), powerName) is not { } type) return new List<SimAction>();
        var whom = ValueAt(command, 1);
        var amount = Number(play, ValueAt(command, 2), AmountOf(card, type));
        // Whom it lands on: (where, which creature) for each.
        var targets = new List<(ApplyPowerAction.Where To, int Creature)>();
        var times = 1;
        if (IsYou(whom)) targets.Add((ApplyPowerAction.Where.You, 0));
        else if (IsTarget(whom))
        {
            if (card.TargetType != TargetType.AnyAlly) targets.Add((ApplyPowerAction.Where.Target, 0));
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
        var actions = new List<SimAction>();
        foreach (var (to, creature) in targets)
            if (PowerTo(found, play, type, powerName, amount, to, creature, times) is { } action) actions.Add(action);
        return actions;
    }

    /// <summary>
    /// The step's power to one creature (PowersFrom): Strength and a temporary Strength on enemies as Strength (a debuff's sign: issue #371,
    /// one Artifact charge takes all of it), Focus and a temporary Focus on you as Focus, any other power the simulator reads (modelled ones,
    /// the ones it keeps as numbers, poison, Artifact, a debuff on enemies). Null for one it doesn't read.
    /// </summary>
    private static SimAction? PowerTo(CardEffects.Found found, StepPlay play, Type type, string powerName, int amount, ApplyPowerAction.Where to, int creature, int times)
    {
        var command = found.Command;
        var card = play.Card;
        var simPowerName = SimPowerName(type);
        var toEnemies = to is ApplyPowerAction.Where.Target or ApplyPowerAction.Where.AllEnemies or ApplyPowerAction.Where.RandomEnemy;
        if (toEnemies && (type == typeof(StrengthPower) || typeof(TemporaryStrengthPower).IsAssignableFrom(type)))
        {
            var sign = typeof(TemporaryStrengthPower).IsAssignableFrom(type) && SimReaders.IsDebuffType(type) ? -1 : 1;
            return new ApplyPowerAction(PowerId.Strength, sign * amount) { To = to, Debuff = sign * amount < 0 };
        }
        if (to == ApplyPowerAction.Where.You && FocusSignOf(type) is var focusSign && focusSign != 0)
            return amount != 0 ? new FocusAction(focusSign * amount) : null;
        var simulated = SimModels.Has(simPowerName) || SelfPowers.Contains(simPowerName) || simPowerName == nameof(PoisonPower) || simPowerName == nameof(ArtifactPower);
        var debuff = SimReaders.IsDebuffType(type);
        if (debuff ? !toEnemies && !simulated : !simulated) return null;
        if (simPowerName == nameof(StretchPower) && to == ApplyPowerAction.Where.You && CardEffects.KeepsStretchForReplays(card.GetType()))
            return new ApplyPowerAction(new PowerId(simPowerName), Amount: 0) { To = to, CopiesOwn = true };
        var amountOf = command.Values.Count > 2 ? ConditionReader.TargetPowerAmountOf(command.Values[2]) : null;
        return new ApplyPowerAction(new PowerId(simPowerName), amount)
        {
            To = to, Creature = creature, Times = times, Debuff = debuff && toEnemies,
            CopiesTarget = toEnemies && amountOf == powerName,
            TargetsAmountOf = amountOf != null && !(toEnemies && amountOf == powerName) ? PowerId.Of(amountOf) : null,
        };
    }
    /// <summary>The power type a step names, among the ones the card's code applies.</summary>
    private static Type? PowerType(Type cardType, string name) =>
        Diagnostics.CardCoverage.PowersApplied(cardType).FirstOrDefault(type => type.Name == name)
        ?? typeof(PowerModel).Assembly.GetTypes().FirstOrDefault(type => type.Name == name && typeof(PowerModel).IsAssignableFrom(type));

    /// <summary>A power's amount when the step's argument can't be read: the card's PowerVar for it, its plain value, or 1.</summary>
    private static int AmountOf(CardModel card, Type type)
    {
        foreach (var dynamicVar in card.DynamicVars.Values)
            if (IsPowerVar(dynamicVar, type)) return (int)dynamicVar.BaseValue;
        return PlainSelfPowerAmount(card, type) ?? 1;
    }

    private static bool IsPowerVar(DynamicVar dynamicVar, Type power) =>
        dynamicVar.GetType() is { IsGenericType: true } type && type.GetGenericTypeDefinition() == typeof(PowerVar<>) && type.GetGenericArguments()[0] == power;

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
        var yours = CardEffects.DrawsForYou(play.Type).FirstOrDefault(draw => ReferenceEquals(draw.Command, command));
        if (yours != null && (players == null || players.Contains(Who.You))) actions.AddRange(YourDraws(yours, play));
        var cards = Number(play, ValueAt(command, 1), Sts2State.Var(play.Card, "Cards"));
        var mates = Teammates(play.Me, play.Combat);
        for (var teammateIndex = 0; teammateIndex < mates.Count; teammateIndex++)
        {
            if (mates[teammateIndex].Creature is not { IsAlive: true } mate) continue;
            var who = Who.Teammate(teammateIndex);
            if (players != null ? !players.Contains(who) : yours != null || mate != play.Ally) continue;
            // Their draws: as many as their hand has room for and their piles hold; a shuffle when the draw pile runs out.
            var hand = PileType.Hand.GetPile(mates[teammateIndex]).Cards.Count;
            var drawPile = PileType.Draw.GetPile(mates[teammateIndex]).Cards.Count;
            var discard = PileType.Discard.GetPile(mates[teammateIndex]).Cards.Count;
            var drawn = Math.Min(cards, Math.Min(Math.Max(0, CardPile.MaxCardsInHand - hand), drawPile + discard));
            if (drawn > drawPile) actions.Add(new CardEventAction(CardEvent.Shuffled, Count: 1) { Player = who, Indirect = true });
            if (drawn > 0) actions.Add(new CardEventAction(CardEvent.Drawn, drawn) { Player = who, Indirect = true });
        }
        return actions;
    }

    /// <summary>One draw of yours: a count of the cards the play took (Calculated Gamble) or of your hand's room (Scrawl) read as it runs; else its own count.</summary>
    private static IEnumerable<SimAction> YourDraws(CardEffects.DrawForYou draw, StepPlay play)
    {
        var takes = CardEffects.ExhaustsOfOthers(play.Type).Concat(CardEffects.Discards(play.Type)).ToList();
        if (takes.Any(take => CardEffects.CountsTaken(draw.Count, take)))
        {
            yield return new CardEventAction(CardEvent.Drawn, Count: 0) { Indirect = true, JoinsHand = true, CountFrom = SimCount.CardsTaken };
            yield break;
        }
        if (CardEffects.ReadsHandRoom(draw.Count))
        {
            yield return new CardEventAction(CardEvent.Drawn, int.MaxValue) { Indirect = true, JoinsHand = true, CountFrom = SimCount.HandRoom };
            yield break;
        }
        var drawn = BoardCounts.DrawCount(play.Card, draw);
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
        var amount = Number(play, amountValue, Sts2State.Var(play.Card, "Energy")) * RoundsOf(found, play, OverPlayers);
        if (amount <= 0) return new List<SimAction>();
        var whom = ValueAt(command, 1);
        CardEventAction Gain(int to) => new(CardEvent.EnergyGained, amount) { Player = to };
        if (IsOwnerPlayer(whom)) return new List<SimAction> { Gain(play.Owner) };
        if (IsTarget(whom)) return Who.KindOf(play.AllyId) == CreatureKind.Teammate ? new List<SimAction> { Gain(play.AllyId) } : new List<SimAction>();
        if (!IsEach(whom) || !found.Loops.Any(OverPlayers)) return new List<SimAction>();
        // Teammates are numbered from your side: a teammate's own card (Howl from Beyond) giving you energy isn't followed (it'd need their
        // numbering of the players).
        var players = PlayersOf(found, play);
        return players.Where(player => player == Who.You || play.Owner == Who.You).Select(player => (SimAction)Gain(player == Who.You ? play.Owner : player)).ToList();
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
                if (CardEffects.OrbOf(command) is not { } kind) return none;
                var to = command.Arguments.Contains("cardPlay.Target.Player") ? play.AllyId : play.Owner;
                if (to == Who.None) return none;
                var times = RoundsOf(found, play);
                // A loop whose bound is a calculated value on the fight's log (Voltaic's Lightning channeled this fight) follows the ledger.
                var logged = found.Loops.Select(LoopBoundKey).OfType<string>().Select(key => HistoryLedger.LoggedOf(play.Card, key, play.Combat)).FirstOrDefault(amount => amount != null);
                return times > 0 || logged != null ? (new List<SimAction> { new OrbAction(OrbAction.Op.Channel) { Orb = kind, Times = times, To = to, TimesLogged = logged } }, null) : none;
            }
            case "EvokeNext":
            {
                var guard = WithoutOrbTests(found.Condition);
                if (found.Loops.FirstOrDefault(loop => loop.While?.Literals.Any(literal => literal.Test.Parts().Any(part => part is QueryValue { Name: "Orbs" })) == true) is { } overOrbs)
                {
                    var perOrb = new CardProgram("", overOrbs.Body).Commands.Count(other => other.Command is { DeclaringType: "OrbCmd", Name: "EvokeNext" });
                    // The loop's evokes are one action: every orb, that many times.
                    foreach (var other in new CardProgram("", overOrbs.Body).Commands.Where(other => !ReferenceEquals(other, command))) play.Consumed.Add(other);
                    return (new List<SimAction> { perOrb == 2 ? new OrbAction(OrbAction.Op.EvokeAllTwice) : new NotModelledAction($"every orb evoked {perOrb} times") }, guard);
                }
                var keeps = ValueAt(command, 2) is ConstantValue { Value: 0 };
                var rounds = RoundsOf(found, play);
                return rounds > 0 ? (new List<SimAction> { new OrbAction(OrbAction.Op.EvokeFront) { Times = rounds, Keeps = keeps } }, guard) : none;
            }
            case "AddSlots" or "RemoveSlots":
            {
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
        var card = play.Card;
        // The cards it discards that aren't in your hand: the ones it looked at on top of your draw pile (Ki Chant), on the board.
        if (take is { Way: CardEffects.TakeWay.LookedAt } && command.Command.Name == "Discard" && BoardCounts.DiscardedOutsideHand(card) is > 0 and var looked)
            actions.Add(new CardEventAction(CardEvent.Discarded, looked) { Indirect = true });
        // Transformed outside your hand (Charge, Seance: your draw pile): a created card each, in place; none certain when you choose how many.
        if (take is { PickPile: not (null or PileType.Hand) } && command.Command.Name.StartsWith("Transform", StringComparison.Ordinal) && !take.ChoosesHowMany
            && BoardCounts.TransformsOutsideHand(card) is > 0 and var transformed)
            actions.Add(new CardEventAction(CardEvent.Generated, transformed) { Indirect = true });
        // A Sly card it discards without a choice (the whole hand: CardCmd.Discard's AutoPlay) plays itself: not simulated yet (tracker). One
        // you pick is a card, never a particular one (decision #188).
        if (take is { Way: CardEffects.TakeWay.Each } && command.Command.Name is "Discard" or "DiscardAndDraw" && BoardCounts.DiscardsSlyWithoutPick(play.Card))
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
    private static Dictionary<CommandStep, List<SimAction>> TakesOf(CardModel card, StepPlay play)
    {
        var type = card.GetType();
        var program = play.Program;
        var board = play.Board;
        var result = new Dictionary<CommandStep, List<SimAction>>(ReferenceEqualityComparer.Instance);
        int Count(ProgramValue? value) => value != null && ProgramEvaluator.Number(value, board, program) is { } read ? Math.Max(0, (int)read) : 1;
        var exhausts = CardEffects.ExhaustsOfOthers(type);
        foreach (var (take, key, cardEvent, eventIfNot, leaves) in BoardCounts.TakesInOrder(card))
        {
            var actions = result.TryGetValue(take.Command, out var already) ? already : result[take.Command] = new List<SimAction>();
            if (eventIfNot != null && CardEffects.Discards(type).FirstOrDefault(discard => ReferenceEquals(discard.Pick, take.Pick)) is { } shared) play.Consumed.Add(shared.Command);
            switch (take.Way)
            {
                case CardEffects.TakeWay.Picked when take.PickPile is PileType.Draw:
                    if (cardEvent == CardEvent.Exhausted)
                        actions.Add(new TakeFromHandAction(HandTakeWay.Pick) { Event = cardEvent, FromDrawPile = true, Most = Count(take.PerPick), Matching = BoardCounts.ExhaustOthers(card, new List<CardModel>(), DrawPileOfCard(card)) });
                    break;
                case CardEffects.TakeWay.Picked when take.PickPile is null or PileType.Hand:
                {
                    var filtered = !take.ItemTest.IsAlways || eventIfNot != null;
                    actions.Add(new TakeFromHandAction(HandTakeWay.Pick)
                    {
                        Event = cardEvent, EventIfNot = eventIfNot, Leaves = leaves, Most = Count(take.PerPick),
                        Filter = filtered ? key : null, Matching = filtered ? BoardCounts.MatchingAtStart(card, take, everyPile: false) : 0,
                    });
                    break;
                }
                case CardEffects.TakeWay.Random or CardEffects.TakeWay.Each:
                {
                    var way = take.Way == CardEffects.TakeWay.Random ? HandTakeWay.Random : take.Single ? HandTakeWay.Pick : HandTakeWay.Each;
                    var most = take.Way == CardEffects.TakeWay.Random ? Count(take.RollCount) : 1;
                    if (CardEffects.IsYourWholeHand(take.Items) && take.ItemTest.IsAlways)
                        actions.Add(new TakeFromHandAction(way) { Event = cardEvent, Leaves = leaves, Most = most, EachCard = EachCardOf(take, play) });
                    else if (BoardCounts.TakesFromEveryPile(card, take))
                    {
                        // Every pile's (Flak Cannon): its hand's share leaves first, then each card of every pile sets off its event.
                        actions.Add(new TakeFromHandAction(way) { Leaves = leaves, Most = most, Filter = key, Matching = BoardCounts.MatchingAtStart(card, take, everyPile: false) });
                        actions.Add(new TakeFromHandAction(way) { Event = cardEvent, Leaves = false, Most = most, Filter = "all:" + key, Matching = BoardCounts.MatchingAtStart(card, take, everyPile: true), EachCard = EachCardOf(take, play) });
                    }
                    else
                        actions.Add(new TakeFromHandAction(way)
                        {
                            Event = cardEvent, Leaves = leaves, Most = most, Filter = key, Matching = BoardCounts.MatchingAtStart(card, take, everyPile: false), EachCard = EachCardOf(take, play),
                        });
                    break;
                }
            }
        }
        // What acts when exhausted among the cards the play's exhausts take from your hand (Drum of Battle), before the first.
        if (exhausts.Select(exhaust => result.GetValueOrDefault(exhaust.Command)).FirstOrDefault(actions => actions is { Count: > 0 }) is { } firstActions && ExhaustPickOf(card) is { FromDrawPile: false } exhaustPick)
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
            .FirstOrDefault(candidate => new CardProgram("", candidate.Body).Commands.Any(command => ReferenceEquals(command, take.Command)));
        if (loop == null) return Array.Empty<SimAction>();
        var actions = new List<SimAction>();
        foreach (var other in new CardProgram("", loop.Body).Commands.Where(command => command.Command is { DeclaringType: "CreatureCmd", Name: "GainBlock" }))
        {
            play.Consumed.Add(other);
            actions.AddRange(BlocksFrom(play.Found[other], play, times: 1));
        }
        return actions;
    }

    /// <summary>
    /// CardPileCmd.Add: into your hand from your piles (HandTakeOf: TakeToHandAction per pile, a choice of how many is unknown); back onto your
    /// draw pile after a pick from your hand (Glimmer, Thinking Ahead: the pick's most); your whole hand moved (Reboot: it leaves, setting
    /// off nothing). Anything else (a pick from your discard pile onto your draw pile, into a teammate's hand) changes nothing simulated.
    /// </summary>
    private static (List<SimAction> Actions, ProgramCondition? Guard) AddsFrom(CardEffects.Found found, StepPlay play)
    {
        var command = found.Command;
        var card = play.Card;
        var actions = new List<SimAction>();
        var intoHand = CardEffects.TakesIntoHandOf(play.Type).FirstOrDefault(take => ReferenceEquals(take.Command, command));
        if (intoHand != null)
        {
            if (intoHand.ChoosesHowMany)
            {
                if (BoardCounts.HandTakeChosenOf(play.Card, Sts2State.Hand(play.Me).Count)) actions.Add(new NotModelledAction("you choose how many cards it takes into your hand"));
                return (actions, intoHand.PlayTest);
            }
            if (ReferenceEquals(CardEffects.TakesIntoHandOf(play.Type)[0].Command, command)) actions.AddRange(TakesToHand(play.Card, play.Me));
            return (actions, intoHand.PlayTest);
        }
        var pile = Argument(command, 1);
        var drawPile = ((int)PileType.Draw).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (pile == drawPile && found.Pick is { Command.Name: "FromHand" } pick)
        {
            var take = new CardEffects.CardsTaken(command, found.Condition, CardEffects.TakeWay.Picked, Items: null, pick);
            actions.Add(new TakeFromHandAction(HandTakeWay.Pick) { Most = Number(play, take.PerPick, fallback: 1) });
            return (actions, ProgramCondition.Always);
        }
        if (IsEach(ValueAt(command, 0)) && found.Loops.Count > 0 && found.Loops[^1] is var loop && CardEffects.IsYourWholeHand(loop.Items))
        {
            actions.Add(new TakeFromHandAction(HandTakeWay.Each));
            return (actions, null);
        }
        return (actions, null);
    }

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
        // The hand the play moved into your draw pile before it (Reboot), by profile.
        var handIn = play.Program.Commands.TakeWhile(other => !ReferenceEquals(other, command))
            .Any(other => other.Command is { DeclaringType: "CardPileCmd", Name: "Add" } && IsEach(ValueAt(other, 0)) && play.Found[other].Loops is [.., var loop] && CardEffects.IsYourWholeHand(loop.Items))
            ? PileType.Hand.GetPile(play.Me).Cards.Where(other => other != play.Card).Select(other => DrawProfiles.GetValueOrDefault(other)).OfType<SimDrawCard>()
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
        var (count, exhausts, upgrades) = BoardCounts.AutoPlayOf(found.Command, play.Card);
        var countsTaken = found.Command.Values.Count > 2 && CardEffects.ExhaustsOfOthers(play.Type).Any(take => CardEffects.CountsTaken(found.Command.Values[2], take));
        return new List<SimAction> { new AutoPlayFromDrawAction(count) { ForceExhaust = exhausts, Upgrade = upgrades, CountFrom = countsTaken ? SimCount.CardsTaken : SimCount.Given } };
    }

    /// <summary>
    /// How a card that exhausts others takes them (ExhaustFromHandAction: what a card that acts when exhausted, Drum of Battle, does
    /// then), read from its program: a pick is Chosen (from your draw pile for Cleanse), a roll Random, a loop over a collection All;
    /// a filter by type (Second Wind's non-Attacks, Thrash's Attacks, The Hermit's Curse) is its Only or Except. A filter that isn't a
    /// type (Growth's Sadness, Florescence's Shoot cards, Flak Cannon's statuses outside the exhaust pile) takes no card in your hand
    /// that acts when exhausted unless one passes it, evaluated on the board: then none is followed. Null for a card that exhausts no
    /// other card.
    /// </summary>
    internal static ExhaustFromHandAction? ExhaustPickOf(CardModel card)
    {
        var program = CardPrograms.Of(card.GetType());
        var board = new ProgramBoard(card);
        foreach (var exhaust in CardEffects.ExhaustsOfOthers(card.GetType()))
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
        if (MadeCards.Of(play.Type).FirstOrDefault(make => ReferenceEquals(make.Found?.Command, found.Command)) is not { } make) return new List<SimAction>();
        if (make.By == MadeCards.Way.Forged)
            return MadeCards.HasBlade(play.Card) ? new List<SimAction>()
                : new List<SimAction> { new FirstPlayOnly(new SimAction[] { new CardEventAction(CardEvent.Generated, Count: 1) { Indirect = true, JoinsHand = true } }) };
        var (count, from) = MadeCount(make, play);
        if (count <= 0 && from == SimCount.Given) return new List<SimAction>();
        var intoHand = MadeCards.IntoHand(make, play.Type);
        var takenBy = MadeTakenBy(play.Card);
        var madeCard = MadeCardOf(play.Card)?.GetType().Name;
        var actions = new List<SimAction>();
        foreach (var player in MadeFor(make, found, play))
        {
            var creator = MadeBy(make, play, player);
            var yours = player == play.Owner;
            actions.Add(new CardEventAction(EventOf(make), count)
            {
                Indirect = true, CountFrom = from, Player = creator == play.Owner ? null : creator, JoinsHand = intoHand,
                IntoHandOf = intoHand && player != creator ? player : null,
                TakenBy = yours ? takenBy : Array.Empty<string>(), MadeCard = intoHand && yours ? madeCard : null,
            });
        }
        return actions;
    }

    /// <summary>A make's card event: a made status, a made Sadness (Fertilizer, All Is Well) or any other made card.</summary>
    private static CardEvent EventOf(MadeCards.Make make) => MadeCards.KindOf(make) switch
    {
        MadeCards.Kind.Status => CardEvent.GeneratedStatus,
        MadeCards.Kind.Sadness => CardEvent.GeneratedSadness,
        _ => CardEvent.Generated,
    };

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
        return (MadeCards.CountOn(make, play.Board, play.Program) ?? 1, SimCount.Given);
    }

    /// <summary>
    /// The players a make's cards are for (their pile's): each player of your side a loop over them goes round (Glimpse Beyond, Blade
    /// Symphony), the teammate the card is aimed at (Largesse: none with none to aim at), or the card's owner.
    /// </summary>
    private static IEnumerable<int> MadeFor(MadeCards.Make make, CardEffects.Found found, StepPlay play)
    {
        if (IsEach(make.For) && found.Loops.Any(OverPlayers)) return PlayersOf(found, play);
        if (IsTarget(make.For)) return Who.KindOf(play.AllyId) == CreatureKind.Teammate ? new[] { play.AllyId } : Array.Empty<int>();
        return new[] { play.Owner };
    }

    /// <summary>Who makes a make's cards for <paramref name="player"/> (its creator argument): the card's owner when it names them, else the player itself.</summary>
    private static int MadeBy(MadeCards.Make make, StepPlay play, int player) => IsOwner(make.Creator) ? play.Owner : player;

    /// <summary>
    /// The cards a card turns into after its last play (MadeCards.Way.AfterPlay: Mourning Blow's Sadness, CardModel.TransformIntoAfterPlay):
    /// made once the card play is over (CardCmd.Transform, where the card goes), never into your hand.
    /// </summary>
    private static IEnumerable<SimAction> MadeAfterPlay(CardModel card) =>
        MadeCards.Of(card.GetType()).Where(make => make.By == MadeCards.Way.AfterPlay)
            .Select(make => (SimAction)new CardEventAction(EventOf(make), Count: 1) { Indirect = true, TakenBy = MadeTakenBy(card) });
}
