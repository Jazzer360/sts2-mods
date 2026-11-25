using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using SpireQoL.Cards;
using SpireQoL.Data;
using Sts2Mods.Core.Game;

namespace SpireQoL.Features;

/// <summary>
/// Tabs on cards you're offered outside fights (docs/CARD_ODDS.md phase 3, decision #62): on a card reward, an event's choice
/// of cards (choose a card, a bundle, a grid of rewards such as Sea Glass's) and the shop, a plain tab with the icon of each
/// of your relics that makes that card stronger in fights in a way the card doesn't show there: Strike Dummy (Strikes),
/// Miniature Cannon (upgraded Attacks), Mystic Lighter (enchanted Attacks), Vitruvian Minion (Minion cards). The card is
/// judged as it's offered (already upgraded or enchanted by an egg, Fresnel Lens, the shop), and with the cards it makes
/// when played (Begone!'s Minion Strike). Relics whose change already shows on the card (the eggs, Fresnel Lens) get none.
/// Only cards that aren't in your deck: picking a card of yours to remove, upgrade or transform isn't an offer.
/// On a card reward, also a tab for each relic that taking the card sets off (Bing Bong, Lucky Fysh, Book of Five Rings: decision
/// #83): in events and the shop those show beside the option or the price instead.
/// </summary>
public sealed partial class RunReminders
{
    private readonly Dictionary<ulong, (CardModel Model, CardDecor? Decor)> _offeredTabs = new();
    /// <summary>What the offered cards' tabs were worked out from (the settings and you: AddRunState), and dev mode's check of it.</summary>
    private int _offeredRunKey;
    private readonly SpireQoL.Combat.ReuseCheck _offeredTabsReuse = new("the offered cards' tabs");

    internal void OfferedCardTabs()
    {
        var nodes = OfferedCards();
        var me = nodes.Count > 0 ? RunMe() : null;
        if (me == null)
        {
            _offeredTabs.Clear();
            return;
        }
        // Worked out again only when the settings or what they read of you change (decision #177: every 10th frame before, garbage
        // while you looked at a shop); drawn every frame.
        var runState = new HashCode();
        runState.Add(QolSettings.Revision);
        AddRunState(ref runState, me);
        var runKey = runState.ToHashCode();
        var reuse = _offeredTabsReuse.Decide(runKey == _offeredRunKey);
        _offeredRunKey = runKey;
        var recompute = reuse != SpireQoL.Combat.FrameReuse.Reuse;
        var seen = _seenOffered;
        seen.Clear();
        foreach (var (node, place) in nodes)
        {
            CardModel? model;
            try { model = node.Model; } catch { continue; }
            if (model == null) continue;
            var id = node.GetInstanceId();
            seen.Add(id);
            if (recompute || !_offeredTabs.TryGetValue(id, out var have) || have.Model != model)
                have = _offeredTabs[id] = OfferedTab(id, model, me, place, check: reuse == SpireQoL.Combat.FrameReuse.WorkOutAndCheck);
            if (have.Decor == null) continue; // a card of yours
            // While an event option runs, what it offers (the odds log).
            if (_window is { } window) window.Offered.Add(model);
            if (!have.Decor.IsEmpty) CardOverlayRenderer.Outside.Add((node, have.Decor));
        }
        _goneOffered.Clear();
        foreach (var key in _offeredTabs.Keys)
            if (!seen.Contains(key)) _goneOffered.Add(key);
        foreach (var gone in _goneOffered) _offeredTabs.Remove(gone);
    }

    private readonly HashSet<ulong> _seenOffered = new();

