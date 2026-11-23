using HarmonyLib;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Event Reminders (docs/FEATURES.md "Event Reminders", decision #185): beside each event option, the icons of your relics that change
/// what it does, with the chance one of a card relic's cards comes up, the Inspect button for an option with random cards, and the
/// event option check (its observe-only patch on EventOption.Chosen). Its work is RunReminders' (shared with the other reminders).
/// </summary>
public sealed class EventRemindersFeature : IFeature, IFrameFeature
{
    public const string FeatureId = "event-reminders";

    private readonly RunReminders _reminders;
    private int _frame;

    public EventRemindersFeature(RunReminders reminders) => _reminders = reminders;

    public string Id => FeatureId;
    public string DisplayName => "Event Reminders";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony) => RunReminders.PatchEventChosen(harmony);

    public void Attach(OverlayHost host) => _reminders.AttachInspect();

    public void OnFrame(OverlayHost host)
    {
        using (host.Part("event-reminders:watch")) RunReminders.TickWindow();
        // Event options change only when the page does: every few frames is plenty.
        if (++_frame % 6 == 0)
            using (host.Part("event-reminders:options")) _reminders.EventOptions();
        using (host.Part("event-reminders:inspect")) _reminders.InspectFrame();
    }
}
