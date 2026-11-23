using System.Reflection;
using System.Reflection.Emit;
using SpireQoL.Procs.Programs;
using SpireQoL.Sim;
using SpireQoL.Sim.Models;

namespace SpireQoL.Diagnostics;

/// <summary>
/// Where each model takes its amounts, against its game class's hooks (docs/CARD_PROGRAMS.md B2; Vicious and Dark Embrace drew 1
/// where their hooks draw base.Amount). For each relic and power model ModelEffectCheck pairs with a game class: the amount of each
/// effect command its game class's hooks call (CardPileCmd.Draw's count, PlayerCmd.GainEnergy's, CreatureCmd.GainBlock's,
/// CreatureCmd.Damage's, PowerCmd.Apply&lt;T&gt;'s) and of each amount it binds to a play (amountsForPlayedCards), read back to
/// where it comes from (its Amount, one of its values by name, the bound amount, a constant, arithmetic over those), against the
/// same effect in the model's own code (Amount(...), PowerOn(ThisPower), Var(..., "Name"), TakeBound, a constant). A model whose
/// source differs is reported, unless it's reviewed here with why. ModelEffectTests runs it on the installed game (beta) and on
/// main's assembly.
/// </summary>
internal static class ModelAmountCheck
{
    /// <summary>An effect ("Draw", "Block", "Apply StrengthPower") and where its amount comes from ("Amount", "Var Cards", "1").</summary>
    internal readonly record struct EffectAmount(string Effect, string Source);

    /// <summary>A model's effect whose amounts don't read the same as its game class's.</summary>
    internal sealed record Mismatch(string Model, string Effect, IReadOnlyList<string> Game, IReadOnlyList<string> Simulated)
    {
        public override string ToString() => $"{Model} {Effect}: game {string.Join(" | ", Game)}; model {string.Join(" | ", Simulated)}";
    }

    /// <summary>
    /// What the check found: how many models it compared (an effect both sides have), the differences, the effects it couldn't
    /// decide (a source on one side it can't read back, so the other side's could be it), and reviewed entries nothing differs on
    /// any more.
    /// </summary>
    internal sealed record Report(int Compared, List<Mismatch> Differences, List<Mismatch> Unread, List<string> StaleReviews);

