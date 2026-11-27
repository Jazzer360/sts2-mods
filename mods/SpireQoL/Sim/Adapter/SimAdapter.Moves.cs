using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using SpireQoL.Procs.Programs;
using Sts2Mods.Core.Game;

namespace SpireQoL.Sim;

/// <summary>
/// A monster's code as the simulator's (decision #195): each command a monster's move or hook calls, read into its program
/// (ModelPrograms, MonsterCode), is translated into the enemy turn's action for it or is a named gap with why. The scan holds every
/// command of every monster's moves and hooks on both branches to one or the other (MonsterProgramTests).
/// </summary>
public static partial class SimAdapter
{
    /// <summary>The commands a monster's move is translated from, each to the enemy turn's action standing for it.</summary>
    internal static readonly IReadOnlyDictionary<string, string> MoveCommandsTranslated = new Dictionary<string, string>
    {
        ["DamageCmd.Attack"] = "MoveAttackAction: the intent's hits of the attack at its place in the move (the game's numbers, through the damage pipeline as each lands)",
        ["PowerCmd.Apply"] = "MovePowerAction: the power (its type argument) on each creature its target argument names, its amount its third argument, read on the monster",
        ["CreatureCmd.GainBlock"] = "MoveBlockAction: block on each creature its first argument names, its amount through the game's block math",
        ["CreatureCmd.Stun"] = "MoveStunAction: the monster stunning itself (Bowlbug Rock's Headbutt when off balance), on its condition asked of its own state: its next turn's move",
        ["CreatureCmd.Add"] = "MoveSpawnAction: the monster its type argument names joins (its HP and its AfterAddedToRoom's powers read as a death's spawn is); one the move picks or makes first, adds in a loop, on a test a play can change, or gives a power once it's joined isn't followed (a reviewed gap: MoveGaps)",
    };

    /// <summary>The commands a monster's moves and hooks call that the enemy turn doesn't follow, and why.</summary>
    internal static readonly IReadOnlyDictionary<string, string> MoveCommandsNotTranslated = new Dictionary<string, string>
    {
        ["PowerCmd.Remove"] = "a power a move takes away (its own: Soar, Steam Eruption, Plating, its revive's): after its attack, nothing later this turn reads it",
        ["CardPileCmd.AddToCombatAndPreview"] = "status cards put into your piles (Dazed, Slimed, Burn...): your next turn's, not this enemy turn's",
        ["CardPileCmd.AddGeneratedCardToCombat"] = "cards put into your piles (Dazed, Beckon, Frantic Escape): your next turn's, not this enemy turn's",
        ["CardPileCmd.RemoveFromCombat"] = "cards a move steals from your piles (Thieving Hopper): your next turn's, not this enemy turn's",
        ["CreatureCmd.Heal"] = "HP a move heals: not followed in the enemy turn (gap)",
        ["CreatureCmd.SetMaxHp"] = "a move's new max HP (Test Subject's respawn): not followed in the enemy turn (gap)",
        ["CreatureCmd.SetMaxAndCurrentHp"] = "HP set as a monster joins or hatches (Decimillipede, Tough Egg): not followed (gap)",
        ["CreatureCmd.Kill"] = "a monster killing itself after its attack (Gas Bomb, Waterfall Giant): its death isn't followed in the enemy turn (gap)",
        ["CreatureCmd.Escape"] = "a monster fleeing (Fat Gremlin, Thieving Hopper): it leaves after its move (gap)",
        ["ThieveryPower.Steal"] = "gold stolen (Gremlin Merc): not a fight's number",
        ["SwipePower.Steal"] = "a card stolen (Thieving Hopper): your next turn's, not this enemy turn's",
        ["ReattachPower.DoReattach"] = "a Decimillipede segment coming back: its next turn's (gap)",
        ["MonsterModel.SetMoveImmediate"] = "a monster's move changed by a hook (Queen's Enrage when her Amalgam dies): not followed (gap)",
    };

    /// <summary>
    /// What a translated move step can't be built from, besides its command (a reviewed kind of gap, MonsterProgramTests): the step keeps the
    /// enemy turn as it was (no action), never a guess.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> MoveStepGaps = new Dictionary<string, string>
    {
        ["on a condition"] = "a step the move runs only on a condition the simulator doesn't ask of it yet (Pheromone Spit's by its Personal Hive, Dampen on a target without it; a stun of itself on its own state is asked: decision #271, and a power or block on a test of its own powers, Plow's Strength while it has Plow); none of them comes before a later hit this turn",
        ["a power made first"] = "PowerCmd.Apply of a power the move makes and sets up first (Sandpit, Dampen, Swipe, Thievery): its own amounts and targets aren't read",
        ["unread value"] = "a target or an amount the evaluator can't work out on the board",
        ["a monster picked"] = "CreatureCmd.Add of a monster the move picks or makes first (Fabricator's random bot: a model given, not a type argument): which one isn't read",
        ["in a loop"] = "CreatureCmd.Add in a loop (Ovicopter's eggs, Living Fog's Gas Bombs): how many join isn't read",
        ["a test a play can change"] = "CreatureCmd.Add on a test a play can change before the move (a free slot: a kill frees one): it isn't decided on the board",
        ["used once joined"] = "CreatureCmd.Add whose creature the move goes on to use (a power applied to it: Fabricator's and Ovicopter's Minion): what it does to it isn't read",
    };

