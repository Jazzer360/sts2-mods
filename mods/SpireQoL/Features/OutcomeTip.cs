using Godot;
using SpireQoL.Combat;
using SpireQoL.Sim;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Hovering Incoming Damage (decision #155): every way the enemy turn can go for you, the most likely first, each other one as its
/// difference from it (HP lost, and the sources that change: a Burn drawn, I Am Invincible's block), with its chance. The mod's own
/// panel, shown while the mouse is over the sum: the sum takes no mouse input (polled each frame), so it never gets in a card's way.
/// </summary>
internal sealed class OutcomeTip
{
    private const int MaxRows = 8;
    private const int FontSize = 18;
    private static readonly Color Dim = new(0.75f, 0.75f, 0.75f);
    private readonly PanelContainer _root;
    private readonly VBoxContainer _rows;
    private TurnEndModel? _builtFor;
    private bool _builtWithChances;

    public OutcomeTip(Control parent)
    {
        _root = new PanelContainer { TopLevel = true, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 100 };
        _root.AddThemeStyleboxOverride("panel", UiKit.PanelStyle(new Color(0.06f, 0.06f, 0.09f, 0.94f), radius: 6, margin: 10));
        _rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _rows.AddThemeConstantOverride("separation", 3);
        _root.AddChild(_rows);
        parent.AddChild(_root);
    }

    public void Hide() => _root.Visible = false;

    /// <summary>Below <paramref name="anchor"/>, or above it when there's no room, kept on screen. <paramref name="chances"/>: the Chances setting.</summary>
    public void Show(Control anchor, TurnEndModel forecast, bool chances)
    {
        if (!ReferenceEquals(_builtFor, forecast) || _builtWithChances != chances)
        {
            Build(forecast, chances);
            _builtFor = forecast;
            _builtWithChances = chances;
        }
        _root.Visible = true;
        _root.ResetSize();
        var rect = anchor.GetGlobalRect();
        var screen = anchor.GetViewportRect().Size;
        var size = _root.Size;
        var left = Mathf.Clamp(rect.Position.X, 4, Math.Max(4, screen.X - size.X - 4));
        var top = rect.End.Y + 8;
        if (top + size.Y > screen.Y - 4) top = Math.Max(4, rect.Position.Y - size.Y - 8);
        _root.GlobalPosition = new Vector2(left, top);
    }

    private void Build(TurnEndModel forecast, bool chances)
    {
        UiKit.Clear(_rows);
        var outcomes = forecast.Outcomes;
        if (outcomes.Count == 0) return;
        var likely = outcomes[0];
        if (outcomes.Count == 1)
        {
            Row(Describe(likely, from: null), "", "certain: no roll decides it", Colors.White);
            Mark(forecast);
            return;
        }
        Row(Describe(likely, from: null), chances ? likely.Chance.Text() : "", "most likely", Colors.White);
        // The others, from the kindest to the worst (deaths last), each against the most likely.
        var others = outcomes.Skip(1).OrderBy(outcome => outcome.Dies).ThenBy(outcome => outcome.Taken).ToList();
        foreach (var outcome in others.Take(MaxRows))
            Row(Describe(outcome, likely), chances ? outcome.Chance.Text() : "", TurnOutcomes.Differences(outcome.Branch, likely.Branch),
                outcome.Dies || outcome.Taken > likely.Taken ? DamageMathPanel.TakeColor : DamageMathPanel.SafeColor);
        if (others.Count > MaxRows)
        {
            var rest = others.Skip(MaxRows).Sum(outcome => outcome.Share);
            Row($"{others.Count - MaxRows} more", chances ? new SimChance(rest, rest, Seen: true).Text() : "", "", Dim);
        }
        Mark(forecast);
    }

    /// <summary>Something not modelled, or the search still going: the chances aren't the whole picture.</summary>
    private void Mark(TurnEndModel forecast)
    {
        if (forecast.ShownMark != null) Row("?", "", "some outcome has something not modelled, or isn't worked out yet", Dim);
    }

    /// <summary>"Lose 9 HP", "Lose 11 HP (+2)", "You die", "Fairy in a Bottle saves you: 23 HP".</summary>
    private static string Describe(TurnOutcome outcome, TurnOutcome? from)
    {
        if (outcome.Dies) return "You die";
        if (outcome.Revived) return $"{(outcome.FairiesUsed > 0 ? "Fairy in a Bottle" : "Lizard Tail")} saves you: {outcome.HpAfter} HP";
        var text = $"Lose {outcome.Taken} HP";
        if (from is { Dies: false, Revived: false } && outcome.Taken != from.Taken)
            text += $" ({(outcome.Taken > from.Taken ? "+" : "−")}{Math.Abs(outcome.Taken - from.Taken)})";
        return text;
    }

    private void Row(string outcome, string chance, string detail, Color color)
    {
        var row = UiKit.Row(separation: 12);
        row.MouseFilter = Control.MouseFilterEnum.Ignore;
        var name = UiKit.Label(outcome, FontSize, color);
        name.CustomMinimumSize = new Vector2(230, 0);
        row.AddChild(name);
        var odds = UiKit.Label(chance, FontSize, Colors.White);
        odds.CustomMinimumSize = new Vector2(64, 0);
        odds.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(odds);
        if (detail.Length > 0) row.AddChild(UiKit.Label(detail, FontSize, Dim));
        _rows.AddChild(row);
    }
}
