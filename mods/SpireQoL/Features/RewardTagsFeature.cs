using HarmonyLib;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Reward Tags (docs/FEATURES.md "Reward Tags", decision #185): outside fights, a tag on each card you're offered (a card reward, an
/// event's choice of cards, the shop) for each of your relics that taking it sets off or that makes it stronger in fights, and the
/// card reward check. Its work is RunReminders' (shared with the other reminders): OfferedCardTabs and WatchReward.
/// </summary>
public sealed class RewardTagsFeature : IFeature, IFrameFeature
{
    public const string FeatureId = "reward-tags";

    private readonly RunReminders _reminders;

    public RewardTagsFeature(RunReminders reminders) => _reminders = reminders;

    public string Id => FeatureId;
    public string DisplayName => "Reward Tags";
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
        // Each job its own part of the frame's times and memory (decision #177: the dev dashboard's steady table names the one that allocates).
        using (host.Part("reward-tags:watch")) _reminders.WatchReward();
        using (host.Part("reward-tags:offered")) _reminders.OfferedCardTabs();
    }
}
