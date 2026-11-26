namespace SpireQoL.Sim;

/// <summary>
/// One enemy's move in a projected turn (decision #304): its attacks' hits (each its base and the captured terms on each player, as the adapter reads
/// the intent on screen), its code's steps (SimAdapter.MoveOf: what it does before a later hit) and whether every intent it shows is an attack (a move
/// whose code isn't read is its hits alone then, as the intent on screen is).
/// </summary>
public sealed record ProjectedMove(string Id)
{
    /// <summary>Its hits, in order (each attack intent's Repeats); null: it doesn't attack.</summary>
    public IReadOnlyList<IntentHit>? Intent { get; init; }

    /// <summary>Its steps as its code runs them; null: not read.</summary>
    public IReadOnlyList<SimAction>? Actions { get; init; }

    /// <summary>Every intent it shows is an attack: unread, it's its hits alone.</summary>
    public bool OnlyAttacks { get; init; }
}

/// <summary>An enemy's moves in a projected turn: each it can make, with its chance (one at 1 when its rules make it certain: IntentForecast).</summary>
public sealed record ProjectedChoice(int EnemyId, IReadOnlyList<(ProjectedMove Move, double Chance)> Options);

/// <summary>
/// An enemy's move rules for a projection (decision #309): its graph with each condition as the simulator's (IntentReader.Graph, decision #195), the
/// move on screen and the state after it (a stun's named move for a stunned one), its moves so far (that one last), and each of its moves read as
/// the intent on screen is (one behind a hidden intent, or whose attack can't be read, is left out: its turn is unknown). Pure data, shared.
/// </summary>
public sealed record ProjectedRules(MoveGraph Graph, string Current, string? CurrentFollowUp, IReadOnlyList<string> Log, IReadOnlyDictionary<string, ProjectedMove> Moves)
{
    /// <summary>
    /// The rules of a creature that joins in the projection (decision #378): its machine new, no move on screen (Current names no state, so its first
    /// roll is from its initial state) and none so far.
    /// </summary>
    public static ProjectedRules Joining(MoveGraph graph, IReadOnlyDictionary<string, ProjectedMove> moves) =>
        new(graph, Current: "", CurrentFollowUp: null, Log: Array.Empty<string>(), moves);
}

/// <summary>
/// What a projected move hits you for, each hit in order (decision #304): as it lands (the damage math at that hit, with no plays of yours in between),
/// and as its intent reads when your turn before it starts (what the game shows then: the forecast audit's). Null where it isn't known, with why.
/// </summary>
public sealed record ProjectedHits(IReadOnlyList<int>? Lands, IReadOnlyList<int>? AtYourTurnStart)
{
    public string? Unknown { get; init; }

    /// <summary>
    /// A roll before it changed what it counts (decision #308): another enemy's random pick, a random target. Its numbers count only what's certain
    /// (as its intent reads when your turn starts, on what every outcome shares); the forecast audit names it when the game's intent differs.
    /// </summary>
    public string? Uncertain { get; init; }
}

/// <summary>Each projected turn's moves' hits (decision #304): by turn (0: the one after the intent on screen), enemy id and move id.</summary>
public sealed class Projection
{
    public List<Dictionary<int, Dictionary<string, ProjectedHits>>> Turns { get; } = new();

    /// <summary>What it cost (SimWork's counts at its end, as SimOutcome's), and the cap that stopped it if one did (the projection is unknown past it).</summary>
    public int Steps { get; internal set; }
    public int Stepped { get; internal set; }
    public int Copies { get; internal set; }
    public int PeakHeld { get; internal set; }
    public string? CapHit { get; internal set; }

    /// <summary>A move's hits in a projected turn, or null: not projected (past where it was known).</summary>
    public ProjectedHits? Of(int turn, int enemyId, string move) =>
        turn < Turns.Count && Turns[turn].TryGetValue(enemyId, out var moves) && moves.TryGetValue(move, out var hits) ? hits : null;

    /// <summary>
    /// Each enemy whose moves the projection worked out from its rules on the projected state (decision #309): its forecast turns, the first random
    /// pick ending them, as IntentForecast.Forecast gives them from the game's lambdas as they answer now.
    /// </summary>
    public Dictionary<int, List<ForecastTurn>> Moves { get; } = new();

    /// <summary>An enemy's forecast turns as the projection worked them out, or null: it had no rules read (the forecast as read now stands).</summary>
    public List<ForecastTurn>? ForecastOf(int enemyId) => Moves.TryGetValue(enemyId, out var turns) ? turns : null;
}

