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
    /// The projection of every living enemy's forecast turns from the board now, with each enemy's simulator id. Without the cards in teammates'
    /// hands unless <paramref name="teammateHands"/> (what's shown: decision #25); the forecast audit uses them all. Null outside a fight's state the
    /// adapter can read.
    /// </summary>
    internal static (Projection Projection, IReadOnlyDictionary<Creature, int> Ids)? Project(Player me, ICombatState combat, bool teammateHands)
    {
        try
        {
            var state = Build(me, combat, out var ids);
            if (!teammateHands && state.Teammates.Count > 0) state = state.WithoutTeammateHands();
            return Project(me, combat, state, ids);
        }
        catch (Exception exception)
        {
            ModEntry.Log.Detail($"[sim] the forecast's projection failed: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// The projection from <paramref name="state"/>, the adapter's state of the board with its enemies' <paramref name="ids"/> (Incoming Damage's own:
    /// TurnEndModel's), every living enemy's forecast moves read from the game now. Null when it can't be read.
    /// </summary>
    internal static (Projection Projection, IReadOnlyDictionary<Creature, int> Ids)? Project(Player me, ICombatState combat, SimState state, IReadOnlyDictionary<Creature, int> ids)
    {
        try
        {
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
            return (SimEngine.Project(state, turns), ids);
        }
        catch (Exception exception)
        {
            ModEntry.Log.Detail($"[sim] the forecast's projection failed: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
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
    private static Func<Creature, int?> IdOfFor(Player me, ICombatState combat, IReadOnlyDictionary<Creature, int> ids)
    {
        var mates = Teammates(me, combat);
        int? PlayerIdOf(Creature creature) => creature == me.Creature ? Who.You
            : mates.FindIndex(player => player.Creature == creature) is var teammateIndex and >= 0 ? Who.Teammate(teammateIndex) : null;
        return creature => ids.TryGetValue(creature, out var enemyId) ? enemyId : PlayerIdOf(creature)
            ?? (creature.PetOwner is { } petOwner && PetKindOf(petOwner, creature) is { } kind && PlayerIdOf(petOwner.Creature) is { } ownerId ? Who.PetOf(ownerId, kind) : null);
    }
}
