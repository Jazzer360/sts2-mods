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
public sealed partial class RunRemindersFeature
{
    private readonly Dictionary<ulong, (CardModel Model, CardDecor Decor)> _offeredTabs = new();
    private int _offeredFrame;

    private void OfferedCardTabs()
    {
        var nodes = OfferedCards();
        var me = nodes.Count > 0 ? RunMe() : null;
        if (me == null)
        {
            _offeredTabs.Clear();
            return;
        }
        // Relics and settings change rarely: the tabs are worked out again a few times a second, drawn every frame.
        var recompute = ++_offeredFrame % 10 == 0;
        var seen = new HashSet<ulong>();
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
                have = (model, Boosts(model, me, place));
                _offeredTabs[id] = have;
            }
            if (!have.Decor.IsEmpty) CardOverlayRenderer.Outside.Add((node, have.Decor));
        }
        foreach (var gone in _offeredTabs.Keys.Where(key => !seen.Contains(key)).ToList()) _offeredTabs.Remove(gone);
    }

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

    /// <summary>
    /// The card nodes on screen that you're being offered: the card reward screen, an event's choice of cards out of combat
    /// (choose a card, a bundle, a grid of cards), and the open shop.
    /// </summary>
    private static List<(NCard Node, ReminderPlace Place)> OfferedCards()
    {
        var found = new List<(NCard, ReminderPlace)>();
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
        return found;
    }

    private static void Collect(Node node, List<(NCard, ReminderPlace)> into, ReminderPlace place, int depth)
    {
        if (depth > 14) return;
        foreach (var child in node.GetChildren())
        {
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
