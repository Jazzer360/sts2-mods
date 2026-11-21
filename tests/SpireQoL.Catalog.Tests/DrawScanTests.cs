using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Sim;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Draws by profile (decision #90): every class in the installed game that reacts to a card being drawn is reviewed in
/// SimAdapter.DrawReactions, saying what about the drawn card it reads and how the profile covers it. A game update adding one
/// fails here, before a prediction lumps together cards it tells apart.
/// </summary>
public class DrawScanTests
{
    private static readonly string[] Hooks = { "AfterCardDrawn", "AfterCardDrawnEarly" };

    private static List<string> DrawReactors()
    {
        Assert.True(GameModels.EnsureLoaded() > 500, "the game's cards didn't load");
        return typeof(AbstractModel).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(AbstractModel).IsAssignableFrom(type))
            .Where(type => Hooks.Any(hook => type.GetMethod(hook, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly) != null))
            .Select(type => type.Name).OrderBy(n => n).ToList();
    }

    [Fact]
    public void EveryDrawReactionIsReviewed()
    {
        var reactors = DrawReactors();
        Assert.NotEmpty(reactors);
        var missing = reactors.Where(reactor => !SimAdapter.DrawReactions.ContainsKey(reactor)).ToList();
        Assert.True(missing.Count == 0, "React to a drawn card, not reviewed for the draw profile (SimAdapter.DrawReactions): " + string.Join(", ", missing));
    }

    [Fact]
    public void NoReviewForAClassThatsGone()
    {
        var reactors = DrawReactors().ToHashSet();
        var stale = SimAdapter.DrawReactions.Keys.Where(key => !reactors.Contains(key)).ToList();
        Assert.True(stale.Count == 0, "Reviewed in SimAdapter.DrawReactions but no longer reacting to draws: " + string.Join(", ", stale));
    }

    /// <summary>
    /// A card that uses what its own draw gave (decision #118: the result of CardPileCmd.Draw isn't thrown away), read from its
    /// compiled code: in an async method the draw's result comes out of its awaiter's GetResult, and a plain draw pops it.
    /// </summary>
    private static bool UsesWhatItDraws(Type card)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var method in card.GetMethods(all))
        {
            var body = method.GetCustomAttribute<System.Runtime.CompilerServices.AsyncStateMachineAttribute>()?.StateMachineType
                .GetMethod("MoveNext", all) ?? (MethodBase)method;
            List<HarmonyLib.CodeInstruction> code;
            try { code = SpireQoL.Diagnostics.GameCode.Instructions(body); } catch { continue; }
            for (var i = 0; i < code.Count; i++)
            {
                if (code[i].operand is not MethodInfo { Name: "Draw" } otherMethod || otherMethod.DeclaringType?.Name != "CardPileCmd") continue;
                var get = code.FindIndex(i, codeInstruction => codeInstruction.operand is MethodInfo { Name: "GetResult" });
                if (get >= 0 && get + 1 < code.Count && code[get + 1].opcode != System.Reflection.Emit.OpCodes.Pop) return true;
            }
        }
        return false;
    }

    [Fact]
    public void EveryCardThatUsesWhatItDrawsIsAReader()
    {
        GameModels.EnsureLoaded();
        var users = ModelDb.AllCards.Select(card => card.GetType()).Distinct().Where(UsesWhatItDraws).Select(type => type.Name).OrderBy(n => n).ToList();
        Assert.Contains("EscapePlan", users); // the scan finds the known ones
        var missing = users.Where(user => !SimAdapter.DrawnCardReaders.ContainsKey(user)).ToList();
        Assert.True(missing.Count == 0, "Cards that act on what they draw, not in SimAdapter.DrawnCardReaders: " + string.Join(", ", missing));
        foreach (var name in SimAdapter.DrawnCardReaders.Keys)
            Assert.True(users.Contains(name), $"{name} is a DrawnCardReader but no longer uses what it draws");
    }
}
