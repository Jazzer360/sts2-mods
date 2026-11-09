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

    /// <summary>
    /// Send crashes and audit mismatches (with the game state at the time; nothing personal) to the
    /// mod's Sentry project, where they become GitHub issues. See README "Error reporting".
    /// </summary>
    public bool ReportErrors { get; set; } = true;

    public string UnlockLayoutKey { get; set; } = "F8";
    public string HideAllKey { get; set; } = "F9";

    /// <summary>
    /// On-card tab kind → shown. Keys are tab keys ("retain", "sly", "blocked", "shuffle",
    /// "relic:PenNib", "proc:FeelNoPainPower", "double-play:BurstPower"...) or a group switch
    /// ("relic:*", "proc:*", "double-play:*"). A tab shows only if both its own key and its group
    /// are on, so a group set to false hides the whole group. Missing = shown.
    /// Keys are added here (as shown) the first time a tab of that kind appears, so the file lists
    /// everything that can be turned off.
    /// </summary>
    public Dictionary<string, bool> Tabs { get; set; } = new();

    /// <summary>Show the amount on block / damage / summon proc tabs (off = icon only).</summary>
    public bool ProcNumbers { get; set; } = true;

    /// <summary>Count procs one step removed (a card that exhausts or discards other cards, or draws into a shuffle).</summary>
    public bool ProcIndirect { get; set; } = true;

    // Card Indicators
    public float EtherealOpacity { get; set; } = 0.8f;

    // Replaced by Tabs in 0.19.0; read once from older files, then dropped (see ModEntry.Migrate).
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? ShowDoublePlay { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? DimBlockedCards { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? ShowInnateEternalBadges { get; set; }

    // Relic Counters
    /// <summary>Also show relics that count turns on their own (Horn Cleat, Candelabra, Captain's Wheel, Happy Flower...).</summary>
    public bool ShowTurnTimers { get; set; } = false;
    public bool PulseOneStepEarly { get; set; } = false;
    public float FlashHz { get; set; } = 2.5f;


    public bool RelicEnabled(string relicTypeName) => !Relics.TryGetValue(relicTypeName, out var on) || on;

    /// <summary>Set when a new tab key or group was added to <see cref="Tabs"/> and should be saved.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool TabsDirty { get; set; }

    public bool TabEnabled(string key)
    {
        var colon = key.IndexOf(':');
        var group = colon > 0 ? key[..colon] + ":*" : null;
        if (group != null && !Tabs.ContainsKey(group)) { Tabs[group] = true; TabsDirty = true; }
        if (!Tabs.TryGetValue(key, out var on)) { Tabs[key] = true; TabsDirty = true; on = true; }
        if (!on) return false;
        return group == null || Tabs[group];
    }


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