public static partial class SimEngine
{
    /// <summary>
    /// The Intent Forecast's later turns, projected from the board (decisions #304, #308): this turn's end with no more plays and the enemy turn (as
    /// Run), the enemies' turn end; then for each forecast turn your next turn (its start: every listener's turn-start code, your block cleared, your
    /// hand left out, since what you'd draw isn't known: YourTurnStart, decision #306; its end with no plays: your powers, relics and orbs as their
    /// code says), the enemies' turn start, each enemy's forecast move (a branch for each move of a random pick, with its chance; its code's steps
    /// before its hits) and their turn's end. It follows only what the forecast shows (SimState.Forecasting): every creature's powers and what they
    /// keep, the enemies' moves and stuns; no one's HP, block or death, no hand, draw, energy or orb, so branches that differ only there are one.
    /// Where a roll leaves what it follows apart, each phase ends on what every outcome shares (Certain): a number changed in only some counts as it
    /// was. Each move's hits on you are read as they land and as its intent reads when your turn starts; where a roll changed them they count only
    /// what's certain (ProjectedHits.Uncertain). What the simulator can't follow there (SimState.Unmodelled, UnmodelledAtTheirTurnEnd, a move whose
    /// code isn't read) leaves the turn unknown.
    /// </summary>
    /// <param name="turns">Each forecast turn's moves, by enemy (IntentForecast's turns after the intent on screen, its rules as the game's lambdas
    /// answer now); a living enemy with none that turn (past its first random pick) makes no move the projection counts.</param>
    /// <param name="rules">The enemies whose moves the projection works out itself (decision #309), by id: each turn's move rolled as your turn
    /// starts (after Hook.BeforeSideTurnStart, as CombatManager.StartTurn's PrepareForNextTurn), from its rules asked of the projected state
    /// (IntentForecast.NextInProjection), in place of its moves in <paramref name="turns"/>.</param>
    /// <param name="joining">The move rules of each monster a move or a death can add (decision #378), by its class (SimEnemy.Monster): a creature
    /// that joins in the projection rolls its moves from them as one on the board does (JoinedRolls).</param>
    public static Projection Project(SimState start, IReadOnlyList<IReadOnlyList<ProjectedChoice>> turns, IReadOnlyDictionary<int, ProjectedRules>? rules = null,
        IReadOnlyDictionary<string, ProjectedRules>? joining = null) =>
        Project(start, turns, rules, new SimWork(), joining);

