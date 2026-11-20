using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Potions;
using SpireQoL.Cards;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Potion tabs (docs/FEATURES.md "Potion Tabs"): in fights, the on-card tab's design hanging under each potion in the top
/// bar, point down like a banner, with the icon of a relic that drinking that potion sets off. Keys "potion:&lt;Relic&gt;".
///  - Reptile Trinket: every potion (3 Strength this turn: ReptileTrinket.AfterPotionUsed).
///  - Belt Buckle: your last potion, while its Dexterity isn't up (holding none gives it: AfterPotionUsed/Discarded).
///  - White Beast Statue: every potion while your slots are full (a potion reward comes after every fight:
///    ShouldForcePotionReward; with no room it's lost).
/// The tabs are copies of a hand card's enchantment tab (the same art as the card tabs). Check: after you drink a potion,
/// Reptile Trinket's Strength and Belt Buckle's Dexterity against the tabs it had.
/// </summary>
public sealed class PotionTabsFeature : IFeature, IFrameFeature
{
    private const string TabName = "SpireQoL_PotionTab";
    private const float TabOverlap = 0.2f; // fraction of the tag length tucked under the potion
    private const float IconScale = 0.85f;
    private static readonly Color SlashColor = new(1f, 0.15f, 0.1f, 0.95f);

    private readonly QolContext _ctx;
    /// <summary>A copy of a card's enchantment tab, kept to make potion tabs from (null until a card is seen).</summary>
    private Control? _proto;
    private readonly Dictionary<ulong, List<Control>> _tabs = new();
    /// <summary>
    /// Each potion's tabs on the last frame it was on the belt (the check reads them once it's drunk). Kept after it leaves the
    /// belt: it does before the game logs it as used. Cleared out of combat.
    /// </summary>
    private readonly Dictionary<PotionModel, List<string>> _last = new(ReferenceEqualityComparer.Instance);
    private int _historySeen = -1;
    private Pending? _pending;
    // Last frame's Reptile Trinket Strength and Belt Buckle state: the baseline for a potion drunk since (its relic may
    // already have reacted by the frame the entry shows up).
    private int _prevStrength;
    private bool _prevBuckle;

    private sealed record Pending(PotionModel Potion, List<string> Tabs, int Strength, bool DexterityApplied, int Frames);

    public PotionTabsFeature(QolContext ctx) => _ctx = ctx;

