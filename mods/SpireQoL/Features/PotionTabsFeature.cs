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

    /// <summary>The potion slots (NPotionContainer.Holders): beta only (decision #163).</summary>
    [BetaOnly, System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static IReadOnlyList<NPotionHolder> HoldersOf(NPotionContainer container) => container.Holders;

    private static readonly Func<NPotionContainer, IReadOnlyList<NPotionHolder>>? Holders = BetaOnly.Gate<Func<NPotionContainer, IReadOnlyList<NPotionHolder>>>(HoldersOf);

    /// <summary>Main's potion slots: its private list (main has no Holders).</summary>
    private static readonly System.Reflection.FieldInfo? MainHoldersField = AccessTools.Field(typeof(NPotionContainer), "_holders");

    /// <summary>What the tabs were last drawn for (OnFrame): drawn again only when it changes.</summary>
    private int _drawnFor;

    public void OnFrame(OverlayHost host)
    {
        var me = Sts2State.Me;
        if (_proto == null) TakeProto();
        Check(me);
        // The same potions in the same slots, the same board (settings among it) and container as last frame: the tabs stand as drawn.
        var drawnFor = new HashCode();
        drawnFor.Add(host.HideAll);
        drawnFor.Add(Sts2State.InCombat);
        drawnFor.Add(NRun.Instance?.GlobalUi?.TopBar?.PotionContainer is { } shownContainer && GodotObject.IsInstanceValid(shownContainer) ? shownContainer.GetInstanceId() : 0);
        if (me != null)
        {
            if (Sts2State.Combat is { } combat) drawnFor.Add(SpireQoL.Combat.BoardProbe.Hash(me, combat));
            var slots = me.PotionSlots;
            for (var i = 0; i < slots.Count; i++) drawnFor.Add(slots[i] == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(slots[i]));
        }
        if (drawnFor.ToHashCode() == _drawnFor) return;
        _drawnFor = drawnFor.ToHashCode();
        // Read only when drawing: the holders' list is made fresh on each read.
        var holders = NRun.Instance?.GlobalUi?.TopBar?.PotionContainer is { } container
            ? Holders?.Invoke(container) ?? MainHoldersField?.GetValue(container) as IReadOnlyList<NPotionHolder>
            : null;
        var seen = new HashSet<ulong>();
        if (me != null && holders != null && Sts2State.InCombat && !host.HideAll)
        {
            var potions = me.Potions.ToList();
            var full = !me.HasOpenPotionSlots;
            var buckleReady = me.Relics.OfType<BeltBuckle>().FirstOrDefault() is { } buckle && !Sts2State.Private(buckle, "_dexterityApplied", fallback: true);
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
                _last[potion] = specs.Select(spec => spec.Key).ToList();
                var shown = specs.Where(spec => _ctx.Settings.TabEnabled(spec.Key) && (spec.Crossed ? _ctx.Settings.CrossedTabs : _ctx.Settings.PlainTabs)).ToList();
                seen.Add(holder.GetInstanceId());
                Show(holder, shown);
            }
        }
        // Holders no longer showing a potion (or out of combat): hide their tabs.
        foreach (var (id, tabs) in _tabs)
        {
            if (seen.Contains(id)) continue;
            foreach (var control in tabs) if (GodotObject.IsInstanceValid(control)) control.Visible = false;
        }
    }

    /// <summary>A copy of a hand card's enchantment tab (the card tabs' art), once.</summary>
    private void TakeProto()
    {
        foreach (var (node, _) in Sts2State.HandNodes())
        {
            if (node.EnchantmentTab is { } control && GodotObject.IsInstanceValid(control))
            {
                _proto = (Control)control.Duplicate();
                return;
            }
        }
    }

    private void Show(NPotionHolder holder, List<(string Key, RelicModel Relic, bool Crossed)> specs)
    {
        var id = holder.GetInstanceId();
        if (!_tabs.TryGetValue(id, out var tabs)) _tabs[id] = tabs = new List<Control>();
        tabs.RemoveAll(control => !GodotObject.IsInstanceValid(control));
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
        var left = (holder.Size.X - width) / 2f;
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
            tab.Position = new Vector2(left + i * step, top + tagH);
            tab.Modulate = Colors.White;
            tab.SelfModulate = Colors.White;
            if (tab.GetNodeOrNull<Label>(GodotNames.Path("Label")) is { } label) label.Visible = false;
            if (tab.GetNodeOrNull<TextureRect>(GodotNames.Path("Icon")) is { } icon)
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
                if (icon.GetNodeOrNull<ColorRect>(GodotNames.Path("SpireQoL_Slash")) is { } slash)
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
        if (_pending is { } pending)
        {
            if (pending.Frames < 60) _pending = pending with { Frames = pending.Frames + 1 };
            else
            {
                _pending = null;
                if (me != null) Compare(me, pending);
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
            var tabs = _last.Remove(used.Potion, out var previous) ? previous : new List<string>();
            _pending = new Pending(used.Potion, tabs, _prevStrength, _prevBuckle, Frames: 0);
        }
        _historySeen = list.Count;
        _prevStrength = Strength(me);
        _prevBuckle = BuckleApplied(me);
    }

    private static int Strength(MegaCrit.Sts2.Core.Entities.Players.Player me) => me.Creature.Powers.OfType<ReptileTrinketPower>().Sum(reptileTrinketPower => reptileTrinketPower.Amount);

    private static bool BuckleApplied(MegaCrit.Sts2.Core.Entities.Players.Player me)
        => me.Relics.OfType<BeltBuckle>().FirstOrDefault() is { } beltBuckle && Sts2State.Private(beltBuckle, "_dexterityApplied", fallback: false);

    private static void Compare(MegaCrit.Sts2.Core.Entities.Players.Player me, Pending pending)
    {
        var problems = new List<(string Key, string Text)>();
        if (me.Relics.Any(relic => relic is ReptileTrinket))
        {
            var gained = Strength(me) > pending.Strength;
            var predicted = pending.Tabs.Contains("potion:" + nameof(ReptileTrinket));
            if (gained != predicted) problems.Add((nameof(ReptileTrinket), $"Reptile Trinket: {(gained ? "fired" : "didn't fire")}, tab {(predicted ? "shown" : "not shown")}"));
        }
        if (me.Relics.Any(relic => relic is BeltBuckle))
        {
            var gained = !pending.DexterityApplied && BuckleApplied(me);
            var predicted = pending.Tabs.Contains("potion:" + nameof(BeltBuckle));
            if (gained != predicted) problems.Add((nameof(BeltBuckle), $"Belt Buckle: {(gained ? "gave Dexterity" : "didn't")}, tab {(predicted ? "shown" : "not shown")}"));
        }
        var what = $"potion {pending.Potion.GetType().Name}: tabs [{string.Join(", ", pending.Tabs)}]";
        ModEntry.Log.Detail($"[audit] [potion] {what}{(problems.Count == 0 ? ", matched" : "; " + string.Join("; ", problems.Select(problem => problem.Text)))}");
        Telemetry.Audits.Done(Telemetry.Audits.Potion, problems.Count == 0, problems.Count == 0 ? pending.Potion.GetType().Name : problems[0].Key);
        if (problems.Count > 0)
        {
            Sts2Mods.Core.Diagnostics.ErrorReporter.Instance?.Mismatch("potion", $"Potion tabs were wrong: {string.Join(", ", problems.Select(problem => problem.Key))}",
                new[] { "potion-tabs" }.Concat(problems.Select(problem => problem.Key)), what + "\n" + string.Join("\n", problems.Select(problem => problem.Text)));
        }
    }
}
