using Godot;
using MegaCrit.Sts2.Core.Modding;
using SpireQoL.Cards;
using SpireQoL.Features;
using Sts2Mods.Core;
using Sts2Mods.Core.Features;
using Sts2Mods.Core.Game;
using Sts2Mods.Core.Settings;
using Sts2Mods.Core.Ui;

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

        var settingsFile = new JsonSettingsFile<QolSettings>(JsonSettingsFile<QolSettings>.DefaultPathFor(typeof(ModEntry), Id), Log);
        var settings = settingsFile.Load();
        MigrateLayout(settings);
        MigrateTabs(settings);

        var host = new OverlayHost(Id, Log)
        {
            UnlockLayoutKey = ParseKey(settings.UnlockLayoutKey, Key.F8),
            HideAllKey = ParseKey(settings.HideAllKey, Key.F9),
        };
        host.LayoutChanged += settingsFile.Save;
        host.SuppressWhen = Sts2State.IsScreenOpenOverCombat;
        host.WidgetUnderlay = Sts2State.WidgetUnderlay; // widgets draw under the cards in hand

        var ctx = new QolContext(settingsFile, host);
        Sts2State.Init();

        // Order matters for card decorations: later features add badges after earlier ones.
        IFeature[] features =
        {
            new CardIndicatorsFeature(ctx),
            new RelicCountersFeature(ctx),
            new ProcIndicatorsFeature(ctx),
            new IncomingDamageFeature(ctx),
            new ShuffleWarningFeature(ctx),
            new IntentForecastFeature(),
            new Diagnostics.ForecastAudit(),
            new Diagnostics.ProcAudit(ctx),
        };
        foreach (var f in features) Features.Add(f);

        Features.ApplyAll(f => settings.Features.TryGetValue(f.Id, out var on) ? on : f.EnabledByDefault);

        // Write every feature id into settings so players can see what can be toggled.
        foreach (var f in features)
        {
            settings.Features.TryAdd(f.Id, f.EnabledByDefault);
        }
        settingsFile.Save();

        foreach (var f in features)
        {
            if (f is IFrameFeature frame && Features.States[f.Id] == FeatureState.Active)
            {
                host.AddFeature(f.Id, frame);
            }
        }

        var renderer = new CardOverlayRenderer(host, ctx.Decor);
        host.AddLateFrame(renderer.Render);

        // New tab kinds are written into settings as they first appear (so the file lists every
        // tab that can be turned off). Save at most every few seconds.
        ulong lastSave = 0;
        host.AddLateFrame(() =>
        {
            if (!settings.TabsDirty) return;
            var now = Time.GetTicksMsec();
            if (now - lastSave < 3000) return;
            lastSave = now;
            settings.TabsDirty = false;
            settingsFile.Save();
        });
        host.Start();
    }

    /// <summary>Resets saved positions whose defaults changed, so players get the new defaults.</summary>
    private static void MigrateLayout(QolSettings settings)
    {
        if (settings.LayoutVersion < 2)
        {
            // 0.3.0: Incoming Damage moved under the player's HP; Shuffle Warning lost its widget.
            settings.Widgets.Remove("incoming-damage");
            settings.Widgets.Remove("shuffle-warning");
        }
        if (settings.LayoutVersion < 3)
        {
            // 0.4.0: Incoming Damage moved to the left of the block indicator.
            settings.Widgets.Remove("incoming-damage");
        }
        if (settings.LayoutVersion < 4)
        {
            // 0.6.0: the counter tray attaches to the right of the HP bar; Ethereal default 0.7 → 0.8.
            settings.Widgets.Remove("relic-counters");
            if (Math.Abs(settings.EtherealOpacity - 0.7f) < 0.001f) settings.EtherealOpacity = 0.8f;
        }
        settings.LayoutVersion = QolSettings.CurrentLayoutVersion;
    }

    /// <summary>0.19.0: the one-off tab switches became entries in Tabs.</summary>
    private static void MigrateTabs(QolSettings settings)
    {
        if (settings.ShowDoublePlay == false) settings.Tabs["double-play:*"] = false;
        if (settings.DimBlockedCards == false) settings.Tabs["blocked"] = false;
        settings.ShowDoublePlay = null;
        settings.DimBlockedCards = null;
        settings.ShowInnateEternalBadges = null; // Innate/Eternal tabs were removed
    }

    private static Key ParseKey(string? name, Key fallback)
        => Enum.TryParse<Key>(name, ignoreCase: true, out var key) ? key : fallback;
}