    /// <summary>
    /// An offered card's tabs (no decor: a card of yours, in one of your piles: picking one to remove, upgrade or transform isn't an offer),
    /// worked out when the card on the node, the settings or your run change, never on every frame: CardModel.Pile looks through all your
    /// piles with a closure on every call (a dev review: 0.5 KB a frame on a card reward). Your deck's size is in what the tabs are kept by
    /// (AddRunState), so a card taken into it is looked at again. Dev mode's check compares them with the tabs kept.
    /// </summary>
    private (CardModel Model, CardDecor? Decor) OfferedTab(ulong id, CardModel model, Player me, ReminderPlace place, bool check)
    {
        var decor = model.Pile != null ? null : Boosts(model, me, place);
        if (check && _offeredTabs.TryGetValue(id, out var kept) && kept.Model == model)
            _offeredTabsReuse.Compare((kept.Decor == null) == (decor == null)
                && (decor == null || kept.Decor!.Tabs.Select(tab => tab.Key).SequenceEqual(decor.Tabs.Select(tab => tab.Key))),
                "an offered card's tabs changed while the settings and your run didn't");
        return (model, decor);
    }
    private readonly List<ulong> _goneOffered = new();

    /// <summary>A tab per relic of yours that boosts this card (or a card it makes), with the relic's switch for this place.</summary>
    private CardDecor Boosts(CardModel card, Player me, ReminderPlace place)
    {
        var decor = new CardDecor();
        foreach (var relic in me.Relics)
        {
            var name = relic.GetType().Name;
            if (place == ReminderPlace.Reward && ChoiceReminders.For(name) is { OnCardReward: true })
            {
                var rewardKey = ChoiceReminders.Key(place, name);
                if (_ctx.Settings.TabEnabled(rewardKey) && RewardTab(relic, me, rewardKey) is { } tab) decor.AddTab(tab);
                continue;
            }
            if (CardAffinity.For(name) is not { Kind: AffinityKind.Boosts } rule) continue;
            var key = ChoiceReminders.Key(place, name);
            if (ChoiceReminders.For(name) is not { CardTab: true } || !_ctx.Settings.TabEnabled(key)) continue;
            if (!CardAffinity.AppliesTo(rule, card) && !CardAffinity.Made(card).Any(otherCard => CardAffinity.AppliesTo(rule, otherCard))) continue;
            decor.AddTab(key, GameIcons.Relic(relic));
        }
        return decor;
    }

    /// <summary>The cards on screen (OfferedCards), one list kept and filled again each frame.</summary>
    private static readonly List<(NCard Node, ReminderPlace Place)> Found = new();

    /// <summary>Every card the last search found, shown or not (Shown picks the ones on screen each frame), and what decides when to search again.</summary>
    private static readonly List<(NCard Node, ReminderPlace Place)> Kept = new();
    /// <summary>Dev mode's check: what a search finds on screen now, against what the kept cards show.</summary>
    private static readonly List<(NCard Node, ReminderPlace Place)> Checked = new();
    private static readonly SpireQoL.Combat.ChangeGate OfferedGate = new("the offered cards' search");

    /// <summary>
    /// The cards on offer. Searched only when what could change them does: the screen on top, the shop opening, a press or release (a
    /// purchase), the window's size (a grid of cards is built again for it: NCardGrid's InitGrid), and a few more times in the second after
    /// (cards that animate in); otherwise the cards found last time, less any gone, each shown while it's on screen: a grid of cards
    /// (an event's) hides the holders past its last card and shows them again as it scrolls, with no press as it glides on
    /// (NCardGrid.AssignCardsToRow), and the card it shows on a holder then is read on every frame (OfferedCardTabs). Walking the screen's
    /// whole tree every frame touched every node in it (decision #177: a shop made garbage every frame). Dev mode searches anyway once a
    /// second and logs it if the kept cards were stale.
    /// </summary>
    private static List<(NCard Node, ReminderPlace Place)> OfferedCards()
    {
        var hashCode = new HashCode();
        try
        {
            var top = NOverlayStack.Instance?.Peek();
            hashCode.Add(top == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(top));
            hashCode.Add(Sts2State.InCombat);
            var inventory = NMerchantRoom.Instance?.Inventory;
            hashCode.Add(inventory == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(inventory));
            hashCode.Add(inventory != null && GodotObject.IsInstanceValid(inventory) && inventory.IsOpen);
            hashCode.Add(Godot.Input.IsAnythingPressed());
            hashCode.Add(DisplayServer.WindowGetSize());
        }
        catch { hashCode.Add(-1); }
        var reuse = OfferedGate.Decide(hashCode.ToHashCode(), Godot.Time.GetTicksMsec());
        var found = Found;
        if (reuse == SpireQoL.Combat.FrameReuse.Reuse)
        {
            Kept.RemoveAll(static entry => !GodotObject.IsInstanceValid(entry.Node));
            Shown(Kept, found);
            return found;
        }
        var check = reuse == SpireQoL.Combat.FrameReuse.WorkOutAndCheck;
        if (check) Shown(Kept, found); // what the kept cards show now
        Search(Kept);
        if (check)
        {
            Shown(Kept, Checked);
            OfferedGate.Compare(SameOffer(Checked, found), "the cards on offer changed with no change of screen and no press");
        }
        Shown(Kept, found);
        return found;
    }

