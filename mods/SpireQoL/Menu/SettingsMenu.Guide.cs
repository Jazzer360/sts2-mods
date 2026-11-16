using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Cards;
using SpireQoL.Cards;
using SpireQoL.Features;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Menu;

/// <summary>
/// The Guide page (decision #56): how to read what the mod shows, with live examples built from the game's own pieces: real
/// cards with tabs drawn by the same renderer as the hand, Relic Counters rows and Incoming Damage sums built by their own
/// features' code. Nothing on it is about the current fight; every example is made up.
/// </summary>
public sealed partial class SettingsMenu
{
    /// <summary>Per-frame redraws for the Guide page's examples (cards settling, flashing rows); cleared on every rebuild.</summary>
    private readonly List<Action<float, float>> _guideTicks = new();

    private const float GuideCardScale = 0.55f;
    private const float GuideCellWidth = 300;

    private void BuildGuide()
    {
        Section("How to read Spire QoL");
        Paragraph("Everything the mod shows is something you could work out yourself from what's on screen and the game's rules. "
            + "It never looks ahead: not the draw pile's order, not a random roll, not an enemy's move beyond its intent. "
            + "When a roll decides something, you see the range or the worst case, never the result. "
            + "The other pages say exactly what each tab and row covers; this page shows how the pieces look.");

        BuildGuideTabs();
        BuildGuideCounters();
        BuildGuideIncoming();
        BuildGuideElsewhere();
    }

    // ------------------------------------------------------------------ card tabs

    private void BuildGuideTabs()
    {
        Section("Tabs on cards");
        Paragraph("A tab hanging off the top of a card in your hand answers one question: does something happen the moment I play this card, right now? "
            + "The icon is what sets it off (a relic, your power, an enemy's power, an enchantment, a pile). One tab per source; a card can carry several. "
            + "Each kind of tab can be switched off on the Card tabs, Relics and powers pages.");

        var flow = Flow();
        var strike = Card<StrikeIronclad>();
        var defend = Card<DefendIronclad>();

        GuideCardCell(flow, strike, "Plain: it sets something off",
            "Playing this sets off what the icon shows. Here it's your third Attack this turn, so Kunai gives you Dexterity.",
            Tab("proc:Kunai", Relic<Kunai>()));
        GuideCardCell(flow, strike, "A number: how much",
            "Block gained, damage dealt or the amount summoned, after everything that changes it. Only those tabs carry a number "
            + "(General: Amounts on proc tabs). Here Ornamental Fan gives 4 block.",
            Tab("proc:OrnamentalFan", Relic<OrnamentalFan>(), number: 4));
        GuideCardCell(flow, strike, "Crossed: a warning",
            "Playing this might be bad for you: it helps an enemy, hurts you, or gives up a relic's bonus. Often still the right play; "
            + "the slash makes sure you know. Here the enemy's Thorns hits you for 3.",
            Tab("proc:ThornsPower", Power<ThornsPower>(), crossed: true, number: 3));
        GuideCardCell(flow, Card<Bash>(), "The arrow: only at some targets",
            "The game's targeting arrow in the corner means it happens only at some of the targets you could pick. "
            + "Here Bash kills an enemy before it acts (the skull), but only aimed at the right one. On a crossed tab, aim elsewhere to avoid it.",
            Tab("lethal", GameIcons.Skull(), aimed: true));
        GuideCardCell(flow, strike, "Several at once",
            "Each source gets its own tab, in a row. If the row is wider than the card, the tabs overlap. "
            + "Here: Pen Nib doubles it, Ornamental Fan gives 4 block, and the enemy curls up if you aim at it.",
            Tab("proc:PenNib", Relic<PenNib>()), Tab("proc:OrnamentalFan", Relic<OrnamentalFan>(), number: 4),
            Tab("proc:CurlUpPower", Power<CurlUpPower>(), crossed: true, aimed: true));
        GuideCardCell(flow, strike, "Free cards: the energy saved",
            "A free card (Free Attack here; also Free Skill, Free Power, Free Card) is used up by this play. The number is the energy it saves. "
            + "Crossed on a card that costs 0 or X: the free card would be wasted.",
            Tab("free:FreeAttackPower", Power<FreeAttackPower>(), number: 1));
        GuideCardCell(flow, strike, "?: a cost you couldn't know",
            "A free card shows every cost as 0. When nothing on screen tells you this copy's real cost (Snecko Eye re-rolled it, "
            + "Frantic Escape raised copies like it), the tab says ? instead of telling you.",
            Tab("free:FreeAttackPower", Power<FreeAttackPower>(), numberText: "?"));
        GuideCardCell(flow, strike, "Text: played more than once",
            "The total plays, when the card replays itself (Replay). Something else that doubles a play shows its own icon instead "
            + "(Echo Form, Burst, Duplication...).",
            Tab("replay", null, text: "2×"));
        GuideCardCell(flow, defend, "A card: it comes back",
            "Playing this brings that card back to your hand: here a Skill that makes Make It So return. "
            + "Also Right Hand Hand and Whistling Arrows.",
            new TabSpec("return:MakeItSo", null, Card: Card<MakeItSo>()));
        GuideCardCell(flow, defend, "Retain and Sly",
            "Steady: the card stays in your hand at end of turn. Master Planner: it's Sly. "
            + "These are about the card, not about playing it, and they show even when it's only for this turn.",
            Tab("retain", Enchantment<Steady>()), Tab("sly", Power<MasterPlannerPower>()));
        GuideCardCell(flow, Card<PommelStrike>(), "Discard pile: a reshuffle",
            "It draws more cards than your draw pile has, so playing it shuffles your discard pile back into your draw pile.",
            Tab("shuffle", GameIcons.DiscardPile()));
        GuideCardCell(flow, Card<Apparition>(), "See-through: Ethereal",
            "The one marker that isn't a tab: an Ethereal card exhausts if it's still in your hand at the end of your turn. "
            + "How see-through: General, Ethereal opacity.",
            (float?)S.EtherealOpacity);
        _rows!.AddChild(flow);

        Paragraph("Potion tabs: the same tab hangs under a potion in the top bar when drinking it sets off a relic "
            + "(Reptile Trinket, Belt Buckle, White Beast Statue).");
    }

