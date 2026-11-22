using System.Reflection.Emit;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Procs;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// The cards a play makes (PlayFacts.Generated) come from a per-card rule (PlayFacts.GeneratedCount). A card whose play reads one of
/// its own card counts (DynamicVars["Shivs"] or DynamicVars.Cards, a CardsVar) makes that many: a rule that says otherwise is out of date (Leading
/// Strike's 1 when the game made it 2, a dev review). Each card the scan flags is fixed in the rule, or reviewed here with why. A card
/// that makes cards with no rule at all is the card scan's (CardCoverage).
/// </summary>
[Collection(GameFightCollection.Name)]
public class MadeCountScanTests
{
    /// <summary>Cards whose count isn't the variable their play reads, and why.</summary>
    private static readonly Dictionary<string, string> Reviewed = new()
    {
    };

    [Fact]
    public void ACardMakesTheCountItsPlayReads()
    {
        GameModels.EnsureLoaded();
        var wrong = new List<string>();
        foreach (var canon in ModelDb.AllCards.ToList())
        {
            var type = canon.GetType();
            if (Reviewed.ContainsKey(type.Name)) continue;
            var keys = canon.DynamicVars.Where(entry => entry.Value is CardsVar).Select(entry => entry.Key).ToHashSet();
            if (keys.Count == 0) continue;
            var read = new HashSet<string>();
            foreach (var method in CardCode.OnPlayMethods(type))
            {
                List<HarmonyLib.CodeInstruction> code;
                try { code = HarmonyLib.PatchProcessor.GetOriginalInstructions(method); }
                catch { continue; }
                foreach (var instruction in code)
                {
                    if (instruction.opcode == OpCodes.Ldstr && instruction.operand is string key && keys.Contains(key)) read.Add(key);
                    // Or through the set's named property (DynamicVars.Cards).
                    if (instruction.operand is System.Reflection.MethodInfo { Name: var getter } called && called.DeclaringType == typeof(DynamicVarSet)
                        && getter.StartsWith("get_", StringComparison.Ordinal) && keys.Contains(getter[4..])) read.Add(getter[4..]);
                }
            }
            if (read.Count != 1) continue; // none, or several counts (a rule of its own)
            // Cards is also how many a card draws (Pommel Strike, Spoils of Battle): not a count of cards made when the play draws.
            if (read.Single() == "Cards" && CardCode.Calls(type, nameof(MegaCrit.Sts2.Core.Commands.CardPileCmd), nameof(MegaCrit.Sts2.Core.Commands.CardPileCmd.Draw))) continue;
            var gameFight = new GameFight();
            gameFight.Add<MegaCrit.Sts2.Core.Models.Cards.DefendIronclad>();
            var card = gameFight.Combat.CreateCard(canon, gameFight.Me);
            PileType.Hand.GetPile(gameFight.Me).AddInternal(card);
            PlayFacts facts;
            try { facts = PlayFacts.For(card, gameFight.Me, gameFight.Combat, plays: 1); }
            catch { continue; }
            var expected = card.DynamicVars[read.Single()].IntValue;
            // Only a card the rule says makes cards: one that makes cards with no rule is the card scan's (CardCoverage).
            if (facts.Generated > 0 && facts.Generated != expected) wrong.Add($"{type.Name}: counted {facts.Generated}, its play reads {read.Single()} = {expected}");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }
}
