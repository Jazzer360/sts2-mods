using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Powers;
using SpireQoL.Cards;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// The stacked "math problem" Incoming Damage shows (yours, and a hovered teammate's):
///
///   [attack] 14
///   [defend] − 5
///   [osty]   − 3   (only while Osty is out)
///   ───────────
///          =  9      (what gets through your block)
///   [heart]  + 3     (only with HP loss no block stops: Regret, Beckon, Bad Luck, Demise)
///   [fairy]  31 HP   (only when Fairy in a Bottle or Lizard Tail keeps you alive: your HP after)
///
/// The skull (and the chance) sits beside the last number, when the enemy turn kills you. With nothing blockable coming,
/// only the heart line shows.
///
/// Above it, [eye] +1 turn when ending your turn now gives you another turn first (Pael's Eye, Extra Turn): the
/// enemies' turn shown comes after that one.
/// </summary>
internal sealed class DamageMathPanel
{
    private static readonly Color IncomingColor = new(1f, 0.6f, 0.5f);
    private static readonly Color BlockColor = new(0.55f, 0.8f, 1f);
    internal static readonly Color TakeColor = new(1f, 0.35f, 0.3f);
    internal static readonly Color SafeColor = new(0.5f, 0.95f, 0.6f);
    private static readonly Color OstyColor = new(0.75f, 0.85f, 0.7f);
    internal const int FontSize = 22;
    internal const float IconSize = 30;

    private readonly Label _incoming;
    private readonly Label _block;
    private readonly Label _result;
    private readonly TextureRect _skull;
    private readonly Label _deathChance;
    private readonly Label _progress;
    private readonly TextureRect _reviveIcon;
    private readonly Label _revive;
    private readonly TextureRect _attackIcon;
    private readonly TextureRect _ostyIcon;
    private readonly Label _osty;
    private readonly Texture2D?[] _attackTiers = new Texture2D?[5];
    private readonly TextureRect _extraTurnIcon;
    private readonly Label _extraTurn;
    private readonly HBoxContainer _resultRow;
    private readonly TextureRect _heartIcon;
    private readonly HBoxContainer _unblockableRow;
    private readonly Label _unblockable;
    /// <summary>The blockable math (attack, defend, Osty, the rule, the result): hidden when only unblockable HP loss is coming.</summary>
    private readonly List<Control> _math = new();

    public DamageMathPanel(Control parent)
    {
        var grid = new GridContainer { Columns = 2, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 2);

        for (var tier = 1; tier <= 5; tier++)
        {
            _attackTiers[tier - 1] = LoadIntentIcon($"attack/intent_attack_{tier}");
        }
        // An extra turn comes before the enemies act: its source's icon (the relic or power).
        _extraTurnIcon = UiKit.Icon(null, IconSize);
        _extraTurnIcon.Visible = false;
        grid.AddChild(_extraTurnIcon);
        _extraTurn = Number(SafeColor);
        _extraTurn.Text = "+1 turn";
        _extraTurn.Visible = false;
        grid.AddChild(_extraTurn);
        _attackIcon = UiKit.Icon(_attackTiers[0], IconSize);
        grid.AddChild(_attackIcon);
        _incoming = Number(IncomingColor);
        grid.AddChild(_incoming);

        var defend = UiKit.Icon(LoadIntentIcon("intent_defend"), IconSize);
        grid.AddChild(defend);
        _block = Number(BlockColor);
        grid.AddChild(_block);

        // Necrobinder: what Osty absorbs (Die For You), shown only while Osty is out.
        _ostyIcon = UiKit.Icon(GameIcons.Power<DieForYouPower>(), IconSize);
        grid.AddChild(_ostyIcon);
        _osty = Number(OstyColor);
        grid.AddChild(_osty);

        var rule1 = Rule();
        var rule2 = Rule();
        grid.AddChild(rule1);
        grid.AddChild(rule2);

        var eq = UiKit.Label("=", FontSize, Colors.White);
        eq.HorizontalAlignment = HorizontalAlignment.Center;
        eq.CustomMinimumSize = new Vector2(IconSize, 0);
        grid.AddChild(eq);
        // The result, with the skull just left of it when the enemy turn kills you.
        var resultRow = _resultRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        resultRow.AddThemeConstantOverride("separation", 4);
        // The chance it kills you, left of the skull, when a roll decides (decision #87).
        _deathChance = UiKit.Label("", (int)(FontSize * 0.75f), Colors.White);
        _deathChance.Visible = false;
        resultRow.AddChild(_deathChance);
        _skull = UiKit.Icon(GameIcons.Skull(), IconSize * 0.8f);
        _skull.Visible = false;
        resultRow.AddChild(_skull);
        _result = Number(TakeColor);
        resultRow.AddChild(_result);
        // Not every outcome explored yet (or ever): "?", the numbers may not be the whole picture.
        _progress = UiKit.Label("", (int)(FontSize * 0.75f), Colors.White);
        _progress.Visible = false;
        resultRow.AddChild(_progress);
        grid.AddChild(resultRow);
        _math.AddRange(new Control[] { _attackIcon, _incoming, defend, _block, rule1, rule2, eq, resultRow });

        // HP loss no block stops (Regret, Beckon, Bad Luck, Demise), under the result: added to it, never against your block.
        _heartIcon = UiKit.Icon(GameIcons.Heart(), IconSize);
        _heartIcon.Visible = false;
        grid.AddChild(_heartIcon);
        _unblockableRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Visible = false };
        _unblockableRow.AddThemeConstantOverride("separation", 4);
        _unblockable = Number(TakeColor);
        _unblockableRow.AddChild(_unblockable);
        grid.AddChild(_unblockableRow);

