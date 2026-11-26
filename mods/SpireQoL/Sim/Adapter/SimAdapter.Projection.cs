using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace SpireQoL.Sim;

/// <summary>
/// The Intent Forecast's later turns from the game (decision #304): every living enemy's forecast moves (IntentForecast: its rules, its moves so far,
/// never a roll), each read as the intent on screen is (its attacks' bases and the captured terms on each player, its code's steps: MoveOf), and
/// the simulator's projection of them from the board (SimEngine.Project).
/// </summary>
public static partial class SimAdapter
{
    /// <summary>
    /// What a projection starts from (decision #304), read from the game on its thread: the adapter's state of the board (Incoming Damage's own:
    /// TurnEndModel's), each enemy's simulator id, every living enemy's forecast turns, each move read now, and each one's move rules with every move
    /// read (decision #309: the projection rolls its moves from the projected state). The projection itself is pure and runs on a worker
    /// (<see cref="StartProjection"/>).
    /// </summary>
    internal sealed record ProjectionInput(SimState State, IReadOnlyDictionary<Creature, int> Ids, IReadOnlyList<IReadOnlyList<ProjectedChoice>> Turns,
        IReadOnlyDictionary<int, ProjectedRules> Rules)
    {
        /// <summary>The move rules of each monster a move or a death on the board can add, by class (decision #378: JoiningRules).</summary>
        public IReadOnlyDictionary<string, ProjectedRules> Joining { get; init; } = new Dictionary<string, ProjectedRules>();
    }

