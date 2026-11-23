using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using Sts2Mods.Core.Game;

using SpireQoL.Sim;
using MegaCrit.Sts2.Core.Models.Powers;

namespace SpireQoL.Procs;

/// <summary>
/// The Fool's mechanics (game v0.113.0), read by name so the mod still runs on v0.111.0, which has none
/// of these types (decision #32): arrows (ArrowCmd, PlayerCombatState.Arrows), Impaled (ImpaledPower,
/// its HitCounter), and melodies (card tags Wah, Cho and Ki; MelodyCmd).
///
/// Arrows: a Shoot card shoots one of your available arrows with its attack (ArrowCmd.ShootArrowAtTarget);
/// with none available it does nothing at all. The target is then Impaled by you (one ImpaledPower per
/// applier, holding its arrows, HitCounter 3): each of your powered hits on it counts down, and at 0 an arrow
/// is dislodged (back to you; Twine, Yah, Epic Retrieval react) and the counter resets to 3. Its death
/// dislodges them all.
///
/// Melodies: a card tagged Wah, Cho or Ki. "Played this turn" counts finished plays (MelodyCmd.PlayedThisTurn,
/// CardPlaysFinished; the current play is finished before AfterCardPlayed). "First this turn" compares started
/// plays (MelodyCmd.IsFirstPlayThisTurn).
/// </summary>
internal static class Fool
{
    public static readonly SimCardTag[] MelodyTags = { SimCardTag.Wah, SimCardTag.Cho, SimCardTag.Ki };

    /// <summary>The card has the tag, matched by name (Sim.SimReaders.TagsOf: main's CardTag has no Wah, Cho, Ki or Shoot).</summary>
    public static bool HasTag(CardModel card, SimCardTag tag) => SimReaders.TagsOf(card).Contains(tag);

    /// <summary>A Shoot card: its attack needs an available arrow (none: the card does nothing).</summary>
    public static bool IsShot(CardModel card) => HasTag(card, SimCardTag.Shoot);

    /// <summary>
    /// Playing it now tries a shot (ArrowCmd.TryShootArrow, which calls AfterArrowShot even with no arrow: Covering Fire).
    /// An X-cost shot at X 0 doesn't (Repeat Shot: no hits, no shot).
    /// </summary>
    public static bool TriesShot(CardModel card)
    {
        if (!IsShot(card)) return false;
        try
        {
            if (!card.EnergyCost.CostsX) return true;
            var combat = card.CombatState;
            var spent = card.EnergyCost.GetAmountToSpend();
            return (combat == null ? spent : MegaCrit.Sts2.Core.Hooks.Hook.ModifyXValue(combat, card, spent)) > 0;
        }
        catch { return true; }
    }

    /// <summary>
    /// Playing it now shoots an actual arrow: it tries a shot and you have an arrow to shoot (Whistling Arrows, Quietus,
    /// Impaled and Explosive Arrows need one; with none the card does nothing at all).
    /// </summary>
    public static bool Shoots(CardModel card, Player me) => TriesShot(card) && ArrowsAvailable(me) > 0;


    public static IEnumerable<SimCardTag> Melodies(CardModel card) => MelodyTags.Where(simCardTag => HasTag(card, simCardTag));

