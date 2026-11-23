using HarmonyLib;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Ui;

namespace SpireQoL.Features;

/// <summary>
/// Shop Reminders (docs/FEATURES.md "Shop Reminders", decision #185): while the shop is open, beside each price, the icons of your
/// relics that buying it sets off, the price after Lucky Fysh's gold, and the purchase check. Its work is RunReminders' (shared with
/// the other reminders): ShopSlots and TickShopWatch.
/// </summary>
public sealed class ShopRemindersFeature : IFeature, IFrameFeature
{
    public const string FeatureId = "shop-reminders";

    private readonly RunReminders _reminders;
    private int _frame;

    public ShopRemindersFeature(RunReminders reminders) => _reminders = reminders;

    public string Id => FeatureId;
    public string DisplayName => "Shop Reminders";
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
        // Shop items change only when the page does: every few frames is plenty.
        if (++_frame % 6 == 0)
            using (host.Part("shop-reminders:prices")) _reminders.ShopSlots();
        using (host.Part("shop-reminders:watch")) _reminders.TickShopWatch();
    }
}
