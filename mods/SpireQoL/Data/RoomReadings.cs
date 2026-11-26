using System.Collections.Concurrent;
using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Procs.Programs;

namespace SpireQoL.Data;

/// <summary>
/// What a relic's or a card's own hooks do on a kind of room of the map (decision #374), read by the program reader from its code: the rows of the
/// map legend it makes unusually good or bad (Map Reminders), those where it works against you, and what ends that.
/// </summary>
/// <param name="Rooms">The legend's rows it shows on.</param>
/// <param name="Crossed">Of those, the rows where it works against you (a purchase ends Maw Bank's gold per room).</param>
/// <param name="Until">Fields of its own that a choice there sets and that end what it does (Maw Bank's HasItemBeenBought): it shows until one is set.</param>
/// <param name="WhileChanging">Its ? row comes only from what a ? can be (ModifyUnknownMapPointRoomTypes, ModifyNextEvent): it shows while its hook
/// changes that (Golden Compass's act, Lantern Key's act 3), not when it leaves a ? as it is.</param>
/// <param name="Hooks">Every hook read (the coverage check: a hook the room scan finds is one of these).</param>
/// <param name="Notes">Each read hook and what it came to (for the snapshot and the review).</param>
public sealed record RoomReading(string Model, PathRoom Rooms, PathRoom Crossed, IReadOnlyList<string> Until, bool WhileChanging, IReadOnlyList<string> Hooks,
    IReadOnlyList<string> Notes);