        // What keeps you alive (Fairy in a Bottle's or Lizard Tail's icon) and your HP after it heals you.
        _reviveIcon = UiKit.Icon(null, IconSize);
        _reviveIcon.Visible = false;
        grid.AddChild(_reviveIcon);
        _revive = Number(SafeColor);
        _revive.Visible = false;
        grid.AddChild(_revive);

        parent.AddChild(grid);
    }

    /// <param name="osty">What Osty takes and whether that kills him; null when Osty isn't out.</param>
    /// <param name="dies">The enemy turn kills you (the skull).</param>
    /// <param name="revive">What keeps you alive at 0 HP (its icon) and your HP after the enemy turn; null when nothing does.</param>
    /// <param name="extraTurn">Ending your turn now gives you another turn before the enemies act: its source's icon.</param>
    /// <param name="deathChance">The chance it kills you ("40%", "40–55%"), when a roll decides; null when certain or not shown.</param>
    /// <param name="progress">"?" while not every outcome is explored; null when complete.</param>
    /// <param name="unblockable">HP you lose that no block stops, shown on its own line under <paramref name="take"/> (what gets
    /// through your block); null when none is coming.</param>
    public void Show(int incoming, int block, (int Taken, bool Dies)? osty, int take, bool dies = false, (Texture2D? Icon, int HpAfter)? revive = null,
        Texture2D? extraTurn = null, string? deathChance = null, string? progress = null, int? unblockable = null)
    {
        var mathShown = unblockable == null || incoming > 0;
        foreach (var c in _math) c.Visible = mathShown;
        _heartIcon.Visible = unblockable != null;
        _unblockableRow.Visible = unblockable != null;
        _unblockable.Text = mathShown ? $"+ {unblockable}" : $"{unblockable}";
        // The skull, the chance and the ? go beside the last number shown.
        var last = unblockable != null ? _unblockableRow : _resultRow;
        if (_skull.GetParent() != last)
        {
            foreach (var n in new Control[] { _deathChance, _skull, _progress }) n.GetParent()?.RemoveChild(n);
            last.AddChild(_deathChance);
            last.AddChild(_skull);
            last.AddChild(_progress);
            last.MoveChild(_deathChance, 0);
            last.MoveChild(_skull, 1);
        }
        _deathChance.Visible = dies && deathChance != null;
        _deathChance.Text = deathChance ?? "";
        _progress.Visible = progress != null;
        _progress.Text = progress ?? "";
        _extraTurnIcon.Visible = extraTurn != null;
        _extraTurn.Visible = extraTurn != null;
        _extraTurnIcon.Texture = extraTurn;
        _skull.Visible = dies;
        _reviveIcon.Visible = revive != null && !dies;
        _revive.Visible = revive != null && !dies;
        if (revive is { } r)
        {
            _reviveIcon.Texture = r.Icon;
            _revive.Text = $"{r.HpAfter} HP";
        }
        _attackIcon.Texture = _attackTiers[AttackTier(incoming) - 1] ?? _attackTiers.FirstOrDefault(t => t != null);
        _incoming.Text = $"{incoming}";
        _block.Text = $"− {block}";
        _ostyIcon.Visible = osty != null && mathShown;
        _osty.Visible = osty != null && mathShown;
        if (osty is { } o)
        {
            _osty.Text = $"− {o.Taken}";
            // Red when the enemy turn kills Osty (its overkill already counts toward yours).
            _osty.AddThemeColorOverride("font_color", o.Dies ? TakeColor : OstyColor);
        }
        _result.Text = $"{take}";
        _result.AddThemeColorOverride("font_color", take > 0 ? TakeColor : SafeColor);
    }

    internal static Label Number(Color color)
    {
        var l = UiKit.Label("", FontSize, color);
        l.HorizontalAlignment = HorizontalAlignment.Right;
        l.CustomMinimumSize = new Vector2(48, 0);
        l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return l;
    }

    internal static ColorRect Rule() => new()
    {
        Color = new Color(1, 1, 1, 0.6f),
        CustomMinimumSize = new Vector2(0, 2),
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    /// <summary>Same tiers the game uses for an enemy's attack intent icon (AttackIntent.GetTexture).</summary>
    private static int AttackTier(int damage) => damage switch
    {
        < 5 => 1,
        < 10 => 2,
        < 20 => 3,
        < 40 => 4,
        _ => 5,
    };

    /// <summary>
    /// The game's own intent icons (atlases/intent_atlas.sprites/*.tres). Attack uses the tiered
    /// attack/intent_attack_N icons; the plain intent_attack.tres isn't shipped (shows a placeholder).
    /// </summary>
    private static Texture2D? LoadIntentIcon(string name)
    {
        try
        {
            return ResourceLoader.Load<Texture2D>(ImageHelper.GetImagePath($"atlases/intent_atlas.sprites/{name}.tres"));
        }
        catch
        {
            return null;
        }
    }
}
