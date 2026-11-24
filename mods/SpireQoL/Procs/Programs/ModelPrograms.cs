using System.Reflection;

namespace SpireQoL.Procs.Programs;

/// <summary>
/// The loaded game's programs of any model's methods (decision #195): a monster's moves and hooks, a power's or a relic's hooks. Each is
/// read once per class and method from the running game's code (ProgramReader over RuntimeCode), and cached: never read per call
/// (decision #31). Cards keep their own cache (CardPrograms), the same reader.
/// </summary>
public static class ModelPrograms
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type Owner, string Method), ModelProgram> Cache = new();

    /// <summary>The program of <paramref name="method"/> of <paramref name="owner"/> (empty when its code can't be read or it has none).</summary>
    public static ModelProgram Of(Type owner, string method) => Cache.GetOrAdd((owner, method), key =>
    {
        try { return ProgramReader.Read(key.Owner, RuntimeCode.Read, key.Method); }
        catch { return new ModelProgram(key.Owner.Name, Array.Empty<ProgramStep>()); }
    });
}

/// <summary>
/// What a monster class's code runs (decision #195), found from its code by shape, so a new monster or a new move is found with no list:
/// its moves (the methods its MoveStates perform: a Task taking the move's targets) and its hooks (the game's virtual methods it overrides).
/// Works on any branch's types (the loaded game's, or an assembly file's in the tests).
/// </summary>
public static class MonsterCode
{
    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>The virtual methods a monster overrides that aren't hooks: how it looks and its move rules' construction (read apart).</summary>
    private static readonly HashSet<string> NotHooks = new(StringComparer.Ordinal)
        { "GenerateAnimator", "GenerateMoveStateMachine", "GenerateBestiaryMoveList", "ShouldShowMoveInBestiary", "SetupSkins", "BeforeAnimPlayedInCompendium", "ToString", "Equals", "GetHashCode" };

    /// <summary>The game's monster classes among <paramref name="types"/> (concrete or not: a base class's moves are its subclasses').</summary>
    public static IEnumerable<Type> Monsters(IEnumerable<Type> types) =>
        types.Where(type => type.Namespace == "MegaCrit.Sts2.Core.Models.Monsters" && !type.IsNested && Derives(type, "MonsterModel"));

    /// <summary>
    /// A move's perform method (MoveState's onPerform: <c>Task Move(IReadOnlyList&lt;Creature&gt; targets)</c>) declared on the class.
    /// </summary>
    public static IEnumerable<MethodInfo> Moves(Type monster) => monster.GetMethods(Declared).Where(IsMove);

    /// <summary>A method shaped as a move: it returns a Task and takes the move's targets.</summary>
    public static bool IsMove(MethodInfo method) =>
        method.ReturnType.Name == "Task" && method.GetParameters() is [{ ParameterType.Name: var list }] && list.StartsWith("IReadOnlyList", StringComparison.Ordinal);

    /// <summary>The game's virtual methods the class overrides (its hooks: AfterAddedToRoom, AfterDeath...), not its looks or its move rules.</summary>
    public static IEnumerable<MethodInfo> Hooks(Type monster) => monster.GetMethods(Declared)
        .Where(method => method.IsVirtual && !method.Attributes.HasFlag(MethodAttributes.NewSlot) && !method.IsSpecialName && !NotHooks.Contains(method.Name));

    private static bool Derives(Type type, string baseName)
    {
        for (var current = type.BaseType; current != null; current = current.BaseType)
            if (current.Name == baseName) return true;
        return false;
    }
}
