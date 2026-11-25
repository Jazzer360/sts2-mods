using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using SpireQoL.Procs.Programs;
using static SpireQoL.Procs.Programs.StepValues;
using Sts2Mods.Core.Game;

namespace SpireQoL.Sim;

/// <summary>
/// A potion's use built from its code (decision #313): its OnUse read with the card program reader (PotionPrograms), each command step translated
/// into the simulator's action for that command, in its code's order, its amounts the potion's own values and whom it acts on what its arguments
/// name on the board (its target, you, a teammate, every enemy you can hit). A command, a whom, an amount or a condition it can't follow makes the
/// use unknown where it runs (a NotModelledAction with why: `?` and a range), never left out. The commands a potion's code calls are held to this
/// translation, on both branches, by the potion scan (PotionScanTests).
/// </summary>
public static partial class SimAdapter
{
    /// <summary>The game commands a potion's step is translated from (PotionStep): the potion scan holds every potion's commands to these or to the card's lists of commands not followed.</summary>
    internal static readonly IReadOnlySet<string> PotionCommandsTranslated = new HashSet<string>(StringComparer.Ordinal)
    {
        "CreatureCmd.Damage", "CreatureCmd.GainBlock", "CreatureCmd.Heal", "PowerCmd.Apply", "CardPileCmd.Draw", "PlayerCmd.GainEnergy", "PlayerCmd.GainStars",
        "OrbCmd.Channel", "OrbCmd.AddSlots", "OstyCmd.Summon",
    };

    /// <summary>
    /// The potions you can drink or throw in a fight (decision #313): CombatOnly and AnyTime, not one that acts on its own (Automatic: Fairy in a
    /// Bottle, whose revive the simulator already follows).
    /// </summary>
    public static bool Usable(PotionModel potion) => potion.Usage is MegaCrit.Sts2.Core.Entities.Potions.PotionUsage.CombatOnly or MegaCrit.Sts2.Core.Entities.Potions.PotionUsage.AnyTime;

    /// <summary>
    /// The use of <paramref name="potion"/> aimed at <paramref name="targetId"/> (an enemy's id, Who.You, a teammate it's thrown at, Who.None for
    /// none), as a SimPlay with its Potion set: its body from its code, the listeners of its hooks the simulator can't follow made unknown.
    /// </summary>
    public static SimPlay Potion(PotionModel potion, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids, int targetId)
    {
        using var reads = Sts2State.TrackReads();
        var name = potion.GetType().Name;
        var play = new SimPlay
        {
            Name = name, TargetId = targetId, HandDelta = 0,
            Potion = new SimPotionUse(name, Who.You, targetId, Fairy: CountsAsFairy(potion)),
        };
        var target = CreatureOf(targetId, me, combat, ids);
        var use = new PotionUse(potion, PotionPrograms.Of(potion.GetType()), ProgramBoard.Of(potion) with { Target = target }, me, combat, ids, target);
        if (use.Program.Steps.Count == 0 && PotionRuns(potion))
            play.Body.Add(new NotModelledAction($"{name}'s OnUse isn't read"));
        else PotionSteps(use.Program.Steps, use, play.Body, rounds: 1);
        // A listener whose class answers a potion hook the simulator doesn't run for it: what it does is unknown (a read model, or a hand-written one, runs).
        foreach (var hook in new[] { nameof(AbstractModel.BeforePotionUsed), nameof(AbstractModel.AfterPotionUsed) })
            if (UnfollowedListener(me, combat, hook) is { } listener) play.Body.Insert(0, new NotModelledAction($"{listener}'s {hook} isn't followed"));
        if (reads.Unread.Count > 0) play.Body.Insert(0, new UnreadAction(reads.Unread[0]));
        return play;
    }

    /// <summary>The potion's class overrides OnUse (a potion with none, the game's deprecated one, does nothing).</summary>
    private static bool PotionRuns(PotionModel potion) =>
        potion.GetType().GetMethod("OnUse", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public) is { } onUse
        && onUse.DeclaringType != typeof(PotionModel);