    /// <summary>The cards of <paramref name="cards"/> on screen now, into <paramref name="shown"/>.</summary>
    private static void Shown(List<(NCard Node, ReminderPlace Place)> cards, List<(NCard Node, ReminderPlace Place)> shown)
    {
        shown.Clear();
        for (var i = 0; i < cards.Count; i++)
            if (GodotObject.IsInstanceValid(cards[i].Node) && cards[i].Node.IsVisibleInTree()) shown.Add(cards[i]);
    }

    /// <summary>
    /// The same cards on offer, each in the same place, in any order: what's kept is a set. Hovering a card brings it to the front of its
    /// row (NGridCardHolder.OnFocus: MoveToFrontSafely, its holder last among its siblings), so the search finds the same cards in another
    /// order with no press and no change of screen (a dev review: the check took that for a change it had missed).
    /// </summary>
    internal static bool SameOffer<TNode>(IReadOnlyList<(TNode Node, ReminderPlace Place)> searched, IReadOnlyList<(TNode Node, ReminderPlace Place)> kept)
        where TNode : class
    {
        if (searched.Count != kept.Count) return false;
        for (var i = 0; i < searched.Count; i++)
        {
            var found = false;
            for (var j = 0; j < kept.Count && !found; j++)
                found = ReferenceEquals(searched[i].Node, kept[j].Node) && searched[i].Place == kept[j].Place;
            if (!found) return false;
        }
        return true;
    }

    /// <summary>
    /// Every card you're being offered now, shown or not (a grid's hidden holders show as it scrolls: Shown picks those on screen): the card
    /// reward screen, an event's choice of cards out of combat (choose a card, a bundle, a grid of cards), and the open shop, walking the
    /// screen on top (while it's shown) and the shop's inventory.
    /// </summary>
    private static void Search(List<(NCard Node, ReminderPlace Place)> found)
    {
        found.Clear();
        try
        {
            var top = NOverlayStack.Instance?.Peek();
            var offer = top is NCardRewardSelectionScreen
                || (!Sts2State.InCombat && top is NChooseACardSelectionScreen or NChooseABundleSelectionScreen or NSimpleCardSelectScreen);
            // A card reward (after a fight, or one an event gives) is the Card rewards column; an event's own choice of cards the Events one.
            var place = top is NCardRewardSelectionScreen ? ReminderPlace.Reward : ReminderPlace.Event;
            if (offer && top is CanvasItem screen && GodotObject.IsInstanceValid(screen) && screen.IsVisibleInTree()) Collect(screen, found, place, depth: 0);
            if (NMerchantRoom.Instance?.Inventory is { } inv && GodotObject.IsInstanceValid(inv) && inv.IsOpen) Collect(inv, found, ReminderPlace.Shop, depth: 0);
        }
        catch { /* a screen closing under us */ }
    }

    /// <summary>
    /// Every card under the node. By index, not GetChildren: on a shop's whole inventory, and GetChildren makes an array at each level (a
    /// dev review: Run Reminders' garbage in a shop).
    /// </summary>
    private static void Collect(Node node, List<(NCard, ReminderPlace)> into, ReminderPlace place, int depth)
    {
        if (depth > 14) return;
        for (var i = 0; i < node.GetChildCount(); i++)
        {
            var child = node.GetChild(i);
            if (!GodotObject.IsInstanceValid(child)) continue;
            if (child is NCard card)
            {
                into.Add((card, place));
                continue;
            }
            Collect(child, into, place, depth + 1);
        }
    }
}
