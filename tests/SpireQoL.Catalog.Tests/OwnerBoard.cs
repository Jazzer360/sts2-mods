using System.Reflection;
using SpireQoL.Procs.Programs;

namespace SpireQoL.Catalog.Tests;

/// <summary>Which kinds of creature a power can be on: a player, an enemy, a player's pet (Osty).</summary>
[Flags]
internal enum OwnerKinds { None = 0, Player = 1, Enemy = 2, Pet = 4, Any = Player | Enemy | Pet }

/// <summary>
/// Which kinds of creature each power can be on, read from every place a branch's code applies it (PowerCmd.Apply&lt;T&gt;, or PowerCmd.Apply of
/// a power made from ModelDb.Power&lt;T&gt;()): the creatures its target argument names, by what the code says they are (a card's or a potion's
/// target by its TargetType, a monster's own creature, the moves' targets, the enemies, a power's owner as that power's own kinds, a command
/// helper's parameter as what its callers pass). A target it can't tell is every kind, and a power applied nowhere it reads is every kind: the
/// board only ever leaves out a kind the code never gives. Relics are a player's. Read once per branch; the parity reviews compare a read
/// model only on the owners it can have (ReadModelParityTests).
/// </summary>
internal sealed class OwnerBoard
{
    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>Code that's never in a player's fight: the developers' console, the autoplayer, the mocks.</summary>
    private static readonly string[] NotInAFight = { ".DevConsole", ".AutoSlay", ".Mocks", ".Mock" };

    /// <summary>A place the code applies a power: the class and method, the power (null: one the code doesn't name), its target, the loops around it.</summary>
    private sealed record Site(Type Owner, string Method, string? Power, ProgramValue Target, IReadOnlyList<LoopStep> Loops);

    /// <summary>A call to a static command helper whose parameter a site's target is (ArrowCmd.ImpaleTarget's target): its arguments at one caller.</summary>
    private sealed record HelperCall(Type Owner, string Method, IReadOnlyList<ProgramValue> Arguments, IReadOnlyList<LoopStep> Loops);

    private readonly BranchCode _code;
    private readonly List<Site> _sites = new();
    private readonly Dictionary<string, List<HelperCall>> _helperCalls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OwnerKinds> _powers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pets = new(StringComparer.Ordinal);

    /// <summary>The places that apply a power the code doesn't name (Misery's copies of an enemy's own powers), as "Class.Method".</summary>
    public IReadOnlyList<string> UnnamedPowers { get; }

    /// <summary>The kinds each power class of the branch can be on.</summary>
    public IReadOnlyDictionary<string, OwnerKinds> Powers => _powers;

