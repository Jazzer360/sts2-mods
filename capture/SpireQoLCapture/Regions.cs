using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace SpireQoLCapture;

/// <summary>
/// Screen regions in window pixels, found from the game's own nodes so crops and hovers hold at any
/// resolution:
///   full | hand | card N | enemy N | enemies | player | intent N | widget &lt;feature id&gt; | node &lt;name&gt;
///   text "&lt;label&gt;" (a visible button or label showing that text) | rect x y w h (fractions of the window)
/// Cards, enemies and intents count from 1, left to right.
/// </summary>
public static class Regions
{
    private static Viewport Root => ((SceneTree)Engine.GetMainLoop()).Root;

    public static Rect2? Resolve(string[] t)
    {
        if (t.Length == 0) return null;
        var size = Root.GetVisibleRect().Size;
        var windowSize = (Vector2)DisplayServer.WindowGetSize();
        switch (t[0].ToLowerInvariant())
        {
            case "full":
                return new Rect2(Vector2.Zero, windowSize);
            case "rect":
                return new Rect2(F(t[1]) * windowSize.X, F(t[2]) * windowSize.Y, F(t[3]) * windowSize.X, F(t[4]) * windowSize.Y);
            case "hand":
                return Union(HandCards().Select(Visual));
            case "card":
                return HandCards().ElementAtOrDefault(I(t[1]) - 1) is { } card ? Visual(card) : null;
            case "enemy":
                return Enemies().ElementAtOrDefault(I(t[1]) - 1) is { } enemy ? Window(enemy.Hitbox) : null;
            case "enemies":
                return Union(Enemies().Select(e => Window(e.Hitbox)));
            case "intent":
                return Enemies().ElementAtOrDefault(I(t[1]) - 1) is { } e2 ? Visual(e2.IntentContainer) : null;
            case "player":
                return NCombatRoom.Instance?.CreatureNodes.FirstOrDefault(c => c.Entity.IsPlayer) is { } p ? Window(p.Hitbox) : null;
            case "widget":
                return Find(Root, n => n.Name.ToString() == "Widget_" + t[1]) is Control w ? Visual(w) : null;
            case "node":
                return Find(Root, n => n.Name.ToString() == t[1]) is Control c ? Visual(c) : null;
            case "text":
                return Find(Root, n => n is Control { Visible: true } ctl && ctl.IsVisibleInTree() && ShowsText(n, t[1])) is Control l ? Window(l) : null;
            default:
                throw new Exception($"unknown region '{t[0]}'");
        }
    }

    private static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
    private static int I(string s) => int.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    private static IEnumerable<Control> HandCards()
        => NPlayerHand.Instance?.ActiveHolders.Select(h => (Control?)h.CardNode ?? h).OrderBy(c => Window(c).Position.X) ?? Enumerable.Empty<Control>();

    public static IEnumerable<NCreature> EnemyNodes() => Enemies();

    private static IEnumerable<NCreature> Enemies()
        => NCombatRoom.Instance?.CreatureNodes.Where(c => c.Entity.IsEnemy && c.Entity.IsAlive && c.IsVisibleInTree())
               .OrderBy(c => Window(c.Hitbox).Position.X) ?? Enumerable.Empty<NCreature>();

    private static bool ShowsText(Node n, string text) => n switch
    {
        Button b => b.Text.Contains(text, StringComparison.OrdinalIgnoreCase),
        Label l => l.Text.Contains(text, StringComparison.OrdinalIgnoreCase),
        RichTextLabel r => r.GetParsedText().Contains(text, StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    /// <summary>A control's rectangle in window pixels (through its canvas layer and the window's stretch).</summary>
    public static Rect2 Window(Control c)
    {
        var xf = c.GetViewport().GetFinalTransform() * c.GetGlobalTransformWithCanvas();
        return xf * new Rect2(Vector2.Zero, c.Size);
    }

    /// <summary>
    /// What a control draws: itself and its visible descendants with a size (card nodes are zero-sized
    /// pivots; their art and Spire QoL's tabs hang below them in the tree).
    /// </summary>
    public static Rect2 Visual(Control c)
    {
        Rect2? r = c.Size.X > 1 && c.Size.Y > 1 ? Window(c) : null;
        foreach (var child in Descendants(c, 10).OfType<Control>())
        {
            if (!child.IsVisibleInTree() || child.Size.X <= 1 || child.Size.Y <= 1) continue;
            if (child.Modulate.A <= 0.01f || child.SelfModulate.A <= 0.01f) continue;
            var cr = Window(child);
            r = r is { } acc ? acc.Merge(cr) : cr;
        }
        return r ?? Window(c);
    }

    private static Rect2? Union(IEnumerable<Rect2> rects)
    {
        Rect2? r = null;
        foreach (var x in rects) r = r is { } acc ? acc.Merge(x) : x;
        return r;
    }

    private static IEnumerable<Node> Descendants(Node n, int depth)
    {
        if (depth <= 0) yield break;
        foreach (var child in n.GetChildren(true))
        {
            yield return child;
            foreach (var d in Descendants(child, depth - 1)) yield return d;
        }
    }

    private static Node? Find(Node n, Func<Node, bool> match)
    {
        foreach (var child in n.GetChildren(true))
        {
            if (match(child)) return child;
            if (Find(child, match) is { } found) return found;
        }
        return null;
    }
}
