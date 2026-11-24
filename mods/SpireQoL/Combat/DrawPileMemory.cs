using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using Sts2Mods.Core.Game;

namespace SpireQoL.Combat;

/// <summary>
/// The cards a perfect memory knows are on top of your draw pile, in order (decision #91): ones the game put there
/// (CardPileCmd.Add with CardPilePosition.Top: Headbutt, Thinking Ahead, Recall, Glimmer, Ki Chant, a card played back to the
/// top...). Each card is inserted at the top in turn, so the last one added is on top. Cards leave it as they leave the pile
/// (drawn, played from the top, exhausted); a card shuffled in at a random place could land above them, so it clears it. The
/// rest of the pile stays a set: its order is hidden.
///
/// Observe-only (decision #10): a prefix on CardPileCmd.Add records what's being put where; the frame applies it once the
/// cards have landed. A diagnostic check compares the known top with the pile's actual top (diagnostics may read it; nothing
/// shown does) and logs and forgets it on a mismatch, so a missed source shows up in the logs instead of in a prediction.
/// </summary>
internal static class DrawPileMemory
{
    private static bool _patched;
    private static readonly List<(List<(CardModel Card, CardPile? From)> Cards, CardPilePosition Position, int Frames)> Pending = new();
    private static readonly List<CardModel> Top = new();
    private static object? _combat;
    private static readonly HashSet<string> Warned = new();

    /// <summary>Patches once, whichever feature asks first (Incoming Damage, Proc Indicators, Death Skull all use it).</summary>
    public static void Patch(Harmony harmony)
    {
        if (_patched) return;
        _patched = true;
        var add = AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.Add),
            new[] { typeof(IEnumerable<CardModel>), typeof(CardPile), typeof(CardPilePosition), typeof(AbstractModel), typeof(bool), typeof(bool) });
        if (add == null)
        {
            ModEntry.Log.Warn("[draws] CardPileCmd.Add(cards, pile, position, ...) not found: cards placed on top of your draw pile aren't tracked");
            return;
        }
        harmony.Patch(add, prefix: new HarmonyMethod(typeof(DrawPileMemory), nameof(BeforeAdd)));
        // A shuffle (CardPileCmd.Shuffle: your discard pile and what's left of your draw pile, in a new order; Reboot calls it
        // with cards still in the pile) forgets the top.
        var shuffle = AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.Shuffle), new[] { typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext), typeof(Player) });
        if (shuffle != null) harmony.Patch(shuffle, prefix: new HarmonyMethod(typeof(DrawPileMemory), nameof(BeforeShuffle)));
        else ModEntry.Log.Warn("[draws] CardPileCmd.Shuffle not found: a shuffle won't clear the known top of your draw pile");
    }

    /// <summary>Frames since a shuffle of yours started, until it's over (-1: none going on).</summary>
    private static int _shuffling = -1;

    private static void BeforeShuffle(Player player)
    {
        using var timing = Sts2Mods.Core.Ui.OverlayHost.TimeHook("draw-pile-memory");
        try
        {
            if (player != Sts2State.Me) return;
            Top.Clear();
            Pending.Clear();
            _shuffling = 0;
        }
        catch
        {
            // observe-only
        }
    }

    private static void BeforeAdd(IEnumerable<CardModel> cards, CardPile newPile, CardPilePosition position)
    {
        using var timing = Sts2Mods.Core.Ui.OverlayHost.TimeHook("draw-pile-memory");
        try
        {
            if (newPile.Type != PileType.Draw || position == CardPilePosition.Bottom) return;
            var me = Sts2State.Me;
            var list = cards.Where(card => card.Owner == me).Select(card => (card, card.Pile)).ToList();
            if (list.Count > 0) Pending.Add((list, position, 0));
        }
        catch
        {
            // observe-only: never in the game's way
        }
    }

    /// <summary>The known top, top first (cards still in your draw pile). Call once a frame before reading (Update).</summary>
    public static IReadOnlyList<CardModel> KnownTop => Top;

    /// <summary>Brings the known top up to date with the frame: landed additions, cards gone, a new fight.</summary>
    public static void Update(Player me, object combat)
    {
        if (!ReferenceEquals(combat, _combat))
        {
            _combat = combat;
            Top.Clear();
            Pending.Clear();
            _shuffling = -1;
        }
        var pile = PileType.Draw.GetPile(me);
        // A shuffle of yours: once it's over (your discard pile emptied into the draw pile), the one card whose enchantment puts it on top
        // (its ModifyShuffleOrder, read from its code: Perfect Fit's) is there; with several, their order is the hook's: not known.
        if (_shuffling >= 0)
        {
            if (PileType.Discard.GetPile(me).Cards.Count == 0 || ++_shuffling > 120)
            {
                _shuffling = -1;
                Top.Clear();
                var fits = pile.Cards.Where(SpireQoL.Sim.SimReaders.OnTopWhenShuffled).ToList();
                if (fits.Count == 1) Top.Add(fits[0]);
            }
            else return; // mid-shuffle: nothing to check yet
        }
        // Additions that have landed, in the order they were made.
        while (Pending.Count > 0)
        {
            var (cards, position, frames) = Pending[0];
            if (!cards.All(card => card.Card.Pile == pile))
            {
                // Still on its way (the add is async: a card stays where it was until it lands), or it went somewhere else, or it
                // never came (about two seconds): dropped then.
                var elsewhere = cards.Any(card => card.Card.Pile != pile && card.Card.Pile != card.From);
                if (elsewhere || frames > 120) { Pending.RemoveAt(0); continue; }
                Pending[0] = (cards, position, frames + 1);
                break;
            }
            Pending.RemoveAt(0);
            if (position == CardPilePosition.Top)
                foreach (var (card, _) in cards) { Top.Remove(card); Top.Insert(0, card); }
            else
                Top.Clear(); // a random place: it may be above them now
        }
        // Gone from the pile (drawn, played from the top, exhausted, the pile shuffled away).
        Top.RemoveAll(card => card.Pile != pile);
        Check(pile);
    }

    /// <summary>Diagnostics: the known top must be the pile's actual top. A mismatch is a source we missed: logged, then forgotten.</summary>
    private static void Check(CardPile pile)
    {
        if (Top.Count == 0) return;
        var actual = pile.Cards.Take(Top.Count).ToList();
        if (actual.Count == Top.Count && actual.Zip(Top).All(pair => ReferenceEquals(pair.First, pair.Second))) return;
        var line = $"[audit] [draws] known top of the draw pile [{string.Join(", ", Top.Select(card => card.GetType().Name))}] isn't the pile's top " +
                   $"[{string.Join(", ", actual.Select(card => card.GetType().Name))}]: forgotten";
        if (Warned.Add(string.Join(",", Top.Select(card => card.GetType().Name)))) ModEntry.Log.Warn(line);
        Top.Clear();
    }
}
