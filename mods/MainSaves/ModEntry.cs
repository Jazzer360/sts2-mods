using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Saves;
using Sts2Mods.Core;
using Sts2Mods.Core.Features;

namespace MainSaves;

/// <summary>
/// Entry point. The game calls Initialize while it loads mods (ModManager.Initialize), before it decides
/// which save folder to use (OneTimeInitialization.ExecuteVeryEarly sets UserDataPathProvider.IsRunningModded
/// after the mods load) and before any save is read, so the redirect is in place for every save path.
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string Id = "MainSaves";

    public static ModLog Log { get; } = new(Id);
    public static FeatureHost Features { get; } = new(Id, Log);

    public static void Initialize()
    {
        Log.Info($"loading v{typeof(ModEntry).Assembly.GetName().Version}");
        Features.Add(new MainSavesFeature());
        Features.ApplyAll();
    }
}

/// <summary>
/// The game keeps modded saves apart: while any mod is loaded, UserDataPathProvider.GetAccountDir returns
/// "modded", so profile.save and every profile's saves (progress, the current run, prefs, run history) live
/// under modded/ instead of the account folder. This answers "" for the current mode instead, so the normal
/// profiles are used. Explicit requests for one mode (forceModState: the game's first-time copy of unmodded
/// saves into modded/) are left alone, and the game still knows it's running modded (IsRunningModded is
/// untouched: run history marks modded runs, and the game's own crash reporting stays off).
/// </summary>
internal sealed class MainSavesFeature : IFeature
{
    public string Id => "main-saves";
    public string DisplayName => "Main Saves";
    public bool EnabledByDefault => true;

    public void Apply(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(UserDataPathProvider), nameof(UserDataPathProvider.GetAccountDir), new[] { typeof(bool?) })
                     ?? throw new MissingMethodException(nameof(UserDataPathProvider), nameof(UserDataPathProvider.GetAccountDir));
        harmony.Patch(target, prefix: new HarmonyMethod(typeof(MainSavesFeature), nameof(Prefix)));
        ModEntry.Log.Info("using your normal profiles (not the modded ones)");
    }

    private static bool Prefix(bool? forceModState, ref string __result)
    {
        if (forceModState != null) return true; // the game asked for a specific mode: its own answer
        __result = "";
        return false;
    }
}
