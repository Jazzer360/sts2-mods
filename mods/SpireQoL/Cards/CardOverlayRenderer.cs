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
    private const float TabOverlap = 0.3f; // fraction of the tab height that sits on the card

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

        ApplyTabs(e, d.Tabs);

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
            e.Tabs.Add(MakeTab(template, e.Overlay));
        }

        // Center the row on the top edge; shrink it if it would be wider than the card.
        var tabSize = template.Size * template.Scale;
        var total = tabs.Count * tabSize.X + Math.Max(0, tabs.Count - 1) * TabGap;
        var fit = total > e.Overlay.Size.X && total > 0 ? e.Overlay.Size.X / total : 1f;
        var scale = template.Scale * fit;
        var step = (tabSize.X + TabGap) * fit;
        var x0 = (e.Overlay.Size.X - total * fit) / 2f;
        var y = -tabSize.Y * fit * (1f - TabOverlap);

        for (var i = 0; i < e.Tabs.Count; i++)
        {
            var tab = e.Tabs[i];
            if (!GodotObject.IsInstanceValid(tab)) continue;
            var on = i < tabs.Count;
            tab.Visible = on;
            if (!on) continue;

            var spec = tabs[i];
            tab.Position = new Vector2(x0 + i * step, y);
            tab.Scale = scale;
            tab.Rotation = 0f;
            // Not the template's tint: the game greys its tab for a disabled enchantment.
            tab.Modulate = Colors.White;
            tab.SelfModulate = Colors.White;

            if (tab.GetNodeOrNull<TextureRect>("Icon") is { } icon)
            {
                icon.Texture = spec.Icon;
                icon.Modulate = spec.Crossed ? new Color(1, 1, 1, 0.8f) : Colors.White;
                if (icon.GetNodeOrNull<ColorRect>("SpireQoL_Slash") is { } slash)
                {
                    slash.Visible = spec.Crossed;
                    if (spec.Crossed) LayoutSlash(slash, icon.Size);
                }
            }
            if (tab.GetNodeOrNull<Label>("Label") is { } label)
            {
                label.Visible = spec.Label != null;
                label.Text = spec.Label ?? "";
            }
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
