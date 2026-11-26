using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using SpireQoL.Data;
using SpireQoL.Procs.Programs;

namespace SpireQoL.Features;

/// <summary>
/// One thing an event option's code does that the reminders and their check look at (decision #350): what it does, the step that does it,
/// when (the tests on the way to it), its amount as the code works it out, and whose code it is (a relic the option gives, a reward it offers,
/// the event's own part).
/// </summary>
public sealed record OptionEffect(ChoiceEffect Kind, string Step, ProgramCondition? Condition, string Amount, string? Via)
{
    public override string ToString() => Kind + " " + Step + (Amount.Length > 0 ? "(" + Amount + ")" : "") + (Via != null ? " via " + Via : "")
        + (Condition != null ? " if " + OptionReading.Short(Condition.ToString()) : "");
}

/// <summary>
/// Something an option's code does that the reading can't follow (decision #350): counted and tracked, never dropped. <paramref name="WhenShown"/>:
/// the reading follows it once the option is shown (a delegate the event holds then), so it's a gap only of a reading made without the event.
/// </summary>
public sealed record OptionGap(string Reason, string? Via, bool WhenShown = false)
{
    public override string ToString() => "? " + Reason + (Via != null ? " via " + Via : "");
}

/// <summary>
/// An event option's code read as a program (decision #350): what it does (<see cref="Effects"/>), the game calls it makes that no reminder looks at
/// (<see cref="Others"/>), and what can't be followed (<see cref="Gaps"/>). The reminders' icons and the event check both come from it.
/// </summary>
public sealed record OptionReading(IReadOnlyList<OptionEffect> Effects, IReadOnlyList<OptionGap> Gaps, IReadOnlyList<string> Others)
{
    public static readonly OptionReading Empty = new(Array.Empty<OptionEffect>(), Array.Empty<OptionGap>(), Array.Empty<string>());

    /// <summary>Every kind of effect the option can have, whatever decides it.</summary>
    public ChoiceEffect Kinds => Effects.Aggregate(ChoiceEffect.None, (kinds, effect) => kinds | effect.Kind);

    /// <summary>Nothing in it the reading couldn't follow.</summary>
    public bool Whole => Gaps.Count == 0;

    public override string ToString()
    {
        var parts = Effects.Select(effect => effect.ToString()).Concat(Gaps.Select(gap => gap.ToString())).ToList();
        if (Others.Count > 0) parts.Add("other: " + string.Join(", ", Others));
        return parts.Count == 0 ? "nothing" : string.Join("; ", parts);
    }

    /// <summary>A condition or value cut to a line's length for a log or a snapshot.</summary>
    internal static string Short(string text) => text.Length <= 120 ? text : text[..117] + "...";
}

