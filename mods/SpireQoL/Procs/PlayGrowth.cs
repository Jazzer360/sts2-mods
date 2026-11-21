using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Models;

namespace SpireQoL.Procs;

/// <summary>
/// What a card's own numbers gain with each play of it (decision #160), so a replay (Echo Form, Burst) has the numbers the game
/// gives it: read from the card's code and its enchantment's, not from a list. The card's play adding its Increase value to its own
/// damage after the attack (Rampage, The Ball; Claw and Maul through their helper): that much more damage each play. Its
/// enchantment raising its own number when the card is played (Goopy's Amount: block; Momentum's extra damage by its Amount): that
/// much more each play. A play that changes its own numbers some other way (Thrash: the exhausted card's damage) can't be told,
/// so a replay of it is not modelled (Unmodelled).
/// </summary>
internal static class PlayGrowth
{
    public sealed record Growth(decimal Damage, decimal Block, string? Unmodelled)
    {
        public static readonly Growth None = new(0m, 0m, null);
    }

    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>How the card's own play changes its numbers.</summary>
    private enum CardGrowth { None, DamageByIncrease, Unreadable }

    /// <summary>How an enchantment's play hooks change the number it adds: by a constant, by its Amount, or a way that can't be read.</summary>
    private sealed record EnchantGrowth(bool Damage, bool Block, int? ByConstant, bool ByAmount, bool Unreadable);

    private static readonly ConcurrentDictionary<Type, CardGrowth> CardCache = new();
    private static readonly ConcurrentDictionary<Type, EnchantGrowth?> EnchantCache = new();

    public static Growth Of(CardModel card)
    {
        decimal damage = 0m, block = 0m;
        string? unmodelled = null;
        switch (CardCache.GetOrAdd(card.GetType(), ReadCard))
        {
            case CardGrowth.DamageByIncrease:
                damage += card.DynamicVars.TryGetValue("Increase", out var increase) ? increase.BaseValue : 0m;
                break;
            case CardGrowth.Unreadable:
                unmodelled = "its damage or block changes with each play";
                break;
        }
        if (card.Enchantment is { } enchantment && EnchantCache.GetOrAdd(enchantment.GetType(), ReadEnchantment) is { } grows)
        {
            if (grows.Unreadable) unmodelled ??= $"its enchantment ({enchantment.GetType().Name}) changes its numbers with each play";
            else
            {
                var each = grows.ByConstant ?? (grows.ByAmount ? enchantment.Amount : 0);
                if (grows.Damage) damage += each;
                if (grows.Block) block += each;
            }
        }
        return damage == 0m && block == 0m && unmodelled == null ? Growth.None : new Growth(damage, block, unmodelled);
    }

    /// <summary>
    /// The card's play, and the card's own methods it calls (Claw's BuffFromClawPlay): a DynamicVar's BaseValue set with its Damage
    /// read and the "Increase" value loaded, after its attack, is growth by Increase; any other BaseValue set on its damage or block
    /// can't be read.
    /// </summary>
    private static CardGrowth ReadCard(Type cardType)
    {
        try
        {
            bool setsBase = false, damage = false, blockVar = false, increase = false, afterAttack = true;
            foreach (var method in CardCode.OnPlayMethods(cardType))
            {
                var instructions = SpireQoL.Diagnostics.GameCode.Instructions(method);
                var lastAttack = instructions.FindLastIndex(instruction => instruction.operand is MethodInfo { Name: "Execute" } called && called.DeclaringType?.Name == "AttackCommand");
                for (var i = 0; i < instructions.Count; i++)
                {
                    var instruction = instructions[i];
                    if (instruction.opcode == OpCodes.Ldstr && instruction.operand as string == "Increase") increase = true;
                    if (instruction.operand is not MethodInfo called) continue;
                    if (called.Name == "get_Damage") damage = true;
                    if (called.Name == "get_Block") blockVar = true;
                    var writes = called.Name == "set_BaseValue" || (called.DeclaringType == cardType && SetsBaseValue(called));
                    if (!writes) continue;
                    setsBase = true;
                    if (i < lastAttack) afterAttack = false;
                }
            }
            if (!setsBase || !(damage || blockVar)) return CardGrowth.None;
            return increase && damage && !blockVar && afterAttack ? CardGrowth.DamageByIncrease : CardGrowth.Unreadable;
        }
        catch
        {
            return CardGrowth.None; // unreadable code: nothing found
        }
    }

