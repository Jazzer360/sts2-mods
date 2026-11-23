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
    private readonly Dictionary<ulong, (CardModel Model, CardDecor Decor)> _offeredTabs = new();
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
            if (model == null || model.Pile != null) continue;
            var id = node.GetInstanceId();
            seen.Add(id);
            // While an event option runs, what it offers (the odds log).
            if (_window is { } window) window.Offered.Add(model);
            if (recompute || !_offeredTabs.TryGetValue(id, out var have) || have.Model != model)
            {
                var had = _offeredTabs.TryGetValue(id, out var kept) && kept.Model == model ? kept.Decor : null;
                have = (model, Boosts(model, me, place));
                if (reuse == SpireQoL.Combat.FrameReuse.WorkOutAndCheck && had != null)
                    _offeredTabsReuse.Compare(had.Tabs.Select(tab => tab.Key).SequenceEqual(have.Decor.Tabs.Select(tab => tab.Key)), "an offered card's tabs changed while the settings and your run didn't");
                _offeredTabs[id] = have;
            }
            if (!have.Decor.IsEmpty) CardOverlayRenderer.Outside.Add((node, have.Decor));
        }
        _goneOffered.Clear();
        foreach (var key in _offeredTabs.Keys)
            if (!seen.Contains(key)) _goneOffered.Add(key);
        foreach (var gone in _goneOffered) _offeredTabs.Remove(gone);
    }

    private readonly HashSet<ulong> _seenOffered = new();
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

    /// <summary>The cards found by the last search, and what decides when to search again.</summary>
    private static readonly List<(NCard Node, ReminderPlace Place)> Searched = new();
    private static readonly SpireQoL.Combat.ChangeGate OfferedGate = new("the offered cards' search");

    /// <summary>
    /// The cards on offer. Searched only when what could change them does: the screen on top, the shop opening, a press or release (a
    /// purchase), and a few more times in the second after (cards that animate in); otherwise the cards found last time, less any
    /// gone. Walking the screen's whole tree every frame touched every node in it (decision #177: a shop made garbage every frame).
    /// Dev mode searches anyway once a second and logs it if the kept cards were stale.
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
        }
        catch { hashCode.Add(-1); }
        var reuse = OfferedGate.Decide(hashCode.ToHashCode(), Godot.Time.GetTicksMsec());
        var found = Found;
        if (reuse == SpireQoL.Combat.FrameReuse.Reuse)
        {
            found.RemoveAll(static entry => !GodotObject.IsInstanceValid(entry.Node) || !entry.Node.IsVisibleInTree());
            return found;
        }
        Search(Searched);
        if (reuse == SpireQoL.Combat.FrameReuse.WorkOutAndCheck)
            OfferedGate.Compare(Searched.Count == found.Count && Searched.Select(entry => entry.Node).SequenceEqual(found.Select(entry => entry.Node)),
                "the cards on offer changed with no change of screen and no press");
        found.Clear();
        found.AddRange(Searched);
        return found;
    }

    /// <summary>
    /// Every visible card you're being offered now: the card reward screen, an event's choice of cards out of combat (choose a card, a
    /// bundle, a grid of cards), and the open shop, walking the screen on top and the shop's inventory.
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
    /// Every visible card under the node. By index, not GetChildren: every frame on a shop's whole inventory, and GetChildren makes an
    /// array at each level (a dev review: Run Reminders' garbage in a shop).
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
                if (card.IsVisibleInTree()) into.Add((card, place));
                continue;
            }
            Collect(child, into, place, depth + 1);
        }
    }
}