    /// <summary>
    /// The move an enemy is about to make (its NextMove), as the enemy turn's actions in its code's order (decision #195): its attacks as its
    /// intent's hits (one attack step for each attack intent, in order), the powers it applies and the block it gives. Null when it isn't read
    /// whole (a hidden intent: decision #72 keeps it hidden; an attack the intent doesn't show at that place; code the reader couldn't
    /// follow): the enemy is its intent's hits alone, as before. A step that can't be built (a gap: MoveStepGaps) is left out.
    /// </summary>
    /// <param name="hitsPerAttack">The hits of each of its intent's attacks, in order (AttackIntent.Repeats).</param>
    /// <param name="idOf">A creature's simulator id (an enemy's, Who.You, Who.Teammate(k)), or null for one the state doesn't hold.</param>
    /// <param name="given">The move to read in place of its NextMove (one its powers' code sets it to: SetMovesOf).</param>
    internal static IReadOnlyList<SimAction>? MoveOf(MonsterModel monster, IReadOnlyList<int> hitsPerAttack, Func<Creature, int?> idOf, ICombatState combat, List<string>? gaps = null,
        MoveState? given = null)
    {
        try
        {
            var move = given ?? monster.NextMove;
            if (move == null || move.Intents.Any(intent => intent is HiddenIntent or UnknownIntent)) return null;
            if (Sts2State.Private<Delegate?>(move, "_onPerform", fallback: null)?.Method is not { DeclaringType: { } owner } perform) return null;
            var program = ModelPrograms.Of(owner, perform.Name);
            if (program.Unknowns.Any() || AttackPlacementGap(program, hitsPerAttack.Count) != null) return null;
            var players = combat.Players.Select(player => player.Creature).Where(creature => creature.IsAlive).ToList();
            var context = new MoveContext(program, ProgramBoard.Of(monster), idOf, combat, players, gaps ?? new List<string>());
            var actions = new List<SimAction>();
            var attacks = 0;
            var firstHit = 0;
            foreach (var step in program.Steps)
            {
                if (step is CommandStep { Command.FullName: "DamageCmd.Attack" })
                {
                    actions.Add(new MoveAttackAction(firstHit, hitsPerAttack[attacks]));
                    firstHit += hitsPerAttack[attacks++];
                }
                else actions.AddRange(MoveStep(step, context, each: null));
            }
            // A number its move rules ask that the move's code sets in a way the reader doesn't keep (decision #411: Call for Backup's count, set on
            // every rat in a lambda): forgotten as it runs, so what asks it is unknown from there.
            foreach (var written in CountersWritten(monster.GetType(), perform))
                if (!actions.Any(action => action is MoveStoreAction { Counter: true } store && store.Field == written))
                    actions.Add(new MoveStoreAction(written, Amount: 0, Unknown: "set where the reader doesn't follow it", Counter: true));
            return actions;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The numbers of its own its move rules ask (MoveConditions.CounterKeys) whose setter a move's code calls anywhere: its method, its async state
    /// machine and the lambdas made inside it (decision #411: Call for Backup's ForEach setting every rat's CallForBackupCount). Read once per move.
    /// </summary>
    private static IReadOnlyList<string> CountersWritten(Type monster, System.Reflection.MethodInfo perform) => WrittenByMove.GetOrAdd((monster, perform.Name), key =>
    {
        var counters = MoveConditions.CounterKeys(key.Monster);
        if (counters.Count == 0) return Array.Empty<string>();
        const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
        var marker = "<" + key.Move + ">";
        var methods = new List<System.Reflection.MethodBase> { perform };
        foreach (var nested in key.Monster.GetNestedTypes(System.Reflection.BindingFlags.NonPublic))
            foreach (var method in nested.GetMethods(all))
                if (nested.Name.Contains(marker, StringComparison.Ordinal) || method.Name.Contains(marker, StringComparison.Ordinal)) methods.Add(method);
        foreach (var method in key.Monster.GetMethods(all))
            if (method.Name.Contains(marker, StringComparison.Ordinal)) methods.Add(method);
        var setters = counters.ToDictionary(counter => "set_" + counter[(counter.LastIndexOf('.') + 1)..], counter => counter, StringComparer.Ordinal);
        var written = new List<string>();
        foreach (var method in methods)
        {
            try
            {
                foreach (var instruction in RuntimeCode.Read(method))
                    if (instruction.IsCall && instruction.Member is { } member && setters.TryGetValue(member.Name, out var counter) && !written.Contains(counter)) written.Add(counter);
            }
            catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or ArgumentException)
            {
                // code that can't be read sets nothing the reader follows
            }
        }
        return written;
    });

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type Monster, string Move), IReadOnlyList<string>> WrittenByMove = new();