    /// <summary>The projection with <paramref name="work"/>: SimJob's on a worker (its cancellation and background gate, decision #107).</summary>
    internal static Projection Project(SimState start, IReadOnlyList<IReadOnlyList<ProjectedChoice>> turns, IReadOnlyDictionary<int, ProjectedRules>? rules, SimWork work,
        IReadOnlyDictionary<string, ProjectedRules>? joining = null)
    {
        var projection = new Projection();
        var rolling = rules?.ToDictionary(entry => entry.Key, entry => new MoveRoll(entry.Value)) ?? new Dictionary<int, MoveRoll>();
        var turnCount = rolling.Count > 0 || joining is { Count: > 0 } ? Math.Max(turns.Count, IntentForecast.MaxTurns) : turns.Count;
        var onBoard = new HashSet<int>();
        foreach (var enemy in start.Enemies) onBoard.Add(enemy.Id);
        if (turnCount == 0) return projection;
        foreach (var id in rolling.Keys) projection.Moves[id] = new List<ForecastTurn>();
        var first = start.Clone();
        first.Work = work;
        first.Log = false; // no breakdown: only the hits are read
        first.Forecasting = true;
        MarkUnread(first);
        var branches = new BranchSet(first);
        var uncertain = new Dictionary<int, string>();
        try
        {
            // This turn, with no more plays: as Run without a play.
            Certainly(branches, uncertain, () => Fighting(branches, () => AutoPostPlay(branches)));
            Certainly(branches, uncertain, () => EndOfTurn(branches));
            JoinedRolls(branches.States, joining, onBoard, rolling, projection, rolledAtAdd: true);
            Certainly(branches, uncertain, () => Fighting(branches, () => EnemyTurnStart(branches)));
            Certainly(branches, uncertain, () => TheirTurn(branches, choices: null, row: null));
            for (var turn = 0; turn < turnCount; turn++)
            {
                // Your next turn starts (CombatManager.StartTurn for the players, its listeners' code: decision #306): what its intents read then.
                // The enemies whose rules are read roll their moves there, after its BeforeSideTurnStart (decision #309).
                var given = turn < turns.Count ? turns[turn] : Array.Empty<ProjectedChoice>();
                IReadOnlyList<ProjectedChoice> choices = given;
                Certainly(branches, uncertain, () => YourTurnStart(branches, rolled: () =>
                {
                    JoinedRolls(branches.States, joining, onBoard, rolling, projection, rolledAtAdd: false);
                    choices = Rolled(branches.States, given, rolling, projection, turn);
                }));
                if (choices.Count == 0) break;
                var state = branches.States[0];
                var row = new Dictionary<int, Dictionary<string, ProjectedHits>>();
                projection.Turns.Add(row);
                var unknown = state.Unmodelled ?? state.UnmodelledAtTheirTurnEnd;
                foreach (var choice in choices)
                    row[choice.EnemyId] = choice.Options.ToDictionary(option => option.Move.Id,
                        option => unknown != null ? new ProjectedHits(Lands: null, AtYourTurnStart: null) { Unknown = unknown }
                            : new ProjectedHits(Lands: null, HitsOnYou(state, choice.EnemyId, option.Move)) { Uncertain = uncertain.GetValueOrDefault(choice.EnemyId) },
                        StringComparer.Ordinal);
                if (unknown != null)
                {
                    // Past what the simulator follows, the later moves are rolled from the rules as the game's lambdas answer now, as before.
                    for (var later = turn + 1; later < turnCount; later++) Rolled(states: null, Array.Empty<ProjectedChoice>(), rolling, projection, later);
                    break;
                }
                // Its end with no plays, the enemies' turn start, their moves, their turn's end.
                Certainly(branches, uncertain, () => EndOfTurn(branches));
                JoinedRolls(branches.States, joining, onBoard, rolling, projection, rolledAtAdd: true);
                Certainly(branches, uncertain, () => Fighting(branches, () => EnemyTurnStart(branches)));
                Certainly(branches, uncertain, () => TheirTurn(branches, choices, row));
            }
        }
        catch (SimCapped)
        {
            // Past the cap the projection is unknown: a turn not finished keeps only what was read before it stopped.
            foreach (var row in projection.Turns)
                foreach (var moves in row.Values)
                    foreach (var move in moves.Keys.ToList())
                        if (moves[move].Lands == null && moves[move].Unknown == null) moves[move] = moves[move] with { Unknown = "the projection stopped at its cap" };
            projection.CapHit = work.CapHit ?? "the work cap";
        }
        finally
        {
            work.EndStep();
            (projection.Steps, projection.Stepped, projection.Copies, projection.PeakHeld) = (work.Steps, work.Stepped, work.Copies, work.PeakHeld);
            work.Release();
        }
        return projection;
    }

    /// <summary>
    /// Where an enemy's moves stand in a projection whose rules it reads (decision #309): the move it made last (the one on screen at first), the
    /// moves so far, and whether its forecast is over (its first random pick made, a stun taking its next move, rules that can't say).
    /// </summary>
    private sealed class MoveRoll
    {
        public MoveRoll(ProjectedRules rules) => (Rules, Last, Log) = (rules, rules.Current, rules.Log.ToList());

        public ProjectedRules Rules { get; }
        public string Last { get; set; }
        public List<string> Log { get; }
        public bool Over { get; set; }

        /// <summary>The state its next move is rolled from: the move after the last one (the initial state when it names none).</summary>
        public string From => Last == Rules.Current && Rules.CurrentFollowUp is { } followUp ? followUp
            : Rules.Graph.Nodes.GetValueOrDefault(Last) is MoveNodeMove { FollowUp: { } next } ? next : Rules.Graph.InitialId;
    }

