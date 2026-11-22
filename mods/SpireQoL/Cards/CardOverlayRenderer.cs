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
///    a tab means "playing this might be bad for you".
///  - **Opacity**: Ethereal translucency, of the card as one picture (<see cref="FlatFade"/>, decision #113).
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
    private const float TabOverlap = 0.25f; // fraction of the tag length tucked under the card (hidden by the card art)
    private const float TagDrop = 0.08f; // fraction of the tag length the backdrop slides down (the icon stays centered in what's visible)
    private const float IconNudge = 0.03f; // fraction of the tag length the icon sits below the visible part's center (it read as hugging the top)
    private const float IconScale = 0.85f; // icon size relative to the tab art's own icon slot

    private readonly OverlayHost _host;
    private readonly CardDecorFrame _frame;
    private readonly Dictionary<ulong, Entry> _entries = new();

    public CardOverlayRenderer(OverlayHost host, CardDecorFrame frame)
    {
        _host = host;
        _frame = frame;
    }

    /// <summary>Time spent this frame on flattened fades (decision #113), reported to the host as "ethereal-fade" for the dev dashboard.</summary>
    private static double _flatMs;

    private sealed class Entry
    {
        public required NCard Card;
        public required Control Container;
        public required Control Overlay;
        public bool TouchedAlpha;
        public bool Seen;
        /// <summary>The flattened copy drawn in the card's place while it's faded (decision #113).</summary>
        public FlatFade? Flat;
        /// <summary>The copy couldn't be made or failed: the card fades layer by layer until it's unfaded.</summary>
        public bool FlatFailed;
        public readonly List<Control> Tabs = new();

        /// <summary>
        /// Holds the tags, placed directly after the card's glow (NCard.CardHighlight) in the glow's
        /// parent, so tags draw above the glow but beneath the card art. Mirrors the card frame's
        /// transform, so tag positions are in card-frame coordinates. Falls back to Overlay.
        /// </summary>
        public Control? TagLayer;
    }

    /// <summary>
    /// Cards outside your hand to decorate this frame (cards you're offered on a reward, shop or event screen: decision #62),
    /// filled by their feature before the frame's render and cleared by it. They get the same tabs, lifecycle and cleanup as
    /// hand cards: the game's pooled nodes are put back as they were when they stop being listed.
    /// </summary>
    internal static readonly List<(NCard Node, CardDecor Decor)> Outside = new();

    public void Render()
    {
        foreach (var entry in _entries.Values) entry.Seen = false;

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

        if (!_host.HideAll)
        {
            foreach (var (node, decor) in Outside)
            {
                if (!GodotObject.IsInstanceValid(node) || decor.IsEmpty || !node.IsVisibleInTree()) continue;
                var entry = GetOrCreate(node);
                if (entry == null) continue;
                entry.Seen = true;
                Apply(entry, decor);
            }
        }
        Outside.Clear();
        if (_flatMs > 0) _host.AddPartTime("ethereal-fade", _flatMs);
        _flatMs = 0;

        // Undo anything on nodes that are no longer decorated hand cards.
        var dead = new List<ulong>();
        foreach (var (id, otherEntry) in _entries)
        {
            if (!GodotObject.IsInstanceValid(otherEntry.Card) || !GodotObject.IsInstanceValid(otherEntry.Overlay))
            {
                dead.Add(id);
                continue;
            }
            if (otherEntry.Seen) continue;
            otherEntry.Overlay.Visible = false;
            if (otherEntry.TagLayer != null && GodotObject.IsInstanceValid(otherEntry.TagLayer)) otherEntry.TagLayer.Visible = false;
            if (otherEntry.TouchedAlpha) Unfade(otherEntry);
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
        // A stale entry's tag layer lives in the card node, not the overlay; free it so its tags
        // don't linger on a reused node.
        if (existing?.TagLayer != null && GodotObject.IsInstanceValid(existing.TagLayer)) existing.TagLayer.QueueFree();

        var container = card.GetNodeOrNull<Control>(GodotNames.Path("%CardContainer")) ?? card;

        var overlay = new Control { Name = OverlayName, MouseFilter = Control.MouseFilterEnum.Ignore };
        container.AddChild(overlay);

        var entry = new Entry { Card = card, Container = container, Overlay = overlay };
        _entries[id] = entry;
        return entry;
    }

    /// <summary>
    /// A card outside the hand drawn with tabs exactly as in the hand (the settings menu's Guide page). The card is ours,
    /// not one of the game's pooled nodes, so nothing needs undoing. Call <see cref="Draw"/> every frame: the card's layout
    /// settles a frame or two after it enters the tree.
    /// </summary>
    internal sealed class Example
    {
        private readonly Entry _entry;

        public Example(NCard card)
        {
            var container = card.GetNodeOrNull<Control>(GodotNames.Path("%CardContainer")) ?? card;
            var overlay = new Control { Name = OverlayName, MouseFilter = Control.MouseFilterEnum.Ignore };
            container.AddChild(overlay);
            _entry = new Entry { Card = card, Container = container, Overlay = overlay };
        }

        public void Draw(CardDecor decor)
        {
            if (!GodotObject.IsInstanceValid(_entry.Card) || !GodotObject.IsInstanceValid(_entry.Overlay)) return;
            Apply(_entry, decor);
        }
    }

    private static void Apply(Entry entry, CardDecor decor)
    {
        // Follow the card frame's transform inside the container.
        var frame = entry.Card.GetNodeOrNull<Control>(GodotNames.Path("%Frame"));
        Vector2 size;
        if (frame != null && frame != entry.Container && frame.Size.X > 1)
        {
            var rel = entry.Container.GetGlobalTransform().AffineInverse() * frame.GetGlobalTransform();
            entry.Overlay.Position = rel.Origin;
            entry.Overlay.Rotation = rel.Rotation;
            entry.Overlay.Scale = rel.Scale;
            size = frame.Size;
        }
        else
        {
            entry.Overlay.Position = -DefaultCardSize / 2;
            entry.Overlay.Rotation = 0;
            entry.Overlay.Scale = Vector2.One;
            size = DefaultCardSize;
        }
        entry.Overlay.Size = size;
        entry.Overlay.Visible = true;
        if (entry.Overlay.GetIndex() != entry.Container.GetChildCount() - 1)
        {
            entry.Container.MoveChild(entry.Overlay, -1);
        }

        var layer = PlaceTagLayer(entry, size);
        ApplyTabs(entry, decor.Tabs, layer);

        if (decor.Opacity is { } opacity) Fade(entry, opacity);
        else if (entry.TouchedAlpha) Unfade(entry);
    }

    /// <summary>
    /// The card at <paramref name="alpha"/> opacity as one picture (decision #113): a flattened copy drawn in its place. Layer by
    /// layer (each of the container's layers faded on its own, the art showing through the frame) when the copy can't be made.
    /// </summary>
    private static void Fade(Entry entry, float alpha)
    {
        entry.TouchedAlpha = true;
        if (entry.Container != entry.Card && !entry.FlatFailed)
        {
            var start = Time.GetTicksUsec();
            try { FadeFlat(entry, alpha); }
            finally { _flatMs += (Time.GetTicksUsec() - start) / 1000.0; }
            if (!entry.FlatFailed) return;
        }
        SetAlpha(entry.Container, alpha);
    }

    /// <summary>The flattened copy made if needed and drawn this frame; <see cref="Entry.FlatFailed"/> set when it can't be.</summary>
    private static void FadeFlat(Entry entry, float alpha)
    {
        if (entry.Flat is { Valid: false }) entry.Flat = null;
        entry.Flat ??= FlatFade.Create(entry.Card, entry.Container);
        if (entry.Flat == null)
        {
            entry.FlatFailed = true;
            return;
        }
        try
        {
            var glow = entry.Card.CardHighlight;
            entry.Flat.Update(alpha, new Node?[] { glow, entry.TagLayer, entry.Overlay }, new Node?[] { glow, entry.TagLayer }, glow);
        }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"Ethereal: the flattened fade failed ({ex.Message}); this card fades its layers instead");
            entry.FlatFailed = true;
            entry.Flat.Dispose();
            entry.Flat = null;
        }
    }

    private static void Unfade(Entry entry)
    {
        entry.TouchedAlpha = false;
        entry.Flat?.Dispose();
        entry.Flat = null;
        entry.FlatFailed = false;
        SetAlpha(entry.Container, alpha: 1f);
    }

    /// <summary>
    /// Puts the tag layer right after the card's glow node and makes it match the card frame
    /// (via the overlay, which already does). Returns the layer, or the overlay as a fallback.
    /// </summary>
    private static Control PlaceTagLayer(Entry entry, Vector2 size)
    {
        var glow = entry.Card.CardHighlight;
        if (glow == null || !GodotObject.IsInstanceValid(glow) || glow.GetParent() is not { } parent)
        {
            return entry.Overlay;
        }

        if (entry.TagLayer == null || !GodotObject.IsInstanceValid(entry.TagLayer))
        {
            entry.TagLayer = new Control { Name = "SpireQoL_TagLayer", MouseFilter = Control.MouseFilterEnum.Ignore };
            parent.AddChild(entry.TagLayer);
        }
        var layer = entry.TagLayer;
        if (layer.GetParent() != parent) layer.Reparent(parent, keepGlobalTransform: false);

        // Directly above the glow, below everything the card draws after it (the card art).
        var want = glow.GetIndex() + 1;
        if (layer.GetIndex() < glow.GetIndex()) want = glow.GetIndex();
        if (layer.GetIndex() != want) parent.MoveChild(layer, want);

        // Same transform as the card frame (the overlay mirrors it inside the container).
        var frameXf = entry.Overlay.GetGlobalTransform();
        var rel = parent is CanvasItem canvasItem ? canvasItem.GetGlobalTransform().AffineInverse() * frameXf : frameXf;
        layer.Position = rel.Origin;
        layer.Rotation = rel.Rotation;
        layer.Scale = rel.Scale;
        layer.Size = size;
        layer.Visible = true;
        return layer;
    }

    private static void ApplyTabs(Entry entry, List<TabSpec> tabs, Control layer)
    {
        var template = entry.Card.EnchantmentTab;
        if (template == null || !GodotObject.IsInstanceValid(template))
        {
            HideTabs(entry);
            return;
        }

        while (entry.Tabs.Count < tabs.Count)
        {
            entry.Tabs.Add(MakeTab(template, layer));
        }

        // The enchantment tab is drawn to stick out sideways from the card's left edge. Rotated a
        // quarter turn clockwise, its card-side end points down, so each tag hangs up off the
        // top edge like an enchantment tab would off the side. Visually a rotated tag is
        // tabSize.Y wide and tabSize.X tall.
        var tabSize = template.Size * template.Scale;
        var tagW = tabSize.Y;
        var tagH = tabSize.X;

        // Center the row on the top edge. Tags keep their size; if the row would be wider than the
        // card, only the spacing shrinks (tags move closer, overlapping if they must), so the row
        // never spills past the card's edges. Later tags draw over earlier ones.
        const float fit = 1f;
        var scale = template.Scale;
        var step = tagW + TabGap;
        var width = tabs.Count * tagW + Math.Max(0, tabs.Count - 1) * TabGap;
        if (width > layer.Size.X && tabs.Count > 1)
        {
            step = Math.Max(0f, (layer.Size.X - tagW) / (tabs.Count - 1));
            width = step * (tabs.Count - 1) + tagW;
        }
        var left = (layer.Size.X - width) / 2f;
        // Tucked under the card's top edge (the card art covers the tucked part); the icon end
        // sticks out above.
        var top = -tagH * fit * (1f - TabOverlap - TagDrop);

        for (var i = 0; i < entry.Tabs.Count; i++)
        {
            var tab = entry.Tabs[i];
            if (!GodotObject.IsInstanceValid(tab)) continue;
            if (tab.GetParent() != layer) tab.Reparent(layer, keepGlobalTransform: false);
            var on = i < tabs.Count;
            tab.Visible = on;
            if (!on) continue;

            var spec = tabs[i];
            try
            {
                LayoutTab(tab, spec, left + i * step, top, tagW * fit, scale);
            }
            catch (Exception ex)
            {
                // Never leave a tag half-configured: a copy of the card's enchantment tab that
                // failed partway would still show that enchantment's icon and number.
                tab.Visible = false;
                if (_tabErrors++ % 600 == 0) ModEntry.Log.Warn($"tab layout failed ({spec.Key}), tab hidden: {ex.Message}");
                Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Exception(ex, "tab-layout");
            }
        }
    }

    private static int _tabErrors;

    private static void LayoutTab(Control tab, TabSpec spec, float left, float top, float visualWidth, Vector2 scale)
    {
        {
            // Rotating +90° about the node's top-left puts its visual box at x ∈ [-h, 0], so shift
            // right by the visual width to land the tag's top-left at (left, top).
            tab.PivotOffset = Vector2.Zero;
            tab.Rotation = Mathf.Pi / 2f;
            tab.Scale = scale;
            tab.Position = new Vector2(left + visualWidth, top);
            // Not the template's tint: the game greys its tab for a disabled enchantment
            // (NCard.SetEnchantmentStatus: the tab's alpha, its shader's saturation, the label's
            // SelfModulate). A tab copied from a card showing a disabled enchantment keeps all three,
            // so every layout puts back the enabled look (the material is the tab's own, MakeTab).
            tab.Modulate = Colors.White;
            tab.SelfModulate = Colors.White;
            if (tab.Material is ShaderMaterial own)
            {
                own.SetShaderParameter("h", 0.25);
                own.SetShaderParameter("s", 0.4);
                own.SetShaderParameter("v", 0.6);
            }
            if (tab.GetNodeOrNull<Label>(GodotNames.Path("Label")) is { } tint) tint.SelfModulate = Colors.White;

            if (tab.GetNodeOrNull<TextureRect>(GodotNames.Path("Icon")) is { } icon)
            {
                // Keep the icon upright, slightly smaller, and centered in the part of the tag that
                // sticks out above the card. In the tab's own (unrotated) coordinates the length
                // runs along x and maps downward onto the card after the +90° turn, so the visible
                // part is x ∈ [0, length × (1 − overlap − drop)] (the backdrop sits TagDrop lower).
                // The icon sits a touch below the center of that visible part (IconNudge).
                var visibleCenter = new Vector2(tab.Size.X * ((1f - TabOverlap - TagDrop) / 2f + IconNudge), tab.Size.Y / 2f);
                icon.PivotOffset = icon.Size / 2f;
                icon.Rotation = -Mathf.Pi / 2f;
                icon.Scale = new Vector2(IconScale, IconScale);
                icon.Position = visibleCenter - icon.Size / 2f;
                // Text tabs (replay "2×") have no icon.
                icon.Texture = spec.Text == null && spec.Card == null && GameIcons.Valid(spec.Icon) ? spec.Icon : null;
                CardIcon(icon, spec.Card);
                // The game sets this on its own icon for a disabled enchantment, which runs the icon
                // through the backdrop's green colorize shader; Duplicate() copies whatever it was.
                icon.UseParentMaterial = false;
                icon.Modulate = spec.Crossed ? new Color(1, 1, 1, 0.8f) : Colors.White;
                if (icon.GetNodeOrNull<ColorRect>(GodotNames.Path("SpireQoL_Slash")) is { } slash)
                {
                    slash.Visible = spec.Crossed;
                    if (spec.Crossed) LayoutSlash(slash, icon.Size);
                }
                // Aim marker (decision #55): it happens only at some targets. The targeting arrow's head in the icon's top-left
                // corner, clear of the slash (bottom-left to top-right) and the number (bottom-right). A tab's own marker (the heart:
                // decision #83) goes in the same corner.
                if (icon.GetNodeOrNull<TextureRect>(GodotNames.Path("SpireQoL_Aim")) is { } aim)
                {
                    var arrow = spec.Marker ?? (spec.Aimed ? GameIcons.TargetArrow() : null);
                    aim.Visible = GameIcons.Valid(arrow);
                    if (aim.Visible) LayoutAim(aim, arrow!, icon.Size);
                }
            }
            // The tab's number label shows only on quantified tabs (block / damage / summon procs, energy a free card saves or "?").
            if (tab.GetNodeOrNull<Label>(GodotNames.Path("Label")) is { } label)
            {
                label.Visible = spec.Number != null || spec.Text != null || spec.NumberText != null;
                if (spec.Text != null && tab.GetNodeOrNull<TextureRect>(GodotNames.Path("Icon")) is { } otherIcon)
                {
                    // Text tab: the text fills the icon's slot, upright. The label is the game's
                    // auto-sizing MegaLabel: give it the icon's box and a large max font size, and
                    // it picks the biggest size that fits.
                    RememberLabel(label);
                    var box = otherIcon.Size * IconScale;
                    // With a count too (Pillage, Scrape: decision #124), the chance takes the lower half and the count sits centred
                    // above it, a gap between them. In the tab's unrotated coordinates, screen-down is +x.
                    var two = spec.NumberText != null;
                    label.Size = new Vector2(box.Y, two ? box.X * 0.5f : box.X); // upright after the -90° turn
                    if (label is MegaCrit.Sts2.addons.mega_text.MegaLabel mega)
                    {
                        mega.AutoSizeEnabled = true;
                        mega.MaxFontSize = Math.Max((int)label.GetMeta(MetaMaxFont), (int)(box.X * 0.8f));
                        mega.SetTextAutoSize(spec.Text);
                    }
                    else
                    {
                        label.Text = spec.Text;
                    }
                    label.HorizontalAlignment = HorizontalAlignment.Center;
                    label.VerticalAlignment = VerticalAlignment.Center;
                    label.PivotOffset = label.Size / 2f;
                    label.Rotation = -Mathf.Pi / 2f;
                    label.Position = otherIcon.Position + otherIcon.Size / 2f + new Vector2(two ? box.X * 0.24f : 0, 0) - label.Size / 2f;
                }
                else if ((spec.Number?.ToString() ?? spec.NumberText) is { } numberText && tab.GetNodeOrNull<TextureRect>(GodotNames.Path("Icon")) is { } nextIcon)
                {
                    RestoreLabel(label);
                    InCorner(label, numberText, nextIcon);
                }
            }
            // A text tab with a count too (a drawn-card chance tab: how many, centred above its chance: decisions #119, #124): the
            // count on a second label made with the tab, in the game's number style.
            if (tab.GetNodeOrNull<Label>(GodotNames.Path("SpireQoL_Corner")) is { } second)
            {
                second.Visible = spec.Text != null && spec.NumberText != null;
                if (second.Visible && tab.GetNodeOrNull<TextureRect>(GodotNames.Path("Icon")) is { } ic2) AboveText(second, spec.NumberText!, ic2);
            }
        }
    }

    /// <summary>A text tab's count (decision #124): upright, centred across the icon, in its upper part (screen-up is -x).</summary>
    private static void AboveText(Label label, string text, TextureRect icon)
    {
        label.Text = text;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.PivotOffset = label.Size / 2f;
        label.Rotation = -Mathf.Pi / 2f;
        var box = icon.Size * IconScale;
        label.Position = icon.Position + icon.Size / 2f - new Vector2(box.X * 0.26f, 0) - label.Size / 2f;
    }

    /// <summary>
    /// A number upright like the icon, centered on the icon's lower-right corner as the game places power amounts. In the tab's
    /// unrotated coordinates, screen-down is +x and screen-right is -y.
    /// </summary>
    private static void InCorner(Label label, string text, TextureRect icon)
    {
        label.Text = text;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.PivotOffset = label.Size / 2f;
        label.Rotation = -Mathf.Pi / 2f;
        var half = icon.Size * IconScale / 2f;
        var iconCenter = icon.Position + icon.Size / 2f;
        var corner = iconCenter + new Vector2(half.Y * 0.8f, -half.X * 0.8f);
        label.Position = corner - label.Size / 2f;
    }

    private static readonly Dictionary<ulong, (string Name, TinyCardIcon Icon)> TinyCards = new();

    /// <summary>A tab whose icon is a card (TabSpec.Card): the game's tiny card in the icon's slot; removed for other tabs.</summary>
    internal static void CardIcon(TextureRect icon, MegaCrit.Sts2.Core.Models.CardModel? card)
    {
        var id = icon.GetInstanceId();
        var name = card?.GetType().Name;
        if (TinyCards.TryGetValue(id, out var have) && (have.Name != name || !GodotObject.IsInstanceValid(have.Icon.Root)))
        {
            if (GodotObject.IsInstanceValid(have.Icon.Root)) have.Icon.Root.QueueFree();
            TinyCards.Remove(id);
        }
        if (card == null) return;
        if (!TinyCards.TryGetValue(id, out have))
        {
            var tiny = new TinyCardIcon(card, Math.Min(icon.Size.X, icon.Size.Y));
            icon.AddChild(tiny.Root);
            have = (name!, tiny);
            TinyCards[id] = have;
        }
        var side = Math.Min(icon.Size.X, icon.Size.Y);
        have.Icon.Root.Position = (icon.Size - new Vector2(side, side)) / 2f;
        have.Icon.Fit();
    }

    private const string MetaSize = "spireqol_label_size";
    private const string MetaMaxFont = "spireqol_label_max_font";

    /// <summary>Saves the tab label's own size and max font size once (tab nodes are reused).</summary>
    private static void RememberLabel(Label label)
    {
        if (label.HasMeta(MetaSize)) return;
        label.SetMeta(MetaSize, label.Size);
        label.SetMeta(MetaMaxFont, label is MegaCrit.Sts2.addons.mega_text.MegaLabel megaLabel ? megaLabel.MaxFontSize : 0);
    }

    /// <summary>Puts back what a text tab changed, for a number tab on the same node.</summary>
    private static void RestoreLabel(Label label)
    {
        if (!label.HasMeta(MetaSize)) return;
        label.Size = (Vector2)label.GetMeta(MetaSize);
        if (label is MegaCrit.Sts2.addons.mega_text.MegaLabel megaLabel) megaLabel.MaxFontSize = (int)label.GetMeta(MetaMaxFont);
    }

    private static Control MakeTab(Control template, Control overlay)
    {
        var tab = (Control)template.Duplicate();
        tab.Name = "SpireQoL_Tab";
        ClearUniqueNames(tab);
        tab.MouseFilter = Control.MouseFilterEnum.Ignore;
        // The tab's backdrop uses a colorize shader (NCard.SetEnchantmentStatus sets h/s/v on it).
        // Duplicate() shares that material with the card's own tab, so give each tag its own copy
        // with the "enabled" values; otherwise a disabled enchantment greys our tags too.
        if (tab.Material is ShaderMaterial shaderMaterial)
        {
            var own = (ShaderMaterial)shaderMaterial.Duplicate();
            own.SetShaderParameter("h", 0.25);
            own.SetShaderParameter("s", 0.4);
            own.SetShaderParameter("v", 0.6);
            tab.Material = own;
        }
        if (tab.GetNodeOrNull<TextureRect>(GodotNames.Path("Icon")) is { } icon)
        {
            icon.AddChild(new ColorRect
            {
                Name = "SpireQoL_Slash",
                Color = SlashColor,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = false,
            });
            icon.AddChild(new TextureRect
            {
                Name = "SpireQoL_Aim",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Visible = false,
            });
        }
        // The corner number of a text tab that has one (decision #119), copied from the game's label before anything changes it.
        if (tab.GetNodeOrNull<Label>(GodotNames.Path("Label")) is { } number)
        {
            var second = (Label)number.Duplicate();
            second.Name = "SpireQoL_Corner";
            second.Visible = false;
            tab.AddChild(second);
        }
        overlay.AddChild(tab);
        return tab;
    }

    /// <summary>The aim marker: 60% of the icon over its top-left corner, its bottom-right a third of the way into the icon
    /// (clear of the slash and the number); it grows up and to the left, past the icon's edge.</summary>
    internal static void LayoutAim(TextureRect aim, Texture2D arrow, Vector2 iconSize)
    {
        aim.Texture = arrow;
        var side = Math.Min(iconSize.X, iconSize.Y) * 0.6f;
        aim.Size = new Vector2(side, side);
        aim.PivotOffset = aim.Size / 2f;
        var bottomRight = Math.Min(iconSize.X, iconSize.Y) * 0.325f;
        aim.Position = new Vector2(bottomRight, bottomRight) - aim.Size;
    }

    /// <summary>A diagonal red bar across the icon (bottom-left to top-right).</summary>
    internal static void LayoutSlash(ColorRect slash, Vector2 iconSize)
    {
        var len = iconSize.Length() * 0.9f;
        var thick = Math.Max(3f, iconSize.Y * 0.12f);
        slash.Size = new Vector2(len, thick);
        slash.PivotOffset = slash.Size / 2;
        slash.Position = iconSize / 2 - slash.Size / 2;
        slash.Rotation = -Mathf.Atan2(iconSize.Y, iconSize.X);
    }

    private static void HideTabs(Entry entry)
    {
        foreach (var tab in entry.Tabs)
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

    private static void SetAlpha(Control control, float alpha)
    {
        var color = control.Modulate;
        color.A = alpha;
        control.Modulate = color;
    }
}
