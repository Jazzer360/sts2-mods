using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Cards;

/// <summary>
/// Draws CardDecor onto the card nodes in the hand, in one uniform visual language:
///
///  - **Tabs** along the card's top edge: copies of the card's own enchantment tab
///    (NCard.EnchantmentTab: "%Enchantment" with an "Icon" TextureRect and a "Label" MegaLabel),
///    centered and overlapping the edge slightly. In a fanned hand the card tops stay visible
///    while the sides are covered, so the tabs read at a glance without hovering. A red slash on
///    a tab means "playing this breaks or is blocked by this".
///  - **Dim**: the card can't be played right now.
///  - **Opacity**: Ethereal translucency.
///
/// Everything lives in an overlay that's the last child of the card's %CardContainer, so it
/// follows the card's hover scale and hand rotation. Card nodes are pooled and reused by the
/// game, so anything we change on a node is undone as soon as that node stops showing a
/// decorated hand card.
/// </summary>
public sealed class CardOverlayRenderer
{
    private const string OverlayName = "SpireQoL_CardOverlay";
    private static readonly Vector2 DefaultCardSize = new(300f, 422f);
    private static readonly Color SlashColor = new(1f, 0.15f, 0.1f, 0.95f);

    private const float TabGap = 6f;
    private const float TabOverlap = 0.4f; // fraction of the tag length tucked under the card (hidden by the card art)

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
        public required ColorRect Dim;
        public bool TouchedAlpha;
        public bool Seen;
        public readonly List<Control> Tabs = new();

        /// <summary>
        /// Holds the tags, placed directly after the card's glow (NCard.CardHighlight) in the glow's
        /// parent, so tags draw above the glow but beneath the card art. Mirrors the card frame's
        /// transform, so tag positions are in card-frame coordinates. Falls back to Overlay.
        /// </summary>
        public Control? TagLayer;
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
            if (e.TagLayer != null && GodotObject.IsInstanceValid(e.TagLayer)) e.TagLayer.Visible = false;
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

        var entry = new Entry { Card = card, Container = container, Overlay = overlay, Dim = dim };
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

        e.Dim.Visible = d.Dim;
        e.Dim.Position = Vector2.Zero;
        e.Dim.Size = size;

        var layer = PlaceTagLayer(e, size);
        ApplyTabs(e, d.Tabs, layer);

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

    /// <summary>
    /// Puts the tag layer right after the card's glow node and makes it match the card frame
    /// (via the overlay, which already does). Returns the layer, or the overlay as a fallback.
    /// </summary>
    private static Control PlaceTagLayer(Entry e, Vector2 size)
    {
        var glow = e.Card.CardHighlight;
        if (glow == null || !GodotObject.IsInstanceValid(glow) || glow.GetParent() is not { } parent)
        {
            return e.Overlay;
        }

        if (e.TagLayer == null || !GodotObject.IsInstanceValid(e.TagLayer))
        {
            e.TagLayer = new Control { Name = "SpireQoL_TagLayer", MouseFilter = Control.MouseFilterEnum.Ignore };
            parent.AddChild(e.TagLayer);
        }
        var layer = e.TagLayer;
        if (layer.GetParent() != parent) layer.Reparent(parent, keepGlobalTransform: false);

        // Directly above the glow, below everything the card draws after it (the card art).
        var want = glow.GetIndex() + 1;
        if (layer.GetIndex() < glow.GetIndex()) want = glow.GetIndex();
        if (layer.GetIndex() != want) parent.MoveChild(layer, want);

        // Same transform as the card frame (the overlay mirrors it inside the container).
        var frameXf = e.Overlay.GetGlobalTransform();
        var rel = parent is CanvasItem ci ? ci.GetGlobalTransform().AffineInverse() * frameXf : frameXf;
        layer.Position = rel.Origin;
        layer.Rotation = rel.Rotation;
        layer.Scale = rel.Scale;
        layer.Size = size;
        layer.Visible = true;
        return layer;
    }

