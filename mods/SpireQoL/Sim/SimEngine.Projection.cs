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
/// What a projected move hits you for, each hit in order (decision #304): as it lands (the damage math at that hit, with no plays of yours in between),
/// and as its intent reads when your turn before it starts (what the game shows then: the forecast audit's). Null where it isn't known, with why.
/// </summary>
public sealed record ProjectedHits(IReadOnlyList<int>? Lands, IReadOnlyList<int>? AtYourTurnStart)
{
    public string? Unknown { get; init; }
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
}

public static partial class SimEngine
{
    /// <summary>
    /// The Intent Forecast's later turns, projected from the board (decision #304): this turn's end with no more plays and the enemy turn (as Run),
    /// the enemies' turn end; then for each forecast turn your next turn (its start: every listener's turn-start code, your block cleared, your hand left
    /// out, since what you'd draw isn't known: YourTurnStart, decision #306; its end with no plays: your powers, relics and orbs as their code says), the enemies' turn start, each enemy's forecast move (a
    /// branch for each move of a random pick, with its chance; its code's steps before its hits) and their turn's end. Each move's hits on you are
    /// read as they land and as its intent reads when your turn starts; a number some branches don't share is unknown (a roll decides it), and so
    /// is everything past what the simulator can't follow (SimState.Unmodelled, UnmodelledAtTheirTurnEnd, a move it doesn't know).
    /// </summary>
    /// <param name="turns">Each forecast turn's moves, by enemy (IntentForecast's turns after the intent on screen); a living enemy with none that
    /// turn makes the branch unknown from its move on.</param>
    public static Projection Project(SimState start, IReadOnlyList<IReadOnlyList<ProjectedChoice>> turns) => Project(start, turns, new SimWork());

