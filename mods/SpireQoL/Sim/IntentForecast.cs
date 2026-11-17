namespace SpireQoL.Sim;

/// <summary>How a random branch may repeat a move (the game's MoveRepeatType).</summary>
public enum MoveRepeat { CanRepeatForever, CanRepeatXTimes, CannotRepeat, UseOnlyOnce }

/// <summary>A state of an enemy's move rules (MonsterMoveStateMachine): a move, a random branch or a conditional branch.</summary>
public abstract record MoveNode(string Id);

/// <summary>A move (MoveState): the state that follows it (null: the machine's initial state).</summary>
public sealed record MoveNodeMove(string Id, string? FollowUp) : MoveNode(Id);

/// <summary>One branch of a random pick (RandomBranchState.StateWeight); Weight is its weight now (NaN: it couldn't be read).</summary>
public sealed record RandomBranch(string To, MoveRepeat Repeat, int MaxTimes, int Cooldown, float Weight);

/// <summary>A random pick (RandomBranchState).</summary>
public sealed record MoveNodeRandom(string Id, IReadOnlyList<RandomBranch> Branches) : MoveNode(Id);

/// <summary>A conditional branch (ConditionalBranchState): the first branch whose condition holds, in order (null: it couldn't be read).</summary>
public sealed record MoveNodeConditional(string Id, IReadOnlyList<(string To, bool? Holds)> Branches) : MoveNode(Id);

/// <summary>An enemy's move rules: every state by id, and the initial state (a move with no follow-up goes back to it).</summary>
public sealed record MoveGraph(IReadOnlyDictionary<string, MoveNode> Nodes, string InitialId);

/// <summary>One forecast turn: each move the enemy can make, with its chance (one move at 1 when it's certain).</summary>
public sealed record ForecastTurn(IReadOnlyList<(string Move, double Chance)> Moves)
{
    public bool Certain => Moves.Count == 1;
}

/// <summary>
/// An enemy's next moves after the intent on screen (decision #72), from its move rules the way the game picks them
/// (MonsterMoveStateMachine.FindNextMoveState): a move goes to its follow-up; a random branch picks by weight, a branch
/// weighted 0 by its repeat rule (RandomBranchState.GetStateWeight: used once already, repeated its most times in a row, on
/// cooldown) never picked; a conditional branch takes the first branch whose condition holds now. Turns follow while each is
/// certain; the first random pick ends the forecast with every move it can make and its chance. Conditions and weights are
/// as things stand now. Pure: no game types, no RNG.
/// </summary>
public static class IntentForecast
{
    /// <summary>How many turns past the intent on screen a forecast looks at most (a fixed cycle never rolls).</summary>
    public const int MaxTurns = 3;

    private const int MaxDepth = 16;

    /// <param name="current">The move on screen (it may be outside the graph: a stun's STUNNED move).</param>
    /// <param name="log">The moves the enemy has been given this fight, oldest first, the one on screen last (StateLog).</param>
    /// <returns>The turns after the one on screen, or an empty list when the rules can't say (a branch with nowhere to go).</returns>
    public static List<ForecastTurn> Forecast(MoveGraph graph, MoveNodeMove current, IReadOnlyList<string> log, int maxTurns = MaxTurns)
    {
        var turns = new List<ForecastTurn>();
        var history = log.ToList();
        var move = current;
        for (var t = 0; t < maxTurns; t++)
        {
            var dist = new Dictionary<string, double>();
            if (!Walk(graph, move.FollowUp ?? graph.InitialId, 1.0, history, dist, 0)) break;
            var moves = dist.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key)
                .Select(kv => (kv.Key, kv.Value)).ToList();
            if (moves.Count == 0) break;
            turns.Add(new ForecastTurn(moves));
            if (moves.Count > 1) break; // the first random pick ends it
            if (graph.Nodes.GetValueOrDefault(moves[0].Key) is not MoveNodeMove next) break;
            history.Add(next.Id);
            move = next;
        }
        return turns;
    }

    /// <summary>Spreads <paramref name="chance"/> over the moves reached from a state; false when the rules can't say.</summary>
    private static bool Walk(MoveGraph graph, string id, double chance, List<string> log, Dictionary<string, double> into, int depth)
    {
        if (depth > MaxDepth || !graph.Nodes.TryGetValue(id, out var node)) return false;
        switch (node)
        {
            case MoveNodeMove m:
                into[m.Id] = into.GetValueOrDefault(m.Id) + chance;
                return true;
            case MoveNodeRandom r:
            {
                var weights = r.Branches.Select(b => Weight(b, log)).ToList();
                if (weights.Any(double.IsNaN)) return false; // a weight that couldn't be read
                var total = weights.Sum();
                if (total <= 0) return false; // the game throws: no valid state
                for (var i = 0; i < r.Branches.Count; i++)
                    if (weights[i] > 0 && !Walk(graph, r.Branches[i].To, chance * weights[i] / total, log, into, depth + 1)) return false;
                return true;
            }
            case MoveNodeConditional c:
            {
                foreach (var (to, holds) in c.Branches)
                {
                    if (holds == null) return false; // a condition that couldn't be read, before the one that holds
                    if (holds.Value) return Walk(graph, to, chance, log, into, depth + 1);
                }
                return false; // the game throws: no valid next state
            }
            default:
                return false;
        }
    }

    /// <summary>A branch's weight now (RandomBranchState.GetStateWeight, line for line).</summary>
    internal static double Weight(RandomBranch b, IReadOnlyList<string> log)
    {
        double num = 1;
        if (b.Repeat == MoveRepeat.UseOnlyOnce)
        {
            if (log.Contains(b.To)) num = 0;
        }
        else if (b.Repeat != MoveRepeat.CanRepeatForever)
        {
            var times = b.Repeat == MoveRepeat.CannotRepeat ? 1 : b.MaxTimes;
            num = log.Count < times ? 1 : 0;
            var i = 0;
            while (log.Count >= times && i < times && log.Count - i > 0)
            {
                if (log[log.Count - 1 - i] != b.To)
                {
                    num = 1;
                    break;
                }
                i++;
            }
        }
        if (b.Cooldown > 0 && log.Reverse().Take(b.Cooldown).Contains(b.To)) return 0;
        return num * b.Weight;
    }
}