    private void ApplyTabs(Entry e, List<TabSpec> tabs, Control layer)
    {
        var template = e.Card.EnchantmentTab;
        if (template == null || !GodotObject.IsInstanceValid(template))
        {
            HideTabs(e);
            return;
        }

        while (e.Tabs.Count < tabs.Count)
        {
            e.Tabs.Add(MakeTab(template, layer));
        }

        // The enchantment tab is drawn to stick out sideways from the card's left edge. Rotated a
        // quarter turn clockwise, its card-side end points down, so each tag hangs up off the
        // top edge like an enchantment tab would off the side. Visually a rotated tag is
        // tabSize.Y wide and tabSize.X tall.
        var tabSize = template.Size * template.Scale;
        var tagW = tabSize.Y;
        var tagH = tabSize.X;

        // Center the row on the top edge; shrink it if it would be wider than the card.
        var total = tabs.Count * tagW + Math.Max(0, tabs.Count - 1) * TabGap;
        var fit = total > layer.Size.X && total > 0 ? layer.Size.X / total : 1f;
        var scale = template.Scale * fit;
        var step = (tagW + TabGap) * fit;
        var x0 = (layer.Size.X - total * fit) / 2f;
        // Tucked under the card's top edge (the card art covers the tucked part); the icon end
        // sticks out above.
        var top = -tagH * fit * (1f - TabOverlap);

        for (var i = 0; i < e.Tabs.Count; i++)
        {
            var tab = e.Tabs[i];
            if (!GodotObject.IsInstanceValid(tab)) continue;
            if (tab.GetParent() != layer) tab.Reparent(layer, keepGlobalTransform: false);
            var on = i < tabs.Count;
            tab.Visible = on;
            if (!on) continue;

            var spec = tabs[i];
            // Rotating +90° about the node's top-left puts its visual box at x ∈ [-h, 0], so shift
            // right by the visual width to land the tag's top-left at (left, top).
            var left = x0 + i * step;
            tab.PivotOffset = Vector2.Zero;
            tab.Rotation = Mathf.Pi / 2f;
            tab.Scale = scale;
            tab.Position = new Vector2(left + tagW * fit, top);
            // Not the template's tint: the game greys its tab for a disabled enchantment.
            tab.Modulate = Colors.White;
            tab.SelfModulate = Colors.White;

            if (tab.GetNodeOrNull<TextureRect>("Icon") is { } icon)
            {
                // Keep the icon upright inside the rotated tag.
                icon.PivotOffset = icon.Size / 2f;
                icon.Rotation = -Mathf.Pi / 2f;
                icon.Texture = spec.Icon;
                icon.Modulate = spec.Crossed ? new Color(1, 1, 1, 0.8f) : Colors.White;
                if (icon.GetNodeOrNull<ColorRect>("SpireQoL_Slash") is { } slash)
                {
                    slash.Visible = spec.Crossed;
                    if (spec.Crossed) LayoutSlash(slash, icon.Size);
                }
            }
            // Tags are icon-only: the tab's number label stays hidden.
            if (tab.GetNodeOrNull<Label>("Label") is { } label) label.Visible = false;
        }
    }

    private static Control MakeTab(Control template, Control overlay)
    {
        var tab = (Control)template.Duplicate();
        tab.Name = "SpireQoL_Tab";
        ClearUniqueNames(tab);
        tab.MouseFilter = Control.MouseFilterEnum.Ignore;
        if (tab.GetNodeOrNull<TextureRect>("Icon") is { } icon)
        {
            icon.AddChild(new ColorRect
            {
                Name = "SpireQoL_Slash",
                Color = SlashColor,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = false,
            });
        }
        overlay.AddChild(tab);
        return tab;
    }

    /// <summary>A diagonal red bar across the icon (bottom-left to top-right).</summary>
    private static void LayoutSlash(ColorRect slash, Vector2 iconSize)
    {
        var len = iconSize.Length() * 0.9f;
        var thick = Math.Max(3f, iconSize.Y * 0.12f);
        slash.Size = new Vector2(len, thick);
        slash.PivotOffset = slash.Size / 2;
        slash.Position = iconSize / 2 - slash.Size / 2;
        slash.Rotation = -Mathf.Atan2(iconSize.Y, iconSize.X);
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

    private static void SetAlpha(Control c, float a)
    {
        var m = c.Modulate;
        m.A = a;
        c.Modulate = m;
    }
}
