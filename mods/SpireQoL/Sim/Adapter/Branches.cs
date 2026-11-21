using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace SpireQoL.Sim;

/// <summary>
/// Which instructions of a method run on every way through it (decision #109): a move's PowerCmd.Apply inside an if, a switch or a
/// loop lands only on some branches. Read from the compiled code (Harmony's instructions): an instruction is on every path when the
/// method can't finish normally without passing it.
///
/// An async method compiles to a state machine's MoveNext: it starts by jumping on its state to where the last await left off, and
/// each await either continues inline (IsCompleted) or suspends and resumes there later. Both reach the same code, so the jumps on the
/// state are dropped (only the first run is followed) and a suspension counts as leaving: the inline path stands for both. "Finishing"
/// is the builder's SetResult (an exception's catch never reaches it); for a plain method, a ret.
/// </summary>
internal static class Branches
{
    /// <summary>For each of <paramref name="at"/> (instruction indexes): whether every normal way through the method passes it.</summary>
    public static bool[] OnEveryPath(List<CodeInstruction> code, IReadOnlyList<int> at)
    {
        var succ = Successors(code);
        var exits = Enumerable.Range(0, code.Count).Where(index => code[index].operand is MethodInfo { Name: "SetResult" }).ToList();
        if (exits.Count == 0) exits = Enumerable.Range(0, code.Count).Where(index => code[index].opcode == OpCodes.Ret).ToList();
        var result = new bool[at.Count];
        for (var point = 0; point < at.Count; point++) result[point] = exits.Count > 0 && !Reaches(succ, 0, exits, at[point]);
        return result;
    }

    /// <summary>Whether some exit is reachable from <paramref name="start"/> without passing <paramref name="avoid"/>.</summary>
    private static bool Reaches(List<int>[] succ, int start, List<int> exits, int avoid)
    {
        if (start == avoid) return false;
        var seen = new bool[succ.Length];
        var stack = new Stack<int>();
        stack.Push(start);
        seen[start] = true;
        while (stack.Count > 0)
        {
            var pop = stack.Pop();
            if (exits.Contains(pop)) return true;
            foreach (var successor in succ[pop])
            {
                if (successor == avoid || successor < 0 || successor >= succ.Length || seen[successor]) continue;
                seen[successor] = true;
                stack.Push(successor);
            }
        }
        return false;
    }

    private static List<int>[] Successors(List<CodeInstruction> code)
    {
        var labels = new Dictionary<Label, int>();
        for (var i = 0; i < code.Count; i++)
            foreach (var label in code[i].labels) labels[label] = i;
        var stateLocal = StateLocal(code);
        var succ = new List<int>[code.Count];
        for (var i = 0; i < code.Count; i++)
        {
            var ins = code[i];
            var next = new List<int>();
            var flow = ins.opcode.FlowControl;
            var dispatch = stateLocal != null && flow == FlowControl.Cond_Branch && LoadsState(code, i, stateLocal);
            switch (flow)
            {
                case FlowControl.Return:
                case FlowControl.Throw:
                    break;
                case FlowControl.Branch:
                    // leave/br: to the target. A suspension's leave goes to the method's end, where nothing more runs: no exit there.
                    if (ins.operand is Label label && labels.TryGetValue(label, out var to)) next.Add(to);
                    break;
                case FlowControl.Cond_Branch:
                    if (i + 1 < code.Count) next.Add(i + 1);
                    if (dispatch) break; // the jump on the state: only the first run is followed
                    if (ins.operand is Label target && labels.TryGetValue(target, out var nextLabel)) next.Add(nextLabel);
                    else if (ins.operand is Label[] many)
                        foreach (var otherLabel in many) if (labels.TryGetValue(otherLabel, out var labelTarget)) next.Add(labelTarget);
                    break;
                default:
                    if (i + 1 < code.Count) next.Add(i + 1);
                    break;
            }
            succ[i] = next;
        }
        return succ;
    }

    /// <summary>The local a state machine copies its state into first (ldarg.0; ldfld &lt;&gt;1__state; stloc), or null.</summary>
    private static object? StateLocal(List<CodeInstruction> code)
    {
        for (var i = 1; i + 1 < code.Count && i < 12; i++)
            if (code[i].operand is FieldInfo { Name: "<>1__state" } && code[i].opcode == OpCodes.Ldfld && IsStloc(code[i + 1]))
                return LocalOf(code[i + 1]);
        return null;
    }

    /// <summary>The branch at <paramref name="index"/> decides on the state local (loaded in the few instructions before it).</summary>
    private static bool LoadsState(List<CodeInstruction> code, int index, object state)
    {
        for (var j = index - 1; j >= Math.Max(0, index - 3); j--)
            if (IsLdloc(code[j]) && Equals(LocalOf(code[j]), state)) return true;
        return false;
    }

    private static bool IsStloc(CodeInstruction codeInstruction) => codeInstruction.opcode == OpCodes.Stloc_0 || codeInstruction.opcode == OpCodes.Stloc_1 || codeInstruction.opcode == OpCodes.Stloc_2
        || codeInstruction.opcode == OpCodes.Stloc_3 || codeInstruction.opcode == OpCodes.Stloc_S || codeInstruction.opcode == OpCodes.Stloc;

    private static bool IsLdloc(CodeInstruction codeInstruction) => codeInstruction.opcode == OpCodes.Ldloc_0 || codeInstruction.opcode == OpCodes.Ldloc_1 || codeInstruction.opcode == OpCodes.Ldloc_2
        || codeInstruction.opcode == OpCodes.Ldloc_3 || codeInstruction.opcode == OpCodes.Ldloc_S || codeInstruction.opcode == OpCodes.Ldloc;

    /// <summary>A local's identity: its index for the short forms, its LocalBuilder (or index) otherwise.</summary>
    private static object? LocalOf(CodeInstruction codeInstruction)
    {
        if (codeInstruction.opcode == OpCodes.Ldloc_0 || codeInstruction.opcode == OpCodes.Stloc_0) return 0;
        if (codeInstruction.opcode == OpCodes.Ldloc_1 || codeInstruction.opcode == OpCodes.Stloc_1) return 1;
        if (codeInstruction.opcode == OpCodes.Ldloc_2 || codeInstruction.opcode == OpCodes.Stloc_2) return 2;
        if (codeInstruction.opcode == OpCodes.Ldloc_3 || codeInstruction.opcode == OpCodes.Stloc_3) return 3;
        return codeInstruction.operand switch { LocalBuilder localBuilder => localBuilder.LocalIndex, null => null, var other => other };
    }
}