    /// <summary>
    /// Each creature that joined the fight in the projection since the board (a move's add, a death's spawn) whose monster's rules were read
    /// (<paramref name="joining"/>, decision #378) rolls its moves from here as one on the board does: its machine new (MonsterModel.SetUpForCombat),
    /// so its first roll is its initial state's (MonsterMoveStateMachine.RollMove: a move it starts on stays until it's made) with no moves so far
    /// (ProjectedRules.Joining). One that joined on the players' side (<paramref name="rolledAtAdd"/>: CombatManager.AfterCreatureAdded rolls it as it
    /// joins) has made that roll already: it's its move in the enemy turn that follows, which the simulator gives a creature joining then none of
    /// (a death's spawn makes no move the turn it joins), and its next roll follows it; a random one there ends its forecast. One that joined on the
    /// enemies' side rolls at your turn's start (Creature.PrepareForNextTurn). A creature of a monster with no rules read makes no move counted.
    /// </summary>
    private static void JoinedRolls(List<SimState> states, IReadOnlyDictionary<string, ProjectedRules>? joining, HashSet<int> onBoard, Dictionary<int, MoveRoll> rolling,
        Projection projection, bool rolledAtAdd)
    {
        if (joining is not { Count: > 0 }) return;
        foreach (var state in states)
        {
            foreach (var enemy in state.Enemies)
            {
                if (!enemy.Alive || onBoard.Contains(enemy.Id) || rolling.ContainsKey(enemy.Id) || !joining.TryGetValue(enemy.Monster, out var rules)) continue;
                var roll = new MoveRoll(rules);
                rolling[enemy.Id] = roll;
                projection.Moves[enemy.Id] = new List<ForecastTurn>();
                if (!rolledAtAdd) continue;
                var first = IntentForecast.NextInProjection(rules.Graph, roll.From, roll.Log, state, enemy.Id);
                if (first is not { Count: 1 })
                {
                    roll.Over = true;
                    continue;
                }
                roll.Last = first[0].Move;
                roll.Log.Add(first[0].Move);
            }
        }
    }

    /// <summary>
    /// Each enemy's moves this projected turn (decision #309): an enemy whose rules are read rolls them as the game does as your turn starts, from the
    /// state after its last move (a stun's named move after its stunned one), its conditions asked of the projected <paramref name="states"/> (as the
    /// game's lambdas answer now where one isn't read, or the states are unknown or null), each move read as its intent is; the others take theirs
    /// from <paramref name="given"/>. A random pick is the last turn of its forecast; so is a stun that takes its next move (the forecast can't show
    /// it) and a roll the branches don't agree on. Each enemy's rolled turn is kept in <paramref name="projection"/>.Moves.
    /// </summary>
    private static IReadOnlyList<ProjectedChoice> Rolled(List<SimState>? states, IReadOnlyList<ProjectedChoice> given, Dictionary<int, MoveRoll> rolling,
        Projection projection, int turn)
    {
        var choices = given.Where(choice => !rolling.ContainsKey(choice.EnemyId)).ToList();
        foreach (var (enemyId, roll) in rolling)
        {
            if (roll.Over) continue;
            var known = states is { Count: > 0 } && states.All(state => state.Unmodelled == null && state.UnmodelledAtTheirTurnEnd == null);
            IReadOnlyList<(string Move, double Chance)>? next = null;
            if (known)
            {
                foreach (var state in states!)
                {
                    if (state.Enemy(enemyId) is not { Alive: true } enemy || enemy.Stunned) { next = null; break; }
                    var from = enemy.NextAfterStun ?? roll.From;
                    var these = IntentForecast.NextInProjection(roll.Rules.Graph, from, roll.Log, state, enemyId);
                    if (these == null || next != null && !these.SequenceEqual(next)) { next = null; break; }
                    next = these;
                }
            }
            else next = IntentForecast.NextInProjection(roll.Rules.Graph, roll.From, roll.Log, state: null, enemyId);
            if (next is not { Count: > 0 })
            {
                roll.Over = true;
                continue;
            }
            projection.Moves[enemyId].Add(new ForecastTurn(next));
            if (next.Count > 1) roll.Over = true; // the first random pick ends it
            else
            {
                roll.Last = next[0].Move;
                roll.Log.Add(next[0].Move);
                if (states != null) foreach (var state in states) if (state.Enemy(enemyId) is { } enemy) enemy.NextAfterStun = null; // its stun's named move is rolled
            }
            choices.Add(new ProjectedChoice(enemyId, next.Select(option =>
                (roll.Rules.Moves.GetValueOrDefault(option.Move) ?? new ProjectedMove(option.Move), option.Chance)).ToList()));
        }
        return choices;
    }