    /// <summary>The aim a creature is (an enemy's id, you, a teammate; Who.None for none or one the simulator doesn't name): a potion's target as the simulator keys it.</summary>
    public static int AimOf(Creature? target, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids)
    {
        if (target == null) return Who.None;
        if (ReferenceEquals(target, me.Creature)) return Who.You;
        if (ids.TryGetValue(target, out var enemyId)) return enemyId;
        var index = Teammates(me, combat).FindIndex(mate => ReferenceEquals(mate.Creature, target));
        return index >= 0 ? Who.Teammate(index) : Who.None;
    }

    /// <summary>The creature an aim names (an enemy by its id, you, a teammate), or null.</summary>
    private static Creature? CreatureOf(int id, Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids)
    {
        if (id == Who.You) return me.Creature;
        if (Who.TeammateIndex(id) is var teammateIndex and >= 0)
            return Teammates(me, combat) is var mates && teammateIndex < mates.Count ? mates[teammateIndex].Creature : null;
        return ids.FirstOrDefault(entry => entry.Value == id).Key;
    }

    /// <summary>The simulator's id for a creature or a player (an enemy's id, you, a teammate); null for any other.</summary>
    private static int? IdOf(object? who, PotionUse use)
    {
        var creature = who switch
        {
            Creature direct => direct,
            Player player => player.Creature,
            _ => null,
        };
        if (creature == null) return null;
        if (ReferenceEquals(creature, use.Me.Creature)) return Who.You;
        if (use.Ids.TryGetValue(creature, out var enemyId)) return enemyId;
        var mates = Teammates(use.Me, use.Combat);
        var index = mates.FindIndex(mate => ReferenceEquals(mate.Creature, creature));
        return index >= 0 ? Who.Teammate(index) : null;
    }

    /// <summary>One potion's use being translated: the potion, its program, the board its values are read on, and the creature it's aimed at.</summary>
    private sealed record PotionUse(PotionModel Potion, ModelProgram Program, ProgramBoard Board, Player Me, ICombatState Combat, IReadOnlyDictionary<Creature, int> Ids, Creature? Target)
    {
        public string Name => Potion.GetType().Name;

        /// <summary>OnUse's own parameter: the target it's given.</summary>
        public IReadOnlyDictionary<string, object?> Parameters => new Dictionary<string, object?> { ["target"] = Target };

        /// <summary>A value on the board (a number as a decimal, a creature, a value object); null when it can't be told.</summary>
        public object? Value(ProgramValue? value) => value == null ? null : ProgramEvaluator.Value(value, Board, Program, Parameters);

        public decimal? Number(ProgramValue? value) => Value(value) switch
        {
            decimal number => number,
            int number => number,
            DynamicVar dynamicVar => dynamicVar.BaseValue,
            _ => null,
        };

        public bool? Holds(ProgramCondition condition) => ProgramEvaluator.Holds(condition, Board, Program, Parameters);
    }

    /// <summary>Each step in order, a loop's body once per round it goes on the board (a command on a pick's cards unknown), a step under a condition the board decides.</summary>
    private static void PotionSteps(IEnumerable<ProgramStep> steps, PotionUse use, List<SimAction> into, int rounds)
    {
        foreach (var step in steps)
        {
            if (step.Condition is { } condition && !condition.IsAlways)
            {
                switch (use.Holds(condition))
                {
                    case false: continue;
                    case null:
                        // A test of what a pick of yours or a roll gave (the card you choose, the card made at random): what follows is that card's.
                        var onPick = condition.Literals.Any(literal => literal.Test.Parts().Any(part => part is ResultValue));
                        into.Add(new NotModelledAction(onPick ? $"{use.Name}: a step on what a pick of yours or a roll gives ({StepName(step)})"
                            : $"{use.Name}: a step on a test the simulator doesn't ask ({condition})"));
                        continue;
                }
            }
            switch (step)
            {
                case LoopStep loop:
                    if (PotionRounds(loop, use) is not { } times) into.Add(new NotModelledAction($"{use.Name}: a loop the simulator can't count ({loop.Over})"));
                    else if (times > 0) PotionSteps(loop.Body, use, into, rounds * times);
                    break;
                case CommandStep command:
                    var actions = PotionStep(command, use);
                    for (var round = 0; round < rounds; round++) into.AddRange(actions);
                    break;
                // A pick of yours (CardSelectCmd): what's done with the cards picked is the commands on its result (unknown below).
                case ChoiceStep:
                    break;
                case Procs.Programs.UnknownStep unknown:
                    into.Add(new NotModelledAction($"{use.Name}: {unknown.Reason}"));
                    break;
            }
        }
    }

