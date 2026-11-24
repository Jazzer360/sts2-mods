namespace SpireQoL.Sim;

/// <summary>Conditions apart from effects (decision #190): a condition asked of each branch's own state when the action it guards runs.</summary>
public static partial class SimEngine
{
    /// <summary>Where a play's shot keeps whether it had an arrow (SimState.PlayTests: 1 it had, 2 it didn't): ShotHadArrowCondition.</summary>
    internal const string ShotKey = "shot";

    /// <summary>Why a branch's condition couldn't be told (it runs as if it held, and the play is unknown).</summary>
    internal const string HandMatchingUnknown = "a condition on the kind of cards in your hand, after cards joined it whose kind wasn't read";

    /// <summary>Why a count of the fight's log couldn't be told (decision #191): an event it may count had a card whose kind wasn't read.</summary>
    internal const string HistoryUnknown = "a count of the fight's log, after an event whose card's kind wasn't read";

    /// <summary>
    /// A WhenAction: its actions on the branches where its condition holds; the others go on untouched. A branch whose condition can't be
    /// told runs them, and is unknown (SimState.Unmodelled).
    /// </summary>
    private static void RunWhen(BranchSet branches, WhenAction whenAction)
    {
        var holding = new List<SimState>();
        var others = new List<SimState>();
        foreach (var branch in branches.States)
        {
            var holds = Holds(branch, whenAction.Condition);
            if (holds == null) branch.Unmodelled ??= HandMatchingUnknown;
            (holds != false ? holding : others).Add(branch);
        }
        if (holding.Count == 0 || whenAction.Then.Count == 0) return;
        if (others.Count == 0)
        {
            foreach (var action in whenAction.Then) Run(branches, action);
            return;
        }
        branches.States = holding;
        branches.Aside += others.Count;
        try
        {
            foreach (var action in whenAction.Then) Run(branches, action);
        }
        finally
        {
            // Stopped at a cap or not, the branches set aside are kept (as they are), after the ones the actions ran on.
            branches.Aside -= others.Count;
            branches.States.AddRange(others);
        }
    }

    /// <summary>An AskAction: the condition asked now on each branch, its tests' answers kept for the rest of the play.</summary>
    private static void RunAsk(BranchSet branches, AskAction askAction) =>
        branches.Do(branch => { if (Holds(branch, askAction.Condition) == null) branch.Unmodelled ??= HandMatchingUnknown; });

    /// <summary>
    /// Whether the condition holds on the state now: null when it can't be told. A test of the card's code (one with an At) is asked once a
    /// play: its first answer is kept (SimState.PlayTests) and given again.
    /// </summary>
    internal static bool? Holds(SimState state, SimCondition condition, int monster = Who.None)
    {
        if (condition.At is { } at && state.PlayTests.Has(at)) return state.PlayTests[at] switch { 1 => true, 2 => false, _ => null };
        var holds = HoldsNow(state, condition, monster);
        if (condition.At is { } asked) state.PlayTests = state.PlayTests.With(asked, holds switch { true => 1, false => 2, _ => 3 });
        return holds;
    }

    private static bool? HoldsNow(SimState state, SimCondition condition, int monster)
    {
        var owner = state.CurrentPlay?.Owner ?? Who.You;
        var target = state.CurrentPlay?.Target ?? Who.None;
        switch (condition)
        {
            case AttackKilledCondition:
                return state.AttackKilled;
            case OstyMissingCondition:
                return state.Player(owner) is not { OstyHp: > 0 };
            case TargetImpaledCondition impaled:
                if (state.Enemy(target) is null) return false;
                return impaled.ByOwner ? PowersOf(state, target).Has(Impaled) : IsImpaled(state, target);
            case ShotHadArrowCondition:
                return state.PlayTests.Has(ShotKey) && state.PlayTests[ShotKey] == 1;
            case TargetIntendsToAttackCondition:
                return state.Enemy(target) is { Alive: true, IntendsToAttack: true, Stunned: false };
            case KnownCondition known:
                return known.Holds;
            case CountCondition counted:
                return CountOf(state, counted, owner, monster) is { } count ? Compares(count, counted.Compare, counted.Than) : null;
            case NotCondition not:
                return Holds(state, not.Inner, monster) is { } inner ? !inner : null;
            case AllCondition all:
            {
                // Asked in order, as the code's && does: a part after one that fails isn't asked.
                foreach (var part in all.Parts)
                    switch (Holds(state, part, monster))
                    {
                        case false: return false;
                        case null: return null;
                    }
                return true;
            }
            case AnyCondition any:
            {
                foreach (var part in any.Parts)
                    switch (Holds(state, part, monster))
                    {
                        case true: return true;
                        case null: return null;
                    }
                return false;
            }
            default:
                throw new NotSupportedException($"No test for {condition.GetType().Name}");
        }
    }

