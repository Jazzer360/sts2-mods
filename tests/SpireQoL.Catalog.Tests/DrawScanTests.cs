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
            .Select(type => type.Name).OrderBy(name => name).ToList();
    }

    /// <summary>A class read from its code (decision #195): its reading says what it asks of the drawn card (SimAdapter.DrawnCardNotKept).</summary>
    private static ReadModel? Read(string name) => SimModels.Get(name) as ReadModel;

    [Fact]
    public void EveryDrawReactionIsReviewed()
    {
        var reactors = DrawReactors();
        Assert.NotEmpty(reactors);
        var missing = reactors.Where(reactor => !SimAdapter.DrawReactions.ContainsKey(reactor) && Read(reactor) == null).ToList();
        Assert.True(missing.Count == 0, "React to a drawn card, neither read from their code nor reviewed for the draw profile (SimAdapter.DrawReactions): " + string.Join(", ", missing));
        // One read from its code asks only what the profile keeps of the drawn card (any card, a Status, Ethereal).
        var notKept = reactors.Where(reactor => Read(reactor) is { } read && SimAdapter.DrawnCardNotKept(read.Spec) != null)
            .Select(reactor => $"{reactor}: {SimAdapter.DrawnCardNotKept(Read(reactor)!.Spec)}").ToList();
        Assert.True(notKept.Count == 0, "Read from their code, asking what the draw profile doesn't keep of the drawn card: " + string.Join("; ", notKept));
    }

    [Fact]
    public void NoReviewForAClassThatsGone()
    {
        var reactors = DrawReactors().ToHashSet();
        var stale = SimAdapter.DrawReactions.Keys.Where(key => !reactors.Contains(key)).ToList();
        Assert.True(stale.Count == 0, "Reviewed in SimAdapter.DrawReactions but no longer reacting to draws: " + string.Join(", ", stale));
        // A review by name for a class read from its code is its reading's to give (decision #281).
        var read = SimAdapter.DrawReactions.Keys.Where(key => Read(key) != null).ToList();
        Assert.True(read.Count == 0, "Reviewed in SimAdapter.DrawReactions but read from their code: " + string.Join(", ", read));
    }

    /// <summary>
    /// The reading's answer: Iteration's type tested against Status and Pagestorm's Ethereal are kept, Automation's count of any card asks nothing;
    /// a type tested against Attack isn't kept (the profile keeps only whether a drawn card is a Status).
    /// </summary>
    [Fact]
    public void WhatADrawHookAsksIsReadFromItsCode()
    {
        GameModels.EnsureLoaded();
        foreach (var name in new[] { nameof(MegaCrit.Sts2.Core.Models.Powers.IterationPower), nameof(MegaCrit.Sts2.Core.Models.Powers.PagestormPower), nameof(MegaCrit.Sts2.Core.Models.Powers.AutomationPower) })
            Assert.Null(SimAdapter.DrawnCardNotKept(Read(name)!.Spec));
        var attack = new HookTest("probe", new HookMath(HookOperation.Equal, new HookFactValue(HookFact.CardType), new HookConstant((int)SimCardType.Attack)));
        var drawsOnAttack = new ModelSpec("Probe", ModelKind.Power, new[] { new HookProgram(HookName.AfterCardDrawn, new HookStep[] { new FlashStep { When = attack } }) });
        Assert.NotNull(SimAdapter.DrawnCardNotKept(drawsOnAttack));
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
    public void EachReadersReactionIsReadFromItsProgram()
    {
        // The table it replaced (SimAdapter.DrawnCardReaders): Pillage draws again while it draws an Attack, Escape Plan blocks only for
        // a Skill, Scrape discards the drawn cards that cost energy.
        GameModels.EnsureLoaded();
        var pillage = SimAdapter.DrawnCardReaderOf(typeof(MegaCrit.Sts2.Core.Models.Cards.Pillage))!;
        Assert.Equal((SimAdapter.DrawnCardReader.Reads.Type, true, true, SimCardType.Attack, false), (pillage.What, pillage.DrawsAgain, pillage.Counts, pillage.Type, pillage.BlockIf));
        var escapePlan = SimAdapter.DrawnCardReaderOf(typeof(MegaCrit.Sts2.Core.Models.Cards.EscapePlan))!;
        Assert.Equal((SimAdapter.DrawnCardReader.Reads.Type, false, false, SimCardType.Skill, true), (escapePlan.What, escapePlan.DrawsAgain, escapePlan.Counts, escapePlan.Type, escapePlan.BlockIf));
        var scrape = SimAdapter.DrawnCardReaderOf(typeof(MegaCrit.Sts2.Core.Models.Cards.Scrape))!;
        Assert.Equal((SimAdapter.DrawnCardReader.Reads.Cost, false, true), (scrape.What, scrape.DrawsAgain, scrape.Counts));
        Assert.Null(SimAdapter.DrawnCardReaderOf(typeof(MegaCrit.Sts2.Core.Models.Cards.Acrobatics)));
    }

    [Fact]
    public void EveryCardThatUsesWhatItDrawsIsAReader()
    {
        GameModels.EnsureLoaded();
        var users = ModelDb.AllCards.Select(card => card.GetType()).Distinct().Where(UsesWhatItDraws).Select(type => type.Name).OrderBy(name => name).ToList();
        Assert.Contains("EscapePlan", users); // the scan finds the known ones
        var readers = ModelDb.AllCards.Select(card => card.GetType()).Distinct().Where(type => SimAdapter.DrawnCardReaderOf(type) != null)
            .Select(type => type.Name).OrderBy(name => name).ToList();
        var missing = users.Except(readers).ToList();
        Assert.True(missing.Count == 0, "Cards that act on what they draw whose reaction their program doesn't say (SimAdapter.DrawnCardReaderOf): " + string.Join(", ", missing));
        foreach (var name in readers)
            Assert.True(users.Contains(name), $"{name} is read as acting on what it draws but no longer uses what it draws");
    }
}