    /// <summary>What a step does, by its command (a loop's, its first command's).</summary>
    private static string StepName(ProgramStep step) => step switch
    {
        CommandStep command => command.Command.FullName,
        LoopStep loop => new ModelProgram("", loop.Body).Commands.FirstOrDefault()?.Command.FullName ?? "a loop",
        _ => step.ToString(),
    };

    /// <summary>How many rounds a loop of the potion's code goes on the board (its collection's count, its counter's bound); null when it can't be told.</summary>
    private static int? PotionRounds(LoopStep loop, PotionUse use)
    {
        if (loop.Items != null && loop.Over.StartsWith("each of", StringComparison.Ordinal))
            return use.Value(loop.Items) is System.Collections.IEnumerable items and not string ? items.Cast<object?>().Count() : null;
        if (loop.While is { Terms: [[{ Holds: true, Test: CompareValue { Op: Comparison.Less or Comparison.LessOrEqual } bound }]] } && use.Number(bound.Right) is { } most)
            return Math.Max(0, (int)most + (bound.Op == Comparison.LessOrEqual ? 1 : 0));
        return null;
    }

    /// <summary>A command step's actions (PotionCommandsTranslated); a command the card translation doesn't follow either is its named gap.</summary>
    private static List<SimAction> PotionStep(CommandStep command, PotionUse use)
    {
        // A command on what a pick of yours or a roll gave (the cards you choose, a card made at random): not followed.
        if (command.Values.Any(value => value.Parts().Any(part => part is ResultValue || part is NamedValue { Name: "each" })))
            return Unknown(use, $"{command.Command.FullName} on cards a pick or a roll gives");
        switch (command.Command.DeclaringType, command.Command.Name)
        {
            case ("CreatureCmd", "Damage"): return PotionDamage(command, use);
            case ("CreatureCmd", "GainBlock"): return PotionBlock(command, use);
            case ("CreatureCmd", "Heal"): return PotionHeal(command, use);
            case ("PowerCmd", "Apply"): return PotionPower(command, use);
            case ("CardPileCmd", "Draw"): return PotionDraw(command, use);
            case ("PlayerCmd", "GainEnergy"):
            {
                if (use.Number(ValueAt(command, 0)) is not { } energy) return Unknown(use, "energy by an amount the simulator doesn't read");
                return IdOf(use.Value(ValueAt(command, 1)), use) is { } player && Who.IsPlayer(player)
                    ? new List<SimAction> { new CardEventAction(CardEvent.EnergyGained, (int)energy) { Player = player } }
                    : Unknown(use, "energy for a player the simulator doesn't name");
            }
            case ("PlayerCmd", "GainStars"):
                // A card's stars gained are one event (SimAdapter.Translate); a potion's for you the same.
                return IdOf(use.Value(ValueAt(command, 1)), use) == Who.You
                    ? new List<SimAction> { new CardEventAction(CardEvent.StarsGained, Count: 1) }
                    : Unknown(use, "stars for a player other than you");
            case ("OrbCmd", "Channel"):
            {
                if (CardEffects.OrbOf(command) is not { } orb) return Unknown(use, "an orb the simulator can't name");
                return IdOf(use.Value(ValueAt(command, 1)), use) is { } player && Who.IsPlayer(player)
                    ? new List<SimAction> { new OrbAction(OrbAction.Op.Channel) { Orb = orb, Times = 1, To = player } }
                    : Unknown(use, "an orb for a player the simulator doesn't name");
            }
            case ("OrbCmd", "AddSlots"):
                return IdOf(use.Value(ValueAt(command, 0)), use) == Who.You && use.Number(ValueAt(command, 1)) is { } slots
                    ? new List<SimAction> { new OrbAction(OrbAction.Op.Slots) { Slots = (int)slots } }
                    : Unknown(use, "orb slots for another player, or by an amount the simulator doesn't read");
            case ("OstyCmd", "Summon"):
                return IdOf(use.Value(ValueAt(command, 1)), use) is { } summoner && Who.IsPlayer(summoner) && use.Number(ValueAt(command, 2)) is { } amount
                    ? new List<SimAction> { new SummonAction((int)amount) { Player = summoner } }
                    : Unknown(use, "a summon for a player or by an amount the simulator doesn't read");
        }
        // A command whose only effect on the fight is the hook it runs (gold, a potion procured): nothing to follow when no listener answers it.
        if (HookOnlyCommands.TryGetValue(command.Command.FullName, out var hook) && UnfollowedListener(use.Me, use.Combat, hook, any: true) == null) return new List<SimAction>();
        return Untranslated(command.Command.FullName) is { } unknown ? new List<SimAction> { unknown } : new List<SimAction>();
    }