/// <summary>
/// Reads where a relic's or a card's hooks act on the map (decision #374), the way the choice rules are read (decision #359): each hook it overrides
/// that's about one kind of room (a rest site's options and heal, a shop's prices and stock, what a ? can be, a chest), and each other hook whose code
/// tests the room it's told about or is in (`room.RoomType == RoomType.Elite`, `room is MerchantRoom`, the map point's `PointType`), is read by the
/// program reader: the rooms its acting steps (or a returned value other than the base hook's) run in are where it acts. What a hook does in every
/// room, every fight or only the boss's (where every path ends) changes no route, so it shows on no row; nor does what the map itself shows (its
/// marks, free travel). An enchantment its code gives a card (Pael's Growth's Clone) brings the enchantment's rooms. A card's play is a choice in a
/// fight, so only a card's other hooks give it a row (Dowsing's ? rooms). Works on any branch's code (the code reader it's given).
/// </summary>
public static class RoomReadings
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>Where a hook's effect happens: the room you're in (its type) or the map point (a ? point), as flags.</summary>
    [Flags]
    private enum Where
    {
        None = 0,
        Monster = 1,
        Elite = 2,
        Boss = 4,
        Treasure = 8,
        Shop = 16,
        Event = 32,
        RestSite = 64,
        /// <summary>A ? point of the map (whatever room it turns out to be).</summary>
        Unknown = 128,
        /// <summary>The map itself, an ancient's room: no row of the legend.</summary>
        Other = 256,
        Everywhere = 511,
        Combat = Monster | Elite | Boss,
    }

    /// <summary>
    /// The hooks about one kind of room (the game runs them only there), and the choice they're told about there (decision #359's setting table:
    /// a purchase, a rest-site heal) when there's one: a relic whose reading crosses that choice is crossed on that row.
    /// </summary>
    private static readonly Dictionary<string, (Where Room, ChoiceEffect Choice)> RoomHooks = new()
    {
        ["TryModifyRestSiteOptions"] = (Where.RestSite, ChoiceEffect.None),
        ["ShouldDisableRemainingRestSiteOptions"] = (Where.RestSite, ChoiceEffect.None),
        ["ModifyRestSiteHealAmount"] = (Where.RestSite, ChoiceEffect.RestHeal),
        ["TryModifyRestSiteHealRewards"] = (Where.RestSite, ChoiceEffect.RestHeal),
        ["AfterRestSiteHeal"] = (Where.RestSite, ChoiceEffect.RestHeal),
        ["AfterRestSiteSmith"] = (Where.RestSite, ChoiceEffect.None),
        ["ModifyMerchantPrice"] = (Where.Shop, ChoiceEffect.None),
        ["ShouldRefillMerchantEntry"] = (Where.Shop, ChoiceEffect.None),
        ["ModifyMerchantCardPool"] = (Where.Shop, ChoiceEffect.None),
        ["ModifyMerchantCardRarity"] = (Where.Shop, ChoiceEffect.None),
        ["ShouldAllowMerchantCardRemoval"] = (Where.Shop, ChoiceEffect.None),
        ["ModifyMerchantCardCreationResults"] = (Where.Shop, ChoiceEffect.None),
        ["AfterItemPurchased"] = (Where.Shop, ChoiceEffect.Purchase),
        ["ModifyUnknownMapPointRoomTypes"] = (Where.Unknown, ChoiceEffect.None),
        ["ModifyNextEvent"] = (Where.Unknown, ChoiceEffect.None),
        ["ModifyOddsIncreaseForUnrolledRoomType"] = (Where.Unknown, ChoiceEffect.None),
        ["ShouldGenerateTreasure"] = (Where.Treasure, ChoiceEffect.None),
    };

    /// <summary>The hooks that change what a ? can be: a ? row from these alone shows while the hook changes it.</summary>
    private static readonly HashSet<string> WhatAQuestionMarkIs = new() { "ModifyUnknownMapPointRoomTypes", "ModifyNextEvent", "ModifyOddsIncreaseForUnrolledRoomType" };

    /// <summary>The hooks that change the map: the map shows what they do (its marks, the paths you can take).</summary>
    private static readonly HashSet<string> MapShown = new() { "ModifyGeneratedMap", "ModifyGeneratedMapLate", "ShouldAllowFreeTravel" };

    /// <summary>Every hook that's about the map or a kind of room by itself (the room scan, CoverageWatch.RoomTestingHooks: overriding one is a room test).</summary>
    public static IReadOnlySet<string> MapHookNames { get; } = RoomHooks.Keys.Concat(MapShown).ToHashSet();

    // ------------------------------------------------------------------ the loaded game's

    private static readonly ConcurrentDictionary<string, RoomReading?> Loaded = new();

    private static readonly Lazy<Dictionary<string, Type>> LoadedTypes = new(() =>
        ModelTypes(typeof(RelicModel).Assembly).GroupBy(type => type.Name).ToDictionary(group => group.Key, group => group.First()));

    /// <summary>The game's relic and card classes on an assembly (the models a map row can come from: your relics, the cards in your deck).</summary>
    public static IEnumerable<Type> ModelTypes(Assembly game) => RelicReadings.RelicTypes(game).Concat(CardTypes(game));

    private static IEnumerable<Type> CardTypes(Assembly game)
    {
        Type[] types;
        try { types = game.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.OfType<Type>().ToArray(); }
        return types.Where(type => !type.IsAbstract && !type.IsNested && Derives(type, "CardModel") && type.Namespace?.EndsWith(".Mocks", StringComparison.Ordinal) != true);
    }

    /// <summary>A relic's or card's reading on the loaded game (null: no such model), kept once its relic reading is (the crossed rows come from it).</summary>
    public static RoomReading? For(string model)
    {
        if (Loaded.TryGetValue(model, out var known)) return known;
        if (!LoadedTypes.Value.TryGetValue(model, out var type)) return null;
        var reading = Read(type, RuntimeCode.Read, RelicReadings.For);
        if (!Derives(type, "RelicModel") || RelicReadings.Kept(model)) Loaded.TryAdd(model, reading);
        return reading;
    }

    /// <summary>Every card of the loaded game whose hooks give it a map row (a quest card in your deck: Dowsing).</summary>
    public static IReadOnlyList<string> LoadedCardsWithRooms => CardTypes(typeof(CardModel).Assembly).Select(type => type.Name).Distinct()
        .Where(name => For(name) is { Rooms: not PathRoom.None }).Order(StringComparer.Ordinal).ToList();

    /// <summary>Every relic and card of the loaded game read (at load, on the reminders' worker, after the relics' readings: decision #352). Returns how many.</summary>
    public static int ReadAll()
    {
        var count = 0;
        foreach (var model in LoadedTypes.Value.Keys)
        {
            try
            {
                For(model);
                count++;
            }
            catch { /* read again when it's asked for */ }
        }
        return count;
    }

    // ------------------------------------------------------------------ reading

    private sealed class Sink
    {
        public PathRoom Rooms, Crossed;
        public bool OtherUnknown;
        public readonly List<string> Hooks = new(), Notes = new(), Until = new();
    }

    /// <summary>
    /// A relic's or card's reading from its own code: <paramref name="read"/> gives a method's instructions, <paramref name="relicReading"/> a relic's
    /// choice reading by name (its crossed choices and what ends them: decision #359).
    /// </summary>
    public static RoomReading Read(Type model, Func<MethodBase, List<CodeOp>> read, Func<string, RelicReading?> relicReading)
    {
        var sink = new Sink();
        var choices = Derives(model, "RelicModel") ? relicReading(model.Name) : null;
        var isCard = Derives(model, "CardModel");
        foreach (var method in model.GetMethods(All).Where(method => method.IsVirtual && !method.IsSpecialName && (method.Attributes & MethodAttributes.NewSlot) == 0)
                     .OrderBy(method => method.Name, StringComparer.Ordinal))
        {
            try { ReadHook(model, method, read, choices, isCard, sink); }
            catch (Exception ex) { sink.Notes.Add($"{method.Name}: ? not read ({ex.GetType().Name})"); }
        }
        var whileChanging = (sink.Rooms & PathRoom.Unknown) != 0 && !sink.OtherUnknown;
        return new RoomReading(model.Name, sink.Rooms, sink.Crossed & sink.Rooms, sink.Until.Distinct().ToList(), whileChanging, sink.Hooks, sink.Notes);
    }

    private static void ReadHook(Type model, MethodInfo method, Func<MethodBase, List<CodeOp>> read, RelicReading? choices, bool isCard, Sink sink)
    {
        var name = method.Name;
        if (MapShown.Contains(name))
        {
            sink.Hooks.Add(name);
            sink.Notes.Add($"{name}: {(Acting(model, method, read) == Where.None ? "nothing" : "the map shows it")}");
            return;
        }
        if (RoomHooks.TryGetValue(name, out var hook))
        {
            sink.Hooks.Add(name);
            if (Acting(model, method, read) == Where.None)
            {
                sink.Notes.Add($"{name}: nothing");
                return;
            }
            // The eggs' and Fresnel Lens' shop cards: changed as every card you get is (their card rule), not a shop's in particular.
            if (name == "ModifyMerchantCardCreationResults" && choices?.Card is { Kind: AffinityKind.Modifies })
            {
                sink.Notes.Add($"{name}: shop cards changed like every card you get");
                return;
            }
            var row = RowsOf(hook.Room);
            sink.Rooms |= row;
            if (hook.Room == Where.Unknown && !WhatAQuestionMarkIs.Contains(name)) sink.OtherUnknown = true;
            var crossed = hook.Choice != ChoiceEffect.None && choices != null && (choices.CrossedOn & hook.Choice) != 0;
            if (crossed)
            {
                sink.Crossed |= row;
                sink.Until.AddRange(choices!.Ends.Where(end => (end.Effect & hook.Choice) != 0).Select(end => end.Field));
            }
            sink.Notes.Add($"{name}: {row}{(crossed ? " crossed" : "")}");
            return;
        }
        var (testsRooms, enchants) = Tests(model, method, read);
        var enchanted = enchants ? EnchantmentRooms(model, method, read) : (Rooms: PathRoom.None, Note: "");
        if (!testsRooms && enchanted.Rooms == PathRoom.None) return;
        sink.Hooks.Add(name);
        // A card's play is a choice in a fight, not a room you pick (The Hunt's reward from a kill).
        if (isCard && name == "OnPlay")
        {
            sink.Notes.Add($"{name}: its play (in a fight)");
            return;
        }
        var where = testsRooms ? Acting(model, method, read) : Where.None;
        var (rows, what) = Classify(where);
        if (testsRooms && where == Where.None && UnreadOwnCall(model, method, read) is { } unread)
        {
            // What it runs isn't read (main's Lord's Parasol: TaskHelper.RunSafely(PurchaseEverything(...))): a gap, shown on the rooms its code tests.
            rows = RowsOf(ClassesTested(model, method, read));
            what = $"? runs {unread} through a call the program doesn't follow ({rows})";
        }
        if (enchanted.Rooms != PathRoom.None)
        {
            rows |= enchanted.Rooms;
            what = (testsRooms ? what + "; " : "") + enchanted.Note;
        }
        if ((rows & PathRoom.Unknown) != 0) sink.OtherUnknown = true;
        sink.Rooms |= rows;
        sink.Notes.Add($"{name}: {what}");
    }

    /// <summary>What a hook's rooms come to: a row of the legend, or why none (every room, every fight, the boss's, nothing).</summary>
    private static (PathRoom Rows, string What) Classify(Where where)
    {
        if (where == Where.None) return (PathRoom.None, "nothing");
        if (where == Where.Everywhere) return (PathRoom.None, "every room");
        if ((where & ~Where.Combat) == Where.None && (where & (Where.Monster | Where.Elite)) == (Where.Monster | Where.Elite)) return (PathRoom.None, "every fight");
        if (where == Where.Boss) return (PathRoom.None, "the boss (every path ends there)");
        var rows = RowsOf(where);
        return (rows, rows == PathRoom.None ? "no row (" + where + ")" : rows.ToString());
    }

    private static PathRoom RowsOf(Where where)
    {
        var rows = PathRoom.None;
        if ((where & Where.Monster) != 0) rows |= PathRoom.Monster;
        if ((where & Where.Elite) != 0) rows |= PathRoom.Elite;
        if ((where & Where.Treasure) != 0) rows |= PathRoom.Treasure;
        if ((where & Where.Shop) != 0) rows |= PathRoom.Shop;
        if ((where & (Where.Event | Where.Unknown)) != 0) rows |= PathRoom.Unknown;
        if ((where & Where.RestSite) != 0) rows |= PathRoom.RestSite;
        return rows;
    }

    /// <summary>
    /// The rooms the hook's effect happens in: each step that acts (RelicReadings.Acts) on the rooms its condition allows, and for a hook that
    /// returns a value, each way that returns other than the base hook does (a Modify hook's amount, a Should hook's true, a TryModify hook's false).
    /// </summary>
    private static Where Acting(Type model, MethodInfo method, Func<MethodBase, List<CodeOp>> read)
    {
        var where = Where.None;
        var program = ProgramReader.ReadMethod(model, method, read);
        foreach (var (step, condition) in RelicReadings.Flattened(program.Steps, ProgramCondition.Always))
            if (RelicReadings.Acts(step)) where |= RoomsOf(condition, model, read);
        if (IsValued(method))
        {
            var identity = BaseValue(method, read);
            foreach (var (when, value) in ProgramReader.ReadReturnedWays(model, method, read).Ways)
                if (!IsIdentity(value, identity, method)) where |= RoomsOf(when, model, read);
        }
        return where;
    }

    private static bool IsValued(MethodInfo method) => method.ReturnType.FullName != typeof(void).FullName && !method.ReturnType.Name.StartsWith("Task", StringComparison.Ordinal);

    /// <summary>What the base hook (AbstractModel's) returns: a parameter by its place, or a constant; null when it's neither.</summary>
    private static (int Parameter, ProgramValue? Value)? BaseValue(MethodInfo method, Func<MethodBase, List<CodeOp>> read)
    {
        if (BaseOf(method) is not { DeclaringType: { } owner } baseMethod) return null;
        var ways = ProgramReader.ReadReturnedWays(owner, baseMethod, read).Ways;
        if (ways.Count != 1) return null;
        var value = ways[0].Value;
        if (value is ParameterValue or NamedValue)
        {
            var name = value is ParameterValue given ? given.Name : ((NamedValue)value).Name;
            var index = Array.FindIndex(baseMethod.GetParameters(), parameter => parameter.Name == name);
            if (index >= 0) return (index, null);
        }
        return (-1, value);
    }

    /// <summary>
    /// The hook as its first base class declares it (AbstractModel's virtual), found by name and parameters up the base classes (a branch's code read
    /// outside the game can't be asked for its base definition).
    /// </summary>
    private static MethodInfo? BaseOf(MethodInfo method)
    {
        var parameters = method.GetParameters().Select(parameter => parameter.ParameterType.FullName).ToList();
        MethodInfo? found = null;
        for (var type = method.DeclaringType?.BaseType; type != null; type = type.BaseType)
            if (type.GetMethods(All).FirstOrDefault(candidate => candidate.Name == method.Name && candidate.IsVirtual
                    && candidate.GetParameters().Select(parameter => parameter.ParameterType.FullName).SequenceEqual(parameters)) is { } declared)
                found = declared;
        return found;
    }

    private static bool IsIdentity(ProgramValue value, (int Parameter, ProgramValue? Value)? identity, MethodInfo method)
    {
        if (identity is not { } known) return false;
        if (known.Parameter >= 0)
        {
            var name = method.GetParameters()[known.Parameter].Name;
            return value is ParameterValue given && given.Name == name || value is NamedValue named && named.Name == name;
        }
        return Equals(value, known.Value) || known.Value != null && (IsZero(value) && IsZero(known.Value) || IsOne(value) && IsOne(known.Value));
    }

    private static bool IsZero(ProgramValue value) => value is NamedValue { Name: "Decimal.Zero" } or ConstantValue { Value: 0 };

    private static bool IsOne(ProgramValue value) => value is NamedValue { Name: "Decimal.One" } or ConstantValue { Value: 1 };

    // ------------------------------------------------------------------ the rooms a condition allows

    /// <summary>The rooms a condition can hold in: each way's room tests together (you're in one room at a time), the ways' rooms together.</summary>
    private static Where RoomsOf(ProgramCondition condition, Type model, Func<MethodBase, List<CodeOp>> read) =>
        Bounds(condition, Array.Empty<ConditionLiteral>(), model, read).May;

    /// <summary>
    /// The rooms a condition may hold in and the rooms it surely holds in: a test that isn't of the room may hold anywhere and surely holds nowhere,
    /// so a negated part (`!(room == null || room.RoomType != Elite)`) takes the rooms its inside surely doesn't hold in. A test the way it's part of
    /// already holds (<paramref name="holding"/>: the code's `player == Owner` again inside a negated part) holds there, and its opposite doesn't.
    /// </summary>
    private static (Where May, Where Must) Bounds(ProgramCondition condition, IReadOnlyList<ConditionLiteral> holding, Type model, Func<MethodBase, List<CodeOp>> read)
    {
        var (may, must) = (Where.None, Where.None);
        foreach (var term in condition.Terms)
        {
            var (wayMay, wayMust) = (Where.Everywhere, Where.Everywhere);
            var inWay = holding.Concat(term).ToList();
            foreach (var literal in term)
            {
                var (literalMay, literalMust) = (Where.Everywhere, Where.Everywhere);
                if (holding.Any(other => Same(other, literal.Negate()))) (literalMay, literalMust) = (Where.None, Where.None);
                else if (!holding.Any(other => Same(other, literal)))
                {
                    // The test's own rooms, then the literal's: where it holds, or where it doesn't.
                    var (testMay, testMust) = literal.Test is ConditionValue inner && inner.Condition.Literals.Any(part => Tested(part.Test, model, read) != null)
                        ? Bounds(inner.Condition, inWay.Where(other => !Same(other, literal)).ToList(), model, read)
                        : Tested(literal.Test, model, read) ?? (Where.Everywhere, Where.None);
                    (literalMay, literalMust) = literal.Holds ? (testMay, testMust) : (Where.Everywhere & ~testMust, Where.Everywhere & ~testMay);
                }
                wayMay &= literalMay;
                wayMust &= literalMust;
            }
            may |= wayMay;
            must |= wayMust;
        }
        return (may, must);
    }

    /// <summary>The same test, held the same way: the same place in the code, or the same value (a test the code makes again on another way through it).</summary>
    private static bool Same(ConditionLiteral first, ConditionLiteral second) =>
        first.Holds == second.Holds && (first.At == second.At || Equals(first.Test, second.Test) || first.Test.ToString() == second.Test.ToString());

    /// <summary>The rooms a test of the room may and surely holds in (null: it doesn't test the room).</summary>
    private static (Where May, Where Must)? Tested(ProgramValue test, Type model, Func<MethodBase, List<CodeOp>> read)
    {
        switch (test)
        {
            case QueryValue { Name: "is", Type: var roomClass } when RoomClass(model.Assembly, roomClass, read) is { } ofClass:
                return (ofClass, ofClass);
            case QueryValue { Name: "IsCombatRoom" }:
                return (Where.Combat, Where.Combat);
            case ConditionValue inner when inner.Condition.Literals.Any(literal => Tested(literal.Test, model, read) != null):
                return Bounds(inner.Condition, Array.Empty<ConditionLiteral>(), model, read);
            case CompareValue { Op: Comparison.Equal or Comparison.NotEqual } compare:
                var (query, constant) = compare.Right is ConstantValue right ? (compare.Left, right) : compare.Left is ConstantValue left ? (compare.Right, left) : (null, null);
                if (query is not QueryValue { Name: "RoomType" or "PointType" } kind || constant == null) return null;
                var enumType = kind.Name == "RoomType" ? nameof(MegaCrit.Sts2.Core.Rooms.RoomType) : nameof(MegaCrit.Sts2.Core.Map.MapPointType);
                var rooms = WhereOf(EnumNameOn(model.Assembly, enumType, constant.Value));
                var holds = compare.Op == Comparison.Equal ? rooms : Where.Everywhere & ~rooms;
                return (holds, holds);
            default:
                return null;
        }
    }

    /// <summary>A room type's or a map point type's member as rooms (they share their names: Monster, Shop, RestSite...; a point's Unknown is a ?).</summary>
    private static Where WhereOf(string? member) =>
        Enum.TryParse<Where>(member, out var where) && where is not (Where.None or Where.Everywhere or Where.Combat) ? where : Where.Other;

    private static readonly ConcurrentDictionary<(Assembly, string), Where?> RoomClasses = new();

    /// <summary>
    /// A room class's rooms (`room is RestSiteRoom`), read from its RoomType: a constant (RestSiteRoom's RestSite), or a fight's (CombatRoom's is
    /// its encounter's: a monster, an elite or a boss). Null for a class that isn't a room.
    /// </summary>
    private static Where? RoomClass(Assembly game, string name, Func<MethodBase, List<CodeOp>> read) => RoomClasses.GetOrAdd((game, name), key =>
    {
        var type = TypesOf(key.Item1).FirstOrDefault(candidate => candidate.Name == key.Item2 && Derives(candidate, "AbstractRoom"));
        if (type?.GetProperty("RoomType", BindingFlags.Instance | BindingFlags.Public)?.GetMethod is not { } getter) return null;
        var value = ProgramReader.ReadReturnedValue(type, getter, read);
        if (value is ConstantValue constant) return WhereOf(EnumNameOn(type.Assembly, nameof(MegaCrit.Sts2.Core.Rooms.RoomType), constant.Value));
        return value?.Parts().Any(part => part is QueryValue { Name: "Encounter" }) == true ? Where.Combat : Where.Everywhere;
    });

    private static Type[] TypesOf(Assembly game)
    {
        try { return game.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.OfType<Type>().ToArray(); }
    }

    /// <summary>An enum member's name on the branch's code (its numbers can differ between branches).</summary>
    private static string? EnumNameOn(Assembly game, string enumType, long value)
    {
        var type = TypesOf(game).FirstOrDefault(candidate => candidate.IsEnum && candidate.Name == enumType);
        return type?.GetFields(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(field => field.IsLiteral && Convert.ToInt64(field.GetRawConstantValue(), System.Globalization.CultureInfo.InvariantCulture) == value)?.Name;
    }

    // ------------------------------------------------------------------ which hooks are read

    /// <summary>
    /// Whether a hook's code (its body, its async body and its lambdas: the compiler's methods named after it) tests a room's type, a room's class
    /// or the map point's type, or gives a card an enchantment (whose rooms it brings).
    /// </summary>
    private static (bool Rooms, bool Enchants) Tests(Type model, MethodInfo method, Func<MethodBase, List<CodeOp>> read)
    {
        var (rooms, enchants) = (false, false);
        foreach (var body in BodiesOf(model, method))
        {
            List<CodeOp> instructions;
            try { instructions = read(body); }
            catch { continue; }
            rooms |= instructions.Any(instruction => instruction.Member is { } member && (member.Name is "get_RoomType" or "get_PointType" or "IsCombatRoom"
                || member.Kind == CodeMemberKind.Type && member.Name.EndsWith("Room", StringComparison.Ordinal) && instruction.OpCode == System.Reflection.Emit.OpCodes.Isinst));
            enchants |= instructions.Any(instruction => instruction.Member is { DeclaringType: "CardCmd", Name: "Enchant" });
        }
        return (rooms, enchants);
    }

    /// <summary>A method of the model's own its code calls (not a property) when the program read no step of it: what the program didn't follow.</summary>
    private static string? UnreadOwnCall(Type model, MethodInfo method, Func<MethodBase, List<CodeOp>> read) => BodiesOf(model, method)
        .SelectMany(body => { try { return read(body); } catch { return new List<CodeOp>(); } })
        .Select(instruction => instruction.IsCall ? instruction.Member : null)
        .FirstOrDefault(member => member is { Kind: CodeMemberKind.Method } && member.DeclaringType == model.Name && !member.Name.StartsWith("get_", StringComparison.Ordinal)
                                  && member.Name != method.Name)?.Name;

    /// <summary>The rooms of the room classes a hook's code tests (`room is MerchantRoom`).</summary>
    private static Where ClassesTested(Type model, MethodInfo method, Func<MethodBase, List<CodeOp>> read)
    {
        var where = Where.None;
        foreach (var body in BodiesOf(model, method))
        {
            List<CodeOp> instructions;
            try { instructions = read(body); }
            catch { continue; }
            foreach (var instruction in instructions)
                if (instruction.OpCode == System.Reflection.Emit.OpCodes.Isinst && instruction.Member is { Kind: CodeMemberKind.Type } tested
                    && RoomClass(model.Assembly, tested.Name, read) is { } rooms) where |= rooms;
        }
        return where;
    }

    /// <summary>A method and the compiler's methods made from it (its async state machine's MoveNext, its lambdas: named "&lt;Method&gt;...").</summary>
    private static IEnumerable<MethodBase> BodiesOf(Type model, MethodInfo method)
    {
        yield return method;
        var mark = "<" + method.Name + ">";
        foreach (var nested in model.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).Append(model))
        {
            var stateMachine = nested != model && nested.Name.StartsWith(mark, StringComparison.Ordinal);
            foreach (var other in nested.GetMethods(All))
                if (other != method && (stateMachine && other.Name == "MoveNext" || other.Name.StartsWith(mark, StringComparison.Ordinal))) yield return other;
        }
    }

    /// <summary>The rooms of the enchantments a hook gives a card (CardCmd.Enchant&lt;T&gt;: Pael's Growth's Clone and its rest-site option).</summary>
    private static (PathRoom Rooms, string Note) EnchantmentRooms(Type model, MethodInfo method, Func<MethodBase, List<CodeOp>> read)
    {
        var rooms = PathRoom.None;
        var notes = new List<string>();
        foreach (var command in ProgramReader.ReadMethod(model, method, read).Commands)
        {
            if (command.Command is not { DeclaringType: "CardCmd", Name: "Enchant", GenericArguments: [var enchantment] }) continue;
            var type = TypesOf(model.Assembly).FirstOrDefault(candidate => candidate.Name == enchantment && Derives(candidate, "EnchantmentModel"));
            if (type == null || type == model) continue;
            var reading = Read(type, read, _ => null);
            if (reading.Rooms == PathRoom.None) continue;
            rooms |= reading.Rooms;
            notes.Add($"gives {enchantment} ({reading.Rooms})");
        }
        return (rooms, string.Join(", ", notes));
    }

    private static bool Derives(Type type, string baseName)
    {
        for (var other = type.BaseType; other != null; other = other.BaseType)
            if (other.Name == baseName) return true;
        return false;
    }
}
