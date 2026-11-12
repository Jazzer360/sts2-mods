using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using SpireQoL.Procs;
using SpireQoL.Sim;

namespace SpireQoL.Diagnostics;

/// <summary>
/// Card code against what the mod counts for it: reads every card's OnPlay from the compiled game
/// (its async state machine included) and checks each effect the mod predicts from per-card tables
/// against those tables, through the same functions the predictions use (PlayFacts, SimAdapter) on
/// the game's own card instances (ModelDb.AllCards). A card that draws but isn't counted as drawing,
/// channels orbs the simulator doesn't add, gains block without a Block value, and so on, is listed;
/// CoverageWatch reports it when the card shows up in a fight. New cards and changed cards in a game
/// update are caught the same way, before anyone plays them.
/// </summary>
internal static class CardCoverage
{
    private static Dictionary<string, List<string>>? _issues;

    /// <summary>Card class name → what its code does that the mod doesn't count.</summary>
    public static IReadOnlyDictionary<string, List<string>> Issues => _issues ??= Build();

    private static Dictionary<string, List<string>> Build()
    {
        var result = new Dictionary<string, List<string>>();
        List<CardModel> cards;
        try { cards = ModelDb.AllCards.ToList(); }
        catch { return result; }
        var others = cards.Take(3).ToList();
        foreach (var card in cards)
        {
            try
            {
                var issues = Check(card, others, cards);
                if (issues.Count > 0) result[card.GetType().Name] = issues;
            }
            catch (Exception ex)
            {
                result[card.GetType().Name] = new List<string> { $"couldn't check: {ex.Message}" };
            }
        }
        return result;
    }

    /// <summary>
    /// Cards whose counts are calculated from the fight (orbs, statuses, history): handled in PlayFacts,
    /// but they read 0 on the game's template card, so the scan can't test them by value.
    /// </summary>
    private static readonly HashSet<string> Calculated = new() { nameof(CompileDriver), nameof(FlakCannon), nameof(Voltaic) };

    /// <summary>
    /// Prefix of issues that only matter with teammates (multiplayer): CoverageWatch reports them only in a
    /// fight with other players, since alone the card acts on you, as the simulator models it.
    /// </summary>
    public const string TeammatesOnly = "with teammates: ";

    /// <summary>
    /// Cards aimed at players (TargetType AnyAlly or AllAllies) whose effect on teammates the simulator models.
    /// None yet: the simulator takes no player target, so a card's block, powers or HP go to you.
    /// </summary>
    private static readonly HashSet<string> TeammateTargetModelled = new();

    /// <summary>Cards reviewed as not needing what the scan finds, and why.</summary>
    private static readonly Dictionary<string, string> Reviewed = new()
    {
        [nameof(DeprecatedCard)] = "a placeholder card the game keeps for old saves (draws 1, removes itself from the deck)",
    };

    /// <summary>
    /// An upgraded copy of the game's template card, made the way the game makes its upgrade tooltips
    /// (MutableClone, UpgradeInternal): some cards only do something when upgraded (Spinner+ channels Glass).
    /// Null when the card can't be upgraded.
    /// </summary>
    private static CardModel? UpgradedCopy(CardModel card)
    {
        try
        {
            if (card.MaxUpgradeLevel < 1) return null;
            var copy = (CardModel)card.MutableClone();
            copy.UpgradeInternal();
            copy.FinalizeUpgradeInternal();
            return copy;
        }
        catch
        {
            return null;
        }
    }