    /// <summary>The projection with <paramref name="work"/>: SimJob's on a worker (its cancellation and background gate, decision #107).</summary>
    internal static Projection Project(SimState start, IReadOnlyList<IReadOnlyList<ProjectedChoice>> turns, SimWork work)
    {
        var projection = new Projection();
        if (turns.Count == 0) return projection;
        var first = start.Clone();
        first.Work = work;
        first.Log = false; // no breakdown: only the hits are read
        MarkUnread(first);
        var branches = new BranchSet(first);
        try
        {
            // This turn, with no more plays: as Run without a play.
            Fighting(branches, () => AutoPostPlay(branches));
            EndOfTurn(branches);
            Fighting(branches, () => EnemyTurnStart(branches));
            branches.Do(EnemyTurn);
            Fighting(branches, () => EnemyTurnEnd(branches));
            branches.Do(TheirTurnIsOver);
            foreach (var choices in turns)
            {
                // The branches whose fight goes on with you in it: the others have no next turn.
                branches.States = branches.States.Where(branch => !FightOver(branch) && !branch.You.Died).ToList();
                if (branches.States.Count == 0) break;
                // Your next turn starts (CombatManager.StartTurn for the players, its listeners' code: decision #306): what its intents read then.
                // One it ends (your poison killing you, its own Noxious Fumes' kill ending the fight) has no turn after it.
                YourTurnStart(branches);
                branches.States = branches.States.Where(branch => !FightOver(branch) && !branch.You.Died).ToList();
                if (branches.States.Count == 0) break;
                var row = new Dictionary<int, Dictionary<string, ProjectedHits>>();
                projection.Turns.Add(row);
                var unknown = branches.States.Select(branch => branch.Unmodelled ?? branch.UnmodelledAtTheirTurnEnd).FirstOrDefault(why => why != null);
                foreach (var choice in choices)
                    row[choice.EnemyId] = choice.Options.ToDictionary(option => option.Move.Id,
                        option => unknown != null ? new ProjectedHits(Lands: null, AtYourTurnStart: null) { Unknown = unknown } : AtYourTurnStart(branches.States, choice.EnemyId, option.Move), StringComparer.Ordinal);
                if (unknown != null) break;
                // Its end with no plays, the enemies' turn start, their moves, their turn's end.
                EndOfTurn(branches);
                Fighting(branches, () => EnemyTurnStart(branches));
                GiveMoves(branches, choices);
                branches.Do(EnemyTurn);
                Landed(branches.States, choices, row);
                Fighting(branches, () => EnemyTurnEnd(branches));
                branches.Do(TheirTurnIsOver);
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
    /// The enemies' turn is over in a projected run: the block they have now stays on them through your turn (the game clears it as their next turn
    /// starts, which EnemyTurn does), so nothing of your turn's is put back (Run's TurnEndBlockBack is for the outcome); each move's projection is cleared.
    /// </summary>
    private static void TheirTurnIsOver(SimState state)
    {
        foreach (var enemy in state.Enemies)
        {
            (enemy.TurnEndBlock, enemy.TurnEndBlockBroken) = (-1, false);
            (enemy.ProjectedMove, enemy.MoveUnknown, enemy.LandedOnYou, enemy.LandedUnknown) = (null, null, null, false);
        }
    }

    /// <summary>Each option's hits on you as its intent reads when your turn starts, the same in every branch where its enemy lives; else unknown, with why.</summary>
    private static ProjectedHits AtYourTurnStart(List<SimState> states, int enemyId, ProjectedMove move)
    {
        List<int>? agreed = null;
        foreach (var branch in states)
        {
            if (branch.Enemy(enemyId) is not { Alive: true }) continue;
            var hits = (move.Intent ?? Array.Empty<IntentHit>()).Select(hit => HitOnYou(branch, enemyId, hit)).ToList();
            if (agreed == null) agreed = hits;
            else if (!agreed.SequenceEqual(hits)) return new ProjectedHits(Lands: null, AtYourTurnStart: null) { Unknown = "a roll before it decides its hits" };
        }
        return agreed == null ? new ProjectedHits(Lands: null, AtYourTurnStart: null) { Unknown = "it dies before then" } : new ProjectedHits(Lands: null, agreed);
    }

    /// <summary>One hit of a move on you through the damage math now (a Move from the enemy: its Strength and Weak, your Vulnerable...), as IntentDamage.</summary>
    private static int HitOnYou(SimState state, int enemyId, IntentHit hit) =>
        CalcDamage(state, hit.Base, new DamageCtx(enemyId, Who.You, SimProps.Move, Card: null, Play: null), hit.TermsOn(Who.You));

    /// <summary>
    /// Each living enemy's move this projected turn: its one move, or a branch for each move of a random pick with its chance as the branch's share;
    /// one with no move known is unknown from its move on (EnemyTurn: SimEnemy.MoveUnknown).
    /// </summary>
    private static void GiveMoves(BranchSet branches, IReadOnlyList<ProjectedChoice> choices)
    {
        branches.Do(state =>
        {
            foreach (var enemy in state.Enemies)
            {
                if (!enemy.Alive) continue;
                var choice = choices.FirstOrDefault(each => each.EnemyId == enemy.Id);
                if (choice is { Options.Count: 1 }) Give(enemy, choice.Options[0].Move);
                else if (choice == null) enemy.MoveUnknown = $"{enemy.Name}'s move that turn isn't known (past its rules' first random pick)";
            }
        });
        foreach (var choice in choices)
        {
            if (choice.Options.Count < 2) continue;
            branches.Each(state =>
            {
                if (state.Enemy(choice.EnemyId) is not { Alive: true }) return new[] { state };
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
        enemy.Intent = move.Intent?.ToList();
        enemy.Hits = new List<int>(new int[move.Intent?.Count ?? 0]);
        enemy.TeammateHits = new();
        enemy.Move = move.Actions;
        enemy.IntendsToAttack = move.Intent is { Count: > 0 };
        enemy.Stunned = false; // a stun takes one move: the forecast's moves come after it
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
    /// Each projected move's hits on you as they landed, where every branch that gave its enemy that move agrees (and it made all its hits); unknown
    /// otherwise, with why.
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
                string? unknown = null;
                var made = false;
                foreach (var branch in states)
                {
                    if (branch.Enemy(choice.EnemyId) is not { } enemy || enemy.ProjectedMove != move.Id) continue;
                    var landed = enemy.LandedOnYou ?? Array.Empty<int>();
                    if (landed.Length == 0 && !enemy.Alive && count > 0) continue; // dead before it attacked: no hit to show there
                    made = true;
                    if (enemy.LandedUnknown) { unknown = "what comes before its hits isn't followed"; break; }
                    if (landed.Length != count) { unknown = "not every hit lands in every outcome"; break; }
                    if (agreed == null) agreed = landed;
                    else if (!agreed.SequenceEqual(landed)) { unknown = "a roll decides its hits"; break; }
                }
                if (!made && count > 0) unknown ??= "it dies before it attacks";
                moves[move.Id] = unknown != null ? hits with { Unknown = unknown } : hits with { Lands = agreed ?? Array.Empty<int>() };
            }
        }
    }
}
