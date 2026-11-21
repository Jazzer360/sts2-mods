using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using SpireQoL.Cards;
using SpireQoL.Data;
using SpireQoL.Features;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Menu;

/// <summary>
/// The Inspect screen (docs/CARD_ODDS.md, decisions #60, #64): every card an event option can give, grouped as the option
/// draws them (a reward per group, a transform's pool), so you can see the possibilities without the compendium. Click the
/// cards you'd be happy with: each group shows the chance it offers at least one of them, and the header the chance across the
/// option. Your relics that apply to a card carry their tab on it, and a button marks all their cards. Marks are kept for the
/// run (in memory: a player's own notes). It shows only what the game's rules allow, never a roll. Own CanvasLayer, game
/// hotkeys blocked while open, Esc closes (as the settings menu).
/// </summary>
internal sealed class InspectScreen
{
    private const float CardScale = 0.42f;
    private const float CellWidth = 140;
    private static readonly Color MarkColor = SettingsMenu.Accent;

    /// <summary>Marked cards (by id) per run: a player's notes, kept while the game runs.</summary>
    private static readonly ConditionalWeakTable<object, HashSet<string>> MarksByRun = new();

    private readonly OverlayHost _host;
    private CanvasLayer? _layer;
    private Control? _root;
    private PanelContainer? _panel;
    private VBoxContainer? _body;
    private Label? _title;
    private Label? _overall;
    private HBoxContainer? _relicButtons;
    private readonly List<ShownCard> _cards = new();
    private readonly List<(PanelContainer Frame, CardModel Card, ShownCard Shown, Panel Ring)> _cells = new();
    private readonly List<(Label Label, DrawGroup Group, int Times)> _groupLabels = new();
    private List<(DrawGroup Group, int Times)> _groups = new();
    private List<DrawGroup> _baseGroups = new();
    private Player? _me;
    /// <summary>The card you'd transform, picked on the screen (null: every pool your deck's cards transform from).</summary>
    private CardModel? _transformFrom;
    private HBoxContainer? _transformRow;
    private HashSet<string> _marks = new();
    private Action? _closeAction;
    private bool _blocking;

    public InspectScreen(OverlayHost host) => _host = host;

    public bool IsOpen => _layer != null && GodotObject.IsInstanceValid(_layer) && _layer.Visible;