    /// <summary>
    /// Reviewed: a model's amounts that read differently from its game class's (the mismatch exactly as reported), and why they
    /// agree or can't matter. Any change to either side's sources reports it again; an entry that no longer differs fails the check
    /// (stale), so it goes once the reader reads it.
    /// </summary>
    internal static readonly Dictionary<string, string> Reviewed = new()
    {
        // The same total, a loop in the game.
        ["CentennialPuzzle Draw: game 1; model Var Cards"] = "the game draws one card Cards times (a for loop up to DynamicVars.Cards); the model draws Cards",
        ["SubroutinePower Energy: game 1; model Bound"] = "the game gains 1 energy as many times as the bound amount (a for loop); the model gives the bound amount, one energy event each",
        // After the turn the simulator predicts: nothing predicted reads them.
        ["InfernoPower Damage: game Amount | Var SelfDamage; model Amount"] =
            "its SelfDamage hits its owner at the start of their turn (AfterPlayerTurnStart): before anything predicted, the turn is under way",
        ["MonologuePower Apply StrengthPower: game -Var StrengthApplied | Bound; model Bound"] =
            "AfterSideTurnEnd takes back the Strength it gave this turn (-StrengthApplied): nothing predicted after your turn ends deals your powered damage",
        ["TenderPower Apply StrengthPower: game -1 | ?TenderPower.get_CardsPlayedThisTurn; model -1"] =
            "AfterSideTurnEnd gives back the Strength it took this turn (CardsPlayedThisTurn): nothing predicted after your turn ends deals your powered damage",
        ["TenderPower Apply DexterityPower: game -1 | ?TenderPower.get_CardsPlayedThisTurn; model -1"] =
            "AfterSideTurnEnd gives back the Dexterity it took this turn (CardsPlayedThisTurn): nothing predicted after your turn ends gains your powered block",
        // Each Instanced copy acts on its own amount: the game's copies are separate powers, each with its base.Amount.
        ["AutomationPower Energy: game Amount; model ?Enumerator.get_Current"] = "each copy's own amount (CounterPower.Copies), as each of the game's copies gives its base.Amount",
        ["CacophonyPower Damage: game Amount; model ?Enumerator.get_Current"] = "each copy's own amount (CounterPower.Copies), as each of the game's copies deals its base.Amount",
        ["PanachePower Damage: game Amount; model ?Enumerator.get_Current"] = "each copy's own amount (CounterPower.Copies), as each of the game's copies deals its base.Amount",
        ["OrbitPower Energy: game mul(Amount, sub(div(?field energySpent, 4), ?field triggerCount)); model ?Enumerator.get_Current"] =
            "each copy's own amount (CounterPower.Copies) each time its count of 4 energy spent runs out; the game gives base.Amount × the times it passed 4",
        // The same arithmetic, over something a hook's parameter or a count holds in the game and the simulation holds elsewhere.
        ["ChildOfTheStarsPower Block: game mul(Amount, ?parameter amount); model mul(Amount, ?)"] =
            "its amount × the stars spent: AfterStarsSpent's amount in the game, the play's Stars in the model",
        ["GalacticDust Block: game mul(?Mathf.FloorToInt, Var Block); model mul(div(add(Amount, ?), Var Stars), Var Block)"] =
            "floor((stars spent so far + this spend) / Stars) × Block in both: the spend is AfterStarsSpent's amount in the game, the play's Stars in the model",
        ["NecroMasteryPower Damage: game mul(-?parameter delta, Amount); model mul(?parameter lost, Amount)"] =
            "its amount × the HP Osty lost: AfterCurrentHpChanged's -delta in the game, AfterOstyHpLost's lost in the model",
        ["CloakClasp Block: game mul(?IReadOnlyCollection`1.get_Count, Var Block); model mul(?field HandCount, Var Block)"] =
            "Block × the cards in its owner's hand: the hand pile's count in the game, the player's HandCount in the model",
        ["ObsidianVice Damage: game mul(Var Damage, ?IReadOnlyCollection`1.get_Count); model mul(Var Damage, ?field HandCount)"] =
            "Damage × the cards in its owner's hand: the hand pile's count in the game, the player's HandCount in the model",
        ["MotifPower Draw: game mul(?local, Amount); model mul(?Enumerable.Count, Amount)"] =
            "its amount × the melodies (Wah, Cho, Ki) the card is the first this turn to play, counted from the turn's plays in both (Melodies.First)",
        ["DarkEmbracePower Draw: game Amount | mul(Amount, ?field etherealCount); model Amount"] =
            "its amount per card exhausted; the Ethereal ones exhausted as the turn ends draw together at AfterSideTurnEnd (amount × etherealCount), the same number",
        ["TheBombPower Damage: game Var Damage; model ?ProcModel.Data"] = "its Damage value, which the adapter reads into its Data (SimReaders.Powers)",
    };

    /// <summary>The check on the loaded game.</summary>
    public static Report Run() => Run(CoverageLedger.ModelTypes(), RuntimeCode.Read);

