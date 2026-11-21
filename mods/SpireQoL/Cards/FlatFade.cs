using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace SpireQoL.Cards;

/// <summary>
/// Ethereal translucency of a card as one picture (decision #113): a copy of the card (the game's card scene, the same model) is
/// drawn at full opacity into an image of its own (a SubViewport), once, and again only when the card shows something new; the
/// image is drawn in the card's container at the Ethereal opacity, where the card's own layers were, and those layers are made
/// invisible (alpha 0: the card stays where it is, so hovering, dragging and playing it are the game's own). The card's glow and
/// the mod's tab layers stay drawn. Fading the card's layers one by one let the art show through the frame; fading them as a
/// group in a CanvasGroup drew wrong in Godot (decisions #110 to #112). Nothing of the game's is moved or changed but the
/// alpha of the container's layers, put back on <see cref="Dispose"/>.
/// </summary>
internal sealed class FlatFade
{
    /// <summary>The image's resolution over the card's own size, so it stays sharp when the card is hovered and grows.</summary>
    private const float Res = 1.5f;
    /// <summary>Room around the card in the image (card units), for its shadow.</summary>
    private const float Margin = 40f;

    private static readonly FieldInfo? PreviewTarget = AccessTools.Field(typeof(NCard), "_previewTarget");
    private static PackedScene? _scene;
    private static readonly HashSet<string> LoggedMismatch = new();
    /// <summary>Images drawn this launch (each one a copy updated and drawn once): the dev log reports the count with each draw.</summary>
    private static int Redraws;

    private readonly NCard _live;
    private readonly Control _container;
    private readonly NCard _copy;
    private readonly SubViewport _view;
    private readonly TextureRect _image;
    private readonly List<(CanvasItem Node, float Alpha)> _hidden = new();
    private bool _placed;
    private int _signature;
    private int _poll;
    private int _renderedFrames = -1; // frames since the last render was asked for (-1: none yet)
    private bool _verify;
    /// <summary>The glow left out where the card covers it (decision #127), once the image is drawn; null when its shader can't take it.</summary>
    private GlowMask? _glowMask;
    private bool _glowTried;