    /// <summary>
    /// The enemies' turn in a projection: each living enemy's forecast move (<paramref name="choices"/>; null: the intents on screen), their moves,
    /// each move's hits as they landed (<paramref name="row"/>), each stun made spent (its named move rolled next: SimEnemy.NextAfterStun), their
    /// turn's end, and what their turn leaves for yours (TheirTurnIsOver).
    /// </summary>
    private static void TheirTurn(BranchSet branches, IReadOnlyList<ProjectedChoice>? choices, Dictionary<int, Dictionary<string, ProjectedHits>>? row)
    {
        if (choices != null) GiveMoves(branches, choices);
        branches.Do(EnemyTurn);
        branches.Do(StunsSpent);
        if (choices != null && row != null) Landed(branches.States, choices, row);
        Fighting(branches, () => EnemyTurnEnd(branches));
        branches.Do(TheirTurnIsOver);
    }

    /// <summary>
    /// One phase of a projection, ending on what every outcome shares (decision #308): branches left apart by a roll in what the projection follows
    /// become one (Certain), each followed number as every branch has it, or as it was before the phase where they differ. An enemy whose hits on
    /// you a roll changed there (its damage math or its stun differing between the branches) counts only what's certain from here, with why.
    /// </summary>
    private static void Certainly(BranchSet branches, Dictionary<int, string> uncertain, Action phase)
    {
        if (branches.States.Count != 1) { phase(); return; }
        var before = branches.States[0].Clone();
        phase();
        if (branches.States.Count <= 1) return;
        foreach (var enemy in branches.States[0].Enemies)
        {
            if (uncertain.ContainsKey(enemy.Id)) continue;
            if (RollChangesItsHits(branches.States, enemy.Id)) uncertain[enemy.Id] = $"a roll changes what {enemy.Name}'s hits count: only what's certain counts";
        }
        branches.States = new List<SimState> { Certain(before, branches.States) };
    }

    /// <summary>The branches don't agree on an enemy's damage math on you (a probe of its hits) or its stun: a roll changed its hits.</summary>
    private static bool RollChangesItsHits(List<SimState> states, int enemyId)
    {
        int[]? agreed = null;
        foreach (var state in states)
        {
            if (state.Enemy(enemyId) is not { } enemy) return true;
            var probe = new[] { HitOnYou(state, enemyId, ProbeHit), HitOnYou(state, enemyId, ProbeHitTwo), enemy.Stunned ? 1 : 0, enemy.NextAfterStun?.GetHashCode() ?? 0 };
            if (agreed == null) agreed = probe;
            else if (!agreed.SequenceEqual(probe)) return true;
        }
        return false;
    }

    /// <summary>Two hits that tell an added term from a multiplied one apart in a probe of the damage math.</summary>
    private static readonly IntentHit ProbeHit = new(Base: 10), ProbeHitTwo = new(Base: 17);

    /// <summary>
    /// What every branch shares (decision #308: the projection counts only what's certain): one state, its weight theirs together, each creature's
    /// powers, relics and what they keep, and each enemy's stun, entry by entry as every branch has it, or as it was before (<paramref name="before"/>)
    /// where they differ. What isn't followed is the first branch's. An unknown in any branch stays.
    /// </summary>
    private static SimState Certain(SimState before, List<SimState> states)
    {
        var result = states[0];
        result.Weight = states.Sum(state => state.Weight);
        result.Unmodelled ??= states.Select(state => state.Unmodelled).FirstOrDefault(why => why != null);
        result.UnmodelledAtTheirTurnEnd ??= states.Select(state => state.UnmodelledAtTheirTurnEnd).FirstOrDefault(why => why != null);
        foreach (var enemy in result.Enemies)
        {
            var others = states.Select(state => state.Enemy(enemy.Id)).ToList();
            if (others.Any(other => other == null))
            {
                result.Unmodelled ??= "a roll decides which enemies are in the fight";
                continue;
            }
            var was = before.Enemy(enemy.Id);
            enemy.Powers = CertainSet(was?.Powers ?? PowerSet.Empty, others.Select(other => other!.Powers));
            enemy.Fields = CertainSet(was?.Fields ?? NamedCounts.Empty, others.Select(other => other!.Fields));
            if (others.Any(other => other!.Stunned != enemy.Stunned || other.NextAfterStun != enemy.NextAfterStun || other.StunnedMove != enemy.StunnedMove))
                (enemy.Stunned, enemy.NextAfterStun, enemy.StunnedMove) = (was?.Stunned ?? false, was?.NextAfterStun, was?.StunnedMove);
        }
        foreach (var id in result.PlayerIds)
        {
            var was = before.Player(id);
            var others = states.Select(state => state.Player(id)!).ToList();
            var powers = CertainSet(was?.Powers ?? PowerSet.Empty, others.Select(other => other.Powers));
            var relics = CertainSet(was?.Relics ?? RelicSet.Empty, others.Select(other => other.Relics));
            var fields = CertainSet(was?.Fields ?? NamedCounts.Empty, others.Select(other => other.Fields));
            var player = result.Player(id)!;
            if (ReferenceEquals(powers, player.Powers) && ReferenceEquals(relics, player.Relics) && ReferenceEquals(fields, player.Fields)) continue;
            var mutable = result.MutablePlayer(id);
            (mutable.Powers, mutable.Relics, mutable.Fields) = (powers, relics, fields);
        }
        return result;
    }