    public OwnerBoard(BranchCode code)
    {
        _code = code;
        var types = code.Assembly.GetTypes().Where(type => !type.IsNested && type.Namespace?.StartsWith("MegaCrit.Sts2.Core", StringComparison.Ordinal) == true
            && !NotInAFight.Any(part => type.Namespace!.Contains(part, StringComparison.Ordinal))).ToList();
        var ops = types.ToDictionary(type => type, type => new[] { type }.Concat(Procs.CardCode.NestedTypes(type))
            .SelectMany(nested => nested.GetMethods(Declared).Cast<MethodBase>().Concat(nested.GetConstructors(Declared))).SelectMany(Read).ToList());
        foreach (var call in ops.Values.SelectMany(list => list).Where(instruction => instruction.IsCall && instruction.Member is { Name: "AddPet" }))
            foreach (var pet in call.Member!.GenericArguments) _pets.Add(pet);
        // Every place that applies a power, read from its method's program.
        foreach (var type in types.Where(type => type.Name != "PowerCmd" && ops[type].Any(instruction => instruction.IsCall && instruction.Member is { DeclaringType: "PowerCmd", Name: "Apply" })))
            foreach (var (method, command, loops) in Commands(type, command => command.Command.FullName == "PowerCmd.Apply"))
            {
                var site = new Site(type, method, PowerOf(command) ?? OnePowerMadeIn(type, method), TargetOf(command), loops);
                // A model's own helper given its target (Mad Science's ExecuteRider(rider, cardPlay.Target)): the reader reads it inside the
                // method that calls it, with what that one passes, so the call's site there says it.
                if (IsInlinedHelperParameter(type, method, site.Target, ops[type])) continue;
                _sites.Add(site);
            }
        // The command helpers whose parameter a site applies to, and their callers.
        var helpers = _sites.Where(site => IsHelperParameter(site, site.Target) is not null).Select(site => site.Owner.Name + "." + site.Method).ToHashSet(StringComparer.Ordinal);
        for (var round = 0; round < 3 && helpers.Count > 0; round++)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in types.Where(type => ops[type].Any(instruction => instruction.IsCall && instruction.Member is { } member && helpers.Contains(member.FullName))))
                foreach (var (method, command, loops) in Commands(type, command => helpers.Contains(command.Command.FullName)))
                {
                    var key = command.Command.FullName;
                    if (!_helperCalls.TryGetValue(key, out var calls)) _helperCalls[key] = calls = new List<HelperCall>();
                    calls.Add(new HelperCall(type, method, command.Values, loops));
                    // A helper called from another helper with its own parameter: that one's callers too.
                    if (type.Namespace == "MegaCrit.Sts2.Core.Commands" && command.Values.Any(value => Unstored(value) is ParameterValue or NamedValue { Name: not "this" })) found.Add(type.Name + "." + method);
                }
            found.ExceptWith(_helperCalls.Keys);
            helpers = found;
        }
        UnnamedPowers = _sites.Where(site => site.Power == null).Select(site => site.Owner.Name + "." + site.Method).Distinct().Order(StringComparer.Ordinal).ToList();
        // Each power's kinds, to a fixed point (a power applied to another power's owner is on what that one can be on).
        var powerClasses = types.Where(type => type.Namespace == "MegaCrit.Sts2.Core.Models.Powers" && !type.IsAbstract && Derives(type, "PowerModel")).Select(type => type.Name).ToList();
        // A power applied nowhere the board reads (a game update's new way, a power the game adds otherwise): every kind.
        foreach (var power in powerClasses) _powers[power] = _sites.Any(site => site.Power == power) ? OwnerKinds.None : OwnerKinds.Any;
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var site in _sites.Where(site => site.Power != null && _powers.ContainsKey(site.Power)))
            {
                var kinds = _powers[site.Power!] | KindsOf(site.Target, site.Owner, site.Method, site.Loops, depth: 0);
                if (kinds == _powers[site.Power!]) continue;
                _powers[site.Power!] = kinds;
                changed = true;
            }
        }
    }

    private List<CodeOp> Read(MethodBase method)
    {
        try { return _code.Read(method); }
        catch { return new List<CodeOp>(); }
    }

    /// <summary>The commands <paramref name="wanted"/> picks in every method of a class, with the loops around each.</summary>
    private IEnumerable<(string Method, CommandStep Command, IReadOnlyList<LoopStep> Loops)> Commands(Type type, Func<CommandStep, bool> wanted)
    {
        foreach (var name in type.GetMethods(Declared).Where(method => !method.Name.StartsWith('<')).Select(method => method.Name).Distinct(StringComparer.Ordinal))
        {
            ModelProgram program;
            try { program = ProgramReader.Read(type, _code.Read, name); }
            catch { continue; }
            foreach (var found in Walk(program.Steps, new List<LoopStep>()))
                if (wanted(found.Command)) yield return (name, found.Command, found.Loops);
        }
    }

    private static IEnumerable<(CommandStep Command, IReadOnlyList<LoopStep> Loops)> Walk(IEnumerable<ProgramStep> steps, List<LoopStep> loops)
    {
        foreach (var step in steps)
        {
            if (step is CommandStep command)
            {
                yield return (command, loops.ToList());
                foreach (var inner in Walk(command.Inner, loops)) yield return inner;
            }
            if (step is LoopStep loop)
            {
                loops.Add(loop);
                foreach (var inner in Walk(loop.Body, loops)) yield return inner;
                loops.RemoveAt(loops.Count - 1);
            }
        }
    }

    /// <summary>The power an Apply names: its type argument, or the class a power made from ModelDb.Power&lt;T&gt;() is (null when it doesn't say).</summary>
    private static string? PowerOf(CommandStep command)
    {
        if (command.Command.GenericArguments is [var power]) return power;
        var at = command.Command.ParameterTypes.ToList().IndexOf("PowerModel");
        if (at < 0 || at >= command.Values.Count) return null;
        // ModelDb.Power<T>().ToMutable(), or the target's own copy of it (target.GetPower<T>() ?? a new one: Magi Knight's Dampen).
        var named = command.Values[at].Parts().OfType<QueryValue>().Where(query => query is { Name: "Power" or "GetPower", GenericArguments: [_] }).Select(query => query.GenericArguments[0]).Distinct().ToList();
        return named.Count == 1 ? named[0] : null;
    }

    /// <summary>
    /// The one power class a method's code makes or gets (ModelDb.Power&lt;T&gt;(), creature.GetPower&lt;T&gt;()), for an Apply of a power kept in
    /// a local the reader doesn't follow (Magi Knight's dampenPower, the target's own or a new one); null when it names none or several.
    /// </summary>
    private string? OnePowerMadeIn(Type type, string method)
    {
        var body = type.GetMethods(Declared).FirstOrDefault(candidate => candidate.Name == method) is { } found ? ProgramReader.BodyOf(found) : null;
        if (body == null) return null;
        var named = Read(body).Where(instruction => instruction.IsCall && instruction.Member is { Name: "Power" or "GetPower", GenericArguments: [_] }).Select(instruction => instruction.Member!.GenericArguments[0]).Distinct().ToList();
        return named.Count == 1 ? named[0] : null;
    }

    private static ProgramValue TargetOf(CommandStep command)
    {
        var types = command.Command.ParameterTypes;
        for (var index = 0; index < types.Count && index < command.Values.Count; index++)
            if (types[index] is "Creature" or "IEnumerable`1") return command.Values[index];
        return NullValue.Instance;
    }

    private static ProgramValue Unstored(ProgramValue value) => value is StoredValue stored ? Unstored(stored.Value) : value;

    /// <summary>The parameter of a static command helper a value is (ArrowCmd.ImpaleTarget's target): its index; null for anything else.</summary>
    private static int? IsHelperParameter(Site site, ProgramValue value) => HelperParameter(site.Owner, site.Method, value);

    private static int? HelperParameter(Type owner, string method, ProgramValue value)
    {
        var name = Unstored(value) switch { ParameterValue parameterValue => parameterValue.Name, NamedValue named => named.Name, _ => null };
        if (owner.Namespace != "MegaCrit.Sts2.Core.Commands" || name is null or "this") return null;
        var found = owner.GetMethods(Declared).Where(candidate => candidate.Name == method && candidate.IsStatic)
            .Select(candidate => candidate.GetParameters().ToList().FindIndex(parameter => parameter.Name == name)).FirstOrDefault(index => index >= 0, -1);
        return found >= 0 ? found : null;
    }

    /// <summary>
    /// The target is a parameter of a model's own instance method that isn't a hook (not virtual) and that another of its methods calls: the
    /// program reader reads that method inside its caller, with the caller's argument.
    /// </summary>
    private static bool IsInlinedHelperParameter(Type owner, string method, ProgramValue target, List<CodeOp> ops)
    {
        var name = Unstored(target) switch { ParameterValue parameterValue => parameterValue.Name, NamedValue named => named.Name, _ => null };
        if (name is null or "this" || owner.Namespace == "MegaCrit.Sts2.Core.Commands") return false;
        var helper = owner.GetMethods(Declared).Where(candidate => candidate.Name == method && !candidate.IsStatic && !candidate.IsVirtual).ToList();
        return helper.Count > 0 && helper.Any(candidate => candidate.GetParameters().Any(parameter => parameter.Name == name))
            && ops.Any(instruction => instruction.IsCall && instruction.Member is { } member && member.DeclaringType == owner.Name && member.Name == method);
    }

    /// <summary>The kinds of creature a value the code names can be (every kind when it can't tell).</summary>
    private OwnerKinds KindsOf(ProgramValue value, Type owner, string method, IReadOnlyList<LoopStep> loops, int depth)
    {
        if (depth > 6) return OwnerKinds.Any;
        OwnerKinds Of(ProgramValue inner) => KindsOf(inner, owner, method, loops, depth + 1);
        switch (value)
        {
            case StoredValue stored: return Of(stored.Value);
            case NullValue: return OwnerKinds.None;
            case WaysValue ways: return ways.Ways.Aggregate(OwnerKinds.None, (kinds, way) => kinds | Of(way.Value));
            case ArrayValue array: return array.Items.Aggregate(OwnerKinds.None, (kinds, item) => kinds | Of(item));
        }
        // The item of a loop around the step (foreach (Creature each in ...)): what the loop goes over.
        if (value is NamedValue { Name: "each" } && loops.LastOrDefault(loop => loop.Items != null) is { Items: { } items }) return Of(items);
        // A command helper's parameter: what its callers pass there.
        if (HelperParameter(owner, method, value) is { } && _helperCalls.TryGetValue(owner.Name + "." + method, out var calls))
        {
            var name = Unstored(value) is ParameterValue parameterValue ? parameterValue.Name : ((NamedValue)Unstored(value)).Name;
            var kinds = OwnerKinds.None;
            foreach (var call in calls)
            {
                // The overload called (by its count of arguments), its argument by the parameter's name; one without it passes no creature there.
                var overload = owner.GetMethods(Declared).FirstOrDefault(candidate => candidate.Name == method && candidate.IsStatic && candidate.GetParameters().Length == call.Arguments.Count);
                var at = overload?.GetParameters().ToList().FindIndex(parameter => parameter.Name == name) ?? -1;
                kinds |= overload == null ? OwnerKinds.Any : at >= 0 ? KindsOf(call.Arguments[at], call.Owner, call.Method, call.Loops, depth + 1) : OwnerKinds.None;
            }
            return kinds;
        }
        if (IsOwnersCreature(value))
            return Derives(owner, "PowerModel") ? PowerKinds(owner)
                : Derives(owner, "CardModel") || Derives(owner, "RelicModel") || Derives(owner, "PotionModel") ? OwnerKinds.Player : OwnerKinds.Any;
        // A monster's own creature: an enemy, or a pet (a monster the code adds as a player's pet: Osty).
        if (value is QueryValue { Name: "Creature", Of: NamedValue { Name: "this" } } && Derives(owner, "MonsterModel"))
            return _pets.Contains(owner.Name) ? OwnerKinds.Pet : OwnerKinds.Enemy;
        // The creatures a monster's move targets (MonsterModel.PerformMove: the CombatState's PlayerCreatures).
        if (value is ParameterValue { Name: "targets" } or NamedValue { Name: "targets" } && Derives(owner, "MonsterModel")) return OwnerKinds.Player;
        // A power's own target as it's applied or lands (BeforeApplied's, AfterApplied's target): its owner.
        if (value is ParameterValue { Name: "target" } or NamedValue { Name: "target" } && Derives(owner, "PowerModel") && method is "BeforeApplied" or "AfterApplied")
            return PowerKinds(owner);
        // The enemies that entered the fight (AfterEnemiesEnteredCombat's creatures).
        if (value is ParameterValue { Name: "creatures" } or NamedValue { Name: "creatures" } && method == "AfterEnemiesEnteredCombat") return OwnerKinds.Enemy;
        // A card's target, a potion's target: by its TargetType.
        if (value is QueryValue { Name: "Target", Of: NamedValue { Name: "cardPlay" } or ParameterValue { Name: "cardPlay" } } && Derives(owner, "CardModel")) return ByTargetType(CardTargetType(owner));
        if (value is ParameterValue { Name: "target" } or NamedValue { Name: "target" } && Derives(owner, "PotionModel") && method == "OnUse") return ByTargetType(PotionTargetType(owner));
        // The enemies' and players' collections, and what's taken from them.
        switch (value)
        {
            case QueryValue { Name: "HittableEnemies" or "Enemies" }: return OwnerKinds.Enemy;
            case QueryValue { Name: "PlayerCreatures" }: return OwnerKinds.Player;
            case QueryValue { Name: "Allies" }: return OwnerKinds.Player | OwnerKinds.Pet;
            case QueryValue { Name: "Players" }: return OwnerKinds.Player;
            // A player's creature (player.Creature, p => p.Creature over the players).
            case QueryValue { Name: "Creature", Of: { } playerOf } when IsAPlayer(playerOf): return OwnerKinds.Player;
            case QueryValue { Name: "Select", Arguments: [var source, ..] } when Of(source) == OwnerKinds.Player: return OwnerKinds.Player;
            case QueryValue { Name: "GetOpponentsOf", Arguments: [var opponentsOf] }:
            {
                var side = Of(opponentsOf);
                return (side.HasFlag(OwnerKinds.Enemy) ? OwnerKinds.Player : OwnerKinds.None) | (side.HasFlag(OwnerKinds.Player) || side.HasFlag(OwnerKinds.Pet) ? OwnerKinds.Enemy : OwnerKinds.None);
            }
            case QueryValue { Name: "GetTeammatesOf", Arguments: [var teammatesOf] }:
            {
                var side = Of(teammatesOf);
                return (side.HasFlag(OwnerKinds.Enemy) ? OwnerKinds.Enemy : OwnerKinds.None) | (side.HasFlag(OwnerKinds.Player) || side.HasFlag(OwnerKinds.Pet) ? OwnerKinds.Player | OwnerKinds.Pet : OwnerKinds.None);
            }
            // A filter, a copy or a pick of a collection: what it's of.
            case QueryValue { Type: "Enumerable", Name: "Where" or "ToList" or "ToArray" or "First" or "FirstOrDefault" or "Last" or "LastOrDefault" or "OrderBy" or "Take" or "Skip" or "Distinct", Arguments: [var source, ..] }:
                return Of(source);
            case QueryValue { Name: "NextItem", Arguments: [var source, ..] }: return Of(source);
            // A monster the code adds to the fight: an enemy (CreatureCmd.Add<T>, CreatureCmd.Add(monster) on the enemies' side by default).
            case ResultValue { Command: var command } when command.StartsWith("CreatureCmd.Add", StringComparison.Ordinal): return OwnerKinds.Enemy;
            case ResultValue { Command: var command } when command.StartsWith("PlayerCmd.AddPet", StringComparison.Ordinal): return OwnerKinds.Pet;
            case QueryValue { Name: "CreateCreature" }: return OwnerKinds.Enemy;
        }
        return OwnerKinds.Any;
    }

    /// <summary>
    /// What a power class's own code runs on: its kinds, or for a base class of powers (TemporaryStrengthPower, whose BeforeApplied every
    /// temporary Strength power runs) every kind of the power classes deriving from it.
    /// </summary>
    private OwnerKinds PowerKinds(Type power) => _powers.TryGetValue(power.Name, out var kinds) ? kinds
        : _code.Assembly.GetTypes().Where(type => !type.IsAbstract && _powers.ContainsKey(type.Name) && Derives(type, power.Name)).Aggregate(OwnerKinds.None, (all, type) => all | _powers[type.Name]);

    /// <summary>The model's owner's creature: base.Owner, base.Owner.Creature, base.Owner.Player.Creature.</summary>
    private static bool IsOwnersCreature(ProgramValue value) => Unstored(value) switch
    {
        QueryValue { Name: "Owner", Of: NamedValue { Name: "this" } } => true,
        QueryValue { Name: "Creature" or "Player", Of: { } of } => IsOwnersCreature(of),
        _ => false,
    };

    /// <summary>A value that's a player (a player of the fight's: GetMe, a loop's player, p in a lambda over the players).</summary>
    private static bool IsAPlayer(ProgramValue value) => Unstored(value) is ParameterValue { Name: "p" or "player" } or NamedValue { Name: "player" or "p" }
        || Unstored(value) is QueryValue { ValueType: "Player" };

    private static OwnerKinds ByTargetType(string? targetType) => targetType switch
    {
        "Self" or "AnyPlayer" or "AnyAlly" => OwnerKinds.Player,
        "AllAllies" => OwnerKinds.Player | OwnerKinds.Pet,
        "AnyEnemy" or "AllEnemies" or "RandomEnemy" => OwnerKinds.Enemy,
        "Osty" => OwnerKinds.Pet,
        "None" or "TargetedNoCreature" => OwnerKinds.None,
        _ => OwnerKinds.Any,
    };

    /// <summary>A card's TargetType: the constant its constructor passes CardModel's (its fourth argument).</summary>
    private string? CardTargetType(Type card)
    {
        foreach (var constructor in card.GetConstructors(Declared))
        {
            var ops = Read(constructor);
            for (var index = 0; index < ops.Count; index++)
            {
                if (ops[index] is not { Member: { Name: ".ctor", DeclaringType: "CardModel" } called }) continue;
                var at = called.ParameterTypes.ToList().IndexOf("TargetType");
                if (at < 0 || CodeDecoder.Producer(ops, index, CodeDecoder.ArgumentDepth(called, at)) is not { } producer || ops[producer].Integer is not { } number) return null;
                return _code.EnumName("TargetType", number);
            }
        }
        return null;
    }

    /// <summary>A potion's TargetType: what its TargetType getter returns.</summary>
    private string? PotionTargetType(Type potion)
    {
        for (var type = potion; type != null; type = type.BaseType)
            if (type.GetMethods(Declared).FirstOrDefault(candidate => candidate.Name == "get_TargetType") is { } getter)
                return ProgramReader.ReadReturnedValue(type, getter, _code.Read) is ConstantValue constant ? _code.EnumName("TargetType", constant.Value) : null;
        return null;
    }

    private static bool Derives(Type type, string baseName)
    {
        for (var current = type.BaseType; current != null; current = current.BaseType)
            if (current.Name == baseName) return true;
        return false;
    }

    /// <summary>Each site whose target it can't tell, for review: "Class.Method: Power on target".</summary>
    public IEnumerable<string> Untold() => _sites.Where(site => site.Power != null && !IsOwnersCreature(site.Target) && KindsOf(site.Target, site.Owner, site.Method, site.Loops, depth: 0) == OwnerKinds.Any)
        .Select(site => $"{site.Owner.Name}.{site.Method}: {site.Power} on {site.Target}");
}
