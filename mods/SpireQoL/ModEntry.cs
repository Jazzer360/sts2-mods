using MegaCrit.Sts2.Core.Modding;
using SpireQoL.Features;
using Sts2Mods.Core;
using Sts2Mods.Core.Features;

namespace SpireQoL;

/// <summary>
/// Entry point. The game calls the static method named in [ModInitializer] once at load.
/// (Without this attribute the game would just call Harmony.PatchAll on the assembly,
/// which would apply every patch with no per-feature isolation.)
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string Id = "SpireQoL";

    public static ModLog Log { get; } = new(Id);
    public static FeatureHost Features { get; } = new(Id, Log);

    public static void Initialize()
    {
        Log.Info($"loading v{typeof(ModEntry).Assembly.GetName().Version}");

        Features
            .Add(new KeywordBadgesFeature())
            .Add(new RelicRemindersFeature())
            .Add(new IntentForecastFeature())
            .Add(new CombatMathFeature());

        // TODO: read per-feature toggles from mod settings once core has a settings layer.
        Features.ApplyAll();
    }
}