    /// <summary>
    /// The moves a power's code can set <paramref name="creature"/>'s monster to (its read program's SetMoveSteps: Steam Eruption's AboutToBlowState),
    /// each read from the monster's own property holding that MoveState as its NextMove would be (SimEnemy.SetMoves): its intent's hits on you and its
    /// steps (MoveOf). A move behind a hidden intent, or a property that holds none, isn't read (a SetMoveStep finding none is unknown). Null: none.
    /// </summary>
    internal static Dictionary<string, SimMove?>? SetMovesOf(Creature creature, Func<Creature, int?> idOf, ICombatState combat, IReadOnlyList<Creature> targets)
    {
        if (creature.Monster is not { } monster) return null;
        Dictionary<string, SimMove?>? moves = null;
        foreach (var power in creature.Powers)
        {
            if (SimModels.Get(SimPowerName(power.GetType())) is not ReadModel read) continue;
            foreach (var setMove in read.Spec.Steps().OfType<SetMoveStep>())
            {
                moves ??= new Dictionary<string, SimMove?>(StringComparer.Ordinal);
                if (moves.ContainsKey(setMove.Move)) continue;
                moves[setMove.Move] = null; // not read unless read below: setting it is unknown
                try
                {
                    var property = monster.GetType().GetProperty(setMove.Move, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (property?.GetValue(monster) is not MoveState move || move.Intents.Any(intent => intent is HiddenIntent or UnknownIntent)) continue;
                    var hits = new List<int>();
                    var hitsPerAttack = new List<int>();
                    foreach (var attack in move.Intents.OfType<AttackIntent>())
                    {
                        var perHit = attack.GetSingleDamage(targets, creature);
                        hitsPerAttack.Add(Math.Max(1, attack.Repeats));
                        for (var i = 0; i < Math.Max(1, attack.Repeats); i++) hits.Add(perHit);
                    }
                    // A move with an attack must place its attacks against its intent (MoveOf): read whole, or not read at all.
                    var actions = MoveOf(monster, hitsPerAttack, idOf, combat, given: move);
                    if (actions != null || hits.Count == 0) moves[setMove.Move] = new SimMove(hits, actions);
                }
                catch
                {
                    // not read: setting it is unknown
                }
            }
        }
        return moves;
    }

    /// <summary>
    /// The steps of a move that change the fight's creatures (a power, block) but can't be built whatever the board (MoveStepGaps: on a
    /// condition, a power made first), each with its gap: what the scan holds to a reviewed list (MonsterProgramTests). With the <paramref name="monster"/>
    /// class, a power or block on a condition of its own state the simulator asks (MoveConditions.OfStep: Plow's Strength while it has Plow) is built.
    /// </summary>
    internal static IEnumerable<(CommandStep Step, string Gap)> MoveGaps(ModelProgram program, Type? monster = null)
    {
        var self = monster == null ? null : SafeCanonical(monster);
        bool Asked(CommandStep command) => self != null && command.Condition is { } condition && OnItsPowers(condition) && MoveConditions.OfStep(self, condition) != null;
        IEnumerable<(CommandStep, string)> Of(IEnumerable<ProgramStep> steps, bool conditional, bool inLoop)
        {
            foreach (var step in steps)
            {
                if (step is LoopStep loop)
                {
                    foreach (var found in Of(loop.Body, conditional || loop.Condition != null, inLoop: true)) yield return found;
                    continue;
                }
                if (step is CommandStep { Command.FullName: "CreatureCmd.Add" } spawn)
                {
                    var gap = inLoop ? "in a loop" : conditional ? "a test a play can change" : MoveSpawnGap(spawn, program);
                    // A random pick (decision #379) is followed in the projection only: the enemy turn after a play runs on one state.
                    if (gap == null && spawn.Command.GenericArguments.Count == 0) gap = "a monster picked: the projection's only";
                    if (gap != null) yield return (spawn, gap);
                    continue;
                }
                if (step is not CommandStep { Command.FullName: "PowerCmd.Apply" or "CreatureCmd.GainBlock" } command) continue;
                // A power on the creature an add just made, with that add followed (decision #379: Fabricator's Minion on its bot): it lands as it joins.
                if (!inLoop && AddedBy(command, program) is { } made && MoveSpawnGap(made, program) == null && AfterJoinGap(command) == null) continue;
                if (!conditional && Asked(command)) continue;
                if (conditional || command.Condition != null) yield return (command, "on a condition");
                else if (command.Command.FullName == "PowerCmd.Apply" && command.Command.GenericArguments.Count == 0) yield return (command, "a power made first");
            }
        }
        return Of(program.Steps, conditional: false, inLoop: false);
    }

    /// <summary>
    /// Every test of the condition is of a power (HasPower, GetPowerAmount): what a move's own steps can't change before it's asked. A test of a field
    /// isn't taken (TestSubject's RespawnMove adds one to Respawns before its switch on it, which the simulated monster's field wouldn't have).
    /// </summary>
    private static HashSet<string> TestedPowers(ProgramCondition condition) => condition.Literals.SelectMany(literal => literal.Test.Parts()).OfType<QueryValue>()
        .Where(part => part.Name is "HasPower" or "GetPowerAmount" or "GetPower" or "OfType").SelectMany(part => part.GenericArguments).ToHashSet(StringComparer.Ordinal);

    private static bool OnItsPowers(ProgramCondition condition) =>
        condition.Literals.Any() && condition.Literals.All(literal => literal.Test.Parts().Any(part => part is QueryValue { Name: "HasPower" or "GetPowerAmount" or "GetPower" }
            || part is QueryValue { Name: "OfType", Arguments: [QueryValue { Name: "Powers" }] }));

    private static MonsterModel? SafeCanonical(Type monster)
    {
        try { return CanonicalMonster(monster); }
        catch { return null; }
    }

    /// <summary>
    /// Why a move's CreatureCmd.Add (outside a loop and an if) isn't followed (MoveStepGaps), or null when it is: a monster its type argument names,
    /// on a condition that's fixed for the fight (its every test a call on the live fight whose code there returns a constant: IsLiveCombat), whose
    /// creature nothing later in the move uses.
    /// </summary>
    internal static string? MoveSpawnGap(CommandStep add, ModelProgram program)
    {
        if (add.Command.GenericArguments.Count != 1 && (add.Values.Count == 0 || PickOf(add.Values[0]) == null)) return "a monster picked";
        if (add.Condition != null && add.Condition.Literals.Any(literal => !FixedForTheFight(literal.Test))) return "a test a play can change";
        // Its creature used later in the move: only a power applied to it (decision #379: Fabricator's Minion) is followed, as it joins.
        if (program.All().Any(step => step != add && StepParts(step).Any(part => part is ResultValue { Command: "CreatureCmd.Add" })
            && (step is not CommandStep apply || AddedBy(apply, program) is not { } made || ReferenceEquals(made, add) && AfterJoinGap(apply) != null))) return "used once joined";
        return null;
    }

    /// <summary>
    /// The CreatureCmd.Add whose creature a PowerCmd.Apply targets (its second argument an add's result: the last add before it in the program),
    /// or null for an apply on anything else.
    /// </summary>
    private static CommandStep? AddedBy(CommandStep apply, ModelProgram program)
    {
        if (apply.Command.FullName != "PowerCmd.Apply" || apply.Values.Count < 2
            || apply.Values[1].Parts().OfType<ResultValue>().FirstOrDefault(result => result.Command == "CreatureCmd.Add") is not { } result) return null;
        // In the program's order (a method called twice, as Fabricator's SpawnBot, has the same code offsets each time): the last add before it.
        CommandStep? last = null;
        foreach (var step in program.All())
        {
            if (ReferenceEquals(step, apply)) return last;
            if (step is CommandStep { Command.FullName: "CreatureCmd.Add" } add) last = add;
        }
        return null;
    }

    /// <summary>
    /// Why a power on an added creature (AddedBy) can't be followed as it joins, or null: a power its type argument names, a constant amount, the
    /// summoner applying it, its condition the same as its add's (or none).
    /// </summary>
    private static string? AfterJoinGap(CommandStep apply)
    {
        if (apply.Command.GenericArguments is not [var power] || PowerType(power) == null) return "a power made first";
        if (apply.Values.Count < 4 || apply.Values[2] is not (ConstantValue or NamedValue { Name: "Decimal.One" or "Decimal.Zero" or "Decimal.MinusOne" })) return "an amount not read";
        if (apply.Values[3] is not QueryValue { Name: nameof(MonsterModel.Creature), Of: NamedValue { Name: "this" } }) return "another applier";
        if (apply.Condition != null && apply.Condition.Literals.Any(literal => !FixedForTheFight(literal.Test))) return "a test a play can change";
        return null;
    }

    /// <summary>
    /// A creature a move picks at random to add (decision #379), read from CreatureCmd.Add's monster: <c>rng.NextItem(ToList(Where(Set, m =&gt; m !=
    /// this.Field)))</c>.ToMutable() (the Where optional), its set a static field of models on a monster's class: the set's monster classes and the field
    /// it leaves out (null: none). Null for any other monster.
    /// </summary>
    internal static (IReadOnlyList<Type> Options, string? Field)? PickOf(ProgramValue monster)
    {
        // A value stored first (the field's store: _lastSpawned = NextItem(...)) is what was stored.
        static ProgramValue? Stored(ProgramValue? value) => value is StoredValue stored ? Stored(stored.Value) : value;
        if (Stored(monster) is not QueryValue { Name: "ToMutable", Of: var picked } || Stored(picked) is not QueryValue { Name: "NextItem", Arguments: [var nextOf] }) return null;
        var items = Stored(nextOf);
        if (items is QueryValue { Name: "ToList", Arguments: [var listed] }) items = Stored(listed);
        string? field = null;
        if (items is QueryValue { Name: "Where", Arguments: [var whereOf, LambdaValue lambda] })
        {
            var set = Stored(whereOf);
            if (lambda.Body.Terms is not [[{ Test: CompareValue compare } literal]]) return null;
            var excludes = compare.Op == Comparison.NotEqual ? literal.Holds : compare.Op == Comparison.Equal && !literal.Holds;
            var (parameter, other) = compare.Left is ParameterValue ? (compare.Left, compare.Right) : (compare.Right, compare.Left);
            if (!excludes || parameter is not ParameterValue || other is not QueryValue { Of: NamedValue { Name: "this" }, Arguments.Count: 0 } read) return null;
            field = read.Name;
            items = set;
        }
        // The set: a static field on a monster's class (Fabricator.aggroSpawns), as the reader names a static field.
        if (items is not NamedValue { Name: var named } || named.LastIndexOf('.') is var dot && dot <= 0 || MonsterType(named[..dot]) is not { } owner) return null;
        const System.Reflection.BindingFlags statics = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var value = owner.GetField(named[(dot + 1)..], statics)?.GetValue(null) ?? owner.GetProperty(named[(dot + 1)..], statics)?.GetValue(null);
        if (value is not IEnumerable<MonsterModel> models) return null;
        var options = models.Select(model => model.GetType()).ToList();
        return options.Count == 0 ? null : (options, field);
    }

    /// <summary>The values a step is given (a command's arguments, a store's value), each with its parts.</summary>
    private static IEnumerable<ProgramValue> StepParts(ProgramStep step) => step switch
    {
        CommandStep command => command.Values.SelectMany(value => value.Parts()),
        StoreStep store => store.Value.Parts(),
        _ => Array.Empty<ProgramValue>(),
    };

    /// <summary>
    /// A test that's the same for the whole fight: a call with no arguments on the fight (this.CombatState.X()) whose code on the live fight's class
    /// returns a constant (CombatState.IsLiveCombat: true; other combat states, a preview's, answer otherwise).
    /// </summary>
    private static bool FixedForTheFight(ProgramValue test)
    {
        if (test is not QueryValue { IsProperty: false, Arguments.Count: 0, Of: QueryValue { Name: "CombatState", Of: NamedValue { Name: "this" } } } call) return false;
        var method = typeof(CombatState).GetMethod(call.Name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public, Type.EmptyTypes);
        if (method == null) return false;
        try
        {
            return StepValues.Unstored(ProgramReader.ReadReturnedValue(typeof(CombatState), method, RuntimeCode.Read)) is ConstantValue;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Why a move's attacks can't be placed against its intent (one unconditional attack step for each attack intent, none in a loop), or null
    /// when they can: such a move stays its intent's hits alone.
    /// </summary>
    internal static string? AttackPlacementGap(ModelProgram program, int attackIntents)
    {
        var attacks = program.Steps.OfType<CommandStep>().Where(command => command.Command.FullName == "DamageCmd.Attack").ToList();
        if (program.All().OfType<CommandStep>().Count(command => command.Command.FullName == "DamageCmd.Attack") != attacks.Count) return "an attack in a loop";
        if (attacks.Any(attack => attack.Condition != null)) return "an attack on a condition";
        return attacks.Count != attackIntents ? $"{attacks.Count} attacks, {attackIntents} attack intents" : null;
    }

    /// <summary>What a move's steps are worked out against: the monster (its "this"), its program, and the fight's creatures.</summary>
    private sealed record MoveContext(ModelProgram Program, ProgramBoard Board, Func<Creature, int?> IdOf, ICombatState Combat,
        IReadOnlyList<Creature> Players, List<string> Gaps);

    /// <summary>One step's actions (a loop's body once for each creature it goes over); a step that can't be built adds its gap and none.</summary>
    private static IEnumerable<SimAction> MoveStep(ProgramStep step, MoveContext context, Creature? each)
    {
        // A store to a property of its own monster that one of its attack intents' damage is (decision #337): kept for the projection's later turns.
        if (step is StoreStep store && context.Board.Self is MonsterModel owner && DamageFieldKey(owner.GetType(), store.Field) is { } key
            && DamageFields(owner).Contains(key))
            return new SimAction[] { MoveStoreOf(store, key, context) };
        // A store to a number of its own its move rules ask (decision #411: Two-Tailed Rat's TurnsUntilSummonable--), kept the same way.
        if (step is StoreStep counted && each == null && context.Board.Self is MonsterModel counting && DamageFieldKey(counting.GetType(), counted.Field) is { } counter
            && MoveConditions.CounterKeys(counting.GetType()).Contains(counter))
            return new SimAction[] { CounterStoreOf(counted, counter, context) };
        // CreatureCmd.Stun of its own creature (Bowlbug Rock's Headbutt when off balance), on its condition as the simulator asks it of the monster's own
        // state (MoveConditions.OfStep: decision #271); a condition it can't ask is a gap.
        if (step is CommandStep { Command.FullName: "CreatureCmd.Stun" } stun && stun.Values.Count > 0 && each == null
            && context.Board.Self is MonsterModel monster && Value(stun.Values[0], context, each) is Creature stunned && ReferenceEquals(stunned, monster.Creature))
        {
            if (step.Condition == null) return new SimAction[] { new MoveStunAction() };
            if (MoveConditions.OfStep(monster, step.Condition) is { } when) return new SimAction[] { new WhenAction(when, new SimAction[] { new MoveStunAction() }) };
            context.Gaps.Add("on a condition");
            return Array.Empty<SimAction>();
        }
        // CreatureCmd.Add<T> outside a loop (decision #376): the monster joins, read as a death's spawn is; one it can't read (MoveSpawnGap) isn't followed.
        if (step is CommandStep { Command.FullName: "CreatureCmd.Add" } add && each == null) return MoveSpawn(add, context);
        // A power on the creature an add just made (decision #379): the add's own action applies it as it joins (MoveSpawnAction.AfterJoin).
        if (step is CommandStep { Command.FullName: "PowerCmd.Apply" } onAdded && each == null && AddedBy(onAdded, context.Program) is { } made
            && MoveSpawnGap(made, context.Program) == null && AfterJoinGap(onAdded) == null) return Array.Empty<SimAction>();
        if (step.Condition != null)
        {
            // A power or block on a condition of the monster's own state (Plow's Strength while it still has Plow: this.Creature.HasPower<PlowPower>()),
            // asked as its stun's is (MoveConditions.OfStep): the projection's later turns carry what it gives (2.0.0's audit: the next Plow hit 2 more).
            if (step is CommandStep { Command.FullName: "PowerCmd.Apply" or "CreatureCmd.GainBlock" } && each == null && context.Board.Self is MonsterModel self
                && OnItsPowers(step.Condition) && MoveConditions.OfStep(self, step.Condition) is { } asked)
            {
                var gapsBefore = context.Gaps.Count;
                var then = MoveStep(step with { Condition = null }, context, each).ToList();
                // The code tested it before an earlier step of the move applied a power it tests (Pheromone Spit's hive, then its Strength by the hive it
                // had: decision #400): asked as the move starts.
                var tested = TestedPowers(step.Condition);
                var changedFirst = context.Program.All().OfType<CommandStep>().Any(earlier => earlier.Offset < step.Offset
                    && earlier.Command is { FullName: "PowerCmd.Apply", GenericArguments: [var applied] } && tested.Contains(applied));
                if (context.Gaps.Count == gapsBefore) return then.Count == 0 ? Array.Empty<SimAction>() : new SimAction[] { new WhenAction(asked, then) { AskedAtMoveStart = changedFirst } };
                return Array.Empty<SimAction>();
            }
            if (step is CommandStep { Command.FullName: "PowerCmd.Apply" or "CreatureCmd.GainBlock" } or LoopStep) context.Gaps.Add("on a condition");
            return Array.Empty<SimAction>();
        }
        if (step is LoopStep loop)
        {
            if (loop.Items is not { } items || Value(items, context, each) is not IEnumerable<object?> over)
            {
                if (loop.Body.Any(inner => inner is CommandStep { Command.FullName: "PowerCmd.Apply" or "CreatureCmd.GainBlock" })) context.Gaps.Add("unread value");
                return Array.Empty<SimAction>();
            }
            var actions = new List<SimAction>();
            foreach (var item in over.ToList())
                foreach (var inner in loop.Body) actions.AddRange(MoveStep(inner, context, item as Creature));
            return actions;
        }
        if (step is not CommandStep command) return Array.Empty<SimAction>();
        return command.Command.FullName switch
        {
            "PowerCmd.Apply" => MovePower(command, context, each),
            "CreatureCmd.GainBlock" => MoveBlock(command, context, each),
            _ => Array.Empty<SimAction>(),
        };
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IReadOnlySet<string>> DamageFieldsByMonster = new();

    /// <summary>
    /// The properties of its own that a monster's attack intents' damage is (each its DamageCalc returning one: Waterfall Giant's SteamEruptionDamage
    /// and CurrentPressureGunDamage), by their keys in an enemy's fields (DamageField); read once per monster class.
    /// </summary>
    private static IReadOnlySet<string> DamageFields(MonsterModel monster) => DamageFieldsByMonster.GetOrAdd(monster.GetType(), _ =>
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var move in monster.MoveStateMachine?.States.Values.OfType<MoveState>() ?? Enumerable.Empty<MoveState>())
            foreach (var attack in move.Intents.OfType<AttackIntent>())
                if (DamageField(monster.GetType(), attack) is { } key) keys.Add(key);
        return keys;
    });

    /// <summary>
    /// The key in an enemy's fields of the monster's own property an attack intent's damage is (its DamageCalc a lambda on the monster returning
    /// the property, with a setter: its own state, which its moves can set), or null for any other damage (decision #337).
    /// </summary>
    internal static string? DamageField(Type monster, AttackIntent attack)
    {
        if (attack.DamageCalc?.Method is not { } calc || calc.IsStatic || calc.DeclaringType is not { } declaring || !declaring.IsAssignableFrom(monster)) return null;
        var ops = RuntimeCode.Read(calc).Where(instruction => instruction.OpCode != System.Reflection.Emit.OpCodes.Nop).ToList();
        if (ops.Count is < 3 or > 4 || ops[0].OpCode != System.Reflection.Emit.OpCodes.Ldarg_0 || ops[^1].OpCode != System.Reflection.Emit.OpCodes.Ret) return null;
        if (ops[1] is not { IsCall: true, Member: { Kind: CodeMemberKind.Method, Name: var getter } } || !getter.StartsWith("get_", StringComparison.Ordinal)) return null;
        if (ops.Count == 4 && ops[2].Member?.Name != "op_Implicit") return null;
        return DamageFieldKey(monster, getter[4..]);
    }

    /// <summary>
    /// The power of its own an attack intent's damage adds (decision #344): its DamageCalc a lambda on the monster returning a getter of its own whose
    /// code is a sum with one term its own creature's amount of a power (The Forgotten's DreadDamage: 13 + base.Creature.GetPowerAmount&lt;DexterityPower&gt;()),
    /// the other terms reading no power. The projection works the hit's base out on that power as it stands then (IntentHit.BasePower). Null for any
    /// other damage; read once per monster class and getter.
    /// </summary>
    internal static Type? DamagePower(Type monster, AttackIntent attack)
    {
        if (attack.DamageCalc?.Method is not { } calc || calc.IsStatic || calc.DeclaringType is not { } declaring || !declaring.IsAssignableFrom(monster)) return null;
        var ops = RuntimeCode.Read(calc).Where(instruction => instruction.OpCode != System.Reflection.Emit.OpCodes.Nop).ToList();
        if (ops.Count is < 3 or > 4 || ops[0].OpCode != System.Reflection.Emit.OpCodes.Ldarg_0 || ops[^1].OpCode != System.Reflection.Emit.OpCodes.Ret) return null;
        if (ops[1] is not { IsCall: true, Member: { Kind: CodeMemberKind.Method, Name: var getter } } || !getter.StartsWith("get_", StringComparison.Ordinal)) return null;
        if (ops.Count == 4 && ops[2].Member?.Name != "op_Implicit") return null;
        return DamagePowers.GetOrAdd((monster, getter), key =>
        {
            var property = key.Monster.GetProperty(key.Getter[4..], System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (property?.GetMethod is not { } method || property.DeclaringType is not { } owner) return null;
            try
            {
                if (ProgramReader.ReadReturnedValue(owner, method, RuntimeCode.Read) is not { } returned) return null;
                var terms = new List<ProgramValue>();
                void Terms(ProgramValue value)
                {
                    if (StepValues.Unstored(value) is ArithmeticValue { Op: "add" } sum) { Terms(sum.Left); Terms(sum.Right); }
                    else terms.Add(StepValues.Unstored(value)!);
                }
                Terms(returned);
                var own = terms.Where(term => term is QueryValue { Name: "GetPowerAmount", GenericArguments: [_], Of: QueryValue { Name: "Creature", Of: NamedValue { Name: "this" } } }).ToList();
                if (own is not [QueryValue { GenericArguments: [var powerName] }] || terms.Except(own).Any(ReadsAPower)) return null;
                return PowerType(powerName);
            }
            catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException) { return null; }
        });
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type Monster, string Getter), Type?> DamagePowers = new();

    /// <summary>
    /// A monster's own property with a setter under its key in an enemy's fields ("Monster.WaterfallGiant.SteamEruptionDamage": the class declaring
    /// it, as a power's store to its owner's monster names it), or null for one it doesn't have.
    /// </summary>
    private static string? DamageFieldKey(Type monster, string property)
    {
        var found = monster.GetProperty(property, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        return found is { CanWrite: true, DeclaringType: { } declaring } ? ProgramReader.OwnersMonsterPrefix + declaring.Name + "." + property : null;
    }

    /// <summary>
    /// A move's store to a property of its own its damage reads, as the projection runs it (MoveStoreAction): the amount of its own power
    /// (this.Creature.GetPowerAmount&lt;T&gt;(): About to Blow's), more than it was (this.X add n: Pressure Gun's; sub n: Two-Tailed Rat's countdown), or a number worked out now that
    /// reads no power; anything else, or one on a condition, is unknown.
    /// </summary>
    private static MoveStoreAction MoveStoreOf(StoreStep store, string key, MoveContext context)
    {
        if (store.Condition != null) return new MoveStoreAction(key, Amount: 0, Unknown: "on a condition");
        var value = StepValues.Unstored(store.Value)!;
        if (value is QueryValue { Name: "GetPowerAmount", GenericArguments: [var powerName], Of: QueryValue { Name: "Creature", Of: NamedValue { Name: "this" } } }
            && PowerType(powerName) is { } powerType)
            return new MoveStoreAction(key, Amount: 0, OwnPower: PowerId.Of(SimPowerName(powerType)));
        if (value is ArithmeticValue { Op: "add" or "sub" } arithmetic && arithmetic.Left is QueryValue { Name: var name, Of: NamedValue { Name: "this" } } itself && name == store.Field
            && !ReadsAPower(arithmetic.Right) && Number(Value(arithmetic.Right, context, each: null)) is { } more && Number(Value(itself, context, each: null)) is { } now)
            return new MoveStoreAction(key, (int)(arithmetic.Op == "sub" ? -more : more), Adds: true, Now: (int)now);
        if (!ReadsAPower(value) && !value.Parts().Any(part => part is QueryValue { Name: var read, Of: NamedValue { Name: "this" } } && read == store.Field)
            && Number(Value(value, context, each: null)) is { } amount)
            return new MoveStoreAction(key, (int)amount);
        return new MoveStoreAction(key, Amount: 0, Unknown: value.ToString());
    }

    /// <summary>
    /// A move's store to a number of its own its move rules ask (decision #411): more or less than it was (this.TurnsUntilSummonable--: sub 1), or a number
    /// worked out now that reads no power; anything else, or one on a condition, forgets it.
    /// </summary>
    private static MoveStoreAction CounterStoreOf(StoreStep store, string key, MoveContext context)
    {
        if (store.Condition != null) return new MoveStoreAction(key, Amount: 0, Unknown: "on a condition", Counter: true);
        var value = StepValues.Unstored(store.Value)!;
        // Its own value read first, kept in a local for the decrement (x = this.T; this.T = x - 1).
        if (value is ArithmeticValue { Op: "add" or "sub" } arithmetic && (arithmetic.Left is StoredValue kept ? kept.Value : arithmetic.Left) is QueryValue { Name: var name, Of: NamedValue { Name: "this" } }
            && name == store.Field && !ReadsAPower(arithmetic.Right) && Number(Value(arithmetic.Right, context, each: null)) is { } more)
            return new MoveStoreAction(key, (int)(arithmetic.Op == "sub" ? -more : more), Adds: true, Counter: true);
        if (!ReadsAPower(value) && !value.Parts().Any(part => part is QueryValue { Name: var read, Of: NamedValue { Name: "this" } } && read == store.Field)
            && Number(Value(value, context, each: null)) is { } amount)
            return new MoveStoreAction(key, (int)amount, Counter: true);
        return new MoveStoreAction(key, Amount: 0, Unknown: value.ToString(), Counter: true);
    }

    /// <summary>A value that reads a creature's powers, which a projection changes: worked out now, it wouldn't be the value as the move runs.</summary>
    private static bool ReadsAPower(ProgramValue value) =>
        value.Parts().Any(part => part is QueryValue { Name: "GetPowerAmount" or "GetPower" or "HasPower" or "Powers" or "Amount" });

    /// <summary>PowerCmd.Apply&lt;T&gt;(choiceContext, target or targets, amount, applier, card): the power on each creature named.</summary>
    private static IEnumerable<SimAction> MovePower(CommandStep command, MoveContext context, Creature? each)
    {
        if (command.Command.GenericArguments is not [var powerName] || command.Values.Count < 3)
        {
            context.Gaps.Add("a power made first");
            return Array.Empty<SimAction>();
        }
        var targets = Creatures(Value(command.Values[1], context, each));
        var powerType = PowerType(powerName);
        if (targets == null || Number(Value(command.Values[2], context, each)) is not { } amount || powerType == null)
        {
            context.Gaps.Add("unread value");
            return Array.Empty<SimAction>();
        }
        // PowerCmd.Apply's scaling of a new one on an enemy for the players is the engine's (SimEngine.ApplyPower, from the power's code: decision #234).
        var ids = targets.Select(context.IdOf).OfType<int>().ToList();
        return ids.Count == 0 ? Array.Empty<SimAction>()
            : new[] { new MovePowerAction(PowerId.Of(SimPowerName(powerType)), (int)amount, ids, SimReaders.IsDebuffType(powerType)) };
    }

    /// <summary>
    /// CreatureCmd.Add&lt;T&gt;(combatState, slot): the monster T joins (MoveSpawnAction), with every HP it can start with that no enemy in the fight has
    /// and the powers its AfterAddedToRoom gives it, as a death's spawn (Spawn); its condition, fixed for the fight (MoveSpawnGap), holding on the
    /// board. One it can't read adds its gap and nothing, as before it was read.
    /// </summary>
    private static IEnumerable<SimAction> MoveSpawn(CommandStep add, MoveContext context)
    {
        var gap = MoveSpawnGap(add, context.Program);
        if (gap == null && add.Condition != null)
        {
            var holds = ProgramEvaluator.Holds(add.Condition, context.Board, context.Program);
            if (holds == false) return Array.Empty<SimAction>(); // fixed for the fight, and false: it adds none
            if (holds == null) gap = "unread value";
        }
        var afterJoin = gap == null ? AfterJoin(add, context) : PowerSet.Empty;
        if (gap == null && add.Command.GenericArguments.Count == 0 && PickOf(add.Values[0]) is { } pick)
        {
            // A random pick (decision #379): every option read, or none; the field it leaves out read on the board until a pick sets it.
            var options = pick.Options.Select(option => JoiningSpawn(option, context)).ToList();
            if (options.Any(option => option == null))
            {
                context.Gaps.Add("unread value");
                return Array.Empty<SimAction>();
            }
            var lastNow = pick.Field == null ? null
                : context.Board.Self?.GetType().GetField(pick.Field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(context.Board.Self) as MonsterModel;
            return new SimAction[] { new MovePickSpawnAction(options!, pick.Field, lastNow?.GetType().Name) { AfterJoin = afterJoin } };
        }
        var type = gap == null ? MonsterType(add.Command.GenericArguments[0]) : null;
        var spawn = type != null ? JoiningSpawn(type, context) : null;
        if (gap != null || spawn == null)
        {
            context.Gaps.Add(gap ?? "unread value");
            return Array.Empty<SimAction>();
        }
        return new SimAction[] { new MoveSpawnAction(spawn) { AfterJoin = afterJoin } };
    }

    /// <summary>The monster of <paramref name="type"/> joining in the enemy turn (Spawn: its HP none an enemy has, its starting powers), or null.</summary>
    private static SimSpawn? JoiningSpawn(Type type, MoveContext context)
    {
        if (CanonicalMonster(type) is not { } monster) return null;
        var taken = context.Combat.Enemies.Select(enemy => enemy.MaxHp).ToHashSet();
        return Spawn(type, monster, context.Combat, taken) with { Monster = type.Name, UniqueFromEarlier = true, JoinUnread = JoinUnread(context.Combat) };
    }

    /// <summary>The powers the move applies to the creature <paramref name="add"/> made (AddedBy, decision #379), each its amount read on the board.</summary>
    private static PowerSet AfterJoin(CommandStep add, MoveContext context)
    {
        var powers = PowerSet.Empty;
        foreach (var apply in context.Program.All().OfType<CommandStep>())
        {
            if (!ReferenceEquals(AddedBy(apply, context.Program), add) || AfterJoinGap(apply) != null || PowerType(apply.Command.GenericArguments[0]) is not { } power
                || Number(ProgramEvaluator.Value(apply.Values[2], context.Board, context.Program)) is not { } amount) continue;
            powers = powers.Add(PowerId.Of(SimPowerName(power)), delta: (int)amount);
        }
        return powers;
    }

    /// <summary>CreatureCmd.GainBlock(creature, amount, props, card): block on each enemy named, through the game's block math now.</summary>
    private static IEnumerable<SimAction> MoveBlock(CommandStep command, MoveContext context, Creature? each)
    {
        if (command.Values.Count < 3 || Creatures(Value(command.Values[0], context, each)) is not { } gainers || Number(Value(command.Values[1], context, each)) is not { } amount
            || Number(Value(command.Values[2], context, each)) is not { } props)
        {
            context.Gaps.Add("unread value");
            return Array.Empty<SimAction>();
        }
        var actions = new List<SimAction>();
        foreach (var gainer in gainers)
        {
            if (gainer.IsPlayer || context.IdOf(gainer) is not { } id) continue;
            var block = (int)Hook.ModifyBlock(context.Combat, gainer, amount, (MegaCrit.Sts2.Core.ValueProps.ValueProp)(int)props, null, null, out _);
            if (block > 0) actions.Add(new MoveBlockAction(block, new[] { id }));
        }
        return actions;
    }

    /// <summary>A value of the move worked out on the board: "this" the monster, its targets the living players, a loop's item "each".</summary>
    private static object? Value(ProgramValue value, MoveContext context, Creature? each) =>
        ProgramEvaluator.Value(value, context.Board, context.Program, new Dictionary<string, object?> { ["targets"] = context.Players, ["each"] = each });

    /// <summary>A number the evaluator gave (a property's int, a constant's decimal), or null.</summary>
    private static decimal? Number(object? value) => value switch
    {
        decimal number => number,
        int number => number,
        long number => number,
        bool flag => flag ? 1m : 0m,
        _ => null,
    };

    /// <summary>A creature or creatures (a list the move names), or null when it's neither.</summary>
    private static List<Creature>? Creatures(object? value) => value switch
    {
        Creature creature => new List<Creature> { creature },
        IEnumerable<Creature> creatures => creatures.ToList(),
        System.Collections.IEnumerable items => items.Cast<object?>().All(item => item is Creature) ? items.Cast<Creature>().ToList() : null,
        _ => null,
    };

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Type?> PowerTypes = new();

    /// <summary>The game's power class by its name (a step's type argument).</summary>
    private static Type? PowerType(string name) => PowerTypes.GetOrAdd(name, simple =>
        typeof(PowerModel).Assembly.GetTypes().FirstOrDefault(type => type.Name == simple && typeof(PowerModel).IsAssignableFrom(type)));

    private static PowerModel? Canonical(Type powerType) =>
        typeof(ModelDb).GetMethod(nameof(ModelDb.Power))!.MakeGenericMethod(powerType).Invoke(null, null) as PowerModel;

    /// <summary>The move whose code a MoveState performs (its _onPerform), for the forecast and the scan's checks.</summary>
    internal static System.Reflection.MethodInfo? PerformOf(MoveState move) =>
        Sts2State.Private<Delegate?>(move, "_onPerform", fallback: null)?.Method;
}