    /// <summary>
    /// A projection's start from <paramref name="state"/> and its enemies' <paramref name="ids"/> (or the board built now, without teammates' hands
    /// unless <paramref name="teammateHands"/>, when none is given). Null when it can't be read.
    /// </summary>
    internal static ProjectionInput? ProjectionStart(Player me, ICombatState combat, SimState? state = null, IReadOnlyDictionary<Creature, int>? ids = null, bool teammateHands = false)
    {
        try
        {
            if (state == null || ids == null)
            {
                state = Build(me, combat, out var built);
                if (!teammateHands && state.Teammates.Count > 0) state = state.WithoutTeammateHands();
                ids = built;
            }
            var idOf = IdOfFor(me, combat, ids);
            var mates = Teammates(me, combat);
            var forecasts = new List<(int Id, Creature Creature, List<ForecastTurn> Turns, MoveStateMachineView View)>();
            foreach (var (creature, id) in ids)
                if (creature.IsAlive && IntentReader.Forecast(creature) is { } forecast) forecasts.Add((id, creature, forecast.Turns, forecast.View));
            var count = forecasts.Count == 0 ? 0 : forecasts.Max(forecast => forecast.Turns.Count);
            var turns = new List<IReadOnlyList<ProjectedChoice>>();
            for (var turn = 0; turn < count; turn++)
            {
                var choices = new List<ProjectedChoice>();
                foreach (var (id, creature, forecastTurns, view) in forecasts)
                {
                    if (turn >= forecastTurns.Count) continue; // past its first random pick: its move that turn isn't known
                    List<(ProjectedMove, double)>? options = new();
                    foreach (var (moveId, chance) in forecastTurns[turn].Moves)
                    {
                        if (view.Move(moveId) is not { } move || ProjectedMoveOf(me, combat, mates, creature, move, idOf) is not { } projected) { options = null; break; }
                        options.Add((projected, chance));
                    }
                    if (options != null) choices.Add(new ProjectedChoice(id, options));
                }
                turns.Add(choices);
            }
            var rules = new Dictionary<int, ProjectedRules>();
            foreach (var (id, creature, _, _) in forecasts)
                if (RulesOf(me, combat, mates, creature, idOf) is { } read) rules[id] = read;
            return new ProjectionInput(state, ids, turns, rules) { Joining = JoiningRules(me, combat, mates, state, rules.Values, turns, idOf) };
        }
        catch (Exception exception)
        {
            ModEntry.Log.Detail($"[sim] the forecast's projection couldn't be read: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    /// <summary>The projection of <paramref name="input"/> on a worker (SimJob, decision #107): the frame waits for it as for any search (SimWait).</summary>
    internal static SimJob StartProjection(ProjectionInput input, string label) => SimJob.StartProjection(input.State, input.Turns, label, rules: input.Rules, joining: input.Joining);

    /// <summary>How many monsters' rules a projection reads for the creatures that can join (each one's own adds included): a bound on the closure.</summary>
    private const int MaxJoiningMonsters = 8;

    /// <summary>
    /// The move rules of each monster that can join the fight in a projection (decision #378), by class: each a death's spawn names (SimEnemy.Spawns)
    /// or a move's add (MoveSpawnAction) in the board's moves, the forecast's and the read rules' moves, and in the moves of those it reads in turn.
    /// Each is read from the monster as it joins (MonsterRules).
    /// </summary>
    private static Dictionary<string, ProjectedRules> JoiningRules(Player me, ICombatState combat, List<Player> mates, SimState state, IEnumerable<ProjectedRules> rules,
        IReadOnlyList<IReadOnlyList<ProjectedChoice>> turns, Func<Creature, int?> idOf)
    {
        var joining = new Dictionary<string, ProjectedRules>(StringComparer.Ordinal);
        var waiting = new Queue<string>();
        void Named(string? monster)
        {
            if (!string.IsNullOrEmpty(monster) && !joining.ContainsKey(monster) && !waiting.Contains(monster)) waiting.Enqueue(monster);
        }
        void InMoves(IEnumerable<SimAction>? actions)
        {
            if (actions == null) return;
            foreach (var action in actions)
                if (action is MoveSpawnAction add) Named(add.Spawn.Monster);
                else if (action is WhenAction when) InMoves(when.Then);
        }
        foreach (var enemy in state.Enemies)
        {
            foreach (var spawn in enemy.Spawns ?? new List<SimSpawn>()) Named(spawn.Monster);
            InMoves(enemy.Move);
        }
        foreach (var read in rules) foreach (var move in read.Moves.Values) InMoves(move.Actions);
        foreach (var turn in turns) foreach (var choice in turn) foreach (var (move, _) in choice.Options) InMoves(move.Actions);
        var tried = 0;
        while (waiting.Count > 0 && tried++ < MaxJoiningMonsters)
        {
            var monster = waiting.Dequeue();
            if (MonsterRules(me, combat, mates, monster, idOf) is not { } read) continue;
            joining[monster] = read;
            foreach (var move in read.Moves.Values) InMoves(move.Actions);
        }
        return joining;
    }

    /// <summary>
    /// A monster's move rules as it joins (decision #378): a copy of its canonical model (ModelDb.Monster&lt;T&gt;().ToMutable(), as CreatureCmd.Add makes
    /// it) on a creature of its own in the fight but not on its board (new Creature: nothing added), its machine made as AddCreature makes it
    /// (MonsterModel.SetUpForCombat), and each move read as one on the board is (ProjectedMoveOf). A condition between moves is the simulator's,
    /// asked of the projected state, or not known (never as the game's lambda answers it on a creature that hasn't joined); a random pick's weights
    /// are its code's on the creature as it joins. Null when it can't be read.
    /// </summary>
    private static ProjectedRules? MonsterRules(Player me, ICombatState combat, List<Player> mates, string monsterClass, Func<Creature, int?> idOf)
    {
        try
        {
            if (MonsterType(monsterClass) is not { } type || CanonicalMonster(type)?.ToMutable() is not { } monster) return null;
            var creature = new Creature(monster, MegaCrit.Sts2.Core.Combat.CombatSide.Enemy, slotName: null) { CombatState = combat };
            monster.SetUpForCombat();
            if (monster.MoveStateMachine is not { } machine || IntentReader.Graph(machine, monster) is not { } graph) return null;
            var nodes = graph.Nodes.ToDictionary(entry => entry.Key, entry => entry.Value is MoveNodeConditional conditional
                ? conditional with { Branches = conditional.Branches.Select(branch => (branch.To, (bool?)null)).ToList() }
                : entry.Value);
            var moves = new Dictionary<string, ProjectedMove>(StringComparer.Ordinal);
            foreach (var move in machine.States.Values.OfType<MoveState>())
            {
                try
                {
                    if (ProjectedMoveOf(me, combat, mates, creature, move, idOf, stepsWhole: true) is { } projected) moves[move.Id] = projected;
                }
                catch (Exception exception) when (exception is InvalidOperationException or NullReferenceException or ArgumentException)
                {
                    // a move whose intent can't be read off the board is left out: its turn is unknown
                }
            }
            return ProjectedRules.Joining(graph with { Nodes = nodes }, moves);
        }
        catch (Exception exception)
        {
            ModEntry.Log.Detail($"[sim] the rules of a {monsterClass} joining couldn't be read: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// An enemy's move rules for a projection (decision #309): its graph with each condition as the simulator's (IntentReader.Rules), the move on
    /// screen and the state after it, its moves so far, and each of its moves read as the intent on screen is (ProjectedMoveOf; one that can't be
    /// read, or is behind a hidden intent, left out). Null where its rules can't be read.
    /// </summary>
    private static ProjectedRules? RulesOf(Player me, ICombatState combat, List<Player> mates, Creature creature, Func<Creature, int?> idOf)
    {
        if (IntentReader.Rules(creature) is not { } rules || creature.Monster is not { MoveStateMachine: { } machine } monster) return null;
        var moves = new Dictionary<string, ProjectedMove>(StringComparer.Ordinal);
        foreach (var move in machine.States.Values.OfType<MoveState>())
            if (ProjectedMoveOf(me, combat, mates, creature, move, idOf) is { } projected) moves[move.Id] = projected;
        var current = monster.NextMove;
        return new ProjectedRules(rules.Graph, rules.Current, current.FollowUpState?.Id ?? current.FollowUpStateId, rules.Log, moves);
    }

    /// <summary>
    /// A forecast move as the simulator's (ProjectedMove): each attack intent's hits (its DamageCalc's base, with the captured terms on each player, as
    /// BuildFromGame reads the intent on screen) and its code's steps (MoveOf). Null for one behind a hidden intent (decision #72), or an attack whose
    /// base can't be read. With <paramref name="stepsWhole"/> (a creature that hasn't joined: decision #378), its steps are read only when every one
    /// is (a step on the creature itself has no id yet): one it can't build leaves the move unread rather than without that step.
    /// </summary>
    private static ProjectedMove? ProjectedMoveOf(Player me, ICombatState combat, List<Player> mates, Creature creature, MoveState move, Func<Creature, int?> idOf,
        bool stepsWhole = false)
    {
        if (creature.Monster is not { } monster || move.Intents.Any(intent => intent is HiddenIntent or UnknownIntent)) return null;
        var intent = new List<IntentHit>();
        var hitsPerAttack = new List<int>();
        foreach (var attack in move.Intents.OfType<AttackIntent>())
        {
            if (attack.DamageCalc == null) return null;
            var damageCalc = attack.DamageCalc();
            var terms = new Dictionary<int, CapturedTerms> { [Who.You] = SimReaders.Damage(me.RunState, combat, card: null, creature, me.Creature, damageCalc, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move) };
            for (var teammateIndex = 0; teammateIndex < mates.Count; teammateIndex++)
                terms[Who.Teammate(teammateIndex)] = SimReaders.Damage(me.RunState, combat, card: null, creature, mates[teammateIndex].Creature, damageCalc, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move);
            var repeats = Math.Max(1, attack.Repeats);
            hitsPerAttack.Add(repeats);
            // Damage that's a property of its own a move of its sets (decision #337: About to Blow's SteamEruptionDamage) reads what that move set.
            var baseFrom = DamageField(monster.GetType(), attack);
            // Damage that adds its own power (decision #344: The Forgotten's Dread, 13 plus its Dexterity) is worked out on that power as it stands then.
            var powerType = DamagePower(monster.GetType(), attack);
            var basePower = powerType != null ? PowerId.Of(SimPowerName(powerType)) : (PowerId?)null;
            var atRead = powerType != null ? creature.Powers.Where(power => power.GetType() == powerType).Sum(power => power.Amount) : 0;
            for (var i = 0; i < repeats; i++) intent.Add(new IntentHit(damageCalc) { Captured = terms, BaseFrom = baseFrom, BasePower = basePower, BasePowerAtRead = atRead });
        }
        var gaps = stepsWhole ? new List<string>() : null;
        var actions = MoveOf(monster, hitsPerAttack, idOf, combat, gaps, given: move);
        return new ProjectedMove(move.Id)
        {
            Intent = intent.Count > 0 ? intent : null,
            Actions = gaps is { Count: > 0 } ? null : actions,
            OnlyAttacks = move.Intents.All(each => each is AttackIntent),
        };
    }

    /// <summary>A creature's simulator id, as BuildFromGame gives them: an enemy's (<paramref name="ids"/>), you, teammate k, or a player's pet.</summary>
    private static Func<Creature, int?> IdOfFor(Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids) => CreatureIds(me, Teammates(me, combat), ids);
}