    /// <summary>The check on a branch's game classes, read with <paramref name="readGame"/> (the models are always the loaded mod's).</summary>
    public static Report Run(IEnumerable<Type> gameTypes, Func<MethodBase, List<CodeOp>> readGame)
    {
        var byName = gameTypes.GroupBy(type => type.Name).ToDictionary(group => group.Key, group => group.ToList());
        var compared = 0;
        var differences = new List<Mismatch>();
        var unread = new List<Mismatch>();
        var stale = Reviewed.Keys.ToHashSet();
        foreach (var name in SimModels.Names.OrderBy(modelName => modelName, StringComparer.Ordinal))
        {
            if (!byName.TryGetValue(name, out var types) || SimModels.Get(name) is not { } model) continue;
            var game = types.SelectMany(type => GameAmounts(ModelEffectCheck.HookMethods(type), readGame)).ToList();
            // A relic whose amount the adapter reads from one of its values (SimReaders.RelicAmountVars): its Amount is that value.
            var amountVar = types.Any(type => type.Namespace == "MegaCrit.Sts2.Core.Models.Relics") && SimReaders.RelicAmountVars.TryGetValue(name, out var read) ? "Var " + read.Var : null;
            var simulated = ModelAmounts(model).Select(amount => amountVar == null ? amount : amount with { Source = AmountWord.Replace(amount.Source, amountVar) }).ToList();
            var effects = game.Select(amount => amount.Effect).Intersect(simulated.Select(amount => amount.Effect)).OrderBy(effect => effect, StringComparer.Ordinal).ToList();
            if (effects.Count == 0) continue;
            compared++;
            foreach (var effect in effects)
            {
                var gameSources = Sources(game, effect);
                var modelSources = Sources(simulated, effect);
                var gameOnly = gameSources.Except(modelSources).ToList();
                var modelOnly = modelSources.Except(gameSources).ToList();
                if (gameOnly.Count == 0 && modelOnly.Count == 0) continue;
                var mismatch = new Mismatch(name, effect, gameSources, modelSources);
                if (Reviewed.ContainsKey(mismatch.ToString()))
                {
                    stale.Remove(mismatch.ToString());
                    continue;
                }
                // Certain only when nothing unread on the other side could be the source one side has.
                var certain = gameOnly.Any(Known) && modelOnly.All(Known) || modelOnly.Any(Known) && gameOnly.All(Known);
                (certain ? differences : unread).Add(mismatch);
            }
        }
        return new Report(compared, differences, unread, stale.OrderBy(key => key, StringComparer.Ordinal).ToList());
    }

