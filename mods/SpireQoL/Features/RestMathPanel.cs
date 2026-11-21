using Godot;
using SpireQoL.Cards;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Rest Preview's sum, drawn like Incoming Damage's (DamageMathPanel: same font, icons, rule and colours):
///
///   [heart]    30        HP now
///   [rest]   + 23        the Rest (or Mend) heal
///   [pillow] + 15        a line per relic that changes it (Regal Pillow, Stone Humidifier, Night Terrors)
///   [panto]  + 24        and per boss relic the boss fight starts with (Pantograph, Bath Robes)
///   ─────────────
///          = 77/77
///            9 over      the part of the rest heal that never reaches the result
/// </summary>
internal sealed class RestMathPanel
{
    internal static readonly Color OverColor = new(1f, 0.55f, 0.25f);
    private static readonly Color HpColor = new(0.95f, 0.92f, 0.85f);

    private readonly GridContainer _grid;
    private string _key = "";

    public RestMathPanel(Control parent)
    {
        _grid = new GridContainer { Columns = 2, MouseFilter = Control.MouseFilterEnum.Ignore };
        _grid.AddThemeConstantOverride("h_separation", 8);
        _grid.AddThemeConstantOverride("v_separation", 2);
        parent.AddChild(_grid);
    }

    /// <param name="lines">Each step's icon and the HP it changes, in order.</param>
    /// <param name="over">The rest heal that doesn't reach the result (0: no line).</param>
    public void Show(int hp, IReadOnlyList<(Texture2D? Icon, int Hp)> lines, int resultHp, int resultMax, int over)
    {
        var key = $"{hp}|{string.Join(",", lines.Select(l => $"{l.Icon?.GetInstanceId()}:{l.Hp}"))}|{resultHp}/{resultMax}|{over}";
        if (key == _key) return;
        _key = key;
        UiKit.Clear(_grid);

        _grid.AddChild(UiKit.Icon(GameIcons.Heart(), DamageMathPanel.IconSize));
        var now = DamageMathPanel.Number(HpColor);
        now.Text = $"{hp}";
        _grid.AddChild(now);
        foreach (var (icon, amount) in lines)
        {
            _grid.AddChild(UiKit.Icon(icon, DamageMathPanel.IconSize));
            var label = DamageMathPanel.Number(amount < 0 ? DamageMathPanel.TakeColor : DamageMathPanel.SafeColor);
            label.Text = amount < 0 ? $"− {-amount}" : $"+ {amount}";
            _grid.AddChild(label);
        }
        _grid.AddChild(DamageMathPanel.Rule());
        _grid.AddChild(DamageMathPanel.Rule());

        var otherLabel = UiKit.Label("=", DamageMathPanel.FontSize, Colors.White);
        otherLabel.HorizontalAlignment = HorizontalAlignment.Center;
        otherLabel.CustomMinimumSize = new Vector2(DamageMathPanel.IconSize, 0);
        _grid.AddChild(otherLabel);
        var result = DamageMathPanel.Number(HpColor);
        result.Text = $"{resultHp}/{resultMax}";
        _grid.AddChild(result);

        if (over > 0)
        {
            _grid.AddChild(new Control { CustomMinimumSize = new Vector2(DamageMathPanel.IconSize, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
            var label = DamageMathPanel.Number(OverColor);
            label.Text = $"{over} over";
            label.AddThemeFontSizeOverride("font_size", DamageMathPanel.FontSize - 4);
            _grid.AddChild(label);
        }
    }
}
