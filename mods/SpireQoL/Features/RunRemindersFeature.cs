using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using System.Reflection;
using OpCodes = System.Reflection.Emit.OpCodes;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
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
using MegaCrit.Sts2.Core.Runs;
using SpireQoL.Cards;
using SpireQoL.Data;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Out-of-combat reminders (docs/FEATURES.md "Run Reminders", decision #37), like StS1's Relic
/// Reminders:
///  - Card rewards: a tab on each offered card for every relic that taking it sets off (Bing Bong,
///    Lucky Fysh, Book of Five Rings on the pick that heals), with the offered-card tabs (CardTabs).
///  - Events: the icons of your relics that change what an option does, beside that option (Tungsten
///    Rod on HP loss, Bing Bong on a card or curse, Ectoplasm crossed on gold...). What an option does
///    is read from its own code: the game commands its OnChosen calls, following the event's own
///    methods but not the options it opens next. That's the option's rule, not a roll.
/// Rules: Data/ChoiceReminders.cs.
/// </summary>
public sealed partial class RunRemindersFeature : IFeature, IFrameFeature
{
    private const string NodeName = "SpireQoL_Reminders";
    private const float EventIcon = 40f;
    private static readonly Color SlashColor = new(1f, 0.15f, 0.1f, 0.95f);

    private readonly QolContext _ctx;
    private int _frame;

    public RunRemindersFeature(QolContext ctx) => _ctx = ctx;

    public string Id => "run-reminders";
    public string DisplayName => "Run Reminders";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Observe-only: the event-option check (what flashed after you chose an option).
        harmony.Patch(AccessTools.Method(typeof(EventOption), nameof(EventOption.Chosen)),
            prefix: new HarmonyMethod(typeof(RunRemindersFeature), nameof(BeforeChosen)),
            postfix: new HarmonyMethod(typeof(RunRemindersFeature), nameof(AfterChosen)));
    }

    // ------------------------------------------------------------------ check (audit)

    /// <summary>What the player has that an option can change, to compare before and after it.</summary>
    private sealed record Before(HashSet<CardModel> Deck, int Gold, int Hp, int MaxHp, int Potions)
    {
        public static Before Of(Player me) => new(me.Deck.Cards.ToHashSet(ReferenceEqualityComparer.Instance as IEqualityComparer<CardModel>),
            me.Gold, me.Creature.CurrentHp, me.Creature.MaxHp, me.Potions.Count());
    }

    /// <summary>The option being resolved: relics with a reminder rule that flash while it runs, and the state before it.</summary>
    private sealed class Window
    {
        public required EventOption Option;
        public required ChoiceEffect Effects;
        public required Player Me;
        public required Before Start;
        public readonly List<RelicModel> Flashed = new();
        public readonly List<RelicModel> Subscribed = new();
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
        try
        {
            Close();
            if (__instance.IsLocked || __instance.WasChosen || RunMe() is not { } me) return;
            var window = new Window { Option = __instance, Effects = EffectsOf(__instance), Me = me, Start = Before.Of(me) };
            window.Shown = IconsFor(__instance, me).Select(relicCrossed => relicCrossed.Relic).ToHashSet();
            foreach (var relic in me.Relics)
            {
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
        try { if (_window is { } window && window.Option == __instance) window.Task = __result; }
        catch (Exception ex) { Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "reminders-chosen"); }
    }

    private static void OnFlashed(RelicModel relic, IEnumerable<MegaCrit.Sts2.Core.Entities.Creatures.Creature> _)
    {
        try { if (_window is { } window && !window.Flashed.Contains(relic)) window.Flashed.Add(relic); }
        catch (Exception ex) { Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "reminders-flash"); }
    }

    /// <summary>
    /// What the option did, from the player's state before and after it: cards and curses that joined
    /// the deck (new card objects, so transforms count), gold gained, HP lost (not when max HP fell
    /// too: that's max HP loss), potions gained. Gold that Lucky Fysh paid for a card isn't the option's.
    /// </summary>
    private static ChoiceEffect Observed(Window window, out string details)
    {
        var me = window.Me;
        var before = window.Start;
        var added = me.Deck.Cards.Where(card => !before.Deck.Contains(card)).ToList();
        var choiceEffect = ChoiceEffect.None;
        if (added.Any(card => card.Type != MegaCrit.Sts2.Core.Entities.Cards.CardType.Curse)) choiceEffect |= ChoiceEffect.CardToDeck;
        if (added.Any(card => card.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Curse)) choiceEffect |= ChoiceEffect.CurseToDeck;
        var luckyFysh = window.Flashed.Any(relic => relic is LuckyFysh);
        if (me.Gold > before.Gold && !luckyFysh) choiceEffect |= ChoiceEffect.GoldGain;
        if (me.Creature.CurrentHp < before.Hp && me.Creature.MaxHp >= before.MaxHp) choiceEffect |= ChoiceEffect.HpLoss;
        if (me.Potions.Count() > before.Potions) choiceEffect |= ChoiceEffect.Potion;
        details = $"deck +[{string.Join(", ", added.Select(card => card.GetType().Name))}], gold {before.Gold}→{me.Gold}, HP {before.Hp}/{before.MaxHp}→{me.Creature.CurrentHp}/{me.Creature.MaxHp}, potions {before.Potions}→{me.Potions.Count()}";
        return choiceEffect;
    }

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
        var observed = Observed(window, out var state);
        // The run ended (a win's last Proceed, or dying): the game sets your HP to 0 as it closes the run (RunState.IsGameOver is
        // every player dead), which isn't the option's doing. Logged, not checked.
        if (window.Me.Creature.IsDead)
        {
            ModEntry.Log.Info($"[audit] [reminders] {window.Option.TextKey}: the run ended, not checked ({state})");
            Telemetry.Audits.Skipped(Telemetry.Audits.Reminders, "event option: the run ended", window.Option.TextKey);
            return;
        }
        var rule = (RelicModel relic) => ChoiceReminders.For(relic.GetType().Name)!;
        var shown = window.Subscribed.Where(window.Shown.Contains).ToList();
        var missed = window.Flashed.Where(relic => !shown.Contains(relic)).Select(relic => relic.GetType().Name).ToList();
        var quiet = shown.Where(relic => !window.Flashed.Contains(relic)).ToList();
        var silent = quiet.Where(relic => (rule(relic).Expect & observed) != 0).Select(relic => relic.GetType().Name).ToList();
        var unread = observed & ~window.Effects;
        // A card added by an option read as adding a curse (or the other way) is still a card the icons covered.
        if ((window.Effects & (ChoiceEffect.CardToDeck | ChoiceEffect.CurseToDeck)) != 0) unread &= ~ChoiceEffect.CardToDeck;
        var unseen = window.Effects & ~observed & ~ChoiceEffect.RestHeal;
        var what = $"{window.Option.TextKey}: read {window.Effects}, happened {observed}; icons [{string.Join(", ", shown.Select(relic => relic.GetType().Name))}]; flashed [{string.Join(", ", window.Flashed.Select(relic => relic.GetType().Name))}]; {state}";
        ModEntry.Log.Info($"[audit] [reminders] {what}");
        if (quiet.Count > silent.Count) ModEntry.Log.Info($"[audit] [reminders] icon shown, no reaction (logged only): {string.Join(", ", quiet.Select(relic => relic.GetType().Name).Except(silent))}");
        if (unseen != ChoiceEffect.None) ModEntry.Log.Info($"[audit] [reminders] read but didn't happen (logged only): {unseen}");
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

    private static void TickWindow()
    {
        if (_window is not { } window) return;
        // A fight from the event: what happens in it isn't the option's.
        if (Sts2State.InCombat) { Close(); return; }
        // Half a second after the option's task ends (its relics react within it), or a long timeout.
        if (window.Task is { IsCompleted: true }) { if (++window.DoneFrames > 30) Close(); }
        else if (++window.Frames > 100_000) Close();
    }

    public void Attach(OverlayHost host)
    {
        _inspect = new SpireQoL.Menu.InspectScreen(_ctx.Host);
    }

    public void OnFrame(OverlayHost host)
    {
        TickWindow();
        WatchReward();
        // Event options and shop items change only when the page does: every few frames is plenty.
        if (++_frame % 6 == 0)
        {
            EventOptions();
            ShopSlots();
            MapLegend();
        }
        TickShopWatch();
        OfferedCardTabs();
        _inspect?.OnFrame();
    }

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
            var heal = Sts2State.Var(relic, "Heal", 20);
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
                return GoldAfterRelics(me, Sts2State.Var(relic, "Gold", 15)) > 0;
            case BookOfFiveRings:
            {
                var every = Sts2State.Var(relic, "Cards", 5);
                if (every <= 0) return false;
                var added = Sts2State.Private(relic, "CardsAdded", -1);
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
        var row = UiKit.Row(6);
        foreach (var (relic, crossed) in icons) row.AddChild(Backed(Crossable(IconOf(relic), size, crossed), size));
        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 0);
        var priceLabel = UiKit.Label($"{price}", 26, new Color(1f, 0.85f, 0.3f));
        priceLabel.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(priceLabel);
        if (net is { } n)
        {
            var netLabel = UiKit.Label($"{n}", (int)Math.Round(size * NetScale), NetColor);
            netLabel.HorizontalAlignment = HorizontalAlignment.Center;
            col.AddChild(netLabel);
        }
        row.AddChild(col);
        return row;
    }

    /// <summary>An event option's relic icons for the Guide page, as left of an option button.</summary>
    internal static Control ExampleEventIcons(IEnumerable<(RelicModel Relic, bool Crossed, string? Odds)> icons)
    {
        var row = UiKit.Row(4);
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
    private void WatchReward()
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
                if (me.Relics.OfType<LuckyFysh>().FirstOrDefault() is { } fysh) rewardWatch.FyshGold = GoldAfterRelics(me, Sts2State.Var(fysh, "Gold", 15)) * perPick;
                if (me.Relics.OfType<BookOfFiveRings>().FirstOrDefault() is { } book)
                {
                    rewardWatch.BookEvery = Sts2State.Var(book, "Cards", 5);
                    rewardWatch.BookAdded = Sts2State.Private(book, "CardsAdded", -1);
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
            var now = Sts2State.Private(book, "CardsAdded", -1);
            if (now >= 0)
            {
                var healed = now / rewardWatch.BookEvery > rewardWatch.BookAdded / rewardWatch.BookEvery;
                if (healed != heals) problems.Add((nameof(BookOfFiveRings), $"Book of Five Rings: {(healed ? "healed" : "didn't heal")} ({rewardWatch.BookAdded}→{now} cards), predicted {(heals ? "a heal" : "no heal")}"));
            }
        }
        CheckCardRules(me, me.Deck.Cards.Where(card => !rewardWatch.Start.Deck.Contains(card)).ToList(), false, null, "card reward");
        var what = $"card reward: {added} added (predicted {rewardWatch.PerPick}), gold {rewardWatch.Start.Gold}→{me.Gold}";
        ModEntry.Log.Info($"[audit] [reminders] {what}{(problems.Count == 0 ? ", matched" : "; " + string.Join("; ", problems.Select(problem => problem.Text)))}");
        Telemetry.Audits.Done(Telemetry.Audits.Reminders, problems.Count == 0, problems.Count == 0 ? "card reward" : "reminder-reward");
        if (problems.Count > 0)
        {
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("reminders", $"Card reward tabs were wrong: {string.Join(", ", problems.Select(problem => problem.Key))}",
                new[] { "reminder-reward" }.Concat(problems.Select(problem => problem.Key)), what + "\n" + string.Join("\n", problems.Select(problem => problem.Text)));
        }
    }

    // ------------------------------------------------------------------ event options

    private void EventOptions()
    {
        var layout = NEventRoom.Instance?.Layout;
        if (layout == null || !GodotObject.IsInstanceValid(layout)) return;
        var me = RunMe();
        foreach (var button in layout.OptionButtons)
        {
            if (!GodotObject.IsInstanceValid(button)) continue;
            var icons = me != null && button.Option is { IsLocked: false, IsProceed: false } option
                ? IconsFor(option, me).Where(relicCrossed => _ctx.Settings.TabEnabled(ChoiceReminders.Key(ReminderPlace.Event, relicCrossed.Relic.GetType().Name)))
                    .Select(relicCrossed => (relicCrossed.Relic, relicCrossed.Crossed, _ctx.Settings.ShowEventOdds ? relicCrossed.Odds : null)).ToList()
                : new List<(RelicModel Relic, bool Crossed, string? Odds)>();
            Show(button, icons);
            ShowInspect(button, me);
        }
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
                else if ((effects & ChoiceEffect.CardToDeck) != 0 && CanGet(option, me, affinity)) odds = OddsText(option, me, affinity);
                else continue;
            }
            icons.Add((relic, (rule.CrossedOn & effects) != 0, odds));
        }
        return icons;
    }

    /// <summary>A row of relic icons left of the option button, vertically centered; removed when empty.</summary>
    private static void Show(NEventOptionButton button, List<(RelicModel Relic, bool Crossed, string? Odds)> icons)
    {
        var box = button.GetNodeOrNull<HBoxContainer>(NodeName);
        var key = string.Join(",", icons.Select(icon => icon.Relic.GetType().Name + (icon.Crossed ? "x" : "") + icon.Odds));
        if (icons.Count == 0)
        {
            box?.QueueFree();
            return;
        }
        if (box == null || (string)box.GetMeta("key", "") != key)
        {
            box?.QueueFree();
            box = new HBoxContainer { Name = NodeName, MouseFilter = Control.MouseFilterEnum.Ignore };
            box.AddThemeConstantOverride("separation", 4);
            box.SetMeta("key", key);
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
    private void ShopSlots()
    {
        var inv = NMerchantRoom.Instance?.Inventory;
        if (inv == null || !GodotObject.IsInstanceValid(inv) || !inv.IsOpen) return;
        var me = RunMe();
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
                        net = entry.Cost - GoldAfterRelics(me, Sts2State.Var(fysh, "Gold", 15)) * perPick;
                    }
                }
            }
            if (entry != null && me != null)
            {
                if (entry.IsStocked)
                {
                    if (net is { } n) _shopNet[entry] = n;
                    else _shopNet.Remove(entry);
                }
                else if (_shopNet.Remove(entry, out var predicted))
                {
                    // Just bought: your gold before was what you had on the last look (before the price came off).
                    _shopWatch = (entry, _shopGold, predicted, 0);
                }
            }
            ShowShop(slot, icons, net);
        }
        if (me != null) _shopGold = me.Gold;
    }

    /// <summary>A row of relic icons left of the item's price, and the net price under it; removed when empty.</summary>
    private static void ShowShop(NMerchantSlot slot, List<(RelicModel Relic, bool Crossed)> icons, int? net)
    {
        var price = Sts2State.Private<Control?>(slot, "_costLabel", null);
        if (price == null || !GodotObject.IsInstanceValid(price)) return;
        var box = price.GetNodeOrNull<Control>(ShopNodeName);
        var key = string.Join(",", icons.Select(icon => icon.Relic.GetType().Name + (icon.Crossed ? "x" : ""))) + "|" + net;
        if (icons.Count == 0)
        {
            box?.QueueFree();
            return;
        }
        var size = Math.Clamp(price.Size.Y, 20f, 36f);
        if (box == null || (string)box.GetMeta("key", "") != key)
        {
            box?.QueueFree();
            box = new Control { Name = ShopNodeName, MouseFilter = Control.MouseFilterEnum.Ignore };
            box.SetMeta("key", key);
            var row = new HBoxContainer { Name = "Icons", MouseFilter = Control.MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", (int)Math.Round(size * ShopIconGap));
            foreach (var (relic, crossed) in icons) row.AddChild(Backed(Crossable(IconOf(relic), size, crossed), size));
            box.AddChild(row);
            if (net is { } n)
            {
                var label = UiKit.Label($"{n}", (int)Math.Round(size * NetScale), NetColor);
                label.Name = "Net";
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
        if (box.GetNodeOrNull<Control>("Icons") is { } row2) row2.Position = new Vector2(left - width - size * BackdropPad - 4, (price.Size.Y - size) / 2);
        // Under the price's number, centered on it.
        if (box.GetNodeOrNull<Control>("Net") is { } netLabel)
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
            if (control is Label label && label.GetThemeFont("font") is { } font)
            {
                var otherX = font.GetStringSize(label.Text, HorizontalAlignment.Left, -1, label.GetThemeFontSize("font_size")).X;
                var x = label.HorizontalAlignment switch
                {
                    HorizontalAlignment.Center => (label.Size.X - otherX) / 2,
                    HorizontalAlignment.Right => label.Size.X - otherX,
                    _ => 0f,
                };
                return (x, otherX);
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
            if (price.GetNodeOrNull(ShopNodeName) is { } ours && ours.IsAncestorOf(pic) || !pic.IsVisibleInTree()) continue;
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
    private void TickShopWatch()
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
        ModEntry.Log.Info($"[audit] [reminders] {what}{(paid == entryGoldBefore.Net ? ", matched" : "")}");
        Telemetry.Audits.Done(Telemetry.Audits.Reminders, paid == entryGoldBefore.Net, paid == entryGoldBefore.Net ? "shop" : "reminder-shop");
        if (paid != entryGoldBefore.Net)
        {
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("reminders", "Shop net price after Lucky Fysh was wrong",
                new[] { "reminder-shop", nameof(LuckyFysh) }, what);
        }
    }

    private static readonly PropertyInfo? OnChosenProp = AccessTools.Property(typeof(EventOption), "OnChosen");
    private static readonly ConditionalWeakTable<EventOption, StrongBox<ChoiceEffect>> Effects = new();

    /// <summary>What choosing the option does (cached per option), from the code it runs.</summary>
    public static ChoiceEffect EffectsOf(EventOption option)
    {
        if (Effects.TryGetValue(option, out var box)) return box.Value;
        var calls = new HashSet<string>();
        var cards = new OptionCards();
        try
        {
            if (OnChosenProp?.GetValue(option) is Delegate d)
            {
                cards.Target = d.Target;
                Walk(d.Method, d.Method.DeclaringType, calls, new HashSet<MethodBase>(), 0, cards);
            }
            // An ancient's relic: what it does when you take it.
            if (option.Relic is { } relic && relic.GetType().GetMethod(nameof(RelicModel.AfterObtained)) is { } obtained && obtained.DeclaringType == relic.GetType())
            {
                cards.Relic = relic;
                Walk(obtained, relic.GetType(), calls, new HashSet<MethodBase>(), 0, cards);
            }
        }
        catch (Exception ex)
        {
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "reminders:" + option.TextKey);
        }
        var effects = ChoiceReminders.EffectsOf(calls);
        Effects.AddOrUpdate(option, new StrongBox<ChoiceEffect>(effects));
        CardsRead.AddOrUpdate(option, cards);
        return effects;
    }

    /// <summary>
    /// The calls a method makes, following calls (not delegates: those are the next page's options)
    /// into its own class and the classes nested in it (lambdas, async state machines), and into a
    /// game mode's static methods (Blitz's Neow rewards).
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
            if (MakesCurse(otherMethod)) calls.Add(ChoiceReminders.CurseMade);
            if (owner != null && IsWithin(otherMethod.DeclaringType, owner)) Walk(otherMethod, owner, calls, seen, depth + 1, cards);
            // A delegate the event keeps in its state, called by the option (the Endless Conveyor's dish:
            // GrabSomethingOffTheBelt runs CurrentDish.action): read the ones the event holds now.
            else if (otherMethod.Name == nameof(Action.Invoke) && typeof(Delegate).IsAssignableFrom(otherMethod.DeclaringType) && owner != null && cards.Target != null)
            {
                foreach (var held in HeldDelegates(cards.Target))
                    if (held.Method.DeclaringType != null && IsWithin(held.Method.DeclaringType, owner)) Walk(held.Method, owner, calls, seen, depth + 1, cards);
            }
            // An event's own part it creates (the Crystal Sphere's minigame, and the items it hides: card rewards, a curse, gold,
            // potions): everything that class does is something the option can do.
            else if (ins.opcode == OpCodes.Newobj && otherMethod.DeclaringType.Namespace?.StartsWith("MegaCrit.Sts2.Core.Events.Custom") == true)
            {
                foreach (var nextMethod in otherMethod.DeclaringType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    Walk(nextMethod, otherMethod.DeclaringType, calls, seen, depth + 1, cards);
            }
            // A game mode's own rewards (Blitz's Neow option runs BlitzRunModel.GiveNeowRewards).
            else if (otherMethod.IsStatic && typeof(RunModel).IsAssignableFrom(otherMethod.DeclaringType)) Walk(otherMethod, otherMethod.DeclaringType, calls, seen, depth + 1, cards);
        }
    }

    /// <summary>ModelDb.Card&lt;T&gt; of a curse card, or ModelDb.CardPool&lt;CurseCardPool&gt;.</summary>
    private static bool MakesCurse(MethodBase method)
    {
        if (method is not MethodInfo { IsGenericMethod: true } otherMethod || otherMethod.DeclaringType?.Name != nameof(ModelDb)) return false;
        var arg = otherMethod.GetGenericArguments()[0];
        if (otherMethod.Name == nameof(ModelDb.CardPool)) return arg.Name == "CurseCardPool";
        if (otherMethod.Name != nameof(ModelDb.Card)) return false;
        try { return ModelDb.AllCards.FirstOrDefault(card => card.GetType() == arg)?.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Curse; }
        catch { return false; }
    }

    /// <summary>
    /// Delegates in an event's (or closure's) instance fields, and in the fields of struct values it holds (the Endless
    /// Conveyor's current Dish record struct).
    /// </summary>
    private static IEnumerable<Delegate> HeldDelegates(object target)
    {
        const BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var found = new List<Delegate>();
        for (var type = target.GetType(); type != null && type != typeof(object); type = type.BaseType)
        {
            foreach (var field in type.GetFields(bindingFlags))
            {
                object? v;
                try { v = field.GetValue(target); } catch { continue; }
                if (v is Delegate d) found.AddRange(d.GetInvocationList());
                else if (v != null && field.FieldType.IsValueType && !field.FieldType.IsPrimitive && !field.FieldType.IsEnum)
                    foreach (var inner in field.FieldType.GetFields(bindingFlags))
                    {
                        try { if (inner.GetValue(v) is Delegate value) found.AddRange(value.GetInvocationList()); } catch { }
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
    private static Player? RunMe()
    {
        if (Sts2State.Me is { } me) return me;
        var state = Sts2State.Private<RunState?>(RunManager.Instance, "State", null);
        return state == null ? null : LocalContext.GetMe(state);
    }
}
