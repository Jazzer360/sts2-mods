namespace SpireQoL.Procs.Programs;

/// <summary>
/// What a command step's arguments are, read from its values (decision #194): whom it acts on (you, your player, the card's target, every
/// enemy, a loop's item), which card value it names, what a counter loop goes up to, and the tests a command makes of itself. The step
/// translators (SimAdapter.Steps) and the program's readers (MadeCards) ask these of the code, never of a card's name.
/// </summary>
public static class StepValues
{
    /// <summary>The value as it was stored (a local kept a moment: its stored value).</summary>
    public static ProgramValue? Unstored(ProgramValue? value) => value is StoredValue stored ? Unstored(stored.Value) : value;

    /// <summary>Is the value the card's owner, or its creature (this.Owner, this.Owner.Creature)?</summary>
    public static bool IsOwner(ProgramValue? value) => IsYou(Unstored(value)) || IsOwnerPlayer(Unstored(value));

    /// <summary>The value sums an attack's results (DamageResult's TotalDamage and OverkillDamage).</summary>
    public static bool ReadsResults(ProgramValue value) => value.Parts().Any(part => part is QueryValue { Name: "TotalDamage" or "Results" });

    public static string? Argument(CommandStep command, int index) => index < command.Arguments.Count ? command.Arguments[index] : null;

    public static ProgramValue? ValueAt(CommandStep command, int index) => index < command.Values.Count ? Unstored(command.Values[index]) : null;

    /// <summary>The loop goes over the enemies you can hit.</summary>
    public static bool OverEnemies(LoopStep loop) => loop.Items?.Parts().Any(part => part is QueryValue { Name: "HittableEnemies" }) == true;

    /// <summary>The value is the item of the loop it's in (each, each.Player, each.Creature).</summary>
    public static bool IsEach(ProgramValue? value) => value != null && value.Parts().Any(part => part is NamedValue { Name: "each" });

    /// <summary>The value is the card's target, or its player, or that player's creature (cardPlay.Target, .Player, .Player.Creature: Constellation's).</summary>
    public static bool IsTarget(ProgramValue? value)
    {
        for (var depth = 0; depth < 3 && value != null; depth++)
        {
            value = Unstored(value);
            if (value is QueryValue { Name: "Target", Of: NamedValue { Name: "cardPlay" } }) return true;
            if (value is not QueryValue { Name: "Player" or "Creature" } inner) return false;
            value = inner.Of;
        }
        return false;
    }

    /// <summary>
    /// Every enemy you can hit (CombatState.HittableEnemies, or that when there's a fight: a value two ways leave, the other null; or the card
    /// owner's opponents that can be hit, ToList(Where(GetOpponentsOf(Owner.Creature), e => e.IsHittable)): Timber's).
    /// </summary>
    public static bool IsEveryEnemy(ProgramValue? value) => Unstored(value) switch
    {
        QueryValue { Name: "HittableEnemies" } => true,
        QueryValue { Name: "ToList", Arguments: [var listed] } => IsEveryEnemy(listed),
        QueryValue { Type: "Enumerable", Name: "Where", Arguments: [var filtered, LambdaValue hittable] } when ReadsOwn(hittable, "IsHittable") =>
            Unstored(filtered) is QueryValue { Name: "GetOpponentsOf", Arguments: [var whose] } && IsYou(Unstored(whose)),
        WaysValue ways => ways.Ways.Any(way => IsEveryEnemy(Unstored(way.Value))) && ways.Ways.All(way => IsEveryEnemy(Unstored(way.Value)) || way.Value is NullValue),
        _ => false,
    };

    /// <summary>A lambda that gives its parameter's property by this name (e => e.IsHittable).</summary>
    private static bool ReadsOwn(LambdaValue lambda, string property) =>
        lambda.Body.Terms is [[{ Holds: true, Test: QueryValue { Name: var name, Of: ParameterValue { Name: var given } } }]] && name == property && given == lambda.Parameter;

    public static bool IsYou(ProgramValue? value) => value is QueryValue { Name: "Creature", Of: QueryValue { Name: "Owner", Of: NamedValue { Name: "this" } } };

    public static bool IsOwnerPlayer(ProgramValue? value) => value is QueryValue { Name: "Owner", Of: NamedValue { Name: "this" } };

    /// <summary>The card value a step's argument names (Damage, OstyDamage, DynamicVars.CalculatedDamage), or null.</summary>
    public static string? ValueKey(ProgramValue? value) => (value == null ? null : Unstored(value)) switch
    {
        CardValue cardValue => cardValue.Key,
        QueryValue { Of: QueryValue { Name: "DynamicVars" } } property => property.Name,
        QueryValue { Name: "op_Implicit", Arguments: [var inner] } => ValueKey(inner),
        _ => null,
    };


