namespace SpireQoL.Sim;

/// <summary>How a random branch may repeat a move (the game's MoveRepeatType).</summary>
public enum MoveRepeat { CanRepeatForever, CanRepeatXTimes, CannotRepeat, UseOnlyOnce }

/// <summary>A state of an enemy's move rules (MonsterMoveStateMachine): a move, a random branch or a conditional branch.</summary>
public abstract record MoveNode(string Id);

/// <summary>A move (MoveState): the state that follows it (null: the machine's initial state).</summary>
public sealed record MoveNodeMove(string Id, string? FollowUp) : MoveNode(Id);

/// <summary>One branch of a random pick (RandomBranchState.StateWeight); Weight is its weight now (NaN: it couldn't be read).</summary>
public sealed record RandomBranch(string To, MoveRepeat Repeat, int MaxTimes, int Cooldown, float Weight)
{
    /// <summary>
    /// Its weight lambda's code as the simulator's (decision #411: MoveConditions.WeightWays), each way's condition and the weight it returns, for asking a
    /// simulated state (Two-Tailed Rat's 1/12 once it can summon, which its own moves count towards); null where it isn't read: Weight, as read now.
    /// </summary>
    public IReadOnlyList<(SimCondition When, double Weight)>? Ways { get; init; }
}

/// <summary>A random pick (RandomBranchState).</summary>
public sealed record MoveNodeRandom(string Id, IReadOnlyList<RandomBranch> Branches) : MoveNode(Id);

