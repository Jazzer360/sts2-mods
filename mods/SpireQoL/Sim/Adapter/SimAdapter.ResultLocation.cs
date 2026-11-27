using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using SpireQoL.Procs.Programs;

namespace SpireQoL.Sim;

/// <summary>
/// Where a card's own code sends it once played (decision #412: its override of CardModel.GetResultLocationForCardPlay, read from its code): Mourning
/// Blow and Particle Wall back to your hand, Shining Strike onto your draw pile's top, where the base's location would be the discard pile.
/// </summary>
public static partial class SimAdapter
{
    /// <summary>
    /// A card's own result location, from each of the base's (CardModel.GetResultLocationForCardPlay: the discard pile, the exhaust pile, none), for
    /// a card whose class overrides it: its code's ways (ProgramReader.ReadReturnedWays) asked with that base location and the board (a test of its
    /// own pile, that it's in a fight, its owner's living teammates), each as the simulator's SimResultLocation. Null map: none of its own. A base
    /// location missing from the map is one where a way can't be asked or followed (a pile the simulator has none for, another player's pile: The
    /// Ball's random teammate): the play is unknown from there, with why, only when its base location is that one.
    /// </summary>
    internal static (IReadOnlyDictionary<SimResultLocation, SimResultLocation>? Map, string? Unknown) OwnResultLocation(CardModel card, Player me, ICombatState combat)
    {
        var method = card.GetType().GetMethod("GetResultLocationForCardPlay", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (method == null || method.DeclaringType == typeof(CardModel)) return (null, null);
        WaysValue ways;
        try { ways = OwnResultWays.GetOrAdd(method.DeclaringType!, owner => ProgramReader.ReadReturnedWays(owner, method, RuntimeCode.Read)); }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            return (null, "where the card's own code sends it once played isn't read");
        }
        var teammates = Teammates(me, combat).Count(teammate => teammate.Creature.IsAlive);
        var map = new Dictionary<SimResultLocation, SimResultLocation>();
        string? unknown = null;
        foreach (var (location, pile) in new[] { (SimResultLocation.Default, PileType.Discard), (SimResultLocation.Exhaust, PileType.Exhaust), (SimResultLocation.Nowhere, PileType.None) })
        {
            var (result, why) = OwnResultFrom(ways, pile, location, teammates);
            if (result is { } own) map[location] = own;
            else unknown ??= why;
        }
        return (map, unknown);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, WaysValue> OwnResultWays = new();

    /// <summary>The first way whose condition holds with the base location in <paramref name="pile"/>, as the simulator's location; null with why when it can't be said.</summary>
    private static (SimResultLocation? Location, string? Why) OwnResultFrom(WaysValue ways, PileType pile, SimResultLocation baseLocation, int teammates)
    {
        foreach (var (when, value) in ways.Ways)
        {
            bool? holds = false;
            foreach (var term in when.Terms)
            {
                bool? all = true;
                foreach (var literal in term)
                {
                    var test = OwnResultTest(literal.Test, pile, teammates);
                    var literalHolds = test == null ? null : (bool?)(test.Value == literal.Holds);
                    if (literalHolds == false) { all = false; break; }
                    if (literalHolds == null) all = null;
                }
                if (all == true) { holds = true; break; }
                if (all == null) holds = null;
            }
            if (holds == null) return (null, "a test of where the card's own code sends it once played isn't read");
            if (holds == false) continue;
            return Unstored(value) switch
            {
                var basis when IsBaseLocation(basis) => (baseLocation, null),
                FieldsSetValue { Fields: var fields } set when IsBaseLocation(set.Of) => OwnLocationOf(fields),
                _ => (null, "the card's own code sends it once played where the simulator doesn't follow"),
            };
        }
        return (null, "no way of the card's own result location holds");
    }

    /// <summary>The base's location (base.GetResultLocationForCardPlay(), kept in a local first).</summary>
    private static bool IsBaseLocation(ProgramValue value) => Unstored(value) is QueryValue { Name: "GetResultLocationForCardPlay" };

    /// <summary>
    /// One test of the ways: its own pile against a pile (location.pileType == PileType.Discard), that it's in a fight (this.CombatState), or the
    /// count of its owner's living teammates (The Ball's); null for any other.
    /// </summary>
    private static bool? OwnResultTest(ProgramValue test, PileType pile, int teammates)
    {
        switch (test)
        {
            case CompareValue { Left: QueryValue { Name: "pileType" } of, Right: ConstantValue { Value: var number } } compare when IsBaseLocation(of.Of ?? NullValue.Instance):
                return compare.Op switch
                {
                    Comparison.Equal => (long)pile == number,
                    Comparison.NotEqual => (long)pile != number,
                    _ => null,
                };
            case QueryValue { Name: "CombatState", Of: NamedValue { Name: "this" } }:
                return true;
            // (kept in a local first: The Ball's list)
            case QueryValue { Name: "Count", Of: { } counted } when Unstored(counted) is QueryValue { Name: "ToList", Arguments: [QueryValue { Name: "Where", Arguments: [QueryValue { Name: "GetTeammatesOf" }, LambdaValue filter] }] }
                && filter.Body.Literals.Any(literal => literal.Test is QueryValue { Name: "IsPlayer" }) && filter.Body.Literals.Any(literal => literal.Test is QueryValue { Name: "IsAlive" }):
                return teammates > 0;
            default:
                return null;
        }
    }

    /// <summary>
    /// A location the code set the pile and position of (CardLocation's pileType, position) as the simulator's. One it gives another player (its
    /// player set: The Ball's teammate, whichever) leaves your piles, none of yours (a teammate's draw pile isn't followed in your turn). Any other
    /// field set: not followed.
    /// </summary>
    private static (SimResultLocation? Location, string? Why) OwnLocationOf(IReadOnlyList<(string Field, ProgramValue Value)> fields)
    {
        if (fields.Any(set => set.Field is not ("pileType" or "position" or "player"))) return (null, "the card's own code sends it once played where the simulator doesn't follow");
        if (fields.Any(set => set.Field == "player")) return (SimResultLocation.Nowhere, null);
        var pile = fields.FirstOrDefault(set => set.Field == "pileType").Value is ConstantValue { Value: var pileNumber } ? (PileType?)(PileType)pileNumber : null;
        var position = fields.FirstOrDefault(set => set.Field == "position").Value is ConstantValue { Value: var positionNumber } ? (CardPilePosition?)(CardPilePosition)positionNumber : null;
        return (pile, position) switch
        {
            (PileType.Hand, _) => (SimResultLocation.HandTop, null),
            (PileType.Draw, CardPilePosition.Top) => (SimResultLocation.DrawTop, null),
            (PileType.Exhaust, _) => (SimResultLocation.Exhaust, null),
            (PileType.Discard, _) => (SimResultLocation.Default, null),
            (PileType.None, _) => (SimResultLocation.Nowhere, null),
            _ => (null, $"the card's own code sends it once played where the simulator has no place for ({pile?.ToString() ?? "a pile not read"}, {position?.ToString() ?? "its position as it was"})"),
        };
    }
}