    private static readonly System.Text.RegularExpressions.Regex AmountWord = new(@"\bAmount\b", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static List<string> Sources(IEnumerable<EffectAmount> amounts, string effect) =>
        amounts.Where(amount => amount.Effect == effect).Select(amount => amount.Source).Distinct().OrderBy(source => source, StringComparer.Ordinal).ToList();

    /// <summary>A source read all the way back (no "?" part).</summary>
    private static bool Known(string source) => !source.Contains('?');


    // ------------------------------------------------------------------ the game's side

    /// <summary>
    /// The effect commands a game class's hooks call, and the amounts they bind to a play, each with where its amount comes from (a
    /// hook's parameter stays unread: the model's own is named differently).
    /// </summary>
    private static IEnumerable<EffectAmount> GameAmounts(IEnumerable<MethodBase> hooks, Func<MethodBase, List<CodeOp>> read)
    {
        var set = new CodeSet(followsCalls: false);
        foreach (var method in hooks)
        {
            try { set.Add(read(method), method); } catch { /* unreadable: nothing it calls */ }
        }
        foreach (var code in set.Codes)
            foreach (var (at, member) in code.Calls())
            {
                // amountsForPlayedCards.Add(card, amount): the amount bound to the play at BeforeCardPlayed.
                if (code.IsBoundAmounts(member, at, "Add")) yield return new EffectAmount("Bind", code.Describe(code.Argument(at, member, parameter: 1)));
                if (GameEffect(member) is not { } effect) continue;
                var parameter = member.ParameterTypes.ToList().FindIndex(type => type is "Decimal" or "BlockVar" or "DamageVar");
                // CardPileCmd.Draw(choiceContext, player): one card.
                if (parameter < 0) yield return new EffectAmount(effect, effect == "Draw" ? "1" : "?no amount");
                else yield return new EffectAmount(effect, code.Describe(code.Argument(at, member, parameter)));
            }
    }

    /// <summary>The effect a game command has, or null for one this check doesn't compare.</summary>
    private static string? GameEffect(CodeMember member)
    {
        if (member.Namespace != "MegaCrit.Sts2.Core.Commands") return null;
        return (member.DeclaringType, member.Name) switch
        {
            ("CardPileCmd", "Draw" or "DrawWithoutBlockingOnOtherPlayers") => "Draw",
            ("PlayerCmd", "GainEnergy") => "Energy",
            // Not PlayerCmd.GainStars: the simulation counts star gains (CardEvent.StarsGained: one per GainStars, as AfterStarsGained
            // runs once per gain), not stars, so a gain's amount isn't an amount there.
            ("CreatureCmd", "GainBlock") => "Block",
            ("CreatureCmd", "Damage") => "Damage",
            // PowerCmd.Apply(choiceContext, power, ...) applies a power its code made: which one isn't in the call.
            ("PowerCmd", "Apply") when member.GenericArguments.Count == 1 => "Apply " + member.GenericArguments[0],
            _ => null,
        };
    }

    // ------------------------------------------------------------------ the model's side

    /// <summary>
    /// The effects the model's own code calls (ProcModel's helpers, SimEngine's) and the amounts it binds (ProcModel.Bind), each
    /// with where its amount comes from. A parameter of the model's own method is read back at the model's own calls to it (a
    /// template's Act(..., amount)), and a call to its own method at what that returns (BoundAmount).
    /// </summary>
    private static List<EffectAmount> ModelAmounts(SimModel model)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var set = new CodeSet(followsCalls: true);
        foreach (var method in ModelEffectCheck.OwnTypes(model.GetType()).SelectMany(type => type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all))))
        {
            if (method.GetMethodBody() == null) continue; // abstract: nothing it calls or returns
            try { set.Add(RuntimeCode.Read(method), method); } catch { /* unreadable: nothing it calls */ }
        }
        return set.Codes.SelectMany(code => code.Calls().Select(call => ModelEffect(code, call.At, call.Member, model.Name))).OfType<EffectAmount>().ToList();
    }

    /// <summary>The effect a call in a model's code has, with its amount's source; null for a call that isn't an effect compared here.</summary>
    private static EffectAmount? ModelEffect(Code code, int at, CodeMember member, string modelName)
    {
        if (member.DeclaringType is not (nameof(SimEngine) or nameof(ProcModel) or nameof(RelicProc))) return null;
        var names = member.ParameterNames.ToList();
        string? effect = member.Name switch
        {
            "Cause" or nameof(SimEngine.ProcCardEvent) or nameof(SimEngine.Raise) => code.Constant(code.Argument(at, member, names.IndexOf("cardEvent"))) switch
            {
                (int)CardEvent.Drawn => "Draw",
                (int)CardEvent.EnergyGained => "Energy",
                _ => null,
            },
            "Block" or "RelicBlock" or nameof(SimEngine.ProcBlock) or nameof(SimEngine.ProcBlockFor) => "Block",
            "DamageAll" or "DamageRandom" or "RelicDamageAll" or "RelicDamageRandom" or nameof(SimEngine.ProcDamageAll) or nameof(SimEngine.ProcDamageRandom)
                or nameof(SimEngine.ProcDamageOn) or nameof(SimEngine.ProcDamagePlayer) or nameof(SimEngine.Damage) or nameof(SimEngine.DamageAllEnemies) => "Damage",
            nameof(SimEngine.ApplyPower) => code.PowerName(code.Argument(at, member, names.IndexOf("power")), modelName) is { } power ? "Apply " + power : null,
            "Bind" => "Bind",
            _ => null,
        };
        if (effect == null) return null;
        // The relic helpers take their value by name (RelicProc.RelicBlock: Var "Block", RelicDamageAll: Var "Damage").
        if (member.Name == "RelicBlock") return new EffectAmount(effect, "Var Block");
        if (member.Name is "RelicDamageAll" or "RelicDamageRandom") return new EffectAmount(effect, "Var Damage");
        var parameter = names.FindIndex(name => name is "amount" or "count" or "baseBlock" or "baseDamage");
        return new EffectAmount(effect, parameter < 0 ? "?no amount" : code.Describe(code.Argument(at, member, parameter)));
    }

    // ------------------------------------------------------------------ reading a value back

    /// <summary>The key a DynamicVarSet property reads (DynamicVars.Strength: "StrengthPower"), from the loaded game's code.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> VarKeys = new();

    private static string VarKey(string property) => VarKeys.GetOrAdd(property, name =>
    {
        try
        {
            var getter = typeof(MegaCrit.Sts2.Core.Localization.DynamicVars.DynamicVarSet).GetProperty(name)?.GetMethod;
            return getter == null ? name : RuntimeCode.Read(getter).Select(instruction => instruction.Text).FirstOrDefault(text => text != null) ?? name;
        }
        catch
        {
            return name;
        }
    });

    /// <summary>
    /// The methods read together (a game class's hooks, or a model's own code): a local one of them captures for a lambda is read
    /// where it's stored; on the model's side, a parameter is read at the calls to its method and a call at what its method returns.
    /// </summary>
    private sealed class CodeSet
    {
        public readonly List<Code> Codes = new();
        private readonly bool _followsCalls;

        public CodeSet(bool followsCalls) => _followsCalls = followsCalls;

        public void Add(List<CodeOp> ops, MethodBase method) => Codes.Add(new Code(ops, method, this));

        /// <summary>A parameter of a method in the set: what every call to it in the set gives, when they agree.</summary>
        public string? ParameterSource(MethodBase method, int parameter, int depth)
        {
            if (!_followsCalls) return null;
            var sources = new HashSet<string>();
            foreach (var caller in Codes)
                foreach (var (at, member) in caller.CallsTo(method.Name, method.GetParameters().Length))
                    sources.Add(caller.Describe(caller.Argument(at, member, parameter), depth + 1));
            return sources.Count == 1 ? sources.First() : null;
        }

        /// <summary>What a call to a method in the set returns: the most derived one's (a model's own types come first).</summary>
        public string? ReturnSource(CodeMember member, int depth) =>
            _followsCalls ? Codes.FirstOrDefault(code => code.MethodName == member.Name && code.ParameterCount == member.ParameterCount)?.Returns(depth) : null;

        /// <summary>A local a lambda captured (a display class's field): what's stored in it, when every store agrees.</summary>
        public string? CapturedSource(CodeMember field, int depth)
        {
            var sources = new HashSet<string>();
            foreach (var code in Codes) sources.UnionWith(code.Stores(field, depth));
            return sources.Count == 1 ? sources.First() : null;
        }
    }

    /// <summary>A method's instructions, and where a value on its stack comes from, described the same way on both sides.</summary>
    private sealed class Code
    {
        private const int MaxDepth = 8;
        private readonly List<CodeOp> _ops;
        private readonly MethodBase _method;
        private readonly IReadOnlySet<int> _landings;
        private readonly CodeSet _set;

        public Code(List<CodeOp> ops, MethodBase method, CodeSet set)
        {
            _ops = ops;
            _method = method;
            _landings = CodeDecoder.Landings(ops);
            _set = set;
        }

        public string MethodName => _method.Name;
        public int ParameterCount => _method.GetParameters().Length;

        /// <summary>What this method stores in a field.</summary>
        public IEnumerable<string> Stores(CodeMember field, int depth) =>
            Enumerable.Range(0, _ops.Count).Where(index => _ops[index].OpCode == OpCodes.Stfld && _ops[index].Member is { } stored
                    && stored.Name == field.Name && stored.DeclaringType == field.DeclaringType)
                .Select(index => Describe(Operand(index, 0), depth + 1)).ToList();

        /// <summary>What the method returns, when every return agrees.</summary>
        public string? Returns(int depth)
        {
            var sources = Enumerable.Range(0, _ops.Count).Where(index => _ops[index].OpCode == OpCodes.Ret)
                .Select(index => Describe(Operand(index, 0), depth + 1)).Distinct().ToList();
            return sources.Count == 1 ? sources[0] : null;
        }

        /// <summary>Every call in the method, with its position.</summary>
        public IEnumerable<(int At, CodeMember Member)> Calls()
        {
            for (var i = 0; i < _ops.Count; i++)
                if (_ops[i].IsCall && _ops[i].Member is { Kind: CodeMemberKind.Method } member) yield return (i, member);
        }

        /// <summary>The calls to a method by name and parameter count (an override's base declaration included).</summary>
        public IEnumerable<(int At, CodeMember Member)> CallsTo(string name, int parameterCount) =>
            Calls().Where(call => call.Member.Name == name && call.Member.ParameterCount == parameterCount);

        /// <summary>The instruction that leaves argument <paramref name="parameter"/> of the call at <paramref name="callAt"/>.</summary>
        public int? Argument(int callAt, CodeMember called, int parameter) =>
            parameter < 0 ? null : CodeDecoder.Producer(_ops, callAt, CodeDecoder.ArgumentDepth(called, parameter), landings: _landings);

        private int? Operand(int at, int depth) => CodeDecoder.Producer(_ops, at, depth, landings: _landings);

        public long? Constant(int? index) => index is { } at ? _ops[at].Integer : null;

        /// <summary>
        /// The call at <paramref name="at"/> is <paramref name="name"/> on a power's amountsForPlayedCards (the amounts the game binds
        /// to a play at BeforeCardPlayed: Add, then Remove at AfterCardPlayed).
        /// </summary>
        public bool IsBoundAmounts(CodeMember member, int at, string name) =>
            member.Name == name && member.DeclaringType.StartsWith("Dictionary", StringComparison.Ordinal)
            && Operand(at, member.ParameterCount) is { } instance && _ops[instance].Member is { Name: "amountsForPlayedCards" };

        /// <summary>The game's name of the power a PowerId argument names (PowerId.Strength: "StrengthPower"; ThisPower: the model's).</summary>
        public string? PowerName(int? index, string modelName)
        {
            if (index is not { } at || _ops[at].Member is not { } member) return null;
            if (member.Name == "get_ThisPower") return modelName;
            if (member is { Kind: CodeMemberKind.Field, DeclaringType: nameof(PowerId) }
                && typeof(PowerId).GetField(member.Name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is PowerId power) return power.Name;
            return null;
        }

        /// <summary>
        /// Where the value the instruction at <paramref name="index"/> leaves comes from: "Amount" (a power's stacks, a relic's
        /// counter), "Var Name" (one of its DynamicVars), "Bound" (the amount bound to the play), a constant, arithmetic over those
        /// ("mul(Amount, 2)"); a part it can't read back starts with "?" ("?parameter amount").
        /// </summary>
        public string Describe(int? index, int depth = 0)
        {
            if (index is not { } at || depth > MaxDepth) return "?";
            var instruction = _ops[at];
            var opCode = instruction.OpCode;
            var name = opCode.Name ?? "";
            if (instruction.Integer is { } constant) return constant.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (name.StartsWith("conv.", StringComparison.Ordinal) || opCode == OpCodes.Castclass || opCode == OpCodes.Unbox_Any || opCode == OpCodes.Box)
                return Describe(Operand(at, 0), depth + 1);
            if (opCode == OpCodes.Neg) return Negated(Describe(Operand(at, 0), depth + 1));
            if (Arithmetic(name) is { } operation) return $"{operation}({Describe(Operand(at, 1), depth + 1)}, {Describe(Operand(at, 0), depth + 1)})";
            // ldloc and ldloca (a Nullable's value is read through its address).
            if (name.StartsWith("ldloc", StringComparison.Ordinal) && instruction.Slot is { } slot) return Local(at, slot, depth);
            if (name.StartsWith("ldarg", StringComparison.Ordinal) && instruction.Slot is { } argument) return Parameter(argument, depth);
            if (instruction.Member is not { } member) return "?" + name;
            if (member.Kind == CodeMemberKind.Field) return Field(at, member, depth);
            if (member.Kind != CodeMemberKind.Method) return "?" + member.Name;
            return Call(at, member, depth);
        }

        private static string Negated(string value) => value.StartsWith('-') ? value[1..] : "-" + value;

        private static string? Arithmetic(string opCodeName) => opCodeName switch
        {
            "add" or "op_Addition" => "add",
            "sub" or "op_Subtraction" => "sub",
            "mul" or "op_Multiply" => "mul",
            "div" or "op_Division" => "div",
            _ => null,
        };

        private string Call(int at, CodeMember member, int depth)
        {
            // Conversions (int to decimal), a Nullable's value, and decimal arithmetic.
            if (member.Name is "op_Implicit" or "op_Explicit" || _ops[at].OpCode == OpCodes.Newobj && member.DeclaringType == "Decimal" && member.ParameterCount == 1
                || member.DeclaringType.StartsWith("Nullable", StringComparison.Ordinal) && member.Name is "GetValueOrDefault" or "get_Value")
                return Describe(Operand(at, 0), depth + 1);
            if (member.DeclaringType == "Decimal" && member.Name == "op_UnaryNegation") return Negated(Describe(Operand(at, 0), depth + 1));
            if (member.DeclaringType == "Decimal" && Arithmetic(member.Name) is { } operation)
                return $"{operation}({Describe(Operand(at, 1), depth + 1)}, {Describe(Operand(at, 0), depth + 1)})";
            // The game's: base.Amount, base.DynamicVars.Name(.BaseValue / .IntValue), base.DynamicVars["Name"].
            if (member.Name == "get_Amount") return "Amount";
            if (member.Name is "get_BaseValue" or "get_IntValue") return VarName(Operand(at, 0)) ?? "?" + member.Name;
            if (member.DeclaringType == "DynamicVarSet") return VarName(at) ?? "?" + member.Name;
            // The model's: Amount(state, owner), PowerOn(state, owner, ThisPower), RelicsOf(...)[ThisRelic], Var(..., "Name", ...),
            // TakeBound(state, owner).
            if (member.Namespace.StartsWith("SpireQoL", StringComparison.Ordinal))
            {
                var names = member.ParameterNames.ToList();
                if (member.Name == "Amount") return "Amount";
                if (member.Name == "TakeBound") return "Bound";
                if (member.Name == nameof(SimEngine.PowerOn))
                    return Argument(at, member, names.IndexOf("power")) is { } power && _ops[power].Member is { Name: "get_ThisPower" } ? "Amount" : "?PowerOn";
                if (member.Name == "get_Item" && Operand(at, 0) is { } key && _ops[key].Member is { Name: "get_ThisPower" or "get_ThisRelic" }) return "Amount";
                if (member.Name == nameof(SimEngine.Var) && Argument(at, member, names.IndexOf("var")) is { } varName && _ops[varName].Text is { } text)
                    return "Var " + text;
                if (_set.ReturnSource(member, depth) is { } returned) return returned;
            }
            return "?" + member.DeclaringType + "." + member.Name;
        }

        /// <summary>The DynamicVars entry the instruction at <paramref name="index"/> gets (DynamicVarSet.Block, DynamicVarSet["Name"]).</summary>
        private string? VarName(int? index)
        {
            index = Origin(index);
            if (index is not { } found || _ops[found].Member is not { DeclaringType: "DynamicVarSet" } member) return null;
            if (member.Name == "get_Item") return Operand(found, 0) is { } key && _ops[key].Text is { } text ? "Var " + text : null;
            return member.Name.StartsWith("get_", StringComparison.Ordinal) ? "Var " + VarKey(member.Name[4..]) : null;
        }

        /// <summary>
        /// The instruction that made a value, through casts and through locals stored once (DynamicVars.Damage held in a local, then
        /// its BaseValue).
        /// </summary>
        private int? Origin(int? index)
        {
            for (var step = 0; step < MaxDepth && index is { } at; step++)
            {
                var instruction = _ops[at];
                var name = instruction.OpCode.Name!;
                int? next = null;
                if (instruction.OpCode == OpCodes.Castclass) next = Operand(at, 0);
                else if (name.StartsWith("ldloc", StringComparison.Ordinal) && !name.StartsWith("ldloca", StringComparison.Ordinal) && instruction.Slot is { } slot)
                {
                    var stores = Enumerable.Range(0, _ops.Count).Where(index => _ops[index].OpCode.Name!.StartsWith("stloc", StringComparison.Ordinal) && _ops[index].Slot == slot).ToList();
                    if (stores.Count == 1) next = Operand(stores[0], 0);
                }
                else if (instruction.OpCode == OpCodes.Ldfld && instruction.Member is { } field && field.Name.StartsWith('<') && field.Name.Contains(">5__", StringComparison.Ordinal))
                {
                    var stores = Enumerable.Range(0, _ops.Count).Where(index => _ops[index].OpCode == OpCodes.Stfld && _ops[index].Member?.Name == field.Name).ToList();
                    if (stores.Count == 1) next = Operand(stores[0], 0);
                }
                if (next == null) return index;
                index = next;
            }
            return index;
        }

        /// <summary>
        /// A local: the value last stored in it, when no jump lands between that store and this load; "Bound" when it's the out
        /// argument of amountsForPlayedCards.Remove.
        /// </summary>
        private string Local(int at, int slot, int depth)
        {
            bool Stores(int index) => _ops[index].OpCode.Name!.StartsWith("stloc", StringComparison.Ordinal) && _ops[index].Slot == slot;
            bool OutStore(int index) => _ops[index].OpCode.Name!.StartsWith("ldloca", StringComparison.Ordinal) && _ops[index].Slot == slot && OutOfBoundAmounts(index);
            // Stored once in the whole method: that value, whatever the way here.
            var stores = Enumerable.Range(0, _ops.Count).Where(index => Stores(index) || OutStore(index)).ToList();
            if (stores.Count == 1) return Stores(stores[0]) ? Describe(Operand(stores[0], 0), depth + 1) : "Bound";
            for (var i = at - 1; i >= 0; i--)
            {
                if (_landings.Contains(_ops[i + 1].Offset)) return "?local";
                var name = _ops[i].OpCode.Name!;
                if (name.StartsWith("stloc", StringComparison.Ordinal) && _ops[i].Slot == slot) return Describe(Operand(i, 0), depth + 1);
                if (name.StartsWith("ldloca", StringComparison.Ordinal) && _ops[i].Slot == slot && OutOfBoundAmounts(i)) return "Bound";
            }
            return "?local";
        }

        /// <summary>The address at <paramref name="at"/> is the out argument of the next call, amountsForPlayedCards.Remove.</summary>
        private bool OutOfBoundAmounts(int at)
        {
            for (var i = at + 1; i < _ops.Count; i++)
                if (_ops[i].IsCall && _ops[i].Member is { Kind: CodeMemberKind.Method } member)
                    return IsBoundAmounts(member, i, "Remove") && Argument(i, member, member.ParameterCount - 1) == at;
            return false;
        }

        /// <summary>
        /// A field: a hoisted local of an async method (&lt;name&gt;5__N) stored once is the value stored (or "Bound", the out
        /// argument of amountsForPlayedCards.Remove); a hook's parameter (a state machine's field with the parameter's name) or another
        /// field isn't read further.
        /// </summary>
        private string Field(int at, CodeMember member, int depth)
        {
            if (member.DeclaringType == "Decimal") return member.Name switch { "One" => "1", "Zero" => "0", "MinusOne" => "-1", _ => "?Decimal." + member.Name };
            if (_ops[at].OpCode != OpCodes.Ldfld) return "?field " + member.Name;
            if (member.Name.StartsWith('<') && member.Name.Contains(">5__", StringComparison.Ordinal))
            {
                var stores = Enumerable.Range(0, _ops.Count).Where(index => _ops[index].OpCode == OpCodes.Stfld && _ops[index].Member?.Name == member.Name).ToList();
                if (stores.Count == 1) return Describe(Operand(stores[0], 0), depth + 1);
                var addresses = Enumerable.Range(0, _ops.Count).Where(index => _ops[index].OpCode == OpCodes.Ldflda && _ops[index].Member?.Name == member.Name).ToList();
                return stores.Count == 0 && addresses.Count == 1 && OutOfBoundAmounts(addresses[0]) ? "Bound" : "?local";
            }
            // A local a lambda captured (<>c__DisplayClass's field): where it's stored.
            if (member.DeclaringType.Contains("DisplayClass", StringComparison.Ordinal)) return _set.CapturedSource(member, depth) ?? "?captured " + member.Name;
            if (member.DeclaringType.StartsWith('<') && !member.Name.StartsWith('<')) return "?parameter " + member.Name;
            return "?field " + member.Name;
        }

        /// <summary>A parameter: where the method's callers in the model take it from, when they all agree; else its name.</summary>
        private string Parameter(int slot, int depth)
        {
            var index = _method.IsStatic ? slot : slot - 1;
            var parameters = _method.GetParameters();
            if (index < 0 || index >= parameters.Length) return "?this";
            return _set.ParameterSource(_method, index, depth) ?? "?parameter " + parameters[index].Name;
        }
    }
}