    // ------------------------------------------------------------------ relic counters

    private void BuildGuideCounters()
    {
        Section("Relic Counters");
        Paragraph("Beside your HP bar, one row per relic or power that counts what you do: its icon, a bar with one segment per step, and a label. "
            + "A row flashes when your next qualifying play sets it off. Counters that carry across turns come first.");

        CounterRow(RelicOf<PenNib>(), 7, 10, RelicCountersFeature.Shade.Relic, "7/10", false,
            "Gold: a relic's counter. Pen Nib has counted 7 of its 10 Attacks; it carries across turns and fights.");
        CounterRow(RelicOf<Kunai>(), 2, 3, RelicCountersFeature.Shade.Relic, "2/3", true,
            "Flashing: your next Attack sets Kunai off. (General: Tracker flashes early starts it one step sooner.)");
        CounterRow(RelicOf<VelvetChoker>(), 5, 6, RelicCountersFeature.Shade.Warning, "1 left", true,
            "A limit: Velvet Choker's plays left this turn, red at the last one, then LIMIT.");
        CounterRow(RelicOf<Pocketwatch>(), 2, 3, RelicCountersFeature.Shade.Good, "2/3 ok", false,
            "Stay under: green while you're within Pocketwatch's limit, red (missed) once you go over.");
        CounterRow(PowerOf<PanachePower>(), 3, 5, RelicCountersFeature.Shade.Power, "2 cards left", false,
            "Blue: a power of yours counting down to its next trigger (Panache: 2 more cards this turn).");
        CounterRow(PowerOf<WitheringPresencePower>(), 4, 6, RelicCountersFeature.Shade.Warning, "2 cards left", false,
            "Red: an enemy's power counting your plays (Withering Presence adds a Wither to your hand).");
        CounterRow(PowerOf<CacophonyPower>(), 12, 33, RelicCountersFeature.Shade.Power, "21 draws left", false,
            "A long count shows just the number.");
        Paragraph("Relics that only count turns (Horn Cleat, Captain's Wheel...) are off by default: switch each on in the Tracker column of the Relics page.");
    }