    /// <summary>Arrows you can shoot now (PlayerCombatState.AvailableArrowCount): beta only (decision #163); main has no arrows.</summary>
    [BetaOnly, System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int AvailableArrows(MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState combatState) => combatState.AvailableArrowCount;

    internal static readonly Func<MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState, int>? ArrowCount =
        BetaOnly.Gate<Func<MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState, int>>(AvailableArrows);

    /// <summary>Arrows you can shoot now (0 on main).</summary>
    public static int ArrowsAvailable(Player me) => me.PlayerCombatState is { } combatState ? ArrowCount?.Invoke(combatState) ?? 0 : 0;

    /// <summary>The Impaled power on <paramref name="target"/> holding <paramref name="applier"/>'s arrows (null: none).</summary>
    public static PowerModel? ImpaledBy(Creature target, Creature applier)
        => target.Powers.FirstOrDefault(power => power.GetType().Name == nameof(ImpaledPower) && power.Applier == applier);

    /// <summary>Impaled by anyone's arrow (ArrowCmd.CreatureIsImpaledByArrow).</summary>
    public static bool IsImpaled(Creature target) => target.Powers.Any(power => power.GetType().Name == nameof(ImpaledPower));

    /// <summary>Your powered hits left before the next arrow comes loose (ImpaledPower.HitCounter): beta only (decision #163).</summary>
    [BetaOnly, System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int HitsLeftIn(PowerModel impaled) => impaled is ImpaledPower power ? power.HitCounter : 3;

    private static readonly Func<PowerModel, int>? HitsLeft = BetaOnly.Gate<Func<PowerModel, int>>(HitsLeftIn);

    /// <summary>Your powered hits left before the next arrow comes loose (3 on main, which has no Impaled).</summary>
    public static int HitCounter(PowerModel impaled) => Math.Max(1, HitsLeft?.Invoke(impaled) ?? 3);

    /// <summary>Arrows stuck in it (ImpaledPower.Arrows): beta only (decision #163).</summary>
    [BetaOnly, System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int ArrowCountIn(PowerModel impaled) => impaled is ImpaledPower power ? power.Arrows.Count : 0;

    private static readonly Func<PowerModel, int>? StuckArrows = BetaOnly.Gate<Func<PowerModel, int>>(ArrowCountIn);

    /// <summary>Arrows stuck in it (none on main).</summary>
    public static int ArrowsIn(PowerModel impaled) => StuckArrows?.Invoke(impaled) ?? 0;

    /// <summary>
    /// Fool powers a play changes outside its own PowerCmd.Apply calls: a shot Impales its target
    /// (ArrowCmd.ImpaleTarget) and spends Aim (AimPower.AfterAttack) and Quietus; an attack's powered hits count
    /// Impaled down, and it goes when its last arrow comes loose (ImpaledPower.AfterDamageReceived); Recall,
    /// Uproot and Wrest pull arrows out; a block card spends Stretch (StretchPower.AfterCardPlayed).
    /// </summary>
    public static IEnumerable<string> PowersTouched(CardModel card)
    {
        if (IsShot(card)) { yield return nameof(ImpaledPower); yield return nameof(AimPower); yield return nameof(QuietusPower); }
        if (card.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack || Programs.CardEffects.DislodgesArrows(card.GetType())) yield return nameof(ImpaledPower);
        if (StretchCard(card)) yield return nameof(StretchPower);
    }

    /// <summary>
    /// Stretch counts this card's play (StretchPower.GainsBlock, v0.113.1): the card gains block, or its enchantment
    /// does (EnchantmentModel.GainsBlock: Adroit).
    /// </summary>
    public static bool StretchCard(CardModel card) => card.GainsBlock || EnchantmentGainsBlock(card);

    /// <summary>The card's enchantment gains block on play (Adroit's powered Move block), so the play gets and spends Stretch.</summary>
    public static bool EnchantmentGainsBlock(CardModel card) => card.Enchantment is { } enchantment && EnchantmentGainsBlockOf?.Invoke(enchantment) == true;

    /// <summary>EnchantmentModel.GainsBlock: beta only (decision #163); main has no such enchantments.</summary>
    [BetaOnly, System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool EnchantmentFlag(MegaCrit.Sts2.Core.Models.EnchantmentModel enchantment) => enchantment.GainsBlock;

    private static readonly Func<MegaCrit.Sts2.Core.Models.EnchantmentModel, bool>? EnchantmentGainsBlockOf =
        BetaOnly.Gate<Func<MegaCrit.Sts2.Core.Models.EnchantmentModel, bool>>(EnchantmentFlag);

    /// <summary>
    /// Block cards with no Block value, and whether their block is powered (a Move without Unpowered): Fisticuffs
    /// gains the damage it dealt (ValueProp.Move), Entrench doubles your block (Unpowered). The card scan flags a new one.
    /// </summary>
    internal static readonly Dictionary<string, bool> BlockWithoutVar = new() { [nameof(Fisticuffs)] = true, [nameof(Entrench)] = false };

    /// <summary>The props of the card's block value (a BlockVar, or a CalculatedBlockVar: Stack, Mimic), or null without one.</summary>
    public static MegaCrit.Sts2.Core.ValueProps.ValueProp? BlockProps(CardModel card)
    {
        foreach (var dynamicVar in card.DynamicVars.Values)
        {
            if (dynamicVar is MegaCrit.Sts2.Core.Localization.DynamicVars.BlockVar blockVar) return blockVar.Props;
            if (dynamicVar is MegaCrit.Sts2.Core.Localization.DynamicVars.CalculatedBlockVar calculatedBlockVar) return calculatedBlockVar.Props;
        }
        return null;
    }

    /// <summary>You finished playing a card with this melody tag this turn (MelodyCmd.PlayedThisTurn).</summary>
    public static bool PlayedThisTurn(Player me, SimCardTag tag) => Sts2State.CardsPlayedThisTurn(me).Any(card => HasTag(card, tag));

    /// <summary>Melodies (Wah, Cho, Ki) you've finished playing this turn (MelodyCmd.MelodiesThisTurn).</summary>
    public static int MelodiesThisTurn(Player me) => MelodyTags.Count(simCardTag => PlayedThisTurn(me, simCardTag));

    /// <summary>A play with this tag has started this turn (so a new one isn't the first: MelodyCmd.IsFirstPlayThisTurn).</summary>
    public static bool StartedThisTurn(Player me, SimCardTag tag)
    {
        var combat = Sts2State.Combat;
        if (combat == null) return false;
        try
        {
            return CombatManager.Instance.History.CardPlaysStarted
                .Any(cardPlayStartedEntry => cardPlayStartedEntry.HappenedThisTurn(combat) && cardPlayStartedEntry.CardPlay.Player == me && HasTag(cardPlayStartedEntry.CardPlay.Card, tag));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Melodies this card plays first this turn (no play with that tag has started yet).</summary>
    public static IEnumerable<SimCardTag> FirstMelodies(CardModel card, Player me) => Melodies(card).Where(simCardTag => !StartedThisTurn(me, simCardTag));
}
