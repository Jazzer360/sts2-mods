using System.Reflection;
using OpCodes = System.Reflection.Emit.OpCodes;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Runs;
using SpireQoL.Data;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Out-of-combat reminders (docs/FEATURES.md "Run Reminders", decision #37), like StS1's Relic
/// Reminders:
///  - Card rewards: a panel next to the card reward screen with every relic that reacts to a card
///    joining your deck (Bing Bong, Book of Five Rings, Lucky Fysh) or to skipping (Pael's Wing),
///    with its counter.
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
    private static readonly Color Fill = new(0.95f, 0.75f, 0.35f);
    private static readonly Color Empty = new(1f, 1f, 1f, 0.15f);
    private static readonly Color Ready = new(1f, 0.35f, 0.9f);

    private readonly QolContext _ctx;
    private MovableWidget? _widget;
    private readonly Dictionary<string, Row> _rows = new();
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
            var w = new Window { Option = __instance, Effects = EffectsOf(__instance), Me = me, Start = Before.Of(me) };
            w.Shown = IconsFor(__instance, me).Select(i => i.Relic).ToHashSet();
            foreach (var r in me.Relics)
            {
                if (ChoiceReminders.For(r.GetType().Name) is not { On: not ChoiceEffect.None }) continue;
                r.Flashed += OnFlashed;
                w.Subscribed.Add(r);
            }
            _window = w;
        }
        catch (Exception ex)
        {
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "reminders:chosen");
        }
    }

    /// <summary>The option's task: the window stays open until it ends (card choices can take a while).</summary>
    private static void AfterChosen(EventOption __instance, Task __result)
    {
        if (_window is { } w && w.Option == __instance) w.Task = __result;
    }

    private static void OnFlashed(RelicModel relic, IEnumerable<MegaCrit.Sts2.Core.Entities.Creatures.Creature> _)
    {
        if (_window is { } w && !w.Flashed.Contains(relic)) w.Flashed.Add(relic);
    }

    /// <summary>
    /// What the option did, from the player's state before and after it: cards and curses that joined
    /// the deck (new card objects, so transforms count), gold gained, HP lost (not when max HP fell
    /// too: that's max HP loss), potions gained. Gold that Lucky Fysh paid for a card isn't the option's.
    /// </summary>
    private static ChoiceEffect Observed(Window w, out string details)
    {
        var me = w.Me;
        var s = w.Start;
        var added = me.Deck.Cards.Where(c => !s.Deck.Contains(c)).ToList();
        var e = ChoiceEffect.None;
        if (added.Any(c => c.Type != MegaCrit.Sts2.Core.Entities.Cards.CardType.Curse)) e |= ChoiceEffect.CardToDeck;
        if (added.Any(c => c.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Curse)) e |= ChoiceEffect.CurseToDeck;
        var luckyFysh = w.Flashed.Any(r => r is LuckyFysh);
        if (me.Gold > s.Gold && !luckyFysh) e |= ChoiceEffect.GoldGain;
        if (me.Creature.CurrentHp < s.Hp && me.Creature.MaxHp >= s.MaxHp) e |= ChoiceEffect.HpLoss;
        if (me.Potions.Count() > s.Potions) e |= ChoiceEffect.Potion;
        details = $"deck +[{string.Join(", ", added.Select(c => c.GetType().Name))}], gold {s.Gold}→{me.Gold}, HP {s.Hp}/{s.MaxHp}→{me.Creature.CurrentHp}/{me.Creature.MaxHp}, potions {s.Potions}→{me.Potions.Count()}";
        return e;
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
        var w = _window;
        if (w == null) return;
        _window = null;
        foreach (var r in w.Subscribed) r.Flashed -= OnFlashed;
        var observed = Observed(w, out var state);
        // The run ended (a win's last Proceed, or dying): the game sets your HP to 0 as it closes the run (RunState.IsGameOver is
        // every player dead), which isn't the option's doing. Logged, not checked.
        if (w.Me.Creature.IsDead)
        {
            ModEntry.Log.Info($"[audit] [reminders] {w.Option.TextKey}: the run ended, not checked ({state})");
            return;
        }
        var rule = (RelicModel r) => ChoiceReminders.For(r.GetType().Name)!;
        var shown = w.Subscribed.Where(w.Shown.Contains).ToList();
        var missed = w.Flashed.Where(r => !shown.Contains(r)).Select(r => r.GetType().Name).ToList();
        var quiet = shown.Where(r => !w.Flashed.Contains(r)).ToList();
        var silent = quiet.Where(r => (rule(r).Expect & observed) != 0).Select(r => r.GetType().Name).ToList();
        var unread = observed & ~w.Effects;
        // A card added by an option read as adding a curse (or the other way) is still a card the icons covered.
        if ((w.Effects & (ChoiceEffect.CardToDeck | ChoiceEffect.CurseToDeck)) != 0) unread &= ~ChoiceEffect.CardToDeck;
        var unseen = w.Effects & ~observed & ~ChoiceEffect.RestHeal;
        var what = $"{w.Option.TextKey}: read {w.Effects}, happened {observed}; icons [{string.Join(", ", shown.Select(r => r.GetType().Name))}]; flashed [{string.Join(", ", w.Flashed.Select(r => r.GetType().Name))}]; {state}";
        ModEntry.Log.Info($"[audit] [reminders] {what}");
        if (quiet.Count > silent.Count) ModEntry.Log.Info($"[audit] [reminders] icon shown, no reaction (logged only): {string.Join(", ", quiet.Select(r => r.GetType().Name).Except(silent))}");
        if (unseen != ChoiceEffect.None) ModEntry.Log.Info($"[audit] [reminders] read but didn't happen (logged only): {unseen}");
        var evt = w.Option.TextKey.Split('.')[0];
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
        var added = w.Me.Deck.Cards.Where(c => !w.Start.Deck.Contains(c)).ToList();
        CheckCardRules(w.Me, added, CardsRead.TryGetValue(w.Option, out var read) && read.GivesUpgraded, w.Shown, w.Option.TextKey);
        LogOdds(w, added);
    }

    private static void TickWindow()
    {
        if (_window is not { } w) return;
        // A fight from the event: what happens in it isn't the option's.
        if (Sts2State.InCombat) { Close(); return; }
        // Half a second after the option's task ends (its relics react within it), or a long timeout.
        if (w.Task is { IsCompleted: true }) { if (++w.DoneFrames > 30) Close(); }
        else if (++w.Frames > 100_000) Close();
    }

    private sealed class Row
    {
        public required HBoxContainer Root;
        public required TickBar Bar;
        public required Label Text;
        public bool Seen;
    }

    public void Attach(OverlayHost host)
    {
        // Left of the card row, vertically centered: clear of the cards, the banner and the skip button.
        _widget = _ctx.Widget(Id, "Run Reminders", new WidgetLayout
        {
            Anchor = ScreenAnchor.Left, OffsetX = 40, OffsetY = 0,
        });
        _inspect = new SpireQoL.Menu.InspectScreen(_ctx.Host);
    }

    public void OnFrame(OverlayHost host)
    {
        TickWindow();
        RewardPanel(host);
        // Event options and shop items change only when the page does: every few frames is plenty.
        if (++_frame % 6 == 0)
        {
            EventOptions();
            ShopSlots();
        }
        TickShopWatch();
        OfferedCardTabs();
        _inspect?.OnFrame();
    }

    // ------------------------------------------------------------------ card reward panel

    private void RewardPanel(OverlayHost host)
    {
        var w = _widget!;
        var screen = NOverlayStack.Instance?.Peek() as NCardRewardSelectionScreen;
        var me = screen != null && GodotObject.IsInstanceValid(screen) && screen.IsVisibleInTree() ? RunMe() : null;
        WatchReward(me, me != null);
        foreach (var r in _rows.Values) r.Seen = false;
        var index = 0;
        if (me != null)
        {
            // A pick adds two cards with Bing Bong (its copy fires the deck hooks too).
            var perPick = me.Relics.Any(r => r is BingBong) ? 2 : 1;
            foreach (var relic in me.Relics)
            {
                var name = relic.GetType().Name;
                if (ChoiceReminders.For(name) is not { OnCardReward: true } || !_ctx.Settings.TabEnabled(ChoiceReminders.Key(ReminderPlace.Reward, name))) continue;
                if (RewardRow(relic, me, perPick) is not { } state) continue;
                if (!_rows.TryGetValue(name, out var row))
                {
                    row = MakeRow(relic);
                    _rows[name] = row;
                    w.Content.AddChild(row.Root);
                }
                row.Seen = true;
                w.Content.MoveChild(row.Root, index++);
                row.Bar.Root.Visible = state.Total > 0;
                var fill = Fill;
                row.Root.Modulate = Colors.White;
                if (state.Flash)
                {
                    var p = UiKit.Pulse(host.Time, _ctx.Settings.FlashHz);
                    fill = fill.Lerp(Ready, p);
                    row.Root.Modulate = new Color(1, 1, 1, 0.65f + 0.35f * p);
                }
                if (state.Total > 0) row.Bar.Set(state.Filled, state.Total, fill, Empty);
                row.Text.Text = state.Label;
            }
        }
        foreach (var (name, row) in _rows.ToList())
        {
            if (row.Seen) continue;
            row.Root.QueueFree();
            _rows.Remove(name);
        }
        w.HasContent = index > 0;
    }

    private readonly record struct RowState(int Filled, int Total, bool Flash, string Label);

    private static RowState? RewardRow(RelicModel relic, Player me, int perPick)
    {
        switch (relic)
        {
            case BingBong:
                return new RowState(0, 0, false, "the card you take is copied");
            case BookOfFiveRings:
            {
                var every = Sts2State.Var(relic, "Cards", 5);
                var heal = Sts2State.Var(relic, "Heal", 20);
                if (every <= 0) return null;
                var n = Math.Clamp(relic.DisplayAmount, 0, every - 1);
                return new RowState(n, every, n + perPick >= every, $"{n}/{every}: heal {heal} at {every}");
            }
            case LuckyFysh:
            {
                var gold = GoldAfterRelics(me, Sts2State.Var(relic, "Gold", 15)) * perPick;
                return new RowState(0, 0, false, $"+{gold} gold when you take a card");
            }
            case PaelsWing:
            {
                var every = Sts2State.Var(relic, "Sacrifices", 2);
                if (every <= 0) return null;
                var n = Math.Clamp(relic.DisplayAmount, 0, every - 1);
                return new RowState(n, every, n + 1 >= every, $"{n}/{every} sacrificed: a relic at {every}");
            }
        }
        return null;
    }

    /// <summary>
    /// Gold after Bowler Hat, Ectoplasm and anything else that modifies it, through the game's own query
    /// (`Hook.ModifyGoldGained`: plain math, no side effects; the flashes come from AfterModifyingGoldGained,
    /// which isn't called). The game adds it truncated (PlayerCmd.GainGold).
    /// </summary>
    private static int GoldAfterRelics(Player me, int amount)
        => (int)MegaCrit.Sts2.Core.Hooks.Hook.ModifyGoldGained(me.RunState, null, amount, me, out _);

    /// <summary>
    /// A card reward panel row for the settings menu's Guide page (decision #56), drawn by the panel's own code; paint redraws
    /// its flash (time, flash Hz).
    /// </summary>
    internal static Control ExampleRewardRow(RelicModel relic, int filled, int total, bool flash, string label, out Action<float, float> paint)
    {
        var row = MakeRow(relic);
        row.Bar.Root.Visible = total > 0;
        row.Text.Text = label;
        paint = (time, hz) =>
        {
            var fill = Fill;
            row.Root.Modulate = Colors.White;
            if (flash)
            {
                var p = UiKit.Pulse(time, hz);
                fill = fill.Lerp(Ready, p);
                row.Root.Modulate = new Color(1, 1, 1, 0.65f + 0.35f * p);
            }
            if (total > 0) row.Bar.Set(filled, total, fill, Empty);
        };
        paint(0, 1);
        return row.Root;
    }

    /// <summary>A shop price for the Guide page: the relic icons left of it and the net price under it, as on a shop slot.</summary>
    internal static Control ExampleShopPrice(IEnumerable<(RelicModel Relic, bool Crossed)> icons, int price, int? net)
    {
        const float size = 30;
        var row = UiKit.Row(6);
        foreach (var (relic, crossed) in icons) row.AddChild(Crossable(IconOf(relic), size, crossed));
        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 0);
        col.AddChild(UiKit.Label($"{price}", 26, new Color(1f, 0.85f, 0.3f)));
        if (net is { } n) col.AddChild(UiKit.Label($"{n}", (int)Math.Round(size * 0.75f), NetColor));
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

    private static Row MakeRow(RelicModel relic)
    {
        var root = UiKit.Row(6);
        root.AddChild(UiKit.Icon(IconOf(relic), 28));
        var bar = new TickBar(9, 12);
        root.AddChild(bar.Root);
        var text = UiKit.Label("", 15, Colors.White);
        root.AddChild(text);
        return new Row { Root = root, Bar = bar, Text = text };
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

    /// <summary>What the panel showed while the card reward screen was open, to check after a pick.</summary>
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
    /// While the screen is open, keeps what the panel predicts; once it closes and the pick has settled
    /// (Bing Bong's copy comes after), checks it when a card was taken: the cards added (two with Bing
    /// Bong), Lucky Fysh's gold, and whether Book of Five Rings healed exactly when its row flashed.
    /// Skipping or sacrificing adds no card and isn't checked.
    /// </summary>
    private void WatchReward(Player? me, bool open)
    {
        if (open && me != null)
        {
            if (_reward == null || _reward.ClosedFrames >= 0)
            {
                CheckReward();
                var perPick = me.Relics.Any(r => r is BingBong) ? 2 : 1;
                var w = new RewardWatch { Me = me, Start = Before.Of(me), PerPick = perPick };
                if (me.Relics.OfType<LuckyFysh>().FirstOrDefault() is { } fysh) w.FyshGold = GoldAfterRelics(me, Sts2State.Var(fysh, "Gold", 15)) * perPick;
                if (me.Relics.OfType<BookOfFiveRings>().FirstOrDefault() is { } book)
                {
                    w.BookEvery = Sts2State.Var(book, "Cards", 5);
                    w.BookAdded = Sts2State.Private(book, "CardsAdded", -1);
                    if (w.BookEvery > 0 && w.BookAdded >= 0) w.BookHeals = w.BookAdded % w.BookEvery + perPick >= w.BookEvery;
                }
                _reward = w;
            }
            return;
        }
        if (_reward == null) return;
        if (_reward.ClosedFrames < 0) _reward.ClosedFrames = 0;
        if (++_reward.ClosedFrames > 90) CheckReward();
    }

    private void CheckReward()
    {
        var w = _reward;
        _reward = null;
        if (w == null || w.ClosedFrames < 0) return;
        var me = w.Me;
        var added = me.Deck.Cards.Count(c => !w.Start.Deck.Contains(c));
        if (added == 0) return; // skipped or sacrificed
        var problems = new List<(string Key, string Text)>();
        if (added != w.PerPick) problems.Add(("cards", $"{added} cards joined the deck, predicted {w.PerPick}"));
        var gold = me.Gold - w.Start.Gold;
        if (w.FyshGold is { } g && gold != g) problems.Add(("LuckyFysh", $"Lucky Fysh: gold +{gold}, predicted +{g}"));
        if (w.BookHeals is { } heals && me.Relics.OfType<BookOfFiveRings>().FirstOrDefault() is { } book)
        {
            var now = Sts2State.Private(book, "CardsAdded", -1);
            if (now >= 0)
            {
                var healed = now / w.BookEvery > w.BookAdded / w.BookEvery;
                if (healed != heals) problems.Add(("BookOfFiveRings", $"Book of Five Rings: {(healed ? "healed" : "didn't heal")} ({w.BookAdded}→{now} cards), predicted {(heals ? "a heal" : "no heal")}"));
            }
        }
        CheckCardRules(me, me.Deck.Cards.Where(c => !w.Start.Deck.Contains(c)).ToList(), false, null, "card reward");
        var what = $"card reward: {added} added (predicted {w.PerPick}), gold {w.Start.Gold}→{me.Gold}";
        ModEntry.Log.Info($"[audit] [reminders] {what}{(problems.Count == 0 ? ", matched" : "; " + string.Join("; ", problems.Select(p => p.Text)))}");
        if (problems.Count > 0)
        {
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("reminders", $"Card reward panel was wrong: {string.Join(", ", problems.Select(p => p.Key))}",
                new[] { "reminder-reward" }.Concat(problems.Select(p => p.Key)), what + "\n" + string.Join("\n", problems.Select(p => p.Text)));
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
                ? IconsFor(option, me).Where(i => _ctx.Settings.TabEnabled(ChoiceReminders.Key(ReminderPlace.Event, i.Relic.GetType().Name)))
                    .Select(i => (i.Relic, i.Crossed, _ctx.Settings.EventOdds ? i.Odds : null)).ToList()
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
        var key = string.Join(",", icons.Select(i => i.Relic.GetType().Name + (i.Crossed ? "x" : "") + i.Odds));
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
                        var perPick = me.Relics.Any(r => r is BingBong) ? 2 : 1;
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
        var key = string.Join(",", icons.Select(i => i.Relic.GetType().Name + (i.Crossed ? "x" : ""))) + "|" + net;
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
            row.AddThemeConstantOverride("separation", 2);
            foreach (var (relic, crossed) in icons) row.AddChild(Crossable(IconOf(relic), size, crossed));
            box.AddChild(row);
            if (net is { } n)
            {
                var label = UiKit.Label($"{n}", (int)Math.Round(size * 0.75f), NetColor);
                label.Name = "Net";
                label.MouseFilter = Control.MouseFilterEnum.Ignore;
                box.AddChild(label);
            }
            price.AddChild(box);
        }
        var width = icons.Count * (size + 2) - 2;
        if (box.GetNodeOrNull<Control>("Icons") is { } row2) row2.Position = new Vector2(-width - 6, (price.Size.Y - size) / 2);
        if (box.GetNodeOrNull<Control>("Net") is { } netLabel) netLabel.Position = new Vector2(0, price.Size.Y);
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
        if (_shopWatch is not { } w) return;
        if (w.Frames < 90)
        {
            _shopWatch = w with { Frames = w.Frames + 1 };
            return;
        }
        _shopWatch = null;
        if (RunMe() is not { } me) return;
        var paid = w.GoldBefore - me.Gold;
        var what = $"shop: bought {w.Entry.GetType().Name}, gold {w.GoldBefore}→{me.Gold}, predicted net {w.Net}";
        ModEntry.Log.Info($"[audit] [reminders] {what}{(paid == w.Net ? ", matched" : "")}");
        if (paid != w.Net)
        {
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("reminders", "Shop net price after Lucky Fysh was wrong",
                new[] { "reminder-shop", "LuckyFysh" }, what);
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
            code = PatchProcessor.GetOriginalInstructions(body);
        }
        catch
        {
            return; // unreadable
        }
        cards.Read(code);
        foreach (var ins in code)
        {
            if (ins.operand is not MethodBase mb || mb.DeclaringType == null) continue;
            if (ins.opcode != OpCodes.Call && ins.opcode != OpCodes.Callvirt && ins.opcode != OpCodes.Newobj) continue;
            calls.Add(mb.DeclaringType.Name + "." + mb.Name);
            // The card it makes is a curse (Wuppy's Injury: ModelDb.Card<Injury>; the Museum's pick from the curse pool): whatever
            // command adds it, a curse joins the deck.
            if (MakesCurse(mb)) calls.Add(ChoiceReminders.CurseMade);
            if (owner != null && IsWithin(mb.DeclaringType, owner)) Walk(mb, owner, calls, seen, depth + 1, cards);
            // An event's own part it creates (the Crystal Sphere's minigame, and the items it hides: card rewards, a curse, gold,
            // potions): everything that class does is something the option can do.
            else if (ins.opcode == OpCodes.Newobj && mb.DeclaringType.Namespace?.StartsWith("MegaCrit.Sts2.Core.Events.Custom") == true)
            {
                foreach (var m in mb.DeclaringType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    Walk(m, mb.DeclaringType, calls, seen, depth + 1, cards);
            }
            // A game mode's own rewards (Blitz's Neow option runs BlitzRunModel.GiveNeowRewards).
            else if (mb.IsStatic && typeof(RunModel).IsAssignableFrom(mb.DeclaringType)) Walk(mb, mb.DeclaringType, calls, seen, depth + 1, cards);
        }
    }

    /// <summary>ModelDb.Card&lt;T&gt; of a curse card, or ModelDb.CardPool&lt;CurseCardPool&gt;.</summary>
    private static bool MakesCurse(MethodBase mb)
    {
        if (mb is not MethodInfo { IsGenericMethod: true } gm || gm.DeclaringType?.Name != nameof(ModelDb)) return false;
        var arg = gm.GetGenericArguments()[0];
        if (gm.Name == nameof(ModelDb.CardPool)) return arg.Name == "CurseCardPool";
        if (gm.Name != nameof(ModelDb.Card)) return false;
        try { return ModelDb.AllCards.FirstOrDefault(c => c.GetType() == arg)?.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Curse; }
        catch { return false; }
    }

    private static bool IsWithin(Type t, Type owner)
    {
        var root = owner;
        while (root.DeclaringType != null) root = root.DeclaringType;
        for (var x = t; x != null; x = x.DeclaringType)
        {
            if (x == root) return true;
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
