using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Cards;

/// <summary>
/// Draws CardDecor onto the card nodes in the hand.
///
/// Placement rules (docs/TRACKER.md §8): badges and relic icons sit in a row just above the
/// card's top edge, so they never cover the cost, title, enchantment tab or affliction
/// overlay. The overlay is the last child of the card's %CardContainer, so it follows the
/// card's hover scale and hand rotation.
///
/// Card nodes are pooled and reused by the game, so anything we change on a node is undone
/// as soon as that node stops showing a decorated hand card.
/// </summary>
public sealed class CardOverlayRenderer
{
    private const string OverlayName = "SpireQoL_CardOverlay";
    private static readonly Vector2 DefaultCardSize = new(300f, 422f);

    private readonly OverlayHost _host;
    private readonly CardDecorFrame _frame;
    private readonly Dictionary<ulong, Entry> _entries = new();

    public CardOverlayRenderer(OverlayHost host, CardDecorFrame frame)
    {
        _host = host;
        _frame = frame;
    }

    private sealed class Entry
    {
        public required NCard Card;
        public required Control Container;
        public required Control Overlay;
        public required HBoxContainer Row;
        public required ColorRect Dim;
        public required Label DimLabel;
        public required ColorRect[] Border;
        public string Signature = "";
        public bool TouchedAlpha;
        public bool Seen;
        public readonly List<TextureRect> Echoes = new();
        public readonly List<Control> Tabs = new();
    }

    // ---- Keyword tabs ----
    // Copies of the card's own enchantment tab (NCard.EnchantmentTab: "%Enchantment" with an
    // "Icon" TextureRect and a "Label" MegaLabel), stacked down the left edge below the
    // enchantment slot, the way a second enchantment would look.
    private const float TabGap = 4f;

    private void ApplyTabs(Entry e, List<TabSpec> tabs)
    {
        var template = e.Card.EnchantmentTab;
        if (template == null || !GodotObject.IsInstanceValid(template))
        {
            HideTabs(e);
            return;
        }

        while (e.Tabs.Count < tabs.Count)
        {
            var copy = (Control)template.Duplicate();
            copy.Name = "SpireQoL_KeywordTab";
            ClearUniqueNames(copy);
            copy.MouseFilter = Control.MouseFilterEnum.Ignore;
            template.GetParent().AddChild(copy);
            e.Tabs.Add(copy);
        }

        // The game moves its tab up when the card has no star cost; follow wherever it is.
        var step = template.Size.Y * template.Scale.Y + TabGap;
        var slot = template.Visible ? 1 : 0;
        for (var i = 0; i < e.Tabs.Count; i++)
        {
            var tab = e.Tabs[i];
            if (!GodotObject.IsInstanceValid(tab)) continue;
            var on = i < tabs.Count;
            tab.Visible = on;
            if (!on) continue;

            var spec = tabs[i];
            tab.Position = template.Position + Vector2.Down * step * (slot + i);
            tab.Scale = template.Scale;
            tab.Rotation = template.Rotation;
            // Not the template's tint: the game greys its tab for a disabled enchantment.
            tab.Modulate = Colors.White;
            tab.SelfModulate = Colors.White;

            if (tab.GetNodeOrNull<TextureRect>("Icon") is { } icon)
            {
                icon.Texture = spec.Icon;
                icon.Modulate = Colors.White;
            }
            if (tab.GetNodeOrNull<Label>("Label") is { } label)
            {
                label.Visible = spec.Label != null;
                label.Text = spec.Label ?? "";
            }
        }
    }

    private static void HideTabs(Entry e)
    {
        foreach (var tab in e.Tabs)
        {
            if (GodotObject.IsInstanceValid(tab)) tab.Visible = false;
        }
    }

    /// <summary>A duplicated scene piece keeps "%unique" flags; clear them so the card's own lookups aren't confused.</summary>
    private static void ClearUniqueNames(Node node)
    {
        node.UniqueNameInOwner = false;
        foreach (var child in node.GetChildren()) ClearUniqueNames(child);
    }

    // ---- Double-play echoes ----
    // Copies of the card's own glow outline (NCard.CardHighlight: a TextureRect with the game's
    // highlight shader, "width" parameter) that expand outward and fade on a loop, like Echo
    // Form's afterimages. They sit right above the real highlight, beneath the card art.
    private static readonly Color EchoColor = new(0.82f, 0.74f, 1f);
    private const float EchoPeriod = 1.4f;   // seconds per ripple
    private const float EchoGrowth = 0.10f;  // +10% size at the end of a ripple
    private const float EchoAlpha = 0.85f;
    private const float EchoWidth = 0.09f;   // highlight shader outline width (game uses 0.075 when shown)