    /// <summary>
    /// The entries every set has alike, in the order they first come; where they differ (in amount, data or being there at all), the entry as it
    /// was before (<paramref name="was"/>), or none when it wasn't there. The first set itself when they're all alike.
    /// </summary>
    private static NamedSet<TName> CertainSet<TName>(NamedSet<TName> was, IEnumerable<NamedSet<TName>> sets) where TName : IEquatable<TName>
    {
        var all = sets.ToList();
        var first = all[0];
        if (all.All(set => ReferenceEquals(set, first) || set.Items.SequenceEqual(first.Items))) return first;
        var result = NamedSet<TName>.Empty;
        foreach (var name in all.SelectMany(set => set.Items.Select(entry => entry.Name)).Distinct())
        {
            var alike = all.All(set => set.Has(name) == first.Has(name) && set[name] == first[name] && set.DataOf(name) == first.DataOf(name));
            var source = alike ? first : was;
            if (source.Has(name)) result = result.With(name, source[name], source.DataOf(name));
        }
        return result;
    }

    /// <summary>
    /// The enemies' moves are made in a projected run: a stunned enemy made its stunned move (CreatureCmd.Stun's, MustPerformOnceBeforeTransitioning),
    /// so its stun is spent; its named move (SimEnemy.NextAfterStun) is where its next roll starts.
    /// </summary>
    private static void StunsSpent(SimState state)
    {
        foreach (var enemy in state.Enemies)
            if (enemy.Stunned) (enemy.Stunned, enemy.StunnedMove) = (false, null);
    }

    /// <summary>
    /// The enemies' turn is over in a projected run: the block they have now stays on them through your turn (the game clears it as their next turn
    /// starts, which EnemyTurn does), so nothing of your turn's is put back (Run's TurnEndBlockBack is for the outcome); each move's projection is cleared.
    /// </summary>
    private static void TheirTurnIsOver(SimState state)
    {
        foreach (var enemy in state.Enemies)
        {
            (enemy.TurnEndBlock, enemy.TurnEndBlockBroken) = (-1, false);
            (enemy.ProjectedMove, enemy.MoveUnknown, enemy.MoveNotForecast, enemy.LandedOnYou, enemy.LandedUnknown) = (null, null, false, null, false);
        }
    }

    /// <summary>Each hit of a move on you as its intent reads on <paramref name="state"/> (what the game shows as your turn starts).</summary>
    private static IReadOnlyList<int> HitsOnYou(SimState state, int enemyId, ProjectedMove move) =>
        (move.Intent ?? Array.Empty<IntentHit>()).Select(hit => HitOnYou(state, enemyId, hit)).ToList();

    /// <summary>One hit of a move on you through the damage math now (a Move from the enemy: its Strength and Weak, your Vulnerable...), as IntentDamage.</summary>
    private static int HitOnYou(SimState state, int enemyId, IntentHit hit) =>
        CalcDamage(state, BaseOf(state.Enemy(enemyId), hit), new DamageCtx(enemyId, Who.You, SimProps.Move, Card: null, Play: null), hit.TermsOn(Who.You));

