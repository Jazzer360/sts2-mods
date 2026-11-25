using System.Reflection;
using System.Reflection.Emit;
using SpireQoL.Cards;
using SpireQoL.Combat;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #177: the code that runs on every frame makes no garbage while nothing changes, read from the compiled mod. Two kinds of
/// garbage can't be seen in the source. A lambda capturing a method's own local makes its closure where that local's scope begins: for
/// one at the method's top level, on every call, even when the lambda never runs (a reused frame that returns early). A foreach through
/// an interface (IReadOnlyList, IEnumerable) boxes the list's enumerator. And anywhere in the mod, a string passed where Godot takes a
/// StringName, NodePath or Variant is converted on every call into a finalizable Godot object (GodotNames makes each name once).
/// </summary>
public class FrameCodeScanTests
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    private static readonly HashSet<FlowControl> Branches = new() { FlowControl.Branch, FlowControl.Cond_Branch, FlowControl.Return, FlowControl.Throw };
    private static Assembly Mod => typeof(SpireQoL.ModEntry).Assembly;

    private static MethodBase Method(Type type, string name) =>
        type.GetMethod(name, All) ?? type.GetProperty(name, All)?.GetMethod ?? throw new InvalidOperationException($"{type.Name}.{name} is gone: update the frame's code list");

    /// <summary>
    /// The frame's own code: every feature's OnFrame, the late-frame jobs, and what they all share every frame (the forecast's and the
    /// cards' reuse paths, the board probe, the game readers, the widgets' layout, the card tabs' render and the Ethereal fade).
    /// </summary>
    internal static List<MethodBase> FrameCode()
    {
        var methods = Mod.GetTypes()
            .Where(type => !type.IsAbstract && typeof(Sts2Mods.Core.Features.IFrameFeature).IsAssignableFrom(type))
            .Select(type => type.GetMethod(nameof(Sts2Mods.Core.Features.IFrameFeature.OnFrame), All))
            .OfType<MethodBase>()
            .ToList();
        // Run every frame, with a change path of their own past the reused frame's return.
        methods.Add(Method(typeof(CardOverlayRenderer), nameof(CardOverlayRenderer.Render)));
        methods.Add(Method(typeof(CardPredictions), nameof(CardPredictions.Update)));
        methods.Add(Method(typeof(MovableWidget), nameof(MovableWidget.UpdateLayout)));
        methods.AddRange(SharedFrameCode());
        return methods;
    }

    /// <summary>
    /// What every feature shares on every frame: held to no LINQ either (a feature's own OnFrame may use it on its change path, and the
    /// dev dashboard's steady watch catches what runs while nothing changes).
    /// </summary>
    internal static List<MethodBase> SharedFrameCode()
    {
        return new List<MethodBase>
        {
            Method(typeof(FlatFade), nameof(FlatFade.Update)),
            Method(typeof(GlowMask), nameof(GlowMask.Update)),
            Method(typeof(TurnEndModel), nameof(TurnEndModel.Begin)),
            Method(typeof(TurnEndModel), "BeginNow"),
            Method(typeof(TurnEndModel), nameof(TurnEndModel.Compute)),
            Method(typeof(TurnEndModel), "Finish"),
            Method(typeof(TurnEndModel), nameof(TurnEndModel.Shown)),
            Method(typeof(TurnEndModel), nameof(TurnEndModel.ShownMark)),
            Method(typeof(TurnEndModel), nameof(TurnEndModel.ProgressMark)),
            Method(typeof(TurnEndModel), nameof(TurnEndModel.BlockableTotal)),
            Method(typeof(TurnEndModel), nameof(TurnEndModel.UnblockableTotal)),
            Method(typeof(TurnEndModel), nameof(TurnEndModel.BlockGain)),
            Method(typeof(TurnEndModel), nameof(TurnEndModel.Unknown)),
            Method(typeof(ChangeGate), nameof(ChangeGate.Decide)),
            Method(typeof(ReuseCheck), nameof(ReuseCheck.Decide)),
            Method(typeof(SpireQoL.Sim.SimOutcome), nameof(SpireQoL.Sim.SimOutcome.AnyUnmodelled)),
            Method(typeof(CardPredictions), "Reuse"),
            Method(typeof(BoardProbe), nameof(BoardProbe.Hash)),
            Method(typeof(Sts2State), nameof(Sts2State.Me)),
            Method(typeof(Sts2State), nameof(Sts2State.MeInRun)),
            Method(typeof(Sts2State), nameof(Sts2State.Hand)),
            Method(typeof(Sts2State), nameof(Sts2State.HandNodes)),
            Method(typeof(MovableWidget), "Placement"),
        };
    }

    private static List<HarmonyLib.CodeInstruction>? Code(MethodBase method)
    {
        if (method.IsAbstract || method.GetMethodBody() == null || method.IsGenericMethodDefinition) return null;
        try { return SpireQoL.Diagnostics.GameCode.Instructions(method); }
        catch { return null; }
    }

    private static string NameOf(MethodBase method) => $"{method.DeclaringType?.Name}.{method.Name}";

    [Fact]
    public void TheFramesCodeMakesNoClosureOnEveryCall()
    {
        var found = new List<string>();
        foreach (var method in FrameCode())
        {
            foreach (var instruction in Code(method) ?? new())
            {
                if (Branches.Contains(instruction.opcode.FlowControl)) break;
                if (instruction.opcode == OpCodes.Newobj && instruction.operand is ConstructorInfo { DeclaringType.Name: var name } && name.Contains("DisplayClass", StringComparison.Ordinal))
                {
                    found.Add(NameOf(method));
                    break;
                }
            }
        }
        Assert.True(found.Count == 0, "Closures made on every call (move the lambda into a method of its own, or loop instead): " + string.Join(", ", found));
    }

    [Fact]
    public void TheFramesCodeLoopsThroughNoInterfaceAndNoLinq()
    {
        var found = new List<string>();
        var shared = SharedFrameCode().ToHashSet();
        foreach (var method in FrameCode())
            foreach (var instruction in Code(method) ?? new())
                if (instruction.operand is MethodInfo { Name: "GetEnumerator", DeclaringType: { IsInterface: true } owner })
                {
                    found.Add($"{NameOf(method)} (foreach through {owner.Name})");
                    break;
                }
                else if (shared.Contains(method) && instruction.operand is MethodInfo { DeclaringType: { } linq } query && linq == typeof(Enumerable))
                {
                    found.Add($"{NameOf(method)} (LINQ's {query.Name})");
                    break;
                }
        Assert.True(found.Count == 0, "A foreach through an interface, or LINQ, makes an enumerator every frame (loop by index, or over the concrete list): " + string.Join(", ", found));
    }

    [Fact]
    public void TheWidgetsPlacementBoxesNothing()
    {
        // Every widget is placed every frame (MovableWidget.UpdateLayout), beside what its attach targets read off the screen: each method of
        // the mod giving a screen rect. A value boxed there is garbage on every frame (a dev review: the HP bar's combat id, compared as an
        // object, made 24 bytes on every frame of every fight).
        var methods = new List<MethodBase> { Method(typeof(MovableWidget), nameof(MovableWidget.UpdateLayout)), Method(typeof(MovableWidget), "Placement") };
        methods.AddRange(Mod.GetTypes().SelectMany(type => type.GetMethods(All))
            .Where(method => method.ReturnType == typeof(Godot.Rect2) || method.ReturnType == typeof(Godot.Rect2?)));
        var found = new List<string>();
        foreach (var method in methods)
            foreach (var instruction in Code(method) ?? new())
                if (instruction.opcode == OpCodes.Box)
                {
                    found.Add($"{NameOf(method)} (boxes {(instruction.operand as Type)?.Name})");
                    break;
                }
        Assert.True(found.Count == 0, "A value boxed while widgets are placed, every frame (compare it as its own type): " + string.Join(", ", found));
    }

    [Fact]
    public void NoGodotNameIsMadeFromAStringOnEveryCall()
    {
        var found = new List<string>();
        foreach (var type in Mod.GetTypes())
        {
            if (type == typeof(GodotNames)) continue; // where each one is made, once
            foreach (var method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
            {
                if (method is ConstructorInfo { IsStatic: true }) continue; // a static initializer runs once
                foreach (var instruction in Code(method) ?? new())
                {
                    var made = instruction.operand switch
                    {
                        MethodInfo { Name: "op_Implicit", DeclaringType.Namespace: "Godot" } conversion when conversion.GetParameters() is [{ ParameterType: var from }] && from == typeof(string) => conversion.ReturnType.Name,
                        ConstructorInfo { DeclaringType: { Namespace: "Godot" } godotType } constructor when instruction.opcode == OpCodes.Newobj
                            && godotType.Name is "StringName" or "NodePath" && constructor.GetParameters() is [{ ParameterType: var from }] && from == typeof(string) => godotType.Name,
                        _ => null,
                    };
                    if (made == null) continue;
                    found.Add($"{type.Name}.{method.Name} ({made})");
                    break;
                }
            }
        }
        Assert.True(found.Count == 0, "Godot names made from a string on every call (GodotNames.Name / GodotNames.Path, or a static field): " + string.Join(", ", found));
    }
}