    private static List<SimAction> Unknown(PotionUse use, string why) => new() { new NotModelledAction($"{use.Name}: {why}") };

    /// <summary>
    /// CreatureCmd.Damage: to the enemy it names, or to every hittable enemy (a list given whole), its amount and props the potion's damage value's
    /// (or the amount and props its code passes), dealt by you, through the game's damage math with no card.
    /// </summary>
    private static List<SimAction> PotionDamage(CommandStep command, PotionUse use)
    {
        var whom = ValueAt(command, 1);
        var (amount, props) = DamageOf(command, use);
        if (amount == null || props == null) return Unknown(use, "damage by an amount the simulator doesn't read");
        // The dealer: the potion's owner (its creature), as every potion's code gives it.
        if (DealerOf(command, use) != Who.You) return Unknown(use, "damage dealt by someone other than you");
        var valueProp = (ValueProp)(int)props.Value;
        if (IsEveryEnemy(whom))
        {
            var captured = new Dictionary<int, CapturedTerms>();
            foreach (var (enemy, id) in use.Ids)
                if (enemy.IsAlive) captured[id] = SimReaders.Damage(use.Me.RunState, use.Combat, card: null, use.Me.Creature, enemy, amount.Value, valueProp);
            return new List<SimAction> { new DamageAction(new DamageSpec(amount.Value, SimReaders.Props(valueProp)) { Captured = captured }) { EveryEnemy = true } };
        }
        if (use.Value(whom) is not Creature target || IdOf(target, use) is not { } targetId || !Who.IsEnemy(targetId))
            return Unknown(use, $"damage to a creature the simulator doesn't name ({whom})");
        return new List<SimAction>
        {
            new DamageAction(new DamageSpec(amount.Value, SimReaders.Props(valueProp))
                { Captured = new Dictionary<int, CapturedTerms> { [targetId] = SimReaders.Damage(use.Me.RunState, use.Combat, card: null, use.Me.Creature, target, amount.Value, valueProp) } }) { TargetId = targetId },
        };
    }

    /// <summary>
    /// A damage command's amount and props: a damage value given whole (its BaseValue and Props), or an amount and the props after it.
    /// </summary>
    private static (decimal? Amount, decimal? Props) DamageOf(CommandStep command, PotionUse use)
    {
        if (use.Value(ValueAt(command, 2)) is DynamicVar dynamicVar) return (dynamicVar.BaseValue, (int)PropsOf(dynamicVar));
        var amount = use.Number(ValueAt(command, 2));
        var props = use.Value(ValueAt(command, 3)) switch
        {
            ValueProp valueProp => (decimal?)(int)valueProp,
            decimal number => number,
            _ => null,
        };
        return (amount, props);
    }