/// <summary>A conditional branch (ConditionalBranchState): the first branch whose condition holds, in order (null: it couldn't be read).</summary>
public sealed record MoveNodeConditional(string Id, IReadOnlyList<(string To, bool? Holds)> Branches) : MoveNode(Id)
{
    /// <summary>
    /// Each branch's condition as the simulator's (decision #195: read from its lambda's code, asked of the monster on a simulated state), in
    /// the order of Branches; null where one couldn't be translated. Null: none read.
    /// </summary>
    public IReadOnlyList<SimCondition?>? Conditions { get; init; }
}

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
        for (var turn = 0; turn < maxTurns; turn++)
        {
            var dist = new Dictionary<string, double>();
            if (!Walk(graph, move.FollowUp ?? graph.InitialId, chance: 1.0, history, dist, depth: 0, AsReadNow, WeightNow)) break;
            var moves = dist.Where(entry => entry.Value > 0).OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key)
                .Select(entry => (entry.Key, entry.Value)).ToList();
            if (moves.Count == 0) break;
            turns.Add(new ForecastTurn(moves));
            if (moves.Count > 1) break; // the first random pick ends it
            if (graph.Nodes.GetValueOrDefault(moves[0].Key) is not MoveNodeMove next) break;
            history.Add(next.Id);
            move = next;
        }
        return turns;
    }

    /// <summary>A conditional branch's condition as the game's lambda answered it when the rules were read.</summary>
    private static bool? AsReadNow(MoveNodeConditional node, int branch) => node.Branches[branch].Holds;

    /// <summary>A random branch's weight as the game's lambda gave it when the rules were read.</summary>
    private static double WeightNow(RandomBranch branch) => branch.Weight;

    /// <summary>
    /// A random branch's weight asked of a simulated state as the enemy <paramref name="enemyId"/> (decision #411): the weight of the first of its ways
    /// whose condition holds, NaN (not known) where one before it can't be told; as read now where its code isn't read.
    /// </summary>
    private static Func<RandomBranch, double> WeightOf(SimState state, int enemyId) => branch =>
    {
        if (branch.Ways is not { } ways) return branch.Weight;
        foreach (var (when, weight) in ways)
            switch (SimEngine.Holds(state, when, enemyId))
            {
                case true: return weight;
                case null: return double.NaN;
            }
        return double.NaN; // its code returns on one of them: none holding is a condition the simulator reads otherwise
    };

    /// <summary>A random branch's weight asked of a simulated state as the enemy <paramref name="enemyId"/> (WeightOf): NaN where it can't be told.</summary>
    public static double WeightAsked(RandomBranch branch, SimState state, int enemyId) => WeightOf(state, enemyId)(branch);

    /// <summary>Some random branch's weight in the rules is read from its code (decision #411): asked of a state, never as read now.</summary>
    public static bool AsksTheState(MoveGraph graph) => graph.Nodes.Values.Any(node => node is MoveNodeRandom random && random.Branches.Any(branch => branch.Ways != null));

    /// <summary>Some random branch's weight in the rules asks its teammates' next moves (decision #411: SimCounted.SideNextMove).</summary>
    public static bool AsksTeammatesMoves(MoveGraph graph) => graph.Nodes.Values.Any(node => node is MoveNodeRandom random
        && random.Branches.Any(branch => branch.Ways?.Any(way => Asks(way.When, SimCounted.SideNextMove)) == true));

    private static bool Asks(SimCondition condition, SimCounted counted) => condition switch
    {
        CountCondition count => count.Counted == counted,
        NotCondition not => Asks(not.Inner, counted),
        AllCondition all => all.Parts.Any(part => Asks(part, counted)),
        AnyCondition any => any.Parts.Any(part => Asks(part, counted)),
        _ => false,
    };

    /// <summary>
    /// The moves an enemy can make after the move <paramref name="moveId"/>, each with its chance, its conditional branches asked of the
    /// simulated <paramref name="state"/> as the enemy <paramref name="enemyId"/> (decision #195: SimCondition, the simulator's own HP, powers
    /// and side), as the game picks when that move ends (MonsterMoveStateMachine.FindNextMoveState). Null when the rules can't say (a branch
    /// with no condition read). <paramref name="log"/>: the moves it was given this fight, that one last.
    /// </summary>
    public static IReadOnlyList<(string Move, double Chance)>? NextOn(MoveGraph graph, string moveId, IReadOnlyList<string> log, SimState state, int enemyId) =>
        graph.Nodes.GetValueOrDefault(moveId) is not MoveNodeMove move ? null : Next(graph, move.FollowUp ?? graph.InitialId, log, AskedOf(state, enemyId, readNowWhereNot: false), WeightOf(state, enemyId));

    /// <summary>
    /// The moves an enemy can make from the state <paramref name="stateId"/> (a stun's follow-up: CreatureCmd.Stun's nextMoveId, a move or a branch),
    /// each with its chance, its conditional branches asked of the simulated <paramref name="state"/> as NextOn does. Null when the rules can't say.
    /// </summary>
    public static IReadOnlyList<(string Move, double Chance)>? NextFrom(MoveGraph graph, string stateId, IReadOnlyList<string> log, SimState state, int enemyId) =>
        Next(graph, stateId, log, AskedOf(state, enemyId, readNowWhereNot: false), WeightOf(state, enemyId));

    /// <summary>
    /// The moves an enemy can make from the state <paramref name="stateId"/> in a projected turn (decision #309), each with its chance: a conditional
    /// branch asked of the projected <paramref name="state"/> where its condition is read (a power the projection follows: Asleep's or Slumber's
    /// stack gone; HP, which a projection doesn't change, reads as it stands now), and as the game's lambda answers it now where it isn't, or
    /// with no state (past what the projection follows). Null when the rules can't say.
    /// </summary>
    public static IReadOnlyList<(string Move, double Chance)>? NextInProjection(MoveGraph graph, string stateId, IReadOnlyList<string> log, SimState? state, int enemyId) =>
        Next(graph, stateId, log, state == null ? AsReadNow : AskedOf(state, enemyId, readNowWhereNot: true), state == null ? WeightNow : WeightOf(state, enemyId));

    /// <summary>The moves reached from <paramref name="stateId"/>, each with its chance, its conditions asked by <paramref name="holdsOf"/>; null when the rules can't say.</summary>
    private static IReadOnlyList<(string Move, double Chance)>? Next(MoveGraph graph, string stateId, IReadOnlyList<string> log, Func<MoveNodeConditional, int, bool?> holdsOf,
        Func<RandomBranch, double> weightOf)
    {
        var dist = new Dictionary<string, double>();
        if (!Walk(graph, stateId, chance: 1.0, log.ToList(), dist, depth: 0, holdsOf, weightOf)) return null;
        return dist.Where(entry => entry.Value > 0).OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key).Select(entry => (entry.Key, entry.Value)).ToList();
    }

    /// <summary>
    /// A conditional branch's condition asked of a simulated state as the enemy <paramref name="enemyId"/> (decision #195: SimCondition); where it
    /// isn't read, or the simulator can't tell, as the game's lambda answered it when the rules were read (<paramref name="readNowWhereNot"/>), else
    /// not known.
    /// </summary>
    private static Func<MoveNodeConditional, int, bool?> AskedOf(SimState state, int enemyId, bool readNowWhereNot) => (node, branch) =>
        node.Conditions is { } conditions && branch < conditions.Count && conditions[branch] is { } condition && SimEngine.Holds(state, condition, enemyId) is { } holds ? holds
            : readNowWhereNot ? AsReadNow(node, branch) : null;

    /// <summary>Spreads <paramref name="chance"/> over the moves reached from a state; false when the rules can't say.</summary>
    private static bool Walk(MoveGraph graph, string id, double chance, List<string> log, Dictionary<string, double> into, int depth,
        Func<MoveNodeConditional, int, bool?> holdsOf, Func<RandomBranch, double> weightOf)
    {
        if (depth > MaxDepth || !graph.Nodes.TryGetValue(id, out var node)) return false;
        switch (node)
        {
            case MoveNodeMove moveNodeMove:
                into[moveNodeMove.Id] = into.GetValueOrDefault(moveNodeMove.Id) + chance;
                return true;
            case MoveNodeRandom moveNodeRandom:
            {
                var weights = moveNodeRandom.Branches.Select(randomBranch => Weight(randomBranch, log, weightOf)).ToList();
                if (weights.Any(double.IsNaN)) return false; // a weight that couldn't be read
                var total = weights.Sum();
                if (total <= 0) return false; // the game throws: no valid state
                for (var i = 0; i < moveNodeRandom.Branches.Count; i++)
                    if (weights[i] > 0 && !Walk(graph, moveNodeRandom.Branches[i].To, chance * weights[i] / total, log, into, depth + 1, holdsOf, weightOf)) return false;
                return true;
            }
            case MoveNodeConditional moveNodeConditional:
            {
                for (var branch = 0; branch < moveNodeConditional.Branches.Count; branch++)
                {
                    var holds = holdsOf(moveNodeConditional, branch);
                    if (holds == null) return false; // a condition that couldn't be read, before the one that holds
                    if (holds.Value) return Walk(graph, moveNodeConditional.Branches[branch].To, chance, log, into, depth + 1, holdsOf, weightOf);
                }
                return false; // the game throws: no valid next state
            }
            default:
                return false;
        }
    }

    /// <summary>A branch's weight now (RandomBranchState.GetStateWeight, line for line).</summary>
    internal static double Weight(RandomBranch randomBranch, IReadOnlyList<string> log) => Weight(randomBranch, log, WeightNow);

    /// <summary>
    /// A branch's weight (RandomBranchState.GetStateWeight, line for line), its lambda's as <paramref name="weightOf"/> gives it: 0 by its repeat rule
    /// whatever that is (a weight not known on a branch that can't be picked changes nothing).
    /// </summary>
    internal static double Weight(RandomBranch randomBranch, IReadOnlyList<string> log, Func<RandomBranch, double> weightOf)
    {
        double num = 1;
        if (randomBranch.Repeat == MoveRepeat.UseOnlyOnce)
        {
            if (log.Contains(randomBranch.To)) num = 0;
        }
        else if (randomBranch.Repeat != MoveRepeat.CanRepeatForever)
        {
            var times = randomBranch.Repeat == MoveRepeat.CannotRepeat ? 1 : randomBranch.MaxTimes;
            num = log.Count < times ? 1 : 0;
            var index = 0;
            while (log.Count >= times && index < times && log.Count - index > 0)
            {
                if (log[log.Count - 1 - index] != randomBranch.To)
                {
                    num = 1;
                    break;
                }
                index++;
            }
        }
        if (randomBranch.Cooldown > 0 && log.Reverse().Take(randomBranch.Cooldown).Contains(randomBranch.To)) return 0;
        return num == 0 ? 0 : num * weightOf(randomBranch);
    }
}