    private static List<string> Check(CardModel card, List<CardModel> others, List<CardModel> all)
    {
        var name = card.GetType().Name;
        if (Reviewed.ContainsKey(name) || Calculated.Contains(name)) return new List<string>();
        var calls = OnPlayCalls(card.GetType());
        var issues = new List<string>();
        var upgraded = UpgradedCopy(card);
        // Counted when the card or its upgrade counts it.
        bool None(Func<CardModel, int> count) => count(card) == 0 && (upgraded == null || count(upgraded) == 0);
        bool Calls(string type, string method) => calls.Contains(type + "." + method);
        bool Applies<T>() => calls.Contains("PowerCmd.Apply<" + typeof(T).Name + ">");
        bool HasPowerVar<T>() => card.DynamicVars.Values.Any(v => v.GetType() is { IsGenericType: true } t
                                                                 && t.GetGenericTypeDefinition() == typeof(PowerVar<>) && t.GetGenericArguments()[0] == typeof(T));

        // Cards aimed at a teammate or at every player: the simulator applies the card's effects to you only.
        if (card.TargetType == TargetType.AnyAlly && !TeammateTargetModelled.Contains(name))
            issues.Add(TeammatesOnly + "targets a teammate (the simulator applies its effects to you)");
        if (card.TargetType == TargetType.AllAllies && !TeammateTargetModelled.Contains(name))
            issues.Add(TeammatesOnly + "acts on every player (the simulator applies its effects to you only)");

        if ((Calls("CardPileCmd", "Draw") || Calls("CardCmd", "DiscardAndDraw"))
            && None(c => PlayFacts.CardsDrawn(c, 5) + PlayFacts.CardsDrawn(c, 1)))
            issues.Add("draws cards (not counted: Shuffle Warning, draw procs)");
        if (Calls("OrbCmd", "Channel") && !SimAdapter.HandlesChannel(name))
            issues.Add("channels orbs (the simulator doesn't add them)");
        if (Calls("OrbCmd", "Channel") && None(c => PlayFacts.ChannelCount(c, name, null!)))
            issues.Add("channels orbs (not counted for Metronome)");
        if (Calls("CreatureCmd", "GainBlock") && None(c => PlayFacts.BlockGainCount(c, name, others)))
            issues.Add("gains block without a Block value (not in the simulator or Juggernaut)");
        if (Applies<PoisonPower>() && !HasPowerVar<PoisonPower>())
            issues.Add("applies poison outside its power value (not in the simulator)");
        if (Calls("CardCmd", "Exhaust") && None(c => PlayFacts.ExhaustOthers(c, name, others, 5, 1)))
            issues.Add("exhausts cards (not counted: Feel No Pain, Dark Embrace...)");
        if ((Calls("CardCmd", "Discard") || Calls("CardCmd", "DiscardAndDraw")) && None(c => PlayFacts.DiscardCount(c, name, others, PlayFacts.CardsDrawn(c, 5))))
            issues.Add("discards cards (not counted: Tingsha, Tough Bandages...)");
        if ((Calls("CardPileCmd", "AddGeneratedCardToCombat") || Calls("CardPileCmd", "AddGeneratedCardsToCombat") || Calls("CardPileCmd", "AddToCombatAndPreview"))
            && None(c => PlayFacts.GeneratedCount(c, name, others, null!, out _)))
            issues.Add("creates cards (not counted: created-card procs)");
        // Forging creates the Sovereign Blade through ForgeCmd, not in the card's own code.
        if (Calls("ForgeCmd", "Forge") && !PlayFacts.ForgeCards.Contains(name))
            issues.Add("forges (its Sovereign Blade isn't counted: created-card procs)");
        // Every game card as the hand, so any filter (statuses, attacks) finds something to transform.
        if ((Calls("CardCmd", "Transform") || Calls("CardCmd", "TransformTo") || Calls("CardCmd", "TransformToRandom"))
            && None(c => PlayFacts.TransformCount(c, name, all, 5)))
            issues.Add("transforms cards (not counted: the proc check's created-card budget)");
        if ((Calls("OrbCmd", "AddSlots") || Calls("OrbCmd", "RemoveSlots")) && None(c => Math.Abs(SimAdapter.SlotChange(c, name))))
            issues.Add("changes your orb slots (the simulator doesn't change them)");
        foreach (var applied in PowersApplied(card.GetType()))
            if (SimAdapter.PlainSelfPowerAmount(card, applied) == 0)
                issues.Add($"applies {applied.Name} with no amount the simulator reads");
        if (Calls("CreatureCmd", "Kill") && !SimAdapter.KillsOstyCards.Contains(name))
            issues.Add("kills a creature (not in the simulator)");
        if (Calls("CardPileCmd", "Shuffle") && !PlayFacts.ShuffleCards.Contains(name))
            issues.Add("shuffles your discard pile (not counted: shuffle procs, Biiig Hug)");
        if (Calls("PlayerCmd", "GainStars") && !PlayFacts.StarGainCards.Contains(name))
            issues.Add("gains stars (not counted for Black Hole)");
        return issues;
    }

    private static readonly BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, List<Type>> Applied = new();

    /// <summary>The power types a card's OnPlay applies (PowerCmd.Apply&lt;T&gt;), read from its compiled code.</summary>
    public static IReadOnlyList<Type> PowersApplied(Type cardType) => Applied.GetOrAdd(cardType, t =>
    {
        var list = new List<Type>();
        foreach (var m in OnPlayMethods(t))
        {
            try
            {
                foreach (var ins in PatchProcessor.GetOriginalInstructions(m))
                    if (ins.operand is MethodInfo { IsGenericMethod: true, Name: "Apply" } mi && mi.DeclaringType?.Name == "PowerCmd")
                        list.Add(mi.GetGenericArguments()[0]);
            }
            catch
            {
                // unreadable: nothing found
            }
        }
        return list;
    });

    private static IEnumerable<MethodBase> OnPlayMethods(Type cardType)
        => cardType.GetMethods(All).Where(m => m.Name == "OnPlay").Cast<MethodBase>()
            .Concat(cardType.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
                .Where(t => t.Name.Contains("<OnPlay>") && typeof(IAsyncStateMachine).IsAssignableFrom(t))
                .Select(t => t.GetMethod("MoveNext", All))
                .Where(m => m != null)
                .Cast<MethodBase>());

    /// <summary>"Type.Method" (and "PowerCmd.Apply&lt;T&gt;") for every call in the card's OnPlay and its async state machine.</summary>
    private static HashSet<string> OnPlayCalls(Type cardType)
    {
        var set = new HashSet<string>();
        foreach (var m in OnPlayMethods(cardType))
        {
            try
            {
                foreach (var ins in PatchProcessor.GetOriginalInstructions(m))
                {
                    if (ins.operand is not MethodInfo mi || mi.DeclaringType == null) continue;
                    set.Add(mi.DeclaringType.Name + "." + mi.Name);
                    if (mi.IsGenericMethod) set.Add(mi.DeclaringType.Name + "." + mi.Name + "<" + mi.GetGenericArguments()[0].Name + ">");
                }
            }
            catch
            {
                // unreadable method: nothing found
            }
        }
        return set;
    }
}