    /// <summary>A damage value's props (DamageVar, BlockVar: their Props), Move for any other.</summary>
    private static ValueProp PropsOf(DynamicVar dynamicVar) => dynamicVar switch
    {
        DamageVar damage => damage.Props,
        BlockVar block => block.Props,
        _ => ValueProp.Move,
    };

    /// <summary>The damage's dealer (the creature argument after its amount and props, or after a value given whole), as the simulator names it.</summary>
    private static int? DealerOf(CommandStep command, PotionUse use)
    {
        var at = use.Value(ValueAt(command, 2)) is DynamicVar ? 3 : 4;
        return IdOf(use.Value(ValueAt(command, at)), use);
    }

    /// <summary>
    /// CreatureCmd.GainBlock: to the player it names (you, or the teammate it's thrown at), its amount a block value given whole (its BaseValue and
    /// Props) or a number with the props after it, through the game's block math with no card.
    /// </summary>
    private static List<SimAction> PotionBlock(CommandStep command, PotionUse use)
    {
        if (use.Value(ValueAt(command, 0)) is not Creature gainer || IdOf(gainer, use) is not { } to || !Who.IsPlayer(to))
            return Unknown(use, "block for a creature the simulator doesn't name");
        decimal amount;
        ValueProp props;
        if (use.Value(ValueAt(command, 1)) is DynamicVar dynamicVar) (amount, props) = (dynamicVar.BaseValue, PropsOf(dynamicVar));
        else if (use.Number(ValueAt(command, 1)) is { } number && use.Value(ValueAt(command, 2)) is ValueProp or decimal)
            (amount, props) = (number, use.Value(ValueAt(command, 2)) is ValueProp given ? given : (ValueProp)(int)(decimal)use.Value(ValueAt(command, 2))!);
        else return Unknown(use, "block by an amount the simulator doesn't read");
        return new List<SimAction> { new GainBlockAction(Times: 1, amount, SimReaders.Props(props)) { To = to, Captured = SimReaders.Block(use.Combat, card: null, gainer, amount, props) } };
    }

    /// <summary>CreatureCmd.Heal of you, by the amount its code works out on the board; anyone else's is unknown.</summary>
    private static List<SimAction> PotionHeal(CommandStep command, PotionUse use)
    {
        if (IdOf(use.Value(ValueAt(command, 0)), use) != Who.You) return Unknown(use, "a heal of a creature other than you");
        return use.Number(ValueAt(command, 1)) is { } amount ? new List<SimAction> { new HealAction((int)amount) } : Unknown(use, "a heal by an amount the simulator doesn't read");
    }