/// <summary>
/// Reads event options into programs (decision #350): the option's handler (the delegate its EventOption runs), read by the card program reader
/// (ProgramReader, decision #182) with the event's class as owner, so its steps come in the code's order with their conditions and values. The event
/// code it reads is shown to the reader with what an event does that a card never does named as steps: a reward made (read through the reward's
/// own OnSelect), the event's own part made (the Crystal Sphere's minigame: every method of it), a method of the event's base class (read through
/// its own code: SelectCardsToAddToDeckFromGrid; a fight, EnterCombatWithoutExitingEvent), a delegate called (the event's or the option's, read as
/// it's shown: decision #74), a query given a delegate that acts (it runs it); a delegate given to a next page's option or stored for later isn't a
/// step of this option, and a C# event raised runs the screens, not the option. A relic the option gives is read through its own
/// AfterObtained the same way. Works on any branch's code (types matched by name, the instructions from the reader it's given), never the game's state.
/// </summary>
public sealed class OptionReader
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private const string CommandsNamespace = "MegaCrit.Sts2.Core.Commands";

    /// <summary>How deep the reading follows helpers, delegates, rewards, parts and relics into each other.</summary>
    private const int MaxDepth = 6;

    private readonly Func<MethodBase, List<CodeOp>> _read;
    private readonly Func<string, bool> _isCurse;
    private readonly Dictionary<string, List<Type>> _types;
    private readonly ConcurrentDictionary<MethodBase, (List<CodeOp> Ops, List<CodeMember> Pages)> _code = new();
    private readonly ConcurrentDictionary<Type, OptionReading> _obtained = new();

    /// <param name="game">The game's assembly (the loaded one, or a branch's file in the tests).</param>
    /// <param name="read">The instructions of a method of it.</param>
    /// <param name="isCurse">Whether a card class (by name) is a curse.</param>
    public OptionReader(Assembly game, Func<MethodBase, List<CodeOp>> read, Func<string, bool> isCurse)
    {
        _read = read;
        _isCurse = isCurse;
        Type[] types;
        try { types = game.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.OfType<Type>().ToArray(); }
        _types = types.GroupBy(type => type.Name).ToDictionary(group => group.Key, group => group.ToList());
    }

    private static readonly Lazy<OptionReader> LoadedGame = new(() => new OptionReader(typeof(MegaCrit.Sts2.Core.Models.EventModel).Assembly, RuntimeCode.Read, LoadedCurse));

    /// <summary>The reader of the loaded game's code.</summary>
    public static OptionReader Game => LoadedGame.Value;

    private static volatile HashSet<string>? _loadedCurses;

    /// <summary>A curse card's class of the loaded game's (its canonical card's type), once the game's models are loaded.</summary>
    private static bool LoadedCurse(string card)
    {
        if (_loadedCurses is { } known) return known.Contains(card);
        try
        {
            var curses = MegaCrit.Sts2.Core.Models.ModelDb.AllCards.Where(model => model.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Curse)
                .Select(model => model.GetType().Name).ToHashSet();
            if (curses.Count > 0) _loadedCurses = curses;
            return curses.Contains(card);
        }
        catch { return false; }
    }

    // ------------------------------------------------------------------ what the steps mean

    /// <summary>
    /// The game commands an option's program runs and what they do ("Type.Method", "Type.Prefix*"; a choice is "CardSelectCmd.Method"). A reward's
    /// effect is read from its own code, not listed here. Events only (combat never shows event options).
    /// </summary>
    public static readonly IReadOnlyList<(string Step, ChoiceEffect Effect)> Steps = new[]
    {
        ("CardPileCmd.Add", ChoiceEffect.CardToDeck),
        ("CardCmd.Transform*", ChoiceEffect.CardToDeck | ChoiceEffect.CardRemoval),
        ("CardPileCmd.AddCurse*", ChoiceEffect.CurseToDeck),
        ("CreatureCmd.Damage", ChoiceEffect.HpLoss),
        ("CreatureCmd.Heal", ChoiceEffect.Heal),
        ("CreatureCmd.GainMaxHp", ChoiceEffect.MaxHpGain | ChoiceEffect.Heal),
        ("CreatureCmd.LoseMaxHp", ChoiceEffect.MaxHpLoss),
        ("PlayerCmd.GainGold", ChoiceEffect.GoldGain),
        ("PlayerCmd.LoseGold", ChoiceEffect.GoldLoss),
        ("PotionCmd.TryToProcure", ChoiceEffect.Potion),
        ("PotionCmd.Discard", ChoiceEffect.PotionLoss),
        ("PlayerCmd.MimicRestSiteHeal", ChoiceEffect.RestHeal | ChoiceEffect.Heal),
        ("CardCmd.Enchant*", ChoiceEffect.EnchantCard),
        ("CardSelectCmd.FromDeckForEnchantment", ChoiceEffect.EnchantCard),
        ("CardCmd.Upgrade*", ChoiceEffect.UpgradeCard),
        ("CardSelectCmd.FromDeckForUpgrade", ChoiceEffect.UpgradeCard),
        ("CardPileCmd.RemoveFromDeck", ChoiceEffect.CardRemoval),
        ("CardSelectCmd.FromDeckForRemoval", ChoiceEffect.CardRemoval),
        ("RelicCmd.Obtain*", ChoiceEffect.RelicGain),
        ("RelicCmd.Remove*", ChoiceEffect.RelicLoss),
        ("EventModel.EnterCombat*", ChoiceEffect.Fight),
    };

    /// <summary>What a step does (None when no reminder or check looks at it).</summary>
    public static ChoiceEffect EffectOf(string step)
    {
        var effect = ChoiceEffect.None;
        foreach (var (name, meaning) in Steps)
            if (name.EndsWith('*') ? step.StartsWith(name[..^1], StringComparison.Ordinal) : step == name) effect |= meaning;
        return effect;
    }

    // ------------------------------------------------------------------ the code the reader sees

    /// <summary>
    /// A method's instructions as the option reader shows them to the program reader: a delegate given to a next page's option or stored in an
    /// object for later is a null (it isn't run here); a reward or one of the event's own parts made, a delegate called and a method of the event's
    /// base class are command calls, so each is a step at its place with its condition and values.
    /// </summary>
    public List<CodeOp> Code(MethodBase method) => Rewrite(method).Ops;

    private (List<CodeOp> Ops, List<CodeMember> Pages) Rewrite(MethodBase method) => _code.GetOrAdd(method, RewriteNow);

    private (List<CodeOp> Ops, List<CodeMember> Pages) RewriteNow(MethodBase method)
    {
        _rewriting ??= new HashSet<MethodBase>();
        _rewriting.Add(method);
        try { return RewriteOps(_read(method)); }
        finally { _rewriting.Remove(method); }
    }

    private (List<CodeOp> Ops, List<CodeMember> Pages) RewriteOps(List<CodeOp> ops)
    {
        var shown = new List<CodeOp>(ops.Count);
        var pages = new List<CodeMember>();
        var runners = new HashSet<int>();
        for (var i = 0; i < ops.Count; i++)
        {
            var instruction = ops[i];
            if (instruction.OpCode == OpCodes.Ldftn && instruction.Member is { } target && i + 1 < ops.Count && ops[i + 1] is { Member: { } made } next
                && next.OpCode == OpCodes.Newobj && IsDelegate(made.DeclaringType) && Consumer(ops, i + 2) is { } at)
            {
                var consumer = ops[at];
                if (Keeps(consumer))
                {
                    // Only a Func<Task> is an option's handler (an option also keeps a WillKillPlayer test).
                    if (IsPage(consumer) && made.TypeArguments is ["Task"]) pages.Add(target);
                    shown.Add(new CodeOp(instruction.Offset, OpCodes.Ldnull));
                    continue;
                }
                // A delegate that acts given to a query that runs it (Neow's Bones' Select(r => new RelicReward(r, ...))): the query is a step, which the
                // program reader gives the delegate's steps to.
                if (consumer.Member is { Namespace: var space } && space.StartsWith("System", StringComparison.Ordinal) && Acts(target)) runners.Add(at);
            }
            // A C# event raised (EventModel's StateChanged?.Invoke(this), a reward's): its listeners are the game's screens, not the option.
            if (instruction.IsCall && instruction.Member is { Name: nameof(Action.Invoke) } invoked && IsDelegate(invoked.DeclaringType))
            {
                var source = DelegateSource(ops, i);
                shown.Add(source == null ? instruction : instruction with { Member = invoked with { Namespace = CommandsNamespace, GenericArguments = new[] { source } } });
                continue;
            }
            if (runners.Contains(i) && instruction.Member is { } runner)
            {
                shown.Add(instruction with { Member = runner with { Namespace = CommandsNamespace } });
                continue;
            }
            shown.Add(instruction.IsCall && instruction.Member is { Kind: CodeMemberKind.Method } member && AsStep(instruction, member) is { } step ? step : instruction);
        }
        return (shown, pages);
    }

    /// <summary>
    /// Where the delegate the call at <paramref name="at"/> invokes comes from, by the nearest load of one before it: a field ("Dish.action"), a property
    /// ("CardRewardAlternative.OnSelect"), a parameter or a local. Null for a C# event's field: raising it runs its listeners, the game's screens.
    /// </summary>
    private string? DelegateSource(List<CodeOp> ops, int at)
    {
        for (var back = at - 1; back >= 0 && back >= at - 16; back--)
        {
            var load = ops[back];
            // An async method's state machine keeps its parameters and locals in fields: those are a parameter or local.
            if (load.OpCode == OpCodes.Ldfld && load.Member is { Kind: CodeMemberKind.Field } field && IsDelegate(field.ValueType))
                return TypeOf(field) is { } owner && owner.GetEvent(field.Name, All) != null ? null
                    : field.DeclaringType.Contains(">d__", StringComparison.Ordinal) ? "a parameter or local" : "field " + field.DeclaringType + "." + field.Name;
            if (load.IsCall && load.Member is { Name: var getter } property && getter.StartsWith("get_", StringComparison.Ordinal) && IsDelegate(property.ValueType))
                return "property " + property.DeclaringType + "." + getter[4..];
        }
        return "a parameter or local";
    }

    /// <summary>The method a delegate points at may act: its code (as shown to the reader) calls a command.</summary>
    private bool Acts(CodeMember target)
    {
        if (TypeOf(target) is not { } owner || ProgramReader.Called(owner.GetMethods(All), target) is not { } method) return false;
        var body = ProgramReader.BodyOf(method);
        _rewriting ??= new HashSet<MethodBase>();
        if (!_rewriting.Add(body)) return false;
        try { return Rewrite(body).Ops.Any(instruction => instruction.IsCall && instruction.Member?.Namespace == CommandsNamespace); }
        catch { return false; }
        finally { _rewriting.Remove(body); }
    }

    [ThreadStatic] private static HashSet<MethodBase>? _rewriting;

    /// <summary>The index of the call a delegate made at <paramref name="from"/> is given to: the first one after it that takes a delegate.</summary>
    private static int? Consumer(List<CodeOp> ops, int from)
    {
        for (var index = from; index < ops.Count; index++)
            if (ops[index].IsCall && ops[index].Member is { } member && member.ParameterTypes.Any(IsDelegate)) return index;
        return null;
    }

    /// <summary>An object made with the delegate keeps it (an EventOption, the Conveyor's Dish), or it makes an option: it isn't run there.</summary>
    private static bool Keeps(CodeOp consumer) => consumer.OpCode == OpCodes.Newobj && consumer.Member?.Namespace.StartsWith("MegaCrit", StringComparison.Ordinal) == true || IsPage(consumer);

    /// <summary>The call makes an event option (its constructor, EventOption.FromRelic, an ancient's RelicOption): the delegate is a next page's option.</summary>
    private static bool IsPage(CodeOp consumer) => consumer.Member is { } member && (member.DeclaringType == "EventOption" || member.ValueType == "EventOption");

    private static bool IsDelegate(string typeName) => typeName.StartsWith("Func`", StringComparison.Ordinal) || typeName.StartsWith("Action", StringComparison.Ordinal);

    private CodeOp? AsStep(CodeOp instruction, CodeMember member)
    {
        if (instruction.OpCode == OpCodes.Newobj)
        {
            if (TypeOf(member) is not { } made || !IsReward(made) && !IsEventPart(made)) return null;
            return instruction with { OpCode = OpCodes.Call, Member = member with { Namespace = CommandsNamespace, HasThis = false, ReturnsValue = true, ValueType = member.DeclaringType } };
        }
        if (!member.HasThis) return null;
        if (Acting(member) && TypeOf(member) is { IsAbstract: true } baseClass && IsEventClass(baseClass)) return instruction with { Member = member with { Namespace = CommandsNamespace } };
        return null;
    }

    /// <summary>A method that may act: not a property, not the compiler's, returning nothing or a task.</summary>
    private static bool Acting(CodeMember member) =>
        !member.Name.StartsWith('<') && !member.Name.StartsWith("get_", StringComparison.Ordinal) && !member.Name.StartsWith("set_", StringComparison.Ordinal)
        && (member.ValueType == typeof(void).Name || member.ValueType.StartsWith("Task", StringComparison.Ordinal));

    private Type? TypeOf(CodeMember member) => TypeNamed(member.DeclaringType, member.Namespace);

    private Type? TypeNamed(string name, string? space = null) =>
        _types.TryGetValue(name, out var found) ? found.FirstOrDefault(type => space == null || type.Namespace == space) ?? (space == null ? null : found.FirstOrDefault()) : null;

    private static bool Derives(Type type, string baseName)
    {
        for (var other = type; other != null; other = other.BaseType)
            if (other.Name == baseName) return true;
        return false;
    }

    private static bool IsReward(Type type) => Derives(type, "Reward") && type.Namespace == "MegaCrit.Sts2.Core.Rewards";

    private static bool IsEventPart(Type type) => type.Namespace?.StartsWith("MegaCrit.Sts2.Core.Events.Custom", StringComparison.Ordinal) == true;

    private static bool IsEventClass(Type type) => Derives(type, "EventModel");

    private static bool IsRelic(Type type) => Derives(type, "RelicModel");

    private static bool IsConcreteEvent(Type type) => !type.IsAbstract && IsEventClass(type);

    private static Type Outermost(Type type)
    {
        while (type.DeclaringType != null) type = type.DeclaringType;
        return type;
    }

    // ------------------------------------------------------------------ reading

    /// <summary>What the reading knows when it reads one option.</summary>
    /// <param name="OptionRelic">The relic the option carries (an ancient's), for an Obtain of a relic its code doesn't name.</param>
    /// <param name="Held">The delegates the event and the option hold as the option is shown (decision #74), by where the call gets one ("field Dish.action",
    /// "a parameter or local"); null: read without the event.</param>
    private sealed record Context(Type? OptionRelic, Func<string, IEnumerable<MethodBase>>? Held);

    private sealed class Sink
    {
        public readonly List<OptionEffect> Effects = new();
        public readonly List<OptionGap> Gaps = new();
        public readonly List<string> Others = new();
        public readonly HashSet<MethodBase> Seen = new();
        public bool RelicUsed;

        public void Effect(OptionEffect effect)
        {
            if (!Effects.Contains(effect)) Effects.Add(effect);
        }

        public void Gap(OptionGap gap)
        {
            if (!Gaps.Contains(gap)) Gaps.Add(gap);
        }

        public void Other(string step)
        {
            if (!Others.Contains(step)) Others.Add(step);
        }

        public OptionReading Reading() => new(Effects.ToList(), Gaps.ToList(), Others.ToList());
    }

    /// <summary>
    /// An option: its handler's program, with <paramref name="optionRelic"/> the relic the option carries (read through its AfterObtained where the
    /// code obtains it, or as the option's own when it doesn't), and <paramref name="held"/> the delegates the event holds now (null without the event).
    /// </summary>
    public OptionReading Read(MethodBase handler, Type? optionRelic = null, Func<string, IEnumerable<MethodBase>>? held = null)
    {
        var sink = new Sink();
        var context = new Context(optionRelic, held);
        if (handler.DeclaringType is { } declaring)
        {
            var owner = Outermost(declaring);
            sink.Seen.Add(handler);
            Collect(ProgramReader.ReadMethod(owner, handler, Code).Steps, owner, null, null, depth: 0, sink, context);
        }
        if (optionRelic != null && !sink.RelicUsed) Include(ReadObtained(optionRelic), null, optionRelic.Name, sink);
        return sink.Reading();
    }

    [ThreadStatic] private static HashSet<Type>? _reading;

    /// <summary>What a relic does as you obtain it: its own AfterObtained, read like an option's code (empty when it has none).</summary>
    public OptionReading ReadObtained(Type relic)
    {
        if (_obtained.TryGetValue(relic, out var known)) return known;
        _reading ??= new HashSet<Type>();
        if (!_reading.Add(relic)) return OptionReading.Empty; // a relic whose obtaining obtains itself: read once
        try
        {
            var sink = new Sink();
            if (relic.GetMethod("AfterObtained", All) is { IsAbstract: false } obtained)
                Collect(ProgramReader.ReadMethod(relic, obtained, Code).Steps, relic, null, null, depth: 0, sink, new Context(null, null));
            var reading = sink.Reading();
            // The loaded game's reading waits for its models (a curse is told by its card) before it keeps one.
            return _isCurse != LoadedCurse || _loadedCurses != null ? _obtained.GetOrAdd(relic, reading) : reading;
        }
        finally { _reading.Remove(relic); }
    }

    private static ProgramCondition? And(ProgramCondition? outer, ProgramCondition? inner) => outer == null ? inner : inner == null ? outer : outer.And(inner);

    private void Collect(IEnumerable<ProgramStep> steps, Type owner, ProgramCondition? outer, string? via, int depth, Sink sink, Context context)
    {
        foreach (var step in steps)
        {
            var condition = And(outer, step.Condition);
            switch (step)
            {
                case LoopStep loop:
                    Collect(loop.Body, owner, condition, via, depth, sink, context);
                    break;
                case UnknownStep unknown:
                    sink.Gap(new OptionGap(unknown.Reason, via));
                    break;
                case ChoiceStep choice:
                    Classify(choice.Command.FullName, condition, "", via, sink);
                    break;
                case CreateStep create:
                    // The card it makes is a curse (Hefty Tablet's RunState.CreateCard<Injury>): whatever command adds it, a curse joins the deck.
                    if (_isCurse(create.Card)) sink.Effect(new OptionEffect(ChoiceEffect.CurseToDeck, "create " + create.Card, condition, "", via));
                    break;
                case CommandStep command:
                    Command(command, owner, condition, via, depth, sink, context);
                    Collect(command.Inner, owner, condition, via, depth, sink, context);
                    break;
            }
        }
    }

    private static void Classify(string step, ProgramCondition? condition, string amount, string? via, Sink sink)
    {
        var effect = EffectOf(step);
        if (effect == ChoiceEffect.None) sink.Other(step);
        else sink.Effect(new OptionEffect(effect, step, condition, amount, via));
    }

    private void Command(CommandStep command, Type owner, ProgramCondition? condition, string? via, int depth, Sink sink, Context context)
    {
        var member = command.Command;
        var name = member.FullName;
        if (depth >= MaxDepth)
        {
            sink.Gap(new OptionGap(name + ": followed too deep", via));
            return;
        }
        // A delegate called. One the event holds (the Endless Conveyor's dish: GrabSomethingOffTheBelt runs CurrentDish.action) or one its option was
        // given (Neow's modifier options) is read as the event and the option hold it when the option is shown (decision #74); one from elsewhere (a card
        // reward's alternatives) is a gap.
        if (member.Name == nameof(Action.Invoke) && IsDelegate(member.DeclaringType))
        {
            var source = member.GenericArguments.Count > 0 ? member.GenericArguments[0] : "a parameter or local";
            var fromEvent = source.StartsWith("field ", StringComparison.Ordinal)
                ? TypeNamed(source["field ".Length..].Split('.')[0]) is { } fieldOwner && IsConcreteEvent(Outermost(fieldOwner))
                : !source.StartsWith("property ", StringComparison.Ordinal) && IsConcreteEvent(owner);
            if (!fromEvent)
            {
                sink.Gap(new OptionGap("runs a delegate from " + source, via));
                return;
            }
            if (context.Held == null)
            {
                sink.Gap(new OptionGap("runs a delegate the event holds", via, WhenShown: true));
                return;
            }
            var held = context.Held(source).Where(method => method.DeclaringType != null).ToList();
            if (held.Count == 0) sink.Gap(new OptionGap("runs a delegate the event doesn't hold", via));
            foreach (var method in held)
            {
                var holder = Outermost(method.DeclaringType!);
                if (sink.Seen.Add(method)) Collect(ProgramReader.ReadMethod(holder, method, Code).Steps, holder, condition, via, depth + 1, sink, context);
            }
            return;
        }
        if (member.Name == ".ctor" && TypeOf(member) is { } made)
        {
            if (IsReward(made)) Reward(made, condition, via, depth, sink, context);
            else Part(made, condition, depth, sink, context);
            return;
        }
        // The event's base class's own method (SelectCardsToAddToDeckFromGrid): its steps, from its code; a fight is the base class's to start.
        if (TypeOf(member) is { IsAbstract: true } baseClass && IsEventClass(baseClass))
        {
            if (EffectOf(name) is not ChoiceEffect.None)
            {
                Classify(name, condition, "", via, sink);
                return;
            }
            if (ProgramReader.Called(baseClass.GetMethods(All), member) is { IsAbstract: false } method)
            {
                if (sink.Seen.Add(method)) Collect(ProgramReader.ReadMethod(baseClass, method, Code).Steps, baseClass, condition, via, depth + 1, sink, context);
            }
            else sink.Gap(new OptionGap(name + ": its code isn't there", via));
            return;
        }
        Classify(name, condition, Amount(command), via, sink);
        if ((EffectOf(name) & ChoiceEffect.CardToDeck) != 0 && NamesCurse(command)) sink.Effect(new OptionEffect(ChoiceEffect.CurseToDeck, name, condition, "", via));
        if (name.StartsWith("RelicCmd.Obtain", StringComparison.Ordinal)) Obtain(command, owner, condition, via, sink, context);
    }

    /// <summary>A reward the option makes: what taking it does, from its own OnSelect.</summary>
    private void Reward(Type reward, ProgramCondition? condition, string? via, int depth, Sink sink, Context context)
    {
        MethodInfo? select = null;
        for (var type = reward; type != null && select == null; type = type.BaseType)
            select = type.GetMethods(All).FirstOrDefault(method => method.Name == "OnSelect" && !method.IsAbstract);
        var name = "reward " + reward.Name;
        if (select == null)
        {
            sink.Gap(new OptionGap(name + ": no OnSelect to read", via));
            return;
        }
        if (sink.Seen.Add(select)) Collect(ProgramReader.ReadMethod(select.DeclaringType!, select, Code).Steps, select.DeclaringType!, condition, via == null ? name : via + ", " + name, depth + 1, sink, context with { OptionRelic = null });
    }

    /// <summary>One of the event's own parts it makes (the Crystal Sphere's minigame, and the items it hides): everything it does is something the option can do.</summary>
    private void Part(Type part, ProgramCondition? condition, int depth, Sink sink, Context context)
    {
        var name = "part " + part.Name;
        foreach (var type in new[] { part }.Concat(SpireQoL.Procs.CardCode.NestedTypes(part)))
        {
            if (type.Name.StartsWith('<')) continue;
            foreach (var method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
            {
                if (method.IsAbstract || method.Name.StartsWith("get_", StringComparison.Ordinal) || !sink.Seen.Add(method)) continue;
                Collect(ProgramReader.ReadMethod(part, method, Code).Steps, part, condition, name, depth + 1, sink, context);
            }
        }
    }

    /// <summary>A relic the option obtains: by its class in the code, else the relic the option carries; its AfterObtained read the same way.</summary>
    private void Obtain(CommandStep command, Type owner, ProgramCondition? condition, string? via, Sink sink, Context context)
    {
        var named = command.Command.GenericArguments.Concat(command.Values.SelectMany(value => value.Parts())
                .OfType<QueryValue>().Where(query => query is { Type: "ModelDb", Name: "Relic" }).SelectMany(query => query.GenericArguments))
            .Select(relic => TypeNamed(relic)).FirstOrDefault(type => type != null && IsRelic(type));
        if (named == null && context.OptionRelic != null && !sink.RelicUsed)
        {
            named = context.OptionRelic;
            sink.RelicUsed = true;
        }
        if (named == null)
        {
            // By where the relic comes from: a reward's (picked as the reward is made), one the event or the relic holds (a property or field of its
            // own), one its code picks as you choose (a call's result).
            var held = command.Values.Count > 0 && Held(command.Values[0]);
            var reason = IsReward(owner) ? "obtains a reward's relic" : held ? "obtains a relic the event holds" : "obtains a relic its code picks as you choose";
            sink.Gap(new OptionGap(reason, via));
            return;
        }
        if (named == context.OptionRelic) sink.RelicUsed = true;
        Include(ReadObtained(named), condition, via == null ? named.Name : via + ", " + named.Name, sink);
    }

    /// <summary>
    /// The value is something the model holds: its own property or field (the Museum's OfferedRelic), an item of one (the Relic Trader's NewRelics[index]),
    /// kept in a local or copied (ToMutable) on the way.
    /// </summary>
    private static bool Held(ProgramValue value) => value switch
    {
        StoredValue stored => Held(stored.Value),
        QueryValue { Name: "ToMutable", Of: { } copied } => Held(copied),
        QueryValue { Name: "Item" or "get_Item" or "ElementAt", Of: { } list } => Held(list),
        QueryValue { Of: NamedValue { Name: "this" } } => true,
        _ => false,
    };

    /// <summary>A relic's obtaining, as part of the option: its effects on the option's way to it, its gaps.</summary>
    private static void Include(OptionReading obtained, ProgramCondition? condition, string via, Sink sink)
    {
        foreach (var effect in obtained.Effects) sink.Effect(effect with { Condition = And(condition, effect.Condition), Via = effect.Via == null ? via : via + ", " + effect.Via });
        foreach (var gap in obtained.Gaps) sink.Gap(gap with { Via = gap.Via == null ? via : via + ", " + gap.Via });
        foreach (var other in obtained.Others) sink.Other(other);
    }

    /// <summary>The step's card is a curse: a curse card's class or the curse pool in its type arguments or values (ModelDb.Card&lt;Injury&gt;).</summary>
    private bool NamesCurse(CommandStep command) =>
        command.Command.GenericArguments.Any(_isCurse) || command.Values.SelectMany(value => value.Parts()).OfType<QueryValue>()
            .Any(query => query.GenericArguments.Any(argument => _isCurse(argument) || argument == "CurseCardPool") || query.Name == ".ctor" && _isCurse(query.Type));

    /// <summary>The amount a step works with, as its code gives it: its arguments that are a value of the event's or a number.</summary>
    private static string Amount(CommandStep command)
    {
        var types = command.Command.ParameterTypes;
        var amounts = command.Values.Where((value, index) => index < types.Count && (types[index] is "Decimal" or "Int32" or "Int64" or "Single" or "Double" || types[index].EndsWith("Var", StringComparison.Ordinal)));
        return OptionReading.Short(string.Join(", ", amounts));
    }

    // ------------------------------------------------------------------ the options in an event's code

    /// <summary>
    /// The options an event's code makes, each with the relic it carries: every handler given to an EventOption (the event's own methods, lambdas
    /// and local functions, any page), and for an ancient, an option for each relic its code names (RelicOption&lt;T&gt;, ModelDb.Relic&lt;T&gt;), whose
    /// handler is its base class's.
    /// </summary>
    public IReadOnlyList<(MethodBase Handler, Type? Relic)> Options(Type eventType)
    {
        var found = new List<(MethodBase, Type?)>();
        var own = new[] { eventType }.Concat(SpireQoL.Procs.CardCode.NestedTypes(eventType)).ToList();
        foreach (var page in Pages(own))
            if (!found.Contains((page, null))) found.Add((page, null));
        var bases = new List<Type>();
        for (var type = eventType.BaseType; type != null && IsEventClass(type); type = type.BaseType) bases.Add(type);
        var relicHandler = bases.SelectMany(baseClass => new[] { baseClass }.Concat(SpireQoL.Procs.CardCode.NestedTypes(baseClass)))
            .SelectMany(type => type.GetMethods(All).Where(method => method.Name == "RelicOption").Select(method => (Type: type, Method: (MethodBase)method)))
            .SelectMany(entry => PagesIn(entry.Method, bases.Concat(bases.SelectMany(SpireQoL.Procs.CardCode.NestedTypes)).ToList())).FirstOrDefault();
        if (relicHandler == null) return found;
        foreach (var type in own)
            foreach (var method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
            {
                List<CodeOp> ops;
                try { ops = _read(method); }
                catch { continue; }
                foreach (var instruction in ops)
                {
                    if (!instruction.IsCall || instruction.Member is not { GenericArguments: [var relic] } member || member.Name != "RelicOption" && !(member is { DeclaringType: "ModelDb", Name: "Relic" })) continue;
                    if (TypeNamed(relic) is { } relicType && IsRelic(relicType) && !found.Contains((relicHandler, relicType))) found.Add((relicHandler, relicType));
                }
            }
        return found;
    }

    private IEnumerable<MethodBase> Pages(List<Type> types) =>
        types.SelectMany(type => type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All))).SelectMany(method => PagesIn(method, types));

    /// <summary>The handlers a method gives to options, found among <paramref name="types"/>.</summary>
    private IEnumerable<MethodBase> PagesIn(MethodBase method, List<Type> types)
    {
        if (method.IsAbstract) yield break;
        List<CodeMember> pages;
        try { pages = Rewrite(method).Pages; }
        catch { yield break; }
        foreach (var page in pages)
            if (types.FirstOrDefault(type => type.Name == page.DeclaringType) is { } owner && ProgramReader.Called(owner.GetMethods(All), page) is { } handler) yield return handler;
    }
}