    private static bool SetsBaseValue(MethodInfo method)
    {
        try
        {
            return SpireQoL.Diagnostics.GameCode.Instructions(method).Any(instruction => instruction.operand is MethodInfo { Name: "set_BaseValue" });
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The enchantment's OnPlay and AfterCardPlayed (with their state machines) setting one of its own properties to itself plus
    /// a constant or its Amount (Goopy's Amount++, Momentum's ExtraDamage += Amount), when it adds to the card's damage or block
    /// (EnchantDamageAdditive, EnchantBlockAdditive); one that also multiplies, or sets them another way, can't be read.
    /// </summary>
    private static EnchantGrowth? ReadEnchantment(Type enchantmentType)
    {
        try
        {
            var damage = Overrides(enchantmentType, nameof(EnchantmentModel.EnchantDamageAdditive));
            var block = Overrides(enchantmentType, nameof(EnchantmentModel.EnchantBlockAdditive));
            var multiplies = Overrides(enchantmentType, nameof(EnchantmentModel.EnchantDamageMultiplicative)) || Overrides(enchantmentType, nameof(EnchantmentModel.EnchantBlockMultiplicative));
            if (!damage && !block) return null;
            var increments = new List<(int? Constant, bool Amount)>();
            foreach (var method in PlayHooks(enchantmentType))
            {
                var instructions = SpireQoL.Diagnostics.GameCode.Instructions(method);
                for (var i = 0; i < instructions.Count; i++)
                {
                    if (instructions[i].operand is not MethodInfo { Name: var setter } called || !setter.StartsWith("set_", StringComparison.Ordinal)
                        || called.DeclaringType == null || !called.DeclaringType.IsAssignableFrom(enchantmentType)) continue;
                    var addend = i >= 2 && instructions[i - 1].opcode == OpCodes.Add ? instructions[i - 2] : null;
                    if (addend != null && ConstantOf(addend) is { } constant) increments.Add((constant, false));
                    else if (addend?.operand is MethodInfo { Name: "get_Amount" }) increments.Add((null, true));
                    else increments.Add((null, false));
                }
            }
            if (increments.Count == 0) return null;
            var first = increments[0];
            var readable = !multiplies && (first.Constant != null || first.Amount) && increments.All(increment => increment == first);
            return new EnchantGrowth(damage, block, first.Constant, first.Amount, Unreadable: !readable);
        }
        catch
        {
            return null; // unreadable code: nothing found
        }
    }

    private static bool Overrides(Type type, string name) =>
        type.GetMethods(All).Any(method => method.Name == name && method.GetBaseDefinition().DeclaringType != type);

    private static IEnumerable<MethodBase> PlayHooks(Type type)
    {
        string[] names = { nameof(EnchantmentModel.OnPlay), nameof(EnchantmentModel.AfterCardPlayed) };
        return type.GetMethods(All).Where(method => names.Contains(method.Name)).Cast<MethodBase>()
            .Concat(CardCode.NestedTypes(type)
                .Where(nested => names.Any(name => nested.Name.Contains("<" + name + ">")) && typeof(IAsyncStateMachine).IsAssignableFrom(nested))
                .Select(nested => nested.GetMethod("MoveNext", All))
                .Where(method => method != null)
                .Cast<MethodBase>());
    }

    /// <summary>What a card class's play reads as (the card scan's check): None, DamageByIncrease or Unreadable.</summary>
    internal static string CardRead(Type cardType) => CardCache.GetOrAdd(cardType, ReadCard).ToString();

    /// <summary>What an enchantment class's play hooks read as (the scan's check): null when its numbers don't change with a play.</summary>
    internal static string? EnchantmentRead(Type enchantmentType) => EnchantCache.GetOrAdd(enchantmentType, ReadEnchantment) is { } grows
        ? (grows.Unreadable ? "unreadable" : (grows.Damage ? "damage" : "") + (grows.Block ? "block" : "") + " by " + (grows.ByAmount ? "Amount" : grows.ByConstant.ToString()))
        : null;

    private static int? ConstantOf(HarmonyLib.CodeInstruction instruction)
    {
        var opcode = instruction.opcode;
        if (opcode == OpCodes.Ldc_I4_1) return 1;
        if (opcode == OpCodes.Ldc_I4_2) return 2;
        if (opcode == OpCodes.Ldc_I4_3) return 3;
        if (opcode == OpCodes.Ldc_I4_4) return 4;
        if (opcode == OpCodes.Ldc_I4_5) return 5;
        if (opcode == OpCodes.Ldc_I4_S && instruction.operand is sbyte small) return small;
        if (opcode == OpCodes.Ldc_I4 && instruction.operand is int big) return big;
        return null;
    }
}
