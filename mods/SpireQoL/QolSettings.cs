using Sts2Mods.Core.Ui;

namespace SpireQoL;

/// <summary>
/// Everything the player can configure. Saved as JSON in mods/SpireQoL/SpireQoL.settings.cfg.
/// Hand-editable; unknown or missing keys fall back to defaults.
/// </summary>
public sealed class QolSettings
{
    /// <summary>Feature id → enabled. Missing ids use the feature's default.</summary>
    public Dictionary<string, bool> Features { get; set; } = new();

    /// <summary>Relic class name (e.g. "PenNib") → shown. Missing = shown.</summary>
    public Dictionary<string, bool> Relics { get; set; } = new();

    /// <summary>Widget id → placement. Filled with defaults on first run.</summary>
    public Dictionary<string, WidgetLayout> Widgets { get; set; } = new();

    public const int CurrentLayoutVersion = 4;

    /// <summary>Bumped when default widget positions change; older saved positions get reset.</summary>
    public int LayoutVersion { get; set; }

    public string UnlockLayoutKey { get; set; } = "F8";
    public string HideAllKey { get; set; } = "F9";

    // Card Indicators
    public float EtherealOpacity { get; set; } = 0.8f;
    public bool ShowInnateEternalBadges { get; set; } = false;
    public bool DimBlockedCards { get; set; } = true;
    public bool ShowDoublePlay { get; set; } = true;

    // Relic Counters
    public bool PulseOneStepEarly { get; set; } = false;
    public float FlashHz { get; set; } = 2.5f;


    public bool RelicEnabled(string relicTypeName) => !Relics.TryGetValue(relicTypeName, out var on) || on;

    public WidgetLayout Widget(string id, WidgetLayout fallback)
    {
        if (!Widgets.TryGetValue(id, out var layout))
        {
            layout = fallback.Clone();
            Widgets[id] = layout;
        }
        return layout;
    }
}