    private FlatFade(NCard live, Control container)
    {
        _live = live;
        _container = container;
        _scene ??= ResourceLoader.Load<PackedScene>("res://scenes/cards/card.tscn");
        _copy = _scene.Instantiate<NCard>();
        _copy.Model = live.Model;
        _copy.Scale = new Vector2(Res, Res);
        var size = (NCard.defaultSize + new Vector2(Margin, Margin) * 2) * Res;
        _view = new SubViewport
        {
            Name = "SpireQoL_FlatFadeView",
            TransparentBg = true,
            Disable3D = true,
            Size = new Vector2I(Mathf.CeilToInt(size.X), Mathf.CeilToInt(size.Y)),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        _view.AddChild(_copy);
        _image = new TextureRect
        {
            Name = "SpireQoL_FlatFade",
            Texture = _view.GetTexture(),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            Visible = false,
        };
        _image.AddChild(_view);
        container.AddChild(_image);
    }

    /// <summary>The flattened fade for this card, or null when it can't be made (the card then fades layer by layer).</summary>
    public static FlatFade? Create(NCard live, Control container)
    {
        if (live.Model == null) return null;
        try { return new FlatFade(live, container); }
        catch (Exception ex)
        {
            ModEntry.Log.Warn($"Ethereal: couldn't make a flattened copy of the card ({ex.Message}); fading its layers instead");
            return null;
        }
    }

    public bool Valid => GodotObject.IsInstanceValid(_image) && GodotObject.IsInstanceValid(_copy);

    /// <summary>
    /// Every frame while the card is faded. <paramref name="keep"/>: the container's layers that stay drawn (the glow, the mod's);
    /// <paramref name="under"/>: those of them the image is drawn over (the glow, the tabs' layer).
    /// </summary>
    public void Update(float alpha, IReadOnlyList<Node?> keep, IReadOnlyList<Node?> under, CanvasItem? glow = null)
    {
        if (!_copy.IsNodeReady()) { HideLayers(alpha, keep, ready: false); return; }
        if (!_placed)
        {
            _placed = true;
            Quiet(_copy);
            _copy.CardHighlight.Visible = false; // the live card's own glow stays drawn
            if (_copy.GetNodeOrNull<CanvasItem>("CardContainer/CardSparkles") is { } sparkles) sparkles.Visible = false;
            // The card frame's top-left corner at the margin, wherever the scene puts its origin.
            if (_copy.GetNodeOrNull<Control>("%Frame") is { } frame)
                _copy.Position += new Vector2(Margin, Margin) * Res - frame.GetGlobalTransform().Origin;
            _signature = 0;
        }

        // Drawn again only when the card shows something new (cost, text, an upgrade, a target's numbers).
        if (_signature == 0 || ++_poll % 3 == 0)
        {
            var now = Signature(_container, keep);
            if (now != _signature)
            {
                var start = Time.GetTicksUsec();
                Sync();
                Redraws++;
                if (ModEntry.DevMode)
                    ModEntry.Log.Info($"[perf] ethereal fade: drew {_live.Model?.GetType().Name ?? "?"} " +
                        $"({(Time.GetTicksUsec() - start) / 1000.0:0.00} ms to update the copy; {Redraws} drawn this launch)");
                _signature = now;
                _view.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
                _renderedFrames = 0;
                _verify = ModEntry.DevMode;
            }
        }
        if (_renderedFrames >= 0 && _renderedFrames < 2) _renderedFrames++;
        var ready = _renderedFrames >= 2; // asked for last frame, drawn at its end

        if (ready && _verify)
        {
            _verify = false;
            Verify(keep);
        }

        // The image in the container's own coordinates: the copy's container drawn into the image, mapped back.
        if (_copy.Body is { } body)
        {
            var back = body.GetGlobalTransform().AffineInverse();
            _image.Position = back.Origin;
            _image.Rotation = back.Rotation;
            _image.Scale = back.Scale;
            _image.Size = _view.Size;
        }
        var over = -1;
        foreach (var k in under) if (k != null && GodotObject.IsInstanceValid(k) && k.GetParent() == _container) over = Math.Max(over, k.GetIndex());
        if (_image.GetIndex() != over + 1) _container.MoveChild(_image, Math.Min(over + 1, _container.GetChildCount() - 1));
        var m = _image.Modulate;
        m.A = alpha;
        _image.Modulate = m;
        _image.Visible = ready;
        HideLayers(alpha, keep, ready);
        if (ready && glow != null)
        {
            if (!_glowTried) { _glowTried = true; _glowMask = GlowMask.Create(glow); }
            _glowMask?.Update(_image, _image.Texture);
        }
    }

    /// <summary>The card's own layers: invisible once the image is drawn, faded layer by layer until then.</summary>
    private void HideLayers(float alpha, IReadOnlyList<Node?> keep, bool ready)
    {
        foreach (var child in _container.GetChildren())
        {
            if (child is not CanvasItem ci || child == _image || keep.Contains(child)) continue;
            var i = _hidden.FindIndex(h => h.Node == ci);
            if (i < 0)
            {
                _hidden.Add((ci, ci.Modulate.A));
                i = _hidden.Count - 1;
            }
            var mm = ci.Modulate;
            mm.A = ready ? 0f : _hidden[i].Alpha * alpha;
            ci.Modulate = mm;
        }
    }

    /// <summary>The copy shows what the card shows: the same model, pile and target, through the game's own UpdateVisuals.</summary>
    private void Sync()
    {
        if (_copy.Model != _live.Model) _copy.Model = _live.Model;
        if (PreviewTarget?.GetValue(_live) is Creature target) _copy.SetPreviewTarget(target);
        else _copy.SetPreviewTarget(null);
        _copy.UpdateVisuals(_live.DisplayingPile, CardPreviewMode.Normal);
    }

    /// <summary>Dev mode: the copy's visible text, textures and colours against the card's, once per render; a difference is logged once per card and node.</summary>
    private void Verify(IReadOnlyList<Node?> keep)
    {
        var live = new List<string>();
        var copy = new List<string>();
        Describe(_container, keep, 0, "", live);
        Describe(_copy.Body, new Node?[] { _copy.CardHighlight }, 0, "", copy);
        var n = Math.Max(live.Count, copy.Count);
        for (var i = 0; i < n; i++)
        {
            var l = i < live.Count ? live[i] : "(nothing)";
            var c = i < copy.Count ? copy[i] : "(nothing)";
            if (l == c) continue;
            var card = _live.Model?.GetType().Name ?? "?";
            if (LoggedMismatch.Add(card + "|" + l))
                ModEntry.Log.Warn($"Ethereal: the faded copy of {card} differs from the card: the card has {l}, the copy {c}");
            return;
        }
    }

    private int Signature(Control root, IReadOnlyList<Node?> keep)
    {
        var parts = new List<string>();
        Describe(root, keep, 0, "", parts);
        var h = new HashCode();
        foreach (var p in parts) h.Add(p);
        var s = h.ToHashCode();
        return s == 0 ? 1 : s;
    }

    /// <summary>What a node tree shows: each visible node's text, texture and colours (not the top layers' alpha, which this sets).</summary>
    private void Describe(Node node, IReadOnlyList<Node?> keep, int depth, string path, List<string> into)
    {
        foreach (var child in node.GetChildren())
        {
            if (child == _image || keep.Contains(child) || child is GpuParticles2D or SubViewport) continue;
            if (child is not CanvasItem ci) continue;
            var p = path + "/" + child.Name;
            if (!ci.Visible) { into.Add(p + " hidden"); continue; }
            var s = p;
            if (depth > 0 && ci.Modulate != Colors.White) s += " modulate " + ci.Modulate.ToHtml();
            if (ci.SelfModulate != Colors.White) s += " self " + ci.SelfModulate.ToHtml();
            switch (child)
            {
                case Label l: s += $" \"{l.Text}\" {l.GetThemeColor("font_color").ToHtml()}"; break;
                case RichTextLabel r: s += $" \"{r.Text}\""; break;
                case TextureRect t: s += " tex " + (t.Texture?.ResourcePath is { Length: > 0 } rp ? rp : t.Texture?.GetInstanceId().ToString() ?? "none"); break;
            }
            into.Add(s);
            Describe(child, keep, depth + 1, p, into);
        }
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

    /// <summary>The card's layers back as they were; the image, its view and the copy freed.</summary>
    public void Dispose()
    {
        _glowMask?.Dispose();
        _glowMask = null;
        foreach (var (node, a) in _hidden)
        {
            if (!GodotObject.IsInstanceValid(node)) continue;
            var m = node.Modulate;
            m.A = a;
            node.Modulate = m;
        }
        _hidden.Clear();
        if (GodotObject.IsInstanceValid(_image))
        {
            _image.GetParent()?.RemoveChild(_image);
            _image.QueueFree();
        }
    }
}
