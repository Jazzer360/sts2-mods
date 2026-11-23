using HarmonyLib;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Map Reminders (docs/FEATURES.md "Map Reminders", decisions #65, #185): while the map is open, beside each row of its legend, the icons
/// of your relics and quest cards that make that kind of room unusually good or bad. Its work is RunReminders' (shared with the other
/// reminders): MapLegend, which works the rows out again only when what they read changes.
/// </summary>
public sealed class MapRemindersFeature : IFeature, IFrameFeature
{
    public const string FeatureId = "map-reminders";

    private readonly RunReminders _reminders;
    private int _frame;

    public MapRemindersFeature(RunReminders reminders) => _reminders = reminders;

    public string Id => FeatureId;
    public string DisplayName => "Map Reminders";
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
        // The legend changes only when the map does: every few frames is plenty.
        if (++_frame % 6 == 0) _reminders.MapLegend();
    }
}
