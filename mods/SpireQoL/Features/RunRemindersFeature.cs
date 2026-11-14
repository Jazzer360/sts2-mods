using System.Reflection;
using OpCodes = System.Reflection.Emit.OpCodes;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
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
public sealed class RunRemindersFeature : IFeature, IFrameFeature
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

    /// <summary>The option being resolved: relics with a reminder rule that flash while it runs.</summary>
    private sealed class Window
    {
        public required EventOption Option;
        public required ChoiceEffect Effects;
        public required Player Me;
        public readonly List<RelicModel> Flashed = new();
        public readonly List<RelicModel> Subscribed = new();
        public Task? Task;
        public int Frames;
    }

    private static Window? _window;

    private static void BeforeChosen(EventOption __instance)
    {
        try
        {
            Close();
            if (__instance.IsLocked || __instance.WasChosen || RunMe() is not { } me) return;
            var w = new Window { Option = __instance, Effects = EffectsOf(__instance), Me = me };
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
    /// Ends the window (the next option, combat starting, or a few seconds after the option finished)
    /// and checks it: a relic that reacted without its icon on the option is reported (a missed
    /// effect: an option's call the reader doesn't map, or a rule missing a choice). One that had an
    /// icon and didn't react is only logged: an option can skip its effect (an empty card choice, a
    /// conditional reward).
    /// </summary>
    private static void Close()
    {
        var w = _window;
        if (w == null) return;
        _window = null;
        foreach (var r in w.Subscribed) r.Flashed -= OnFlashed;
        var shown = w.Subscribed.Where(r => (ChoiceReminders.For(r.GetType().Name)!.On & w.Effects) != 0).ToList();
        var missed = w.Flashed.Where(r => !shown.Contains(r)).Select(r => r.GetType().Name).ToList();
        var quiet = shown.Where(r => !w.Flashed.Contains(r)).Select(r => r.GetType().Name).ToList();
        var what = $"{w.Option.TextKey}: effects {w.Effects}; icons [{string.Join(", ", shown.Select(r => r.GetType().Name))}]; flashed [{string.Join(", ", w.Flashed.Select(r => r.GetType().Name))}]";
        ModEntry.Log.Info($"[audit] [reminders] {what}");
        if (quiet.Count > 0) ModEntry.Log.Info($"[audit] [reminders] icon shown, no reaction (logged only): {string.Join(", ", quiet)}");
        if (missed.Count > 0)
        {
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("reminders", $"Relic reacted to an event option without a reminder: {string.Join(", ", missed)}",
                new[] { "reminder-missed" }.Concat(missed).Append(w.Option.TextKey.Split('.')[0]),
                what);
        }
    }

    private static void TickWindow()
    {
        if (_window is not { } w) return;
        // A fight from the event: what happens in it isn't the option's.
        if (Sts2State.InCombat) { Close(); return; }
        // Half a second after the option's task ends (its relics react within it), or a long timeout.
        if (w.Task is { IsCompleted: true } ? ++w.Frames > 30 : ++w.Frames > 3600) Close();
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
    }

    public void OnFrame(OverlayHost host)
    {
        TickWindow();
        RewardPanel(host);
        // Event options change only when the page changes: every few frames is plenty.
        if (++_frame % 6 == 0) EventOptions();
    }

    // ------------------------------------------------------------------ card reward panel

    private void RewardPanel(OverlayHost host)
    {
        var w = _widget!;
        var screen = NOverlayStack.Instance?.Peek() as NCardRewardSelectionScreen;
        var me = screen != null && GodotObject.IsInstanceValid(screen) && screen.IsVisibleInTree() ? RunMe() : null;
        foreach (var r in _rows.Values) r.Seen = false;
        var index = 0;
        if (me != null)
        {
            // A pick adds two cards with Bing Bong (its copy fires the deck hooks too).
            var perPick = me.Relics.Any(r => r is BingBong) ? 2 : 1;
            foreach (var relic in me.Relics)
            {
                var name = relic.GetType().Name;
                if (ChoiceReminders.For(name) is not { OnCardReward: true } || !_ctx.Settings.TabEnabled("reminder:" + name)) continue;
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

    /// <summary>Gold after Bowler Hat, Ectoplasm and the like (their ModifyGoldGained is plain math).</summary>
    private static int GoldAfterRelics(Player me, int amount)
    {
        decimal a = amount;
        foreach (var r in me.Relics) a = r.ModifyGoldGained(me, a);
        return (int)Math.Floor(a);
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

    // ------------------------------------------------------------------ event options

    private void EventOptions()
    {
        var layout = NEventRoom.Instance?.Layout;
        if (layout == null || !GodotObject.IsInstanceValid(layout)) return;
        var me = RunMe();
        var on = _ctx.Settings.TabEnabled("reminder:*");
        foreach (var button in layout.OptionButtons)
        {
            if (!GodotObject.IsInstanceValid(button)) continue;
            var icons = new List<(RelicModel Relic, bool Crossed)>();
            if (on && me != null && button.Option is { IsLocked: false, IsProceed: false } option)
            {
                var effects = EffectsOf(option);
                if (effects != ChoiceEffect.None)
                {
                    foreach (var relic in me.Relics)
                    {
                        var name = relic.GetType().Name;
                        if (ChoiceReminders.For(name) is not { } rule || (rule.On & effects) == 0 || !_ctx.Settings.TabEnabled("reminder:" + name)) continue;
                        icons.Add((relic, (rule.CrossedOn & effects) != 0));
                    }
                }
            }
            Show(button, icons);
        }
    }

    /// <summary>A row of relic icons left of the option button, vertically centered; removed when empty.</summary>
    private static void Show(NEventOptionButton button, List<(RelicModel Relic, bool Crossed)> icons)
    {
        var box = button.GetNodeOrNull<HBoxContainer>(NodeName);
        var key = string.Join(",", icons.Select(i => i.Relic.GetType().Name + (i.Crossed ? "x" : "")));
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
            foreach (var (relic, crossed) in icons)
            {
                var icon = UiKit.Icon(IconOf(relic), EventIcon);
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

    private static readonly PropertyInfo? OnChosenProp = AccessTools.Property(typeof(EventOption), "OnChosen");
    private static readonly ConditionalWeakTable<EventOption, StrongBox<ChoiceEffect>> Effects = new();

    /// <summary>What choosing the option does (cached per option), from the code it runs.</summary>
    public static ChoiceEffect EffectsOf(EventOption option)
    {
        if (Effects.TryGetValue(option, out var box)) return box.Value;
        var calls = new HashSet<string>();
        try
        {
            if (OnChosenProp?.GetValue(option) is Delegate d) Walk(d.Method, d.Method.DeclaringType, calls, new HashSet<MethodBase>(), 0);
            // An ancient's relic: what it does when you take it.
            if (option.Relic is { } relic && relic.GetType().GetMethod(nameof(RelicModel.AfterObtained)) is { } obtained && obtained.DeclaringType == relic.GetType())
            {
                Walk(obtained, relic.GetType(), calls, new HashSet<MethodBase>(), 0);
            }
        }
        catch (Exception ex)
        {
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "reminders:" + option.TextKey);
        }
        var effects = ChoiceReminders.EffectsOf(calls);
        Effects.AddOrUpdate(option, new StrongBox<ChoiceEffect>(effects));
        return effects;
    }

    /// <summary>
    /// The calls a method makes, following calls (not delegates: those are the next page's options)
    /// into its own class and the classes nested in it (lambdas, async state machines).
    /// </summary>
    private static void Walk(MethodBase method, Type? owner, HashSet<string> calls, HashSet<MethodBase> seen, int depth)
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
        foreach (var ins in code)
        {
            if (ins.operand is not MethodBase mb || mb.DeclaringType == null) continue;
            if (ins.opcode != OpCodes.Call && ins.opcode != OpCodes.Callvirt && ins.opcode != OpCodes.Newobj) continue;
            calls.Add(mb.DeclaringType.Name + "." + mb.Name);
            if (owner != null && IsWithin(mb.DeclaringType, owner)) Walk(mb, owner, calls, seen, depth + 1);
        }
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