    private void ApplyEchoes(Entry e, int playCount)
    {
        var want = playCount <= 1 ? 0 : Math.Min(3, playCount);
        var highlight = e.Card.CardHighlight;
        if (highlight == null || !GodotObject.IsInstanceValid(highlight)) want = 0;

        while (e.Echoes.Count < want)
        {
            var echo = new TextureRect
            {
                Name = "SpireQoL_Echo",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                StretchMode = highlight!.StretchMode,
                ExpandMode = highlight.ExpandMode,
            };
            if (highlight.Material is ShaderMaterial mat)
            {
                var copy = (ShaderMaterial)mat.Duplicate();
                copy.SetShaderParameter("width", EchoWidth);
                echo.Material = copy;
            }
            highlight.GetParent().AddChild(echo);
            e.Echoes.Add(echo);
        }

        for (var i = 0; i < e.Echoes.Count; i++)
        {
            var echo = e.Echoes[i];
            if (!GodotObject.IsInstanceValid(echo)) continue;
            var on = i < want;
            echo.Visible = on;
            if (!on) continue;

            // Match the real highlight, then sit just above it.
            echo.Texture = highlight!.Texture;
            echo.Size = highlight.Size;
            echo.Position = highlight.Position;
            echo.Rotation = highlight.Rotation;
            echo.PivotOffset = highlight.Size / 2;
            var parent = highlight.GetParent();
            if (echo.GetParent() == parent && echo.GetIndex() != highlight.GetIndex() + 1 + i)
            {
                parent.MoveChild(echo, Math.Min(parent.GetChildCount() - 1, highlight.GetIndex() + 1 + i));
            }

            // Staggered ripples: each starts at the outline and grows/fades outward.
            var t = (_host.Time / EchoPeriod + (float)i / want) % 1f;
            var s = 1f + EchoGrowth * t;
            echo.Scale = highlight.Scale * s;
            var c = EchoColor;
            c.A = EchoAlpha * (1f - t) * (1f - t);
            echo.Modulate = c;
        }
    }

    private static void HideEchoes(Entry e)
    {
        foreach (var echo in e.Echoes)
        {
            if (GodotObject.IsInstanceValid(echo)) echo.Visible = false;
        }
    }

    public void Render()
    {
        foreach (var e in _entries.Values) e.Seen = false;

        if (!_host.HideAll && Sts2State.InCombat)
        {
            foreach (var (node, card) in Sts2State.HandNodes())
            {
                var decor = _frame.Get(card);
                if (decor == null || decor.IsEmpty) continue;
                var entry = GetOrCreate(node);
                if (entry == null) continue;
                entry.Seen = true;
                Apply(entry, decor);
            }
        }

        // Undo anything on nodes that are no longer decorated hand cards.
        var dead = new List<ulong>();
        foreach (var (id, e) in _entries)
        {
            if (!GodotObject.IsInstanceValid(e.Card) || !GodotObject.IsInstanceValid(e.Overlay))
            {
                dead.Add(id);
                continue;
            }
            if (e.Seen) continue;
            e.Overlay.Visible = false;
            HideEchoes(e);
            HideTabs(e);
            if (e.TouchedAlpha)
            {
                SetAlpha(e.Container, 1f);
                e.TouchedAlpha = false;
            }
        }
        foreach (var id in dead) _entries.Remove(id);

        _frame.Clear();
    }

    private Entry? GetOrCreate(NCard card)
    {
        var id = card.GetInstanceId();
        if (_entries.TryGetValue(id, out var existing) && GodotObject.IsInstanceValid(existing.Overlay))
        {
            return existing;
        }

        var container = card.GetNodeOrNull<Control>("%CardContainer") ?? card;

        var overlay = new Control { Name = OverlayName, MouseFilter = Control.MouseFilterEnum.Ignore };
        container.AddChild(overlay);

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = Control.MouseFilterEnum.Ignore };
        overlay.AddChild(dim);
        var dimLabel = UiKit.Label("", 30, new Color(1f, 0.85f, 0.85f));
        dimLabel.HorizontalAlignment = HorizontalAlignment.Center;
        dimLabel.VerticalAlignment = VerticalAlignment.Center;
        dim.AddChild(dimLabel);

