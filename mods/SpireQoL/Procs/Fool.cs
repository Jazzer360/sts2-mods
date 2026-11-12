using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using Sts2Mods.Core.Game;

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
    public const string Wah = "Wah", Cho = "Cho", Ki = "Ki";
    public static readonly string[] MelodyTags = { Wah, Cho, Ki };

    public static bool HasTag(CardModel card, string tag)
    {
        try { return card.Tags.Any(t => t.ToString() == tag); }
        catch { return false; }
    }

    /// <summary>A Shoot card: its attack needs an available arrow (none: the card does nothing).</summary>
    public static bool IsShot(CardModel card) => HasTag(card, "Shoot");

    /// <summary>Cards that pull every arrow of yours out of their target (ArrowCmd.DislodgeArrow in OnPlay).</summary>
    public static readonly HashSet<string> DislodgeCards = new() { "Recall", "Uproot", "Wrest" };

    public static IEnumerable<string> Melodies(CardModel card) => MelodyTags.Where(t => HasTag(card, t));

    /// <summary>Arrows you can shoot now (PlayerCombatState.AvailableArrowCount; 0 before v0.113.0).</summary>
    public static int ArrowsAvailable(Player me)
    {
        var pcs = me.PlayerCombatState;
        return pcs == null ? 0 : Sts2State.Private(pcs, "AvailableArrowCount", 0);
    }

    /// <summary>The Impaled power on <paramref name="target"/> holding <paramref name="applier"/>'s arrows (null: none).</summary>
    public static PowerModel? ImpaledBy(Creature target, Creature applier)
        => target.Powers.FirstOrDefault(p => p.GetType().Name == "ImpaledPower" && p.Applier == applier);

    /// <summary>Impaled by anyone's arrow (ArrowCmd.CreatureIsImpaledByArrow).</summary>
    public static bool IsImpaled(Creature target) => target.Powers.Any(p => p.GetType().Name == "ImpaledPower");

    /// <summary>Your powered hits left before the next arrow comes loose (ImpaledPower.HitCounter).</summary>
    public static int HitCounter(PowerModel impaled) => Math.Max(1, Sts2State.Var(impaled, "HitCounter", 3));

    /// <summary>Arrows stuck in it (ImpaledPower.Arrows).</summary>
    public static int ArrowsIn(PowerModel impaled)
        => Sts2State.Private<System.Collections.IEnumerable?>(impaled, "Arrows", null)?.Cast<object>().Count() ?? 0;

    /// <summary>
    /// Arrows a play's powered hits on an Impaled enemy dislodge (ImpaledPower.AfterDamageReceived): the first
    /// when the counter runs out, then one per 3 more hits, at most the arrows it holds.
    /// </summary>
    public static int Dislodges(int hits, int counter, int arrows)
        => hits < counter || arrows <= 0 ? 0 : Math.Min(arrows, 1 + (hits - counter) / 3);

    /// <summary>
    /// Your arrows this play dislodges from <paramref name="e"/>: its powered hits on an enemy your arrows Impale
    /// (ImpaledPower.AfterDamageReceived), all of them if the play kills it (ImpaledPower.AfterDeath), and a shot's
    /// own arrow when its attack kills the target (ArrowCmd.ImpaleTarget: impaled, then dislodged off the dead
    /// target). Recall, Uproot and Wrest pull out every arrow of yours in their target (ArrowCmd.DislodgeArrow).
    /// The kill is PlayFacts' pre-simulation estimate.
    /// </summary>
    public static int DislodgesOn(PlayFacts f, Creature e, Player me)
    {
        var imp = ImpaledBy(e, me.Creature);
        var arrows = imp == null ? 0 : ArrowsIn(imp);
        if (DislodgeCards.Contains(f.Card.GetType().Name)) return arrows;
        var kills = f.Kills(e);
        var d = kills ? arrows : imp == null || !f.CanHit(e) ? 0 : Dislodges(f.HitsOn(e), HitCounter(imp), arrows);
        if (kills && IsShot(f.Card) && ArrowsAvailable(me) > 0) d++;
        return d;
    }

    /// <summary>
    /// Arrows the play dislodges, fewest and most over where you can aim it: a single-target card at each enemy
    /// you could pick; an all-enemy card everywhere at once. Random targets and targetless cards: none counted.
    /// </summary>
    public static (int Min, int Max) Dislodged(PlayFacts f, Player me, ICombatState cs)
    {
        var enemies = cs.HittableEnemies.Where(x => x.IsAlive).ToList();
        if (enemies.Count == 0) return (0, 0);
        switch (f.Card.TargetType)
        {
            case MegaCrit.Sts2.Core.Entities.Cards.TargetType.AnyEnemy:
            {
                var each = enemies.Select(x => DislodgesOn(f, x, me)).ToList();
                return (each.Min(), each.Max());
            }
            case MegaCrit.Sts2.Core.Entities.Cards.TargetType.AllEnemies:
            {
                var all = enemies.Sum(x => DislodgesOn(f, x, me));
                return (all, all);
            }
            default:
                return (0, 0);
        }
    }

    /// <summary>
    /// Fool powers a play changes outside its own PowerCmd.Apply calls: a shot Impales its target
    /// (ArrowCmd.ImpaleTarget) and spends Aim (AimPower.AfterAttack) and Quietus; an attack's powered hits count
    /// Impaled down, and it goes when its last arrow comes loose (ImpaledPower.AfterDamageReceived); Recall,
    /// Uproot and Wrest pull arrows out; a block card spends Stretch (StretchPower.AfterCardPlayed).
    /// </summary>
    public static IEnumerable<string> PowersTouched(CardModel card)
    {
        if (IsShot(card)) { yield return "ImpaledPower"; yield return "AimPower"; yield return "QuietusPower"; }
        if (card.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack || DislodgeCards.Contains(card.GetType().Name)) yield return "ImpaledPower";
        if (Sts2State.Private(card, "GainsBlock", false)) yield return "StretchPower";
    }

    /// <summary>You finished playing a card with this melody tag this turn (MelodyCmd.PlayedThisTurn).</summary>
    public static bool PlayedThisTurn(Player me, string tag) => Sts2State.CardsPlayedThisTurn(me).Any(c => HasTag(c, tag));

    /// <summary>Melodies (Wah, Cho, Ki) you've finished playing this turn (MelodyCmd.MelodiesThisTurn).</summary>
    public static int MelodiesThisTurn(Player me) => MelodyTags.Count(t => PlayedThisTurn(me, t));

    /// <summary>A play with this tag has started this turn (so a new one isn't the first: MelodyCmd.IsFirstPlayThisTurn).</summary>
    public static bool StartedThisTurn(Player me, string tag)
    {
        var cs = Sts2State.Combat;
        if (cs == null) return false;
        try
        {
            return CombatManager.Instance.History.CardPlaysStarted
                .Any(e => e.HappenedThisTurn(cs) && e.CardPlay.Player == me && HasTag(e.CardPlay.Card, tag));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Melodies this card plays first this turn (no play with that tag has started yet).</summary>
    public static IEnumerable<string> FirstMelodies(CardModel card, Player me) => Melodies(card).Where(t => !StartedThisTurn(me, t));

    /// <summary>Melodies played this turn once this card is played too.</summary>
    public static int MelodiesAfter(CardModel card, Player me) => MelodyTags.Count(t => PlayedThisTurn(me, t) || HasTag(card, t));
}
