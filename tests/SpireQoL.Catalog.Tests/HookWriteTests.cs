using System.Reflection;
using System.Reflection.Emit;
using SpireQoL.Procs;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// "Read whole" means nothing is dropped (decision #195): in every hook a read model answers, on both branches, every instruction of its code that
/// changes something (a store to a field, a property set, a collection changed, a command, a call that changes one of the game's objects), in the
/// hook's body and in the class's own methods and the game's helpers it calls, maps to a step of its program; and every command and store of the
/// program reaches the simulator's program (ModelSpec), but a store nothing reads. The instructions are found here from the code alone, apart from
/// the reader's own checks (HookReader's OwnData), so a kind of write the reader passes over silently fails here.
/// </summary>
public class HookWriteTests
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public static TheoryData<string> Branches => new() { "beta", "main" };

    /// <summary>What only shows something (the game's flash, its async machinery, its log): no write.</summary>
    private static readonly HashSet<string> ShowsOnly = new(StringComparer.Ordinal)
    {
        "Flash", "InvokeDisplayAmountChanged", "AssertMutable", "SetResult", "Start", "AwaitUnsafeOnCompleted", "AwaitOnCompleted", "SetStateMachine", "SetException",
    };

    /// <summary>
    /// The game's helpers that change nothing of the fight: TaskHelper.RunSafely awaits a task the code made (the class's own method that made
    /// it is followed: its writes count), GodotTreeExtensions adds a node to the scene (what's shown).
    /// </summary>
    private static readonly HashSet<string> Helpers = new(StringComparer.Ordinal) { "TaskHelper", "GodotTreeExtensions" };

    /// <summary>The commands' classes that only show or sound something (ProgramReader's: they change nothing).</summary>
    private static readonly HashSet<string> ShowCommands = new(StringComparer.Ordinal) { "Cmd", "SfxCmd", "VfxCmd", "TalkCmd", "ThinkCmd", "MelodyCmd" };

    /// <summary>A collection's methods that only ask.</summary>
    private static readonly HashSet<string> Queries = new(StringComparer.Ordinal)
    {
        "Contains", "ContainsKey", "ContainsValue", "TryGetValue", "get_Count", "get_Item", "get_Keys", "get_Values", "GetEnumerator", "IndexOf", "Peek", "ToArray",
        "MoveNext", "get_Current", "Dispose",
    };

    [Theory]
    [MemberData(nameof(Branches))]
    public void EveryWriteInAReadHookIsAStep(string branch)
    {
        var path = branch == "main" ? BranchCode.MainPath : BranchCode.BetaPath;
        if (!File.Exists(path)) return; // no decompile of this branch here
        var code = BranchCode.Of(path);
        var types = code.Assembly.GetTypes();
        var read = code.Specs().Where(spec => spec.Unread == null).ToDictionary(spec => spec.Name, StringComparer.Ordinal);
        var dropped = new List<string>();
        var checkedHooks = 0;
        foreach (var type in HookReader.Powers(types).Concat(HookReader.Relics(types)).Where(type => read.ContainsKey(type.Name)))
        {
            var spec = read[type.Name];
            foreach (var hook in HookReader.Hooks(type))
            {
                if (!Enum.TryParse<HookName>(hook.Name, out var name) || code.HookReading(type).Hooks.FirstOrDefault(reading => reading.Hook == hook.Name) is not { Translated: true })
                    continue;
                checkedHooks++;
                var program = ProgramReader.Read(type, code.Read, hook.Name);
                var steps = program.All().ToList();
                // Its own data's Remove(cardPlay.Card, out amount) tested or taken for its value: a fact of the program (HookFact.TakeBound).
                var takes = spec.Values().Any(value => value is HookFactValue { Fact: HookFact.TakeBound or HookFact.BoundTaken }) || spec.Steps().Any(step => step is UnbindStep);
                foreach (var write in Writes(type, ProgramReader.BodyOf(hook), code.Read))
                    if (!Maps(write, steps) && !(takes && write is { Kind: "data", Name: "Remove" })) dropped.Add($"{type.Name}.{hook.Name}: {write} has no step");
                // A Modify hook is its returns: nothing it runs is a step, so it may change nothing.
                if (spec.Of(name) is { Returns: not null }) continue;
                var translated = spec.Of(name)?.Steps ?? Array.Empty<HookStep>();
                var flat = new ModelSpec(spec.Name, spec.Kind, new[] { new HookProgram(name, translated) }).Steps().ToList();
                // A keyword applied to a copy the same hook then makes (Music Box's ApplyKeyword on cardPlay.Card.CreateClone()) is the made card's, one step with it.
                var made = steps.OfType<CommandStep>().Where(step => step.Command.FullName == "CardPileCmd.AddGeneratedCardToCombat" && step.Values.Count > 0).Select(step => step.Values[0].ToString()).ToHashSet(StringComparer.Ordinal);
                var folded = steps.OfType<CommandStep>().Count(step => step.Command.FullName == "CardCmd.ApplyKeyword" && step.Values.Count > 0 && made.Contains(step.Values[0].ToString()));
                var commands = steps.OfType<CommandStep>().Count(step => step.Condition is not { IsNever: true }) + steps.OfType<CardActionStep>().Count(step => step.Condition is not { IsNever: true }) - folded;
                var commandSteps = flat.Count(step => step is not (StoreFieldStep or MonsterStateStep or BindStep or BindAddStep or UnbindStep or RepeatStep or ForEachStep or LetStep or FlashStep));
                if (commands != commandSteps) dropped.Add($"{type.Name}.{hook.Name}: {commands} commands, {commandSteps} translated");
                foreach (var store in steps.OfType<StoreStep>())
                    if (!flat.OfType<StoreFieldStep>().Any(step => step.Field == store.Field) && spec.Fields().Contains(store.Field))
                        dropped.Add($"{type.Name}.{hook.Name}: the store to {store.Field}, which a program reads, isn't translated");
            }
        }
        Assert.True(checkedHooks > 0, "no read hook checked");
        Assert.True(dropped.Count == 0, $"{branch}: what read hooks change that their programs drop:\n" + string.Join("\n", dropped));
    }

    /// <summary>
    /// The scan sees a write the program has no step for: Vital Spark's AfterPowerAmountChanged sets each Tainted card's affliction amount
    /// (item.Affliction.Amount = base.Amount), a property set on another of the game's objects that no store step reads (the reader takes it as
    /// only shown, HookReader.ShownOnly, and doesn't run the hook); Panache's count of its own (DynamicVars["CardsLeft"].BaseValue--) is a store step of its state.
    /// </summary>
    [Fact]
    public void TheScanFindsAWriteWithNoStep()
    {
        if (!File.Exists(BranchCode.BetaPath)) return;
        var code = BranchCode.Of(BranchCode.BetaPath);
        var vitalSpark = code.Assembly.GetTypes().Single(type => type.Name == PowerId.VitalSpark.Name);
        var hook = HookReader.Hooks(vitalSpark).Single(method => method.Name == nameof(HookName.AfterPowerAmountChanged));
        var steps = ProgramReader.Read(vitalSpark, code.Read, hook.Name).All().ToList();
        Assert.Contains(Writes(vitalSpark, ProgramReader.BodyOf(hook), code.Read), write => write == new Write("store", "Amount") && !Maps(write, steps));
        var panache = code.Assembly.GetTypes().Single(type => type.Name == PowerId.Panache.Name);
        var counted = HookReader.Hooks(panache).Single(method => method.Name == nameof(HookName.AfterCardPlayed));
        var panacheSteps = ProgramReader.Read(panache, code.Read, counted.Name).All().ToList();
        Assert.All(Writes(panache, ProgramReader.BodyOf(counted), code.Read).Where(write => write.Kind == "store"), write => Assert.True(Maps(write, panacheSteps), write.ToString()));
    }

    /// <summary>A write the code makes: what kind and what it names.</summary>
    private sealed record Write(string Kind, string Name)
    {
        public override string ToString() => $"{Kind} {Name}";
    }

    private static bool Maps(Write write, List<ProgramStep> steps) => write.Kind switch
    {
        // A store to a field or property of its own, a plain field of its data, or the BaseValue of one of its own values (DynamicVars["CardsLeft"]).
        "store" => steps.OfType<StoreStep>().Any(step => step.Field == write.Name || step.Field == ProgramReader.OwnDataPrefix + write.Name
            || write.Name == "BaseValue" && step.Field.StartsWith(ProgramReader.OwnVarPrefix, StringComparison.Ordinal)
            // A property set on an object the hook is given (an arrow or a card it's given: "Given.arrow.Status").
            || step.Field.StartsWith(ProgramReader.GivenPrefix, StringComparison.Ordinal) && step.Field.EndsWith("." + write.Name, StringComparison.Ordinal)
            // A property set on its owner's monster (((CorpseSlug)base.Owner.Monster).IsRavenous: "Monster.CorpseSlug.IsRavenous"), read beside a stun.
            || step.Field.StartsWith(ProgramReader.OwnersMonsterPrefix, StringComparison.Ordinal) && step.Field.EndsWith("." + write.Name, StringComparison.Ordinal)
            // A property set on a monster the hook makes before it joins (Infested's wriggler.StartStunned: "Made.Wriggler.StartStunned").
            || step.Field.StartsWith(ProgramReader.MadeMonsterPrefix, StringComparison.Ordinal) && step.Field.EndsWith("." + write.Name, StringComparison.Ordinal)
            // A property its owner's monster's method sets on its own creature (Steam Eruption's base.Creature.HpDisplay: "Creature.HpDisplay").
            || step.Field == ProgramReader.OwnCreaturePrefix + write.Name),
        "data" => steps.OfType<DataStep>().Any(step => step.Operation == write.Name),
        _ => steps.OfType<CommandStep>().Any(step => step.Command.FullName == write.Name) || steps.OfType<ChoiceStep>().Any(step => step.Command.FullName == write.Name)
            || steps.OfType<CardActionStep>().Any(step => step.Action.FullName == write.Name),
    };

    /// <summary>
    /// Every write in a hook's body and in what it calls of its own class (its methods, its local functions and lambdas, a base class's code
    /// of the hook) and of the game's static model helpers, three calls deep.
    /// </summary>
    private static IEnumerable<Write> Writes(Type type, MethodBase body, Func<MethodBase, List<CodeOp>> read)
    {
        var own = new[] { type }.Concat(CardCode.NestedTypes(type)).ToList();
        var seen = new HashSet<MethodBase> { body };
        var queue = new Queue<(MethodBase Method, int Depth)>();
        queue.Enqueue((body, 0));
        while (queue.Count > 0)
        {
            var (method, depth) = queue.Dequeue();
            List<CodeOp> ops;
            try { ops = read(method); }
            catch { continue; }
            for (var index = 0; index < ops.Count; index++)
            {
                var instruction = ops[index];
                if (Of(type, own, instruction) is { } write) yield return write;
                // A field set to its default through its address (ldflda, then initobj: Hardened Shell's damageReceivedThisTurn = default).
                if (instruction.OpCode == OpCodes.Ldflda && index + 1 < ops.Count && ops[index + 1].OpCode == OpCodes.Initobj && instruction.Member is { } cleared
                    && !cleared.DeclaringType.StartsWith('<') && !cleared.Name.StartsWith('<'))
                    yield return new Write("store", cleared.Name);
                if (depth >= 3 || !instruction.IsCall || instruction.Member is not { Kind: CodeMemberKind.Method } member || member.Name.StartsWith("get_", StringComparison.Ordinal)
                    || member.Name.StartsWith("set_", StringComparison.Ordinal) || ProgramReader.IsCommand(member)) continue;
                if (Called(type, own, member) is { } called && seen.Add(called)) queue.Enqueue((called, depth + 1));
            }
        }
    }

    /// <summary>The code a call runs that's the class's own (or a base class's of the game's, or a static model helper of the game's).</summary>
    private static MethodBase? Called(Type type, List<Type> own, CodeMember member)
    {
        var owner = own.FirstOrDefault(candidate => candidate.Name == member.DeclaringType);
        if (owner == null)
            for (var baseType = type.BaseType; baseType != null && owner == null; baseType = baseType.BaseType)
                if (baseType.Name == member.DeclaringType && baseType.Namespace?.StartsWith("MegaCrit.Sts2.Core.Models", StringComparison.Ordinal) == true) owner = baseType;
        if (owner == null && !member.HasThis && member.Namespace.StartsWith("MegaCrit.Sts2.Core.Models", StringComparison.Ordinal))
            owner = type.Assembly.GetType(member.Namespace + "." + member.DeclaringType);
        // A method of a monster class a power calls (on its owner's monster: Plow's CeremonialBeast.SetStunned, decision #275), read where it's called.
        if (owner == null && OfAMonster(member)) owner = type.Assembly.GetType(member.Namespace + "." + member.DeclaringType);
        // A creature's own method (Creature.SetNodeVisible), followed: its writes count.
        if (owner == null && OfACreature(member)) owner = type.Assembly.GetType(member.Namespace + "." + member.DeclaringType);
        if (owner == null) return null;
        var found = owner.GetMethods(All).FirstOrDefault(method => method.Name == member.Name && method.GetParameters().Length == member.ParameterCount);
        return found == null || found.IsAbstract ? null : ProgramReader.BodyOf(found);
    }

    /// <summary>An instance method a monster class of the game's declares (its writes are followed, as the reader reads it where a power calls it on its owner's monster).</summary>
    private static bool OfAMonster(CodeMember member) => member is { HasThis: true, Namespace: "MegaCrit.Sts2.Core.Models.Monsters" };

    /// <summary>An instance method of a creature (its writes are followed: Creature.SetNodeVisible only sets its node's Visible, which shows).</summary>
    private static bool OfACreature(CodeMember member) => member is { HasThis: true, Namespace: "MegaCrit.Sts2.Core.Entities.Creatures", DeclaringType: nameof(MegaCrit.Sts2.Core.Entities.Creatures.Creature) };

    /// <summary>The write an instruction makes, or null (a read, a compiler's local, something that only shows).</summary>
    private static Write? Of(Type type, List<Type> own, CodeOp instruction)
    {
        if (instruction.Member is not { } member) return null;
        if (instruction.OpCode == OpCodes.Stfld || instruction.OpCode == OpCodes.Stsfld)
            return member.DeclaringType.StartsWith('<') || member.Name.StartsWith('<') ? null : new Write("store", member.Name);
        if (!instruction.IsCall || member.Kind != CodeMemberKind.Method || member.Name == ".ctor" || member.Name.StartsWith('<') || ShowsOnly.Contains(member.Name)) return null;
        if (member.Namespace.Contains(".Nodes", StringComparison.Ordinal) || member.DeclaringType.StartsWith('<') || Helpers.Contains(member.DeclaringType)) return null;
        // A text set (a StringVar's StringValue: words a tooltip shows) only shows.
        if (member.Name.StartsWith("set_", StringComparison.Ordinal) && member.HasThis && member.ParameterTypes is ["String"]) return null;
        if (member.Name.StartsWith("set_", StringComparison.Ordinal) && member.HasThis)
            return member.Namespace.StartsWith("System.Collections", StringComparison.Ordinal) ? new Write("data", member.Name)
                : member.Namespace.StartsWith("MegaCrit", StringComparison.Ordinal) ? new Write("store", member.Name[4..]) : null;
        if (member.HasThis && member.Namespace.StartsWith("System.Collections", StringComparison.Ordinal))
            return Queries.Contains(member.Name) || member.Name.StartsWith("get_", StringComparison.Ordinal) ? null : new Write("data", member.Name);
        if (member.Namespace == "MegaCrit.Sts2.Core.Commands")
            return ShowCommands.Contains(member.DeclaringType) || !ProgramReader.IsCommand(member) && member.DeclaringType != "CardSelectCmd" && !(member.DeclaringType == "DamageCmd" && member.Name == "Attack")
                ? null : new Write("command", member.FullName);
        if (!member.Namespace.StartsWith("MegaCrit.Sts2.Core", StringComparison.Ordinal) || member.Name.StartsWith("get_", StringComparison.Ordinal)) return null;
        // One of the game's methods that gives nothing back (or a task): it changes something. The class's own, a base class's and a static model
        // helper's are followed (their writes count); any other is a write of its own.
        if (member.ValueType != typeof(void).Name && !member.ValueType.StartsWith("Task", StringComparison.Ordinal)) return null;
        if (own.Any(candidate => candidate.Name == member.DeclaringType)) return null;
        if (!member.HasThis && member.Namespace.StartsWith("MegaCrit.Sts2.Core.Models", StringComparison.Ordinal)) return null;
        if (OfAMonster(member) || OfACreature(member)) return null;
        for (var baseType = type.BaseType; baseType != null; baseType = baseType.BaseType)
            if (baseType.Name == member.DeclaringType && member.HasThis && !ProgramReader.IsModelAction(member) && !ProgramReader.IsPowerAction(member)) return null;
        return new Write("changes", member.FullName);
    }
}