    private void CounterRow(AbstractModel? model, int filled, int total, RelicCountersFeature.Shade shade, string label, bool flash, string text)
    {
        if (model == null) return;
        var row = RowBox();
        var example = RelicCountersFeature.ExampleRow(model, filled, total, shade, label, flash, out var paint);
        var holder = new MarginContainer { CustomMinimumSize = new Vector2(300, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        holder.AddChild(example);
        example.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(holder);
        row.AddChild(Wrapped(text));
        AddRow(row);
        _guideTicks.Add(paint);
    }

    // ------------------------------------------------------------------ incoming damage

    private void BuildGuideIncoming()
    {
        Section("Incoming Damage");
        Paragraph("Left of your block during your turn, a sum: what the enemies will deal on their turn, minus the block you'll have, "
            + "equals what you'll lose. Worked out hit by hit, in the game's order.");

        var legend = new (string Text, string Detail)[]
        {
            ("Attack icon", "What lands on you: the enemies' intents (its tier matches the game's), plus Burn, Decay, Constrict and the like. "
                + "An enemy that dies first (Poison, your end-of-turn damage) doesn't attack, and retaliation can stop a multi-hit partway."),
            ("Block icon", "Your block when the turn ends, including what you gain then (Plating, Frost orbs, Orichalcum...)."),
            ("=", "What you'll lose: red, or 0 in green. HP loss block can't stop (Regret...) is included. "
                + "When random targets are involved, it's the worst case for you."),
        };
        foreach (var (name, detail) in legend)
        {
            var row = RowBox();
            row.AddChild(Label(name, 20, Text, NameWidth));
            row.AddChild(Wrapped(detail));
            AddRow(row);
        }

        var flow = Flow();
        IncomingCell(flow, "You take 9", "", p => p.Show(14, 5, null, 9));
        IncomingCell(flow, "All blocked", "", p => p.Show(8, 12, null, 0));
        IncomingCell(flow, "This kills you", "The skull: the enemy turn takes you to 0 with nothing to save you.",
            p => p.Show(46, 6, null, 40, dies: true));
        IncomingCell(flow, "Something saves you", "Fairy in a Bottle (or Lizard Tail) brings you back: your HP after the enemy turn.",
            p => p.Show(46, 6, null, 40, revive: (Potion<FairyInABottle>(), 23)));
        IncomingCell(flow, "Osty takes some", "Necrobinder: what Osty absorbs for you (red if it kills Osty).",
            p => p.Show(18, 5, (6, false), 7));
        IncomingCell(flow, "Another turn first", "Ending your turn now gives you another one before the enemies act (Pael's Eye, Extra Turn).",
            p => p.Show(12, 0, null, 12, extraTurn: Relic<PaelsEye>()));
        _rows!.AddChild(flow);
    }

    private static void IncomingCell(HFlowContainer flow, string title, string text, Action<DamageMathPanel> show)
    {
        var cell = Cell();
        cell.AddChild(Label(title, 20, Accent));
        var box = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        box.AddThemeStyleboxOverride("panel", UiKit.PanelStyle(new Color(0, 0, 0, 0.35f), 6, 14));
        var inner = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddChild(inner);
        show(new DamageMathPanel(inner));
        cell.AddChild(box);
        if (text.Length > 0) cell.AddChild(CellText(text));
        flow.AddChild(cell);
    }

    // ------------------------------------------------------------------ the rest

    private void BuildGuideElsewhere()
    {
        Section("Elsewhere");
        var skull = UiKit.Icon(GameIcons.Skull(), IconSize * 0.7f);
        var example = UiKit.Row(4);
        example.AddChild(skull);
        example.AddChild(UiKit.Label("50%", 18, Colors.White));
        example.AddChild(UiKit.Label("  12/40", 20, Colors.White));
        GuideRow(example, "Death Skull",
            "Beside an enemy's HP: it dies before acting if you end your turn now (Poison, your end-of-turn damage, orbs). "
            + "With a chance when a roll decides it (General: Death chance).");
        GuideRow(null, "Run Reminders",
            "Outside fights, your relics that change a choice: beside event options and shop prices (crossed when they work against you), "
            + "and a panel next to card rewards.");
        GuideRow(null, "Rest Preview", "At rest sites: the HP resting leaves you with and how much of the heal is wasted.");
        GuideRow(null, "Moving things",
            $"{S.UnlockLayoutKey} unlocks the layout: drag a widget to move it, scroll over it to resize, right-click to reset. {S.HideAllKey} hides everything.");
    }

    private void GuideRow(Control? example, string name, string text)
    {
        var row = RowBox();
        var holder = new MarginContainer { CustomMinimumSize = new Vector2(120, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        if (example != null)
        {
            example.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            holder.AddChild(example);
        }
        row.AddChild(holder);
        row.AddChild(Label(name, 20, Text, NameWidth));
        row.AddChild(Wrapped(text));
        AddRow(row);
    }

    // ------------------------------------------------------------------ example cards

    private void GuideCardCell(HFlowContainer flow, CardModel? card, string title, string text, params TabSpec[] tabs) =>
        GuideCardCell(flow, card, title, text, null, tabs);

    private void GuideCardCell(HFlowContainer flow, CardModel? card, string title, string text, float? opacity, params TabSpec[] tabs)
    {
        var cell = Cell();
        if (card != null)
        {
            var example = new GuideCard(card, GuideCardScale, tabs, opacity);
            cell.AddChild(example.Root);
            _guideTicks.Add((_, _) => example.Tick());
        }
        cell.AddChild(Label(title, 20, Accent));
        cell.AddChild(CellText(text));
        flow.AddChild(cell);
    }

    /// <summary>
    /// One of the game's cards (its own card scene, not a pooled hand card), with tabs drawn by the hand's renderer. It's
    /// laid out once it's ready: the card frame is placed at the bottom of the box, leaving room above it for the tabs.
    /// </summary>
    private sealed class GuideCard
    {
        private const float TabRoom = 70; // above the card, in card units: the tabs stick up about this far

        public Control Root { get; }
        private readonly NCard? _card;
        private readonly CardDecor _decor = new();
        private readonly float _scale;
        private CardOverlayRenderer.Example? _example;
        private bool _visuals;
        private bool _failed;

        public GuideCard(CardModel model, float scale, IEnumerable<TabSpec> tabs, float? opacity)
        {
            _scale = scale;
            var size = NCard.defaultSize * scale;
            Root = new Control
            {
                CustomMinimumSize = new Vector2(GuideCellWidth, size.Y + TabRoom * scale),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            foreach (var t in tabs) _decor.AddTab(t);
            _decor.Opacity = opacity;
            try
            {
                var scene = ResourceLoader.Load<PackedScene>("res://scenes/cards/card.tscn");
                _card = scene.Instantiate<NCard>();
                _card.Model = model;
                _card.Scale = new Vector2(scale, scale);
                Root.AddChild(_card);
            }
            catch (Exception ex)
            {
                ModEntry.Log.Warn($"guide: card {model.GetType().Name} unavailable: {ex.Message}");
                _card = null;
            }
        }

        public void Tick()
        {
            if (_failed || _card == null || !GodotObject.IsInstanceValid(_card) || !_card.IsNodeReady()) return;
            try
            {
                if (!_visuals)
                {
                    _visuals = true;
                    _card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
                    Quiet(_card);
                }
                Place();
                _example ??= new CardOverlayRenderer.Example(_card);
                _example.Draw(_decor);
            }
            catch (Exception ex)
            {
                _failed = true; // once: not every frame
                ModEntry.Log.Warn($"guide: card example failed: {ex.Message}");
                _card.Visible = false;
            }
        }

        /// <summary>Moves the card so its frame sits centred at the bottom of the box (wherever the scene puts its origin).</summary>
        private void Place()
        {
            if (_card!.GetNodeOrNull<Control>("%Frame") is not { } frame || frame.Size.X < 1) return;
            var rel = Root.GetGlobalTransform().AffineInverse() * frame.GetGlobalTransform();
            var shown = frame.Size * _scale;
            var want = new Vector2((Root.Size.X - shown.X) / 2f, Root.Size.Y - shown.Y);
            var delta = want - rel.Origin;
            if (delta.LengthSquared() > 0.25f) _card.Position += delta;
        }

        private static void Quiet(Node node)
        {
            if (node is Control c)
            {
                c.MouseFilter = Control.MouseFilterEnum.Ignore;
                c.FocusMode = Control.FocusModeEnum.None;
            }
            foreach (var child in node.GetChildren()) Quiet(child);
        }
    }

    // ------------------------------------------------------------------ helpers

    private static TabSpec Tab(string key, Texture2D? icon, bool crossed = false, int? number = null, string? numberText = null,
        bool aimed = false, string? text = null) =>
        new(key, icon, crossed, number, Text: text, NumberText: numberText, Aimed: aimed);

    private static CardModel? Card<T>() where T : CardModel
    {
        try { return ModelDb.Card<T>(); } catch { return null; }
    }

    private static Texture2D? Relic<T>() where T : RelicModel
    {
        try { return ModelDb.Relic<T>().Icon; } catch { return null; }
    }

    private static RelicModel? RelicOf<T>() where T : RelicModel
    {
        try { return ModelDb.Relic<T>(); } catch { return null; }
    }

    private static PowerModel? PowerOf<T>() where T : PowerModel
    {
        try { return ModelDb.Power<T>(); } catch { return null; }
    }

    private static Texture2D? Power<T>() where T : PowerModel => GameIcons.Power<T>();

    private static Texture2D? Enchantment<T>() where T : EnchantmentModel => GameIcons.Enchantment<T>();

    private static Texture2D? Potion<T>() where T : PotionModel
    {
        try { return ModelDb.Potion<T>().Image; } catch { return null; }
    }

    private void Paragraph(string text)
    {
        var l = Label(text, 18, Muted);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var m = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        m.AddThemeConstantOverride("margin_left", 8);
        m.AddThemeConstantOverride("margin_right", 8);
        m.AddThemeConstantOverride("margin_bottom", 8);
        m.AddChild(l);
        _rows!.AddChild(m);
    }

    private static Label Wrapped(string text)
    {
        var d = Label(text, 18, Muted);
        d.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        d.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return d;
    }

    private static HFlowContainer Flow()
    {
        var flow = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        flow.AddThemeConstantOverride("h_separation", 24);
        flow.AddThemeConstantOverride("v_separation", 26);
        return flow;
    }

    private static VBoxContainer Cell()
    {
        var cell = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(GuideCellWidth, 0) };
        cell.AddThemeConstantOverride("separation", 6);
        return cell;
    }

    private static Label CellText(string text)
    {
        var l = Label(text, 17, Muted);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(GuideCellWidth, 0);
        return l;
    }
}