    /// <summary>
    /// PowerCmd.Apply&lt;T&gt;: the power on whom its code names (a creature, or every hittable enemy), its amount its third argument, applied by you
    /// (one applied by a teammate is unknown), by the card translation's rules for which powers land (decisions #284, #312): Strength and a
    /// temporary Strength on enemies as Strength, Focus on you as Focus, any other power the simulator reads; one it doesn't read lands nowhere.
    /// </summary>
    private static List<SimAction> PotionPower(CommandStep command, PotionUse use)
    {
        if (command.Command.GenericArguments is not [var powerName]
            || typeof(PowerModel).Assembly.GetTypes().FirstOrDefault(type => type.Name == powerName && typeof(PowerModel).IsAssignableFrom(type)) is not { } type)
            return Unknown(use, "a power the simulator can't name");
        var whom = ValueAt(command, 1);
        var targets = new List<(ApplyPowerAction.Where To, int Creature)>();
        if (IsEveryEnemy(whom)) targets.Add((ApplyPowerAction.Where.AllEnemies, 0));
        else if (IdOf(use.Value(whom), use) is { } id) targets.Add((id == Who.You ? ApplyPowerAction.Where.You : ApplyPowerAction.Where.Creature, id));
        else return Unknown(use, $"{powerName} on a creature the simulator doesn't name ({whom})");
        if (IdOf(use.Value(ValueAt(command, 3)), use) is not { } applier) return Unknown(use, $"{powerName} applied by a creature the simulator doesn't name");
        if (applier != Who.You && !(targets is [(_, var only)] && only == applier && applier == Who.You))
            return Unknown(use, $"{powerName} applied by someone other than you");
        var amount = use.Number(ValueAt(command, 2));
        var actions = new List<SimAction>();
        var simPowerName = SimPowerName(type);
        foreach (var (to, creature) in targets)
        {
            var toEnemies = to == ApplyPowerAction.Where.AllEnemies || Who.IsEnemy(creature) && to == ApplyPowerAction.Where.Creature;
            var simulated = Simulated(simPowerName);
            var debuff = SimReaders.IsDebuffType(type);
            var strength = type == typeof(StrengthPower) || typeof(TemporaryStrengthPower).IsAssignableFrom(type);
            var focusSign = to == ApplyPowerAction.Where.You ? FocusSignOf(type) : 0;
            if (!(toEnemies && strength) && focusSign == 0 && (debuff ? !toEnemies && !simulated : !simulated)) continue;
            if (amount == null)
            {
                actions.Add(new NotModelledAction($"{PowerAmountUnread} ({powerName}: {ValueAt(command, 2)})"));
                continue;
            }
            var given = (int)amount.Value;
            if (toEnemies && strength)
            {
                var sign = typeof(TemporaryStrengthPower).IsAssignableFrom(type) && debuff ? -1 : 1;
                actions.Add(new ApplyPowerAction(PowerId.Strength, sign * given) { To = to, Creature = creature, Debuff = sign * given < 0 });
            }
            else if (focusSign != 0) { if (given != 0) actions.Add(new FocusAction(focusSign * given)); }
            else actions.Add(new ApplyPowerAction(new PowerId(simPowerName), given) { To = to, Creature = creature, Debuff = debuff && toEnemies });
        }
        return actions;
    }

    /// <summary>CardPileCmd.Draw: your draws from your pile, as a card's own; a teammate's, counted on their piles as a card's draws for them are.</summary>
    private static List<SimAction> PotionDraw(CommandStep command, PotionUse use)
    {
        if (use.Number(ValueAt(command, 1)) is not { } count) return Unknown(use, "draws by a count the simulator doesn't read");
        var cards = (int)count;
        switch (IdOf(use.Value(ValueAt(command, 2)), use))
        {
            case Who.You:
                return cards > 0 ? new List<SimAction> { new CardEventAction(CardEvent.Drawn, cards) { Indirect = true, JoinsHand = true } } : new List<SimAction>();
            case { } teammate when Who.TeammateIndex(teammate) is var index and >= 0:
                return TeammateDraws(Teammates(use.Me, use.Combat)[index], teammate, cards).ToList();
            default:
                return Unknown(use, "draws for a player the simulator doesn't name");
        }
    }

    /// <summary>
    /// A listener of the run whose class answers <paramref name="hook"/> with no model the simulator runs for it (a read one or a hand-written one):
    /// its class's name, or null when every one that answers it is followed. <paramref name="any"/>: any listener whose class answers it at all.
    /// </summary>
    private static string? UnfollowedListener(Player me, ICombatState combat, string hook, bool any = false)
    {
        try
        {
            foreach (var model in me.RunState.IterateHookListeners(combat))
            {
                if (model.GetType().GetMethod(hook, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public) is not { } method || method.DeclaringType == typeof(AbstractModel)) continue;
                if (any || SimModels.Get(SimReaders.ModelName(model)) is not { } simModel || !simModel.Overrides(hook)) return model.GetType().Name;
            }
            return null;
        }
        catch
        {
            return "a listener";
        }
    }
}