    /// <summary>Opens on an option's groups (identical groups merged: Glass Eye's two common rewards become one, twice).</summary>
    public void Open(string optionTitle, List<DrawGroup> groups, Player me)
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        if (_layer == null || !GodotObject.IsInstanceValid(_layer)) Build(tree);
        _marks = MarksByRun.GetValue(me.RunState, _ => new HashSet<string>());
        _me = me;
        _baseGroups = groups;
        _transformFrom = null;
        _title!.Text = $"Inspect: {optionTitle}";
        Regroup();
        Fill(me);
        _layer!.Visible = true;
        BlockGameInput(true);
    }

    public void Close()
    {
        if (_layer != null && GodotObject.IsInstanceValid(_layer)) _layer.Visible = false;
        BlockGameInput(false);
        if (_body != null) UiKit.Clear(_body);
        _cards.Clear();
        _cells.Clear();
        _groupLabels.Clear();
    }

    public void OnFrame()
    {
        if (!IsOpen) return;
        Fit();
        foreach (var shownCard in _cards) shownCard.Tick();
        foreach (var cell in _cells) PlaceRing(cell.Shown, cell.Ring);
    }

    /// <summary>Space between a marked card and its ring.</summary>
    private const float RingGap = 6;

    /// <summary>
    /// A marked card's ring, around the card itself (and its tabs, when it has some), drawn over it: the box it sits in has room
    /// above for tabs, and the card reaches its bottom edge, so a border on the box left a gap above and was hidden below.
    /// </summary>
    private static void PlaceRing(ShownCard shown, Panel ring)
    {
        if (!ring.Visible || shown.CardRect is not { } rect) return;
        var top = shown.Decor.Tabs.Count > 0 ? 0 : rect.Position.Y - RingGap;
        var pos = new Vector2(rect.Position.X - RingGap, top);
        var size = new Vector2(rect.Size.X + 2 * RingGap, rect.End.Y + RingGap - top);
        if (ring.Position != pos) ring.Position = pos;
        if (ring.Size != size) ring.Size = size;
    }

    /// <summary>
    /// The groups shown: identical ones merged (Glass Eye's two common rewards become one, twice), and a transform's pools
    /// replaced by what the picked card becomes (a card can't become itself; event and ancient cards draw from the colorless pool).
    /// </summary>
    private void Regroup()
    {
        var groups = _baseGroups.ToList();
        if (_transformFrom != null && groups.Any(drawGroup => drawGroup.Kind == DrawKind.Transform))
        {
            var first = groups.FindIndex(drawGroup => drawGroup.Kind == DrawKind.Transform);
            groups.RemoveAll(drawGroup => drawGroup.Kind == DrawKind.Transform);
            var upgraded = _baseGroups.First(drawGroup => drawGroup.Kind == DrawKind.Transform).Upgraded;
            groups.Insert(Math.Min(first, groups.Count), new DrawGroup(DrawKind.Transform, RunRemindersFeature.OptionCards.TransformResults(_transformFrom),
                MegaCrit.Sts2.Core.Runs.CardRarityOddsType.Uniform, 1, upgraded));
        }
        _groups = groups
            .GroupBy(drawGroup => (drawGroup.Kind, drawGroup.Count, string.Join(",", drawGroup.Cards.Select(card => card.Id.ToString()))))
            .Select(x => (x.First(), x.Count())).ToList();
    }

    /// <summary>For a transform: pick the card you'd transform from your deck and see exactly what it can become.</summary>
    private void FillTransformPicker(Player me)
    {
        UiKit.Clear(_transformRow!);
        _transformRow!.Visible = _baseGroups.Any(drawGroup => drawGroup.Kind == DrawKind.Transform);
        if (!_transformRow.Visible) return;
        _transformRow.AddChild(Text("Card you transform:", 20, SettingsMenu.Text));
        var pick = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        pick.AddThemeFontSizeOverride("font_size", 18);
        pick.AddItem("Any of your cards");
        // One entry per card as you'd see it (same card, upgrade and enchantment), in your deck's order.
        var cards = me.Deck.Cards.GroupBy(card => (card.Id, card.CurrentUpgradeLevel, card.Enchantment?.GetType())).Select(g => g.First()).ToList();
        foreach (var card in cards) pick.AddItem(CardName(card));
        var selected = _transformFrom == null ? 0 : cards.IndexOf(_transformFrom) + 1;
        pick.Selected = Math.Max(0, selected);
        pick.ItemSelected += i =>
        {
            _transformFrom = i <= 0 || i > cards.Count ? null : cards[(int)i - 1];
            Regroup();
            Fill(me);
        };
        _transformRow.AddChild(pick);
    }

    private static string CardName(CardModel card)
    {
        string title;
        try { title = card.Title; } catch { title = card.GetType().Name; }
        return title + (card.CurrentUpgradeLevel > 0 ? "+" : "") + (card.Enchantment != null ? $" ({card.Enchantment.GetType().Name})" : "");
    }

    // ------------------------------------------------------------------ building

    private void Build(SceneTree tree)
    {
        _layer = new CanvasLayer { Name = "SpireQoL_Inspect", Layer = 100, Visible = false };
        tree.Root.AddChild(_layer);
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_root);
        var backdrop = new ColorRect { Color = new Color(0, 0, 0, 0.62f), MouseFilter = Control.MouseFilterEnum.Stop };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(backdrop);

        _panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        var style = UiKit.PanelStyle(new Color(0.06f, 0.06f, 0.085f, 0.97f), 12, 22);
        style.ContentMarginTop = 16;
        style.ContentMarginBottom = 14;
        style.SetBorderWidthAll(2);
        style.BorderColor = SettingsMenu.Accent;
        _panel.AddThemeStyleboxOverride("panel", style);
        _root.AddChild(_panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        _panel.AddChild(box);

        var header = UiKit.Row(14);
        _title = Text("", 28, SettingsMenu.Accent);
        header.AddChild(_title);
        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        var clear = SettingsMenu.MakeButton("Clear marks", 20);
        clear.Pressed += () => { _marks.Clear(); Refresh(); };
        header.AddChild(clear);
        var close = SettingsMenu.MakeButton("Close", 20);
        close.Pressed += Close;
        header.AddChild(close);
        box.AddChild(header);

        _overall = Text("", 20, SettingsMenu.Text);
        _overall.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_overall);
        _relicButtons = UiKit.Row(8);
        _relicButtons.MouseFilter = Control.MouseFilterEnum.Pass;
        box.AddChild(_relicButtons);
        _transformRow = UiKit.Row(10);
        _transformRow.MouseFilter = Control.MouseFilterEnum.Pass;
        box.AddChild(_transformRow);

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 14);
        scroll.AddChild(_body);
        box.AddChild(scroll);
        box.AddChild(Text("Click the cards you'd be happy with. Marks are kept for this run. Every card the rules allow, never the roll. Esc: close.", 16, SettingsMenu.Muted));
        Fit();
    }

    private void Fit()
    {
        if (_panel == null) return;
        var position = _host.ViewportSize;
        var size = new Vector2(Math.Min(1500, position.X - 80), position.Y - 80);
        _panel.CustomMinimumSize = size;
        _panel.Size = size;
        _panel.Position = (position - size) / 2;
    }

    private void Fill(Player me)
    {
        UiKit.Clear(_body!);
        UiKit.Clear(_relicButtons!);
        _cards.Clear();
        _cells.Clear();
        _groupLabels.Clear();
        var held = me.Relics.Select(relic => (Relic: relic, Rule: CardAffinity.For(relic.GetType().Name))).Where(relicRule => relicRule.Rule != null).ToList();
        var heldRules = held.Select(relicRule => relicRule.Rule!).ToList();
        var upgradedCards = _groups.Any(g => g.Group.Upgraded);
        var all = _groups.SelectMany(g => g.Group.Cards).Distinct().ToList();
        FillTransformPicker(me);

        // A button per relic of yours that applies to some card here: marks its cards.
        foreach (var (relic, rule) in held)
        {
            var mine = all.Where(card => CardAffinity.AppliesWith(rule!, card, heldRules, upgradedCards)).ToList();
            if (mine.Count == 0) continue;
            var button = SettingsMenu.MakeButton($"Mark {Title(relic)}'s cards ({mine.Count})", 18);
            button.Icon = GameIcons.Relic(relic);
            button.ExpandIcon = true;
            button.AddThemeConstantOverride("icon_max_width", 28);
            button.Pressed += () => { foreach (var card in mine) _marks.Add(card.Id.ToString()); Refresh(); };
            _relicButtons!.AddChild(button);
        }

        var n = 0;
        foreach (var (group, times) in _groups)
        {
            n++;
            var head = Text("", 21, SettingsMenu.Accent);
            head.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _body!.AddChild(head);
            _groupLabels.Add((head, group, times));
            var flow = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            flow.AddThemeConstantOverride("h_separation", 6);
            flow.AddThemeConstantOverride("v_separation", 6);
            foreach (var card in group.Cards) flow.AddChild(Cell(card, held, group.Upgraded));
            _body.AddChild(flow);
        }
        Refresh();
    }

    /// <summary>A card you can click to mark, with a tab for each relic of yours that applies to it.</summary>
    private Control Cell(CardModel card, List<(RelicModel Relic, CardAffinityRule? Rule)> held, bool upgraded)
    {
        var heldRules = held.Select(relicRule => relicRule.Rule!).ToList();
        var tabs = held.Where(relicRule => CardAffinity.AppliesWith(relicRule.Rule!, card, heldRules, upgraded))
            .Select(relicRule => new TabSpec("inspect:" + relicRule.Relic.GetType().Name, GameIcons.Relic(relicRule.Relic))).ToList();
        var shown = new ShownCard(card, CardScale, CellWidth, tabs);
        _cards.Add(shown);
        var frame = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.PointingHand, TooltipText = card.Title };
        // Room around the card for its ring (RingGap and the border).
        frame.AddThemeStyleboxOverride("panel", new StyleBoxEmpty { ContentMarginLeft = 4, ContentMarginRight = 4, ContentMarginTop = 4, ContentMarginBottom = RingGap + 6 });
        frame.AddChild(shown.Root);
        var ringStyle = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = MarkColor };
        ringStyle.SetBorderWidthAll(3);
        ringStyle.SetCornerRadiusAll(10);
        var ring = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        ring.AddThemeStyleboxOverride("panel", ringStyle);
        shown.Root.AddChild(ring); // after the card: drawn over it
        frame.GuiInput += inputEvent =>
        {
            if (inputEvent is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            var id = card.Id.ToString();
            if (!_marks.Remove(id)) _marks.Add(id);
            Refresh();
        };
        _cells.Add((frame, card, shown, ring));
        return frame;
    }

    /// <summary>Marks and chances after a change.</summary>
    private void Refresh()
    {
        bool Good(CardModel card) => _marks.Contains(card.Id.ToString());
        foreach (var (frame, card, shown, ring) in _cells)
        {
            var marked = Good(card);
            ring.Visible = marked;
            PlaceRing(shown, ring);
            // The others darkened, never see-through: a translucent card shows its art through its frame.
            frame.Modulate = marked || _marks.Count == 0 ? Colors.White : new Color(0.45f, 0.45f, 0.45f);
        }
        var rewardChances = new List<double>();
        var n = 0;
        foreach (var (label, group, times) in _groupLabels)
        {
            n++;
            var chance = group.Chance(Good);
            label.Text = Describe(n, group, times) + (_marks.Count > 0 && chance is { } v ? $": {Percent(v)}" : "");
            if (group.Kind == DrawKind.Reward && chance is { } rp) for (var i = 0; i < times; i++) rewardChances.Add(rp);
        }
        var marks = _cells.Select(cell => cell.Card).Distinct().Count(Good);
        _overall!.Text = _marks.Count == 0 || marks == 0
            ? "Mark the cards you'd be happy with to see your chances."
            : rewardChances.Count > 1
                ? $"{marks} marked. Chance you're offered at least one of them across the rewards: {Percent(Sim.CardOdds.AnyOf(rewardChances))}."
                : $"{marks} marked.";
    }

    /// <summary>What a group is: "Reward 2: 3 of these 18 uncommons (twice)", "Each card you transform becomes one of these 72".</summary>
    private static string Describe(int n, DrawGroup drawGroup, int times)
    {
        var count = drawGroup.Cards.Count;
        var rarity = drawGroup.Cards.Select(card => card.Rarity).Distinct().ToList() is { Count: 1 } one ? " " + one[0].ToString().ToLowerInvariant() + (count == 1 ? "" : "s") : "";
        var twice = times > 1 ? $" ({times} rewards like it)" : "";
        return drawGroup.Kind switch
        {
            DrawKind.Reward when drawGroup.Count is { } drawGroupCount => $"Reward {n}: {(drawGroupCount == 1 ? "one" : drawGroupCount.ToString())} of these {count}{rarity}{twice}{Roll(drawGroup)}, chance one you marked is offered",
            DrawKind.Reward => $"Reward {n}: some of these {count}{rarity}",
            DrawKind.Transform => $"A card you transform becomes one of these {count}, chance it's one you marked",
            DrawKind.Certain => "Always given",
            _ => $"One of these {count}",
        };
    }

    /// <summary>A reward that rolls its rarity first, then a card of it: each rarity's chance (" (rarity first: rare 1.5%, uncommon 98.5%)").</summary>
    private static string Roll(DrawGroup drawGroup)
        => drawGroup.Cards.Select(card => card.Rarity).Distinct().Count() > 1 && Features.RunRemindersFeature.OptionCards.RarityRoll(drawGroup.Cards, drawGroup.Odds) is { } rarityRoll
            ? $" (rarity first: {rarityRoll})"
            : "";

    private static string Percent(double p) => p <= 0 ? "0%" : p >= 1 ? "100%" : p < 0.01 ? "<1%" : $"{Math.Round(p * 100)}%";

    private static string Title(RelicModel relic)
    {
        try { return relic.Title.GetFormattedText(); } catch { return relic.GetType().Name; }
    }

    private static Label Text(string text, int size, Color color)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    // ------------------------------------------------------------------ input

    private void BlockGameInput(bool block)
    {
        var nHotkeyManager = NHotkeyManager.Instance;
        if (nHotkeyManager == null || _root == null || block == _blocking) return;
        try
        {
            _closeAction ??= Close;
            var closeKeys = new[] { MegaInput.cancel.ToString(), MegaInput.pauseAndBack.ToString(), MegaInput.back.ToString() };
            if (block)
            {
                nHotkeyManager.AddBlockingScreen(_root);
                foreach (var closeKey in closeKeys) SettingsMenu.PushHotkey(nHotkeyManager, closeKey, _closeAction);
            }
            else
            {
                foreach (var closeKey in closeKeys) nHotkeyManager.RemoveHotkeyPressedBinding(closeKey, _closeAction);
                nHotkeyManager.RemoveBlockingScreen(_root);
            }
            _blocking = block;
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"inspect: couldn't {(block ? "block" : "unblock")} game hotkeys: {ex.Message}");
        }
    }
}
