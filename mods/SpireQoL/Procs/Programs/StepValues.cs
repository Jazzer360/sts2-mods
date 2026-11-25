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

    /// <summary>The value is the count of your draw pile's cards (GetPile(Draw).Cards.Count).</summary>
    public static bool DrawPileCount(ProgramValue value) =>
        Unstored(value) is QueryValue { Name: "Count" } count && count.Parts().Any(part => part is QueryValue { Name: "GetPile", Arguments: [ConstantValue { Value: var pile }, ..] } && pile == (int)MegaCrit.Sts2.Core.Entities.Cards.PileType.Draw);
}