        var border = new ColorRect[4];
        for (var i = 0; i < 4; i++)
        {
            border[i] = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore };
            overlay.AddChild(border[i]);
        }

        var row = UiKit.Row(4);
        row.Alignment = BoxContainer.AlignmentMode.Center;
        overlay.AddChild(row);

        var entry = new Entry
        {
            Card = card, Container = container, Overlay = overlay, Row = row,
            Dim = dim, DimLabel = dimLabel, Border = border,
        };
        _entries[id] = entry;
        return entry;
    }

    private void Apply(Entry e, CardDecor d)
    {
        // Follow the card frame's transform inside the container.
        var frame = e.Card.GetNodeOrNull<Control>("%Frame");
        Vector2 size;
        if (frame != null && frame != e.Container && frame.Size.X > 1)
        {
            var rel = e.Container.GetGlobalTransform().AffineInverse() * frame.GetGlobalTransform();
            e.Overlay.Position = rel.Origin;
            e.Overlay.Rotation = rel.Rotation;
            e.Overlay.Scale = rel.Scale;
            size = frame.Size;
        }
        else
        {
            e.Overlay.Position = -DefaultCardSize / 2;
            e.Overlay.Rotation = 0;
            e.Overlay.Scale = Vector2.One;
            size = DefaultCardSize;
        }
        e.Overlay.Size = size;
        e.Overlay.Visible = true;
        if (e.Overlay.GetIndex() != e.Container.GetChildCount() - 1)
        {
            e.Container.MoveChild(e.Overlay, -1);
        }

        // Badge + relic row, just above the top edge.
        var sig = d.RowSignature();
        if (sig != e.Signature)
        {
            e.Signature = sig;
            UiKit.Clear(e.Row);
            foreach (var b in d.Badges)
            {
                e.Row.AddChild(UiKit.Badge(b.Text, b.Bg, b.Fg, 18));
            }
            foreach (var r in d.Relics)
            {
                e.Row.AddChild(RelicIcon(r));
            }
        }
        e.Row.Visible = d.Badges.Count > 0 || d.Relics.Count > 0;
        e.Row.ResetSize();
        e.Row.Size = new Vector2(size.X, e.Row.Size.Y);
        e.Row.Position = new Vector2(0, -e.Row.Size.Y - 4);

        // Dim + reason.
        e.Dim.Visible = d.DimReason != null;
        e.Dim.Position = Vector2.Zero;
        e.Dim.Size = size;
        e.DimLabel.Text = d.DimReason ?? "";
        e.DimLabel.Position = Vector2.Zero;
        e.DimLabel.Size = size;

        // Pulsing border.
        var showBorder = d.Border != null;
        if (showBorder)
        {
            var c = d.Border!.Value;
            c.A = 0.35f + 0.65f * UiKit.Pulse(_host.Time, 2f);
            const float t = 7f;
            SetRect(e.Border[0], new Vector2(-t, -t), new Vector2(size.X + 2 * t, t), c);
            SetRect(e.Border[1], new Vector2(-t, size.Y), new Vector2(size.X + 2 * t, t), c);
            SetRect(e.Border[2], new Vector2(-t, 0), new Vector2(t, size.Y), c);
            SetRect(e.Border[3], new Vector2(size.X, 0), new Vector2(t, size.Y), c);
        }
        foreach (var b in e.Border) b.Visible = showBorder;

        // Double-play echoes and keyword tabs.
        ApplyEchoes(e, d.PlayCount);
        ApplyTabs(e, d.Tabs);

        // Translucency.
        if (d.Opacity is { } a)
        {
            SetAlpha(e.Container, a);
            e.TouchedAlpha = true;
        }
        else if (e.TouchedAlpha)
        {
            SetAlpha(e.Container, 1f);
            e.TouchedAlpha = false;
        }
    }

    private static Control RelicIcon(RelicMark r)
    {
        const float s = 40f;
        var holder = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        holder.AddThemeStyleboxOverride("panel", UiKit.PanelStyle(new Color(0.05f, 0.05f, 0.08f, 0.8f), 6, 2));
        var inner = new Control { CustomMinimumSize = new Vector2(s, s), MouseFilter = Control.MouseFilterEnum.Ignore };
        holder.AddChild(inner);
        var icon = UiKit.Icon(r.Icon, s);
        inner.AddChild(icon);
        if (r.Crossed)
        {
            icon.Modulate = new Color(1, 1, 1, 0.75f);
            var line = new ColorRect
            {
                Color = new Color(1f, 0.15f, 0.1f, 0.95f),
                Size = new Vector2(s * 1.3f, 5),
                PivotOffset = new Vector2(s * 0.65f, 2.5f),
                Position = new Vector2(s / 2 - s * 0.65f, s / 2 - 2.5f),
                Rotation = -Mathf.Pi / 4,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            inner.AddChild(line);
        }
        return holder;
    }

    private static void SetRect(ColorRect r, Vector2 pos, Vector2 size, Color c)
    {
        r.Position = pos;
        r.Size = size;
        r.Color = c;
    }

    private static void SetAlpha(Control c, float a)
    {
        var m = c.Modulate;
        m.A = a;
        c.Modulate = m;
    }
}
