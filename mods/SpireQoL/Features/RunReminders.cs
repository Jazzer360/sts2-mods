using System.Reflection;
using OpCodes = System.Reflection.Emit.OpCodes;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using SpireQoL.Cards;
using SpireQoL.Data;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// What the four reminders outside fights share (docs/FEATURES.md "Reminders outside fights", decisions #37, #185), like StS1's
/// Relic Reminders: the rules, the state each one keeps and the checks. Each place is a feature of its own, which runs its part of
/// the frame here: Reward Tags (tags on cards you're offered: RewardTagsFeature), Event Reminders (icons beside event options, the
/// chance under them and Inspect: EventRemindersFeature), Shop Reminders (icons beside shop prices: ShopRemindersFeature) and Map
/// Reminders (icons beside the map legend's rows: MapRemindersFeature).
///  - Card rewards: a tag on each offered card for every relic that taking it sets off (Bing Bong,
///    Lucky Fysh, Book of Five Rings on the pick that heals), with the offered-card tags (CardTabs).
///  - Events: the icons of your relics that change what an option does, beside that option (Tungsten
///    Rod on HP loss, Bing Bong on a card or curse, Ectoplasm crossed on gold...). What an option does
///    is its code read as a program (OptionReader, decision #350): its steps in order, with their
///    conditions and amounts, through the event's own methods, its base class's, the rewards it makes
///    and the relics it gives, but not the options it opens next. That's the option's rule, not a roll.
///    The check after you choose holds what happened to the same reading.
/// Rules: Data/ChoiceReminders.cs.
/// </summary>
public sealed partial class RunReminders
{
    private const string NodeName = "SpireQoL_Reminders";
    private const float EventIcon = 40f;
    private static readonly Color SlashColor = new(1f, 0.15f, 0.1f, 0.95f);

    /// <summary>The feature whose time the event option check's hooks count under (OverlayHost.TimeHook).</summary>
    private const string EventHookId = EventRemindersFeature.FeatureId;

    private readonly QolContext _ctx;

    public RunReminders(QolContext ctx) => _ctx = ctx;