    /// <summary>A counted condition's count on the state; null when it can't be told (a filter's cards after one joined unread).</summary>
    private static int? CountOf(SimState state, CountCondition counted, int owner, int monster)
    {
        switch (counted.Counted)
        {
            case SimCounted.HandCards:
                return state.Player(owner) is { } player ? Math.Max(0, player.HandCount - player.PlayingFromHand) : 0;
            case SimCounted.HandMatching:
                if (counted.Filter == null) return null;
                if (!state.TakeMatching.Has(counted.Filter)) return counted.Matching; // outside a card play: as the adapter counted it
                return state.TakeMatching.DataOf(counted.Filter) != 0 ? null : state.TakeMatching[counted.Filter];
            case SimCounted.PlaysFinishedThisTurn:
                return state.Player(owner)?.Turn[SimKeys.Plays(PlayStage.Finished)] ?? 0;
            case SimCounted.CardPlaysFinished:
                return counted.Before + (state.CurrentPlay?.PlayIndex ?? 0);
            case SimCounted.ArrowsAvailable:
                return state.Player(owner)?.Arrows ?? 0;
            case SimCounted.ExhaustPile:
                return state.Player(owner)?.ExhaustPile ?? 0;
            case SimCounted.TargetPower:
                if (counted.Power is not { } power || state.Enemy(state.CurrentPlay?.Target ?? Who.None) is not { Alive: true } aimedAt) return 0;
                return power == PowerId.Poison ? aimedAt.PoisonStacks : aimedAt.Powers[power];
            case SimCounted.OwnerPower:
                return counted.Power is { } owned ? PowerOn(state, owner, owned) : 0;
            case SimCounted.TargetHp:
            {
                var hpOf = state.CurrentPlay?.Target ?? Who.None;
                if (state.Enemy(hpOf) is { } enemy) return enemy.Alive ? Math.Max(0, enemy.Hp) : 0;
                return state.Player(hpOf) is { } aimedPlayer ? Math.Max(0, aimedPlayer.Hp) : 0;
            }
            case SimCounted.MonsterHp:
                return state.Enemy(monster) is { Alive: true } asking ? asking.Hp : 0;
            case SimCounted.MonsterPower:
                return counted.Power is { } its && state.Enemy(monster) is { Alive: true } holder ? (its == PowerId.Poison ? holder.PoisonStacks : holder.Powers[its]) : 0;
            case SimCounted.MonsterSideAlive:
                return state.Enemies.Count(enemy => enemy.Alive && !(counted.ExcludingItself && enemy.Id == monster));
            case SimCounted.History:
                if (counted.Query == null) return null;
                var logged = HistoryCount(state, owner, counted.Query);
                if (logged == null) state.Unmodelled ??= HistoryUnknown;
                return logged;
            default:
                throw new NotSupportedException($"No count for {counted.Counted}");
        }
    }

    private static bool Compares(int count, SimCompare compare, int than) => compare switch
    {
        SimCompare.Equal => count == than,
        SimCompare.NotEqual => count != than,
        SimCompare.Less => count < than,
        SimCompare.LessOrEqual => count <= than,
        SimCompare.Greater => count > than,
        _ => count >= than,
    };

    /// <summary>What a condition's filter's name starts with (CountCondition.Filter): a filtered take's has none.</summary>
    public const string ConditionFilterPrefix = "if:";

    /// <summary>The name is a condition's filter's (its count is exact or not known), not a take's.</summary>
    internal static bool IsConditionFilter(string name) => name.StartsWith(ConditionFilterPrefix, StringComparison.Ordinal);

    /// <summary>The filters of the counted conditions in a play's actions (their cards in the hand as the card play starts), WhenActions' included.</summary>
    internal static IEnumerable<CountCondition> HandFilters(IEnumerable<SimAction> actions)
    {
        foreach (var action in Flattened(actions))
        {
            var condition = action switch { WhenAction guarded => guarded.Condition, AskAction ask => ask.Condition, _ => null };
            if (condition != null)
                foreach (var counted in ConditionParts(condition).OfType<CountCondition>())
                    if (counted is { Counted: SimCounted.HandMatching, Filter: not null })
                        yield return counted;
        }
    }

    /// <summary>The actions and every action a WhenAction among them guards, in order.</summary>
    internal static IEnumerable<SimAction> Flattened(IEnumerable<SimAction> actions)
    {
        foreach (var action in actions)
        {
            yield return action;
            if (action is WhenAction whenAction)
                foreach (var inner in Flattened(whenAction.Then))
                    yield return inner;
        }
    }

    /// <summary>The condition and every condition inside it.</summary>
    internal static IEnumerable<SimCondition> ConditionParts(SimCondition condition)
    {
        yield return condition;
        var inner = condition switch
        {
            NotCondition not => new[] { not.Inner },
            AllCondition all => all.Parts,
            AnyCondition any => any.Parts,
            _ => Array.Empty<SimCondition>(),
        };
        foreach (var part in inner)
            foreach (var nested in ConditionParts(part))
                yield return nested;
    }
}
