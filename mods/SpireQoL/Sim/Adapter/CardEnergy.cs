using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using SpireQoL.Procs;
using Sts2Mods.Core.Game;

namespace SpireQoL.Sim;

/// <summary>
/// A card's own energy (decision #117): every PlayerCmd.GainEnergy its code makes, read from its compiled code. A plain gain (in
/// OnPlay, on every way through it, its energy value, to its owner: Production, Turbo, Supercritical) needs nothing named here;
/// <see cref="Rules"/> covers a gain that depends on something (a kill, the target, the hand) or goes elsewhere, and
/// <see cref="Reviewed"/> says why a gain isn't simulated. The catalog scan (CardEnergyTests) holds every card in the installed game
/// that gains energy to one of the three, so a new card can't gain energy the simulator doesn't count (the Pael's Tears tab and the
/// energy check after each play depend on it).
/// </summary>
internal static class CardEnergy
{
    /// <summary>One GainEnergy call in a card's code: the method it's in, whether every way through that method passes it.</summary>
    internal sealed record Call(string Method, bool Certain, bool ToOwner, bool FromEnergyVar);

    /// <summary>Cards whose gain a rule in <see cref="AddTo"/> works out, with what decides it (read from the decompile).</summary>
    internal static readonly Dictionary<string, string> Rules = new()
    {
        [nameof(Sunder)] = "its attack killed the target (DamageCmd's results: WasTargetKilled): AttackAction.OnKillEnergy",
        ["BowSmack"] = "the target was impaled before its hit (ArrowCmd.CreatureIsImpaledByArrow)",
        [nameof(Restlessness)] = "it's the only card in your hand (IsOnlyCardInHand, the card's gold glow)",
        [nameof(ForgottenRitual)] = "you exhausted a card this turn (WasCardExhaustedThisTurn, the card's gold glow)",
        [nameof(DoubleEnergy)] = "as much energy as you have once its cost is spent (GainEnergyAsHeldAction)",
        [nameof(MadScience)] = "its Energized rider (TinkerTimeRider, EnergizedEnergy)",
        ["EpicRetrieval"] = "per arrow of yours its play knocks loose (AfterArrowDislodged, source this card): SimCardPlay.ArrowEnergy",
        [nameof(BelieveInYou)] = "to the teammate it's aimed at",
        [nameof(Constellation)] = "to the teammate it's aimed at",
        [nameof(EnergySurge)] = "to every living player, you included",
    };

    /// <summary>Cards that gain energy the simulator doesn't count, and why.</summary>
    internal static readonly Dictionary<string, string> Reviewed = new()
    {
        [nameof(DrumOfBattle)] = "when it's exhausted (AfterCardExhausted of itself), not when played: an exhaust of it during another play"
            + " isn't simulated (tracker: Drum of Battle)",
    };

    private static readonly Dictionary<Type, List<Call>> Cache = new();

    /// <summary>Every GainEnergy call in the card's own methods (an async one's state machine included).</summary>
    internal static List<Call> CallsOf(Type cardType)
    {
        lock (Cache)
            if (Cache.TryGetValue(cardType, out var cached)) return cached;
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
                if (code[i].operand is MethodInfo { Name: "GainEnergy" } m && m.DeclaringType?.Name == "PlayerCmd") at.Add(i);
            if (at.Count == 0) continue;
            var certain = Branches.OnEveryPath(code, at);
            for (var k = 0; k < at.Count; k++)
            {
                // The arguments, pushed in order: the amount (base.DynamicVars.Energy...), then the player (base.Owner last).
                var before = code.Take(at[k]).Reverse().Take(8).Select(x => x.operand as MethodInfo).Where(x => x != null).ToList();
                var toOwner = before.FirstOrDefault() is { Name: "get_Owner" } owner && typeof(CardModel).IsAssignableFrom(owner.DeclaringType);
                var fromVar = before.Any(x => x!.Name == "get_Energy" && x.DeclaringType?.Name == "DynamicVarSet");
                calls.Add(new Call(method.Name, certain[k], toOwner, fromVar));
            }
        }
        lock (Cache) Cache[cardType] = calls;
        return calls;
    }

    /// <summary>A plain gain: one call, in OnPlay, on every way through it, the card's energy value, to its owner.</summary>
    internal static bool IsPlain(Type cardType) =>
        CallsOf(cardType) is [{ Method: "OnPlay", Certain: true, ToOwner: true, FromEnergyVar: true }];

    /// <summary>
    /// What the card's own energy adds to its play aimed at <paramref name="target"/>: events for the energy it gains (each play),
    /// or the fields the simulator works it out from. <paramref name="owner"/>: whose card it is; <paramref name="allyId"/>: the
    /// teammate it's aimed at (-1: none).
    /// </summary>
    internal static void AddTo(SimPlay play, CardModel card, Player me, ICombatState cs, Creature? target, int owner, int allyId)
    {
        var name = card.GetType().Name;
        var energy = Sts2State.Var(card, "Energy");
        void Gain(int amount, int to)
        {
            if (amount > 0) play.Body.Add(new CardEventAction(CardEvent.EnergyGained, amount) { Player = to });
        }
        switch (name)
        {
            case nameof(Sunder):
                for (var i = 0; i < play.Body.Count; i++)
                    if (play.Body[i] is AttackAction a) { play.Body[i] = a with { OnKillEnergy = energy }; break; }
                return;
            case "BowSmack":
                if (target != null && Fool.IsImpaled(target)) Gain(energy, owner);
                return;
            case nameof(Restlessness):
                if (Sts2State.Private(card, "IsOnlyCardInHand", false)) Gain(energy, owner);
                return;
            case nameof(ForgottenRitual):
                if (Sts2State.Private(card, "WasCardExhaustedThisTurn", false)) Gain(energy, owner);
                return;
            case nameof(DoubleEnergy):
                play.Body.Add(new GainEnergyAsHeldAction());
                return;
            case nameof(MadScience):
                if (card is MadScience ms && ms.TinkerTimeRider.ToString() == "Energized") Gain(Sts2State.Var(card, "EnergizedEnergy"), owner);
                return;
            case "EpicRetrieval":
                if (play.Card != null) play.Card = play.Card with { ArrowEnergy = energy };
                return;
            case nameof(BelieveInYou) or nameof(Constellation):
                if (Who.KindOf(allyId) == CreatureKind.Teammate) Gain(energy, allyId);
                return;
            case nameof(EnergySurge):
                Gain(energy, owner);
                // Teammates are numbered from your side: a teammate's own Energy Surge (Howl from Beyond) gives you energy too, which
                // isn't followed (it'd need their numbering of the players).
                if (owner != Who.You) return;
                var mates = SimAdapter.Teammates(me, cs);
                for (var k = 0; k < mates.Count; k++)
                    if (mates[k].Creature is { IsAlive: true }) Gain(energy, Who.Teammate(k));
                return;
        }
        if (Reviewed.ContainsKey(name)) return;
        if (IsPlain(card.GetType())) Gain(energy, owner);
    }
}