    /// <summary>
    /// Each living enemy's move this projected turn: its one move, or a branch for each move of a random pick with its chance as the branch's share;
    /// one with no move given makes none the projection counts (SimEnemy.MoveNotForecast: what it would do isn't certain).
    /// </summary>
    private static void GiveMoves(BranchSet branches, IReadOnlyList<ProjectedChoice> choices)
    {
        branches.Do(state =>
        {
            foreach (var enemy in state.Enemies)
            {
                if (!enemy.Alive || enemy.Stunned) continue; // a stun since the roll takes its move: it makes its stunned move
                var choice = choices.FirstOrDefault(each => each.EnemyId == enemy.Id);
                if (choice is { Options.Count: 1 }) Give(enemy, choice.Options[0].Move);
                else if (choice == null) enemy.MoveNotForecast = true;
            }
        });
        foreach (var choice in choices)
        {
            if (choice.Options.Count < 2) continue;
            branches.Each(state =>
            {
                if (state.Enemy(choice.EnemyId) is not { Alive: true, Stunned: false }) return new[] { state };
                return choice.Options.Select(option =>
                {
                    var branch = state.Clone();
                    Give(branch.Enemy(choice.EnemyId)!, option.Move);
                    branch.Weight *= option.Chance;
                    return branch;
                }).ToList();
            });
        }
    }

    /// <summary>An enemy makes <paramref name="move"/> this projected turn: its hits, its code's steps (unknown when they aren't read and it isn't only attacks).</summary>
    private static void Give(SimEnemy enemy, ProjectedMove move)
    {
        enemy.ProjectedMove = move.Id;
        enemy.MoveUnknown = move.Actions == null && !move.OnlyAttacks ? $"{enemy.Name}'s {move.Id} isn't read from its code" : null;
        enemy.MoveNotForecast = false;
        enemy.Intent = move.Intent?.ToList();
        enemy.Hits = new List<int>(new int[move.Intent?.Count ?? 0]);
        enemy.TeammateHits = new();
        enemy.Move = move.Actions;
        enemy.IntendsToAttack = move.Intent is { Count: > 0 };
        enemy.Stunned = false; // a stun takes one move: the forecast's moves come after it
        (enemy.StunnedMove, enemy.NextAfterStun) = (null, null);
        enemy.MoveBlock = 0;
        (enemy.LandedOnYou, enemy.LandedUnknown) = (null, false);
    }

    /// <summary>A projected move's hit on you, as it lands (EnemyTurn's Attack, before your block): kept on the enemy, with whether the branch was known then.</summary>
    private static void LandsOnYou(SimState state, SimEnemy enemy, int amount)
    {
        var landed = enemy.LandedOnYou ?? Array.Empty<int>();
        var more = new int[landed.Length + 1];
        landed.CopyTo(more, 0);
        more[^1] = amount;
        enemy.LandedOnYou = more;
        if (state.Unmodelled != null) enemy.LandedUnknown = true;
    }

    /// <summary>
    /// Each projected move's hits on you as they landed, where every branch that gave its enemy that move agrees; where a roll before them (another
    /// enemy's random pick) left them apart, only what's certain counts: its hits as its intent read when your turn started (ProjectedHits.Uncertain,
    /// decision #308). Unknown where what came before them isn't followed, or it made no move.
    /// </summary>
    private static void Landed(List<SimState> states, IReadOnlyList<ProjectedChoice> choices, Dictionary<int, Dictionary<string, ProjectedHits>> row)
    {
        foreach (var choice in choices)
        {
            if (!row.TryGetValue(choice.EnemyId, out var moves)) continue;
            foreach (var (move, _) in choice.Options)
            {
                if (!moves.TryGetValue(move.Id, out var hits) || hits.Unknown != null) continue;
                var count = move.Intent?.Count ?? 0;
                int[]? agreed = null;
                string? unknown = null, uncertain = null;
                var made = false;
                foreach (var branch in states)
                {
                    if (branch.Enemy(choice.EnemyId) is not { } enemy || enemy.ProjectedMove != move.Id) continue;
                    made = true;
                    if (enemy.LandedUnknown) { unknown = "what comes before its hits isn't followed"; break; }
                    var landed = enemy.LandedOnYou ?? Array.Empty<int>();
                    if (agreed == null) agreed = landed;
                    else if (!agreed.SequenceEqual(landed)) uncertain = "a roll before its hits changes them: only what's certain counts";
                }
                if (!made && count > 0) unknown ??= "it doesn't make its move";
                if (agreed != null && agreed.Length != count) uncertain ??= "not every hit lands in every outcome: only what's certain counts";
                moves[move.Id] = unknown != null ? hits with { Unknown = unknown }
                    : uncertain != null ? hits with { Lands = hits.AtYourTurnStart, Uncertain = hits.Uncertain ?? uncertain }
                    : hits with { Lands = agreed ?? Array.Empty<int>() };
            }
        }
    }
}
