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
        IReadOnlyDictionary<int, ProjectedRules> Rules);

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
            return new ProjectionInput(state, ids, turns, rules);
        }
        catch (Exception exception)
        {
            ModEntry.Log.Detail($"[sim] the forecast's projection couldn't be read: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    /// <summary>The projection of <paramref name="input"/> on a worker (SimJob, decision #107): the frame waits for it as for any search (SimWait).</summary>
    internal static SimJob StartProjection(ProjectionInput input, string label) => SimJob.StartProjection(input.State, input.Turns, label, rules: input.Rules);

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
    /// base can't be read.
    /// </summary>
    private static ProjectedMove? ProjectedMoveOf(Player me, ICombatState combat, List<Player> mates, Creature creature, MoveState move, Func<Creature, int?> idOf)
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
            for (var i = 0; i < repeats; i++) intent.Add(new IntentHit(damageCalc) { Captured = terms });
        }
        return new ProjectedMove(move.Id)
        {
            Intent = intent.Count > 0 ? intent : null,
            Actions = MoveOf(monster, hitsPerAttack, idOf, combat, given: move),
            OnlyAttacks = move.Intents.All(each => each is AttackIntent),
        };
    }

    /// <summary>A creature's simulator id, as BuildFromGame gives them: an enemy's (<paramref name="ids"/>), you, teammate k, or a player's pet.</summary>
    private static Func<Creature, int?> IdOfFor(Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids) => CreatureIds(me, Teammates(me, combat), ids);
}