    /// <summary>The card value a counter loop goes up to (i &lt; CalculatedChannels: Voltaic's), or null.</summary>
    public static string? LoopBoundKey(LoopStep loop) =>
        loop.While is { Terms: [[{ Holds: true, Test: CompareValue { Op: Comparison.Less or Comparison.LessOrEqual } bound }]] } && Unstored(bound.Right) is CardValue { Key: var key }
            ? key : null;

    /// <summary>The condition without its tests of your orbs (an evoke with none evokes nothing).</summary>
    public static ProgramCondition WithoutOrbTests(ProgramCondition condition)
    {
        if (condition.Terms.Count != 1) return condition;
        var kept = ProgramCondition.Always;
        foreach (var literal in condition.Terms[0])
            if (!literal.Test.Parts().Any(part => part is QueryValue { Name: "Orbs" or "OrbQueue" })) kept = kept.And(literal);
        return kept;
    }

    /// <summary>The condition without the tests <paramref name="dropped"/> picks, on each of its ways.</summary>
    public static ProgramCondition Without(ProgramCondition condition, Func<ConditionLiteral, bool> dropped)
    {
        if (!condition.Literals.Any(dropped)) return condition;
        var result = ProgramCondition.Never;
        foreach (var way in condition.Terms)
        {
            var kept = ProgramCondition.Always;
            foreach (var literal in way)
                if (!dropped(literal)) kept = kept.And(literal);
            result = result.Or(kept);
        }
        return result;
    }

    /// <summary>
    /// The test is that a roll the step acts on found something (Cinder's card from your hand, Bouncing Flask's enemy: x != null), which the
    /// step's action rolls itself as it runs: a branch per outcome, none with nothing to roll over (decision #311).
    /// </summary>
    public static bool TestsOwnRoll(ConditionLiteral literal, CommandStep step)
    {
        var (tested, found) = literal.Test switch
        {
            CompareValue { Op: Comparison.NotEqual, Right: NullValue } notNull => (notNull.Left, literal.Holds),
            CompareValue { Op: Comparison.Equal, Right: NullValue } isNull => (isNull.Left, !literal.Holds),
            var truthy => (truthy, literal.Holds),
        };
        var rolled = Unstored(tested)!;
        if (!found || !IsRoll(rolled is QueryValue { Name: "FirstOrDefault", Arguments: [var first] } ? Unstored(first) : rolled)) return false;
        var text = rolled.ToString();
        return step.Values.Any(value => value.Parts().Any(part => Unstored(part)!.ToString() == text));
    }

    /// <summary>
    /// The test is that a collection of cards the step takes from has any (Ki Chant's top cards: topCards.Count != 0), when the step is a take
    /// (<paramref name="take"/>; <paramref name="takenFrom"/>: its arguments and what the loops around it go over): its action takes the cards
    /// there are as it runs, none with none there (decision #311).
    /// </summary>
    public static bool TestsOwnTake(ConditionLiteral literal, IReadOnlyList<ProgramValue> takenFrom, bool take)
    {
        if (!take) return false;
        var (counted, any) = literal.Test switch
        {
            CompareValue { Op: Comparison.NotEqual or Comparison.Greater, Right: ConstantValue { Value: 0 } } above => (above.Left, literal.Holds),
            CompareValue { Op: Comparison.Equal or Comparison.LessOrEqual, Right: ConstantValue { Value: 0 } } none => (none.Left, !literal.Holds),
            var truthy => (truthy, literal.Holds),
        };
        var collection = Unstored(counted) switch
        {
            QueryValue { Name: "Count", IsProperty: true, Of: { } of } => of,
            QueryValue { Type: "Enumerable", Name: "Count" or "Any", Arguments: [var source] } => source,
            _ => null,
        };
        if (!any || collection == null) return false;
        var text = Unstored(collection)!.ToString();
        return takenFrom.Any(value => value.Parts().Any(part => Unstored(part)!.ToString() == text));
    }

    private static bool IsRoll(ProgramValue? value) => value is QueryValue { Name: "NextItem" or "TakeRandom" or "GetDistinctForCombat" or "StableShuffle" };

    /// <summary>The value is the count of your draw pile's cards (GetPile(Draw).Cards.Count).</summary>
    public static bool DrawPileCount(ProgramValue value) =>
        Unstored(value) is QueryValue { Name: "Count" } count && count.Parts().Any(part => part is QueryValue { Name: "GetPile", Arguments: [ConstantValue { Value: var pile }, ..] } && pile == (int)MegaCrit.Sts2.Core.Entities.Cards.PileType.Draw);
}