    public string Id => "potion-tabs";
    public string DisplayName => "Potion Tabs";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        // Frame-driven; no patches.
    }

    public void Attach(OverlayHost host)
    {
    }

    public void OnFrame(OverlayHost host)
    {
        var me = Sts2State.Me;
        var holders = NRun.Instance?.GlobalUi?.TopBar?.PotionContainer?.Holders;
        if (_proto == null) TakeProto();
        Check(me);
        var seen = new HashSet<ulong>();
        if (me != null && holders != null && Sts2State.InCombat && !host.HideAll)
        {
            var potions = me.Potions.ToList();
            var full = !me.HasOpenPotionSlots;
            var buckleReady = me.Relics.OfType<BeltBuckle>().FirstOrDefault() is { } buckle && !Sts2State.Private(buckle, "_dexterityApplied", true);
            foreach (var holder in holders)
            {
                if (!GodotObject.IsInstanceValid(holder) || holder.Potion is not { } node || !GodotObject.IsInstanceValid(node)) continue;
                PotionModel? potion;
                try { potion = node.Model; } catch { continue; }
                var specs = new List<(string Key, RelicModel Relic, bool Crossed)>();
                foreach (var relic in me.Relics)
                {
                    var on = relic switch
                    {
                        ReptileTrinket => true,
                        BeltBuckle => buckleReady && potions.Count == 1,
                        WhiteBeastStatue => full,
                        _ => false,
                    };
                    if (on) specs.Add(("potion:" + relic.GetType().Name, relic, false));
                }
                _last[potion] = specs.Select(s => s.Key).ToList();
                var shown = specs.Where(s => _ctx.Settings.TabEnabled(s.Key) && (s.Crossed ? _ctx.Settings.CrossedTabs : _ctx.Settings.PlainTabs)).ToList();
                seen.Add(holder.GetInstanceId());
                Show(holder, shown);
            }
        }
        // Holders no longer showing a potion (or out of combat): hide their tabs.
        foreach (var (id, tabs) in _tabs)
        {
            if (seen.Contains(id)) continue;
            foreach (var t in tabs) if (GodotObject.IsInstanceValid(t)) t.Visible = false;
        }
    }

    /// <summary>A copy of a hand card's enchantment tab (the card tabs' art), once.</summary>
    private void TakeProto()
    {
        foreach (var (node, _) in Sts2State.HandNodes())
        {
            if (node.EnchantmentTab is { } t && GodotObject.IsInstanceValid(t))
            {
                _proto = (Control)t.Duplicate();
                return;
            }
        }
    }

    private void Show(NPotionHolder holder, List<(string Key, RelicModel Relic, bool Crossed)> specs)
    {
        var id = holder.GetInstanceId();
        if (!_tabs.TryGetValue(id, out var tabs)) _tabs[id] = tabs = new List<Control>();
        tabs.RemoveAll(t => !GodotObject.IsInstanceValid(t));
        while (tabs.Count < specs.Count && _proto != null) tabs.Add(MakeTab(holder));
        if (_proto == null) return;

        // The tab art is long along x and thin along y; turned a quarter turn counter-clockwise, its outer end (x = 0, the
        // point) hangs down and its inner end tucks under the potion. Sized to the potion slot.
        var size = _proto.Size;
        var natural = size.Y * _proto.Scale.Y;
        var fit = Math.Min(1f, holder.Size.X * 0.55f / Math.Max(1f, natural));
        var scale = _proto.Scale * fit;
        var tagW = size.Y * scale.Y;
        var tagH = size.X * scale.X;
        const float gap = 2f;
        var step = tagW + gap;
        var width = specs.Count * tagW + Math.Max(0, specs.Count - 1) * gap;
        if (width > holder.Size.X && specs.Count > 1)
        {
            step = Math.Max(0f, (holder.Size.X - tagW) / (specs.Count - 1));
            width = step * (specs.Count - 1) + tagW;
        }
        var x0 = (holder.Size.X - width) / 2f;
        var top = holder.Size.Y - tagH * TabOverlap; // the tucked end starts just above the slot's bottom edge

        for (var i = 0; i < tabs.Count; i++)
        {
            var tab = tabs[i];
            tab.Visible = i < specs.Count;
            if (!tab.Visible) continue;
            var (_, relic, crossed) = specs[i];
            // Turning -90° about the top-left puts the visual box at y ∈ [-length, 0]: shift down by the length.
            tab.PivotOffset = Vector2.Zero;
            tab.Rotation = -Mathf.Pi / 2f;
            tab.Scale = scale;
            tab.Position = new Vector2(x0 + i * step, top + tagH);
            tab.Modulate = Colors.White;
            tab.SelfModulate = Colors.White;
            if (tab.GetNodeOrNull<Label>("Label") is { } label) label.Visible = false;
            if (tab.GetNodeOrNull<TextureRect>("Icon") is { } icon)
            {
                // Upright and centered in the part that hangs below the potion (x ∈ [0, length × (1 − overlap)]).
                var center = new Vector2(tab.Size.X * (1f - TabOverlap) / 2f, tab.Size.Y / 2f);
                icon.PivotOffset = icon.Size / 2f;
                icon.Rotation = Mathf.Pi / 2f;
                icon.Scale = new Vector2(IconScale, IconScale);
                icon.Position = center - icon.Size / 2f;
                Texture2D? tex = null;
                try { tex = relic.Icon; } catch { /* missing art */ }
                icon.Texture = GameIcons.Valid(tex) ? tex : null;
                icon.UseParentMaterial = false;
                icon.Modulate = crossed ? new Color(1, 1, 1, 0.8f) : Colors.White;
                if (icon.GetNodeOrNull<ColorRect>("SpireQoL_Slash") is { } slash)
                {
                    slash.Visible = crossed;
                    if (crossed) CardOverlayRenderer.LayoutSlash(slash, icon.Size);
                }
            }
        }
    }

    private Control MakeTab(NPotionHolder holder)
    {
        var tab = (Control)_proto!.Duplicate();
        tab.Name = TabName;
        ClearUniqueNames(tab);
        tab.MouseFilter = Control.MouseFilterEnum.Ignore;
        if (tab.Material is ShaderMaterial sm)
        {
            var own = (ShaderMaterial)sm.Duplicate();
            own.SetShaderParameter("h", 0.25);
            own.SetShaderParameter("s", 0.4);
            own.SetShaderParameter("v", 0.6);
            tab.Material = own;
        }
        if (tab.GetNodeOrNull<TextureRect>("Icon") is { } icon)
        {
            icon.AddChild(new ColorRect { Name = "SpireQoL_Slash", Color = SlashColor, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false });
        }
        holder.AddChild(tab);
        holder.MoveChild(tab, 0); // drawn first: the potion covers the tucked end
        return tab;
    }

    private static void ClearUniqueNames(Node node)
    {
        node.UniqueNameInOwner = false;
        foreach (var child in node.GetChildren()) ClearUniqueNames(child);
    }

    // ------------------------------------------------------------------ check

    /// <summary>
    /// A potion you drank (a new PotionUsedEntry of yours): a second later, Reptile Trinket's Strength and Belt Buckle's
    /// Dexterity against the tabs it had on its last frame.
    /// </summary>
    private void Check(MegaCrit.Sts2.Core.Entities.Players.Player? me)
    {
        if (_pending is { } p)
        {
            if (p.Frames < 60) _pending = p with { Frames = p.Frames + 1 };
            else
            {
                _pending = null;
                if (me != null) Compare(me, p);
            }
        }
        var entries = CombatManager.Instance?.History?.Entries;
        if (me == null || entries == null || !Sts2State.InCombat)
        {
            _historySeen = -1;
            _last.Clear();
            return;
        }
        var list = entries.ToList();
        if (_historySeen < 0 || _historySeen > list.Count) _historySeen = list.Count;
        for (var i = _historySeen; i < list.Count; i++)
        {
            if (list[i] is not PotionUsedEntry used || used.Actor != me.Creature) continue;
            var tabs = _last.Remove(used.Potion, out var t) ? t : new List<string>();
            _pending = new Pending(used.Potion, tabs, _prevStrength, _prevBuckle, 0);
        }
        _historySeen = list.Count;
        _prevStrength = Strength(me);
        _prevBuckle = BuckleApplied(me);
    }

    private static int Strength(MegaCrit.Sts2.Core.Entities.Players.Player me) => me.Creature.Powers.OfType<ReptileTrinketPower>().Sum(x => x.Amount);

    private static bool BuckleApplied(MegaCrit.Sts2.Core.Entities.Players.Player me)
        => me.Relics.OfType<BeltBuckle>().FirstOrDefault() is { } b && Sts2State.Private(b, "_dexterityApplied", false);

    private static void Compare(MegaCrit.Sts2.Core.Entities.Players.Player me, Pending p)
    {
        var problems = new List<(string Key, string Text)>();
        if (me.Relics.Any(r => r is ReptileTrinket))
        {
            var gained = Strength(me) > p.Strength;
            var predicted = p.Tabs.Contains("potion:" + nameof(ReptileTrinket));
            if (gained != predicted) problems.Add((nameof(ReptileTrinket), $"Reptile Trinket: {(gained ? "fired" : "didn't fire")}, tab {(predicted ? "shown" : "not shown")}"));
        }
        if (me.Relics.Any(r => r is BeltBuckle))
        {
            var gained = !p.DexterityApplied && BuckleApplied(me);
            var predicted = p.Tabs.Contains("potion:" + nameof(BeltBuckle));
            if (gained != predicted) problems.Add((nameof(BeltBuckle), $"Belt Buckle: {(gained ? "gave Dexterity" : "didn't")}, tab {(predicted ? "shown" : "not shown")}"));
        }
        var what = $"potion {p.Potion.GetType().Name}: tabs [{string.Join(", ", p.Tabs)}]";
        ModEntry.Log.Info($"[audit] [potion] {what}{(problems.Count == 0 ? ", matched" : "; " + string.Join("; ", problems.Select(x => x.Text)))}");
        Telemetry.Audits.Done(Telemetry.Audits.Potion, problems.Count == 0, problems.Count == 0 ? p.Potion.GetType().Name : problems[0].Key);
        if (problems.Count > 0)
        {
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("potion", $"Potion tabs were wrong: {string.Join(", ", problems.Select(x => x.Key))}",
                new[] { "potion-tabs" }.Concat(problems.Select(x => x.Key)), what + "\n" + string.Join("\n", problems.Select(x => x.Text)));
        }
    }
}
