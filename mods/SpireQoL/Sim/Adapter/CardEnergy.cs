using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using Sts2Mods.Core.Game;

namespace SpireQoL.Sim;

/// <summary>
/// A card's own energy (decision #117): every PlayerCmd.GainEnergy its code makes, read from its compiled code. A gain in its OnPlay is a
/// step of its program, translated with the rest of the play (decision #193: SimAdapter.EnergyFrom, its amount, its player and its condition
/// its own); <see cref="Rules"/> covers a gain in a hook of the card's own, and <see cref="Reviewed"/> says why a gain isn't simulated. The
/// catalog scan (CardEnergyTests) holds every card in the installed game that gains energy outside its OnPlay to one of the two, so a new card
/// can't gain energy the simulator doesn't count (the Pael's Tears tab and the energy check after each play depend on it).
/// </summary>
internal static class CardEnergy
{
    /// <summary>One GainEnergy call in a card's code: the method it's in, whether every way through that method passes it.</summary>
    internal sealed record Call(string Method, bool Certain, bool ToOwner, bool FromEnergyVar);

    /// <summary>
    /// Cards that gain energy outside their OnPlay (a hook of their own), with what gives it. A gain in OnPlay is a step of the card's program,
    /// translated for every card (SimAdapter.EnergyFrom: its amount and its player its arguments), so it needs nothing here.
    /// </summary>
    internal static readonly Dictionary<string, string> Rules = new()
    {
        [nameof(EpicRetrieval)] = "per arrow of yours its play knocks loose (AfterArrowDislodged, source this card): SimCardPlay.ArrowEnergy",
        [nameof(DrumOfBattle)] = "when it's exhausted (its own AfterCardExhausted, WhenExhausted): from your hand by another play (ExhaustFromHandAction), or its own exhaust (Corruption)",
    };

    /// <summary>Cards that gain energy the simulator doesn't count, and why.</summary>
    internal static readonly Dictionary<string, string> Reviewed = new();

    /// <summary>
    /// The energy a card takes when it's drawn: a loss of its Energy value from its owner in its own AfterCardDrawn (Void), read from
    /// its code; 0 for any other card. It's what SimDrawCard.EnergyLostOnDraw holds.
    /// </summary>
    internal static int LostWhenDrawn(CardModel card) =>
        CallsOf(card.GetType(), "LoseEnergy").Any(call => call is { Method: "AfterCardDrawn", ToOwner: true, FromEnergyVar: true }) ? Sts2State.Var(card, "Energy") : 0;

    /// <summary>
    /// The energy a card gives when it's exhausted: a gain of its Energy value to its owner in its own AfterCardExhausted (Drum of
    /// Battle), read from its code; 0 for any other card. It's what SimPlayer.ExhaustingInHand holds.
    /// </summary>
    internal static int WhenExhausted(CardModel card) =>
        CallsOf(card.GetType()).Any(call => call is { Method: "AfterCardExhausted", ToOwner: true, FromEnergyVar: true }) ? Sts2State.Var(card, "Energy") : 0;

    private static readonly Dictionary<(Type, string), List<Call>> Cache = new();

    /// <summary>Every call of a PlayerCmd energy command (GainEnergy, LoseEnergy) in the card's own methods (an async one's state machine included).</summary>
    internal static List<Call> CallsOf(Type cardType, string command = "GainEnergy")
    {
        lock (Cache)
            if (Cache.TryGetValue((cardType, command), out var cached)) return cached;
        var calls = new List<Call>();
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var method in cardType.GetMethods(all))
        {
            var body = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
                .GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ?? (MethodBase)method;
            List<CodeInstruction> code;
            try { code = Diagnostics.GameCode.Instructions(body); }
            catch { continue; }
            var at = new List<int>();
            for (var i = 0; i < code.Count; i++)
                if (code[i].operand is MethodInfo otherMethod && otherMethod.Name == command && otherMethod.DeclaringType?.Name == "PlayerCmd") at.Add(i);
            if (at.Count == 0) continue;
            var certain = Branches.OnEveryPath(code, at);
            for (var aim = 0; aim < at.Count; aim++)
            {
                // The arguments, pushed in order: the amount (base.DynamicVars.Energy...), then the player (base.Owner last).
                var before = code.Take(at[aim]).Reverse().Take(8).Select(codeInstruction => codeInstruction.operand as MethodInfo).Where(nextMethod => nextMethod != null).ToList();
                var toOwner = before.FirstOrDefault() is { Name: "get_Owner" } owner && typeof(CardModel).IsAssignableFrom(owner.DeclaringType);
                var fromVar = before.Any(nextMethod => nextMethod!.Name == "get_Energy" && nextMethod.DeclaringType?.Name == "DynamicVarSet");
                calls.Add(new Call(method.Name, certain[aim], toOwner, fromVar));
            }
        }
        lock (Cache) Cache[(cardType, command)] = calls;
        return calls;
    }

    /// <summary>A plain gain: one call, in OnPlay, on every way through it, the card's energy value, to its owner.</summary>
    internal static bool IsPlain(Type cardType) =>
        CallsOf(cardType) is [{ Method: "OnPlay", Certain: true, ToOwner: true, FromEnergyVar: true }];

    /// <summary>
    /// The energy the card gives from its own hooks during its play (Rules): Epic Retrieval's for each arrow of yours its play knocks loose
    /// (SimCardPlay.ArrowEnergy). Every gain in its OnPlay is a step of its program (SimAdapter.EnergyFrom).
    /// </summary>
    internal static void AddHookEnergy(SimPlay play, CardModel card)
    {
        if (play.Card == null || !CallsOf(card.GetType()).Any(call => call.Method == "AfterArrowDislodged")) return;
        play.Card = play.Card with { ArrowEnergy = Sts2State.Var(card, "Energy") };
    }}