    /// <summary>Event Reminders' patch. Observe-only: the event-option check (what flashed after you chose an option).</summary>
    internal static void PatchEventChosen(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(EventOption), nameof(EventOption.Chosen)),
            prefix: new HarmonyMethod(typeof(RunReminders), nameof(BeforeChosen)),
            postfix: new HarmonyMethod(typeof(RunReminders), nameof(AfterChosen)));
    }

    // ------------------------------------------------------------------ check (audit)

    /// <summary>
    /// What the player has that an option can change, to compare before and after it: the deck (each card's upgrades and enchantment too), gold, HP, max
    /// HP, potions and relics.
    /// </summary>
    private sealed record Before(HashSet<CardModel> Deck, int Gold, int Hp, int MaxHp, int Potions, HashSet<RelicModel> Relics,
        Dictionary<CardModel, (int Upgrades, EnchantmentModel? Enchantment)> Cards)
    {
        public static Before Of(Player me) => new(me.Deck.Cards.ToHashSet(ReferenceEqualityComparer.Instance as IEqualityComparer<CardModel>),
            me.Gold, me.Creature.CurrentHp, me.Creature.MaxHp, me.Potions.Count(),
            me.Relics.ToHashSet(ReferenceEqualityComparer.Instance as IEqualityComparer<RelicModel>),
            me.Deck.Cards.ToDictionary(card => card, card => (card.CurrentUpgradeLevel, card.Enchantment), ReferenceEqualityComparer.Instance as IEqualityComparer<CardModel>));
    }

    /// <summary>The option being resolved: relics with a reminder rule that flash while it runs, and the state before it.</summary>
    private sealed class Window
    {
        public required EventOption Option;
        public required OptionReading Reading;
        public ChoiceEffect Effects => Reading.Kinds;
        public required Player Me;
        public required Before Start;
        public readonly List<RelicModel> Flashed = new();
        public readonly List<RelicModel> Subscribed = new();
        /// <summary>Any relic of yours flashed while it ran (not only the reminders' relics): a reaction, whose effects aren't the option's.</summary>
        public bool AnyFlashed;
        public readonly List<RelicModel> Watched = new();
        /// <summary>The relics whose icon the option had (IconsFor, before the settings switches).</summary>
        public HashSet<RelicModel> Shown = new();
        /// <summary>The cards you were offered while it ran (reward and choice screens), for the odds log.</summary>
        public readonly HashSet<CardModel> Offered = new();
        public Task? Task;
        public int Frames;
        public int DoneFrames;
    }

    private static Window? _window;

    private static void BeforeChosen(EventOption __instance)
    {
        using var timing = OverlayHost.TimeHook(EventHookId);
        try
        {
            Close();
            if (__instance.IsLocked || __instance.WasChosen || RunMe() is not { } me) return;
            var window = new Window { Option = __instance, Reading = ReadingOf(__instance), Me = me, Start = Before.Of(me) };
            window.Shown = IconsFor(__instance, me).Select(relicCrossed => relicCrossed.Relic).ToHashSet();
            foreach (var relic in me.Relics)
            {
                relic.Flashed += OnAnyFlashed;
                window.Watched.Add(relic);
                if (ChoiceReminders.For(relic.GetType().Name) is not { On: not ChoiceEffect.None }) continue;
                relic.Flashed += OnFlashed;
                window.Subscribed.Add(relic);
            }
            _window = window;
        }
        catch (Exception ex)
        {
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "reminders:chosen");
        }
    }

    /// <summary>The option's task: the window stays open until it ends (card choices can take a while).</summary>
    private static void AfterChosen(EventOption __instance, Task __result)
    {
        using var timing = OverlayHost.TimeHook(EventHookId);
        try { if (_window is { } window && window.Option == __instance) window.Task = __result; }
        catch (Exception ex) { Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "reminders-chosen"); }
    }

    private static void OnFlashed(RelicModel relic, IEnumerable<MegaCrit.Sts2.Core.Entities.Creatures.Creature> _)
    {
        using var timing = OverlayHost.TimeHook(EventHookId);
        try { if (_window is { } window && !window.Flashed.Contains(relic)) window.Flashed.Add(relic); }
        catch (Exception ex) { Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "reminders-flash"); }
    }

    private static void OnAnyFlashed(RelicModel relic, IEnumerable<MegaCrit.Sts2.Core.Entities.Creatures.Creature> _)
    {
        if (_window is { } window) window.AnyFlashed = true;
    }

    /// <summary>
    /// What the option did, from the player's state before and after it: cards and curses that joined
    /// the deck (new card objects, so transforms count), gold gained, HP lost (not when max HP fell
    /// too: that's max HP loss), potions gained. Gold that Lucky Fysh paid for a card isn't the option's.
    /// Also (decision #351) gold lost, a heal (HP up by more than max HP), max HP up or down, potions lost, cards that left the deck, a
    /// card of yours upgraded or enchanted, relics gained or lost, and a fight started.
    /// </summary>
    private static ChoiceEffect Observed(Window window, out string details)
    {
        var me = window.Me;
        var before = window.Start;
        var added = me.Deck.Cards.Where(card => !before.Deck.Contains(card)).ToList();
        var deck = me.Deck.Cards.ToHashSet(ReferenceEqualityComparer.Instance as IEqualityComparer<CardModel>);
        var removed = before.Deck.Where(card => !deck.Contains(card)).ToList();
        var choiceEffect = ChoiceEffect.None;
        if (added.Any(card => card.Type != MegaCrit.Sts2.Core.Entities.Cards.CardType.Curse)) choiceEffect |= ChoiceEffect.CardToDeck;
        if (added.Any(card => card.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Curse)) choiceEffect |= ChoiceEffect.CurseToDeck;
        var luckyFysh = window.Flashed.Any(relic => relic is LuckyFysh);
        if (me.Gold > before.Gold && !luckyFysh) choiceEffect |= ChoiceEffect.GoldGain;
        if (me.Gold < before.Gold) choiceEffect |= ChoiceEffect.GoldLoss;
        if (me.Creature.CurrentHp < before.Hp && me.Creature.MaxHp >= before.MaxHp) choiceEffect |= ChoiceEffect.HpLoss;
        if (me.Creature.CurrentHp - before.Hp > Math.Max(0, me.Creature.MaxHp - before.MaxHp)) choiceEffect |= ChoiceEffect.Heal;
        if (me.Creature.MaxHp > before.MaxHp) choiceEffect |= ChoiceEffect.MaxHpGain;
        if (me.Creature.MaxHp < before.MaxHp) choiceEffect |= ChoiceEffect.MaxHpLoss;
        if (me.Potions.Count() > before.Potions) choiceEffect |= ChoiceEffect.Potion;
        if (me.Potions.Count() < before.Potions) choiceEffect |= ChoiceEffect.PotionLoss;
        if (removed.Count > 0) choiceEffect |= ChoiceEffect.CardRemoval;
        var kept = me.Deck.Cards.Where(card => before.Cards.ContainsKey(card)).ToList();
        if (kept.Any(card => card.CurrentUpgradeLevel > before.Cards[card].Upgrades)) choiceEffect |= ChoiceEffect.UpgradeCard;
        if (kept.Any(card => card.Enchantment != null && card.Enchantment != before.Cards[card].Enchantment)) choiceEffect |= ChoiceEffect.EnchantCard;
        var relics = me.Relics.ToHashSet(ReferenceEqualityComparer.Instance as IEqualityComparer<RelicModel>);
        if (me.Relics.Any(relic => !before.Relics.Contains(relic))) choiceEffect |= ChoiceEffect.RelicGain;
        if (before.Relics.Any(relic => !relics.Contains(relic))) choiceEffect |= ChoiceEffect.RelicLoss;
        if (Sts2State.InCombat) choiceEffect |= ChoiceEffect.Fight;
        details = $"deck +[{string.Join(", ", added.Select(card => card.GetType().Name))}] -[{string.Join(", ", removed.Select(card => card.GetType().Name))}], gold {before.Gold}→{me.Gold}, HP {before.Hp}/{before.MaxHp}→{me.Creature.CurrentHp}/{me.Creature.MaxHp}, potions {before.Potions}→{me.Potions.Count()}, relics {before.Relics.Count}→{relics.Count}";
        return choiceEffect;
    }

    /// <summary>
    /// The effects the check reported as unread before the option reading (decision #351): the others it holds to the reading only when no relic of
    /// yours flashed while the option ran (a reaction's heal, max HP or gold, Darkstone Periapt's, Dragon Fruit's, isn't the option's).
    /// </summary>
    private const ChoiceEffect CheckedAlways = ChoiceEffect.CardToDeck | ChoiceEffect.CurseToDeck | ChoiceEffect.HpLoss | ChoiceEffect.GoldGain | ChoiceEffect.Potion;

    /// <summary>
    /// Ends the window (the next option, combat starting, or just after the option's task ended) and
    /// checks it. Reported: a relic that reacted without its icon on the option; an effect that happened
    /// that the option's code wasn't read as having (gold, HP loss, cards, curses, potions); an icon whose
    /// effect happened while its relic, which always reacts to it, didn't. Only logged: an effect read
    /// from the code that didn't happen (an option can skip it: an empty card choice, a reward left, full
    /// potion slots, Ectoplasm).
    /// </summary>
    private static void Close()
    {
        var window = _window;
        if (window == null) return;
        _window = null;
        foreach (var relic in window.Subscribed) relic.Flashed -= OnFlashed;
        foreach (var relic in window.Watched) relic.Flashed -= OnAnyFlashed;
        var observed = Observed(window, out var state);
        // The run ended (a win's last Proceed, or dying): the game sets your HP to 0 as it closes the run (RunState.IsGameOver is
        // every player dead), which isn't the option's doing. Logged, not checked.
        if (window.Me.Creature.IsDead)
        {
            ModEntry.Log.Detail($"[audit] [reminders] {window.Option.TextKey}: the run ended, not checked ({state})");
            Telemetry.Audits.Skipped(Telemetry.Audits.Reminders, "event option: the run ended", window.Option.TextKey);
            return;
        }
        var rule = (RelicModel relic) => ChoiceReminders.For(relic.GetType().Name)!;
        var shown = window.Subscribed.Where(window.Shown.Contains).ToList();
        var missed = window.Flashed.Where(relic => !shown.Contains(relic)).Select(relic => relic.GetType().Name).ToList();
        var quiet = shown.Where(relic => !window.Flashed.Contains(relic)).ToList();
        var silent = quiet.Where(relic => (rule(relic).Expect & observed) != 0).Select(relic => relic.GetType().Name).ToList();
        // A relic the option gave you (This or That's Ornate: a random one) does its own thing as you obtain it (Cursed Pearl's
        // gold): its AfterObtained, read like the option's code, is the relic's doing, not an effect the option's code missed.
        var obtained = window.Me.Relics.Where(relic => !window.Start.Relics.Contains(relic) && relic != window.Option.Relic).ToList();
        var fromObtained = obtained.Aggregate(ChoiceEffect.None, (effects, relic) => effects | ObtainEffects(relic));
        var unread = observed & ~window.Effects & ~fromObtained;
        // A card added by an option read as adding a curse (or the other way) is still a card the icons covered.
        if ((window.Effects & (ChoiceEffect.CardToDeck | ChoiceEffect.CurseToDeck)) != 0) unread &= ~ChoiceEffect.CardToDeck;
        if (window.AnyFlashed) unread &= CheckedAlways;
        var unseen = window.Effects & ~observed & ~ChoiceEffect.RestHeal;
        if (!window.Reading.Whole) ModEntry.Log.Detail($"[audit] [reminders] {window.Option.TextKey}: the reading has gaps: {string.Join("; ", window.Reading.Gaps)}");
        var what = $"{window.Option.TextKey}: read {window.Effects}, happened {observed}; " +
                   (obtained.Count == 0 ? "" : $"obtained [{string.Join(", ", obtained.Select(relic => $"{relic.GetType().Name}: {ObtainEffects(relic)}"))}]; ") + $"icons [{string.Join(", ", shown.Select(relic => relic.GetType().Name))}]; flashed [{string.Join(", ", window.Flashed.Select(relic => relic.GetType().Name))}]{(window.AnyFlashed && window.Flashed.Count == 0 ? " (another relic flashed)" : "")}; {state}; reading: {window.Reading}";
        ModEntry.Log.Detail($"[audit] [reminders] {what}");
        if (quiet.Count > silent.Count) ModEntry.Log.Detail($"[audit] [reminders] icon shown, no reaction (logged only): {string.Join(", ", quiet.Select(relic => relic.GetType().Name).Except(silent))}");
        if (unseen != ChoiceEffect.None) ModEntry.Log.Detail($"[audit] [reminders] read but didn't happen (logged only): {unseen}");
        var evt = window.Option.TextKey.Split('.')[0];
        var eventOk = missed.Count == 0 && unread == ChoiceEffect.None && silent.Count == 0;
        Telemetry.Audits.Done(Telemetry.Audits.Reminders, eventOk, eventOk ? "event option"
            : missed.Count > 0 ? "reminder-missed" : unread != ChoiceEffect.None ? "reminder-unread" : "reminder-silent");
        var reporter = Sts2Mods.Core.Diagnostics.ErrorReporter.Instance;
        if (missed.Count > 0)
        {
            reporter?.Mismatch("reminders", $"Relic reacted to an event option without a reminder: {string.Join(", ", missed)}",
                new[] { "reminder-missed" }.Concat(missed).Append(evt), what);
        }
        if (unread != ChoiceEffect.None)
        {
            reporter?.Mismatch("reminders", $"Event option did something its code wasn't read as doing: {unread}",
                new[] { "reminder-unread", unread.ToString(), evt }, what);
        }
        if (silent.Count > 0)
        {
            reporter?.Mismatch("reminders", $"Reminder icon's effect happened but the relic didn't react: {string.Join(", ", silent)}",
                new[] { "reminder-silent" }.Concat(silent).Append(evt), what);
        }
        var added = window.Me.Deck.Cards.Where(card => !window.Start.Deck.Contains(card)).ToList();
        CheckCardRules(window.Me, added, CardsRead.TryGetValue(window.Option, out var read) && read.GivesUpgraded, window.Shown, window.Option.TextKey);
        LogOdds(window, added);
    }

    internal static void TickWindow()
    {
        if (_window is not { } window) return;
        // A fight from the event: what happens in it isn't the option's.
        if (Sts2State.InCombat) { Close(); return; }
        // Half a second after the option's task ends (its relics react within it), or a long timeout.
        if (window.Task is { IsCompleted: true }) { if (++window.DoneFrames > 30) Close(); }
        else if (++window.Frames > 100_000) Close();
    }

    /// <summary>Event Reminders' Inspect screen, made once the overlay exists.</summary>
    internal void AttachInspect() => _inspect ??= new SpireQoL.Menu.InspectScreen(_ctx.Host);

    /// <summary>The Inspect screen's frame (open or not).</summary>
    internal void InspectFrame() => _inspect?.OnFrame();

    // ------------------------------------------------------------------ card reward tabs

    /// <summary>
    /// A relic's tab on a card a reward offers (decision #83), or null for none. Book of Five Rings is on every card (taking one
    /// counts toward its heal), with Regen's green plus in its corner on the pick that heals, crossed when part of that heal would be
    /// wasted (it heals more than the HP you're missing).
    /// </summary>
    internal static TabSpec? RewardTab(RelicModel relic, Player me, string key)
    {
        if (relic is BookOfFiveRings)
        {
            if (SetOffByTaking(relic, me) != true) return new TabSpec(key, GameIcons.Relic(relic));
            var heal = Sts2State.Var(relic, "Heal", fallback: 20);
            var missing = me.Creature.MaxHp - me.Creature.CurrentHp;
            return new TabSpec(key, GameIcons.Relic(relic), Crossed: heal > missing, Marker: GameIcons.Heal());
        }
        return SetOffByTaking(relic, me) == true ? new TabSpec(key, GameIcons.Relic(relic)) : null;
    }

    /// <summary>
    /// Whether taking a card offered by a card reward sets this relic off (its AfterCardChangedPiles on a card joining your deck):
    /// Bing Bong copies it, Lucky Fysh gives gold, Book of Five Rings heals on the pick that reaches its count (a pick adds two
    /// cards with Bing Bong, the copy counting too). Null for a relic that has no card reward tab.
    /// </summary>
    internal static bool? SetOffByTaking(RelicModel relic, Player me)
    {
        switch (relic)
        {
            case BingBong:
                return true;
            case LuckyFysh:
                // None with Ectoplasm (ModifyGoldGained to 0): then taking a card brings nothing.
                return GoldAfterRelics(me, Sts2State.Var(relic, "Gold", fallback: 15)) > 0;
            case BookOfFiveRings:
            {
                var every = Sts2State.Var(relic, "Cards", fallback: 5);
                if (every <= 0) return false;
                var added = Sts2State.Private(relic, "CardsAdded", fallback: -1);
                if (added < 0) return false;
                return added % every + PerPick(me) >= every;
            }
        }
        return null;
    }

    /// <summary>Cards a pick adds to your deck: two with Bing Bong (its copy fires the deck hooks too).</summary>
    private static int PerPick(Player me) => me.Relics.Any(relic => relic is BingBong) ? 2 : 1;

    /// <summary>
    /// Gold after Bowler Hat, Ectoplasm and anything else that modifies it, through the game's own query
    /// (`Hook.ModifyGoldGained`: plain math, no side effects; the flashes come from AfterModifyingGoldGained,
    /// which isn't called). The game adds it truncated (PlayerCmd.GainGold).
    /// </summary>
    private static int GoldAfterRelics(Player me, int amount)
        => (int)MegaCrit.Sts2.Core.Hooks.Hook.ModifyGoldGained(me.RunState, null, amount, me, out _);

    /// <summary>A shop price for the Guide page: the relic icons left of it and the net price under it, as on a shop slot.</summary>
    internal static Control ExampleShopPrice(IEnumerable<(RelicModel Relic, bool Crossed)> icons, int price, int? net)
    {
        const float size = 30;
        var row = UiKit.Row(separation: 6);
        foreach (var (relic, crossed) in icons) row.AddChild(Backed(Crossable(IconOf(relic), size, crossed), size));
        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride(GodotNames.Name("separation"), 0);
        var priceLabel = UiKit.Label($"{price}", size: 26, new Color(1f, 0.85f, 0.3f));
        priceLabel.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(priceLabel);
        if (net is { } netGold)
        {
            var netLabel = UiKit.Label($"{netGold}", (int)Math.Round(size * NetScale), NetColor);
            netLabel.HorizontalAlignment = HorizontalAlignment.Center;
            col.AddChild(netLabel);
        }
        row.AddChild(col);
        return row;
    }

    /// <summary>An event option's relic icons for the Guide page, as left of an option button.</summary>
    internal static Control ExampleEventIcons(IEnumerable<(RelicModel Relic, bool Crossed, string? Odds)> icons)
    {
        var row = UiKit.Row(separation: 4);
        foreach (var (relic, crossed, odds) in icons)
        {
            var icon = Crossable(IconOf(relic), EventIcon, crossed);
            if (odds != null) icon.AddChild(OddsLabel(odds, EventIcon));
            row.AddChild(icon);
        }
        return row;
    }

    private static Texture2D? IconOf(RelicModel relic)
    {
        try
        {
            return relic.Icon;
        }
        catch
        {
            return null; // missing art: text only
        }
    }

    // ------------------------------------------------------------------ card reward check

    /// <summary>What the card reward tabs showed while the screen was open, to check after a pick.</summary>
    private sealed class RewardWatch
    {
        public required Player Me;
        public required Before Start;
        public required int PerPick;
        public int? FyshGold;
        public bool? BookHeals;
        public int BookAdded = -1;
        public int BookEvery;
        public int ClosedFrames = -1;
    }

    private RewardWatch? _reward;

    /// <summary>
    /// While the card reward screen is open, keeps what its tabs predict; once it closes and the pick has settled
    /// (Bing Bong's copy comes after), checks it when a card was taken: the cards added (two with Bing
    /// Bong), Lucky Fysh's gold, and whether Book of Five Rings healed exactly when its tab showed.
    /// Skipping or sacrificing adds no card and isn't checked.
    /// </summary>
    internal void WatchReward()
    {
        Player? me = null;
        try
        {
            if (NOverlayStack.Instance?.Peek() is NCardRewardSelectionScreen screen && GodotObject.IsInstanceValid(screen) && screen.IsVisibleInTree()) me = RunMe();
        }
        catch { /* the screen closing under us */ }
        if (me != null)
        {
            if (_reward == null || _reward.ClosedFrames >= 0)
            {
                CheckReward();
                var perPick = PerPick(me);
                var rewardWatch = new RewardWatch { Me = me, Start = Before.Of(me), PerPick = perPick };
                if (me.Relics.OfType<LuckyFysh>().FirstOrDefault() is { } fysh) rewardWatch.FyshGold = GoldAfterRelics(me, Sts2State.Var(fysh, "Gold", fallback: 15)) * perPick;
                if (me.Relics.OfType<BookOfFiveRings>().FirstOrDefault() is { } book)
                {
                    rewardWatch.BookEvery = Sts2State.Var(book, "Cards", fallback: 5);
                    rewardWatch.BookAdded = Sts2State.Private(book, "CardsAdded", fallback: -1);
                    if (rewardWatch.BookEvery > 0 && rewardWatch.BookAdded >= 0) rewardWatch.BookHeals = SetOffByTaking(book, me);
                }
                _reward = rewardWatch;
            }
            return;
        }
        if (_reward == null) return;
        if (_reward.ClosedFrames < 0) _reward.ClosedFrames = 0;
        if (++_reward.ClosedFrames > 90) CheckReward();
    }

    private void CheckReward()
    {
        var rewardWatch = _reward;
        _reward = null;
        if (rewardWatch == null || rewardWatch.ClosedFrames < 0) return;
        var me = rewardWatch.Me;
        var added = me.Deck.Cards.Count(card => !rewardWatch.Start.Deck.Contains(card));
        if (added == 0) return; // skipped or sacrificed
        var problems = new List<(string Key, string Text)>();
        if (added != rewardWatch.PerPick) problems.Add(("cards", $"{added} cards joined the deck, predicted {rewardWatch.PerPick}"));
        var gold = me.Gold - rewardWatch.Start.Gold;
        if (rewardWatch.FyshGold is { } fyshGold && gold != fyshGold) problems.Add((nameof(LuckyFysh), $"Lucky Fysh: gold +{gold}, predicted +{fyshGold}"));
        if (rewardWatch.BookHeals is { } heals && me.Relics.OfType<BookOfFiveRings>().FirstOrDefault() is { } book)
        {
            var now = Sts2State.Private(book, "CardsAdded", fallback: -1);
            if (now >= 0)
            {
                var healed = now / rewardWatch.BookEvery > rewardWatch.BookAdded / rewardWatch.BookEvery;
                if (healed != heals) problems.Add((nameof(BookOfFiveRings), $"Book of Five Rings: {(healed ? "healed" : "didn't heal")} ({rewardWatch.BookAdded}→{now} cards), predicted {(heals ? "a heal" : "no heal")}"));
            }
        }
        CheckCardRules(me, me.Deck.Cards.Where(card => !rewardWatch.Start.Deck.Contains(card)).ToList(), givesUpgraded: false, shown: null, "card reward");
        var what = $"card reward: {added} added (predicted {rewardWatch.PerPick}), gold {rewardWatch.Start.Gold}→{me.Gold}";
        ModEntry.Log.Detail($"[audit] [reminders] {what}{(problems.Count == 0 ? ", matched" : "; " + string.Join("; ", problems.Select(problem => problem.Text)))}");
        Telemetry.Audits.Done(Telemetry.Audits.Reminders, problems.Count == 0, problems.Count == 0 ? "card reward" : "reminder-reward");
        if (problems.Count > 0)
        {
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("reminders", $"Card reward tabs were wrong: {string.Join(", ", problems.Select(problem => problem.Key))}",
                new[] { "reminder-reward" }.Concat(problems.Select(problem => problem.Key)), what + "\n" + string.Join("\n", problems.Select(problem => problem.Text)));
        }
    }

    // ------------------------------------------------------------------ event options

    /// <summary>What the event's and the shop's icons were last worked out from, and what they showed (dev mode's check compares).</summary>
    private readonly SpireQoL.Combat.ChangeGate _eventsGate = new("the event options' reminders"), _shopGate = new("the shop's reminders");
    private int _eventsShown, _shopShown;

    /// <summary>
    /// The key of what the event's or the shop's icons read: the screen, anything held (a click picks an option or buys), the
    /// settings and you (AddRunState). Worked out again only when it changes (decision #177: every 6th frame before, garbage while
    /// you read an event or looked at a shop).
    /// </summary>
    private static int ScreenKey(GodotObject screen, MegaCrit.Sts2.Core.Entities.Players.Player? me)
    {
        var hashCode = new HashCode();
        hashCode.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(screen));
        hashCode.Add(Godot.Input.IsAnythingPressed());
        hashCode.Add(QolSettings.Revision);
        if (me != null) AddRunState(ref hashCode, me);
        return hashCode.ToHashCode();
    }

    internal void EventOptions()
    {
        var layout = NEventRoom.Instance?.Layout;
        if (layout == null || !GodotObject.IsInstanceValid(layout)) return;
        var me = RunMe();
        var reuse = _eventsGate.Decide(ScreenKey(layout, me), Godot.Time.GetTicksMsec());
        if (reuse == SpireQoL.Combat.FrameReuse.Reuse) return;
        var shown = new HashCode();
        foreach (var button in layout.OptionButtons)
        {
            if (!GodotObject.IsInstanceValid(button)) continue;
            var icons = me != null && button.Option is { IsLocked: false, IsProceed: false } option
                ? IconsFor(option, me).Where(relicCrossed => _ctx.Settings.TabEnabled(ChoiceReminders.Key(ReminderPlace.Event, relicCrossed.Relic.GetType().Name)))
                    .Select(relicCrossed => (relicCrossed.Relic, relicCrossed.Crossed, _ctx.Settings.ShowEventOdds ? relicCrossed.Odds : null)).ToList()
                : new List<(RelicModel Relic, bool Crossed, string? Odds)>();
            shown.Add(button);
            foreach (var icon in icons) shown.Add(icon);
            Show(button, icons);
            ShowInspect(button, me);
        }
        if (reuse == SpireQoL.Combat.FrameReuse.WorkOutAndCheck) _eventsGate.Compare(shown.ToHashCode() == _eventsShown, "an event option's reminders changed with no change of screen, press or run");
        _eventsShown = shown.ToHashCode();
    }

    /// <summary>
    /// The icons an option gets (before the settings switches, which the check ignores): each relic with a reminder rule for
    /// what the option does. A relic that cares about certain cards only when the option makes the change it looks for to a
    /// card of yours it applies to (Mystic Lighter: an enchantment an Attack of yours can take, Tezcatara's soup), or one of
    /// its cards can come up (as your other relics would change it), with the chance it does (none when sure or unknown).
    /// </summary>
    private static List<(RelicModel Relic, bool Crossed, string? Odds)> IconsFor(EventOption option, Player me)
    {
        var icons = new List<(RelicModel, bool, string?)>();
        var effects = EffectsOf(option);
        if (effects == ChoiceEffect.None) return icons;
        foreach (var relic in me.Relics)
        {
            var name = relic.GetType().Name;
            if (ChoiceReminders.For(name) is not { } rule || (rule.On & effects) == 0) continue;
            string? odds = null;
            if (CardAffinity.For(name) is { } affinity)
            {
                if ((effects & CardAffinity.ChangeEffect(affinity)) != 0 && ChangeReaches(option, me, affinity)) { }
                else if ((effects & ChoiceEffect.CardToDeck) != 0 && CanGet(option, me, affinity))
                {
                    // A card it cares about can't come up after all (its chance is 0%): no icon.
                    odds = OddsText(option, me, affinity, out var never);
                    if (never) continue;
                }
                else continue;
            }
            icons.Add((relic, (rule.CrossedOn & effects) != 0, odds));
        }
        return icons;
    }

    /// <summary>A row of relic icons left of the option button, vertically centered; removed when empty.</summary>
    private static void Show(NEventOptionButton button, List<(RelicModel Relic, bool Crossed, string? Odds)> icons)
    {
        var box = button.GetNodeOrNull<HBoxContainer>(GodotNames.Path(NodeName));
        var key = string.Join(",", icons.Select(icon => icon.Relic.GetType().Name + (icon.Crossed ? "x" : "") + icon.Odds));
        if (icons.Count == 0)
        {
            box.RemoveAndFree();
            return;
        }
        if (box == null || UiKit.KeyOf(box) != key)
        {
            box.RemoveAndFree();
            box = new HBoxContainer { Name = GodotNames.Name(NodeName), MouseFilter = Control.MouseFilterEnum.Ignore };
            box.AddThemeConstantOverride(GodotNames.Name("separation"), 4);
            UiKit.SetKey(box, key);
            foreach (var (relic, crossed, odds) in icons)
            {
                var icon = UiKit.Icon(IconOf(relic), EventIcon);
                if (odds != null) icon.AddChild(OddsLabel(odds, EventIcon));
                icon.MouseFilter = Control.MouseFilterEnum.Ignore;
                if (crossed)
                {
                    var len = EventIcon * 1.414f * 0.9f;
                    var thick = Math.Max(3f, EventIcon * 0.12f);
                    var slash = new ColorRect
                    {
                        Color = SlashColor,
                        MouseFilter = Control.MouseFilterEnum.Ignore,
                        Size = new Vector2(len, thick),
                        Position = new Vector2(EventIcon / 2 - len / 2, EventIcon / 2 - thick / 2),
                        PivotOffset = new Vector2(len / 2, thick / 2),
                        Rotation = -Mathf.Pi / 4,
                    };
                    icon.AddChild(slash);
                }
                box.AddChild(icon);
            }
            button.AddChild(box);
        }
        var width = icons.Count * (EventIcon + 4) - 4;
        box.Position = new Vector2(-width - 10, (button.Size.Y - EventIcon) / 2);
    }

    // ------------------------------------------------------------------ shops

    private const string ShopNodeName = "SpireQoL_ShopReminders";
    private static readonly Color NetColor = new(0.5f, 0.95f, 0.6f);
    /// <summary>Stocked card entries' net price after Lucky Fysh, to check a purchase (TickShopWatch).</summary>
    private readonly Dictionary<MerchantEntry, int> _shopNet = new();
    private (MerchantEntry Entry, int GoldBefore, int Net, int Frames)? _shopWatch;
    private int _shopGold;

    /// <summary>
    /// Shop items (the open merchant inventory): the icons of your relics that buying it sets off, left of its price, and
    /// with Lucky Fysh the net price (after the gold it gives back) under it in green. A card joins your deck (Bing Bong,
    /// Book of Five Rings, Lucky Fysh), a potion is one you'd hold (Sozu and Belt Buckle crossed), and any purchase ends
    /// Maw Bank's gold (crossed). Cards the eggs or Fresnel Lens change already show it on the shop's own card.
    /// </summary>
    internal void ShopSlots()
    {
        var inv = NMerchantRoom.Instance?.Inventory;
        if (inv == null || !GodotObject.IsInstanceValid(inv) || !inv.IsOpen) return;
        var me = RunMe();
        var reuse = _shopGate.Decide(ScreenKey(inv, me), Godot.Time.GetTicksMsec());
        if (reuse == SpireQoL.Combat.FrameReuse.Reuse) return;
        var shown = new HashCode();
        foreach (var slot in inv.GetAllSlots())
        {
            if (!GodotObject.IsInstanceValid(slot)) continue;
            var icons = new List<(RelicModel Relic, bool Crossed)>();
            int? net = null;
            MerchantEntry? entry = null;
            try { entry = slot.Entry; } catch { /* not set up yet */ }
            if (me != null && entry is { IsStocked: true })
            {
                var effects = ChoiceEffect.Purchase | entry switch
                {
                    MerchantCardEntry => ChoiceEffect.CardToDeck,
                    MerchantPotionEntry => ChoiceEffect.Potion,
                    _ => ChoiceEffect.None,
                };
                foreach (var relic in me.Relics)
                {
                    var name = relic.GetType().Name;
                    if (ChoiceReminders.For(name) is not { } rule || (rule.On & effects) == 0 || !rule.ShopIcon
                        || !_ctx.Settings.TabEnabled(ChoiceReminders.Key(ReminderPlace.Shop, name))) continue;
                    if (relic is MawBank { IsUsedUp: true }) continue;
                    icons.Add((relic, (rule.CrossedOn & effects) != 0));
                    if (relic is LuckyFysh fysh && (effects & ChoiceEffect.CardToDeck) != 0)
                    {
                        // Bing Bong's copy joins your deck too, so Lucky Fysh pays twice.
                        var perPick = me.Relics.Any(otherRelic => otherRelic is BingBong) ? 2 : 1;
                        net = entry.Cost - GoldAfterRelics(me, Sts2State.Var(fysh, "Gold", fallback: 15)) * perPick;
                    }
                }
            }
            if (entry != null && me != null)
            {
                if (entry.IsStocked)
                {
                    if (net is { } netGold) _shopNet[entry] = netGold;
                    else _shopNet.Remove(entry);
                }
                else if (_shopNet.Remove(entry, out var predicted))
                {
                    // Just bought: your gold before was what you had on the last look (before the price came off).
                    _shopWatch = (entry, _shopGold, predicted, 0);
                }
            }
            shown.Add(slot);
            shown.Add(entry?.IsStocked);
            shown.Add(net);
            foreach (var icon in icons) shown.Add(icon);
            ShowShop(slot, icons, net);
        }
        if (reuse == SpireQoL.Combat.FrameReuse.WorkOutAndCheck) _shopGate.Compare(shown.ToHashCode() == _shopShown, "a shop slot's reminders changed with no change of screen, press or run");
        _shopShown = shown.ToHashCode();
        if (me != null) _shopGold = me.Gold;
    }

    /// <summary>A row of relic icons left of the item's price, and the net price under it; removed when empty.</summary>
    private static void ShowShop(NMerchantSlot slot, List<(RelicModel Relic, bool Crossed)> icons, int? net)
    {
        var price = Sts2State.Private<Control?>(slot, "_costLabel", fallback: null);
        if (price == null || !GodotObject.IsInstanceValid(price)) return;
        var box = price.GetNodeOrNull<Control>(GodotNames.Path(ShopNodeName));
        var key = string.Join(",", icons.Select(icon => icon.Relic.GetType().Name + (icon.Crossed ? "x" : ""))) + "|" + net;
        if (icons.Count == 0)
        {
            box.RemoveAndFree();
            return;
        }
        var size = Math.Clamp(price.Size.Y, 20f, 36f);
        if (box == null || UiKit.KeyOf(box) != key)
        {
            box.RemoveAndFree();
            box = new Control { Name = GodotNames.Name(ShopNodeName), MouseFilter = Control.MouseFilterEnum.Ignore };
            UiKit.SetKey(box, key);
            var row = new HBoxContainer { Name = GodotNames.Name("Icons"), MouseFilter = Control.MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride(GodotNames.Name("separation"), (int)Math.Round(size * ShopIconGap));
            foreach (var (relic, crossed) in icons) row.AddChild(Backed(Crossable(IconOf(relic), size, crossed), size));
            box.AddChild(row);
            if (net is { } netGold)
            {
                var label = UiKit.Label($"{netGold}", (int)Math.Round(size * NetScale), NetColor);
                label.Name = GodotNames.Name("Net");
                label.MouseFilter = Control.MouseFilterEnum.Ignore;
                label.HorizontalAlignment = HorizontalAlignment.Center;
                box.AddChild(label);
            }
            price.AddChild(box);
        }
        var gap = (int)Math.Round(size * ShopIconGap);
        var width = icons.Count * (size + gap) - gap;
        // Left of the game's coin, which sits beside the label, not over it, with room for the icons' backdrops.
        var left = PriceLeft(slot, price) - price.GlobalPosition.X;
        if (box.GetNodeOrNull<Control>(GodotNames.Path("Icons")) is { } row2) row2.Position = new Vector2(left - width - size * BackdropPad - 4, (price.Size.Y - size) / 2);
        // Under the price's number, centered on it.
        if (box.GetNodeOrNull<Control>(GodotNames.Path("Net")) is { } netLabel)
        {
            var (textLeft, textWidth) = TextSpan(price);
            netLabel.Size = new Vector2(NetWidth, netLabel.Size.Y);
            netLabel.Position = new Vector2(textLeft + textWidth / 2 - NetWidth / 2, price.Size.Y - 2);
        }
    }

    /// <summary>The net price under a shop price (Lucky Fysh): this much of the price's height, in a box this wide to center in.</summary>
    private const float NetScale = 0.9f, NetWidth = 160f;
    /// <summary>Space between a shop price's relic icons, as a share of their size.</summary>
    private const float ShopIconGap = 0.2f;

    /// <summary>Where a label's text sits within it (left edge, width), from its font and alignment; the whole label when unreadable.</summary>
    private static (float Left, float Width) TextSpan(Control control)
    {
        try
        {
            if (control is Label label && label.GetThemeFont(GodotNames.Name("font")) is { } font)
            {
                var otherX = font.GetStringSize(label.Text, HorizontalAlignment.Left, -1, label.GetThemeFontSize(GodotNames.Name("font_size"))).X;
                var left = label.HorizontalAlignment switch
                {
                    HorizontalAlignment.Center => (label.Size.X - otherX) / 2,
                    HorizontalAlignment.Right => label.Size.X - otherX,
                    _ => 0f,
                };
                return (left, otherX);
            }
        }
        catch { /* unreadable font: the whole label */ }
        return (0, control.Size.X);
    }

    /// <summary>
    /// The left edge (global x) of a shop price with its coin: the label's, or that of a visible picture beside it (the
    /// game's coin icon, anywhere in the slot) that sits level with it and ends at or near the label's left edge.
    /// </summary>
    private static float PriceLeft(Node slot, Control price)
    {
        var rect = price.GetGlobalRect();
        var left = rect.Position.X;
        foreach (var pic in Pictures(slot))
        {
            if (price.GetNodeOrNull(GodotNames.Path(ShopNodeName)) is { } ours && ours.IsAncestorOf(pic) || !pic.IsVisibleInTree()) continue;
            var otherRect = pic.GetGlobalRect();
            var level = otherRect.End.Y > rect.Position.Y && otherRect.Position.Y < rect.End.Y;
            var beside = otherRect.End.X <= rect.Position.X + rect.Size.Y && otherRect.End.X >= rect.Position.X - rect.Size.Y;
            if (level && beside) left = Math.Min(left, otherRect.Position.X);
        }
        return left;
    }

    private static IEnumerable<TextureRect> Pictures(Node node)
    {
        foreach (var otherNode in node.GetChildren())
        {
            if (otherNode is TextureRect icon) yield return icon;
            foreach (var picture in Pictures(otherNode)) yield return picture;
        }
    }

    /// <summary>How far an icon's backdrop reaches past the icon, as a share of its size.</summary>
    private const float BackdropPad = 0.28f;
    private static GradientTexture2D? _backdrop;

    /// <summary>
    /// An icon on a soft dark disc (fading out at its edge), so it reads on any background: the shop's cloth, the map legend's
    /// paper. The holder is the icon's size; the disc reaches a little past it.
    /// </summary>
    internal static Control Backed(Control icon, float size)
    {
        var holder = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(size, size),
            Size = new Vector2(size, size),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        var pad = size * BackdropPad;
        holder.AddChild(new TextureRect
        {
            Texture = Backdrop(),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Position = new Vector2(-pad, -pad),
            Size = new Vector2(size + 2 * pad, size + 2 * pad),
        });
        holder.AddChild(icon);
        return holder;
    }

    private static Texture2D Backdrop()
    {
        if (_backdrop != null) return _backdrop;
        var gradient = new Gradient
        {
            Offsets = new[] { 0f, 0.6f, 1f },
            Colors = new[] { new Color(0, 0, 0, 0.72f), new Color(0, 0, 0, 0.55f), new Color(0, 0, 0, 0) },
        };
        return _backdrop = new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
            Width = 64,
            Height = 64,
        };
    }

    /// <summary>A relic icon, with the red slash when it works against you.</summary>
    private static TextureRect Crossable(Texture2D? tex, float size, bool crossed)
    {
        var icon = UiKit.Icon(tex, size);
        icon.MouseFilter = Control.MouseFilterEnum.Ignore;
        // Keep the icon its own size in any container, so the slash (placed for that size) stays across it.
        icon.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        icon.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        if (!crossed) return icon;
        var len = size * 1.414f * 0.9f;
        var thick = Math.Max(3f, size * 0.12f);
        icon.AddChild(new ColorRect
        {
            Color = SlashColor,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = new Vector2(len, thick),
            Position = new Vector2(size / 2 - len / 2, size / 2 - thick / 2),
            PivotOffset = new Vector2(len / 2, thick / 2),
            Rotation = -Mathf.Pi / 4,
        });
        return icon;
    }

    /// <summary>
    /// After you buy a card with Lucky Fysh: what the purchase cost you in the end against the net price shown, once the
    /// gold has settled (about a second and a half); a newer purchase replaces the watch.
    /// </summary>
    internal void TickShopWatch()
    {
        if (_shopWatch is not { } entryGoldBefore) return;
        if (entryGoldBefore.Frames < 90)
        {
            _shopWatch = entryGoldBefore with { Frames = entryGoldBefore.Frames + 1 };
            return;
        }
        _shopWatch = null;
        if (RunMe() is not { } me) return;
        var paid = entryGoldBefore.GoldBefore - me.Gold;
        var what = $"shop: bought {entryGoldBefore.Entry.GetType().Name}, gold {entryGoldBefore.GoldBefore}→{me.Gold}, predicted net {entryGoldBefore.Net}";
        ModEntry.Log.Detail($"[audit] [reminders] {what}{(paid == entryGoldBefore.Net ? ", matched" : "")}");
        Telemetry.Audits.Done(Telemetry.Audits.Reminders, paid == entryGoldBefore.Net, paid == entryGoldBefore.Net ? "shop" : "reminder-shop");
        if (paid != entryGoldBefore.Net)
        {
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("reminders", "Shop net price after Lucky Fysh was wrong",
                new[] { "reminder-shop", nameof(LuckyFysh) }, what);
        }
    }

    private static readonly PropertyInfo? OnChosenProp = AccessTools.Property(typeof(EventOption), "OnChosen");
    private static readonly ConditionalWeakTable<EventOption, OptionReading> Readings = new();
    private static readonly ConditionalWeakTable<EventOption, StrongBox<ChoiceEffect>> Scans = new();

    /// <summary>What choosing the option does (cached per option): every kind of effect its program reading has (decision #350).</summary>
    public static ChoiceEffect EffectsOf(EventOption option) => ReadingOf(option).Kinds;

    /// <summary>
    /// The option's code read as a program (decision #350, cached per option): its handler, the delegates the event and the option hold as it's
    /// shown (decision #74), and the relic it carries through its AfterObtained. Also reads which cards it can give (the card walk: CardsRead).
    /// </summary>
    internal static OptionReading ReadingOf(EventOption option)
    {
        if (Readings.TryGetValue(option, out var known)) return known;
        var reading = OptionReading.Empty;
        try
        {
            ReadCards(option);
            var relic = option.Relic?.GetType();
            if (OnChosenProp?.GetValue(option) is Delegate handler)
            {
                var target = handler.Target;
                reading = OptionReader.Game.Read(handler.Method, relic, target == null ? null : source => HeldMethods(target, source));
            }
            else if (relic != null) reading = OptionReader.Game.ReadObtained(relic);
        }
        catch (Exception ex)
        {
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "reminders:" + option.TextKey);
        }
        Readings.AddOrUpdate(option, reading);
        return reading;
    }

    /// <summary>
    /// What the option's code calls, by the flag-level scan the program reading replaced (ChoiceReminders.Calls): the check that the reading
    /// misses nothing the scan finds (OptionProgramTests).
    /// </summary>
    internal static ChoiceEffect ScanOf(EventOption option)
    {
        ReadCards(option);
        return Scans.TryGetValue(option, out var box) ? box.Value : ChoiceEffect.None;
    }

    /// <summary>The card walk (which cards the option can give: CardsRead) and its scan of the calls, once per option.</summary>
    private static void ReadCards(EventOption option)
    {
        if (CardsRead.TryGetValue(option, out _)) return;
        var calls = new HashSet<string>();
        var cards = new OptionCards();
        try
        {
            if (OnChosenProp?.GetValue(option) is Delegate handler)
            {
                cards.Target = handler.Target;
                Walk(handler.Method, handler.Method.DeclaringType, calls, new HashSet<MethodBase>(), depth: 0, cards);
            }
            // An ancient's relic: what it does when you take it.
            if (option.Relic is { } relic && relic.GetType().GetMethod(nameof(RelicModel.AfterObtained)) is { } obtained && obtained.DeclaringType == relic.GetType())
            {
                cards.Relic = relic;
                Walk(obtained, relic.GetType(), calls, new HashSet<MethodBase>(), depth: 0, cards);
            }
        }
        catch (Exception ex)
        {
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "reminders-cards-walk:" + option.TextKey);
        }
        Scans.AddOrUpdate(option, new StrongBox<ChoiceEffect>(ChoiceReminders.EffectsOf(calls)));
        CardsRead.AddOrUpdate(option, cards);
    }

    /// <summary>What a relic does as you obtain it (its own AfterObtained), read as a program like an option's code (decision #350).</summary>
    internal static ChoiceEffect ObtainEffects(RelicModel relic) => ObtainReading(relic).Kinds;

    internal static OptionReading ObtainReading(RelicModel relic)
    {
        try { return OptionReader.Game.ReadObtained(relic.GetType()); }
        catch (Exception ex)
        {
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "reminders:obtained:" + relic.GetType().Name);
            return OptionReading.Empty;
        }
    }

    /// <summary>
    /// The methods of the delegates <paramref name="target"/> (the event, or the closure an option's lambda runs in) holds that a delegate call can run,
    /// by where the call gets it: "field Dish.action", only that field's (the Endless Conveyor's current dish); anything else, every one it holds (Neow's
    /// modifier options: the modifier's own option). The game's models' code only (a C# event's listeners, the screens, aren't the option's).
    /// </summary>
    private static IEnumerable<MethodBase> HeldMethods(object target, string source)
    {
        var field = source.StartsWith("field ", StringComparison.Ordinal) ? source[(source.LastIndexOf('.') + 1)..] : null;
        return HeldDelegates(target).Where(held => field == null || held.Field == field)
            .Select(held => held.Delegate.Method).Where(method => method.DeclaringType?.Namespace?.StartsWith("MegaCrit.Sts2.Core.Models", StringComparison.Ordinal) == true)
            .Cast<MethodBase>().Distinct();
    }

    /// <summary>A game mode's class (Blitz's BlitzRunModel): beta only (decision #163); main has no game modes.</summary>
    [BetaOnly, System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool IsRunModel(Type type) => typeof(RunModel).IsAssignableFrom(type);

    private static readonly Func<Type, bool>? GameModeIs = BetaOnly.Gate<Func<Type, bool>>(IsRunModel);

    private static bool IsGameMode(Type? type) => type != null && GameModeIs?.Invoke(type) == true;

    /// <summary>
    /// The card walk: the calls a method makes, following calls (not delegates: those are the next page's options)
    /// into its own class and the classes nested in it (lambdas, async state machines), and into a
    /// game mode's static methods (Blitz's Neow rewards). It reads which cards an option can give (OptionCards),
    /// and its calls are the flag-level scan the option reading is checked against (ScanOf); what an option does
    /// is the option reading's (decision #350).
    /// </summary>
    private static void Walk(MethodBase method, Type? owner, HashSet<string> calls, HashSet<MethodBase> seen, int depth, OptionCards cards)
    {
        if (depth > 6 || !seen.Add(method)) return;
        var body = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            .GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ?? method;
        List<CodeInstruction> code;
        try
        {
            code = SpireQoL.Diagnostics.GameCode.Instructions(body);
        }
        catch
        {
            return; // unreadable
        }
        cards.Read(code);
        foreach (var ins in code)
        {
            if (ins.operand is not MethodBase otherMethod || otherMethod.DeclaringType == null) continue;
            if (ins.opcode != OpCodes.Call && ins.opcode != OpCodes.Callvirt && ins.opcode != OpCodes.Newobj) continue;
            calls.Add(otherMethod.DeclaringType.Name + "." + otherMethod.Name);
            // The card it makes is a curse (Wuppy's Injury: ModelDb.Card<Injury>; the Museum's pick from the curse pool): whatever
            // command adds it, a curse joins the deck.
            if (MakesCurse(otherMethod) || ins.opcode == OpCodes.Newobj && IsCurseCard(otherMethod.DeclaringType)) calls.Add(ChoiceReminders.CurseMade);
            if (owner != null && IsWithin(otherMethod.DeclaringType, owner)) Walk(otherMethod, owner, calls, seen, depth + 1, cards);
            // A delegate the event keeps in its state, called by the option (the Endless Conveyor's dish:
            // GrabSomethingOffTheBelt runs CurrentDish.action): read the ones the event holds now.
            else if (otherMethod.Name == nameof(Action.Invoke) && typeof(Delegate).IsAssignableFrom(otherMethod.DeclaringType) && owner != null && cards.Target != null)
            {
                foreach (var (_, held) in HeldDelegates(cards.Target))
                    if (held.Method.DeclaringType != null && IsWithin(held.Method.DeclaringType, owner)) Walk(held.Method, owner, calls, seen, depth + 1, cards);
            }
            // An event's own part it creates (the Crystal Sphere's minigame, and the items it hides: card rewards, a curse, gold,
            // potions): everything that class does is something the option can do.
            else if (ins.opcode == OpCodes.Newobj && otherMethod.DeclaringType.Namespace?.StartsWith("MegaCrit.Sts2.Core.Events.Custom", StringComparison.Ordinal) == true)
            {
                foreach (var nextMethod in otherMethod.DeclaringType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    Walk(nextMethod, otherMethod.DeclaringType, calls, seen, depth + 1, cards);
            }
            // A game mode's own rewards (Blitz's Neow option runs BlitzRunModel.GiveNeowRewards).
            else if (otherMethod.IsStatic && IsGameMode(otherMethod.DeclaringType)) Walk(otherMethod, otherMethod.DeclaringType, calls, seen, depth + 1, cards);
        }
    }

    /// <summary>
    /// A call that makes or names a curse card: any generic method given a curse card's class (ModelDb.Card&lt;Injury&gt;, Hefty Tablet's
    /// RunState.CreateCard&lt;Injury&gt;, a combat's CreateCard&lt;T&gt;), or ModelDb.CardPool&lt;CurseCardPool&gt; (the Museum's pick from the curse pool).
    /// By the card's class, not by the method that takes it, so a new way of making one is read too.
    /// </summary>
    internal static bool MakesCurse(MethodBase method)
    {
        if (method is not MethodInfo { IsGenericMethod: true } otherMethod) return false;
        foreach (var arg in otherMethod.GetGenericArguments())
        {
            if (arg.Name == "CurseCardPool" && otherMethod.Name == nameof(ModelDb.CardPool)) return true;
            if (IsCurseCard(arg)) return true;
        }
        return false;
    }

    /// <summary>A card class whose canonical card is a Curse (CardModel's type, as the game's model of it has it).</summary>
    internal static bool IsCurseCard(Type type)
    {
        if (!typeof(CardModel).IsAssignableFrom(type) || type.IsAbstract) return false;
        try { return ModelDb.AllCards.FirstOrDefault(card => card.GetType() == type)?.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Curse; }
        catch { return false; }
    }

    /// <summary>
    /// Delegates in an event's (or closure's) instance fields, and in the fields of struct values it holds (the Endless
    /// Conveyor's current Dish record struct), with the field each is in; not a C# event's (its listeners).
    /// </summary>
    private static List<(string Field, Delegate Delegate)> HeldDelegates(object target)
    {
        const BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var found = new List<(string, Delegate)>();
        for (var type = target.GetType(); type != null && type != typeof(object); type = type.BaseType)
        {
            foreach (var field in type.GetFields(bindingFlags))
            {
                if (type.GetEvent(field.Name, bindingFlags) != null) continue;
                object? value;
                try { value = field.GetValue(target); } catch { continue; }
                if (value is Delegate handler) found.AddRange(handler.GetInvocationList().Select(each => (field.Name, each)));
                else if (value != null && field.FieldType.IsValueType && !field.FieldType.IsPrimitive && !field.FieldType.IsEnum)
                    foreach (var inner in field.FieldType.GetFields(bindingFlags))
                    {
                        try { if (inner.GetValue(value) is Delegate innerHandler) found.AddRange(innerHandler.GetInvocationList().Select(each => (inner.Name, each))); } catch { }
                    }
            }
        }
        return found;
    }

    private static bool IsWithin(Type type, Type owner)
    {
        var root = owner;
        while (root.DeclaringType != null) root = root.DeclaringType;
        for (var otherType = type; otherType != null; otherType = otherType.DeclaringType)
        {
            if (otherType == root) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>The local player outside combat (RunManager's run state).</summary>
    private static Player? RunMe() => Sts2State.MeInRun;
}
